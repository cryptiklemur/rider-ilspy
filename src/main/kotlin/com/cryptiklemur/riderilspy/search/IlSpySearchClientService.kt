package com.cryptiklemur.riderilspy.search

import com.intellij.openapi.components.Service
import com.intellij.openapi.project.Project
import com.jetbrains.rd.platform.util.idea.LifetimedService
import com.jetbrains.rd.util.lifetime.Lifetime
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel

@Service(Service.Level.PROJECT)
class IlSpySearchClientService(project: Project) : LifetimedService() {
    val lifetime: Lifetime get() = serviceLifetime
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob())
    val client: IlSpySearchClient = IlSpySearchClient(
        project = project,
        lifetime = serviceLifetime,
        scope = scope,
    )

    init {
        serviceLifetime.onTermination { scope.cancel() }
    }
}
