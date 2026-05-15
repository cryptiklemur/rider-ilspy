package com.cryptiklemur.riderilspy

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
     * Push the current mode to the backend. Safe to call from any thread; rd
     * properties take care of marshalling onto the protocol thread.
     */
    fun setMode(mode: IlSpyMode) {
        try {
            val model = project.solution.riderIlSpyModel
            model.mode.set(mode.backendName)
        } catch (error: Exception) {
            // The protocol can be unbound briefly during solution open/close;
            // log so users can diagnose "mode toggle did nothing" rather than
            // failing silently like the prior file-based path did.
            log.warn("Failed to push ILSpy mode to backend for project ${project.name}", error)
        }
    }

    /**
     * Subscribe to ready-tick notifications from the backend. [onReady] runs
     * for every fire that arrives during [lifetime] and is automatically
     * unsubscribed when the lifetime ends.
     */
    fun adviseReady(lifetime: Lifetime, onReady: () -> Unit) {
        try {
            project.solution.riderIlSpyModel.readyTick.advise(lifetime) { onReady() }
        } catch (error: Exception) {
            log.warn("Failed to advise on ILSpy readyTick for project ${project.name}", error)
        }
    }

    init {
        // Push the persisted mode as soon as the protocol is bound so the
        // backend doesn't start with a stale default. Done on a pooled thread
        // because solution-bind ordering is not guaranteed at service init.
        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val mode = IlSpyFrontendSettings.getInstance().mode
                project.solution.riderIlSpyModel.mode.set(mode.backendName)
            } catch (error: Exception) {
                log.info("Deferred initial ILSpy mode push (protocol not bound yet): ${error.message}")
            }
        }
    }

    companion object {
        fun getInstance(project: Project): IlSpyProtocolHost =
            project.getService(IlSpyProtocolHost::class.java)
    }
}
