# Unusual optimizations; ref foreach and ref returns

A really interesting feature quietly slipped into C# 7.3 - interesting to me, at least - but which I've seen almost no noise about. As I've said many times before: I have niche interests - I spend a lot of time in library code, or acting in a consulting capacity on performance tuning application code - so in both capacities, I tend to look at performance tweaks that *aren't usually needed*, but when they are: they're **glorious**. As I say: I haven't seen this discussed a lot, so: "be the change you want to see" - here's my attempt to sell you on the glory of `ref foreach`.

Because I know folks have a short attention span, I'll start with the money shot:

|                   Method |       Mean |  Gen 0 | Allocated |
|------------------------- |-----------:|-------:|----------:|
|          ListForEachLoop | 2,724.7 ns |      - |         - |
|         ArrayForEachLoop |   972.2 ns |      - |         - |
|        CustomForEachLoop |   987.2 ns |      - |         - |
|              ListForLoop | 1,201.3 ns |      - |         - |
|             ArrayForLoop |   593.0 ns |      - |         - |
|            CustomForLoop |   596.2 ns |      - |         - |
|              ListLinqSum | 7,057.1 ns | 0.0076 |      80 B |
|             ArrayLinqSum | 4,832.7 ns |      - |      32 B |
|        ListForEachMethod | 2,070.6 ns | 0.0114 |      88 B |
|       ListRefForeachLoop |   586.2 ns |      - |         - |
|          ListSpanForLoop |   590.3 ns |      - |         - |
|      ArrayRefForeachLoop |   574.1 ns |      - |         - |
|     CustomRefForeachLoop |   581.0 ns |      - |         - |
|    CustomSpanForeachLoop |   816.1 ns |      - |         - |
| CustomSpanRefForeachLoop |   592.2 ns |      - |         - |

With the point being: I want to sell you on those sub-600 nanosecond versions, rather than the multi-millisecond versions *of the same operation*.


# What the hell is `ref foreach`?

First, simple recap: let's consider:

``` c#
foreach (var someValue in someSequence)
{
    someValue.DoSomething();
}
```

The *details* here may vary depending on what `someSequence` is, but *conceptually*, what this is doing is reading each value from `someSequence` into a local variable `someValue`, and calling the `DoSomething()` method on each. If the type of `someValue` is a reference-type (i.e. a `class` or `interface`), then each "value" in the sequence is just that: a reference - so we're not really moving much data around here, just a pointer.

When this gets *interesting* is: what if the type of `someValue` is a `struct`? And in particular, what if it is a *heckin' chonka* of a `struct`? (and yes, there *are* some interesting scenarios where `struct` is useful outside of simple data types, especially if we enforce `readonly struct` to prevent ourselves from shooting our own feet off) In that case, copying the value out of the sequence can be a singificant operation (if we do it often enough to care). Historically, the `foreach` syntax has an inbuilt implementation for some types (arrays, etc), falling back to a duck-typed pattern that relies on a `bool MoveNext()` and `SomeType Current {get;}` pair (often, but not exclusively, provided via `IEnumerator<T>`) - so the "return the entire value" is baked into the old signature (via  the `Current` property).

***What if we could avoid that?***

# For arrays: *we already can!*

Let's consider that `someSequence` is explicitly typed as an array. It is very tempting to think that `foreach` and `for` over the array work the same - i.e. the same `foreach` as above, compared to `for`:

``` c#
for(int i = 0 ; i < someArray.Length ; i++)
{
    someArray[i].DoSomething();
}
```

