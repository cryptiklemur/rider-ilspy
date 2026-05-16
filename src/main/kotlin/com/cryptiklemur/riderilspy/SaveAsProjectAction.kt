package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.model.SaveAsProjectRequest
import com.cryptiklemur.riderilspy.model.riderIlSpyModel
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.fileChooser.FileChooser
import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.jetbrains.rider.projectView.solution
import com.jetbrains.rider.projectView.workspace.getFile
import com.jetbrains.rider.projectView.workspace.getProjectModelEntity
import java.io.File

/**
 * "Save Decompiled Assembly as Project..." — invoked from the right-click
 * context menu on a managed assembly in the Solution Explorer (project
 * references, NuGet PackageReferences) or in the Assembly Explorer. Picks
 * the assembly off the [AnActionEvent.dataContext], prompts for an output
 * directory, and asks the ReSharper backend (via the rd call defined in
 * [com.cryptiklemur.riderilspy.model.RiderIlSpyModel]) to decompile the
 * whole assembly into a buildable .csproj tree there.
 *
 * UX flow:
 *   1. Right-click a .dll / .exe in References / NuGet / Assembly Explorer.
 *   2. Pick an output directory (folder chooser; created on the backend if missing).
 *   3. Backgroundable progress task runs the rd call so the IDE stays responsive
 *      while ILSpy churns through types (this can take many seconds for large
 *      assemblies like System.Private.CoreLib).
 *   4. Notification on completion: success carries the .csproj path + file count;
 *      failure carries the backend's error message.
 *
 * The action does NOT live under the Tools menu — there's no file picker for
 * a bare invocation, because the context-menu surfaces always carry an
 * assembly. Users who want to decompile a binary not in their solution
 * should first add it via *Tools | Open Assembly* (built-in Rider action),
 * which surfaces the file under Assembly Explorer; this action then becomes
 * available on its right-click.
 */
class SaveAsProjectAction : AnAction() {
    private val log: Logger = Logger.getInstance(SaveAsProjectAction::class.java)

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(e: AnActionEvent) {
        // Only visible when (a) a project is open AND (b) the right-click context
        // resolves to a managed .dll / .exe. Without (b) the action has nothing
        // to act on — and unlike the old Tools-menu entry there is no file
        // picker fallback, so don't show a no-op item to the user.
        val project = e.project
        e.presentation.isEnabledAndVisible = project != null && findAssemblyPathFromContext(e) != null
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val assemblyPath = findAssemblyPathFromContext(e) ?: run {
            log.warn("SaveAsProjectAction invoked without an assembly path in context")
            return
        }

        val dirDescriptor = FileChooserDescriptorFactory
            .singleDir()
            .withTitle("Choose Output Directory")
            .withDescription("ILSpy will write a .csproj and source files into this folder.")
        val outDir = FileChooser.chooseFile(dirDescriptor, project, null) ?: return

        val assemblyName = assemblyPath.substringAfterLast('/').substringAfterLast('\\')
        val title = "Decompiling $assemblyName to project"
        ProgressManager.getInstance().run(object : Task.Backgroundable(project, title, true) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                indicator.text = "Decompiling $assemblyName via ILSpy..."
                runSaveAsProject(project, assemblyPath, outDir.path)
            }
        })
    }

    /**
     * Resolve the assembly path the user right-clicked on. Two surfaces:
     *  1. **Assembly Explorer** — items expose `CommonDataKeys.VIRTUAL_FILE`
     *     directly; the file *is* the .dll / .exe.
     *  2. **Solution Explorer references / NuGet packages** — items expose a
     *     [ProjectModelEntity]; the workspace-model file system knows where
     *     the referenced assembly lives on disk (under `~/.nuget/packages`
     *     for `PackageReference` items, the project's `bin/` for project
     *     references, etc.).
     *
     * Returns `null` if neither lookup finds a managed binary — the action's
     * `update()` keys off this so we never show the menu item where it can't
     * act.
     */
    private fun findAssemblyPathFromContext(e: AnActionEvent): String? {
        e.getData(CommonDataKeys.VIRTUAL_FILE)?.let { vf ->
            val ext = vf.extension?.lowercase()
            if (ext == "dll" || ext == "exe") return vf.path
        }
        val entity = e.dataContext.getProjectModelEntity(false) ?: return null
        val file: File = entity.getFile() ?: return null
        val path = file.path
        if (!path.endsWith(".dll", ignoreCase = true) && !path.endsWith(".exe", ignoreCase = true)) return null
        return path
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
