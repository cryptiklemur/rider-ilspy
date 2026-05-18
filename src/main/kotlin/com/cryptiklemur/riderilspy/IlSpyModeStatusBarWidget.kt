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

    override fun getSelectedValue(): String =
        renderStatusBarLabel(
            mode = IlSpyFrontendSettings.getInstance().mode,
            indexState = lastIndexState,
            bundle = { key, args -> RiderIlSpyBundle.message(key, *args) },
        )

    override fun getPopup(): JBPopup {
        val current = IlSpyFrontendSettings.getInstance().mode
        val step = object : BaseListPopupStep<IlSpyMode>(
            RiderIlSpyBundle.message("statusbar.mode.popup_title"),
            IlSpyMode.entries.toList(),
        ) {
            override fun getTextFor(value: IlSpyMode): String =
                if (value == current) "${value.displayName}  (current)" else value.displayName

            override fun onChosen(selectedValue: IlSpyMode, finalChoice: Boolean): PopupStep<*>? {
                if (selectedValue != current) {
                    // 1. Persist locally so the choice survives restart.
                    IlSpyFrontendSettings.getInstance().mode = selectedValue
                    // 2. Push to the backend over rd; the backend will re-decompile and
                    //    fire readyTick which our adviseReady handler turns into a VFS refresh.
                    IlSpyProtocolHost.getInstance(project).setMode(selectedValue)
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
fun renderStatusBarLabel(
    mode: IlSpyMode,
    indexState: IlSpySearchIndexStateSnapshot?,
    bundle: (String, Array<out Any>) -> String,
): String {
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
