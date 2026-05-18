package com.cryptiklemur.riderilspy.search

import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory

class IlSpySearchToolWindow : ToolWindowFactory {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val content = IlSpySearchToolWindowContent(project)
        val tc = ContentFactory.getInstance().createContent(content.panel, "", false)
        toolWindow.contentManager.addContent(tc)
    }
}
