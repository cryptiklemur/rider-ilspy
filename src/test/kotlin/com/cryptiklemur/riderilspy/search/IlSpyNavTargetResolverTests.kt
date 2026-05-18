package com.cryptiklemur.riderilspy.search

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/**
 * Tests the pure [resourceKindToAction] routing helper extracted from
 * [IlSpyResourceHandler]. Kept separate from [IlSpyResourceHandler] itself
 * so it can be tested without a [com.intellij.openapi.project.Project].
 */
class IlSpyNavTargetResolverTests {

    @Test
    fun `image mime hint routes to OpenAsImage`() {
        assertEquals(ResourceAction.OpenAsImage, resourceKindToAction("image"))
    }

    @Test
    fun `text mime hint routes to OpenAsText`() {
        assertEquals(ResourceAction.OpenAsText, resourceKindToAction("text"))
    }

    @Test
    fun `resources mime hint routes to OpenAsText`() {
        assertEquals(ResourceAction.OpenAsText, resourceKindToAction("resources"))
    }

    @Test
    fun `unknown mime hint routes to PromptSaveAs`() {
        assertEquals(ResourceAction.PromptSaveAs, resourceKindToAction("binary"))
        assertEquals(ResourceAction.PromptSaveAs, resourceKindToAction(""))
        assertEquals(ResourceAction.PromptSaveAs, resourceKindToAction("application/octet-stream"))
    }
}
