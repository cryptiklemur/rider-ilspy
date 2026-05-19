package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.cryptiklemur.riderilspy.internals.IlSpyFrontendSettings
import com.cryptiklemur.riderilspy.internals.IlSpyMode
import com.cryptiklemur.riderilspy.internals.isIlSpyDecompiledPath
import com.cryptiklemur.riderilspy.search.IlSpySearchClientService
import com.cryptiklemur.riderilspy.search.IlSpySearchIndexStateSnapshot
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.popup.JBPopup
import com.intellij.openapi.ui.popup.JBPopupFactory
import com.intellij.openapi.ui.popup.PopupStep
import com.intellij.openapi.ui.popup.util.BaseListPopupStep
import com.intellij.openapi.vfs.VfsUtil
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.WindowManager
import com.jetbrains.rd.util.lifetime.LifetimeDefinition

class IlSpyModeStatusBarWidget(private val project: Project) :
    StatusBarWidget,
    StatusBarWidget.MultipleTextValuesPresentation {

    private var statusBar: StatusBar? = null
    private val installLifetime = LifetimeDefinition()
    private var lastIndexState: IlSpySearchIndexStateSnapshot? = null

    override fun ID(): String = IlSpyModeStatusBarWidgetFactory.WIDGET_ID

    override fun getPresentation(): StatusBarWidget.WidgetPresentation = this

    override fun install(statusBar: StatusBar) {
        this.statusBar = statusBar
        // Backend fires readyTick after each re-decompile completes; bounce a
        // VFS refresh on the EDT so the in-memory decompiled file editors
        // re-read from disk and pick up the new content.
        IlSpyProtocolHost.getInstance(project).adviseReady(installLifetime.lifetime) {
            refreshOpenIlSpyFiles()
        }

        val clientService = project.getService(IlSpySearchClientService::class.java)
        clientService.client.indexState.advise(installLifetime.lifetime) { snapshot ->
            lastIndexState = snapshot
            refreshStatusBar()
        }
    }

    override fun dispose() {
        statusBar = null
        installLifetime.terminate()
    }

    override fun getTooltipText(): String =
        RiderIlSpyBundle.message("statusbar.mode.tooltip")

    override fun getSelectedValue(): String {
        val settings = IlSpyFrontendSettings.getInstance()
        return renderStatusBarLabel(
            enabled = settings.enabled,
            mode = settings.mode,
            indexState = lastIndexState,
            bundle = { key, args -> RiderIlSpyBundle.message(key, *args) },
        )
    }

    override fun getPopup(): JBPopup {
        val settings = IlSpyFrontendSettings.getInstance()
        val currentEnabled = settings.enabled
        val currentMode = settings.mode
        // Order: modes first, then Off as the dedicated disable entry.
        // Keeping Off at the end of the list (rather than the top) means
        // the common case — switching between modes — stays in muscle
        // memory; users disabling the plugin are doing something less
        // common and don't mind the extra row.
        val choices: List<IlSpyStatusBarChoice> =
            IlSpyMode.entries.map { IlSpyStatusBarChoice.ModeChoice(it) } + IlSpyStatusBarChoice.Off
        val currentSuffix = RiderIlSpyBundle.message("statusbar.mode.popup.current_suffix")
        val offLabel = RiderIlSpyBundle.message("statusbar.mode.popup.off")
        val step = object : BaseListPopupStep<IlSpyStatusBarChoice>(
            RiderIlSpyBundle.message("statusbar.mode.popup_title"),
            choices,
        ) {
            override fun getTextFor(value: IlSpyStatusBarChoice): String = when (value) {
                is IlSpyStatusBarChoice.ModeChoice -> {
                    val base = value.mode.displayName
                    if (currentEnabled && value.mode == currentMode) "$base$currentSuffix" else base
                }
                is IlSpyStatusBarChoice.Off ->
                    if (!currentEnabled) "$offLabel$currentSuffix" else offLabel
            }

            override fun onChosen(selectedValue: IlSpyStatusBarChoice, finalChoice: Boolean): PopupStep<*>? {
                val transition = decideStatusBarTransition(currentEnabled, currentMode, selectedValue)
                val host = IlSpyProtocolHost.getInstance(project)
                transition.applyEnabled?.let {
                    // Persist locally so the choice survives restart, then
                    // push to the backend over rd so the C# provider stops
                    // (or resumes) intercepting navigation on the next request.
                    settings.enabled = it
                    host.setEnabled(it)
                }
                transition.applyMode?.let {
                    settings.mode = it
                    host.setMode(it)
                }
                if (transition.applyEnabled != null || transition.applyMode != null) {
                    refreshStatusBar()
                }
                return FINAL_CHOICE
            }
        }
        return JBPopupFactory.getInstance().createListPopup(step)
    }

    private fun refreshStatusBar() {
        val sb = statusBar ?: WindowManager.getInstance().getStatusBar(project) ?: return
        sb.updateWidget(ID())
    }

    // The backend fires readyTick when its re-decompile pass completes and
    // IlSpyProtocolHost forwards the event here with a single VFS refresh.
    // The refresh itself is dispatched onto the EDT via invokeLater, so callers
    // should treat this as "schedule a refresh", not a synchronous reload.
    private fun refreshOpenIlSpyFiles() {
        if (project.isDisposed) return
        val fem = FileEditorManager.getInstance(project)
        val targets: List<VirtualFile> = fem.openFiles.filter { isIlSpyDecompiledPath(it.path) }
        if (targets.isEmpty()) return
        ApplicationManager.getApplication().invokeLater {
            if (!project.isDisposed) {
                VfsUtil.markDirtyAndRefresh(true, false, false, *targets.toTypedArray())
            }
        }
    }
}

