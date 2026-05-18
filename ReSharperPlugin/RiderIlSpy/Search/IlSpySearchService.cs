using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;
using JetBrains.Application.FileSystemTracker;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Tasks;
using JetBrains.Rd;
using JetBrains.Rd.Base;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Util;
using JetBrains.Util.Logging;
using RiderIlSpy.Model;

namespace RiderIlSpy.Search;

[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class IlSpySearchService
{
    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpySearchService>();

    private readonly Lifetime myLifetime;
    private readonly RiderIlSpyModel myModel;
    private readonly IFileSystemTracker myFileSystemTracker;
    private readonly IlSpyAssemblyWatcher myWatcher;

    private IlSpySearchIndex? myIndex;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> myActiveSearches = new();
    private readonly IlSpyNavResolver myNavResolver = new IlSpyNavResolver();

    public IlSpySearchService(
        Lifetime lifetime,
        ISolution solution,
        IFileSystemTracker fileSystemTracker,
        ISolutionLoadTasksScheduler scheduler)
    {
        myLifetime = lifetime;
        myFileSystemTracker = fileSystemTracker;
        myModel = solution.GetProtocolSolution().GetRiderIlSpyModel();
        myWatcher = new IlSpyAssemblyWatcher(myLifetime, OnAssemblyChanged);

        PublishState("Idle", 0, 0, 0, "");

        myModel.RunSearch.Set((lt, request) => RdTask.Successful(HandleRunSearch(lt, request)));
        myModel.CancelSearch.Advise(myLifetime, OnCancel);
        myModel.RescanAssembly.Advise(myLifetime, OnRescan);
        myModel.ResolveNavTarget.Set((lt, navTarget) => HandleResolveNavTarget(lt, navTarget));

        scheduler.EnqueueTask(new SolutionLoadTask(
            GetType(),
            SolutionLoadTaskKinds.Done,
            () => StartIndexBuild(solution)));
    }

    private void StartIndexBuild(ISolution solution)
    {
        IlSpySolutionAssemblyEnumerator enumerator = new IlSpySolutionAssemblyEnumerator(solution);
        List<string> paths = new List<string>(enumerator.EnumerateReferencedAssemblyPaths());
        ourLogger.Info($"ilspy-search: index build starting with {paths.Count} assemblies");
        PublishState("Building", 0, paths.Count, 0, "");

        Task.Run(() =>
        {
            try
            {
                IlSpySearchIndexer indexer = new IlSpySearchIndexer();
                IlSpyIndexBuildProgress? lastProgress = null;
                int loggedSkips = 0;
                IlSpySearchIndex index = indexer.BuildAll(
                    paths,
                    p => { lastProgress = p; PublishState("Building", p.Indexed, p.Total, p.Skipped, ""); },
                    myLifetime.ToCancellationToken(),
                    (path, ex) =>
                    {
                        if (loggedSkips++ < 5)
                            ourLogger.Warn($"ilspy-search: skipped {path}: {ex.GetType().Name}: {ex.Message}");
                    });
                myIndex = index;
                int indexedCount = lastProgress?.Indexed ?? 0;
                int skippedCount = lastProgress?.Skipped ?? 0;
                ourLogger.Info($"ilspy-search: index ready, indexed={indexedCount} skipped={skippedCount} total={paths.Count}");
                PublishState("Ready", indexedCount, paths.Count, skippedCount, "");
                SubscribeFileSystem();
            }
            catch (OperationCanceledException) { /* solution closing — drop silently */ }
            catch (Exception ex)
            {
                PublishState("Failed", 0, paths.Count, 0, ex.Message);
                ourLogger.Error(ex, "ilspy-search: index build failed");
            }
        }, myLifetime.ToCancellationToken());
    }

    private void SubscribeFileSystem()
    {
        if (myIndex == null) return;
        foreach (AssemblyMetadata asm in myIndex.RegisteredAssemblies())
        {
            string displayPath = asm.DisplayPath;
            VirtualFileSystemPath vpath = VirtualFileSystemPath.Parse(
                displayPath,
                InteractionContext.SolutionContext,
                FileSystemPathInternStrategy.TRY_GET_INTERNED_BUT_DO_NOT_INTERN);
            myFileSystemTracker.AdviseFileChanges(myLifetime, vpath, _ => myWatcher.OnFileChanged(displayPath));
        }
    }

    private void OnAssemblyChanged(string path) => OnRescan(path);

    private string HandleRunSearch(Lifetime requestLt, SearchRequest request)
    {
        ourLogger.Info($"ilspy-search: request type={request.QueryType} input='{request.Input}' indexReady={myIndex != null}");
        if (myIndex == null)
        {
            EmitErrorBatch(request.SearchId, RiderIlSpy.Resources.Strings.Search_IndexNotReady);
            return request.SearchId;
        }

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            myLifetime.ToCancellationToken());
        myActiveSearches[request.SearchId] = cts;

        Task.Run(() =>
        {
            try
            {
                ourLogger.Info($"ilspy-search: dispatch start id={request.SearchId} type={request.QueryType}");
                DispatchQuery(request, cts.Token);
                ourLogger.Info($"ilspy-search: dispatch done id={request.SearchId}");
            }
            catch (OperationCanceledException) { ourLogger.Info($"ilspy-search: dispatch cancelled id={request.SearchId}"); }
            catch (Exception ex)
            {
                ourLogger.Warn($"ilspy-search: dispatch failed id={request.SearchId}: {ex.GetType().Name}: {ex.Message}");
                EmitErrorBatch(request.SearchId, ex.Message);
            }
            finally { myActiveSearches.TryRemove(request.SearchId, out _); }
        }, cts.Token);

        return request.SearchId;
    }

    private void DispatchQuery(SearchRequest request, CancellationToken ct)
    {
        switch (request.QueryType)
        {
            case "Literal": EmitLiteralResults(request, ct); break;
            case "Attribute": EmitAttributeResults(request, ct); break;
            case "Token": EmitTokenResults(request, ct); break;
            case "Constant": EmitConstantResults(request, ct); break;
            case "Resource": EmitResourceResults(request, ct); break;
            default: EmitErrorBatch(request.SearchId, RiderIlSpy.Resources.Strings.Search_UnknownQueryType(request.QueryType)); break;
        }
    }

    private void EmitLiteralResults(SearchRequest req, CancellationToken ct)
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(myIndex!);
        List<LiteralIndexEntry> hits = handler.Query(
            new LiteralQuery(req.Input, req.CaseSensitive, req.Regex, req.WholeWord));
        EmitBatched(req.SearchId, hits, ToRow, req.MaxResults, ct);
    }

    private void EmitAttributeResults(SearchRequest req, CancellationToken ct)
    {
        AttributeQueryHandler handler = new AttributeQueryHandler(myIndex!);
        List<AttributeIndexEntry> hits = handler.Query(req.Input);
        EmitBatched(req.SearchId, hits, ToRow, req.MaxResults, ct);
    }

    private void EmitTokenResults(SearchRequest req, CancellationToken ct)
    {
        if (!TokenQueryHandler.TryParse(req.Input, out int token, out string? _))
        {
            EmitErrorBatch(req.SearchId, RiderIlSpy.Resources.Strings.Search_InvalidTokenFormat);
            return;
        }

        SearchResultRow row = new SearchResultRow(
            assemblyName: "?",
            target: $"#{token:X8}",
            snippet: "",
            matchStart: 0,
            matchLength: 0,
            navTarget: new NavTarget(
                kind: "Code",
                assemblyPath: "",
                metadataToken: token,
                ilOffset: -1,
                resourceEntry: "",
                mimeHint: ""));

        FireBatch(req.SearchId, new List<SearchResultRow> { row }, isComplete: true, errorMessage: "");
    }

    private void EmitConstantResults(SearchRequest req, CancellationToken ct)
    {
        IReadOnlyCollection<AssemblyMetadata> assemblies = myIndex!.RegisteredAssemblies();
        List<PEFile> peFiles = new List<PEFile>(assemblies.Count);
        try
        {
            foreach (AssemblyMetadata asm in assemblies)
            {
                try { peFiles.Add(new PEFile(asm.DisplayPath)); }
                catch { /* skip unreadable assemblies */ }
            }

            ConstantQueryHandler handler = new ConstantQueryHandler();
            List<ConstantHit> hits = handler.Scan(peFiles, req.Input);
            EmitBatched(req.SearchId, hits, ToRow, req.MaxResults, ct);
        }
        finally
        {
            foreach (PEFile pe in peFiles) pe.Dispose();
        }
    }

    private void EmitResourceResults(SearchRequest req, CancellationToken ct)
    {
        ResourceQueryHandler handler = new ResourceQueryHandler(myIndex!);
        List<ResourceIndexEntry> hits = handler.Query(req.Input);
        EmitBatched(req.SearchId, hits, ToRow, req.MaxResults, ct);
    }

    private static SearchResultRow ToRow(LiteralIndexEntry e) =>
        new SearchResultRow(
            assemblyName: Path.GetFileName(e.AssemblyId.NormalizedPath),
            target: $"#{e.ContainingMethodToken:X8}",
            snippet: e.StringValue,
            matchStart: 0,
            matchLength: 0,
            navTarget: new NavTarget(
                kind: "Code",
                assemblyPath: e.AssemblyId.NormalizedPath,
                metadataToken: e.ContainingMethodToken,
                ilOffset: e.IlOffset,
                resourceEntry: "",
                mimeHint: ""));

    private static SearchResultRow ToRow(AttributeIndexEntry e) =>
        new SearchResultRow(
            assemblyName: Path.GetFileName(e.AssemblyId.NormalizedPath),
            target: $"#{e.TargetMetadataToken:X8} ({e.TargetKind})",
            snippet: $"{e.AttributeTypeFullName}{e.ArgsSummary}",
            matchStart: 0,
            matchLength: 0,
            navTarget: new NavTarget(
                kind: "Code",
                assemblyPath: e.AssemblyId.NormalizedPath,
                metadataToken: e.TargetMetadataToken,
                ilOffset: -1,
                resourceEntry: "",
                mimeHint: ""));

    private static SearchResultRow ToRow(ConstantHit h) =>
        new SearchResultRow(
            assemblyName: Path.GetFileName(h.AssemblyId.NormalizedPath),
            target: $"#{h.DeclaringToken:X8}",
            snippet: h.Value,
            matchStart: 0,
            matchLength: 0,
            navTarget: new NavTarget(
                kind: "Code",
                assemblyPath: h.AssemblyId.NormalizedPath,
                metadataToken: h.DeclaringToken,
                ilOffset: -1,
                resourceEntry: "",
                mimeHint: ""));

    private static SearchResultRow ToRow(ResourceIndexEntry e) =>
        new SearchResultRow(
            assemblyName: Path.GetFileName(e.AssemblyId.NormalizedPath),
            target: e.ResourceName,
            snippet: $"{e.SizeBytes} bytes ({e.MimeHint})",
            matchStart: 0,
            matchLength: 0,
            navTarget: new NavTarget(
                kind: "Resource",
                assemblyPath: e.AssemblyId.NormalizedPath,
                metadataToken: e.ManifestResourceToken,
                ilOffset: -1,
                resourceEntry: e.ParentEntryName ?? "",
                mimeHint: e.MimeHint));

    private void EmitBatched<T>(
        string searchId,
        List<T> hits,
        Func<T, SearchResultRow> toRow,
        int maxResults,
        CancellationToken ct)
    {
        const int batchSize = 50;
        List<SearchResultRow> batch = new List<SearchResultRow>(batchSize);
        int sent = 0;
        foreach (T h in hits)
        {
            ct.ThrowIfCancellationRequested();
            if (sent >= maxResults) break;
            batch.Add(toRow(h));
            sent++;
            if (batch.Count == batchSize)
            {
                FireBatch(searchId, batch, isComplete: false, errorMessage: "");
                batch = new List<SearchResultRow>(batchSize);
            }
        }
        FireBatch(searchId, batch, isComplete: true, errorMessage: "");
    }

    private void FireBatch(string searchId, List<SearchResultRow> rows, bool isComplete, string errorMessage)
    {
        ourLogger.Info($"ilspy-search: fire batch id={searchId} rows={rows.Count} complete={isComplete} err='{errorMessage}'");
        SearchResultBatch payload = new SearchResultBatch(searchId, rows, isComplete, errorMessage);
        IProtocol protocol = ((IRdDynamic)myModel).TryGetProto();
        if (protocol != null)
            protocol.Scheduler.Queue(() => myModel.SearchResultBatch.Fire(payload));
    }

    private void EmitErrorBatch(string searchId, string message)
    {
        FireBatch(searchId, new List<SearchResultRow>(), isComplete: true, errorMessage: message);
    }

    private void OnCancel(string searchId)
    {
        if (myActiveSearches.TryRemove(searchId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void OnRescan(string assemblyPath)
    {
        if (myIndex == null) return;
        AssemblyId id = AssemblyId.From(assemblyPath);
        myIndex.DropAssembly(id);
        Task.Run(() =>
        {
            try
            {
                using PEFile pe = new PEFile(assemblyPath);
                AssemblyMetadata metadata = AssemblyMetadata.From(assemblyPath);
                IlSpySearchIndexer indexer = new IlSpySearchIndexer();
                indexer.IndexLiterals(pe, metadata, myIndex);
                indexer.IndexAttributes(pe, metadata, myIndex);
                indexer.IndexResources(pe, metadata, myIndex);
            }
            catch (Exception ex)
            {
                ourLogger.Warn($"ilspy-search: rescan failed for {assemblyPath}: {ex.GetType().Name}: {ex.Message}");
            }
        }, myLifetime.ToCancellationToken());
    }

    private RdTask<NavResolution> HandleResolveNavTarget(Lifetime requestLt, NavTarget target)
    {
        RdTask<NavResolution> task = new RdTask<NavResolution>();
        Task.Run(() =>
        {
            try
            {
                IlSpyNavResolution result = myNavResolver.Resolve(target.AssemblyPath, target.MetadataToken, target.IlOffset);
                NavResolution payload = new NavResolution(
                    success: result.Success,
                    filePath: result.FilePath,
                    line: result.Line,
                    column: result.Column,
                    errorMessage: result.ErrorMessage);
                task.Set(payload);
            }
            catch (Exception ex)
            {
                ourLogger.Warn($"ilspy-search nav: handler failed path={target.AssemblyPath} token=0x{target.MetadataToken:X8}: {ex.GetType().Name}: {ex.Message}");
                task.Set(new NavResolution(false, string.Empty, 1, 1, ex.Message));
            }
        }, myLifetime.ToCancellationToken());
        return task;
    }

    private void PublishState(string phase, int indexed, int total, int skipped, string error)
    {
        SearchIndexState state = new SearchIndexState(phase, indexed, total, skipped, error);
        IProtocol protocol = ((IRdDynamic)myModel).TryGetProto();
        if (protocol != null)
            protocol.Scheduler.Queue(() => myModel.SearchIndexState.Value = state);
        else
            myModel.SearchIndexState.Value = state;
    }
}
