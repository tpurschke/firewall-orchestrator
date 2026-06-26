using FWO.Api.Client;
using FWO.Basics;
using FWO.Basics.Exceptions;
using FWO.Config.Api;
using FWO.Data;
using FWO.Logging;
using FWO.Services;
using Quartz;

namespace FWO.Middleware.Server.Jobs
{
    /// <summary>
    /// Quartz Job for importing TSQ path-analysis data.
    /// </summary>
    [DisallowConcurrentExecution]
    public class ImportPathAnalysisDataJob : IJob
    {
        private const string LogMessageTitle = "Import Path Analysis Data";
        private readonly ApiConnection apiConnection;
        private readonly GlobalConfig globalConfig;

        /// <summary>
        /// Creates a new path-analysis data import job.
        /// </summary>
        public ImportPathAnalysisDataJob(ApiConnection apiConnection, GlobalConfig globalConfig)
        {
            this.apiConnection = apiConnection;
            this.globalConfig = globalConfig;
        }

        /// <inheritdoc />
        public async Task Execute(IJobExecutionContext context)
        {
            Log.WriteDebug(LogMessageTitle, "Process started");

            try
            {
                PathAnalysisDataImport import = new(apiConnection, globalConfig);
                List<string> failedImports = await import.Run();
                if (failedImports.Count > 0)
                {
                    throw new ProcessingFailedException($"{LogMessageTitle} failed for {string.Join(", ", failedImports)}.");
                }
            }
            catch (Exception exception)
            {
                await AlertHelper.LogErrorsWithAlert(apiConnection, globalConfig, 2, LogMessageTitle,
                    GlobalConst.kImportPathAnalysisData, AlertCode.ImportPathAnalysisData, exception);
            }
        }
    }
}
