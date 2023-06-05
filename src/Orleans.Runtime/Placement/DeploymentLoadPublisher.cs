using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime.Scheduler;
using Orleans.Statistics;

namespace Orleans.Runtime
{
    /// <summary>
    /// This class collects runtime statistics for all silos in the current deployment for use by placement.
    /// </summary>
    internal class DeploymentLoadPublisher : SystemTarget, IDeploymentLoadPublisher, ISiloStatusListener
    {
        private readonly ILocalSiloDetails siloDetails;
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IInternalGrainFactory grainFactory;
        private readonly ActivationDirectory activationDirectory;
        private readonly IActivationWorkingSet activationWorkingSet;
        private readonly IAppEnvironmentStatistics appEnvironmentStatistics;
        private readonly IHostEnvironmentStatistics hostEnvironmentStatistics;
        private readonly IOptions<LoadSheddingOptions> loadSheddingOptions;

        private readonly ConcurrentDictionary<SiloAddress, SiloRuntimeStatistics> periodicStats;
        private readonly TimeSpan statisticsRefreshTime;
        private readonly IList<ISiloStatisticsChangeListener> siloStatisticsChangeListeners;
        private readonly ILogger logger;
        private IDisposable publishTimer;

        public ConcurrentDictionary<SiloAddress, SiloRuntimeStatistics> PeriodicStatistics { get { return periodicStats; } }

        public DeploymentLoadPublisher(
            ILocalSiloDetails siloDetails,
            ISiloStatusOracle siloStatusOracle,
            IOptions<DeploymentLoadPublisherOptions> options,
            IInternalGrainFactory grainFactory,
            ILoggerFactory loggerFactory,
            ActivationDirectory activationDirectory,
            IActivationWorkingSet activationWorkingSet,
            IAppEnvironmentStatistics appEnvironmentStatistics,
            IHostEnvironmentStatistics hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions)
            : base(Constants.DeploymentLoadPublisherSystemTargetType, siloDetails.SiloAddress, loggerFactory)
        {
            logger = loggerFactory.CreateLogger<DeploymentLoadPublisher>();
            this.siloDetails = siloDetails;
            this.siloStatusOracle = siloStatusOracle;
            this.grainFactory = grainFactory;
            this.activationDirectory = activationDirectory;
            this.activationWorkingSet = activationWorkingSet;
            this.appEnvironmentStatistics = appEnvironmentStatistics;
            this.hostEnvironmentStatistics = hostEnvironmentStatistics;
            this.loadSheddingOptions = loadSheddingOptions;
            statisticsRefreshTime = options.Value.DeploymentLoadPublisherRefreshTime;
            periodicStats = new ConcurrentDictionary<SiloAddress, SiloRuntimeStatistics>();
            siloStatisticsChangeListeners = new List<ISiloStatisticsChangeListener>();
        }

        public async Task Start()
        {
            logger.LogDebug("Starting DeploymentLoadPublisher");
            if (statisticsRefreshTime > TimeSpan.Zero)
            {
                // Randomize PublishStatistics timer,
                // but also upon start publish my stats to everyone and take everyone's stats for me to start with something.
                var randomTimerOffset = RandomTimeSpan.Next(statisticsRefreshTime);
                publishTimer = RegisterTimer(PublishStatistics, null, randomTimerOffset, statisticsRefreshTime, "DeploymentLoadPublisher.PublishStatisticsTimer");
            }
            await RefreshStatistics();
            await PublishStatistics(null);
            logger.LogDebug("Started DeploymentLoadPublisher");
        }

        private async Task PublishStatistics(object _)
        {
            try
            {
                if (logger.IsEnabled(LogLevel.Trace)) logger.LogTrace("PublishStatistics");
                var members = siloStatusOracle.GetApproximateSiloStatuses(true).Keys;
                var tasks = new List<Task>();
                var activationCount = activationDirectory.Count;
                var recentlyUsedActivationCount = activationWorkingSet.Count;
                var myStats = new SiloRuntimeStatistics(
                    activationCount,
                    recentlyUsedActivationCount,
                    appEnvironmentStatistics,
                    hostEnvironmentStatistics,
                    loadSheddingOptions,
                    DateTime.UtcNow);
                foreach (var siloAddress in members)
                {
                    try
                    {
                        tasks.Add(grainFactory.GetSystemTarget<IDeploymentLoadPublisher>(
                            Constants.DeploymentLoadPublisherSystemTargetType, siloAddress)
                            .UpdateRuntimeStatistics(siloDetails.SiloAddress, myStats));
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            (int)ErrorCode.Placement_RuntimeStatisticsUpdateFailure_1,
                            exception,
                            "An unexpected exception was thrown by PublishStatistics.UpdateRuntimeStatistics(). Ignored");
                    }
                }
                await Task.WhenAll(tasks);
            }
            catch (Exception exc)
            {
                logger.LogWarning(
                    (int)ErrorCode.Placement_RuntimeStatisticsUpdateFailure_2,
                    exc,
                    "An exception was thrown by PublishStatistics.UpdateRuntimeStatistics(). Ignoring");
            }
        }