But: if we [run both of those through sharplab](https://sharplab.io/#v2:D4AQzABCBMEMIFgBQBvZENQIwDYoBYIAxAewCcBRAQwGMALACgGUSBbAUwBUBPAB3YDaAXQgBnNuwCCZMlW4BKdJjRJMaiADNy7WnQgMAblTJiJANSoAbAK7sIASwB2pjtNkKl6iCq9fxHCxt2ADoAERIWDgAXOicAcwZ5AG5PdQBfVIzVTBBcAmJyZgkefmEXKRk5RWyMH18tEwYnKIcIAF4IAAYIJNaAHnK3OWCAGXZHOJiehwBqGerfb1TF/wr3AXshMIiJGPjElJr0zOQssh0AExJHS24xKLJrGhbIrj52ZDqMc6orm7uLuwaPZWFYIFQADQAIwhNEOanA+XCrz2E0S3iyaSAA==), we can see that they compile differently; in C#, the difference is that `foreach` is basically:

``` c#
SomeType someType = someArray[index];
someType.DoSomething();
```

which fetches the entire value out of the array, where as `for` is:

``` c#
someArray[index].DoSomething();
```

Now, you might be looking at that and thinking "aren't they the same thing?", and the simple answer is: "no, no they are not". You see, there are *two ways* of accessing values inside an array; you can *copy the data out* (`ldelem` in IL, which returns the *value* at the index), or you can access the data *directly inside the array* (`ldelema` in IL, which returns the *address* at the index). Ironically, we need an *address* to call the `DoSomething()` method, so for the `foreach` version, this actually becomes three steps: "copy out the value from the index, store the value to a local, get the address of a local" - instead of just "get the address of the index"; or in IL:

``` txt
IL_0006: ldloc.0 // the array
IL_0007: ldloc.1 // the index
IL_0008: ldelem SomeType // read value out from array:index
IL_000d: stloc.2 // store in local
IL_000e: ldloca.s 2 // get address of local
IL_0010: call instance void SomeType::DoSomething() // invoke method
```

vs

``` txt
IL_0004: ldarg.0 // the array
IL_0005: ldloc.0 // the index
IL_0006: ldelema SomeType // get address of array:index
IL_000b: call instance void SomeType::DoSomething() // invoke method
```

So by using `for` here, *not only* have we avoided copying the entire value, but we've dodged a few extra operations too! Nice. Depending on the size of the value being iterated (again, think "chunky struct" here), using `for` rather than `foreach` on an array (making sure you snapshot the value to elide bounds checks) can make a significant difference!

But: that's arrays, and we aren't always interested in arrays.

# But how does that help me outside arrays?

You might reasonably be thinking "great, but I don't want to just hand arrays around" - after all, they give me no ability to protect the data, and they're inconvenient for sizing - you can't add/remove, short of creating a second array and copying all the data. This is where C# 7.3 takes a huge flex; it introduces a few key things here:

- C# 7.0 adds `ref` return values from custom methods *including indexers*, and `ref` local values (so you don't need to use them immediately as a return value)
- C# 7.2 adds `ref readonly` to most places where `ref` might be used (and `readonly struct`, which often applies here)
- C# 7.3 adds `ref` (and `ref readonly`) as `foreach` L-values (i.e. the iterator value corresponding to `.Current`)

Note that with `ref`, the caller can *mutate* the data in-place, which is not always wanted; `ref readonly` signals that we don't want that to happen, hence why it is so often matched with `readonly struct` (to avoid having to make defensive copies of data), but as a warning: `readonly` is always a *guideline*, not a rule; a suitably motivated caller can convert a `ref readonly` to a `ref`, and can convert a `ReadOnlySpan<T>` to a `Span<T>`, and convert any of the above to an unmanaged `T*` pointer (at which point you can forget about all safety); this is not a bug, but a simple reality: *everything* is mutable if you try hard enough.

These languages features provide the building blocks - especially, but not exclusively, when combined with `Span<T>`; `Span<T>` (and the twin, `ReadOnlySpan<T>`) provide unified access to arbitrary data, which *could* be a slice of an array, but could be anything else - with the usual `.Length`, indexer (`this[int index]`) and `foreach` support you'd expect, with some additional compiler and JIT tricks (much like with arrays) to make them fly. Since spans are naturally optimized, one of the first things we can do - if we don't want to deal with arrays - is: deal with spans instead! This is sometimes a little hard to fit into existing systems without drastically refactoring the code, but more recently (.NET 5+), we get helper methods like [`CollectionsMarshal.AsSpan`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.collectionsmarshal.asspan), which gives us the sized span of the data underpinning a `List<T>`. This is only useful transiently (as any `Add`/`Remove` on the list will render the span broken - the length will be wrong, and it may now even point to the wrong array instance, if the list had to re-size the underlying data), but *when used correctly*, it allows us to access the data *in situ* rather than having to go via the indexer or iterator (both of which copy out the entire value at each position). For example:

``` c#
foreach (ref var tmp in CollectionsMarshal.AsSpan(someList))
{   // also works identically with "ref readonly var", since this is
    // a readonly struct
    tmp.DoSomething();
}
```

Our use of `ref var tmp` with `foreach` here means that the L-value (`tmp`) is a *managed pointer* to the data - not the data itself; we have avoided copying the overweight value-type, and called the value in-place.

If you look carefully, the indexer on a span is not `T this[int index]`, but rather: `ref T this[int index]` (or `ref readonly T this[int index]` for `ReadOnlySpan<T>`), so we can also use a `for` loop, and avoid copying the data at any point:

``` c#
var span = CollectionsMarshal.AsSpan(_list);
for (int i = 0; i < span.Length; i++)
{
    total += span[i].SomeValue;
}
```

# Generalizing this

Sometimes, spans aren't vialbe either - for whatever reason. The good news is: we can do the exact same thing with our own types, in two ways:

1. we can write our own types with an indexer that returns a `ref` or `ref readonly` managed pointer to the real data
2. we can write our own *iterator* types with a `ref` or `ref readonly` return value on `Current`; this won't satisfy `IEnumerator<T>`, but the *compiler* isn't limited to `IEnumerator<T>`, and if you're writing a custom iterator (rather than using a `yield return` iterator block): you're probably using a custom value-type iterator and *avoiding* the interface to make sure it never gets boxed accidentally, so: nothing is lost!

Purely for illustration (you wouldn't do this - you'd just use `ReadOnlySpan<T>`), a very simple custom iterator could be something like:

``` c#
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
```

which would provide `foreach` access *almost* as good as a direct span. If the caller uses `foreach (var tmp in ...)` rather than `foreach(ref readonly var tmp in ...)`, then the compiler will simply de-reference the value for the caller, which it *would have done anyway in the old-style `foreach`*, so: once again: no harm.

# Summary

In modern C#, we have a range of tricks that can help in certain niche scenarios relating to sequences of - in particular - value types. These scenarios don't apply to everyone, *and that's fine*. If you never need to use any of the above: **that's great**, and good luck to you. But when you *do* need them, they are *incredibly* powerful and versatile, and a valuable tool in the optimizer's toolbox.

The benchamrk code used for the table at the start of the post [is included here](https://github.com/mgravell/blog-preview/tree/main/RefForeach).

 



