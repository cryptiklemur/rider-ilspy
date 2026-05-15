package com.cryptiklemur.riderilspy

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.util.xmlb.XmlSerializerUtil
import java.io.File
import java.nio.file.Files

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
            val path = sharedModeFile()
            Files.createDirectories(path.parentFile.toPath())
            path.writeText(value.backendName)
        }
    }

    companion object {
        fun getInstance(): IlSpyFrontendSettings =
            ApplicationManager.getApplication().getService(IlSpyFrontendSettings::class.java)

        fun sharedModeFile(): File =
            File(System.getProperty("user.home"), ".RiderIlSpy/mode.txt")
    }
}
