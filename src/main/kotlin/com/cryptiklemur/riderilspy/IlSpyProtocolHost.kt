package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.internals.IlSpyFrontendSettings
import com.cryptiklemur.riderilspy.internals.IlSpyMode
import com.cryptiklemur.riderilspy.internals.attemptWithBackoff
import com.cryptiklemur.riderilspy.internals.initialModePushSchedule
import com.cryptiklemur.riderilspy.internals.runOnEdt
import com.cryptiklemur.riderilspy.model.riderIlSpyModel
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.jetbrains.rd.platform.util.idea.LifetimedService
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rider.projectView.solution

/**
 * Project-scoped bridge between the user-facing settings/UI and the generated
 * rd protocol model that talks to the ReSharper backend.
 *
 * Owns a [LifetimedService] base so all signal subscriptions get torn
 * down when the project closes; pushes the persisted mode to the backend once
 * the solution attaches, and exposes hooks for the status bar widget to keep
 * the two in sync after that.
 */
@Service(Service.Level.PROJECT)
class IlSpyProtocolHost(private val project: Project) : LifetimedService() {

    private val log: Logger = Logger.getInstance(IlSpyProtocolHost::class.java)

    /**
     * Push the current mode to the backend. Safe to call from any thread; this
     * marshals onto the EDT because RdOptionalProperty.set asserts the protocol
     * dispatcher thread and throws "Wrong thread" otherwise (rd does not
     * auto-marshal here despite the JVM-side API surface suggesting it).
     *
     * Single try/catch (no retry) intentional: by the time this is reachable
     * from the status-bar widget, [pushInitialModeWithRetry] has already won
     * the protocol-binding race during service init, so steady-state callers
     * almost never see the unbound exception. Retry policy lives there.
     */
    fun setMode(mode: IlSpyMode) {
        try {
            setModeOnEdt(mode.backendName)
        } catch (error: Exception) {
            // The rd protocol can be briefly unbound during solution open/close;
            // without this log the toggle would silently no-op and users would
            // have nothing to grep when reporting "mode switch did nothing".
            // Info level — same protocol-binding race as [pushInitialModeWithRetry];
            // see that method for the retry policy at init time.
            log.info("Deferred ILSpy mode push (protocol not bound yet)", error)
        }
    }

    /**
     * Set `riderIlSpyModel.mode` on the EDT (the rd dispatcher thread). The
     * dispatch decision lives in [runOnEdt] so it's testable without an IDE
     * harness; here we just bind it to the live IntelliJ application.
     */
    private fun setModeOnEdt(backendName: String) {
        val app = ApplicationManager.getApplication()
        runOnEdt(
            isDispatchThread = { app.isDispatchThread },
            invokeAndWait = { app.invokeAndWait(it) },
        ) {
            project.solution.riderIlSpyModel.mode.set(backendName)
        }
    }

    /**
     * Subscribe to ready-tick notifications from the backend. [onReady] runs
     * for every fire that arrives during [lifetime] and is automatically
     * unsubscribed when the lifetime ends.
     *
     * Like [setMode], no retry: advise calls originate post-solution-bind
     * (after [pushInitialModeWithRetry] has run on the pooled thread), so
     * the binding race is already settled. See [pushInitialModeWithRetry].
     */
    fun adviseReady(lifetime: Lifetime, onReady: () -> Unit) {
        try {
            project.solution.riderIlSpyModel.readyTick.advise(lifetime) { onReady() }
        } catch (error: Exception) {
            // Same protocol-binding race as setMode and the init-time push; the
            // retried code path is [pushInitialModeWithRetry], steady-state is
            // here — info only because retry isn't actionable after init wins.
            log.info("Deferred ILSpy readyTick advise (protocol not bound yet)", error)
        }
    }

    init {
        // Push the persisted mode as soon as the protocol is bound so the
        // backend doesn't start with a stale default. Solution-bind ordering
        // is not guaranteed at service init, so retry with backoff against the
        // unbound-protocol race rather than fire-and-forget exactly once.
        ApplicationManager.getApplication().executeOnPooledThread {
            val mode = IlSpyFrontendSettings.getInstance().mode
            pushInitialModeWithRetry(mode)
        }
    }

    private fun pushInitialModeWithRetry(mode: IlSpyMode) {
        // Schedule + termination logic lives in [attemptWithBackoff] +
        // [initialModePushSchedule] so they're unit-testable without an IDE.
        // Each attempt marshals onto the EDT because RdOptionalProperty.set
        // asserts the protocol dispatcher thread; calling from this pooled
        // executor without invokeAndWait crashes with "Wrong thread".
        var lastError: Exception? = null
        val ok = attemptWithBackoff(
            schedule = initialModePushSchedule,
            isDisposed = { project.isDisposed },
        ) {
            try {
                setModeOnEdt(mode.backendName)
                true
            } catch (error: Exception) {
                lastError = error
                false
            }
        }
        if (!ok && lastError != null && !project.isDisposed) {
            log.info("Gave up initial ILSpy mode push after ${initialModePushSchedule.size} attempts (protocol still unbound)", lastError)
        }
    }

    companion object {
        fun getInstance(project: Project): IlSpyProtocolHost =
            project.getService(IlSpyProtocolHost::class.java)
    }
}
