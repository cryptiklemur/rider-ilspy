using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RiderIlSpy.Tests;

public class SourceLinkSourceFetcherTests
{
    [Fact]
    public void GetCachePath_buckets_by_first_two_hash_chars()
    {
        SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(
            new HttpClient(),
            "/tmp/cache-root",
            TimeSpan.FromSeconds(1));
        string url = "https://example.com/foo.cs";
        string path = fetcher.GetCachePath(url);

        Assert.StartsWith("/tmp/cache-root" + Path.DirectorySeparatorChar, path);
        // The hash function is documented as SHA-256-of-UTF8, so any non-empty
        // input produces a hex digest of length 64 and a 2-char bucket.
        string hash = SourceLinkSourceFetcher.ComputeUrlHash(url);
        Assert.Equal(64, hash.Length);
        Assert.EndsWith(hash + ".txt", path);
        Assert.Contains(hash.Substring(0, 2) + Path.DirectorySeparatorChar + hash, path);
    }

    [Fact]
    public void FetchOrCached_returns_cached_content_without_hitting_network()
    {
        // No HTTP handler — any request would throw, so a network roundtrip
        // would fail the test loudly. The cache hit must short-circuit before
        // touching the HttpClient.
        HttpMessageHandler boomHandler = new BoomHandler();
        HttpClient client = new HttpClient(boomHandler);
        string root = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(client, root, TimeSpan.FromSeconds(1));
            string url = "https://example.com/cached.cs";
            string cachePath = fetcher.GetCachePath(url);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, "// cached body");

            string? result = fetcher.FetchOrCached(url);
            Assert.Equal("// cached body", result);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FetchOrCached_downloads_and_caches_on_miss()
    {
        string body = "// remote source body\nclass X {}";
        HttpClient client = new HttpClient(new StubHandler(body, HttpStatusCode.OK));
        string root = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(client, root, TimeSpan.FromSeconds(2));
            string url = "https://example.com/remote.cs";

            string? first = fetcher.FetchOrCached(url);
            Assert.Equal(body, first);
            Assert.True(File.Exists(fetcher.GetCachePath(url)));
            Assert.Equal(body, File.ReadAllText(fetcher.GetCachePath(url)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FetchOrCached_returns_null_on_non_success_status()
    {
        HttpClient client = new HttpClient(new StubHandler("not found", HttpStatusCode.NotFound));
        string root = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(client, root, TimeSpan.FromSeconds(2));
            string? result = fetcher.FetchOrCached("https://example.com/missing.cs");
            Assert.Null(result);
            // A 404 must NOT poison the cache. Otherwise a transient outage
            // turns into permanent failure for subsequent navigations.
            Assert.False(File.Exists(fetcher.GetCachePath("https://example.com/missing.cs")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FetchOrCached_returns_null_when_url_is_empty()
    {
        SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(
            new HttpClient(new BoomHandler()),
            "/tmp/unused",
            TimeSpan.FromSeconds(1));
        Assert.Null(fetcher.FetchOrCached(""));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string myBody;
        private readonly HttpStatusCode myStatus;
        public StubHandler(string body, HttpStatusCode status)
        {
            myBody = body;
            myStatus = status;
        }
        private HttpResponseMessage BuildResponse()
        {
            return new HttpResponseMessage(myStatus)
            {
                Content = new StringContent(myBody),
            };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildResponse());
        }
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return BuildResponse();
        }
    }

    private sealed class BoomHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("test should not have hit the network");
        }
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("test should not have hit the network");
        }
    }
}
