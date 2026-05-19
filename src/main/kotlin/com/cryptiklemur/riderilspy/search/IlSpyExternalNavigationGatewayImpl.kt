package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
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
            LOG.warn(RiderIlSpyBundle.message("nav.error.protocol_not_ready"))
            return
        }
        scheduler.invokeOrQueue {
            val task: RdTask<*> = model.resolveNavTarget.start(clientService.lifetime, target) as RdTask<*>
            task.result.advise(clientService.lifetime) { result ->
                val payload = result.unwrap() as? com.cryptiklemur.riderilspy.model.NavResolution
                ApplicationManager.getApplication().invokeLater {
                    if (payload == null || !payload.success) {
                        val errMsg = payload?.errorMessage ?: RiderIlSpyBundle.message("nav.error.unknown")
                        Messages.showInfoMessage(
                            project,
                            RiderIlSpyBundle.message("nav.error.could_not_resolve", errMsg),
                            RiderIlSpyBundle.message("nav.dialog.title"),
                        )
                        return@invokeLater
                    }
                    openFileAt(payload.filePath, payload.line, payload.column)
                }
            }
        }
    }

    private fun openFileAt(path: String, line: Int, column: Int) {
        // refreshAndFindFileByPath touches VFS, which delegates to FSRecordsImpl
        // and triggers SlowOperations.assertSlowOperationsAreAllowed on the EDT
        // (IntelliJ 2024+ enforcement). Resolve on a pooled thread, then bounce
        // back to the EDT for the OpenFileDescriptor.navigate(true) call which
        // requires the write-intent read action only the EDT can take.
        val app = ApplicationManager.getApplication()
        resolveAndOpenOffEdt(
            runOnPooled = { app.executeOnPooledThread(it) },
            runOnEdt = { app.invokeLater(it) },
            resolveFile = { LocalFileSystem.getInstance().refreshAndFindFileByPath(path) },
            onOpen = { vfile ->
                val descriptor = OpenFileDescriptor(
                    project,
                    vfile as com.intellij.openapi.vfs.VirtualFile,
                    (line - 1).coerceAtLeast(0),
                    (column - 1).coerceAtLeast(0),
                )
                descriptor.navigate(true)
            },
            onMissing = {
                LOG.warn("ilspy-search nav-fe: file not found after decompile: $path")
                Messages.showInfoMessage(
                    project,
                    RiderIlSpyBundle.message("nav.error.file_missing", path),
                    RiderIlSpyBundle.message("nav.dialog.title"),
                )
            },
        )
    }

    companion object {
        private val LOG = Logger.getInstance(IlSpyExternalNavigationGatewayImpl::class.java)
    }
}

/**
 * Pure orchestrator for the "VFS-resolve off EDT, then open on EDT" pattern.
 * Extracted so the threading-discipline contract (pooled-then-edt, missing-vs-open
 * dispatch by null) can be asserted in a unit test without an IDE harness.
 *
 * Contract:
 *   - [runOnPooled] is invoked exactly once, immediately, with a runnable that
 *     itself runs [resolveFile] on a non-EDT thread.
 *   - After [resolveFile] returns, [runOnEdt] is invoked from inside the pooled
 *     runnable with a continuation that calls either [onOpen] or [onMissing].
 *   - Null result from [resolveFile] routes to [onMissing]; non-null routes to
 *     [onOpen]. The orchestrator never touches the resolved object's identity.
 */
fun resolveAndOpenOffEdt(
    runOnPooled: (Runnable) -> Unit,
    runOnEdt: (Runnable) -> Unit,
    resolveFile: () -> Any?,
    onOpen: (Any) -> Unit,
    onMissing: () -> Unit,
) {
    runOnPooled(Runnable {
        val resolved = resolveFile()
        runOnEdt(Runnable {
            if (resolved == null) onMissing() else onOpen(resolved)
        })
    })
}
