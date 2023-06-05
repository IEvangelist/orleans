using Orleans.Runtime;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;

namespace UnitTests.PersistentState.Grains
{
    [GrainType("new-test-storage-grain")]
    public class GrainStorageTestGrain : Grain,
        IGrainStorageTestGrain, IGrainStorageTestGrain_LongKey
    {
        private readonly IPersistentState<PersistenceTestGrainState> persistentState;

        public GrainStorageTestGrain(
            [PersistentState("state", "GrainStorageForTest")]
            IPersistentState<PersistenceTestGrainState> persistentState)
        {
            this.persistentState = persistentState;
        }

        public Task<int> GetValue()
        {
            return Task.FromResult(persistentState.State.Field1);
        }

        public Task DoWrite(int val)
        {
            persistentState.State.Field1 = val;
            return persistentState.WriteStateAsync();
        }

        public async Task<int> DoRead()
        {
            await persistentState.ReadStateAsync(); // Re-read state from store
            return persistentState.State.Field1;
        }

        public Task DoDelete()
        {
            return persistentState.ClearStateAsync();
        }
    }

    [Orleans.Providers.StorageProvider(ProviderName = "GrainStorageForTest")]
    [GrainType("new-test-storage-grain-with-extended-key")]
    public class GrainStorageTestGrainExtendedKey : Grain,
        IGrainStorageTestGrain_GuidExtendedKey, IGrainStorageTestGrain_LongExtendedKey
    {
        private readonly IPersistentState<PersistenceTestGrainState> persistentState;

        public GrainStorageTestGrainExtendedKey(
            [PersistentState("state", "GrainStorageForTest")]
            IPersistentState<PersistenceTestGrainState> persistentState)
        {
            this.persistentState = persistentState;
        }

        public Task<int> GetValue()
        {
            return Task.FromResult(persistentState.State.Field1);
        }

        public Task<string> GetExtendedKeyValue()
        {
            string extKey;
            _ = this.GetPrimaryKey(out extKey);
            return Task.FromResult(extKey);
        }

        public Task DoWrite(int val)
        {
            persistentState.State.Field1 = val;
            return persistentState.WriteStateAsync();
        }

        public async Task<int> DoRead()
        {
            await persistentState.ReadStateAsync(); // Re-read state from store
            return persistentState.State.Field1;
        }

        public Task DoDelete()
        {
            return persistentState.ClearStateAsync();
        }
    }
}