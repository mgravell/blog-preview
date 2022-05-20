using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ForEachBenchmarks
{
    [SimpleJob(RuntimeMoniker.Net60), MemoryDiagnoser]
    public class ForEachBenchmark
    {
        static void Main(string[] args)
            => BenchmarkRunner.Run(typeof(ForEachBenchmark).Assembly, args: args);

        private readonly List<SomeStruct> _list = new List<SomeStruct>();
        private SomeStruct[] _array;
        private CustomWrapper _custom;

        [GlobalSetup]
        public void Populate()
        {
            for (int i = 0; i < 1000; i++)
            {
                _list.Add(new SomeStruct(i));
            }
            _array = _list.ToArray();
            _custom = new CustomWrapper(_array);
        }

        [Benchmark]
        public int ListForEachLoop()
        {
            int total = 0;
            foreach (var tmp in _list)
            {
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ArrayForEachLoop()
        {
            int total = 0;
            foreach (var tmp in _array)
            {
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int CustomForEachLoop()
        {
            int total = 0;
            foreach (var tmp in _custom)
            {
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ListForLoop()
        {
            int total = 0;
            var snapshot = _list; // (just reduce the field fetches)
            for (int i = 0; i < snapshot.Count; i++)
            {
                total += snapshot[i].SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ArrayForLoop()
        {
            int total = 0;
            var snapshot = _array; // make sure we can elide bounds checks (and also: reduce field fetches)
            for (int i = 0; i < snapshot.Length; i++)
            {
                total += snapshot[i].SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int CustomForLoop()
        {
            int total = 0;
            var snapshot = _custom; // (just reduce field fetches)
            int length = _custom.Length; // JIT won't hoist / elide :(
            for (int i = 0; i < length; i++)
            {
                total += snapshot[i].SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ListLinqSum()
            => _list.Sum(x => x.SomeValue);

        [Benchmark]
        public int ArrayLinqSum()
            => _array.Sum(x => x.SomeValue);

        [Benchmark]
        public int ListForEachMethod()
        {
            int total = 0;
            _list.ForEach(x => total += x.SomeValue);
            return total;
        }

        [Benchmark]
        public int ListRefForeachLoop()
        {
            int total = 0;
            // note: you can do this directly on spans; this code shows
            // how you can get the inner span from a *list* (and why you might want to)
            foreach (ref var tmp in CollectionsMarshal.AsSpan(_list))
            {   // also works identically with "ref readonly var", since this is
                // a readonly struct
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ListSpanForLoop()
        {
            int total = 0;
            var span = CollectionsMarshal.AsSpan(_list);
            for (int i = 0; i < span.Length; i++)
            {
                total += span[i].SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int ArrayRefForeachLoop()
        {
            int total = 0;
            foreach (ref var tmp in _array.AsSpan())
            {   // also works identically with "ref readonly var", since this is
                // a readonly struct
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int CustomRefForeachLoop()
        {
            int total = 0;
            foreach (ref readonly var tmp in _custom)
            {   // need "ref readonly" here, as we've protected the value
                total += tmp.SomeValue;
            }
            return total;
        }


        [Benchmark]
        public int CustomSpanForeachLoop()
        {
            int total = 0;
            foreach (var tmp in _custom.AsSpan())
            {
                total += tmp.SomeValue;
            }
            return total;
        }

        [Benchmark]
        public int CustomSpanRefForeachLoop()
        {
            int total = 0;
            foreach (ref readonly var tmp in _custom.AsSpan())
            {   // need "ref readonly" here, as we've protected the value
                total += tmp.SomeValue;
            }
            return total;
        }
    }

    // deliberately non-trivial size
    internal readonly struct SomeStruct
    {
        public SomeStruct(int someValue)
        {
            SomeValue = someValue;
            When = DateTime.UtcNow;
            Id = Guid.NewGuid();
            HowMuch = 42;
        }
        public int SomeValue { get; }

        // some other values just to pad things a bit
        public DateTime When { get; }
        public decimal HowMuch { get; }
        public Guid Id { get; }
    }

    readonly struct CustomWrapper
    {
        private readonly SomeStruct[] _array; // or some other underlying store
        public CustomWrapper(SomeStruct[] array)
            => _array = array;

        public ReadOnlySpan<SomeStruct> AsSpan() => _array; // for convenience

        public int Length => _array.Length;

        public ref readonly SomeStruct this[int index]
            => ref _array[index];

        public Enumerator GetEnumerator()
            => new Enumerator(_array);

        public struct Enumerator
        {
            private readonly SomeStruct[] _array;
            private int _index;

            internal Enumerator(SomeStruct[] array)
            {
                _array = array;
                _index = -1;
            }

            public bool MoveNext()
                => ++_index < _array.Length;

            public ref readonly SomeStruct Current
                => ref _array[_index];
        }
    }

}