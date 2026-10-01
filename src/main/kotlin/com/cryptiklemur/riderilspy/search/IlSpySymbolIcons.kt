package com.cryptiklemur.riderilspy.search

import icons.ReSharperIcons
import javax.swing.Icon

object IlSpySymbolIcons {
    fun forKind(kind: String?): Icon? = when (kind) {
        "Type" -> ReSharperIcons.PsiSymbols.Class
        "Method" -> ReSharperIcons.PsiSymbols.Method
        "Field" -> ReSharperIcons.PsiSymbols.Field
        "Property" -> ReSharperIcons.PsiSymbols.Property
        "Event" -> ReSharperIcons.PsiSymbols.Event
        "Namespace" -> ReSharperIcons.PsiSymbols.Namespace
        else -> null
    }
}