        public Task UpdateRuntimeStatistics(SiloAddress siloAddress, SiloRuntimeStatistics siloStats)
        {
            if (logger.IsEnabled(LogLevel.Trace)) logger.LogTrace("UpdateRuntimeStatistics from {Server}", siloAddress);
            if (siloStatusOracle.GetApproximateSiloStatus(siloAddress) != SiloStatus.Active)
                return Task.CompletedTask;

            SiloRuntimeStatistics old;
            // Take only if newer.
            if (periodicStats.TryGetValue(siloAddress, out old) && old.DateTime > siloStats.DateTime)
                return Task.CompletedTask;

            periodicStats[siloAddress] = siloStats;
            NotifyAllStatisticsChangeEventsSubscribers(siloAddress, siloStats);
            return Task.CompletedTask;
        }

        internal async Task<ConcurrentDictionary<SiloAddress, SiloRuntimeStatistics>> RefreshStatistics()
        {
            if (logger.IsEnabled(LogLevel.Trace)) logger.LogTrace("RefreshStatistics");
            await this.RunOrQueueTask(() =>
                {
                    var tasks = new List<Task>();
                    var members = siloStatusOracle.GetApproximateSiloStatuses(true).Keys;
                    foreach (var siloAddress in members)
                    {
                        var capture = siloAddress;
                        var task = grainFactory.GetSystemTarget<ISiloControl>(Constants.SiloControlType, capture)
                                .GetRuntimeStatistics()
                                .ContinueWith((Task<SiloRuntimeStatistics> statsTask) =>
                                    {
                                        if (statsTask.Status == TaskStatus.RanToCompletion)
                                        {
                                            UpdateRuntimeStatistics(capture, statsTask.Result);
                                        }
                                        else
                                        {
                                            logger.LogWarning(
                                                (int)ErrorCode.Placement_RuntimeStatisticsUpdateFailure_3,
                                                statsTask.Exception,
                                                "An unexpected exception was thrown from RefreshStatistics by ISiloControl.GetRuntimeStatistics({SiloAddress}). Will keep using stale statistics.",
                                                capture);
                                        }
                                    });
                        tasks.Add(task);
                        task.Ignore();
                    }
                    return Task.WhenAll(tasks);
                });
            return periodicStats;
        }

        public bool SubscribeToStatisticsChangeEvents(ISiloStatisticsChangeListener observer)
        {
            lock (siloStatisticsChangeListeners)
            {
                if (siloStatisticsChangeListeners.Contains(observer)) return false;

                siloStatisticsChangeListeners.Add(observer);
                return true;
            }
        }

        public bool UnsubscribeStatisticsChangeEvents(ISiloStatisticsChangeListener observer)
        {
            lock (siloStatisticsChangeListeners)
            {
                return siloStatisticsChangeListeners.Contains(observer) &&
                    siloStatisticsChangeListeners.Remove(observer);
            }
        }

        private void NotifyAllStatisticsChangeEventsSubscribers(SiloAddress silo, SiloRuntimeStatistics stats)
        {
            lock (siloStatisticsChangeListeners)
            {
                foreach (var subscriber in siloStatisticsChangeListeners)
                {
                    if (stats == null)
                    {
                        subscriber.RemoveSilo(silo);
                    }
                    else
                    {
                        subscriber.SiloStatisticsChangeNotification(silo, stats);
                    }
                }
            }
        }


        public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
        {
            WorkItemGroup.QueueAction(() =>
            {
                Utils.SafeExecute(() => OnSiloStatusChange(updatedSilo, status), logger);
            });
        }

        private void OnSiloStatusChange(SiloAddress updatedSilo, SiloStatus status)
        {
            if (!status.IsTerminating()) return;

            if (Equals(updatedSilo, Silo))
                publishTimer.Dispose();
            periodicStats.TryRemove(updatedSilo, out _);
            NotifyAllStatisticsChangeEventsSubscribers(updatedSilo, null);
        }
    }
}
