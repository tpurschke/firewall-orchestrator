using Microsoft.Extensions.Options;

namespace FWO.GenericGatewayImporter
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.Configure<Models.GenericGatewayImporterOptions>(builder.Configuration.GetSection("GenericGatewayImporter"));
            builder.Services.Configure<Models.SkyhighOptions>(builder.Configuration.GetSection("Skyhigh"));
            builder.Services.Configure<Models.F5BigIpOptions>(builder.Configuration.GetSection("F5BigIp"));

            builder.Services.AddSingleton<Storage.IProxyRuleStore>(sp =>
            {
                Models.GenericGatewayImporterOptions options = sp.GetRequiredService<IOptions<Models.GenericGatewayImporterOptions>>().Value;
                return new Storage.PostgresProxyRuleStore(options);
            });

            builder.Services.AddHttpClient<Importers.SkyhighGenericGatewayImporter>()
                .ConfigurePrimaryHttpMessageHandler(sp =>
                    CreateHttpHandler(sp.GetRequiredService<IOptions<Models.SkyhighOptions>>().Value.VerifyTls));
            builder.Services.AddHttpClient<Importers.F5BigIpImporter>()
                .ConfigurePrimaryHttpMessageHandler(sp =>
                    CreateHttpHandler(sp.GetRequiredService<IOptions<Models.F5BigIpOptions>>().Value.VerifyTls));
            builder.Services.AddHostedService<Services.GenericGatewayImportScheduler>();

            var app = builder.Build();

            app.MapGet("/api/generic-gateway/health", () => Results.Ok(new { status = "ok" }));

            app.MapGet("/api/generic-gateway/rules", (string? managementIds, int? ownerId, int? dueWithinDays, bool? includeRecertified, Storage.IProxyRuleStore store) =>
            {
                List<int> mgmtIds = ParseIds(managementIds);
                List<Models.ProxyRuleDocument> rules = store.GetRules(mgmtIds, ownerId, dueWithinDays, includeRecertified ?? false);
                return Results.Ok(rules);
            });

            app.MapGet("/api/generic-gateway/recert/overview", (string? managementIds, int? dueWithinDays, Storage.IProxyRuleStore store) =>
            {
                List<int> mgmtIds = ParseIds(managementIds);
                List<Models.ProxyRecertOwnerOverview> overview = store.GetRecertOverview(mgmtIds, dueWithinDays);
                return Results.Ok(overview);
            });

            app.MapPost("/api/generic-gateway/recertify", (Models.ProxyRecertificationRequest request, Storage.IProxyRuleStore store, IOptions<Models.GenericGatewayImporterOptions> options) =>
            {
                int updated = store.RecertifyRules(request, options.Value.DefaultRecertificationDays);
                return Results.Ok(new { updated });
            });

            app.MapPost("/api/generic-gateway/import/skyhigh", async (int managementId, string? managementName, Importers.SkyhighGenericGatewayImporter importer, Storage.IProxyRuleStore store, CancellationToken ct) =>
            {
                List<Models.ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(managementId, ct);
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

            app.MapPost("/api/generic-gateway/import/f5", async (int managementId, string? managementName, Importers.F5BigIpImporter importer, Storage.IProxyRuleStore store, CancellationToken ct) =>
            {
                List<Models.ProxyRuleDocument> rules = await importer.ImportProxyRulesAsync(managementId, ct);
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
        }

        private static HttpMessageHandler CreateHttpHandler(bool verifyTls)
        {
            if (verifyTls)
            {
                return new HttpClientHandler();
            }

            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        }

        private static List<int> ParseIds(string? ids)
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
    }
}
