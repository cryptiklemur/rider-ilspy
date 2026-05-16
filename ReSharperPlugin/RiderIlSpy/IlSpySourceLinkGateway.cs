using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using JetBrains.Application;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace RiderIlSpy;

/// <summary>
/// Integration boundary between the navigation pipeline and the SourceLink fetch
/// subsystem. Wraps <see cref="PdbSourceLinkReader"/> + <see cref="SourceLinkMapping"/>
/// + <see cref="SourceLinkSourceFetcher"/> into a single typed entry point —
/// callers get back a <see cref="SourceLinkAttempt"/> describing either the
/// fetched content or the canonical "why didn't this work" status.
/// </summary>
/// <remarks>
/// Lives on its own [ShellComponent] (rather than as a pass-through method on
/// IlSpyDecompiler) for three reasons:
/// <list type="bullet">
///   <item>The SourceLink flow is a distinct concern — it consumes a PDB, a JSON
///   mapping, and an HTTP fetcher; it doesn't touch ICSharpCode.Decompiler at all.</item>
///   <item>The shared <see cref="HttpClient"/> is a single long-lived instance per
///   process — it belongs on the component that actually makes HTTP requests.
///   Per-fetch HttpClient creation leaks TCP sockets on .NET Core.</item>
///   <item>Decompiler-side test fixtures don't need a working HttpClient to
///   exercise the decompile paths; isolating the gateway means decompile-only
///   tests aren't coupled to the SourceLink integration.</item>
/// </list>
/// </remarks>
[ShellComponent]
public sealed class IlSpySourceLinkGateway
{
    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpySourceLinkGateway>();

    // Single HttpClient instance reused across SourceLink fetches. HttpClient is
    // documented as designed to be long-lived; creating one per fetch leaks TCP
    // sockets on .NET Core and triggers SocketException after a few thousand
    // requests under sustained navigation.
    private readonly HttpClient mySharedHttpClient = CreateSharedHttpClient();

    private static HttpClient CreateSharedHttpClient()
    {
        HttpClient client = new HttpClient();
        // raw.githubusercontent.com (the most common SourceLink target) accepts
        // anonymous GETs without a User-Agent, but other Git hosts (e.g. some
        // self-hosted Gitea) reject empty UAs. Identifying ourselves keeps the
        // fetch unblocked on any vendor's traffic-filter.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RiderIlSpy/1.0 (+https://github.com/cryptiklemur/rider-ilspy)");
        return client;
    }

    /// <summary>
    /// When <paramref name="assemblyPath"/>'s portable PDB carries a SourceLink
    /// CustomDebugInformation entry, looks up <paramref name="typeFullName"/>'s
    /// primary source document, fetches it from the published URL, and returns
    /// the fetched content plus a status identifier. <see cref="SourceLinkAttempt.Content"/>
    /// is <c>null</c> on any failure — caller falls back to ILSpy decompilation
    /// and the <see cref="SourceLinkAttempt.Outcome"/> tells you which step bailed.
    /// </summary>
    /// <remarks>
    /// Outcome statuses (see <see cref="SourceLinkStatus"/> for the full enum):
    /// <see cref="SourceLinkStatus.NoPdb"/>, <see cref="SourceLinkStatus.NoSourceLinkEntry"/>,
    /// <see cref="SourceLinkStatus.MalformedJson"/>, <see cref="SourceLinkStatus.NoDocument"/>,
    /// <see cref="SourceLinkStatus.NoUrlMapping"/>, <see cref="SourceLinkStatus.HttpFailed"/>,
    /// <see cref="SourceLinkStatus.Exception"/>, <see cref="SourceLinkStatus.Used"/>.
    /// <see cref="SourceLinkOutcome.UsedUrl"/> is populated on <see cref="SourceLinkStatus.Used"/>;
    /// <see cref="SourceLinkOutcome.ExceptionType"/> on <see cref="SourceLinkStatus.Exception"/>.
    /// </remarks>
    public SourceLinkAttempt Fetch(string assemblyPath, string typeFullName, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds <= 0) timeoutSeconds = 5;
        try
        {
            using PdbSourceLinkReader? pdb = PdbSourceLinkReader.TryOpen(assemblyPath);
            // No per-branch Info logs: the SourceLinkAttempt.Outcome return value is
            // already the canonical "why did SourceLink not fire" signal, surfaced
            // in the diagnostic banner. Logging it here just duplicates the status
            // string at a different sink. The catch below still logs Warn for the
            // unexpected-exception path because the Status alone ("exception: X")
            // doesn't carry the message.
            if (pdb == null)
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.NoPdb));
            string? json = pdb.TryReadSourceLinkJson();
            if (json == null)
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.NoSourceLinkEntry));
            SourceLinkMapping? mapping = SourceLinkMapping.TryParse(json);
            if (mapping == null)
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.MalformedJson));
            string? documentPath = pdb.TryGetPrimaryDocumentPath(typeFullName);
            if (string.IsNullOrEmpty(documentPath))
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.NoDocument));
            string? url = mapping.ResolveUrl(documentPath);
            if (string.IsNullOrEmpty(url))
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.NoUrlMapping));
            string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpy", "sourcelink-cache");
            SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(mySharedHttpClient, cacheRoot, TimeSpan.FromSeconds(timeoutSeconds));
            string? fetched = fetcher.GetOrFetch(url, cancellationToken);
            if (string.IsNullOrEmpty(fetched))
                return new SourceLinkAttempt(null, SourceLinkOutcome.Plain(SourceLinkStatus.HttpFailed));
            return new SourceLinkAttempt(fetched, SourceLinkOutcome.UsedAt(url));
        }
        catch (Exception ex)
        {
            // Defensive: we never want a SourceLink lookup to bubble up. Any
            // failure here just means "fall back to decompile" upstream.
            ourLogger.Warn("RiderIlSpy.SourceLink fetch for " + typeFullName + " threw: " + ex.GetType().Name + ": " + ex.Message);
            return new SourceLinkAttempt(null, SourceLinkOutcome.ExceptionOf(ex.GetType().Name));
        }
    }
}
