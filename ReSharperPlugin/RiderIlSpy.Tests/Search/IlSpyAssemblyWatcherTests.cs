using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Lifetimes;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpyAssemblyWatcherTests
{
    private static readonly TimeSpan FastDebounce = TimeSpan.FromMilliseconds(50);
    // Generous wall-clock ceiling for the TaskCompletionSource handshake. Tests
    // wait against this, but the actual callback fires after FastDebounce (~50ms)
    // — so a healthy run consumes ~50ms and only a stuck/regressed run consumes
    // the full ceiling before failing.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    // Pins the debounce contract: 10 rapid events on the same path collapse to
    // exactly one callback. Uses a TaskCompletionSource handshake instead of
    // Thread.Sleep, which makes the test wall-clock-insensitive — under CI
    // scheduling pressure the previous Thread.Sleep(300) against a 100ms debounce
    // would flake (debounce timer could be scheduled later than the sleep).
    [Fact]
    public async Task Debounces_Rapid_Events()
    {
        int fires = 0;
        TaskCompletionSource<bool> fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LifetimeDefinition lt = new LifetimeDefinition();
        using IlSpyAssemblyWatcher watcher = new IlSpyAssemblyWatcher(
            lt.Lifetime,
            _ =>
            {
                Interlocked.Increment(ref fires);
                fired.TrySetResult(true);
            },
            FastDebounce);

        for (int i = 0; i < 10; i++) watcher.OnFileChanged("/x/a.dll");

        Task winner = await Task.WhenAny(fired.Task, Task.Delay(HandshakeTimeout));
        Assert.Same(fired.Task, winner);
        // Give the dispatcher a brief settle window after the first fire to
        // confirm no second callback slips through from the cancelled timers.
        await Task.Delay(FastDebounce + FastDebounce);
        Assert.Equal(1, fires);
    }

    // Two distinct paths interleaved into the same watcher must each get their
    // own debounced callback — the per-path CancellationTokenSource dictionary
    // (IlSpyAssemblyWatcher.cs:15) is what makes that work, and a regression
    // collapsing the dict to a single CTS would silently break the indexer's
    // "rebuild only the changed assembly" promise.
    [Fact]
    public async Task Distinct_Paths_Debounce_Independently()
    {
        List<string> firedPaths = new List<string>();
        object firedLock = new object();
        TaskCompletionSource<bool> bothFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LifetimeDefinition lt = new LifetimeDefinition();
        using IlSpyAssemblyWatcher watcher = new IlSpyAssemblyWatcher(
            lt.Lifetime,
            path =>
            {
                lock (firedLock)
                {
                    firedPaths.Add(path);
                    if (firedPaths.Count >= 2) bothFired.TrySetResult(true);
                }
            },
            FastDebounce);

        watcher.OnFileChanged("/x/a.dll");
        watcher.OnFileChanged("/x/b.dll");
        watcher.OnFileChanged("/x/a.dll");
        watcher.OnFileChanged("/x/b.dll");

        Task winner = await Task.WhenAny(bothFired.Task, Task.Delay(HandshakeTimeout));
        Assert.Same(bothFired.Task, winner);
        lock (firedLock)
        {
            Assert.Equal(2, firedPaths.Count);
            Assert.Contains("/x/a.dll", firedPaths);
            Assert.Contains("/x/b.dll", firedPaths);
        }
    }

    // Dispose during a pending debounce must cancel the in-flight Task.Delay
    // — IlSpyAssemblyWatcher.cs:47-58 walks every CTS and cancels it, so the
    // callback should not fire after Dispose returns. This pins the race that
    // the dictionary's lock guards against: an old timer firing past Dispose
    // would call back into a disposed shell.
    [Fact]
    public void Dispose_Cancels_Pending_Callbacks()
    {
        int fires = 0;
        using LifetimeDefinition lt = new LifetimeDefinition();
        IlSpyAssemblyWatcher watcher = new IlSpyAssemblyWatcher(
            lt.Lifetime,
            _ => Interlocked.Increment(ref fires),
            TimeSpan.FromMilliseconds(500));

        watcher.OnFileChanged("/x/a.dll");
        watcher.OnFileChanged("/x/b.dll");
        watcher.Dispose();

        // Wait longer than the debounce — if Dispose did not cancel, both
        // timers would now fire. The handshake-style "wait for a thing that
        // shouldn't happen" needs a real time window; we keep it short
        // (debounce + margin) because this is the only sleep in the file.
        Thread.Sleep(TimeSpan.FromMilliseconds(750));
        Assert.Equal(0, fires);
    }
}
