using System.Globalization;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace TestExtensions.Runners
{
    public class GrainPersistenceTestsRunner : OrleansTestingBase
    {
        private readonly ITestOutputHelper output;
        private readonly string grainNamespace;
        private readonly BaseTestClusterFixture fixture;
        protected readonly ILogger logger;
        protected TestCluster HostedCluster { get; private set; }

        public GrainPersistenceTestsRunner(ITestOutputHelper output, BaseTestClusterFixture fixture, string grainNamespace = "UnitTests.Grains")
        {
            this.output = output;
            this.fixture = fixture;
            this.grainNamespace = grainNamespace;
            logger = fixture.Logger;
            HostedCluster = fixture.HostedCluster;
        }

        public IGrainFactory GrainFactory => fixture.GrainFactory;

        [SkippableFact]
        public async Task Grain_GrainStorage_Delete()
        {
            var id = Guid.NewGuid();
            var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

            await grain.DoWrite(1);

            await grain.DoDelete();

            var val = await grain.GetValue(); // Should this throw instead?
            Assert.Equal(0, val);  // "Value after Delete"

            await grain.DoWrite(2);

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Delete + New Write"
        }

        [SkippableFact]
        public async Task Grain_GrainStorage_Read()
        {
            var id = Guid.NewGuid();
            var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"
        }

        [SkippableFact]
        public async Task Grain_GuidKey_GrainStorage_Read_Write()
        {
            var id = Guid.NewGuid();
            var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        [SkippableFact]
        public async Task Grain_LongKey_GrainStorage_Read_Write()
        {
            long id = Random.Shared.Next();
            var grain = GrainFactory.GetGrain<IGrainStorageTestGrain_LongKey>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        [SkippableFact]
        public async Task Grain_LongKeyExtended_GrainStorage_Read_Write()
        {
            long id = Random.Shared.Next();
            var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

            var
                grain = GrainFactory.GetGrain<IGrainStorageTestGrain_LongExtendedKey>(id, extKey, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();
            Assert.Equal(2, val);  // "Value after DoRead"

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Re-Read"

            var extKeyValue = await grain.GetExtendedKeyValue();
            Assert.Equal(extKey, extKeyValue);  // "Extended Key"
        }

        [SkippableFact]
        public async Task Grain_GuidKeyExtended_GrainStorage_Read_Write()
        {
            var id = Guid.NewGuid();
            var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

            var
                grain = GrainFactory.GetGrain<IGrainStorageTestGrain_GuidExtendedKey>(id, extKey, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();
            Assert.Equal(2, val);  // "Value after DoRead"

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Re-Read"

            var extKeyValue = await grain.GetExtendedKeyValue();
            Assert.Equal(extKey, extKeyValue);  // "Extended Key"
        }

        [SkippableFact]
        public async Task Grain_Generic_GrainStorage_Read_Write()
        {
            long id = Random.Shared.Next();

            var grain = GrainFactory.GetGrain<IGrainStorageGenericGrain<int>>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        [SkippableFact]
        public async Task Grain_NestedGeneric_GrainStorage_Read_Write()
        {
            long id = Random.Shared.Next();

            var grain = GrainFactory.GetGrain<IGrainStorageGenericGrain<List<int>>>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Null(val);  // "Initial value"

            await grain.DoWrite(new List<int> { 1 });
            val = await grain.GetValue();
            Assert.Equal(new List<int> { 1 }, val);  // "Value after Write-1"

            await grain.DoWrite(new List<int> { 1, 2 });
            val = await grain.GetValue();
            Assert.Equal(new List<int> { 1, 2 }, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(new List<int> { 1, 2 }, val);  // "Value after Re-Read"
        }

        [SkippableFact]
        public async Task Grain_Generic_GrainStorage_DiffTypes()
        {
            long id1 = Random.Shared.Next();
            var id2 = id1;
            var id3 = id1;

            var grain1 = GrainFactory.GetGrain<IGrainStorageGenericGrain<int>>(id1, grainNamespace);

            var grain2 = GrainFactory.GetGrain<IGrainStorageGenericGrain<string>>(id2, grainNamespace);

            var grain3 = GrainFactory.GetGrain<IGrainStorageGenericGrain<double>>(id3, grainNamespace);

            var val1 = await grain1.GetValue();
            Assert.Equal(0, val1);  // "Initial value - 1"

            var val2 = await grain2.GetValue();
            Assert.Null(val2);  // "Initial value - 2"

            var val3 = await grain3.GetValue();
            Assert.Equal(0.0, val3);  // "Initial value - 3"

            var expected1 = 1;
            await grain1.DoWrite(expected1);
            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value after Write#1 - 1"

            var expected2 = "Three";
            await grain2.DoWrite(expected2);
            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value after Write#1 - 2"

            var expected3 = 5.1;
            await grain3.DoWrite(expected3);
            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value after Write#1 - 3"

            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value before Write#2 - 1"
            expected1 = 2;
            await grain1.DoWrite(expected1);
            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value after Write#2 - 1"
            val1 = await grain1.DoRead();
            Assert.Equal(expected1, val1);  // "Value after Re-Read - 1"

            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value before Write#2 - 2"
            expected2 = "Four";
            await grain2.DoWrite(expected2);
            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value after Write#2 - 2"
            val2 = await grain2.DoRead();
            Assert.Equal(expected2, val2);  // "Value after Re-Read - 2"

            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value before Write#2 - 3"
            expected3 = 6.2;
            await grain3.DoWrite(expected3);
            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value after Write#2 - 3"
            val3 = await grain3.DoRead();
            Assert.Equal(expected3, val3);  // "Value after Re-Read - 3"
        }
        
        [SkippableFact]
        public async Task Grain_GrainStorage_SiloRestart()
        {
            var initialServiceId = fixture.GetClientServiceId();

            output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, initialServiceId);

            var id = Guid.NewGuid();
            var grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);

            var serviceId = await GrainFactory.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
            Assert.Equal(initialServiceId, serviceId);  // "ServiceId same before restart."

            output.WriteLine("About to reset Silos");
            foreach (var silo in HostedCluster.GetActiveSilos().ToList())
            {
                await HostedCluster.RestartSiloAsync(silo);
            }

            await HostedCluster.InitializeClientAsync();

            output.WriteLine("Silos restarted");

            serviceId = await GrainFactory.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
            grain = GrainFactory.GetGrain<IGrainStorageTestGrain>(id, grainNamespace);
            output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, serviceId);
            Assert.Equal(initialServiceId, serviceId);  // "ServiceId same after restart."

            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }
    }
}
