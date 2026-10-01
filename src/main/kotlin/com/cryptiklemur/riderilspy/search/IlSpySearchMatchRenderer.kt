package com.cryptiklemur.riderilspy.search

import com.intellij.ui.ColoredListCellRenderer
import com.intellij.ui.SimpleTextAttributes
import javax.swing.JList

class IlSpySearchMatchRenderer : ColoredListCellRenderer<IlSpySearchMatch>() {
    override fun customizeCellRenderer(
        list: JList<out IlSpySearchMatch>,
        value: IlSpySearchMatch,
        index: Int,
        selected: Boolean,
        hasFocus: Boolean,
    ) {
        val display = rowDisplay(value.target, value.snippet)
        icon = IlSpySymbolIcons.forKind(display.kind)
        if (display.kind == null) {
            append("\"${display.primary}\"", SimpleTextAttributes.REGULAR_BOLD_ATTRIBUTES)
        } else {
            append(display.primary, SimpleTextAttributes.REGULAR_BOLD_ATTRIBUTES)
        }
        if (display.secondary.isNotEmpty()) {
            append("  ${display.secondary}", SimpleTextAttributes.GRAYED_ATTRIBUTES)
        }
        append("  ·  ${value.assemblyName}", SimpleTextAttributes.GRAYED_SMALL_ATTRIBUTES)
    }
}
