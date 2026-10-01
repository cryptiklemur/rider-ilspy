package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class IlSpySearchQueryTypesTest {
    @Test
    fun `dropdown lists ilspy's query types in ilspy's order`() {
        assertEquals(
            listOf(
                "TypeAndMember",
                "Type",
                "Member",
                "Method",
                "Field",
                "Property",
                "Event",
                "Constant",
                "Token",
                "Resource",
                "Assembly",
                "Namespace",
            ),
            IlSpySearchToolWindowContent.QUERY_TYPE_IDS,
        )
    }

    @Test
    fun `every query type has a label`() {
        for (id in IlSpySearchToolWindowContent.QUERY_TYPE_IDS) {
            val label = RiderIlSpyBundle.message(IlSpySearchToolWindowContent.bundleKeyFor(id))
            assertTrue(label.isNotBlank() && !label.startsWith("!"), "missing label for $id")
        }
    }

    @Test
    fun `bundle keys are snake cased`() {
        assertEquals(
            "search.toolwindow.query_type.type_and_member",
            IlSpySearchToolWindowContent.bundleKeyFor("TypeAndMember"),
        )
        assertEquals(
            "search.toolwindow.query_type.type",
            IlSpySearchToolWindowContent.bundleKeyFor("Type"),
        )
    }

    @Test
    fun `search everywhere queries the type-and-member and constant modes`() {
        assertEquals(listOf("TypeAndMember", "Constant"), IlSpySearchEverywhereContributor.QUERY_TYPES)
    }
}
