using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Serialization;

namespace Orleans.Runtime.MembershipService
{
    internal class SystemTargetBasedMembershipTable : IMembershipTable
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;
        private IMembershipTableSystemTarget grain;

        public SystemTargetBasedMembershipTable(IServiceProvider serviceProvider, ILogger<SystemTargetBasedMembershipTable> logger)
        {
            this.serviceProvider = serviceProvider;
            this.logger = logger;
        }
        public async Task InitializeMembershipTable(bool tryInitTableVersion) => grain = await GetMembershipTable();

        private async Task<IMembershipTableSystemTarget> GetMembershipTable()
        {
            var options = serviceProvider.GetRequiredService<IOptions<DevelopmentClusterMembershipOptions>>().Value;
            if (options.PrimarySiloEndpoint == null)
            {
                throw new OrleansConfigurationException(
                    $"{nameof(DevelopmentClusterMembershipOptions)}.{nameof(options.PrimarySiloEndpoint)} must be set when using development clustering.");
            }

            var siloDetails = serviceProvider.GetService<ILocalSiloDetails>();
            var isPrimarySilo = siloDetails.SiloAddress.Endpoint.Equals(options.PrimarySiloEndpoint);
            if (isPrimarySilo)
            {
                logger.LogInformation((int)ErrorCode.MembershipFactory1, "Creating in-memory membership table");
                var catalog = serviceProvider.GetRequiredService<Catalog>();
                catalog.RegisterSystemTarget(ActivatorUtilities.CreateInstance<MembershipTableSystemTarget>(serviceProvider));
            }

            var grainFactory = serviceProvider.GetRequiredService<IInternalGrainFactory>();
            var result = grainFactory.GetSystemTarget<IMembershipTableSystemTarget>(Constants.SystemMembershipTableType, SiloAddress.New(options.PrimarySiloEndpoint, 0));
            if (isPrimarySilo)
            {
                await WaitForTableGrainToInit(result);
            }

            return result;
        }

        // Only used with MembershipTableGrain to wait for primary to start.
        private async Task WaitForTableGrainToInit(IMembershipTableSystemTarget membershipTableSystemTarget)
        {
            var timespan = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(5);
            // This is a quick temporary solution to enable primary node to start fully before secondaries.
            // Secondary silos waits untill GrainBasedMembershipTable is created. 
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    await membershipTableSystemTarget.ReadAll().WithTimeout(timespan, $"MembershipGrain trying to read all content of the membership table, failed due to timeout {timespan}");
                    logger.LogInformation((int)ErrorCode.MembershipTableGrainInit2, "Connected to membership table provider.");
                    return;
                }
                catch (Exception exc)
                {
                    var type = exc.GetBaseException().GetType();
                    if (type == typeof(TimeoutException) || type == typeof(OrleansException))
                    {
                        logger.LogInformation(
                            (int)ErrorCode.MembershipTableGrainInit3,
                            "Waiting for membership table provider to initialize. Going to sleep for {Duration} and re-try to reconnect.",
                            timespan);
                    }
                    else
                    {
                        logger.LogInformation((int)ErrorCode.MembershipTableGrainInit4, "Membership table provider failed to initialize. Giving up.");
                        throw;
                    }
                }

                await Task.Delay(timespan);
            }
        }

        public Task DeleteMembershipTableEntries(string clusterId) => grain.DeleteMembershipTableEntries(clusterId);

        public Task<MembershipTableData> ReadRow(SiloAddress key) => grain.ReadRow(key);

        public Task<MembershipTableData> ReadAll() => grain.ReadAll();

        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => grain.InsertRow(entry, tableVersion);

        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => grain.UpdateRow(entry, etag, tableVersion);

        public Task UpdateIAmAlive(MembershipEntry entry) => grain.UpdateIAmAlive(entry);

        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => throw new NotImplementedException();
    }

    [Reentrant]
    internal class MembershipTableSystemTarget : SystemTarget, IMembershipTableSystemTarget
    {
        private InMemoryMembershipTable table;
        private readonly ILogger logger;

        public MembershipTableSystemTarget(
            ILocalSiloDetails localSiloDetails,
            ILoggerFactory loggerFactory,
            DeepCopier deepCopier)
            : base(CreateId(localSiloDetails), localSiloDetails.SiloAddress, loggerFactory)
        {
            logger = loggerFactory.CreateLogger<MembershipTableSystemTarget>();
            table = new InMemoryMembershipTable(deepCopier);
            logger.LogInformation((int)ErrorCode.MembershipGrainBasedTable1, "GrainBasedMembershipTable Activated.");
        }

        private static SystemTargetGrainId CreateId(ILocalSiloDetails localSiloDetails) => SystemTargetGrainId.Create(Constants.SystemMembershipTableType, SiloAddress.New(localSiloDetails.SiloAddress.Endpoint, 0));

        public Task InitializeMembershipTable(bool tryInitTableVersion)
        {
            logger.LogInformation("InitializeMembershipTable {TryInitTableVersion}.", tryInitTableVersion);
            return Task.CompletedTask;
        }

        public Task DeleteMembershipTableEntries(string clusterId)
        {
            logger.LogInformation("DeleteMembershipTableEntries {ClusterId}", clusterId);
            table = null;
            return Task.CompletedTask;
        }

        public Task<MembershipTableData> ReadRow(SiloAddress key) => Task.FromResult(table.Read(key));

        public Task<MembershipTableData> ReadAll()
        {
            var t = table.ReadAll();
            return Task.FromResult(t);
        }

        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("InsertRow entry = {Entry}, table version = {Version}", entry.ToString(), tableVersion);
            var result = table.Insert(entry, tableVersion);
            if (result == false)
                logger.LogInformation(
                    (int)ErrorCode.MembershipGrainBasedTable2,
                    "Insert of {Entry} and table version {Version} failed. Table now is {Table}",
                    entry.ToString(),
                    tableVersion,
                    table.ReadAll());

            return Task.FromResult(result);
        }

        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("UpdateRow entry = {Entry}, etag = {ETag}, table version = {Version}", entry.ToString(), etag, tableVersion);
            var result = table.Update(entry, etag, tableVersion);
            if (result == false)
                logger.LogInformation(
                    (int)ErrorCode.MembershipGrainBasedTable3,
                    "Update of {Entry}, eTag {ETag}, table version {Version} failed. Table now is {Table}",
                    entry.ToString(),
                    etag,
                    tableVersion,
                    table.ReadAll());

            return Task.FromResult(result);
        }

        public Task UpdateIAmAlive(MembershipEntry entry)
        {
            if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("UpdateIAmAlive entry = {Entry}", entry.ToString());
            table.UpdateIAmAlive(entry);
            return Task.CompletedTask;
        }

        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => throw new NotImplementedException();
    }
}