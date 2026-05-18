package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.cryptiklemur.riderilspy.model.NavTarget
import com.intellij.openapi.project.Project

class IlSpyNavTargetResolver(
    private val project: Project,
    private val resourceHandler: IlSpyResourceHandler,
) {
    fun navigate(target: NavTarget) {
        when (target.kind) {
            "Code" -> navigateCode(target)
            "Resource" -> resourceHandler.handle(target)
            else -> error(RiderIlSpyBundle.message("nav.error.unknown_kind", target.kind))
        }
    }

    private fun navigateCode(target: NavTarget) {
        val gateway = project.getService(IlSpyExternalNavigationGateway::class.java)
        gateway.openDecompiledMember(
            assemblyPath = target.assemblyPath,
            metadataToken = target.metadataToken,
            ilOffset = target.ilOffset,
        )
    }
}
