using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RiderIlSpy;

/// <summary>
/// Downloads source files referenced by SourceLink and caches them on disk so
/// later navigations to the same type are instant.
///
/// The cache key is the SHA-256 of the URL; the on-disk layout is
/// <c>{cacheRoot}/{first 2 hex chars}/{full hex}.txt</c>. We avoid mirroring
/// the URL path because GitHub raw URLs include the commit SHA, branch, and
/// arbitrarily deep repo paths — easy to collide with case-insensitive file
/// systems (Windows, macOS default APFS) and easy to exceed Windows MAX_PATH.
/// Hash-based naming sidesteps both.
/// </summary>
public sealed class SourceLinkSourceFetcher
{
    private readonly HttpClient myHttpClient;
    private readonly string myCacheRoot;
    private readonly TimeSpan myTimeout;

    public SourceLinkSourceFetcher(HttpClient httpClient, string cacheRoot, TimeSpan timeout)
    {
        myHttpClient = httpClient;
        myCacheRoot = cacheRoot;
        myTimeout = timeout;
    }

    /// <summary>
    /// Returns the cached source content for <paramref name="url"/> if present,
    /// otherwise downloads it. Returns <c>null</c> when the cache lookup AND
    /// the download both fail — caller falls back to ILSpy decompilation.
    /// </summary>
    public string? FetchOrCached(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url)) return null;
        string cachePath = GetCachePath(url);
        if (File.Exists(cachePath))
        {
            try { return File.ReadAllText(cachePath); }
            catch (IOException) { /* fall through to refetch */ }
        }
        string? downloaded = TryDownload(url, cancellationToken);
        if (downloaded == null) return null;
        TryWriteCache(cachePath, downloaded);
        return downloaded;
    }

    /// <summary>
    /// Exposed for tests — computes the on-disk path a given URL would map to.
    /// </summary>
    public string GetCachePath(string url)
    {
        string hash = ComputeUrlHash(url);
        string bucket = hash.Substring(0, 2);
        return Path.Combine(myCacheRoot, bucket, hash + ".txt");
    }

    internal static string ComputeUrlHash(string url)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(url);
        byte[] hash = SHA256.HashData(bytes);
        StringBuilder sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    private string? TryDownload(string url, CancellationToken cancellationToken)
    {
        // We're called from the ReSharper external-source-provider pipeline, which
        // is synchronous. HttpClient.Send (the sync sibling of SendAsync) avoids
        // sync-over-async deadlocks AND keeps the call shape sync without bleeding
        // async/await up into the decompiler interface.
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(myTimeout);
        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = myHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            using Stream stream = response.Content.ReadAsStream(cts.Token);
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private void TryWriteCache(string cachePath, string content)
    {
        try
        {
            string? dir = Path.GetDirectoryName(cachePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(cachePath, content);
        }
        catch (IOException) { /* best-effort cache write */ }
        catch (UnauthorizedAccessException) { /* readonly filesystem etc. */ }
    }
}
