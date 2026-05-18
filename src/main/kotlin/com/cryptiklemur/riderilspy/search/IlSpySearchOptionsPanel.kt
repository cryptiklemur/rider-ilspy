package com.cryptiklemur.riderilspy.search

import com.intellij.ui.dsl.builder.bindIntText
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.panel
import javax.swing.JComponent

class IlSpySearchOptionsPanel(private val settings: IlSpySearchSettings) {
    val component: JComponent = panel {
        group("Search") {
            row {
                checkBox("Enable background indexer")
                    .bindSelected(settings::indexerEnabled)
            }
            row {
                checkBox("Persist index between sessions")
                    .bindSelected(settings::persistBetweenSessions)
            }
            row("Max results per query:") {
                intTextField(1..50000)
                    .bindIntText(settings::maxResultsPerQuery)
            }
            row("Excluded assemblies:") {
                comment("Add assembly file names (one per line) in the global settings file.")
            }
        }
    }
}
