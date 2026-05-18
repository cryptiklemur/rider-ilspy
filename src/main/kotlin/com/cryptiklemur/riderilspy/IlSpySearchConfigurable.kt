package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.cryptiklemur.riderilspy.internals.IlSpyFrontendSettings
import com.cryptiklemur.riderilspy.search.IlSpySearchOptionsPanel
import com.intellij.openapi.options.Configurable
import javax.swing.JComponent

class IlSpySearchConfigurable : Configurable {
    private var optionsPanel: IlSpySearchOptionsPanel? = null

    override fun getDisplayName(): String = RiderIlSpyBundle.message("options.page.search.name")

    override fun createComponent(): JComponent {
        val settings = IlSpyFrontendSettings.getInstance().search
        val panel = IlSpySearchOptionsPanel(settings)
        optionsPanel = panel
        return panel.component
    }

    override fun disposeUIResources() {
        optionsPanel = null
    }

    override fun isModified(): Boolean = false

    override fun apply() {}
}
