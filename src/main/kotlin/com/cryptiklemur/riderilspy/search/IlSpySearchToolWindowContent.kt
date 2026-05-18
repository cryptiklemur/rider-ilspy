package com.cryptiklemur.riderilspy.search

import com.cryptiklemur.riderilspy.i18n.RiderIlSpyBundle
import com.cryptiklemur.riderilspy.model.NavTarget
import com.cryptiklemur.riderilspy.model.SearchResultBatch
import com.intellij.icons.AllIcons
import com.intellij.openapi.project.Project
import com.intellij.ui.ColoredTreeCellRenderer
import com.intellij.ui.SimpleTextAttributes
import com.intellij.ui.components.JBLabel
import com.intellij.ui.treeStructure.Tree
import com.intellij.util.ui.JBUI
import java.awt.BorderLayout
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import javax.swing.BoxLayout
import javax.swing.JButton
import javax.swing.JCheckBox
import javax.swing.JComboBox
import javax.swing.JPanel
import javax.swing.JScrollPane
import javax.swing.JTextField
import javax.swing.JTree
import javax.swing.SwingUtilities
import javax.swing.tree.DefaultMutableTreeNode
import javax.swing.tree.DefaultTreeModel
import javax.swing.tree.TreeSelectionModel

class IlSpySearchToolWindowContent(private val project: Project) {
    // The selectedItem.protocolId is what we send to the backend; the toString()
    // override is what the user sees. Keep them split so localizing the labels
    // doesn't change the protocol payload.
    private val queryTypeBox = JComboBox(
        arrayOf(
            QueryTypeChoice("Literal", RiderIlSpyBundle.message("search.toolwindow.query_type.literal")),
            QueryTypeChoice("Attribute", RiderIlSpyBundle.message("search.toolwindow.query_type.attribute")),
            QueryTypeChoice("Token", RiderIlSpyBundle.message("search.toolwindow.query_type.token")),
            QueryTypeChoice("Constant", RiderIlSpyBundle.message("search.toolwindow.query_type.constant")),
            QueryTypeChoice("Resource", RiderIlSpyBundle.message("search.toolwindow.query_type.resource")),
        ),
    )
    private val inputField = JTextField()
    private val regexBox = JCheckBox(RiderIlSpyBundle.message("search.toolwindow.regex"))
    private val caseBox = JCheckBox(RiderIlSpyBundle.message("search.toolwindow.case_sensitive"))
    private val wordBox = JCheckBox(RiderIlSpyBundle.message("search.toolwindow.whole_word"))
    private val statusLabel = JBLabel(" ")

    private val rootNode = DefaultMutableTreeNode("results")
    private val treeModel = DefaultTreeModel(rootNode)
    private val tree = Tree(treeModel).apply {
        isRootVisible = false
        showsRootHandles = true
        selectionModel.selectionMode = TreeSelectionModel.SINGLE_TREE_SELECTION
        cellRenderer = ResultsTreeRenderer()
        emptyText.text = RiderIlSpyBundle.message("search.toolwindow.empty_text")
    }

    private val assemblyNodes = mutableMapOf<String, AssemblyGroupNode>()
    private var totalHits = 0

    val panel: JPanel = JPanel(BorderLayout()).apply {
        add(buildTopBar(), BorderLayout.NORTH)
        add(JScrollPane(tree), BorderLayout.CENTER)
        add(statusLabel, BorderLayout.SOUTH)
    }

    init {
        inputField.addActionListener { runSearch() }

        val clientService = project.getService(IlSpySearchClientService::class.java)
        clientService.client.indexState.advise(clientService.lifetime) { snapshot ->
            renderState(snapshot)
        }

        tree.addMouseListener(object : MouseAdapter() {
            override fun mouseClicked(e: MouseEvent) {
                if (e.clickCount != 2) return
                val path = tree.getPathForLocation(e.x, e.y) ?: return
                val node = path.lastPathComponent as? DefaultMutableTreeNode ?: return
                val payload = node.userObject as? ResultPayload ?: return
                val resolver = project.getService(IlSpyNavTargetResolverService::class.java).resolver
                resolver.navigate(payload.navTarget)
            }
        })
    }

    private fun runSearch() {
        val client = project.getService(IlSpySearchClientService::class.java).client
        clearResults()
        client.runSearch(
            queryType = (queryTypeBox.selectedItem as QueryTypeChoice).protocolId,
            input = inputField.text,
            assemblyFilter = emptyList(),
            regex = regexBox.isSelected,
            caseSensitive = caseBox.isSelected,
            wholeWord = wordBox.isSelected,
            maxResults = 5000,
            onBatch = ::onBatch,
        )
    }

