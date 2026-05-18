package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.IlSpySaveAsProjectAction
import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
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
// Text + description are pulled from plugin.xml (resolved via `action.ILSpy.RescanAll.text`
// in the resource bundle), so the no-arg AnAction() constructor is sufficient — the
// IntelliJ registration provides live values that override any constructor defaults.
class IlSpyRescanAllAction : AnAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val group = NotificationGroupManager.getInstance()
            .getNotificationGroup(IlSpySaveAsProjectAction.NOTIFICATION_GROUP_ID)
            ?: return
        group.createNotification(
            RiderIlSpyBundle.message("action.rescan.notification.title"),
            RiderIlSpyBundle.message("action.rescan.notification.message"),
            NotificationType.INFORMATION,
        ).notify(project)
    }
}
