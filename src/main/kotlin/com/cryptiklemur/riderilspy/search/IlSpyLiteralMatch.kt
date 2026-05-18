package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.model.NavTarget

data class IlSpyLiteralMatch(
    val assemblyName: String,
    val containingMember: String,
    val snippet: String,
    val navTarget: NavTarget,
)
