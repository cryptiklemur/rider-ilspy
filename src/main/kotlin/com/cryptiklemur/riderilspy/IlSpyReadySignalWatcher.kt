package com.cryptiklemur.riderilspy

import com.intellij.openapi.diagnostic.Logger
import java.nio.file.ClosedWatchServiceException
import java.nio.file.FileSystems
import java.nio.file.Path
import java.nio.file.StandardWatchEventKinds
import java.nio.file.WatchService

/**
 * Watches the shared IPC directory for the C# backend's ready.txt signal and invokes
 * [onReady] whenever the file is created or modified. The watch runs on a daemon
 * thread, so failure to start (e.g. unwriteable home directory) is logged but never
 * blocks the IDE shell.
 */
class IlSpyReadySignalWatcher(private val onReady: () -> Unit) {

    private var watchService: WatchService? = null
    private var watchThread: Thread? = null

    fun start() {
        try {
            val dir = IlSpyFrontendSettings.sharedDir()
            if (!dir.exists()) dir.mkdirs()
            val ws = FileSystems.getDefault().newWatchService()
            dir.toPath().register(
                ws,
                StandardWatchEventKinds.ENTRY_MODIFY,
                StandardWatchEventKinds.ENTRY_CREATE,
            )
            watchService = ws
            val thread = Thread(::pump, "RiderIlSpy-ready-watcher").apply { isDaemon = true }
            watchThread = thread
            thread.start()
        } catch (error: Exception) {
            // If we can't watch, mode flips will require the user to re-trigger
            // navigation to see the new content. Log so the cause is discoverable
            // without spamming the UI side.
            LOG.warn("Failed to start ready-signal watcher on ${IlSpyFrontendSettings.sharedDir()}", error)
        }
    }

    fun stop() {
        try {
            watchService?.close()
        } catch (_: Exception) {
            // best-effort close on shutdown; nothing to do if the service is already gone
        }
        watchService = null
        watchThread?.interrupt()
        watchThread = null
    }

    private fun pump() {
        val ws = watchService ?: return
        try {
            while (!Thread.currentThread().isInterrupted) {
                val key = ws.take()
                var sawReady = false
                for (event in key.pollEvents()) {
                    val ctx = event.context()
                    if (ctx is Path && ctx.fileName.toString() == IlSpyFrontendSettings.READY_FILE_NAME) {
                        sawReady = true
                        break
                    }
                }
                if (sawReady) onReady()
                if (!key.reset()) break
            }
        } catch (_: InterruptedException) {
            // shutting down
        } catch (_: ClosedWatchServiceException) {
            // shutting down
        }
    }

    companion object {
        private val LOG: Logger = Logger.getInstance(IlSpyReadySignalWatcher::class.java)
    }
}
