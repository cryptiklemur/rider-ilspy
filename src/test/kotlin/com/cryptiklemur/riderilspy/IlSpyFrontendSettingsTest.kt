package com.cryptiklemur.riderilspy

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class IlSpyFrontendSettingsTest {

    @Test
    fun `getState returns the loaded state contents`() {
        val settings = IlSpyFrontendSettings()
        val state = IlSpyFrontendSettings.State(mode = IlSpyMode.IL.backendName)
        settings.loadState(state)
        assertEquals(IlSpyMode.IL.backendName, settings.state.mode)
    }

    @Test
    fun `mode getter decodes backendName via IlSpyMode_fromBackendName`() {
        val settings = IlSpyFrontendSettings()
        settings.loadState(IlSpyFrontendSettings.State(mode = IlSpyMode.CSharpWithIL.backendName))
        assertEquals(IlSpyMode.CSharpWithIL, settings.mode)
    }

    @Test
    fun `mode getter falls back to CSharp when state holds an unknown identifier`() {
        val settings = IlSpyFrontendSettings()
        // Older RiderIlSpy.xml files may carry pre-rename backend identifiers; the
        // getter must coerce them to a known value rather than throwing.
        settings.loadState(IlSpyFrontendSettings.State(mode = "LegacyUnknownMode"))
        assertEquals(IlSpyMode.CSharp, settings.mode)
    }

    @Test
    fun `setting mode mutates in-memory state without filesystem side effects`() {
        val settings = IlSpyFrontendSettings()
        settings.mode = IlSpyMode.IL
        assertEquals(IlSpyMode.IL.backendName, settings.state.mode)
        settings.mode = IlSpyMode.CSharpWithIL
        assertEquals(IlSpyMode.CSharpWithIL.backendName, settings.state.mode)
    }
}
