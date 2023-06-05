using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.SerializerTests
{
    [TestCategory("Serialization"), TestCategory("BVT")]
    public class RoundTripSerializerTests : HostedTestClusterEnsureDefaultStarted
    {
        public RoundTripSerializerTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Serialize_TestMethodResultRecord()
        {
            var grain = GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            var retVal = await grain.GetRetValForParamVal(new ParamVal(42));
            Assert.Equal(42, retVal.Value);
        }

        [Fact]
        public async Task Serialize_TestMethodResultEnum()
        {
            var grain = GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            var result = await grain.GetEnemyType();
            Assert.Equal(CampaignEnemyTestType.Enemy2, result); //Enum return value wasn't transmitted properly
        }

        [Fact]
        public async Task Serialize_TestMethodResultWithInheritedClosedGeneric()
        {
            var grain = GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            var result = await grain.GetClosedGenericValue();
            Assert.NotNull(result);
        }
    }
}
