package com.cryptiklemur.riderilspy

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class IlSpyModeTest {

    @Test
    fun `fromBackendName resolves each known backend identifier`() {
        assertEquals(IlSpyMode.CSharp, IlSpyMode.fromBackendName("CSharp"))
        assertEquals(IlSpyMode.IL, IlSpyMode.fromBackendName("IL"))
        assertEquals(IlSpyMode.CSharpWithIL, IlSpyMode.fromBackendName("CSharpWithIL"))
    }

    @Test
    fun `fromBackendName falls back to CSharp on null`() {
        assertEquals(IlSpyMode.CSharp, IlSpyMode.fromBackendName(null))
    }

    @Test
    fun `fromBackendName falls back to CSharp on unknown identifier`() {
        assertEquals(IlSpyMode.CSharp, IlSpyMode.fromBackendName(""))
        assertEquals(IlSpyMode.CSharp, IlSpyMode.fromBackendName("garbage"))
        assertEquals(IlSpyMode.CSharp, IlSpyMode.fromBackendName("csharp"))
    }

    @Test
    fun `display and backend names are distinct per mode and round-trip`() {
        for (mode in IlSpyMode.entries) {
            assertEquals(mode, IlSpyMode.fromBackendName(mode.backendName))
        }
    }
}
