package com.cryptiklemur.riderilspy.internals

import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Schedule + termination contract for [attemptWithBackoff].
 * Covers the four exit conditions:
 *   1. attempt succeeds        -> true
 *   2. schedule exhausted      -> false
 *   3. sleeper signals interrupt -> false (and no further attempts)
 *   4. isDisposed flips during loop -> false (and no further attempts)
 */
class BackoffRetryTest {

    @Test
    fun `succeeds on first attempt and skips remaining schedule`() {
        val sleeps = mutableListOf<Long>()
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(0L, 100L, 200L),
            sleeper = { sleeps.add(it); true },
            attemptOnce = { attempts++; true },
        )
        assertTrue(ok)
        assertEquals(1, attempts)
        assertEquals(emptyList<Long>(), sleeps) // first slot is 0L -> no sleep
    }

    @Test
    fun `succeeds on later attempt and waits per schedule`() {
        val sleeps = mutableListOf<Long>()
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(0L, 100L, 200L),
            sleeper = { sleeps.add(it); true },
            attemptOnce = { attempts++; attempts == 3 },
        )
        assertTrue(ok)
        assertEquals(3, attempts)
        assertArrayEquals(longArrayOf(100L, 200L), sleeps.toLongArray())
    }

    @Test
    fun `returns false when schedule exhausted without success`() {
        val sleeps = mutableListOf<Long>()
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(0L, 100L, 200L),
            sleeper = { sleeps.add(it); true },
            attemptOnce = { attempts++; false },
        )
        assertFalse(ok)
        assertEquals(3, attempts)
        assertArrayEquals(longArrayOf(100L, 200L), sleeps.toLongArray())
    }

    @Test
    fun `sleeper interrupt aborts loop without further attempts`() {
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(0L, 100L, 200L),
            sleeper = { it != 100L }, // interrupt on the 2nd slot
            attemptOnce = { attempts++; false },
        )
        assertFalse(ok)
        assertEquals(1, attempts) // first attempt ran (0L slot), interrupt aborts before 2nd
    }

    @Test
    fun `isDisposed during loop aborts before next attempt`() {
        val disposeAfter = 2
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(0L, 100L, 200L, 400L),
            sleeper = { true },
            isDisposed = { attempts >= disposeAfter },
            attemptOnce = { attempts++; false },
        )
        assertFalse(ok)
        assertEquals(disposeAfter, attempts) // stops once attempts == 2
    }

    @Test
    fun `empty schedule returns false without invoking attempt`() {
        var attempts = 0
        val ok = attemptWithBackoff(
            schedule = longArrayOf(),
            attemptOnce = { attempts++; true },
        )
        assertFalse(ok)
        assertEquals(0, attempts)
    }

    @Test
    fun `production schedule has six attempts totalling 3 seconds of backoff`() {
        // Locks in the wall-clock budget so a future "let's bump 1500L to 1500000L"
        // refactor fails this test loudly instead of silently leaking the worker.
        assertEquals(6, initialModePushSchedule.size)
        assertEquals(3000L, initialModePushSchedule.sum())
        // First slot must be 0 so the first attempt happens immediately.
        assertEquals(0L, initialModePushSchedule.first())
    }
}
