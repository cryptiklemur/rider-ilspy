package com.cryptiklemur.riderilspy.internals

import com.cryptiklemur.riderilspy.search.IlSpySearchSettings
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.util.xmlb.XmlSerializerUtil
import com.intellij.util.xmlb.annotations.OptionTag
@Service(Service.Level.APP)
@State(name = "RiderIlSpyFrontendSettings", storages = [Storage("RiderIlSpy.xml")])
class IlSpyFrontendSettings : PersistentStateComponent<IlSpyFrontendSettings.State> {

    data class State(
        var mode: String = IlSpyMode.CSharp.backendName,
        // Master on/off for the plugin. Persisted here (not on the C# side)
        // so the source of truth survives IDE restart and the C# backend
        // gets it pushed via rd protocol on each solution open. Default
        // true preserves existing installs' behavior.
        var enabled: Boolean = true,
        @OptionTag var search: IlSpySearchSettings = IlSpySearchSettings(),
    )

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

    /**
     * Master on/off for the plugin. When false, the C# external sources
     * provider short-circuits both IsApplicableForNavigation and
     * IsPreferredForNavigation, so Rider's default navigation behavior
     * takes over with no ILSpy intercept. The status bar widget exposes
     * this as a single-click "Off" toggle.
     *
     * Pushed to the backend via [IlSpyProtocolHost.setEnabled] / the
     * initial push during service init.
     */
    var enabled: Boolean
        get() = internalState.enabled
        set(value) {
            internalState.enabled = value
        }

    val search: IlSpySearchSettings
        get() = internalState.search

    companion object {
        fun getInstance(): IlSpyFrontendSettings =
            ApplicationManager.getApplication().getService(IlSpyFrontendSettings::class.java)
    }
}
