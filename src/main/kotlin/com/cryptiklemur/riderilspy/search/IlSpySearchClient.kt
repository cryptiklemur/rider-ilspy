package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.model.SearchRequest
import com.cryptiklemur.riderilspy.model.SearchResultBatch
import com.cryptiklemur.riderilspy.model.riderIlSpyModel
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.util.reactive.IOptPropertyView
import com.jetbrains.rd.util.reactive.map
import com.jetbrains.rider.projectView.solution
import java.util.UUID
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.CoroutineScope

class IlSpySearchClient(
    private val project: Project,
    private val lifetime: Lifetime,
    scope: CoroutineScope,
    debounceMs: Long = 150L,
) {
    private val debouncer = Debouncer(scope, debounceMs)
    private val activeSearchIdRef = AtomicReference<String?>(null)

    val indexState: IOptPropertyView<IlSpySearchIndexStateSnapshot>
        get() = project.solution.riderIlSpyModel.searchIndexState.map { s ->
            IlSpySearchIndexStateSnapshot(s.phase, s.indexedCount, s.totalCount, s.skippedCount, s.errorMessage)
        }

    fun runSearch(
        queryType: String,
        input: String,
        assemblyFilter: List<String>,
        regex: Boolean,
        caseSensitive: Boolean,
        wholeWord: Boolean,
        maxResults: Int,
        debounce: Boolean = true,
        onBatch: (SearchResultBatch) -> Unit,
    ) {
        val start: () -> Unit = {
            val searchId = UUID.randomUUID().toString()
            activeSearchIdRef.set(searchId)
            val model = project.solution.riderIlSpyModel
            val request = SearchRequest(
                searchId = searchId,
                queryType = queryType,
                input = input,
                assemblyFilter = assemblyFilter,
                regex = regex,
                caseSensitive = caseSensitive,
                wholeWord = wholeWord,
                maxResults = maxResults,
            )
            model.protocol?.scheduler?.invokeOrQueue {
                LOG.info("ilspy-search-fe: advise + start id=$searchId type=$queryType")
                val subscription = lifetime.createNested()
                model.searchResultBatch.advise(subscription.lifetime) { batch ->
                    if (batch.searchId != searchId) return@advise
                    LOG.info("ilspy-search-fe: batch received id=${batch.searchId} rows=${batch.rows.size} complete=${batch.isComplete}")
                    onBatch(batch)
                    if (batch.isComplete) subscription.terminate()
                }
                model.runSearch.start(subscription.lifetime, request)
            }
        }
        if (debounce) debouncer.trigger(start) else start()
    }

    fun cancelActive() {
        val id = activeSearchIdRef.getAndSet(null) ?: return
        val model = project.solution.riderIlSpyModel
        model.protocol?.scheduler?.invokeOrQueue {
            model.cancelSearch.fire(id)
        }
    }

    companion object {
        private val LOG = Logger.getInstance(IlSpySearchClient::class.java)
    }
}
