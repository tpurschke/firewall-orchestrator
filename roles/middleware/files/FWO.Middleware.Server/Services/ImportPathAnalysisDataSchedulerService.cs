using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Config.Api;
using FWO.Middleware.Server.Jobs;
using Quartz;

namespace FWO.Middleware.Server.Services
{
    /// <summary>
    /// Config listener and rescheduler for TSQ path-analysis data imports.
    /// </summary>
    public class ImportPathAnalysisDataSchedulerService : QuartzSchedulerServiceBase<ImportPathAnalysisDataJob>
    {
        private const string JobKeyName = "ImportPathAnalysisDataJob";
        private const string TriggerKeyName = "ImportPathAnalysisDataTrigger";
        private const string SchedulerName = "ImportPathAnalysisDataScheduler";

        /// <summary>
        /// Initializes the path-analysis data import scheduler service.
        /// </summary>
        public ImportPathAnalysisDataSchedulerService(
            ISchedulerFactory schedulerFactory,
            ApiConnection apiConnection,
            GlobalConfig globalConfig,
            IHostApplicationLifetime appLifetime)
            : base(
                schedulerFactory,
                apiConnection,
                globalConfig,
                appLifetime,
                new QuartzSchedulerOptions(
                    SchedulerName,
                    JobKeyName,
                    TriggerKeyName,
                    ConfigQueries.subscribeImportPathAnalysisDataConfigChanges))
        { }

        /// <inheritdoc/>
        protected override int SleepTime => globalConfig.ImportPathAnalysisDataSleepTime;

        /// <inheritdoc/>
        protected override DateTime StartAt => globalConfig.ImportPathAnalysisDataStartAt;

        /// <inheritdoc/>
        protected override TimeSpan Interval => TimeSpan.FromHours(globalConfig.ImportPathAnalysisDataSleepTime);

        /// <inheritdoc/>
        protected override string IntervalLogSuffix => "h";
    }
}
