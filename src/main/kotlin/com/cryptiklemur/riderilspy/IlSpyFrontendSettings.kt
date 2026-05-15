package com.cryptiklemur.riderilspy

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
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
        }
    }

    companion object {
        fun getInstance(): IlSpyFrontendSettings =
            ApplicationManager.getApplication().getService(IlSpyFrontendSettings::class.java)

        fun sharedModeFile(): File =
            File(System.getProperty("user.home"), ".RiderIlSpy/mode.txt")
    }
}
