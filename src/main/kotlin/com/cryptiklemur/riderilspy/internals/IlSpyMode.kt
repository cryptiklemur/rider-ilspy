package com.cryptiklemur.riderilspy.internals

enum class IlSpyMode(val displayName: String, val backendName: String) {
    CSharp("C#", "CSharp"),
    IL("IL", "IL"),
    CSharpWithIL("C# + IL", "CSharpWithIL");

    companion object {
        fun fromBackendName(name: String?): IlSpyMode =
            entries.firstOrNull { it.backendName == name } ?: CSharp
    }
}
