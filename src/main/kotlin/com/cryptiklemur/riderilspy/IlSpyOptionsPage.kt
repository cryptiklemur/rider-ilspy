package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.jetbrains.rider.settings.simple.SimpleOptionsPage

class IlSpyOptionsPage : SimpleOptionsPage(RiderIlSpyBundle.message("options.page.decompiler.name"), PAGE_ID) {
    override fun getId(): String = PAGE_ID

    companion object {
        // Framework-imposed triplication: this id must stay in sync with
        //   - ReSharperPlugin/RiderIlSpy/IlSpyOptionsPage.cs (Pid constant)
        //   - src/main/resources/META-INF/plugin.xml (<applicationConfigurable id=...>)
        // Treat this constant as the canonical source when renaming.
        const val PAGE_ID = "RiderIlSpyOptionsPage"
    }
}