    private fun clearResults() {
        rootNode.removeAllChildren()
        assemblyNodes.clear()
        totalHits = 0
        treeModel.reload()
    }

    private fun onBatch(batch: SearchResultBatch) {
        SwingUtilities.invokeLater {
            for (r in batch.rows) {
                val group = assemblyNodes.getOrPut(r.assemblyName) {
                    val payload = AssemblyGroupPayload(r.assemblyName, 0)
                    val node = AssemblyGroupNode(payload)
                    rootNode.add(node)
                    node
                }
                val resultPayload = ResultPayload(r.target, r.snippet, r.navTarget)
                group.add(DefaultMutableTreeNode(resultPayload, false))
                group.payload.count++
                totalHits++
            }
            treeModel.nodeStructureChanged(rootNode)
            var i = 0
            while (i < tree.rowCount) {
                tree.expandRow(i)
                i++
            }
            statusLabel.text = if (batch.isComplete)
                RiderIlSpyBundle.message("search.toolwindow.status.results", totalHits, assemblyNodes.size)
            else
                RiderIlSpyBundle.message("search.toolwindow.status.results_searching", totalHits, assemblyNodes.size)
        }
    }

    private fun renderState(snapshot: IlSpySearchIndexStateSnapshot) {
        SwingUtilities.invokeLater {
            statusLabel.text = when (snapshot.phase) {
                "Building" -> RiderIlSpyBundle.message(
                    "search.toolwindow.status.indexing", snapshot.indexed, snapshot.total,
                )
                "Failed" -> RiderIlSpyBundle.message(
                    "search.toolwindow.status.indexing_failed", snapshot.errorMessage,
                )
                "Idle" -> RiderIlSpyBundle.message("search.toolwindow.status.idle")
                else -> if (snapshot.skipped > 0)
                    RiderIlSpyBundle.message(
                        "search.toolwindow.status.indexed_skipped", snapshot.indexed, snapshot.skipped,
                    )
                else
                    RiderIlSpyBundle.message("search.toolwindow.status.indexed", snapshot.indexed)
            }
        }
    }

    private fun buildTopBar(): JPanel {
        val bar = JPanel()
        bar.layout = BoxLayout(bar, BoxLayout.Y_AXIS)
        bar.border = JBUI.Borders.empty(4)
        val row1 = JPanel(BorderLayout())
        row1.add(queryTypeBox, BorderLayout.WEST)
        row1.add(inputField, BorderLayout.CENTER)
        row1.add(
            JButton(RiderIlSpyBundle.message("search.toolwindow.search_button"))
                .apply { addActionListener { runSearch() } },
            BorderLayout.EAST,
        )
        val row2 = JPanel()
        row2.add(regexBox)
        row2.add(caseBox)
        row2.add(wordBox)
        bar.add(row1)
        bar.add(row2)
        return bar
    }
}

private data class QueryTypeChoice(val protocolId: String, val displayLabel: String) {
    override fun toString(): String = displayLabel
}

private class AssemblyGroupNode(val payload: AssemblyGroupPayload) : DefaultMutableTreeNode(payload, true)

private data class AssemblyGroupPayload(val name: String, var count: Int)

private data class ResultPayload(val target: String, val snippet: String, val navTarget: NavTarget)

private class ResultsTreeRenderer : ColoredTreeCellRenderer() {
    override fun customizeCellRenderer(
        tree: JTree,
        value: Any?,
        selected: Boolean,
        expanded: Boolean,
        leaf: Boolean,
        row: Int,
        hasFocus: Boolean,
    ) {
        val node = value as? DefaultMutableTreeNode ?: return
        when (val payload = node.userObject) {
            is AssemblyGroupPayload -> {
                icon = AllIcons.Nodes.PpLib
                append(payload.name, SimpleTextAttributes.REGULAR_BOLD_ATTRIBUTES)
                append("  (${payload.count})", SimpleTextAttributes.GRAYED_ATTRIBUTES)
            }
            is ResultPayload -> {
                icon = AllIcons.Nodes.Method
                append(payload.target, SimpleTextAttributes.REGULAR_ATTRIBUTES)
                if (payload.snippet.isNotBlank()) {
                    append("   ")
                    append(payload.snippet, SimpleTextAttributes.GRAYED_ATTRIBUTES)
                }
            }
            else -> append(payload?.toString() ?: "")
        }
    }
}
