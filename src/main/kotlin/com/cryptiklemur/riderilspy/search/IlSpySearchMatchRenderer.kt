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
        append("\"${value.snippet}\"", SimpleTextAttributes.REGULAR_BOLD_ATTRIBUTES)
        append("  ${value.target}", SimpleTextAttributes.GRAYED_ATTRIBUTES)
        append("  ·  ${value.assemblyName}", SimpleTextAttributes.GRAYED_SMALL_ATTRIBUTES)
    }
}
