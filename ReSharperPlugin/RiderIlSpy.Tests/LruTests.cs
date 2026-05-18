using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RiderIlSpy.Tests;

// Pins the LRU mechanics extracted from TypeEntryCache: capacity eviction
// drops the LRU key, Track on an existing key replaces value and resets to
// MRU, TryGet does NOT promote (so passive reads don't pin entries), and
// Snapshot is stable under concurrent mutation. These tests use plain
// strings as TKey/TValue so they exercise the generic engine without
// dragging JetBrains.Platform.ProjectModel into the test project — which
// is exactly why TypeEntryCache delegates to Lru<TKey,TValue>.
public class LruTests
{
    [Fact]
    public void Empty_cache_reports_empty()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 4);
        Assert.True(lru.IsEmpty);
        Assert.Equal(0, lru.Count);
        Assert.False(lru.Contains("missing"));
        Assert.Null(lru.TryGet("missing"));
    }

    [Fact]
    public void Track_inserts_value()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 4);
        lru.Track("k1", "v1");
        Assert.False(lru.IsEmpty);
        Assert.Equal(1, lru.Count);
        Assert.True(lru.Contains("k1"));
        Assert.Equal("v1", lru.TryGet("k1"));
    }

    [Fact]
    public void Track_existing_key_replaces_value()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 4);
        lru.Track("k1", "first");
        lru.Track("k1", "second");
        Assert.Equal(1, lru.Count);
        Assert.Equal("second", lru.TryGet("k1"));
    }

    [Fact]
    public void Eviction_at_capacity_drops_lru_key()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 3);
        lru.Track("k1", "v1");
        lru.Track("k2", "v2");
        lru.Track("k3", "v3");
        lru.Track("k4", "v4");
        // k1 was the least-recently tracked, so it should be evicted.
        Assert.False(lru.Contains("k1"));
        Assert.True(lru.Contains("k2"));
        Assert.True(lru.Contains("k3"));
        Assert.True(lru.Contains("k4"));
        Assert.Equal(3, lru.Count);
    }

    [Fact]
    public void Track_existing_key_promotes_to_mru()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 3);
        lru.Track("k1", "v1");
        lru.Track("k2", "v2");
        lru.Track("k3", "v3");
        // Re-track k1 — should move it to MRU position.
        lru.Track("k1", "v1-updated");
        // Now adding k4 should evict the LRU, which is k2 (not k1, since k1 was promoted).
        lru.Track("k4", "v4");
        Assert.True(lru.Contains("k1"));
        Assert.Equal("v1-updated", lru.TryGet("k1"));
        Assert.False(lru.Contains("k2"));
        Assert.True(lru.Contains("k3"));
        Assert.True(lru.Contains("k4"));
    }

    [Fact]
    public void TryGet_does_not_promote()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 3);
        lru.Track("k1", "v1");
        lru.Track("k2", "v2");
        lru.Track("k3", "v3");
        // Passive read of k1 should NOT pin it.
        Assert.Equal("v1", lru.TryGet("k1"));
        // Adding k4 should still evict k1 (because the read didn't promote it).
        lru.Track("k4", "v4");
        Assert.False(lru.Contains("k1"));
    }

    [Fact]
    public void Snapshot_returns_detached_copy()
    {
        Lru<string, string> lru = new Lru<string, string>(capacity: 4);
        lru.Track("k1", "v1");
        lru.Track("k2", "v2");

        IReadOnlyList<KeyValuePair<string, string>> snapshot = lru.Snapshot();
        Assert.Equal(2, snapshot.Count);

        // Mutate after snapshot — snapshot should be unaffected.
        lru.Track("k3", "v3");
        Assert.Equal(2, snapshot.Count);
        Assert.DoesNotContain(snapshot, kv => kv.Key == "k3");
    }

    [Fact]
    public async Task Snapshot_is_stable_under_concurrent_mutation()
    {
        // Pins the thread-safety claim: a Snapshot taken while other threads
        // are racing Track calls must return a consistent list (no torn
        // entries, no half-written dictionary state). If the lock were
        // missing on one side, this test would surface as a sporadic
        // exception or a count mismatch.
        Lru<string, string> lru = new Lru<string, string>(capacity: 1000);
        for (int i = 0; i < 100; i++)
        {
            lru.Track("k" + i, "v" + i);
        }

        using ManualResetEventSlim start = new ManualResetEventSlim(false);
        Task[] writers = new Task[4];
        for (int t = 0; t < writers.Length; t++)
        {
            int threadId = t;
            writers[t] = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 200; i++)
                {
                    lru.Track($"t{threadId}_k{i}", $"v{i}");
                }
            });
        }

        IReadOnlyList<KeyValuePair<string, string>>[] snapshots = new IReadOnlyList<KeyValuePair<string, string>>[10];
        Task snapper = Task.Run(() =>
        {
            start.Wait();
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = lru.Snapshot();
            }
        });

        start.Set();
        await Task.WhenAll(writers.Concat(new[] { snapper }).ToArray());

        // No null-or-empty snapshots, and each snapshot's keys are unique.
        foreach (IReadOnlyList<KeyValuePair<string, string>> snap in snapshots)
        {
            Assert.NotNull(snap);
            HashSet<string> seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> kv in snap)
            {
                Assert.True(seen.Add(kv.Key), $"snapshot contained duplicate key {kv.Key}");
            }
        }
    }
}
