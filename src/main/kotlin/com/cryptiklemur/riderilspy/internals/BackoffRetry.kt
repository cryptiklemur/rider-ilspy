package com.cryptiklemur.riderilspy.internals

/**
 * Bounded backoff retry: invokes [attemptOnce] up to `schedule.size` times,
 * sleeping for each entry in [schedule] before the corresponding attempt
 * (a leading 0L means "attempt immediately"). Returns true the first time
 * [attemptOnce] returns true; false if the schedule is exhausted, the
 * sleeper signals interruption, or [isDisposed] returns true.
 *
 * Extracted from [IlSpyProtocolHost.pushInitialModeWithRetry] so the
 * schedule + termination logic can be unit-tested without an IDE harness.
 * The protocol-bind race that motivated this helper requires a real Rider
 * instance to reproduce, but the schedule contract (early-exit on dispose,
 * interrupt propagation, last-attempt detection) is pure policy.
 *
 * @param schedule per-attempt sleep durations in milliseconds. The number
 *   of entries is the attempt count.
 * @param sleeper sleep implementation; returns true if the sleep completed
 *   normally, false if interrupted (caller is responsible for restoring
 *   the interrupt flag before returning false). Default delegates to
 *   [Thread.sleep] with [InterruptedException] handling.
 * @param isDisposed checked before each attempt; returning true terminates
 *   the loop without invoking [attemptOnce].
 * @param attemptOnce the operation to retry; returning true terminates
 *   the loop successfully.
 */
internal fun attemptWithBackoff(
    schedule: LongArray,
    sleeper: (Long) -> Boolean = ::defaultBackoffSleep,
    isDisposed: () -> Boolean = { false },
    attemptOnce: () -> Boolean,
): Boolean {
    for (backoff in schedule) {
        if (backoff > 0L && !sleeper(backoff)) return false
        if (isDisposed()) return false
        if (attemptOnce()) return true
    }
    return false
}

/**
 * Schedule used by [IlSpyProtocolHost.pushInitialModeWithRetry]. Lives at
 * package scope so the schedule contract is unit-testable without exposing
 * the host's private internals.
 *
 * 0 / 100 / 200 / 400 / 800 / 1500 ms — covers the cold-start solution-open
 * race while bounding total wait to ~3 seconds so a permanently unbound
 * state can't leak the worker.
 */
internal val initialModePushSchedule: LongArray = longArrayOf(0L, 100L, 200L, 400L, 800L, 1500L)

internal fun defaultBackoffSleep(ms: Long): Boolean {
    return try {
        Thread.sleep(ms)
        true
    } catch (_: InterruptedException) {
        Thread.currentThread().interrupt()
        false
    }
}
