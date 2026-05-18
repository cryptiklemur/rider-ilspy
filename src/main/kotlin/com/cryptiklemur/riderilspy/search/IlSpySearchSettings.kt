package com.cryptiklemur.riderilspy.search

import com.intellij.util.xmlb.annotations.OptionTag

class IlSpySearchSettings {
    @OptionTag var indexerEnabled: Boolean = true
    @OptionTag var persistBetweenSessions: Boolean = true
    @OptionTag var maxResultsPerQuery: Int = 5000
    @OptionTag var excludedAssemblies: MutableList<String> = mutableListOf()
}