// Top-level so it's unit-testable without an IDE harness — same pattern as
// resourceKindToAction, shouldServe, attemptOnce. The bundle lambda is injected
// so tests can pass a fake bundler and assert against the chosen key, instead
// of needing the real RiderIlSpyBundle on the test classpath.
//
// When `enabled` is false the plugin is fully disabled; the status bar shows
// the "Off" label and the index-state branches are skipped (the index isn't
// relevant when ILSpy isn't intercepting anything).
fun renderStatusBarLabel(
    enabled: Boolean,
    mode: IlSpyMode,
    indexState: IlSpySearchIndexStateSnapshot?,
    bundle: (String, Array<out Any>) -> String,
): String {
    if (!enabled) return bundle("statusbar.mode.label_off", emptyArray())
    val base = bundle("statusbar.mode.label_base", arrayOf<Any>(mode.displayName))
    return when (indexState?.phase) {
        "Building" -> bundle(
            "statusbar.mode.label_indexing",
            arrayOf<Any>(base, indexState.indexed, indexState.total),
        )
        "Failed" -> bundle("statusbar.mode.label_failed", arrayOf<Any>(base))
        else -> base
    }
}


/**
 * Choices the status-bar widget offers in its popup. Off is a singleton
 * because there's only one disabled state; ModeChoice wraps an [IlSpyMode]
 * because each mode is a distinct selectable entry.
 *
 * Kept at top-level so [decideStatusBarTransition] (the pure decision
 * helper) and its tests don't need to instantiate the IDE-bound widget.
 */
sealed class IlSpyStatusBarChoice {
    object Off : IlSpyStatusBarChoice()
    data class ModeChoice(val mode: IlSpyMode) : IlSpyStatusBarChoice()
}

/**
 * What should change after a popup selection. A null field means "leave
 * that axis alone". The widget's [IlSpyModeStatusBarWidget.getPopup] handler
 * applies non-null values to both [IlSpyFrontendSettings] and the
 * [IlSpyProtocolHost] (push to backend); a transition with both fields
 * null is a no-op (no settings write, no protocol push, no widget refresh).
 */
data class IlSpyStatusBarTransition(
    val applyEnabled: Boolean?,
    val applyMode: IlSpyMode?,
)

/**
 * Pure decision for what a popup selection should change. Lives at top
 * level so it's unit-testable without an IDE harness.
 *
 * Truth table:
 *  - Off while enabled  -> applyEnabled=false, applyMode=null
 *  - Off while disabled -> no-op (both null)
 *  - Mode(M) while enabled, mode already M   -> no-op
 *  - Mode(M) while enabled, mode currently X -> applyMode=M
 *  - Mode(M) while disabled, mode already M  -> applyEnabled=true (re-enable, mode unchanged)
 *  - Mode(M) while disabled, mode currently X -> applyEnabled=true, applyMode=M
 */
fun decideStatusBarTransition(
    currentEnabled: Boolean,
    currentMode: IlSpyMode,
    selected: IlSpyStatusBarChoice,
): IlSpyStatusBarTransition = when (selected) {
    is IlSpyStatusBarChoice.Off -> IlSpyStatusBarTransition(
        applyEnabled = if (currentEnabled) false else null,
        applyMode = null,
    )
    is IlSpyStatusBarChoice.ModeChoice -> IlSpyStatusBarTransition(
        applyEnabled = if (!currentEnabled) true else null,
        applyMode = if (selected.mode != currentMode) selected.mode else null,
    )
}
