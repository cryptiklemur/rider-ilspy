package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.model.NavTarget
import com.cryptiklemur.riderilspy.model.riderIlSpyModel
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.fileEditor.OpenFileDescriptor
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.vfs.LocalFileSystem
import com.jetbrains.rd.framework.impl.RdTask
import com.jetbrains.rider.projectView.solution

class IlSpyExternalNavigationGatewayImpl(
    private val project: Project,
) : IlSpyExternalNavigationGateway {
    override fun openDecompiledMember(assemblyPath: String, metadataToken: Int, ilOffset: Int) {
        val model = project.solution.riderIlSpyModel
        val clientService = project.getService(IlSpySearchClientService::class.java)
        val target = NavTarget(
            kind = "Code",
            assemblyPath = assemblyPath,
            metadataToken = metadataToken,
            ilOffset = ilOffset,
            resourceEntry = "",
            mimeHint = "",
        )
        val scheduler = model.protocol?.scheduler
        if (scheduler == null) {
            LOG.warn("ilspy-search nav-fe: protocol not ready")
            return
        }
        scheduler.invokeOrQueue {
            val task: RdTask<*> = model.resolveNavTarget.start(clientService.lifetime, target) as RdTask<*>
            task.result.advise(clientService.lifetime) { result ->
                val payload = result.unwrap() as? com.cryptiklemur.riderilspy.model.NavResolution
                ApplicationManager.getApplication().invokeLater {
                    if (payload == null || !payload.success) {
                        Messages.showInfoMessage(
                            project,
                            "Could not resolve decompiled source.\n${payload?.errorMessage ?: "unknown error"}",
                            "ILSpy Search",
                        )
                        return@invokeLater
                    }
                    openFileAt(payload.filePath, payload.line, payload.column)
                }
            }
        }
    }

    private fun openFileAt(path: String, line: Int, column: Int) {
        val vfile = LocalFileSystem.getInstance().refreshAndFindFileByPath(path)
        if (vfile == null) {
            LOG.warn("ilspy-search nav-fe: file not found after decompile: $path")
            Messages.showInfoMessage(project, "Decompiled file missing on disk:\n$path", "ILSpy Search")
            return
        }
        val descriptor = OpenFileDescriptor(project, vfile, (line - 1).coerceAtLeast(0), (column - 1).coerceAtLeast(0))
        descriptor.navigate(true)
    }

    companion object {
        private val LOG = Logger.getInstance(IlSpyExternalNavigationGatewayImpl::class.java)
    }
}
