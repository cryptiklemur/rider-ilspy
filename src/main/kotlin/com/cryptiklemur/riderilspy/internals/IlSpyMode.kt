package com.cryptiklemur.riderilspy.internals

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle

enum class IlSpyMode(private val displayKey: String, val backendName: String) {
    CSharp("mode.csharp.display", "CSharp"),
    IL("mode.il.display", "IL"),
    CSharpWithIL("mode.csharp_with_il.display", "CSharpWithIL");

    // Looked up lazily so the bundle is always loaded by the time we render —
    // enum constants are initialized very early in some startup paths.
    val displayName: String
        get() = RiderIlSpyBundle.message(displayKey)

    companion object {
        fun fromBackendName(name: String?): IlSpyMode =
            entries.firstOrNull { it.backendName == name } ?: CSharp
    }
}
