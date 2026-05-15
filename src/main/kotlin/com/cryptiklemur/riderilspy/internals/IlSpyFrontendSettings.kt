package com.cryptiklemur.riderilspy.internals

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.util.xmlb.XmlSerializerUtil

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
     * The active ILSpy decompiler output mode. Reading returns the persisted
     * mode (or [IlSpyMode.CSharp] if the persisted identifier is unknown);
     * writing just mutates the in-memory [State] which the IntelliJ settings
     * machinery flushes to RiderIlSpy.xml on shutdown.
     *
     * Cross-process delivery to the C# backend goes through [IlSpyProtocolHost]
     * (rd protocol) — this property is intentionally backend-agnostic.
     */
    var mode: IlSpyMode
        get() = IlSpyMode.fromBackendName(internalState.mode)
        set(value) {
            internalState.mode = value.backendName
        }

    companion object {
        fun getInstance(): IlSpyFrontendSettings =
            ApplicationManager.getApplication().getService(IlSpyFrontendSettings::class.java)
    }
}
