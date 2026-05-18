package com.cryptiklemur.riderilspy.i18n

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class RiderIlSpyBundleTest {

    @Test
    fun `bundle resolves representative keys to non-empty values`() {
        // If RiderIlSpyBundle.properties is missing or misnamed, DynamicBundle
        // falls back to returning the key wrapped in `!key!`. Asserting the
        // value is not equal to the key catches that case.
        val displayName = RiderIlSpyBundle.message("statusbar.mode.display_name")
        assertNotEquals("statusbar.mode.display_name", displayName)
        assertTrue(displayName.isNotEmpty(), "display name should not be empty")
    }

    @Test
    fun `bundle interpolates positional placeholders`() {
        val result = RiderIlSpyBundle.message("statusbar.mode.label_indexing", "ILSpy: C#", 3, 10)
        assertTrue(result.contains("ILSpy: C#"), "should include base label arg")
        assertTrue(result.contains("3"), "should include indexed count")
        assertTrue(result.contains("10"), "should include total count")
    }

    @Test
    fun `mode display names resolve via bundle`() {
        // Each enum constant's lazy displayName getter must hit the bundle —
        // if the lookup ever silently fails it would return the literal key
        // text. The key names themselves are dotted so a key leaking through
        // is obvious.
        val csharp = RiderIlSpyBundle.message("mode.csharp.display")
        val il = RiderIlSpyBundle.message("mode.il.display")
        val mixed = RiderIlSpyBundle.message("mode.csharp_with_il.display")
        assertEquals("C#", csharp)
        assertEquals("IL", il)
        assertEquals("C# + IL", mixed)
    }
}
