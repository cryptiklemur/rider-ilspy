package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.model.SaveAsProjectRequest
import com.cryptiklemur.riderilspy.model.riderIlSpyModel
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.fileChooser.FileChooser
import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.jetbrains.rider.projectView.solution

/**
 * "Save Decompiled Assembly as Project..." — prompts the user for an assembly
 * file and an output directory, then asks the ReSharper backend (via the rd
 * call defined in [com.cryptiklemur.riderilspy.model.RiderIlSpyModel]) to
 * decompile the whole assembly into a buildable .csproj tree there.
 *
 * UX flow:
 *   1. Pick a .dll / .exe assembly (single-file chooser, filtered).
 *   2. Pick an output directory (folder chooser; created on the backend if missing).
 *   3. Backgroundable progress task runs the rd call so the IDE stays responsive
 *      while ILSpy churns through types (this can take many seconds for large
 *      assemblies like System.Private.CoreLib).
 *   4. Notification on completion: success carries the .csproj path + file count;
 *      failure carries the backend's error message.
 *
 * Plugin.xml registers this action under the Tools menu — that's the
 * conventional home for "do something with the current solution" entries that
 * don't fit a more specific surface.
 */
class SaveAsProjectAction : AnAction() {
    private val log: Logger = Logger.getInstance(SaveAsProjectAction::class.java)

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(e: AnActionEvent) {
        // Only meaningful with an open project — the rd model is solution-scoped.
        e.presentation.isEnabledAndVisible = e.project != null
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return

        val assemblyDescriptor = FileChooserDescriptorFactory
            .singleFile()
            .withTitle("Select .NET Assembly to Decompile")
            .withDescription("Pick a managed .dll or .exe — ILSpy will decompile the entire assembly.")
            .withFileFilter { it.extension == "dll" || it.extension == "exe" }
        val asmFile = FileChooser.chooseFile(assemblyDescriptor, project, null) ?: return

        val dirDescriptor = FileChooserDescriptorFactory
            .singleDir()
            .withTitle("Choose Output Directory")
            .withDescription("ILSpy will write a .csproj and source files into this folder.")
        val outDir = FileChooser.chooseFile(dirDescriptor, project, null) ?: return

        val title = "Decompiling ${asmFile.name} to project"
        ProgressManager.getInstance().run(object : Task.Backgroundable(project, title, true) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                indicator.text = "Decompiling ${asmFile.name} via ILSpy..."
                runSaveAsProject(project, asmFile.path, outDir.path)
            }
        })
    }

    private fun runSaveAsProject(project: Project, assemblyPath: String, targetDirectory: String) {
        val response = try {
            project.solution.riderIlSpyModel.saveAsProject.sync(SaveAsProjectRequest(assemblyPath, targetDirectory))
        } catch (t: Throwable) {
            log.warn("RiderIlSpy SaveAsProject rd-call failed", t)
            notify(project, "Save as project failed: ${t.message ?: t.javaClass.simpleName}", NotificationType.ERROR)
            return
        }

        if (response.success) {
            val where = response.projectFilePath.ifBlank { targetDirectory }
            notify(
                project,
                "Wrote ${response.csharpFileCount} C# files to $where",
                NotificationType.INFORMATION,
            )
        } else {
            val msg = response.errorMessage.ifBlank { "ILSpy returned no error message" }
            notify(project, "Save as project failed: $msg", NotificationType.ERROR)
        }
    }

    private fun notify(project: Project, content: String, type: NotificationType) {
        // RiderIlSpy notification group is declared in plugin.xml under
        // <applicationService> + <notificationGroup>. Falling back to a
        // synthetic group when missing keeps the action safe in tests that
        // don't load the full IDE wiring.
        val group = NotificationGroupManager.getInstance().getNotificationGroup(NOTIFICATION_GROUP_ID)
            ?: NotificationGroupManager.getInstance().getNotificationGroup("Balloon")
            ?: return
        group.createNotification("RiderIlSpy", content, type).notify(project)
    }

    companion object {
        const val NOTIFICATION_GROUP_ID: String = "RiderIlSpy"
    }
}
