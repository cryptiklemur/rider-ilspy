package com.cryptiklemur.riderilspy.internals

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertSame
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Regression coverage for the "Wrong thread RdOptionalProperty" crash where
 * `IlSpyProtocolHost` was setting `riderIlSpyModel.mode` directly from a
 * pooled-thread executor during init. The fix routes the call through
 * [runOnEdt], so these tests pin the dispatch policy: on-EDT runs inline,
 * off-EDT marshals via `invokeAndWait`, exceptions cross the boundary.
 */
class EdtMarshalTest {

    @Test
    fun `runs block inline when caller is already on dispatch thread`() {
        var marshalled = false
        val result = runOnEdt(
            isDispatchThread = { true },
            invokeAndWait = { marshalled = true; it.run() },
        ) { "ok" }

        assertEquals("ok", result)
        assertFalse(marshalled, "invokeAndWait must NOT be called when already on dispatch thread")
    }

    @Test
    fun `marshals through invokeAndWait when caller is off dispatch thread`() {
        var marshalCount = 0
        var ranBlock = false
        val result = runOnEdt(
            isDispatchThread = { false },
            invokeAndWait = { runnable -> marshalCount++; runnable.run() },
        ) {
            ranBlock = true
            42
        }

        assertEquals(1, marshalCount, "invokeAndWait must be called exactly once when off dispatch thread")
        assertTrue(ranBlock)
        assertEquals(42, result)
    }

    @Test
    fun `propagates exception thrown inside off-EDT block to caller`() {
        // This is the core of the bug-1 regression: previously the .set() call
        // ran on the wrong thread and IllegalStateException escaped raw. The
        // helper must surface block exceptions to the retry loop so it can
        // count failures and back off, not swallow them.
        val boom = IllegalStateException("Wrong thread RdOptionalProperty")
        val thrown = assertThrows(IllegalStateException::class.java) {
            runOnEdt<Unit>(
                isDispatchThread = { false },
                invokeAndWait = { it.run() },
            ) { throw boom }
        }
        assertSame(boom, thrown, "the original exception instance must reach the caller, not a wrapper")
    }

    @Test
    fun `propagates exception thrown inside on-EDT block to caller`() {
        val boom = RuntimeException("inline path")
        val thrown = assertThrows(RuntimeException::class.java) {
            runOnEdt<Unit>(
                isDispatchThread = { true },
                invokeAndWait = { it.run() },
            ) { throw boom }
        }
        assertSame(boom, thrown)
    }

    @Test
    fun `does not call invokeAndWait when on dispatch thread even if block throws`() {
        var marshalled = false
        assertThrows(RuntimeException::class.java) {
            runOnEdt<Unit>(
                isDispatchThread = { true },
                invokeAndWait = { marshalled = true; it.run() },
            ) { throw RuntimeException() }
        }
        assertFalse(marshalled)
    }

    @Test
    fun `evaluates isDispatchThread once per call`() {
        var checks = 0
        runOnEdt(
            isDispatchThread = { checks++; false },
            invokeAndWait = { it.run() },
        ) { Unit }
        assertEquals(1, checks)
    }

    @Test
    fun `block running on off-EDT path returns null result if block returns null`() {
        // Nullable result reaches the caller — guards against the unchecked cast
        // inside runOnEdt accidentally NPE'ing on null returns.
        val result: String? = runOnEdt(
            isDispatchThread = { false },
            invokeAndWait = { it.run() },
        ) { null as String? }
        assertNull(result)
    }
}
