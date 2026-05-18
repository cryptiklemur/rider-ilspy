package com.cryptiklemur.riderilspy.search

enum class ResourceAction { OpenAsImage, OpenAsText, PromptSaveAs }

fun resourceKindToAction(mimeHint: String): ResourceAction = when (mimeHint) {
    "image" -> ResourceAction.OpenAsImage
    "text", "resources" -> ResourceAction.OpenAsText
    else -> ResourceAction.PromptSaveAs
}
