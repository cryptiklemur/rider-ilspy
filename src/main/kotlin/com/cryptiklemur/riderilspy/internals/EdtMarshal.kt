package com.cryptiklemur.riderilspy.internals

/**
 * Run [block] on the EDT (the IntelliJ event-dispatch thread), returning its
 * result and propagating exceptions to the caller.
 *
 * If [isDispatchThread] returns true, [block] runs inline. Otherwise [block]
 * is handed to [invokeAndWait] so the caller blocks until the EDT has run it.
 *
 * Exists because rd's `RdOptionalProperty.set` asserts the protocol dispatcher
 * (which is the EDT in Rider) and throws "Wrong thread" if called from any
 * other context, including pooled-thread executors used during service init.
 * Hoisting the dispatch decision into a pure helper makes the contract
 * testable without requiring a live IntelliJ harness.
 *
 * Exceptions thrown inside [block] cross the [invokeAndWait] boundary and
 * are rethrown from this function so the caller's try/catch (e.g. the retry
 * loop in `IlSpyProtocolHost.pushInitialModeWithRetry`) sees them.
 */
internal fun <T> runOnEdt(
    isDispatchThread: () -> Boolean,
    invokeAndWait: (Runnable) -> Unit,
    block: () -> T,
): T {
    if (isDispatchThread()) return block()
    var result: T? = null
    var thrown: Throwable? = null
    invokeAndWait(Runnable {
        try {
            result = block()
        } catch (error: Throwable) {
            thrown = error
        }
    })
    thrown?.let { throw it }
    @Suppress("UNCHECKED_CAST")
    return result as T
}
