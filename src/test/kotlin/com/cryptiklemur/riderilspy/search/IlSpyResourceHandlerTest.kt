package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class IlSpyResourceHandlerTest {

    @Test
    fun `resourceTempRelativePath handles unix paths`() {
        val (dir, name) = resourceTempRelativePath("/home/user/proj/MyAssembly.dll", "embedded.txt")
        assertEquals("ilspy-resources/MyAssembly.dll", dir)
        assertEquals("embedded.txt", name)
    }

    @Test
    fun `resourceTempRelativePath handles windows backslash paths`() {
        val (dir, name) = resourceTempRelativePath("C:\\src\\foo\\bar.dll", "embedded.txt")
        assertEquals("ilspy-resources/bar.dll", dir)
        assertEquals("embedded.txt", name)
    }

    @Test
    fun `resourceTempRelativePath handles mixed separator paths`() {
        // Windows paths sometimes mix forward and back slashes (e.g. NuGet caches
        // normalize forward slashes inside a drive-rooted path).
        val (dir, _) = resourceTempRelativePath("C:/Users/me/nuget/cache/lib/net8.0/x.dll", "f")
        assertEquals("ilspy-resources/x.dll", dir)
    }

    @Test
    fun `resourceTempRelativePath bare basename has no separators`() {
        val (dir, _) = resourceTempRelativePath("BareName.dll", "f")
        assertEquals("ilspy-resources/BareName.dll", dir)
    }

    @Test
    fun `resourceTempRelativePath preserves fileName verbatim`() {
        val (_, name) = resourceTempRelativePath("/x/a.dll", "deeply.nested.resource.name")
        assertEquals("deeply.nested.resource.name", name)
    }
}
