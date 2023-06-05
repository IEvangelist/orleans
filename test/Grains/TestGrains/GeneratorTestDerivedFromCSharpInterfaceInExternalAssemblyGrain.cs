﻿using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class GeneratorTestDerivedFromCSharpInterfaceInExternalAssemblyGrain : Grain, IGeneratorTestDerivedFromCSharpInterfaceInExternalAssemblyGrain
    {
        public Task<int> Echo(int x) => Task.FromResult(x);
    }
}
