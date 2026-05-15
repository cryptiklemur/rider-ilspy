package com.cryptiklemur.riderilspy.model

import com.jetbrains.rd.generator.nova.Ext
import com.jetbrains.rd.generator.nova.PredefinedType.long
import com.jetbrains.rd.generator.nova.PredefinedType.string
import com.jetbrains.rd.generator.nova.csharp.CSharp50Generator
import com.jetbrains.rd.generator.nova.kotlin.Kotlin11Generator
import com.jetbrains.rd.generator.nova.property
import com.jetbrains.rd.generator.nova.signal
import com.jetbrains.rd.generator.nova.setting
import com.jetbrains.rider.model.nova.ide.SolutionModel

// IPC contract for the RiderIlSpy plugin's frontend (Rider/IntelliJ) <-> backend
// (ReSharper host) communication. Attached to SolutionModel.Solution, so an
// instance is created per opened solution and torn down with it.
//
//   - mode      : Frontend writes this when the user toggles the ILSpy output
//                 mode from the status bar; backend advises and re-decompiles
//                 open files. Encoded as the IlSpyMode.backendName string
//                 ("CSharp" / "IL" / "CSharpWithIL") so the contract stays
//                 human-readable in logs and we keep parity with the prior
//                 file-based protocol.
//   - readyTick : Backend fires this after each re-decompile pass completes;
//                 frontend advises and refreshes any open ILSpy editors. The
//                 payload is a monotonic tick (DateTime.UtcNow.Ticks) so
//                 observers can distinguish duplicate fires even when the same
//                 mode is set twice in a row.
@Suppress("unused")
object RiderIlSpyModel : Ext(SolutionModel.Solution) {
    init {
        property("mode", string)
        signal("readyTick", long)

        setting(Kotlin11Generator.Namespace, "com.cryptiklemur.riderilspy.model")
        setting(CSharp50Generator.Namespace, "RiderIlSpy.Model")
    }
}
