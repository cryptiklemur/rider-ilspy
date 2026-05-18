package com.cryptiklemur.riderilspy.search

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.cryptiklemur.riderilspy.SaveAsProjectAction

/**
 * Toolbar action that requests a full rescan of the ILSpy search index.
 *
 * The rd model exposes `rescanAssembly(path: String)` per-assembly but has no
 * "rescan all" signal (planned for a future protocol revision). Until that
 * signal lands, this action posts a notification explaining the limitation.
 * Per-assembly rescans remain available via the context menu on each assembly.
 *
 * TODO(Phase 12+): add `signal("rescanAll", void)` to the rd model and fire it
 * here instead of showing this notification.
 */
class IlSpyRescanAllAction : AnAction("Rescan ILSpy Search Index", "Rebuild the ILSpy search index for the open solution", null) {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val group = NotificationGroupManager.getInstance()
            .getNotificationGroup(SaveAsProjectAction.NOTIFICATION_GROUP_ID)
            ?: return
        group.createNotification(
            "ILSpy Search",
            "Rescan All is not yet wired — use rescanAssembly per-assembly via the context menu. " +
                "(Planned: add rescanAll signal to the rd model in Phase 12+.)",
            NotificationType.INFORMATION,
        ).notify(project)
    }
}
