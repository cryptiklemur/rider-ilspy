package com.cryptiklemur.riderilspy

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.diagnostic.Logger
import com.intellij.util.xmlb.XmlSerializerUtil
import java.io.File
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

@Service(Service.Level.APP)
@State(name = "RiderIlSpyFrontendSettings", storages = [Storage("RiderIlSpy.xml")])
class IlSpyFrontendSettings : PersistentStateComponent<IlSpyFrontendSettings.State> {

    data class State(var mode: String = IlSpyMode.CSharp.backendName)

    private var internalState: State = State()

    override fun getState(): State = internalState

    override fun loadState(state: State) {
        XmlSerializerUtil.copyBean(state, internalState)
    }

    /**
     * The active ILSpy decompiler output mode.
     *
     * Reading returns the persisted mode (or [IlSpyMode.CSharp] if the persisted
     * identifier is unknown). Writing has two side effects: it mutates the in-memory
     * [State] and synchronously writes the chosen [IlSpyMode.backendName] to
     * [sharedModeFile] so the C# backend can pick it up. Filesystem failures on that
     * write are logged via [Logger] (see [writeSharedFile]) but are intentionally not
     * propagated — the in-memory toggle still succeeds, and the IDE is the source of
     * truth on the next backend roundtrip.
     */
    var mode: IlSpyMode
        get() = IlSpyMode.fromBackendName(internalState.mode)
        set(value) {
            internalState.mode = value.backendName
            writeSharedFile(value)
        }

    private fun writeSharedFile(value: IlSpyMode) {
        runCatching {
            val target = sharedModeFile().toPath()
            Files.createDirectories(target.parent)
            // Write to a sibling tmp file then atomically rename it over the target.
            // Plain `writeText` open-truncate-write leaves a window where readers see
            // an empty file; ATOMIC_MOVE replaces the inode in one rename syscall and
            // also overwrites any symlink at the destination rather than following it.
            val tmp = target.resolveSibling("mode.txt.tmp")
            Files.write(tmp, value.backendName.toByteArray(Charsets.UTF_8))
            try {
                Files.move(tmp, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE)
            } catch (_: AtomicMoveNotSupportedException) {
                // Filesystem (e.g. some FUSE mounts) doesn't support atomic moves; fall back
                // to non-atomic replace. Still better than the original truncate-then-write.
                Files.move(tmp, target, StandardCopyOption.REPLACE_EXISTING)
            }
        }.onFailure { error ->
            // In-memory state is updated, but the C# backend reads from this file —
            // surfacing the cause helps users understand why a mode toggle "did nothing".
            Logger.getInstance(IlSpyFrontendSettings::class.java)
                .warn("Failed to write shared mode file at ${sharedModeFile().path}", error)
        }
    }

    companion object {
        // Cross-process channel with the C# backend. The directory and filenames are
        // mirrored on the C# side (RiderIlSpy backend); any change here must mirror there.
        const val SHARED_DIR_NAME: String = ".RiderIlSpy"
        const val MODE_FILE_NAME: String = "mode.txt"
        const val READY_FILE_NAME: String = "ready.txt"

        fun getInstance(): IlSpyFrontendSettings =
            ApplicationManager.getApplication().getService(IlSpyFrontendSettings::class.java)

        fun sharedDir(): File =
            File(System.getProperty("user.home"), SHARED_DIR_NAME)

        fun sharedModeFile(): File = File(sharedDir(), MODE_FILE_NAME)

        fun sharedReadyFile(): File = File(sharedDir(), READY_FILE_NAME)
    }
}
