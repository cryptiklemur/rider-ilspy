package com.cryptiklemur.riderilspy

import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Files
import java.nio.file.Path
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

class IlSpyReadySignalWatcherTest {

    private var originalUserHome: String? = null

    @BeforeEach
    fun overrideUserHome(@TempDir tempHome: Path) {
        originalUserHome = System.getProperty("user.home")
        System.setProperty("user.home", tempHome.toString())
    }

    @AfterEach
    fun restoreUserHome() {
        originalUserHome?.let { System.setProperty("user.home", it) } ?: System.clearProperty("user.home")
    }

    @Test
    fun `start creates the shared directory when missing`() {
        val watcher = IlSpyReadySignalWatcher(onReady = {})
        val sharedDir = IlSpyFrontendSettings.sharedDir()
        assertTrue(!sharedDir.exists(), "precondition: shared dir should not exist")

        try {
            watcher.start()
            assertTrue(sharedDir.exists(), "watcher.start() should create the shared dir")
            assertTrue(sharedDir.isDirectory, "shared dir should be a directory")
        } finally {
            watcher.stop()
        }
    }

    @Test
    fun `invokes onReady when ready_txt is created in the shared dir`() {
        val latch = CountDownLatch(1)
        val invocations = AtomicInteger(0)
        val watcher = IlSpyReadySignalWatcher(onReady = {
            invocations.incrementAndGet()
            latch.countDown()
        })

        try {
            watcher.start()
            val readyFile = IlSpyFrontendSettings.sharedReadyFile().toPath()
            Files.writeString(readyFile, "ok")

            assertTrue(
                latch.await(5, TimeUnit.SECONDS),
                "onReady should fire within 5 seconds of ready.txt being created",
            )
            assertEquals(1, invocations.get())
        } finally {
            watcher.stop()
        }
    }

    @Test
    fun `ignores writes to unrelated filenames in the shared dir`() {
        val invocations = AtomicInteger(0)
        val watcher = IlSpyReadySignalWatcher(onReady = { invocations.incrementAndGet() })

        try {
            watcher.start()
            val unrelated = IlSpyFrontendSettings.sharedDir().toPath().resolve("noise.txt")
            Files.writeString(unrelated, "should-not-trigger")

            Thread.sleep(500)

            assertEquals(0, invocations.get(), "writes to other filenames must not trigger onReady")
        } finally {
            watcher.stop()
        }
    }

    @Test
    fun `stop is idempotent and safe to call without start`() {
        val watcher = IlSpyReadySignalWatcher(onReady = {})

        watcher.stop()
        watcher.stop()
    }
}
