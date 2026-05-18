package com.cryptiklemur.riderilspy.search

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
    private val queryTypeBox = JComboBox(arrayOf("Literal", "Attribute", "Token", "Constant", "Resource"))
    private val inputField = JTextField()
    private val regexBox = JCheckBox("regex")
    private val caseBox = JCheckBox("case-sensitive")
    private val wordBox = JCheckBox("whole word")
    private val statusLabel = JBLabel(" ")

    private val rootNode = DefaultMutableTreeNode("results")
    private val treeModel = DefaultTreeModel(rootNode)
    private val tree = Tree(treeModel).apply {
        isRootVisible = false
        showsRootHandles = true
        selectionModel.selectionMode = TreeSelectionModel.SINGLE_TREE_SELECTION
        cellRenderer = ResultsTreeRenderer()
        emptyText.text = "no results yet"
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
            queryType = queryTypeBox.selectedItem as String,
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
                "$totalHits results in ${assemblyNodes.size} assemblies"
            else
                "$totalHits results in ${assemblyNodes.size} assemblies... still searching"
        }
    }

    private fun renderState(snapshot: IlSpySearchIndexStateSnapshot) {
        SwingUtilities.invokeLater {
            statusLabel.text = when (snapshot.phase) {
                "Building" -> "indexing ${snapshot.indexed}/${snapshot.total}"
                "Failed" -> "indexing failed: ${snapshot.errorMessage}"
                "Idle" -> "idle"
                else -> if (snapshot.skipped > 0) "${snapshot.indexed} indexed · ${snapshot.skipped} skipped" else "${snapshot.indexed} indexed"
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
        row1.add(JButton("Search").apply { addActionListener { runSearch() } }, BorderLayout.EAST)
        val row2 = JPanel()
        row2.add(regexBox)
        row2.add(caseBox)
        row2.add(wordBox)
        bar.add(row1)
        bar.add(row2)
        return bar
    }
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
