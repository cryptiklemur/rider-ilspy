package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.StatusBarWidgetFactory

class IlSpyModeStatusBarWidgetFactory : StatusBarWidgetFactory {
    override fun getId(): String = WIDGET_ID
    override fun getDisplayName(): String = RiderIlSpyBundle.message("statusbar.mode.display_name")
    override fun createWidget(project: Project): StatusBarWidget = IlSpyModeStatusBarWidget(project)
    override fun isAvailable(project: Project): Boolean = true
    override fun canBeEnabledOn(statusBar: StatusBar): Boolean = true

    companion object {
        const val WIDGET_ID = "RiderIlSpy.ModeStatusBarWidget"
    }
}
