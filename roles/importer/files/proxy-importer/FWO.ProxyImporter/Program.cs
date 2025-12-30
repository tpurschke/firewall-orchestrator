using FWO.ProxyImporter.Importers;
using FWO.ProxyImporter.Models;
using FWO.ProxyImporter.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ProxyImporterOptions>(builder.Configuration.GetSection("ProxyImporter"));
builder.Services.Configure<SkyhighOptions>(builder.Configuration.GetSection("Skyhigh"));
builder.Services.Configure<F5BigIpOptions>(builder.Configuration.GetSection("F5BigIp"));

builder.Services.AddSingleton<IProxyRuleStore>(sp =>
{
    ProxyImporterOptions options = sp.GetRequiredService<IOptions<ProxyImporterOptions>>().Value;
    return new PostgresProxyRuleStore(options);
});

builder.Services.AddHttpClient<SkyhighProxyImporter>();
builder.Services.AddHttpClient<F5BigIpImporter>();
builder.Services.AddHostedService<FWO.ProxyImporter.Services.GenericGatewayImportScheduler>();

var app = builder.Build();

app.MapGet("/api/proxy/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/proxy/rules", (string? managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified, IProxyRuleStore store) =>
{
    List<int> mgmtIds = ParseIds(managementIds);
    List<ProxyRuleDocument> rules = store.GetRules(mgmtIds, ownerId, dueWithinDays, includeRecertified);
    return Results.Ok(rules);
});

app.MapGet("/api/proxy/recert/overview", (string? managementIds, int? dueWithinDays, IProxyRuleStore store) =>
{
    List<int> mgmtIds = ParseIds(managementIds);
    List<ProxyRecertOwnerOverview> overview = store.GetRecertOverview(mgmtIds, dueWithinDays);
    return Results.Ok(overview);
});

app.MapPost("/api/proxy/recertify", (ProxyRecertificationRequest request, IProxyRuleStore store, IOptions<ProxyImporterOptions> options) =>
{
    int updated = store.RecertifyRules(request, options.Value.DefaultRecertificationDays);
    return Results.Ok(new { updated });
});

app.MapPost("/api/proxy/import/skyhigh", async (int managementId, string? managementName, SkyhighProxyImporter importer, IProxyRuleStore store, CancellationToken ct) =>
{
    List<ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(managementId, ct);
    if (!string.IsNullOrWhiteSpace(managementName))
    {
        foreach (var rule in rules)
        {
            rule.ManagementName = managementName;
        }
    }
    store.UpsertRules(rules);
    return Results.Ok(new { imported = rules.Count });
});

app.MapPost("/api/generic/import/f5", async (int managementId, string? managementName, F5BigIpImporter importer, IProxyRuleStore store, CancellationToken ct) =>
{
    List<ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(managementId, ct);
    if (!string.IsNullOrWhiteSpace(managementName))
    {
        foreach (var rule in rules)
        {
            rule.ManagementName = managementName;
        }
    }
    store.UpsertRules(rules);
    return Results.Ok(new { imported = rules.Count });
});

app.Run();

static List<int> ParseIds(string? ids)
{
    if (string.IsNullOrWhiteSpace(ids))
    {
        return [];
    }
    List<int> parsed = [];
    foreach (string entry in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (int.TryParse(entry, out int value))
        {
            parsed.Add(value);
        }
    }
    return parsed;
}
