package com.cryptiklemur.riderilspy

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
import com.intellij.openapi.wm.StatusBarWidgetFactory
import com.intellij.openapi.wm.WindowManager
import com.jetbrains.rd.util.lifetime.LifetimeDefinition

class IlSpyModeStatusBarWidgetFactory : StatusBarWidgetFactory {
    override fun getId(): String = WIDGET_ID
    override fun getDisplayName(): String = "ILSpy Mode"
    override fun createWidget(project: Project): StatusBarWidget = IlSpyModeStatusBarWidget(project)
    override fun isAvailable(project: Project): Boolean = true
    override fun canBeEnabledOn(statusBar: StatusBar): Boolean = true

    companion object {
        const val WIDGET_ID = "RiderIlSpy.ModeStatusBarWidget"
    }
}

class IlSpyModeStatusBarWidget(private val project: Project) :
    StatusBarWidget,
    StatusBarWidget.MultipleTextValuesPresentation {

    private var statusBar: StatusBar? = null
    private val installLifetime = LifetimeDefinition()

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
    }

    override fun dispose() {
        statusBar = null
        installLifetime.terminate()
    }

    override fun getTooltipText(): String =
        "ILSpy decompiler output mode. Click to switch — affects subsequent decompiles."

    override fun getSelectedValue(): String =
        "ILSpy: ${IlSpyFrontendSettings.getInstance().mode.displayName}"

    override fun getPopup(): JBPopup {
        val current = IlSpyFrontendSettings.getInstance().mode
        val step = object : BaseListPopupStep<IlSpyMode>("ILSpy Mode", IlSpyMode.entries.toList()) {
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
        val targets: List<VirtualFile> = fem.openFiles.filter { isIlSpyDecompiledFile(it) }
        if (targets.isEmpty()) return
        ApplicationManager.getApplication().invokeLater {
            if (!project.isDisposed) {
                VfsUtil.markDirtyAndRefresh(true, false, false, *targets.toTypedArray())
            }
        }
    }

    private fun isIlSpyDecompiledFile(file: VirtualFile): Boolean {
        val path = file.path
        return path.contains("/DecompilerCache/RiderIlSpy/") || path.contains("\\DecompilerCache\\RiderIlSpy\\")
    }
}
