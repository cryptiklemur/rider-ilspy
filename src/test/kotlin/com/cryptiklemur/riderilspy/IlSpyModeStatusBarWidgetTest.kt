package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.internals.IlSpyMode
import com.cryptiklemur.riderilspy.search.IlSpySearchIndexStateSnapshot
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class IlSpyModeStatusBarWidgetTest {

    private val bundle: (String, Array<out Any>) -> String = { key, args ->
        "$key:${args.joinToString("|")}"
    }

    @Test
    fun `renderStatusBarLabel uses base label when no index state`() {
        val label = renderStatusBarLabel(IlSpyMode.CSharp, indexState = null, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", label)
    }

    @Test
    fun `renderStatusBarLabel uses indexing label during Building phase`() {
        val snap = IlSpySearchIndexStateSnapshot(phase = "Building", indexed = 3, total = 10, skipped = 0, errorMessage = "")
        val label = renderStatusBarLabel(IlSpyMode.IL, indexState = snap, bundle = bundle)
        val expectedBase = "statusbar.mode.label_base:${IlSpyMode.IL.displayName}"
        assertEquals("statusbar.mode.label_indexing:$expectedBase|3|10", label)
    }

    @Test
    fun `renderStatusBarLabel uses failed label on Failed phase`() {
        val snap = IlSpySearchIndexStateSnapshot(phase = "Failed", indexed = 0, total = 0, skipped = 0, errorMessage = "boom")
        val label = renderStatusBarLabel(IlSpyMode.CSharpWithIL, indexState = snap, bundle = bundle)
        val expectedBase = "statusbar.mode.label_base:${IlSpyMode.CSharpWithIL.displayName}"
        assertEquals("statusbar.mode.label_failed:$expectedBase", label)
    }

    @Test
    fun `renderStatusBarLabel falls through to base for Ready and other phases`() {
        val ready = IlSpySearchIndexStateSnapshot(phase = "Ready", indexed = 10, total = 10, skipped = 0, errorMessage = "")
        val labelReady = renderStatusBarLabel(IlSpyMode.CSharp, indexState = ready, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", labelReady)

        val idle = IlSpySearchIndexStateSnapshot(phase = "Idle", indexed = 0, total = 0, skipped = 0, errorMessage = "")
        val labelIdle = renderStatusBarLabel(IlSpyMode.CSharp, indexState = idle, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", labelIdle)
    }
}
