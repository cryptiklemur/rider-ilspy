package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class IlSpySearchEverywhereContributorTest {

    private fun readyState() = IlSpySearchIndexStateSnapshot(phase = "Ready", indexed = 10, total = 10, skipped = 0, errorMessage = "")
    private fun buildingState() = IlSpySearchIndexStateSnapshot(phase = "Building", indexed = 3, total = 10, skipped = 0, errorMessage = "")
    private fun failedState() = IlSpySearchIndexStateSnapshot(phase = "Failed", indexed = 0, total = 0, skipped = 0, errorMessage = "disk full")

    @Test
    fun `pattern too short returns false`() {
        assertFalse(IlSpySearchEverywhereContributor.shouldServe(readyState(), ""))
        assertFalse(IlSpySearchEverywhereContributor.shouldServe(readyState(), "x"))
    }

    @Test
    fun `ready state with sufficient pattern returns true`() {
        assertTrue(IlSpySearchEverywhereContributor.shouldServe(readyState(), "ab"))
        assertTrue(IlSpySearchEverywhereContributor.shouldServe(readyState(), "hello"))
    }

    @Test
    fun `building state returns false even with long pattern`() {
        assertFalse(IlSpySearchEverywhereContributor.shouldServe(buildingState(), "hello"))
    }

    @Test
    fun `failed state returns false`() {
        assertFalse(IlSpySearchEverywhereContributor.shouldServe(failedState(), "hello"))
    }

    @Test
    fun `null state returns false`() {
        assertFalse(IlSpySearchEverywhereContributor.shouldServe(null, "hello"))
    }
}
