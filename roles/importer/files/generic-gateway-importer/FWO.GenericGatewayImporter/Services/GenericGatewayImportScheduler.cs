using System.Net;
using System.Text.RegularExpressions;
using FWO.GenericGatewayImporter.Importers;
using FWO.GenericGatewayImporter.Models;
using FWO.GenericGatewayImporter.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FWO.GenericGatewayImporter.Services
{
    public class GenericGatewayImportScheduler : BackgroundService
    {
        private const int ProxyManagementTypeId = 30;
        private const int DefaultSleepSeconds = 40;

        private readonly IServiceProvider serviceProvider;
        private readonly GenericGatewayImporterOptions proxyOptions;
        private readonly SkyhighOptions skyhighOptions;
        private readonly F5BigIpOptions f5Options;
        private readonly ILogger<GenericGatewayImportScheduler> logger;
        private readonly string hostname = Dns.GetHostName();

        public GenericGatewayImportScheduler(
            IServiceProvider serviceProvider,
            IOptions<GenericGatewayImporterOptions> proxyOptions,
            IOptions<SkyhighOptions> skyhighOptions,
            IOptions<F5BigIpOptions> f5Options,
            ILogger<GenericGatewayImportScheduler> logger)
        {
            this.serviceProvider = serviceProvider;
            this.proxyOptions = proxyOptions.Value;
            this.skyhighOptions = skyhighOptions.Value;
            this.f5Options = f5Options.Value;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(proxyOptions.ConnectionString))
            {
                logger.LogWarning("Generic gateway scheduler disabled: database connection string is missing.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                int sleepSeconds = await GetImportSleepTimeAsync(stoppingToken);
                List<ManagementInfo> managements = await GetManagementsAsync(stoppingToken);

                await ImportSkyhighAsync(managements, stoppingToken);
                await ImportF5Async(managements, stoppingToken);

                logger.LogInformation("Generic gateway importer sleeping for {SleepSeconds} seconds.", sleepSeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, sleepSeconds)), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task<int> GetImportSleepTimeAsync(CancellationToken ct)
        {
            try
            {
                await using var connection = new NpgsqlConnection(proxyOptions.ConnectionString);
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand(
                    "SELECT config_value FROM public.config WHERE config_key = 'importSleepTime' AND config_user = 0",
                    connection);
                object? result = await command.ExecuteScalarAsync(ct);
                if (result != null && int.TryParse(result.ToString(), out int seconds))
                {
                    return seconds > 0 ? seconds : DefaultSleepSeconds;
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to read import sleep time, using default.");
            }
            return DefaultSleepSeconds;
        }

        private async Task<List<ManagementInfo>> GetManagementsAsync(CancellationToken ct)
        {
            List<ManagementInfo> result = [];
            try
            {
                await using var connection = new NpgsqlConnection(proxyOptions.ConnectionString);
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand(
                    "SELECT mgm_id, mgm_name, importer_hostname, do_not_import FROM public.management WHERE dev_typ_id = @type_id",
                    connection);
                command.Parameters.AddWithValue("type_id", ProxyManagementTypeId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new ManagementInfo(
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? false : reader.GetBoolean(3)));
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to load generic gateway managements.");
            }
            return result;
        }

        private async Task ImportSkyhighAsync(List<ManagementInfo> managements, CancellationToken ct)
        {
            if (!HasImportSource(skyhighOptions))
            {
                return;
            }

            List<ManagementInfo> targets = FilterManagements(managements, skyhighOptions.ManagementIds, skyhighOptions.ManagementNamePattern);
            if (targets.Count == 0)
            {
                logger.LogInformation("Skyhigh import skipped: no matching managements configured.");
                return;
            }

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            SkyhighGenericGatewayImporter importer = scope.ServiceProvider.GetRequiredService<SkyhighGenericGatewayImporter>();
            IProxyRuleStore store = scope.ServiceProvider.GetRequiredService<IProxyRuleStore>();

            foreach (ManagementInfo management in targets)
            {
                if (!ShouldRunImport(management))
                {
                    continue;
                }

                try
                {
                    List<ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(management.Id, ct);
                    foreach (var rule in rules)
                    {
                        rule.ManagementName = management.Name;
                    }
                    store.UpsertRules(rules);
                    logger.LogInformation("Skyhigh import done for management {ManagementId} ({ManagementName}) with {Count} rules.",
                        management.Id, management.Name, rules.Count);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Skyhigh import failed for management {ManagementId} ({ManagementName}).",
                        management.Id, management.Name);
                }
            }
        }

        private async Task ImportF5Async(List<ManagementInfo> managements, CancellationToken ct)
        {
            if (!HasImportSource(f5Options))
            {
                return;
            }

            List<ManagementInfo> targets = FilterManagements(managements, f5Options.ManagementIds, f5Options.ManagementNamePattern);
            if (targets.Count == 0)
            {
                logger.LogInformation("F5 Big-IP import skipped: no matching managements configured.");
                return;
            }

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            F5BigIpImporter importer = scope.ServiceProvider.GetRequiredService<F5BigIpImporter>();
            IProxyRuleStore store = scope.ServiceProvider.GetRequiredService<IProxyRuleStore>();

            foreach (ManagementInfo management in targets)
            {
                if (!ShouldRunImport(management))
                {
                    continue;
                }

                try
                {
                    List<ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(management.Id, ct);
                    foreach (var rule in rules)
                    {
                        rule.ManagementName = management.Name;
                    }
                    store.UpsertRules(rules);
                    logger.LogInformation("F5 Big-IP import done for management {ManagementId} ({ManagementName}) with {Count} rules.",
                        management.Id, management.Name, rules.Count);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "F5 Big-IP import failed for management {ManagementId} ({ManagementName}).",
                        management.Id, management.Name);
                }
            }
        }

        private bool HasImportSource(SkyhighOptions options)
        {
            return !string.IsNullOrWhiteSpace(options.RulesJsonPath) || !string.IsNullOrWhiteSpace(options.BaseUrl);
        }

        private bool HasImportSource(F5BigIpOptions options)
        {
            return !string.IsNullOrWhiteSpace(options.RulesJsonPath) || !string.IsNullOrWhiteSpace(options.BaseUrl);
        }

        private List<ManagementInfo> FilterManagements(List<ManagementInfo> managements, List<int> managementIds, string namePattern)
        {
            IEnumerable<ManagementInfo> filtered = managements;
            if (managementIds.Count > 0)
            {
                filtered = filtered.Where(m => managementIds.Contains(m.Id));
            }
            else if (!string.IsNullOrWhiteSpace(namePattern))
            {
                Regex regex = new(namePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                filtered = filtered.Where(m => regex.IsMatch(m.Name));
            }
            return filtered.ToList();
        }

        private bool ShouldRunImport(ManagementInfo management)
        {
            if (management.DoNotImport)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(management.ImporterHostname))
            {
                return true;
            }
            return string.Equals(management.ImporterHostname, hostname, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record ManagementInfo(int Id, string Name, string ImporterHostname, bool DoNotImport);
    }
}
