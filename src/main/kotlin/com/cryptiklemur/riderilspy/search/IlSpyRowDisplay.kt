package com.cryptiklemur.riderilspy.search

data class IlSpyRowDisplay(
    val primary: String,
    val secondary: String,
    val kind: String?,
)

private val SYMBOL_KINDS = setOf("Type", "Method", "Field", "Property", "Event", "Namespace")

fun rowDisplay(target: String, snippet: String): IlSpyRowDisplay {
    if (snippet !in SYMBOL_KINDS) {
        return IlSpyRowDisplay(primary = snippet, secondary = target, kind = null)
    }
    if (snippet == "Namespace") {
        return IlSpyRowDisplay(primary = target, secondary = "", kind = snippet)
    }
    val cut = target.lastIndexOf('.')
    if (cut <= 0) return IlSpyRowDisplay(primary = target, secondary = "", kind = snippet)
    return IlSpyRowDisplay(
        primary = target.substring(cut + 1),
        secondary = target.substring(0, cut),
        kind = snippet,
    )
}
