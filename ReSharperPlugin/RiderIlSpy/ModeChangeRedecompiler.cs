using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiderIlSpy;

/// <summary>
/// Owns the debounced-cancellation choreography for rd-driven output mode changes.
/// One instance lives on <see cref="IlSpyExternalSourcesProvider"/>; the provider
/// wires <c>RiderIlSpyModel.Mode.Advise(lifetime, redecompiler.OnModeChanged)</c>
/// in its ctor, and this class takes it from there:
///  1. Cancel any in-flight redecompile from a prior mode change.
///  2. Sleep 75 ms to debounce rapid toggles of the IDE's mode selector.
///  3. Run the caller-supplied redecompile delegate with the new CancellationToken.
///  4. Fire the readyTick via the caller-supplied protocol-thread callback.
/// Extracted out of the provider so the provider stays focused on the
/// IExternalSourcesProvider contract — the reviewer flagged the inline
/// "debounced CTS coordinator spread over six members + two fields" as the
/// biggest design-coherence drag on that file. Takes log seams as plain delegates
/// (not <c>JetBrains.Util.ILogger</c>) so the SDK-free unit tests don't need to
/// stub the JetBrains logging surface.
/// </summary>
internal sealed class ModeChangeRedecompiler
{
    private readonly Func<CancellationToken, Task> myRedecompile;
    private readonly Action myFireReadyTick;
    private readonly Action<string> myLogVerbose;
    private readonly Action<Exception, string> myLogError;
    private readonly object myLock = new object();
    private CancellationTokenSource? myActiveCts;

    public ModeChangeRedecompiler(
        Func<CancellationToken, Task> redecompile,
        Action fireReadyTick,
        Action<string> logVerbose,
        Action<Exception, string> logError)
    {
        myRedecompile = redecompile;
        myFireReadyTick = fireReadyTick;
        myLogVerbose = logVerbose;
        myLogError = logError;
    }

    /// <summary>
    /// Adviser entry point — call from <c>RiderIlSpyModel.Mode.Advise</c>. The
    /// initial advise-fire from rd passes the current value, including null/empty
    /// during reconnect; those are no-ops here. Real mode-name strings spawn a
    /// background task that supersedes any in-flight redecompile.
    /// </summary>
    public void OnModeChanged(string? newMode)
    {
        if (string.IsNullOrEmpty(newMode)) return;
        CancellationTokenSource cts = SwapActiveCts();
        _ = Task.Run(() => RunAsync(cts));
    }

    private CancellationTokenSource SwapActiveCts()
    {
        CancellationTokenSource newCts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (myLock)
        {
            previous = myActiveCts;
            myActiveCts = newCts;
        }
        SafeCancelAndDispose(previous);
        return newCts;
    }

    private async Task RunAsync(CancellationTokenSource cts)
    {
        try
        {
            // 75 ms debounce is short enough to feel instant in the IDE but
            // long enough to coalesce rapid toggles of the mode selector
            // (UX testing showed users frequently click-click-click between
            // C# / IL / Mixed to compare output).
            await Task.Delay(75, cts.Token).ConfigureAwait(false);
            await myRedecompile(cts.Token).ConfigureAwait(false);
            myFireReadyTick();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer mode change; the next OnModeChanged will redrive the redecompile.
            // Logging at Verbose only — frequent during rapid mode toggles, not actionable.
            myLogVerbose("RiderIlSpy.ModeChangeRedecompiler: redecompile cancelled by newer mode change");
        }
        catch (Exception ex)
        {
            myLogError(ex, "RiderIlSpy.ModeChangeRedecompiler: redecompile failed");
        }
        finally
        {
            ClearActiveCtsIfMatches(cts);
        }
    }

    private void ClearActiveCtsIfMatches(CancellationTokenSource cts)
    {
        lock (myLock)
        {
            if (ReferenceEquals(myActiveCts, cts))
                myActiveCts = null;
        }
        SafeCancelAndDispose(cts);
    }

    // Cancel + dispose a CancellationTokenSource while tolerating the
    // ObjectDisposedException race with whichever task path got there first.
    private static void SafeCancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed by its task's finally */ }
        try { cts.Dispose(); } catch (ObjectDisposedException) { /* already disposed by a concurrent cancel-and-swap path */ }
    }
}
