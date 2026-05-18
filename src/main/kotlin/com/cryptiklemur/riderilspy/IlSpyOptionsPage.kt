package com.cryptiklemur.riderilspy

import com.jetbrains.rider.settings.simple.SimpleOptionsPage

class IlSpyOptionsPage : SimpleOptionsPage("ILSpy Decompiler", PAGE_ID) {
    override fun getId(): String = PAGE_ID

    companion object {
        // Framework-imposed triplication: this id must stay in sync with
        //   - ReSharperPlugin/RiderIlSpy/IlSpyOptionsPage.cs (Pid constant)
        //   - src/main/resources/META-INF/plugin.xml (<applicationConfigurable id=...>)
        // Treat this constant as the canonical source when renaming.
        const val PAGE_ID = "RiderIlSpyOptionsPage"
    }
}


class IlSpySearchConfigurable : com.intellij.openapi.options.Configurable {
    private val settings = com.cryptiklemur.riderilspy.internals.IlSpyFrontendSettings.getInstance().search
    private val optionsPanel = com.cryptiklemur.riderilspy.search.IlSpySearchOptionsPanel(settings)

    override fun getDisplayName(): String = "Search"

    override fun createComponent(): javax.swing.JComponent = optionsPanel.component

    override fun isModified(): Boolean = false

    override fun apply() {}
}
