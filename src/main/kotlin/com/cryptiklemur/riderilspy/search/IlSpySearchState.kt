package com.cryptiklemur.riderilspy.search

data class IlSpySearchIndexStateSnapshot(
    val phase: String,
    val indexed: Int,
    val total: Int,
    val skipped: Int,
    val errorMessage: String,
) {
    val isReady: Boolean get() = phase == "Ready"
}
