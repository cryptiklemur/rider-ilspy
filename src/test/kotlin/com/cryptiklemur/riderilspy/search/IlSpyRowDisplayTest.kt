package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class IlSpyRowDisplayTest {
    @Test
    fun `member rows lead with the member name and trail with its declaring type`() {
        val d = rowDisplay("System.Text.StringBuilder.AppendLine", "Method")

        assertEquals("AppendLine", d.primary)
        assertEquals("System.Text.StringBuilder", d.secondary)
        assertEquals("Method", d.kind)
    }

    @Test
    fun `type rows lead with the type name and trail with its namespace`() {
        val d = rowDisplay("System.Text.StringBuilder", "Type")

        assertEquals("StringBuilder", d.primary)
        assertEquals("System.Text", d.secondary)
    }

    @Test
    fun `namespace rows have no secondary part`() {
        val d = rowDisplay("System.Text", "Namespace")

        assertEquals("System.Text", d.primary)
        assertEquals("", d.secondary)
        assertEquals("Namespace", d.kind)
    }

    @Test
    fun `literal rows keep the matched value as the primary part`() {
        val d = rowDisplay("#0A000123", "Invalid cast from {0} to {1}")

        assertEquals("Invalid cast from {0} to {1}", d.primary)
        assertEquals("#0A000123", d.secondary)
        assertNull(d.kind)
    }

    @Test
    fun `assembly rows keep their path as the primary part`() {
        val d = rowDisplay("System.Runtime", "/nuget/System.Runtime.dll")

        assertEquals("/nuget/System.Runtime.dll", d.primary)
        assertEquals("System.Runtime", d.secondary)
        assertNull(d.kind)
    }

    @Test
    fun `a global namespace type keeps its whole name as the primary part`() {
        val d = rowDisplay("TopLevelType", "Type")

        assertEquals("TopLevelType", d.primary)
        assertEquals("", d.secondary)
    }
}
