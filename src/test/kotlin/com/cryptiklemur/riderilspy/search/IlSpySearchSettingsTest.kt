package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class IlSpySearchSettingsTest {
    @Test
    fun `defaults`() {
        val s = IlSpySearchSettings()
        assertTrue(s.indexerEnabled)
        assertTrue(s.persistBetweenSessions)
        assertEquals(5000, s.maxResultsPerQuery)
        assertTrue(s.excludedAssemblies.isEmpty())
    }
}
