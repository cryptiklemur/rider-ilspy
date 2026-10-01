package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.model.NavTarget

data class IlSpySearchMatch(
    val assemblyName: String,
    val target: String,
    val snippet: String,
    val navTarget: NavTarget,
)
