using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// LRU cache of <see cref="TypeDecompileEntry"/> records keyed by their moniker.
/// Thin facade over the generic <see cref="Lru{TKey,TValue}"/> engine — the
/// mechanics (eviction, MRU-on-track, snapshot stability) live there and are
/// unit-tested independently of <see cref="TypeDecompileEntry"/>, which would
/// otherwise drag JetBrains.Platform.ProjectModel into the test project.
/// </summary>
public sealed class TypeEntryCache
{
    /// <summary>Maximum number of entries before LRU eviction kicks in.</summary>
    public const int Capacity = 512;

    private readonly Lru<string, TypeDecompileEntry> myLru = new Lru<string, TypeDecompileEntry>(Capacity);

    public bool IsEmpty => myLru.IsEmpty;

    public bool Contains(string moniker) => myLru.Contains(moniker);

    /// <summary>
    /// Returns the entry for <paramref name="moniker"/>, or <c>null</c> when
    /// missing. Does NOT promote the entry in LRU order — promotion is reserved
    /// for <see cref="Track"/>, the explicit "this entry is still live" signal.
    /// </summary>
    public TypeDecompileEntry? TryGet(string moniker) => myLru.TryGet(moniker);

    /// <summary>
    /// Inserts or replaces <paramref name="moniker"/>'s entry and marks it
    /// most-recently-used. Evicts the oldest entry when capacity is exceeded.
    /// </summary>
    public void Track(string moniker, TypeDecompileEntry entry) => myLru.Track(moniker, entry);

    /// <summary>
    /// Returns a snapshot of every (moniker, entry) pair currently in the cache.
    /// Snapshot semantics — the returned list is detached from the cache, so
    /// callers can iterate without holding the lock or worrying about concurrent
    /// mutations during a long redecompile pass.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, TypeDecompileEntry>> Snapshot() => myLru.Snapshot();
}
