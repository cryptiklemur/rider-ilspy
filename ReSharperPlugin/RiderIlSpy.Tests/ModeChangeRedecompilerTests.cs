using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Unit tests for the extracted mode-change cancellation choreography. Covers
/// the contract that previously lived inline in IlSpyExternalSourcesProvider
/// across six methods + two fields — extracting it to a SDK-free type makes
/// the cancel/supersede semantics directly testable. Logging is taken as plain
/// delegates so these tests don't need to stub JetBrains.Util.ILogger.
/// </summary>
public class ModeChangeRedecompilerTests
{
    private static ModeChangeRedecompiler Make(
        Func<CancellationToken, Task> redecompile,
        Action? fireReadyTick = null,
        Action<string>? logVerbose = null,
        Action<Exception, string>? logError = null) =>
        new ModeChangeRedecompiler(
            redecompile,
            fireReadyTick ?? (() => { }),
            logVerbose ?? (_ => { }),
            logError ?? ((_, _) => { }));

    [Fact]
    public void OnModeChanged_with_null_is_a_noop()
    {
        // Initial advise-fire from rd passes null/empty during reconnect — the
        // type must reject those silently so the redecompile delegate isn't
        // invoked. Without the early-return the worker would spin up against
        // an unset mode and either no-op or do duplicate work.
        int redecompileCalls = 0;
        ModeChangeRedecompiler subject = Make(_ =>
        {
            Interlocked.Increment(ref redecompileCalls);
            return Task.CompletedTask;
        });
        subject.OnModeChanged(null);
        subject.OnModeChanged(string.Empty);
        Thread.Sleep(150);
        Assert.Equal(0, redecompileCalls);
    }

    [Fact]
    public async Task OnModeChanged_invokes_redecompile_then_fireReadyTick()
    {
        TaskCompletionSource<bool> done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int redecompileCalls = 0;
        int readyTickCalls = 0;
        ModeChangeRedecompiler subject = Make(
            redecompile: _ =>
            {
                Interlocked.Increment(ref redecompileCalls);
                return Task.CompletedTask;
            },
            fireReadyTick: () =>
            {
                Interlocked.Increment(ref readyTickCalls);
                done.TrySetResult(true);
            });
        subject.OnModeChanged("CSharp");
        await Task.WhenAny(done.Task, Task.Delay(2000));
        Assert.Equal(1, redecompileCalls);
        Assert.Equal(1, readyTickCalls);
    }

    [Fact]
    public async Task A_second_mode_change_cancels_the_first_redecompiles_token()
    {
        // The cancel-supersede contract: when a second OnModeChanged arrives
        // while the first is in-flight, the first's token must observe
        // cancellation. Without this, rapid mode toggles would stack redecompile
        // workers all racing to write the same cache entry.
        TaskCompletionSource<bool> firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callIndex = 0;
        bool firstSawCancellation = false;
        ModeChangeRedecompiler subject = Make(
            redecompile: async ct =>
            {
                int idx = Interlocked.Increment(ref callIndex);
                if (idx == 1)
                {
                    firstStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        firstSawCancellation = true;
                        throw;
                    }
                }
                else
                {
                    await Task.CompletedTask.ConfigureAwait(false);
                }
            },
            fireReadyTick: () =>
            {
                if (Volatile.Read(ref callIndex) >= 2)
                    secondDone.TrySetResult(true);
            });
        subject.OnModeChanged("CSharp");
        await Task.WhenAny(firstStarted.Task, Task.Delay(2000));
        subject.OnModeChanged("IL");
        await Task.WhenAny(secondDone.Task, Task.Delay(2000));
        Assert.True(firstSawCancellation, "first redecompile must observe cancellation when superseded");
        Assert.Equal(2, callIndex);
    }

    [Fact]
    public async Task Redecompile_exception_is_logged_not_propagated()
    {
        // The redecompile task runs detached via Task.Run — an unhandled
        // exception there would land on TaskScheduler.UnobservedTaskException
        // (best case) or crash a worker thread (worst case). The catch in
        // RunAsync must funnel anything not-OperationCanceledException into the
        // error-log delegate.
        TaskCompletionSource<Exception> errored = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        ModeChangeRedecompiler subject = Make(
            redecompile: _ => throw new InvalidOperationException("boom"),
            logError: (ex, _) => errored.TrySetResult(ex));
        subject.OnModeChanged("CSharp");
        Task<Exception> result = errored.Task;
        Task winner = await Task.WhenAny(result, Task.Delay(2000));
        Assert.Same(result, winner);
        Exception captured = await result;
        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public async Task Cancellation_during_debounce_delay_does_not_invoke_redecompile()
    {
        // The 75 ms Task.Delay between OnModeChanged and the redecompile body
        // is the debounce window. A second OnModeChanged inside that window
        // must cancel the first BEFORE it even calls the redecompile delegate
        // — otherwise rapid clicks would still trigger redundant work even
        // though the cancel-supersede protocol fires.
        int callIndex = 0;
        TaskCompletionSource<bool> secondDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ModeChangeRedecompiler subject = Make(
            redecompile: _ =>
            {
                Interlocked.Increment(ref callIndex);
                return Task.CompletedTask;
            },
            fireReadyTick: () =>
            {
                if (Volatile.Read(ref callIndex) >= 1)
                    secondDone.TrySetResult(true);
            });
        subject.OnModeChanged("CSharp");
        // Supersede before the 75 ms delay elapses. The first redecompile body
        // should never run.
        subject.OnModeChanged("IL");
        await Task.WhenAny(secondDone.Task, Task.Delay(2000));
        Assert.Equal(1, callIndex);
    }
}
