@file:Suppress("EXPERIMENTAL_API_USAGE","EXPERIMENTAL_UNSIGNED_LITERALS","PackageDirectoryMismatch","UnusedImport","unused","LocalVariableName","CanBeVal","PropertyName","EnumEntryName","ClassName","ObjectPropertyName","UnnecessaryVariable","SpellCheckingInspection")
package com.cryptiklemur.riderilspy.model

import com.jetbrains.rd.framework.*
import com.jetbrains.rd.framework.base.*
import com.jetbrains.rd.framework.impl.*

import com.jetbrains.rd.util.lifetime.*
import com.jetbrains.rd.util.reactive.*
import com.jetbrains.rd.util.string.*
import com.jetbrains.rd.util.*
import kotlin.time.Duration
import kotlin.reflect.KClass
import kotlin.jvm.JvmStatic



/**
 * #### Generated from [RiderIlSpyModel.kt:44]
 */
class RiderIlSpyModel private constructor(
    private val _mode: RdOptionalProperty<String>,
    private val _readyTick: RdSignal<Long>,
    private val _saveAsProject: RdCall<SaveAsProjectRequest, SaveAsProjectResponse>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
            val classLoader = javaClass.classLoader
            serializers.register(LazyCompanionMarshaller(RdId(9044132729172568), classLoader, "com.cryptiklemur.riderilspy.model.SaveAsProjectRequest"))
            serializers.register(LazyCompanionMarshaller(RdId(280368114657283480), classLoader, "com.cryptiklemur.riderilspy.model.SaveAsProjectResponse"))
        }
        
        
        
        
        
        const val serializationHash = 891022274870918931L
        
    }
    override val serializersOwner: ISerializersOwner get() = RiderIlSpyModel
    override val serializationHash: Long get() = RiderIlSpyModel.serializationHash
    
    //fields
    val mode: IOptProperty<String> get() = _mode
    val readyTick: ISignal<Long> get() = _readyTick
    val saveAsProject: IRdCall<SaveAsProjectRequest, SaveAsProjectResponse> get() = _saveAsProject
    //methods
    //initializer
    init {
        _mode.optimizeNested = true
    }
    
    init {
        bindableChildren.add("mode" to _mode)
        bindableChildren.add("readyTick" to _readyTick)
        bindableChildren.add("saveAsProject" to _saveAsProject)
    }
    
    //secondary constructor
    internal constructor(
    ) : this(
        RdOptionalProperty<String>(FrameworkMarshallers.String),
        RdSignal<Long>(FrameworkMarshallers.Long),
        RdCall<SaveAsProjectRequest, SaveAsProjectResponse>(SaveAsProjectRequest, SaveAsProjectResponse)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("RiderIlSpyModel (")
        printer.indent {
            print("mode = "); _mode.print(printer); println()
            print("readyTick = "); _readyTick.print(printer); println()
            print("saveAsProject = "); _saveAsProject.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): RiderIlSpyModel   {
        return RiderIlSpyModel(
            _mode.deepClonePolymorphic(),
            _readyTick.deepClonePolymorphic(),
            _saveAsProject.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val com.jetbrains.rd.ide.model.Solution.riderIlSpyModel get() = getOrCreateExtension("riderIlSpyModel", ::RiderIlSpyModel)



/**
 * #### Generated from [RiderIlSpyModel.kt:47]
 */
data class SaveAsProjectRequest (
    val assemblyPath: String,
    val targetDirectory: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(assemblyPath)
        buffer.writeString(targetDirectory)
    }
    //companion
    
    companion object : IMarshaller<SaveAsProjectRequest> {
        override val _type: KClass<SaveAsProjectRequest> = SaveAsProjectRequest::class
        override val id: RdId get() = RdId(9044132729172568)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SaveAsProjectRequest  {
            val assemblyPath = buffer.readString()
            val targetDirectory = buffer.readString()
            return SaveAsProjectRequest(assemblyPath, targetDirectory)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SaveAsProjectRequest)  {
            value.write(ctx, buffer)
        }
        
        
    }
    //fields
    //methods
    //initializer
    //secondary constructor
    //equals trait
    override fun equals(other: Any?): Boolean  {
        if (this === other) return true
        if (other == null || other::class != this::class) return false
        
        other as SaveAsProjectRequest
        
        if (assemblyPath != other.assemblyPath) return false
        if (targetDirectory != other.targetDirectory) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + assemblyPath.hashCode()
        __r = __r*31 + targetDirectory.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SaveAsProjectRequest (")
        printer.indent {
            print("assemblyPath = "); assemblyPath.print(printer); println()
            print("targetDirectory = "); targetDirectory.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderIlSpyModel.kt:51]
 */
data class SaveAsProjectResponse (
    val success: Boolean,
    val projectFilePath: String,
    val csharpFileCount: Int,
    val errorMessage: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeBool(success)
        buffer.writeString(projectFilePath)
        buffer.writeInt(csharpFileCount)
        buffer.writeString(errorMessage)
    }
    //companion
    
    companion object : IMarshaller<SaveAsProjectResponse> {
        override val _type: KClass<SaveAsProjectResponse> = SaveAsProjectResponse::class
        override val id: RdId get() = RdId(280368114657283480)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SaveAsProjectResponse  {
            val success = buffer.readBool()
            val projectFilePath = buffer.readString()
            val csharpFileCount = buffer.readInt()
            val errorMessage = buffer.readString()
            return SaveAsProjectResponse(success, projectFilePath, csharpFileCount, errorMessage)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SaveAsProjectResponse)  {
            value.write(ctx, buffer)
        }
        
        
    }
    //fields
    //methods
    //initializer
    //secondary constructor
    //equals trait
    override fun equals(other: Any?): Boolean  {
        if (this === other) return true
        if (other == null || other::class != this::class) return false
        
        other as SaveAsProjectResponse
        
        if (success != other.success) return false
        if (projectFilePath != other.projectFilePath) return false
        if (csharpFileCount != other.csharpFileCount) return false
        if (errorMessage != other.errorMessage) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + success.hashCode()
        __r = __r*31 + projectFilePath.hashCode()
        __r = __r*31 + csharpFileCount.hashCode()
        __r = __r*31 + errorMessage.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SaveAsProjectResponse (")
        printer.indent {
            print("success = "); success.print(printer); println()
            print("projectFilePath = "); projectFilePath.print(printer); println()
            print("csharpFileCount = "); csharpFileCount.print(printer); println()
            print("errorMessage = "); errorMessage.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}
