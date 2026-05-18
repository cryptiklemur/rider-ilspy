package com.cryptiklemur.riderilspy.search

import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
class DebouncerTest {

    @Test
    fun `rapid-fire triggers coalesce into exactly one invocation`() = runBlocking {
        val delayMs = 100L
        val count = AtomicInteger(0)
        val debouncer = Debouncer(CoroutineScope(Dispatchers.Default), delayMs)

        repeat(10) {
            debouncer.trigger { count.incrementAndGet() }
        }

        // Wait past the debounce window.
        Thread.sleep(delayMs * 3)

        assertEquals(1, count.get(), "10 trigger() calls within the window must produce exactly 1 invocation")
    }

    @Test
    fun `cancelPending prevents the queued action from firing`() = runBlocking {
        val delayMs = 100L
        val count = AtomicInteger(0)
        val debouncer = Debouncer(CoroutineScope(Dispatchers.Default), delayMs)

        debouncer.trigger { count.incrementAndGet() }
        debouncer.cancelPending()

        Thread.sleep(delayMs * 3)

        assertEquals(0, count.get(), "cancelPending() must prevent the pending action from firing")
    }

    @Test
    fun `action fires after delay when not superseded`() = runBlocking {
        val delayMs = 50L
        val count = AtomicInteger(0)
        val debouncer = Debouncer(CoroutineScope(Dispatchers.Default), delayMs)

        debouncer.trigger { count.incrementAndGet() }

        Thread.sleep(delayMs * 4)

        assertEquals(1, count.get(), "a single trigger() must fire its action after the delay")
    }
}
