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
    private val _enabled: RdOptionalProperty<Boolean>,
    private val _readyTick: RdSignal<Long>,
    private val _saveAsProject: RdCall<SaveAsProjectRequest, SaveAsProjectResponse>,
    private val _searchIndexState: RdOptionalProperty<SearchIndexState>,
    private val _runSearch: RdCall<SearchRequest, String>,
    private val _searchResultBatch: RdSignal<SearchResultBatch>,
    private val _cancelSearch: RdSignal<String>,
    private val _rescanAssembly: RdSignal<String>,
    private val _resolveNavTarget: RdCall<NavTarget, NavResolution>
) : RdExtBase() {
    //companion
    
    companion object : ISerializersOwner {
        
        override fun registerSerializersCore(serializers: ISerializers)  {
            val classLoader = javaClass.classLoader
            serializers.register(LazyCompanionMarshaller(RdId(9044132729172568), classLoader, "com.cryptiklemur.riderilspy.model.SaveAsProjectRequest"))
            serializers.register(LazyCompanionMarshaller(RdId(280368114657283480), classLoader, "com.cryptiklemur.riderilspy.model.SaveAsProjectResponse"))
            serializers.register(LazyCompanionMarshaller(RdId(7353996714499796186), classLoader, "com.cryptiklemur.riderilspy.model.SearchIndexState"))
            serializers.register(LazyCompanionMarshaller(RdId(-2997325831071850892), classLoader, "com.cryptiklemur.riderilspy.model.SearchRequest"))
            serializers.register(LazyCompanionMarshaller(RdId(571654268904897), classLoader, "com.cryptiklemur.riderilspy.model.NavTarget"))
            serializers.register(LazyCompanionMarshaller(RdId(-2738048159577182430), classLoader, "com.cryptiklemur.riderilspy.model.SearchResultRow"))
            serializers.register(LazyCompanionMarshaller(RdId(6620121186778372738), classLoader, "com.cryptiklemur.riderilspy.model.SearchResultBatch"))
            serializers.register(LazyCompanionMarshaller(RdId(-7020905497120254756), classLoader, "com.cryptiklemur.riderilspy.model.NavResolution"))
        }
        
        
        
        
        
        const val serializationHash = -956756264648943532L
        
    }
    override val serializersOwner: ISerializersOwner get() = RiderIlSpyModel
    override val serializationHash: Long get() = RiderIlSpyModel.serializationHash
    
    //fields
    val mode: IOptProperty<String> get() = _mode
    val enabled: IOptProperty<Boolean> get() = _enabled
    val readyTick: ISignal<Long> get() = _readyTick
    val saveAsProject: IRdCall<SaveAsProjectRequest, SaveAsProjectResponse> get() = _saveAsProject
    val searchIndexState: IOptProperty<SearchIndexState> get() = _searchIndexState
    val runSearch: IRdCall<SearchRequest, String> get() = _runSearch
    val searchResultBatch: ISignal<SearchResultBatch> get() = _searchResultBatch
    val cancelSearch: ISignal<String> get() = _cancelSearch
    val rescanAssembly: ISignal<String> get() = _rescanAssembly
    val resolveNavTarget: IRdCall<NavTarget, NavResolution> get() = _resolveNavTarget
    //methods
    //initializer
    init {
        _mode.optimizeNested = true
        _enabled.optimizeNested = true
        _searchIndexState.optimizeNested = true
    }
    
    init {
        bindableChildren.add("mode" to _mode)
        bindableChildren.add("enabled" to _enabled)
        bindableChildren.add("readyTick" to _readyTick)
        bindableChildren.add("saveAsProject" to _saveAsProject)
        bindableChildren.add("searchIndexState" to _searchIndexState)
        bindableChildren.add("runSearch" to _runSearch)
        bindableChildren.add("searchResultBatch" to _searchResultBatch)
        bindableChildren.add("cancelSearch" to _cancelSearch)
        bindableChildren.add("rescanAssembly" to _rescanAssembly)
        bindableChildren.add("resolveNavTarget" to _resolveNavTarget)
    }
    
    //secondary constructor
    internal constructor(
    ) : this(
        RdOptionalProperty<String>(FrameworkMarshallers.String),
        RdOptionalProperty<Boolean>(FrameworkMarshallers.Bool),
        RdSignal<Long>(FrameworkMarshallers.Long),
        RdCall<SaveAsProjectRequest, SaveAsProjectResponse>(SaveAsProjectRequest, SaveAsProjectResponse),
        RdOptionalProperty<SearchIndexState>(SearchIndexState),
        RdCall<SearchRequest, String>(SearchRequest, FrameworkMarshallers.String),
        RdSignal<SearchResultBatch>(SearchResultBatch),
        RdSignal<String>(FrameworkMarshallers.String),
        RdSignal<String>(FrameworkMarshallers.String),
        RdCall<NavTarget, NavResolution>(NavTarget, NavResolution)
    )
    
    //equals trait
    //hash code trait
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("RiderIlSpyModel (")
        printer.indent {
            print("mode = "); _mode.print(printer); println()
            print("enabled = "); _enabled.print(printer); println()
            print("readyTick = "); _readyTick.print(printer); println()
            print("saveAsProject = "); _saveAsProject.print(printer); println()
            print("searchIndexState = "); _searchIndexState.print(printer); println()
            print("runSearch = "); _runSearch.print(printer); println()
            print("searchResultBatch = "); _searchResultBatch.print(printer); println()
            print("cancelSearch = "); _cancelSearch.print(printer); println()
            print("rescanAssembly = "); _rescanAssembly.print(printer); println()
            print("resolveNavTarget = "); _resolveNavTarget.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    override fun deepClone(): RiderIlSpyModel   {
        return RiderIlSpyModel(
            _mode.deepClonePolymorphic(),
            _enabled.deepClonePolymorphic(),
            _readyTick.deepClonePolymorphic(),
            _saveAsProject.deepClonePolymorphic(),
            _searchIndexState.deepClonePolymorphic(),
            _runSearch.deepClonePolymorphic(),
            _searchResultBatch.deepClonePolymorphic(),
            _cancelSearch.deepClonePolymorphic(),
            _rescanAssembly.deepClonePolymorphic(),
            _resolveNavTarget.deepClonePolymorphic()
        )
    }
    //contexts
    //threading
    override val extThreading: ExtThreadingKind get() = ExtThreadingKind.Default
}
val com.jetbrains.rd.ide.model.Solution.riderIlSpyModel get() = getOrCreateExtension("riderIlSpyModel", ::RiderIlSpyModel)



/**
 * #### Generated from [RiderIlSpyModel.kt:121]
 */
data class NavResolution (
    val success: Boolean,
    val filePath: String,
    val line: Int,
    val column: Int,
    val errorMessage: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeBool(success)
        buffer.writeString(filePath)
        buffer.writeInt(line)
        buffer.writeInt(column)
        buffer.writeString(errorMessage)
    }
    //companion
    
    companion object : IMarshaller<NavResolution> {
        override val _type: KClass<NavResolution> = NavResolution::class
        override val id: RdId get() = RdId(-7020905497120254756)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): NavResolution  {
            val success = buffer.readBool()
            val filePath = buffer.readString()
            val line = buffer.readInt()
            val column = buffer.readInt()
            val errorMessage = buffer.readString()
            return NavResolution(success, filePath, line, column, errorMessage)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: NavResolution)  {
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
        
        other as NavResolution
        
        if (success != other.success) return false
        if (filePath != other.filePath) return false
        if (line != other.line) return false
        if (column != other.column) return false
        if (errorMessage != other.errorMessage) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + success.hashCode()
        __r = __r*31 + filePath.hashCode()
        __r = __r*31 + line.hashCode()
        __r = __r*31 + column.hashCode()
        __r = __r*31 + errorMessage.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("NavResolution (")
        printer.indent {
            print("success = "); success.print(printer); println()
            print("filePath = "); filePath.print(printer); println()
            print("line = "); line.print(printer); println()
            print("column = "); column.print(printer); println()
            print("errorMessage = "); errorMessage.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderIlSpyModel.kt:90]
 */
data class NavTarget (
    val kind: String,
    val assemblyPath: String,
    val metadataToken: Int,
    val ilOffset: Int,
    val resourceEntry: String,
    val mimeHint: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(kind)
        buffer.writeString(assemblyPath)
        buffer.writeInt(metadataToken)
        buffer.writeInt(ilOffset)
        buffer.writeString(resourceEntry)
        buffer.writeString(mimeHint)
    }
    //companion
    
    companion object : IMarshaller<NavTarget> {
        override val _type: KClass<NavTarget> = NavTarget::class
        override val id: RdId get() = RdId(571654268904897)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): NavTarget  {
            val kind = buffer.readString()
            val assemblyPath = buffer.readString()
            val metadataToken = buffer.readInt()
            val ilOffset = buffer.readInt()
            val resourceEntry = buffer.readString()
            val mimeHint = buffer.readString()
            return NavTarget(kind, assemblyPath, metadataToken, ilOffset, resourceEntry, mimeHint)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: NavTarget)  {
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
        
        other as NavTarget
        
        if (kind != other.kind) return false
        if (assemblyPath != other.assemblyPath) return false
        if (metadataToken != other.metadataToken) return false
        if (ilOffset != other.ilOffset) return false
        if (resourceEntry != other.resourceEntry) return false
        if (mimeHint != other.mimeHint) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + kind.hashCode()
        __r = __r*31 + assemblyPath.hashCode()
        __r = __r*31 + metadataToken.hashCode()
        __r = __r*31 + ilOffset.hashCode()
        __r = __r*31 + resourceEntry.hashCode()
        __r = __r*31 + mimeHint.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("NavTarget (")
        printer.indent {
            print("kind = "); kind.print(printer); println()
            print("assemblyPath = "); assemblyPath.print(printer); println()
            print("metadataToken = "); metadataToken.print(printer); println()
            print("ilOffset = "); ilOffset.print(printer); println()
            print("resourceEntry = "); resourceEntry.print(printer); println()
            print("mimeHint = "); mimeHint.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


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


/**
 * #### Generated from [RiderIlSpyModel.kt:69]
 */
data class SearchIndexState (
    val phase: String,
    val indexedCount: Int,
    val totalCount: Int,
    val skippedCount: Int,
    val errorMessage: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(phase)
        buffer.writeInt(indexedCount)
        buffer.writeInt(totalCount)
        buffer.writeInt(skippedCount)
        buffer.writeString(errorMessage)
    }
    //companion
    
    companion object : IMarshaller<SearchIndexState> {
        override val _type: KClass<SearchIndexState> = SearchIndexState::class
        override val id: RdId get() = RdId(7353996714499796186)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SearchIndexState  {
            val phase = buffer.readString()
            val indexedCount = buffer.readInt()
            val totalCount = buffer.readInt()
            val skippedCount = buffer.readInt()
            val errorMessage = buffer.readString()
            return SearchIndexState(phase, indexedCount, totalCount, skippedCount, errorMessage)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SearchIndexState)  {
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
        
        other as SearchIndexState
        
        if (phase != other.phase) return false
        if (indexedCount != other.indexedCount) return false
        if (totalCount != other.totalCount) return false
        if (skippedCount != other.skippedCount) return false
        if (errorMessage != other.errorMessage) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + phase.hashCode()
        __r = __r*31 + indexedCount.hashCode()
        __r = __r*31 + totalCount.hashCode()
        __r = __r*31 + skippedCount.hashCode()
        __r = __r*31 + errorMessage.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SearchIndexState (")
        printer.indent {
            print("phase = "); phase.print(printer); println()
            print("indexedCount = "); indexedCount.print(printer); println()
            print("totalCount = "); totalCount.print(printer); println()
            print("skippedCount = "); skippedCount.print(printer); println()
            print("errorMessage = "); errorMessage.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderIlSpyModel.kt:79]
 */
data class SearchRequest (
    val searchId: String,
    val queryType: String,
    val input: String,
    val assemblyFilter: List<String>,
    val regex: Boolean,
    val caseSensitive: Boolean,
    val wholeWord: Boolean,
    val maxResults: Int
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(searchId)
        buffer.writeString(queryType)
        buffer.writeString(input)
        buffer.writeList(assemblyFilter) { v -> buffer.writeString(v) }
        buffer.writeBool(regex)
        buffer.writeBool(caseSensitive)
        buffer.writeBool(wholeWord)
        buffer.writeInt(maxResults)
    }
    //companion
    
    companion object : IMarshaller<SearchRequest> {
        override val _type: KClass<SearchRequest> = SearchRequest::class
        override val id: RdId get() = RdId(-2997325831071850892)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SearchRequest  {
            val searchId = buffer.readString()
            val queryType = buffer.readString()
            val input = buffer.readString()
            val assemblyFilter = buffer.readList { buffer.readString() }
            val regex = buffer.readBool()
            val caseSensitive = buffer.readBool()
            val wholeWord = buffer.readBool()
            val maxResults = buffer.readInt()
            return SearchRequest(searchId, queryType, input, assemblyFilter, regex, caseSensitive, wholeWord, maxResults)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SearchRequest)  {
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
        
        other as SearchRequest
        
        if (searchId != other.searchId) return false
        if (queryType != other.queryType) return false
        if (input != other.input) return false
        if (assemblyFilter != other.assemblyFilter) return false
        if (regex != other.regex) return false
        if (caseSensitive != other.caseSensitive) return false
        if (wholeWord != other.wholeWord) return false
        if (maxResults != other.maxResults) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + searchId.hashCode()
        __r = __r*31 + queryType.hashCode()
        __r = __r*31 + input.hashCode()
        __r = __r*31 + assemblyFilter.hashCode()
        __r = __r*31 + regex.hashCode()
        __r = __r*31 + caseSensitive.hashCode()
        __r = __r*31 + wholeWord.hashCode()
        __r = __r*31 + maxResults.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SearchRequest (")
        printer.indent {
            print("searchId = "); searchId.print(printer); println()
            print("queryType = "); queryType.print(printer); println()
            print("input = "); input.print(printer); println()
            print("assemblyFilter = "); assemblyFilter.print(printer); println()
            print("regex = "); regex.print(printer); println()
            print("caseSensitive = "); caseSensitive.print(printer); println()
            print("wholeWord = "); wholeWord.print(printer); println()
            print("maxResults = "); maxResults.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderIlSpyModel.kt:108]
 */
data class SearchResultBatch (
    val searchId: String,
    val rows: List<SearchResultRow>,
    val isComplete: Boolean,
    val errorMessage: String
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(searchId)
        buffer.writeList(rows) { v -> SearchResultRow.write(ctx, buffer, v) }
        buffer.writeBool(isComplete)
        buffer.writeString(errorMessage)
    }
    //companion
    
    companion object : IMarshaller<SearchResultBatch> {
        override val _type: KClass<SearchResultBatch> = SearchResultBatch::class
        override val id: RdId get() = RdId(6620121186778372738)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SearchResultBatch  {
            val searchId = buffer.readString()
            val rows = buffer.readList { SearchResultRow.read(ctx, buffer) }
            val isComplete = buffer.readBool()
            val errorMessage = buffer.readString()
            return SearchResultBatch(searchId, rows, isComplete, errorMessage)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SearchResultBatch)  {
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
        
        other as SearchResultBatch
        
        if (searchId != other.searchId) return false
        if (rows != other.rows) return false
        if (isComplete != other.isComplete) return false
        if (errorMessage != other.errorMessage) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + searchId.hashCode()
        __r = __r*31 + rows.hashCode()
        __r = __r*31 + isComplete.hashCode()
        __r = __r*31 + errorMessage.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SearchResultBatch (")
        printer.indent {
            print("searchId = "); searchId.print(printer); println()
            print("rows = "); rows.print(printer); println()
            print("isComplete = "); isComplete.print(printer); println()
            print("errorMessage = "); errorMessage.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}


/**
 * #### Generated from [RiderIlSpyModel.kt:99]
 */
data class SearchResultRow (
    val assemblyName: String,
    val target: String,
    val snippet: String,
    val matchStart: Int,
    val matchLength: Int,
    val navTarget: NavTarget
) : IPrintable {
    //write-marshaller
    private fun write(ctx: SerializationCtx, buffer: AbstractBuffer)  {
        buffer.writeString(assemblyName)
        buffer.writeString(target)
        buffer.writeString(snippet)
        buffer.writeInt(matchStart)
        buffer.writeInt(matchLength)
        NavTarget.write(ctx, buffer, navTarget)
    }
    //companion
    
    companion object : IMarshaller<SearchResultRow> {
        override val _type: KClass<SearchResultRow> = SearchResultRow::class
        override val id: RdId get() = RdId(-2738048159577182430)
        
        @Suppress("UNCHECKED_CAST")
        override fun read(ctx: SerializationCtx, buffer: AbstractBuffer): SearchResultRow  {
            val assemblyName = buffer.readString()
            val target = buffer.readString()
            val snippet = buffer.readString()
            val matchStart = buffer.readInt()
            val matchLength = buffer.readInt()
            val navTarget = NavTarget.read(ctx, buffer)
            return SearchResultRow(assemblyName, target, snippet, matchStart, matchLength, navTarget)
        }
        
        override fun write(ctx: SerializationCtx, buffer: AbstractBuffer, value: SearchResultRow)  {
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
        
        other as SearchResultRow
        
        if (assemblyName != other.assemblyName) return false
        if (target != other.target) return false
        if (snippet != other.snippet) return false
        if (matchStart != other.matchStart) return false
        if (matchLength != other.matchLength) return false
        if (navTarget != other.navTarget) return false
        
        return true
    }
    //hash code trait
    override fun hashCode(): Int  {
        var __r = 0
        __r = __r*31 + assemblyName.hashCode()
        __r = __r*31 + target.hashCode()
        __r = __r*31 + snippet.hashCode()
        __r = __r*31 + matchStart.hashCode()
        __r = __r*31 + matchLength.hashCode()
        __r = __r*31 + navTarget.hashCode()
        return __r
    }
    //pretty print
    override fun print(printer: PrettyPrinter)  {
        printer.println("SearchResultRow (")
        printer.indent {
            print("assemblyName = "); assemblyName.print(printer); println()
            print("target = "); target.print(printer); println()
            print("snippet = "); snippet.print(printer); println()
            print("matchStart = "); matchStart.print(printer); println()
            print("matchLength = "); matchLength.print(printer); println()
            print("navTarget = "); navTarget.print(printer); println()
        }
        printer.print(")")
    }
    //deepClone
    //contexts
    //threading
}
