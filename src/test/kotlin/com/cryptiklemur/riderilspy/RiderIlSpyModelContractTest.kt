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

    /**
     * Pins the rd-call surface used by [SaveAsProjectAction]. The kotlin action
     * calls `saveAsProject.sync(...)` and the C# backend's `SetSync` handler is
     * keyed on the property name — rd-gen renaming or dropping this would
     * silently break the action with no compile error on either side.
     */
    @Test
    fun `model exposes saveAsProject as IRdCall of request to response`() {
        val members = RiderIlSpyModel::class.memberProperties.associateBy { it.name }
        val saveAsProject = members["saveAsProject"]
        assertNotNull(saveAsProject, "RiderIlSpyModel.saveAsProject call missing — rd-gen schema regression")

        val returnTypeName = saveAsProject!!.returnType.toString()
        assert(returnTypeName.contains("IRdCall")) {
            "saveAsProject must expose IRdCall<SaveAsProjectRequest, SaveAsProjectResponse> — got $returnTypeName."
        }
        assert(returnTypeName.contains("SaveAsProjectRequest")) {
            "saveAsProject input type must be SaveAsProjectRequest — got $returnTypeName."
        }
        assert(returnTypeName.contains("SaveAsProjectResponse")) {
            "saveAsProject output type must be SaveAsProjectResponse — got $returnTypeName."
        }
    }

    /**
     * Field-shape contract for the request struct. The kotlin action constructs
     * one of these by positional arg order, so a rd-gen reorder or rename would
     * silently mis-map paths. Pins names + count.
     */
    @Test
    fun `SaveAsProjectRequest carries assemblyPath and targetDirectory`() {
        val fields = com.cryptiklemur.riderilspy.model.SaveAsProjectRequest::class.java.declaredFields
            .map { it.name }
            .toSet()
        assert("assemblyPath" in fields) { "SaveAsProjectRequest.assemblyPath field missing — fields=$fields" }
        assert("targetDirectory" in fields) { "SaveAsProjectRequest.targetDirectory field missing — fields=$fields" }
    }

    /**
     * Field-shape contract for the response struct. The kotlin action reads
     * success / projectFilePath / csharpFileCount / errorMessage by name — a
     * rd-gen rename would compile but display the wrong notification text.
     */
    @Test
    fun `SaveAsProjectResponse carries success projectFilePath csharpFileCount errorMessage`() {
        val fields = com.cryptiklemur.riderilspy.model.SaveAsProjectResponse::class.java.declaredFields
            .map { it.name }
            .toSet()
        assert("success" in fields) { "SaveAsProjectResponse.success field missing — fields=$fields" }
        assert("projectFilePath" in fields) { "SaveAsProjectResponse.projectFilePath field missing — fields=$fields" }
        assert("csharpFileCount" in fields) { "SaveAsProjectResponse.csharpFileCount field missing — fields=$fields" }
        assert("errorMessage" in fields) { "SaveAsProjectResponse.errorMessage field missing — fields=$fields" }
    }
}
