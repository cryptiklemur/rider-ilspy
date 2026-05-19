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
        val label = renderStatusBarLabel(enabled = true, mode = IlSpyMode.CSharp, indexState = null, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", label)
    }

    @Test
    fun `renderStatusBarLabel uses indexing label during Building phase`() {
        val snap = IlSpySearchIndexStateSnapshot(phase = "Building", indexed = 3, total = 10, skipped = 0, errorMessage = "")
        val label = renderStatusBarLabel(enabled = true, mode = IlSpyMode.IL, indexState = snap, bundle = bundle)
        val expectedBase = "statusbar.mode.label_base:${IlSpyMode.IL.displayName}"
        assertEquals("statusbar.mode.label_indexing:$expectedBase|3|10", label)
    }

    @Test
    fun `renderStatusBarLabel uses failed label on Failed phase`() {
        val snap = IlSpySearchIndexStateSnapshot(phase = "Failed", indexed = 0, total = 0, skipped = 0, errorMessage = "boom")
        val label = renderStatusBarLabel(enabled = true, mode = IlSpyMode.CSharpWithIL, indexState = snap, bundle = bundle)
        val expectedBase = "statusbar.mode.label_base:${IlSpyMode.CSharpWithIL.displayName}"
        assertEquals("statusbar.mode.label_failed:$expectedBase", label)
    }

    @Test
    fun `renderStatusBarLabel falls through to base for Ready and other phases`() {
        val ready = IlSpySearchIndexStateSnapshot(phase = "Ready", indexed = 10, total = 10, skipped = 0, errorMessage = "")
        val labelReady = renderStatusBarLabel(enabled = true, mode = IlSpyMode.CSharp, indexState = ready, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", labelReady)

        val idle = IlSpySearchIndexStateSnapshot(phase = "Idle", indexed = 0, total = 0, skipped = 0, errorMessage = "")
        val labelIdle = renderStatusBarLabel(enabled = true, mode = IlSpyMode.CSharp, indexState = idle, bundle = bundle)
        assertEquals("statusbar.mode.label_base:${IlSpyMode.CSharp.displayName}", labelIdle)
    }

    @Test
    fun `renderStatusBarLabel uses Off label when disabled regardless of index state`() {
        // Off short-circuits before the index-state branches because the
        // plugin isn't intercepting anything; index status is irrelevant.
        val labelNoIndex = renderStatusBarLabel(enabled = false, mode = IlSpyMode.CSharp, indexState = null, bundle = bundle)
        assertEquals("statusbar.mode.label_off:", labelNoIndex)

        val building = IlSpySearchIndexStateSnapshot(phase = "Building", indexed = 1, total = 5, skipped = 0, errorMessage = "")
        val labelBuilding = renderStatusBarLabel(enabled = false, mode = IlSpyMode.IL, indexState = building, bundle = bundle)
        assertEquals("statusbar.mode.label_off:", labelBuilding)
    }

    @Test
    fun `decideStatusBarTransition Off when enabled disables and leaves mode alone`() {
        val t = decideStatusBarTransition(currentEnabled = true, currentMode = IlSpyMode.CSharp, selected = IlSpyStatusBarChoice.Off)
        assertEquals(IlSpyStatusBarTransition(applyEnabled = false, applyMode = null), t)
    }

    @Test
    fun `decideStatusBarTransition Off when already disabled is no-op`() {
        // Clicking Off twice should not generate a redundant rd push or
        // settings write; both fields null signal the widget to short-circuit.
        val t = decideStatusBarTransition(currentEnabled = false, currentMode = IlSpyMode.IL, selected = IlSpyStatusBarChoice.Off)
        assertEquals(IlSpyStatusBarTransition(applyEnabled = null, applyMode = null), t)
    }

    @Test
    fun `decideStatusBarTransition selecting same mode while enabled is no-op`() {
        val t = decideStatusBarTransition(
            currentEnabled = true,
            currentMode = IlSpyMode.CSharp,
            selected = IlSpyStatusBarChoice.ModeChoice(IlSpyMode.CSharp),
        )
        assertEquals(IlSpyStatusBarTransition(applyEnabled = null, applyMode = null), t)
    }

    @Test
    fun `decideStatusBarTransition selecting different mode while enabled switches mode only`() {
        val t = decideStatusBarTransition(
            currentEnabled = true,
            currentMode = IlSpyMode.CSharp,
            selected = IlSpyStatusBarChoice.ModeChoice(IlSpyMode.IL),
        )
        assertEquals(IlSpyStatusBarTransition(applyEnabled = null, applyMode = IlSpyMode.IL), t)
    }

    @Test
    fun `decideStatusBarTransition selecting same mode while disabled re-enables and keeps mode`() {
        // Re-enabling with the same persisted mode shouldn't push the mode
        // again — the backend already has the right mode persisted, so this
        // saves a redundant rd round-trip + re-decompile.
        val t = decideStatusBarTransition(
            currentEnabled = false,
            currentMode = IlSpyMode.IL,
            selected = IlSpyStatusBarChoice.ModeChoice(IlSpyMode.IL),
        )
        assertEquals(IlSpyStatusBarTransition(applyEnabled = true, applyMode = null), t)
    }

    @Test
    fun `decideStatusBarTransition selecting different mode while disabled re-enables and switches mode`() {
        val t = decideStatusBarTransition(
            currentEnabled = false,
            currentMode = IlSpyMode.IL,
            selected = IlSpyStatusBarChoice.ModeChoice(IlSpyMode.CSharpWithIL),
        )
        assertEquals(IlSpyStatusBarTransition(applyEnabled = true, applyMode = IlSpyMode.CSharpWithIL), t)
    }
}
