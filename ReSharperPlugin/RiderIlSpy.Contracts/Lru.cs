using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// Generic thread-safe LRU cache. The order list tracks recency; the dictionary
/// holds the key→value mapping. <see cref="Track"/> is the only writer surface —
/// it inserts or replaces a key and marks it most-recently-used, evicting the
/// oldest when capacity is exceeded. Reads via <see cref="TryGet"/> do NOT
/// promote, so a passive lookup of an entry that's about to be evicted won't
/// keep it pinned indefinitely.
/// </summary>
/// <remarks>
/// All public methods are thread-safe via a single internal lock. The lock
/// dominates any read or write because every mutation touches both the
/// dictionary and the linked list; a ConcurrentDictionary alongside would add
/// no real concurrency, just two locks.
///
/// Extracted out of <see cref="TypeEntryCache"/> so the cache mechanics are
/// testable without dragging in JetBrains.Platform.ProjectModel (which the
/// concrete value type, <see cref="TypeDecompileEntry"/>, transitively
/// requires).
/// </remarks>
public sealed class Lru<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly int myCapacity;
    private readonly object myLock = new object();
    private readonly Dictionary<TKey, TValue> myEntries = new Dictionary<TKey, TValue>();
    private readonly LinkedList<TKey> myOrder = new LinkedList<TKey>();

    public Lru(int capacity)
    {
        myCapacity = capacity;
    }

    public int Capacity => myCapacity;

    public bool IsEmpty
    {
        get { lock (myLock) return myEntries.Count == 0; }
    }

    public int Count
    {
        get { lock (myLock) return myEntries.Count; }
    }

    public bool Contains(TKey key)
    {
        lock (myLock) return myEntries.ContainsKey(key);
    }

    /// <summary>
    /// Returns the value for <paramref name="key"/>, or <c>null</c> when
    /// missing. Does NOT promote the entry in LRU order — promotion is reserved
    /// for <see cref="Track"/>.
    /// </summary>
    public TValue? TryGet(TKey key)
    {
        lock (myLock)
        {
            myEntries.TryGetValue(key, out TValue? value);
            return value;
        }
    }

    /// <summary>
    /// Inserts or replaces the value for <paramref name="key"/> and marks it
    /// most-recently-used. Evicts oldest entries when capacity is exceeded.
    /// On replace, the existing key is removed from order before re-adding so
    /// the LRU position resets to MRU.
    /// </summary>
    public void Track(TKey key, TValue value)
    {
        lock (myLock)
        {
            if (myEntries.ContainsKey(key))
            {
                myOrder.Remove(key);
            }
            myEntries[key] = value;
            myOrder.AddLast(key);
            while (myOrder.Count > myCapacity)
            {
                LinkedListNode<TKey>? first = myOrder.First;
                if (first == null) break;
                myOrder.RemoveFirst();
                myEntries.Remove(first.Value);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of every (key, value) pair currently in the cache.
    /// The list is detached from the cache, so callers can iterate without
    /// holding the lock or worrying about concurrent mutations.
    /// </summary>
    public IReadOnlyList<KeyValuePair<TKey, TValue>> Snapshot()
    {
        lock (myLock)
        {
            List<KeyValuePair<TKey, TValue>> snapshot = new List<KeyValuePair<TKey, TValue>>(myEntries.Count);
            foreach (KeyValuePair<TKey, TValue> kv in myEntries)
            {
                snapshot.Add(kv);
            }
            return snapshot;
        }
    }
}
