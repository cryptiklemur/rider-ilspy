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
import java.io.File
import java.nio.file.FileSystems
import java.nio.file.StandardWatchEventKinds
import java.nio.file.WatchService

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

    override fun ID(): String = IlSpyModeStatusBarWidgetFactory.WIDGET_ID

    override fun getPresentation(): StatusBarWidget.WidgetPresentation = this

    override fun install(statusBar: StatusBar) {
        this.statusBar = statusBar
        startReadySignalWatcher()
    }

    override fun dispose() {
        statusBar = null
        stopReadySignalWatcher()
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
                    refreshOpenIlSpyFilesNow()
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

    // No longer schedules multiple guess-the-deadline refreshes. The backend writes
    // ~/.RiderIlSpy/ready.txt when its re-decompile pass completes and a WatchService
    // (see startReadySignalWatcher) reacts with a single VFS refresh.
    private fun refreshOpenIlSpyFilesNow() {
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


    private var watchService: WatchService? = null
    private var watchThread: Thread? = null

    private fun startReadySignalWatcher() {
        try {
            val dir = File(System.getProperty("user.home"), ".RiderIlSpy")
            if (!dir.exists()) dir.mkdirs()
            val ws = FileSystems.getDefault().newWatchService()
            dir.toPath().register(
                ws,
                StandardWatchEventKinds.ENTRY_MODIFY,
                StandardWatchEventKinds.ENTRY_CREATE,
            )
            watchService = ws
            val thread = Thread({
                try {
                    while (!Thread.currentThread().isInterrupted) {
                        val key = ws.take()
                        var sawReady = false
                        for (event in key.pollEvents()) {
                            val ctx = event.context()
                            if (ctx is java.nio.file.Path && ctx.fileName.toString() == "ready.txt") {
                                sawReady = true
                                break
                            }
                        }
                        if (sawReady) refreshOpenIlSpyFilesNow()
                        if (!key.reset()) break
                    }
                } catch (_: InterruptedException) {
                    // shutting down
                } catch (_: java.nio.file.ClosedWatchServiceException) {
                    // shutting down
                }
            }, "RiderIlSpy-ready-watcher").apply { isDaemon = true }
            watchThread = thread
            thread.start()
        } catch (_: Exception) {
            // If we can't watch, mode flips will require the user to re-trigger navigation
            // to see the new content. We log nothing here to keep the widget side quiet.
        }
    }

    private fun stopReadySignalWatcher() {
        try { watchService?.close() } catch (_: Exception) { /* ignore */ }
        watchService = null
        watchThread?.interrupt()
        watchThread = null
    }
}
