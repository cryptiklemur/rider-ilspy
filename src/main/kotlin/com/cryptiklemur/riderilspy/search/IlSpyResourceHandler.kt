package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.cryptiklemur.riderilspy.model.NavTarget
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.vfs.LocalFileSystem
import java.nio.file.Files
import java.nio.file.Path

class IlSpyResourceHandler(private val project: Project) {
    fun handle(target: NavTarget) {
        when (resourceKindToAction(target.mimeHint)) {
            ResourceAction.OpenAsImage -> openAsImage(target)
            ResourceAction.OpenAsText -> openAsText(target)
            ResourceAction.PromptSaveAs -> promptSaveAs(target)
        }
    }

    private fun openAsImage(target: NavTarget) {
        val tmp = extractToTemp(target)
        val vf = LocalFileSystem.getInstance().refreshAndFindFileByNioFile(tmp) ?: return
        FileEditorManager.getInstance(project).openFile(vf, true)
    }

    private fun openAsText(target: NavTarget) {
        val tmp = extractToTemp(target)
        val vf = LocalFileSystem.getInstance().refreshAndFindFileByNioFile(tmp) ?: return
        FileEditorManager.getInstance(project).openFile(vf, true)
    }

    private fun promptSaveAs(target: NavTarget) {
        ApplicationManager.getApplication().invokeLater {
            // Phase 12 wires up FileChooserDescriptor + SaveAs dialog; on confirm,
            // calls extractToPath(target, chosenPath). The backend exposes a
            // dedicated rd call for extraction (out of scope for this phase).
            Messages.showInfoMessage(
                project,
                RiderIlSpyBundle.message("resource.save_dialog.not_wired_message", target.resourceEntry),
                RiderIlSpyBundle.message("resource.save_dialog.title"),
            )
        }
    }

    private fun extractToTemp(target: NavTarget): Path {
        val name = target.resourceEntry.ifEmpty {
            RiderIlSpyBundle.message("resource.save_dialog.default_name", target.metadataToken)
        }
        val rel = resourceTempRelativePath(target.assemblyPath, name)
        val dir = Path.of(System.getProperty("java.io.tmpdir")).resolve(rel.first)
        Files.createDirectories(dir)
        val out = dir.resolve(rel.second)
        // Prototype stub: extraction returns empty bytes.
        // Phase 12 wires the backend rd call (extractResource) for real bytes.
        Files.write(out, ByteArray(0))
        return out
    }
}

// Pure path-naming logic, extracted so the Windows-vs-Unix separator handling
// can be unit-tested without touching the filesystem. Returns the (dir-suffix,
// file-name) pair under ilspy-resources/<asm-basename>/<file-name>. Pure: no
// IO, no system properties.
fun resourceTempRelativePath(assemblyPath: String, fileName: String): Pair<String, String> {
    // Take basename across BOTH separators so Windows paths like
    // C:\src\foo\bar.dll route into ilspy-resources/bar.dll/ instead of
    // ilspy-resources/C:\src\foo\bar.dll/ (which would explode on Files.createDirectories).
    val asmBasename = assemblyPath.substringAfterLast('/').substringAfterLast('\\')
    return "ilspy-resources/$asmBasename" to fileName
}
