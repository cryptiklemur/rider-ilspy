package com.cryptiklemur.riderilspy

import com.jetbrains.rider.settings.simple.SimpleOptionsPage

class IlSpyOptionsPage : SimpleOptionsPage("ILSpy Decompiler", PAGE_ID) {
    override fun getId(): String = PAGE_ID

    companion object {
        const val PAGE_ID = "RiderIlSpyOptionsPage"
    }
}
