package com.cryptiklemur.riderilspy.search

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Coalesces rapid-fire trigger() calls so only the last one fires its action,
 * after [delayMs] elapses without another trigger(). Extracted from
 * [IlSpySearchClient] so it can be tested without the rd protocol surface.
 */
class Debouncer(private val scope: CoroutineScope, private val delayMs: Long) {
    private var pending: Job? = null

    fun trigger(action: () -> Unit) {
        pending?.cancel()
        pending = scope.launch {
            delay(delayMs)
            action()
        }
    }

    fun cancelPending() {
        pending?.cancel()
        pending = null
    }
}
