package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.intellij.ui.dsl.builder.bindIntText
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.panel
import javax.swing.JComponent

class IlSpySearchOptionsPanel(private val settings: IlSpySearchSettings) {
    val component: JComponent = panel {
        group(RiderIlSpyBundle.message("search.options.group.title")) {
            row {
                checkBox(RiderIlSpyBundle.message("search.options.enable_indexer"))
                    .bindSelected(settings::indexerEnabled)
            }
            row {
                checkBox(RiderIlSpyBundle.message("search.options.persist_index"))
                    .bindSelected(settings::persistBetweenSessions)
            }
            row(RiderIlSpyBundle.message("search.options.max_results")) {
                intTextField(1..50000)
                    .bindIntText(settings::maxResultsPerQuery)
            }
            row(RiderIlSpyBundle.message("search.options.excluded_assemblies")) {
                comment(RiderIlSpyBundle.message("search.options.excluded_assemblies.comment"))
            }
        }
    }
}
