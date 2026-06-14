# Proposal: Stop the hourly scheduler restarts caused by internal JWT rotation

## Summary

The middleware schedulers (daily check, recert, variance analysis, report,
external request, auto-discover, import-app/ip-data, update-flows,
update-rule-owner-mapping, compliance, …) appear to restart/reschedule
themselves roughly once per hour. This is **not** caused by configuration
changes. It is a side effect of the internal middleware-server JWT being
rotated on a fixed **60-minute** lifetime. Each rotation reconnects all GraphQL
subscriptions, and each reconnect re-emits the current config value, which makes
every scheduler tear down and re-create its Quartz job (or restart its timers).

This document explains the mechanism and recommends a fix.

## Root cause analysis

The restart is the end of a four-step chain:

1. **Fixed 60-minute internal token lifetime.**
   The middleware-server service token is hardcoded to 60 minutes in
   [`TokenLifetimeProvider`](../../../roles/middleware/files/FWO.Middleware.Server/Services/TokenLifetimeProvider.cs):

   ```csharp
   private static readonly TimeSpan kInternalServiceTokenLifetime = TimeSpan.FromMinutes(60);
   ```

2. **Background rotation ~2 min before expiry.**
   [`InternalApiTokenRefreshService`](../../../roles/middleware/files/FWO.Middleware.Server/Services/InternalApiTokenRefreshService.cs)
   polls every `RefreshCheckInterval` (1 min) and rotates once the token is
   within `RefreshLeadTime` (2 min) of expiry — i.e. **about every 58 minutes**
   (see [`InternalApiTokenServiceOptions`](../../../roles/middleware/files/FWO.Middleware.Server/Services/InternalApiTokenServiceOptions.cs)).
   On rotation it calls `apiConnection.ReconnectSubscriptionsAsync(...)`
   ([`InternalApiTokenService.EnsureFreshTokenAsync`](../../../roles/middleware/files/FWO.Middleware.Server/Services/InternalApiTokenService.cs)).

3. **Reconnect re-creates every subscription.**
   [`GraphQlApiConnection.ReconnectSubscriptionsAsync`](../../../roles/lib/files/FWO.Api.Client/GraphQlApiConnection.cs)
   disposes the old subscription client and re-creates **all** active
   subscriptions on a fresh client. A newly created GraphQL subscription
   immediately re-emits the current value (initial push).

4. **Re-emit is treated as a config change → reschedule.**
   Each scheduler's subscription handler reacts to that initial push as if the
   configuration had changed. In
   [`QuartzSchedulerServiceBase.HandleGlobalConfigChangeAsync`](../../../roles/middleware/files/FWO.Middleware.Server/Services/QuartzSchedulerServiceBase.cs)
   (and the per-service variants such as
   [`DailyCheckSchedulerService`](../../../roles/middleware/files/FWO.Middleware.Server/Services/DailyCheckSchedulerService.cs))
   this runs `ScheduleJob()`, which deletes the existing Quartz job and
   re-creates it, logging `Removed existing job` / `Job rescheduled due to
   config change`. The legacy timer-based
   [`SchedulerBase`](../../../roles/middleware/files/FWO.Middleware.Server/SchedulerBase.cs)
   restarts its `ScheduleTimer`/`RecurringTimer` the same way.

### Why this matters

- The restarts are **spurious** — no configuration actually changed.
- They happen on **any** subscription reconnect, so the same symptom appears on
  transient network blips, not only on token rotation. Token rotation just
  makes it reliably hourly.
- Restarting a Quartz job recomputes its next start time. For schedulers that
  run "every N hours/days from a start time," an hourly delete/recreate can keep
  pushing the next fire time forward and, in the worst case, **delay or skip
  runs**.

## Recommended fix (Option B): make the config handlers idempotent

Treat the subscription emission as "here is the current desired
configuration," not "the configuration changed." Only reschedule when the
values this scheduler actually depends on differ from what is already applied.

Concretely:

- In `QuartzSchedulerServiceBase`, after `globalConfig.SubscriptionUpdateHandler(...)`,
  compute a small fingerprint of the values that affect scheduling
  (`SleepTime`, `StartAt`, `Interval`, `IsActive`) and compare it to the last
  applied fingerprint. If unchanged, log at debug level and **return without
  touching Quartz**.
- Apply the same guard to the per-service `OnGlobalConfigChange` handlers that
  do not yet derive from the base (e.g. `DailyCheckSchedulerService`,
  `RecertCheck`, and other `SchedulerBase` subclasses): cache the last applied
  scheduling inputs and skip the timer/job rebuild when they match.

Sketch (base class):

```csharp
private string? lastAppliedScheduleKey;

private async Task HandleGlobalConfigChangeAsync(List<ConfigItem> config)
{
    globalConfig.SubscriptionUpdateHandler([.. config]);

    string scheduleKey = $"{IsActive}|{SleepTime}|{StartAt:o}|{Interval}";
    if (scheduleKey == lastAppliedScheduleKey)
    {
        Log.WriteDebug(options.SchedulerName, "Config emission with unchanged schedule - skipping reschedule.");
        return;
    }

    lastAppliedScheduleKey = scheduleKey;
    await ScheduleJob();
    Log.WriteInfo(options.SchedulerName, "Job rescheduled due to config change");
}
```

### Why B over the alternatives

- **A — raise/expose the 60-minute lifetime.** Lowers the *frequency* of the
  restarts but does not remove them, leaves the network-blip case unfixed, and
  trades off against keeping service tokens short-lived. Reasonable as a
  complementary tweak, not as the fix.
- **Suppress the initial emit on reconnect.** Fragile: it requires the
  connection layer to distinguish "first push after (re)subscribe" from a real
  change, and it would also swallow genuine changes that happened while
  disconnected.
- **B — idempotent reschedule.** Fixes the actual defect (reacting to
  no-op emissions), is local to the middleware scheduler layer, and is robust
  against every reconnect cause. Recommended.

Optionally combine B with a modest, configurable increase of
`kInternalServiceTokenLifetime` to also reduce reconnect churn, but B alone
resolves the reported symptom.

## Scope of change (when implemented)

- `roles/middleware/files/FWO.Middleware.Server/Services/QuartzSchedulerServiceBase.cs`
  — add the change-detection guard (covers most schedulers).
- Per-service handlers not on the base — `DailyCheckSchedulerService.cs` and the
  `SchedulerBase` subclasses (`RecertCheck`, `RuleExpiryCheck`,
  `OwnerActiveRuleCheck`, `ExternalRequestHandler`, …) — apply the same guard.
- Unit tests under `roles/tests-unit/files/FWO.Test/` asserting that a repeated
  emission with identical scheduling inputs does **not** delete/recreate the
  Quartz job, while a changed input does.

## Validation plan

- `dotnet build --configuration Debug roles/FWO.sln`
- `dotnet test roles/tests-unit/files/FWO.Test/FWO.Test.csproj`
- Manual: run the middleware for >1 hour and confirm the logs no longer show
  `Job rescheduled due to config change` / `Removed existing job` at the token
  rotation interval, while a real config change still reschedules.

## How to verify the diagnosis in a running system

In the middleware log, the restarts line up with the rotation. Look for the
rotation audit from `InternalApiTokenService` (`Rotated internal middleware
JWT.`) and the reconnect line `Reconnecting N API subscriptions after JWT
refresh.`, each immediately followed by every scheduler logging `Job rescheduled
due to config change`. The cadence will be ~58 minutes — the 60-minute lifetime
minus the 2-minute refresh lead time.
