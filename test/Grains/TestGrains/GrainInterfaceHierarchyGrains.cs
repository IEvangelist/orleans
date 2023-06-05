﻿using TestGrainInterfaces;

namespace TestGrains
{
    public class DoSomethingEmptyGrain : Grain, IDoSomethingEmptyGrain
    {
        private int A;

        public Task<string> DoIt() => Task.FromResult(GetType().Name);

        public Task SetA(int a)
        {
            A = a;
            return Task.CompletedTask;
        }

        public Task IncrementA()
        {
            A++;
            return Task.CompletedTask;
        }

        public Task<int> GetA() => Task.FromResult(A);
    }

    public class DoSomethingEmptyWithMoreGrain : Grain, IDoSomethingEmptyWithMoreGrain
    {
        private int A;

        public Task<string> DoIt() => Task.FromResult(GetType().Name);

        public Task<string> DoMore() => Task.FromResult(GetType().Name);

        public Task SetA(int a)
        {
            A = a;
            return Task.CompletedTask;
        }

        public Task IncrementA()
        {
            A++;
            return Task.CompletedTask;
        }

        public Task<int> GetA() => Task.FromResult(A);
    }

    public class DoSomethingWithMoreGrain : Grain, IDoSomethingWithMoreGrain
    {
        private int A;
        private int B;

        public Task<string> DoIt() => Task.FromResult(GetType().Name);

        public Task<string> DoThat() => Task.FromResult(GetType().Name);

        public Task SetA(int a)
        {
            A = a;
            return Task.CompletedTask;
        }

        public Task IncrementA()
        {
            A++;
            return Task.CompletedTask;
        }

        public Task<int> GetA() => Task.FromResult(A);

        public Task SetB(int b)
        {
            B = b;
            return Task.CompletedTask;
        }

        public Task IncrementB()
        {
            B++;
            return Task.CompletedTask;
        }

        public Task<int> GetB() => Task.FromResult(B);

    }

    public class DoSomethingWithMoreEmptyGrain : Grain, IDoSomethingWithMoreEmptyGrain
    {
        private int A;

        public Task<string> DoIt() => Task.FromResult(GetType().Name);

        public Task SetA(int a)
        {
            A = a;
            return Task.CompletedTask;
        }

        public Task IncrementA()
        {
            A++;
            return Task.CompletedTask;
        }

        public Task<int> GetA() => Task.FromResult(A);

        public Task<string> DoMore() => Task.FromResult(GetType().Name);
    }



    public class DoSomethingCombinedGrain : Grain, IDoSomethingCombinedGrain
    {
        private int A;
        private int B;
        private int C;

        public Task<string> DoIt() => Task.FromResult(GetType().Name);

        public Task<string> DoMore() => Task.FromResult(GetType().Name);

        public Task<string> DoThat() => Task.FromResult(GetType().Name);

        public Task SetA(int a)
        {
            A = a;
            return Task.CompletedTask;
        }

        public Task IncrementA()
        {
            A++;
            return Task.CompletedTask;
        }

        public Task<int> GetA() => Task.FromResult(A);

        public Task SetB(int b)
        {
            B = b;
            return Task.CompletedTask;
        }

        public Task IncrementB()
        {
            B++;
            return Task.CompletedTask;
        }

        public Task<int> GetB() => Task.FromResult(B);

        public Task SetC(int c)
        {
            C = c;
            return Task.CompletedTask;
        }

        public Task IncrementC()
        {
            C++;
            return Task.CompletedTask;
        }

        public Task<int> GetC() => Task.FromResult(C);
    }
}
