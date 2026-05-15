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
 * #### Generated from [RiderIlSpyModel.kt:28]
 */
class RiderIlSpyModel private constructor(
    private val _mode: RdOptionalProperty<String>,
    private val _readyTick: RdSignal<Long>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
        }
        
        
        
        
        
        const val serializationHash = 4283210212157620969L
        
    }
    override val serializersOwner: ISerializersOwner get() = RiderIlSpyModel
    override val serializationHash: Long get() = RiderIlSpyModel.serializationHash
    
    //fields
    val mode: IOptProperty<String> get() = _mode
    val readyTick: ISignal<Long> get() = _readyTick
    //methods
    //initializer
    init {
        _mode.optimizeNested = true
    }
    
    init {
        bindableChildren.add("mode" to _mode)
        bindableChildren.add("readyTick" to _readyTick)
    }
    
    //secondary constructor
    internal constructor(
    ) : this(
        RdOptionalProperty<String>(FrameworkMarshallers.String),
        RdSignal<Long>(FrameworkMarshallers.Long)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("RiderIlSpyModel (")
        printer.indent {
            print("mode = "); _mode.print(printer); println()
            print("readyTick = "); _readyTick.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): RiderIlSpyModel   {
        return RiderIlSpyModel(
            _mode.deepClonePolymorphic(),
            _readyTick.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val com.jetbrains.rd.ide.model.Solution.riderIlSpyModel get() = getOrCreateExtension("riderIlSpyModel", ::RiderIlSpyModel)

