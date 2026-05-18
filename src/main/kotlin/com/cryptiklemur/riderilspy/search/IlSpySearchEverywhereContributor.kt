package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.model.SearchResultBatch
import com.intellij.ide.actions.searcheverywhere.SearchEverywhereContributor
import com.intellij.ide.actions.searcheverywhere.SearchEverywhereContributorFactory
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.project.Project
import com.intellij.util.Processor
import java.util.concurrent.ConcurrentLinkedQueue
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import javax.swing.ListCellRenderer

class IlSpySearchEverywhereContributor(private val project: Project) : SearchEverywhereContributor<IlSpyLiteralMatch> {

    override fun getSearchProviderId(): String = "ILSpyLiterals"
    override fun getGroupName(): String = "ILSpy Literals"
    override fun getSortWeight(): Int = 1500
    override fun showInFindResults(): Boolean = false

    override fun fetchElements(
        pattern: String,
        progressIndicator: ProgressIndicator,
        consumer: Processor<in IlSpyLiteralMatch>,
    ) {
        val service = project.getService(IlSpySearchClientService::class.java)
        if (!shouldServe(service.client.indexState.valueOrNull, pattern)) return

        val queue = ConcurrentLinkedQueue<IlSpyLiteralMatch>()
        val done = CountDownLatch(1)
        service.client.runSearch(
            queryType = "Literal",
            input = pattern,
            assemblyFilter = emptyList(),
            regex = false,
            caseSensitive = false,
            wholeWord = false,
            maxResults = 32,
            onBatch = { batch -> emit(batch, queue); if (batch.isComplete) done.countDown() },
        )
        done.await(2, TimeUnit.SECONDS)
        var emitted = 0
        while (queue.isNotEmpty() && emitted < 8) {
            val m = queue.poll() ?: break
            if (!consumer.process(m)) return
            emitted++
        }
    }

    private fun emit(batch: SearchResultBatch, queue: ConcurrentLinkedQueue<IlSpyLiteralMatch>) {
        for (r in batch.rows) {
            queue.add(
                IlSpyLiteralMatch(
                    assemblyName = r.assemblyName,
                    containingMember = r.target,
                    snippet = r.snippet,
                    navTarget = r.navTarget,
                )
            )
        }
    }

    override fun processSelectedItem(selected: IlSpyLiteralMatch, modifiers: Int, searchText: String): Boolean {
        val resolver = project.getService(IlSpyNavTargetResolverService::class.java).resolver
        resolver.navigate(selected.navTarget)
        return true
    }

    override fun getElementsRenderer(): ListCellRenderer<in IlSpyLiteralMatch> = IlSpyLiteralMatchRenderer()

    companion object {
        fun shouldServe(state: IlSpySearchIndexStateSnapshot?, pattern: String): Boolean {
            if (pattern.length < 2) return false
            if (state == null) return false
            return state.isReady
        }
    }

    class Factory : SearchEverywhereContributorFactory<IlSpyLiteralMatch> {
        override fun createContributor(event: AnActionEvent): SearchEverywhereContributor<IlSpyLiteralMatch> =
            IlSpySearchEverywhereContributor(event.project!!)
    }
}
