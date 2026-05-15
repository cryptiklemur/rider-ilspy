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
    private val readyWatcher = IlSpyReadySignalWatcher(onReady = ::refreshOpenIlSpyFiles)

    override fun ID(): String = IlSpyModeStatusBarWidgetFactory.WIDGET_ID

    override fun getPresentation(): StatusBarWidget.WidgetPresentation = this

    override fun install(statusBar: StatusBar) {
        this.statusBar = statusBar
        readyWatcher.start()
    }

    override fun dispose() {
        statusBar = null
        readyWatcher.stop()
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
                    IlSpyFrontendSettings.getInstance().mode = selectedValue
                    refreshOpenIlSpyFiles()
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

    // The backend writes ~/.RiderIlSpy/ready.txt when its re-decompile pass completes
    // and IlSpyReadySignalWatcher reacts with a single VFS refresh. The refresh itself
    // is dispatched onto the EDT via invokeLater, so callers should treat this as
    // "schedule a refresh", not a synchronous reload.
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
