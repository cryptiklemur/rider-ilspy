package com.cryptiklemur.riderilspy.model

import com.jetbrains.rd.generator.nova.Ext
import com.jetbrains.rd.generator.nova.PredefinedType.bool
import com.jetbrains.rd.generator.nova.PredefinedType.int
import com.jetbrains.rd.generator.nova.PredefinedType.long
import com.jetbrains.rd.generator.nova.PredefinedType.string
import com.jetbrains.rd.generator.nova.call
import com.jetbrains.rd.generator.nova.csharp.CSharp50Generator
import com.jetbrains.rd.generator.nova.field
import com.jetbrains.rd.generator.nova.kotlin.Kotlin11Generator
import com.jetbrains.rd.generator.nova.property
import com.jetbrains.rd.generator.nova.signal
import com.jetbrains.rd.generator.nova.setting
import com.jetbrains.rider.model.nova.ide.SolutionModel

// IPC contract for the RiderIlSpy plugin's frontend (Rider/IntelliJ) <-> backend
// (ReSharper host) communication. Attached to SolutionModel.Solution, so an
// instance is created per opened solution and torn down with it.
//
//   - mode          : Frontend writes this when the user toggles the ILSpy output
//                     mode from the status bar; backend advises and re-decompiles
//                     open files. Encoded as the IlSpyMode.backendName string
//                     ("CSharp" / "IL" / "CSharpWithIL") so rd-protocol traces stay
//                     grep-able and human-readable rather than opaque enum ordinals.
//
//                     Unknown-value policy: if the wire value is null/empty/unknown,
//                     the C# backend treats it as "no preference" and falls back to
//                     the persisted IlSpySettings.OutputMode — never silently
//                     substituting CSharp. The kotlin frontend's
//                     IlSpyMode.fromBackendName is for parsing persisted settings,
//                     not protocol values, and falls back to CSharp only when the
//                     persisted state itself is corrupt.
//   - readyTick     : Backend fires this after each re-decompile pass completes;
//                     frontend advises and refreshes any open ILSpy editors. The
//                     payload is a monotonic tick (DateTime.UtcNow.Ticks) so
//                     observers can distinguish duplicate fires even when the same
//                     mode is set twice in a row.
//   - saveAsProject : Frontend asks backend to decompile a full assembly to a project
//                     tree. Request carries the assembly path + target dir; response
//                     reports where it landed plus a count, or an error string when
//                     ILSpy throws. This is the rd surface for the "Save Decompiled
//                     Assembly as Project..." action.
@Suppress("unused")
object RiderIlSpyModel : Ext(SolutionModel.Solution) {
    init {
        val saveAsProjectRequest = structdef("SaveAsProjectRequest") {
            field("assemblyPath", string)
            field("targetDirectory", string)
        }
        val saveAsProjectResponse = structdef("SaveAsProjectResponse") {
            field("success", bool)
            field("projectFilePath", string)
            field("csharpFileCount", int)
            field("errorMessage", string)
        }

        property("mode", string)
        signal("readyTick", long)
        call("saveAsProject", saveAsProjectRequest, saveAsProjectResponse)

        setting(Kotlin11Generator.Namespace, "com.cryptiklemur.riderilspy.model")
        setting(CSharp50Generator.Namespace, "RiderIlSpy.Model")
    }
}
