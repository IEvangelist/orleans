﻿using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    internal class KeyExtensionTestGrain : Grain, IKeyExtensionTestGrain
    {
        private readonly Guid uniqueId = Guid.NewGuid();

        public Task<IKeyExtensionTestGrain> GetGrainReference() => Task.FromResult(this.AsReference<IKeyExtensionTestGrain>());

        public Task<string> GetActivationId() => Task.FromResult(uniqueId.ToString());
    }
}
