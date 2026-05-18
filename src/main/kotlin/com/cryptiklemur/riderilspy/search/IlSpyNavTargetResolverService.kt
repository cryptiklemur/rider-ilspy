package com.cryptiklemur.riderilspy.search

import com.intellij.openapi.components.Service
import com.intellij.openapi.project.Project

@Service(Service.Level.PROJECT)
class IlSpyNavTargetResolverService(project: Project) {
    val resolver: IlSpyNavTargetResolver = IlSpyNavTargetResolver(
        project = project,
        resourceHandler = IlSpyResourceHandler(project),
    )
}
