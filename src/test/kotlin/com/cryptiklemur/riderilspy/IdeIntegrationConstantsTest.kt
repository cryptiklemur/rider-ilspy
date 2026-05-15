package com.cryptiklemur.riderilspy

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Locks in the framework-imposed constants that have to stay in sync across
 * three files each: the Kotlin side, the C# Pid constant, and plugin.xml.
 *
 * These files (IlSpyOptionsPage, IlSpyModeStatusBarWidget,
 * IlSpyModeStatusBarWidgetFactory) are otherwise IntelliJ-Platform-bound and
 * can't run under unit tests without a BasePlatformTestCase harness — but the
 * static IDs they expose are pure values, and a rename of either side breaks
 * the integration silently. Pinning them here makes the drift loud.
 */
class IdeIntegrationConstantsTest {

    @Test
    fun `options page id is stable and matches conventional shape`() {
        val id = IlSpyOptionsPage.PAGE_ID
        assertEquals("RiderIlSpyOptionsPage", id)
        assertTrue(id.isNotBlank())
    }

    @Test
    fun `status bar widget id is stable and unique-looking`() {
        val id = IlSpyModeStatusBarWidgetFactory.WIDGET_ID
        assertEquals("RiderIlSpy.ModeStatusBarWidget", id)
        assertTrue(id.startsWith("RiderIlSpy."))
    }

    @Test
    fun `options page and widget ids do not collide`() {
        // Different IntelliJ extension points but if someone copy-pastes a
        // constant the namespace collision would be hard to spot at runtime.
        assertTrue(IlSpyOptionsPage.PAGE_ID != IlSpyModeStatusBarWidgetFactory.WIDGET_ID)
    }
}
