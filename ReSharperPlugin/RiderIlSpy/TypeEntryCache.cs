using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// LRU cache of <see cref="TypeDecompileEntry"/> records keyed by their moniker.
/// Caps at <see cref="Capacity"/> entries — oldest evicted when full. Extracted
/// from <see cref="IlSpyExternalSourcesProvider"/> so the cache is one named
/// concern with its own thread-safety story instead of three fields, one lock,
/// and one const scattered across the provider.
/// </summary>
/// <remarks>
/// All public methods are thread-safe via a single internal lock. The earlier
/// design used a ConcurrentDictionary alongside the lock, but every write also
/// touched the LinkedList (which is not thread-safe), so the ConcurrentDictionary
/// added no real concurrency — the lock dominated. Folding to a plain Dictionary
/// makes the contract obvious: every read and write takes the lock, period.
///
/// No direct unit tests: <see cref="TypeDecompileEntry"/> embeds JetBrains'
/// <c>IAssembly</c>, which isn't transitively available in the test project
/// (the test csproj only ProjectReferences RiderIlSpy.csproj, not the heavy
/// JetBrains.Platform.ProjectModel assembly). Pulling JetBrains references
/// into the test project just to construct one record would balloon test
/// build time. The cache mechanics are plain Dictionary + LinkedList LRU
/// (~80 LOC); any bug surfaces immediately on the first navigation in a
/// running Rider session. If this class grows new behaviour, factor a generic
/// <c>Lru&lt;TKey, TValue&gt;</c> engine out and test that.
/// </remarks>
public sealed class TypeEntryCache
{
    /// <summary>Maximum number of entries before LRU eviction kicks in.</summary>
    public const int Capacity = 512;

    private readonly object myLock = new object();
    private readonly Dictionary<string, TypeDecompileEntry> myEntries = new Dictionary<string, TypeDecompileEntry>();
    private readonly LinkedList<string> myOrder = new LinkedList<string>();

    /// <summary>True when no entries have been tracked yet (or all evicted).</summary>
    public bool IsEmpty
    {
        get { lock (myLock) return myEntries.Count == 0; }
    }

    /// <summary>True if <paramref name="moniker"/> is currently in the cache.</summary>
    public bool Contains(string moniker)
    {
        lock (myLock) return myEntries.ContainsKey(moniker);
    }

    /// <summary>
    /// Returns the entry for <paramref name="moniker"/>, or <c>null</c> when
    /// missing. Does NOT promote the entry in LRU order — promotion is reserved
    /// for <see cref="Track"/>, the explicit "this entry is still live" signal.
    /// </summary>
    public TypeDecompileEntry? TryGet(string moniker)
    {
        lock (myLock)
        {
            myEntries.TryGetValue(moniker, out TypeDecompileEntry? entry);
            return entry;
        }
    }

    /// <summary>
    /// Inserts or replaces <paramref name="moniker"/>'s entry and marks it
    /// most-recently-used. Evicts the oldest entry when capacity is exceeded.
    /// </summary>
    public void Track(string moniker, TypeDecompileEntry entry)
    {
        lock (myLock)
        {
            if (myEntries.ContainsKey(moniker))
            {
                myOrder.Remove(moniker);
            }
            myEntries[moniker] = entry;
            myOrder.AddLast(moniker);
            while (myOrder.Count > Capacity)
            {
                LinkedListNode<string>? first = myOrder.First;
                if (first == null) break;
                myOrder.RemoveFirst();
                myEntries.Remove(first.Value);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of every (moniker, entry) pair currently in the cache.
    /// Snapshot semantics — the returned list is detached from the cache, so
    /// callers can iterate without holding the lock or worrying about concurrent
    /// mutations during a long redecompile pass.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, TypeDecompileEntry>> Snapshot()
    {
        lock (myLock)
        {
            List<KeyValuePair<string, TypeDecompileEntry>> snapshot = new List<KeyValuePair<string, TypeDecompileEntry>>(myEntries.Count);
            foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntries)
            {
                snapshot.Add(kv);
            }
            return snapshot;
        }
    }
}
