package com.cryptiklemur.riderilspy

import com.cryptiklemur.riderilspy.model.RiderIlSpyModel
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Test
import kotlin.reflect.full.memberProperties

/**
 * Schema-shape contract for the rd-generated [RiderIlSpyModel]. The C# backend
 * depends on the property and signal names being exactly "mode" and "readyTick"
 * (advised on by name), so this test fails fast if rd-gen output ever changes
 * the public surface — even before any integration test fires.
 */
class RiderIlSpyModelContractTest {

    @Test
    fun `model exposes mode property and readyTick signal`() {
        val members = RiderIlSpyModel::class.memberProperties.associateBy { it.name }
        val modeProperty = members["mode"]
        val readyTickSignal = members["readyTick"]

        assertNotNull(modeProperty, "RiderIlSpyModel.mode property missing — rd-gen schema regression")
        assertNotNull(readyTickSignal, "RiderIlSpyModel.readyTick signal missing — rd-gen schema regression")
    }

    @Test
    fun `mode is an IOptProperty of String`() {
        val modeProperty = RiderIlSpyModel::class.memberProperties.first { it.name == "mode" }
        val modeReturnTypeName = modeProperty.returnType.toString()
        // toString() emits "com.jetbrains.rd.util.reactive.IOptProperty<kotlin.String>".
        // String-based assertion avoids KClass identity churn from rdgen's classloader.
        assert(modeReturnTypeName.contains("IOptProperty")) {
            "mode must expose IOptProperty<String> — got $modeReturnTypeName. C# backend Advises on .Value of this exact type."
        }
        assert(modeReturnTypeName.contains("String")) {
            "mode must carry a String payload (rd wire format) — got $modeReturnTypeName."
        }
    }

    @Test
    fun `readyTick is an ISignal of Long`() {
        val readyTickProperty = RiderIlSpyModel::class.memberProperties.first { it.name == "readyTick" }
        val readyTickReturnTypeName = readyTickProperty.returnType.toString()
        assert(readyTickReturnTypeName.contains("ISignal")) {
            "readyTick must expose ISignal<Long> — got $readyTickReturnTypeName. Frontend Advises on this for refresh ticks."
        }
        assert(readyTickReturnTypeName.contains("Long")) {
            "readyTick must carry a Long payload (monotonic UtcNow.Ticks) — got $readyTickReturnTypeName."
        }
    }
}
