package com.cryptiklemur.riderilspy.search

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
                "Save-as for resource '${target.resourceEntry}' not yet wired.",
                "ILSpy Search",
            )
        }
    }

    private fun extractToTemp(target: NavTarget): Path {
        val dir = Path.of(
            System.getProperty("java.io.tmpdir"),
            "ilspy-resources",
            target.assemblyPath.substringAfterLast('/'),
        )
        Files.createDirectories(dir)
        val name = target.resourceEntry.ifEmpty { "resource-${target.metadataToken}" }
        val out = dir.resolve(name)
        // Prototype stub: extraction returns empty bytes.
        // Phase 12 wires the backend rd call (extractResource) for real bytes.
        Files.write(out, ByteArray(0))
        return out
    }
}
