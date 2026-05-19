package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class ResolveAndOpenOffEdtTest {

    @Test
    fun `dispatches resolveFile on pooled, then open on edt when found`() {
        val callOrder = mutableListOf<String>()
        val opened = mutableListOf<Any>()

        resolveAndOpenOffEdt(
            runOnPooled = { r -> callOrder += "pooled"; r.run() },
            runOnEdt = { r -> callOrder += "edt"; r.run() },
            resolveFile = { callOrder += "resolve"; "vfile-stub" },
            onOpen = { v -> callOrder += "open"; opened += v },
            onMissing = { callOrder += "missing" },
        )

        // Pooled must wrap resolve; edt must wrap open. Missing must not fire.
        assertEquals(listOf("pooled", "resolve", "edt", "open"), callOrder)
        assertEquals(listOf<Any>("vfile-stub"), opened)
    }

    @Test
    fun `routes null resolveFile result to onMissing on edt`() {
        val callOrder = mutableListOf<String>()
        var openedValue: Any? = null

        resolveAndOpenOffEdt(
            runOnPooled = { r -> callOrder += "pooled"; r.run() },
            runOnEdt = { r -> callOrder += "edt"; r.run() },
            resolveFile = { callOrder += "resolve"; null },
            onOpen = { v -> openedValue = v; callOrder += "open" },
            onMissing = { callOrder += "missing" },
        )

        assertEquals(listOf("pooled", "resolve", "edt", "missing"), callOrder)
        assertNull(openedValue, "onOpen must not fire when resolveFile returns null")
    }

    @Test
    fun `resolveFile runs inside the pooled runnable not before it`() {
        val captured = mutableListOf<Runnable>()
        var resolveCalled = false

        resolveAndOpenOffEdt(
            runOnPooled = { r -> captured += r }, // capture, do NOT run
            runOnEdt = { r -> r.run() },
            resolveFile = { resolveCalled = true; "x" },
            onOpen = { },
            onMissing = { },
        )

        // Without invoking the captured pooled runnable, resolveFile must not
        // have been called yet — proves resolve happens off the calling thread.
        assertEquals(1, captured.size)
        assertTrue(!resolveCalled, "resolveFile must not run before runOnPooled invokes the runnable")

        captured.single().run()
        assertTrue(resolveCalled, "resolveFile must run when the captured pooled runnable executes")
    }

    @Test
    fun `onOpen runs inside the edt runnable not directly after resolve`() {
        var capturedEdt: Runnable? = null
        var openCalled = false

        resolveAndOpenOffEdt(
            runOnPooled = { r -> r.run() }, // pooled runs immediately
            runOnEdt = { r -> capturedEdt = r }, // capture edt, do NOT run
            resolveFile = { "vfile" },
            onOpen = { openCalled = true },
            onMissing = { },
        )

        // Until the captured edt runnable runs, onOpen must not have fired —
        // proves open() is gated behind the EDT dispatch.
        assertTrue(!openCalled, "onOpen must not run before runOnEdt invokes the runnable")
        capturedEdt?.run()
        assertTrue(openCalled, "onOpen must run when the captured edt runnable executes")
    }
}
