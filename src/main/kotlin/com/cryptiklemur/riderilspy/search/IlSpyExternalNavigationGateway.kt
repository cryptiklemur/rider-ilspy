package com.cryptiklemur.riderilspy.search

/**
 * Thin facade over the decompiled-member navigation stack that lets
 * [IlSpyNavTargetResolver] open a search result in the editor without
 * depending directly on the rd protocol or the C# backend's
 * IlSpyExternalSourcesProvider.
 *
 * The production implementation is a project service registered in plugin.xml
 * that delegates to the existing go-to-declaration infrastructure (Phase 12).
 * Tests and Phase 8 consumers depend only on this interface.
 */
interface IlSpyExternalNavigationGateway {
    /**
     * Open the decompiled source for the member identified by [metadataToken]
     * inside [assemblyPath], scrolled to [ilOffset] if non-negative.
     */
    fun openDecompiledMember(assemblyPath: String, metadataToken: Int, ilOffset: Int)
}
