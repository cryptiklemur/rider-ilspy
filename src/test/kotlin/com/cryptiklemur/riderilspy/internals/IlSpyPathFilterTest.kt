package com.cryptiklemur.riderilspy.internals

import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class IlSpyPathFilterTest {
    @Test
    fun `matches unix decompiler cache path`() {
        assertTrue(isIlSpyDecompiledPath("/home/me/.local/share/JetBrains/Rider/DecompilerCache/RiderIlSpy/abc/file.cs"))
    }

    @Test
    fun `matches windows decompiler cache path`() {
        assertTrue(isIlSpyDecompiledPath("C:\\Users\\me\\AppData\\Local\\JetBrains\\Rider\\DecompilerCache\\RiderIlSpy\\abc\\file.cs"))
    }

    @Test
    fun `does not match generic source file`() {
        assertFalse(isIlSpyDecompiledPath("/home/me/projects/Foo/Bar.cs"))
    }

    @Test
    fun `does not match other decompiler cache (different provider)`() {
        assertFalse(isIlSpyDecompiledPath("/home/me/.local/share/JetBrains/Rider/DecompilerCache/SomeOtherProvider/file.cs"))
    }

    @Test
    fun `does not match windows path without trailing separator`() {
        assertFalse(isIlSpyDecompiledPath("C:\\DecompilerCache\\RiderIlSpyOther\\file.cs"))
    }

    @Test
    fun `empty string does not match`() {
        assertFalse(isIlSpyDecompiledPath(""))
    }
}
