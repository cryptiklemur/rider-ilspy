package com.cryptiklemur.riderilspy

import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Path
import kotlin.io.path.readText

class IlSpyFrontendSettingsTest {

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
    fun `getState returns the loaded state contents`() {
        val settings = IlSpyFrontendSettings()
        settings.loadState(IlSpyFrontendSettings.State(mode = IlSpyMode.IL.backendName))

        assertEquals(IlSpyMode.IL.backendName, settings.state.mode)
    }

    @Test
    fun `mode getter decodes backendName via IlSpyMode_fromBackendName`() {
        val settings = IlSpyFrontendSettings()
        settings.loadState(IlSpyFrontendSettings.State(mode = IlSpyMode.CSharpWithIL.backendName))

        assertEquals(IlSpyMode.CSharpWithIL, settings.mode)
    }

    @Test
    fun `mode getter falls back to CSharp when state holds an unknown identifier`() {
        val settings = IlSpyFrontendSettings()
        settings.loadState(IlSpyFrontendSettings.State(mode = "not-a-real-mode"))

        assertEquals(IlSpyMode.CSharp, settings.mode)
    }

    @Test
    fun `setting mode writes backendName to the shared mode file atomically`() {
        val settings = IlSpyFrontendSettings()

        settings.mode = IlSpyMode.CSharpWithIL

        val sharedFile = IlSpyFrontendSettings.sharedModeFile()
        assertTrue(sharedFile.exists(), "shared mode file should be created on write")
        assertEquals(IlSpyMode.CSharpWithIL.backendName, sharedFile.toPath().readText(Charsets.UTF_8))
        assertEquals(IlSpyMode.CSharpWithIL, settings.mode)
    }


    @Test
    fun `setting mode twice overwrites the shared mode file with the latest value`() {
        val settings = IlSpyFrontendSettings()

        settings.mode = IlSpyMode.IL
        settings.mode = IlSpyMode.CSharpWithIL

        val sharedFile = IlSpyFrontendSettings.sharedModeFile()
        assertEquals(IlSpyMode.CSharpWithIL.backendName, sharedFile.toPath().readText(Charsets.UTF_8))
    }

    @Test
    fun `setting mode creates the shared directory when it does not exist`() {
        val settings = IlSpyFrontendSettings()
        val sharedDir = IlSpyFrontendSettings.sharedDir()
        assertTrue(!sharedDir.exists(), "precondition: shared dir should not exist before first write")

        settings.mode = IlSpyMode.IL

        assertTrue(sharedDir.exists(), "shared dir should be created on first write")
        assertTrue(sharedDir.isDirectory, "shared dir should be a directory")
    }

    @Test
    fun `sharedDir resolves under user_home with the canonical directory name`() {
        val home = System.getProperty("user.home")
        val expected = java.io.File(home, IlSpyFrontendSettings.SHARED_DIR_NAME)

        assertEquals(expected, IlSpyFrontendSettings.sharedDir())
    }

    @Test
    fun `sharedReadyFile lives inside sharedDir with the canonical ready filename`() {
        val ready = IlSpyFrontendSettings.sharedReadyFile()

        assertEquals(IlSpyFrontendSettings.sharedDir(), ready.parentFile)
        assertEquals(IlSpyFrontendSettings.READY_FILE_NAME, ready.name)
    }

    @Test
    fun `sharedModeFile lives inside sharedDir with the canonical mode filename`() {
        val mode = IlSpyFrontendSettings.sharedModeFile()

        assertEquals(IlSpyFrontendSettings.sharedDir(), mode.parentFile)
        assertEquals(IlSpyFrontendSettings.MODE_FILE_NAME, mode.name)
    }
}
