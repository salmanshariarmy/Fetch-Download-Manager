using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Fetch.Core;

/// <summary>
/// A direct HTTP(S) downloader with verified byte ranges and disk-backed resume.
/// Network work never runs on the UI thread. Does not bypass authentication or DRM.
/// </summary>
public sealed class TransferEngine : IDisposable
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _http;
    private readonly JobStore _store;
    private readonly BandwidthLimiter _limiter;

    private sealed record RemoteInfo(long? Length, bool Ranges, string? ETag,
        string? LastModified, bool IdentityEncoding, string? SuggestedFileName);

    private sealed class RangeNotHonoredException : IOException
    {
        public RangeNotHonoredException() : base("The server ignored or changed the requested byte range.") { }
    }

    private sealed class NonRetryableHttpException : IOException
    {
        public NonRetryableHttpException(HttpStatusCode status)
            : base($"HTTP {(int)status} ({status}). The server rejected this download; it will not be retried automatically.") { }
    }

    private sealed class ProgressCounter
    {
        private long _bytes;
        private long _lastTick;
        private long _lastBytes;
        private readonly long? _total;
        private readonly IProgress<TransferProgress>? _progress;

        public ProgressCounter(long startingBytes, long? total, IProgress<TransferProgress>? progress)
        {
            _bytes = startingBytes;
            _lastBytes = startingBytes;
            _total = total;
            _progress = progress;
            _lastTick = Environment.TickCount64;
            progress?.Report(new TransferProgress(startingBytes, total, 0));
        }

        public long Bytes => Interlocked.Read(ref _bytes);

        public void Add(int amount)
        {
            long current = Interlocked.Add(ref _bytes, amount);
            long now = Environment.TickCount64;
            long oldTick = Interlocked.Read(ref _lastTick);
            if (now - oldTick < 250 ||
                Interlocked.CompareExchange(ref _lastTick, now, oldTick) != oldTick) return;
            long previous = Interlocked.Exchange(ref _lastBytes, current);
            double speed = Math.Max(0, (current - previous) * 1000.0 / Math.Max(1, now - oldTick));
            _progress?.Report(new TransferProgress(current, _total, speed));
        }

        public void Finish() => _progress?.Report(new TransferProgress(Bytes, _total, 0));
    }

    public TransferEngine(JobStore store, DownloadPreferences? preferences = null)
    {
        _store = store;
        _limiter = new BandwidthLimiter(preferences?.RateLimitKiB ?? 0);
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            MaxConnectionsPerServer = 32,
            UseCookies = false
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Fetch/0.1 (Windows download manager)");
    }

    public void SetSpeedLimitKiB(int value) => _limiter.SetLimit(value);

    public async Task<TransferResult> DownloadAsync(
        DownloadItem item,
        IProgress<TransferProgress>? progress,
        Func<Task>? onMetadataReady,
        CancellationToken cancellation)
    {
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Only absolute HTTP and HTTPS URLs are supported.");

        Directory.CreateDirectory(item.SaveDirectory);
        string partDir = Path.Combine(_store.PartsFolder, item.Id.ToString("N"));
        Directory.CreateDirectory(partDir);
        var remote = await ProbeAsync(url, item.ReferrerOrigin, cancellation).ConfigureAwait(false);
        bool noExistingParts = !Directory.EnumerateFiles(partDir, "seg-*.part").Any();
        if (item.SegmentCount == 0 && noExistingParts &&
            !string.IsNullOrWhiteSpace(remote.SuggestedFileName))
            item.FileName = FileNameSafety.Sanitize(remote.SuggestedFileName);
        int proposedSegments = SegmentPlan.Recommend(remote.Length,
            remote.Ranges && remote.IdentityEncoding, desired: Math.Clamp(item.RequestedConnections, 1, 32));
        if (item.SegmentCount > 0 && item.SegmentCount != proposedSegments)
            EraseParts(partDir);

        // Resuming without a server validator risks silently mixing two file versions.
        bool partialExists = Directory.EnumerateFiles(partDir, "seg-*.part").Any();
        bool hasValidator = !string.IsNullOrWhiteSpace(remote.ETag) ||
                            !string.IsNullOrWhiteSpace(remote.LastModified);
        bool validatorChanged = (item.ETag != null && item.ETag != remote.ETag) ||
            (item.LastModified != null && item.LastModified != remote.LastModified) ||
            (item.TotalBytes.HasValue && item.TotalBytes != remote.Length);
        if (partialExists && (!hasValidator || validatorChanged ||
                              (!remote.Ranges && proposedSegments == 1)))
            EraseParts(partDir);

        item.ETag = remote.ETag;
        item.LastModified = remote.LastModified;
        item.TotalBytes = remote.Length;
        item.SegmentCount = proposedSegments;
        if (onMetadataReady != null) await onMetadataReady().ConfigureAwait(false);

        if (remote.Length == 0)
        {
            // An empty file must satisfy the requested checksum, too.
            if (!string.IsNullOrWhiteSpace(item.ExpectedSha256) &&
                !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>()))
                    .Equals(item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("SHA-256 mismatch for empty download; no final file was created.");
            string emptyTarget = FileNameSafety.FreePath(item.SaveDirectory, item.FileName);
            await using (var empty = new FileStream(emptyTarget, FileMode.CreateNew,
                FileAccess.Write, FileShare.None)) { }
            TryMarkInternetZone(emptyTarget, item.Url);
            item.FileName = Path.GetFileName(emptyTarget);
            progress?.Report(new TransferProgress(0, 0, 0));
            return new TransferResult(emptyTarget, 0);
        }

        try
        {
            return await DownloadContentAsync(item, remote, partDir, progress, cancellation)
                .ConfigureAwait(false);
        }
        catch (RangeNotHonoredException) when (proposedSegments > 1)
        {
            // Some servers lie about Accept-Ranges. Safely retry with one stream.
            EraseParts(partDir);
            item.SegmentCount = 1;
            if (onMetadataReady != null) await onMetadataReady().ConfigureAwait(false);
            return await DownloadContentAsync(item, remote with { Ranges = false }, partDir,
                progress, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<RemoteInfo> ProbeAsync(Uri url, string? referrerOrigin, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        ApplySafeReferrer(head, referrerOrigin);
        head.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        using var response = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return ReadMetadata(response);

        // HEAD is blocked by some file hosts; fall back to a zero-byte range probe.
        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        ApplySafeReferrer(get, referrerOrigin);
        get.Headers.Range = new RangeHeaderValue(0, 0);
        get.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        using var fallback = await _http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        fallback.EnsureSuccessStatusCode();
        return ReadMetadata(fallback);
    }

    private static RemoteInfo ReadMetadata(HttpResponseMessage response)
    {
        long? length = response.Content.Headers.ContentRange?.Length ??
                       response.Content.Headers.ContentLength;
        bool ranges = response.Headers.AcceptRanges.Any(s =>
                          s.Equals("bytes", StringComparison.OrdinalIgnoreCase)) ||
                      response.StatusCode == HttpStatusCode.PartialContent;
        bool identity = response.Content.Headers.ContentEncoding.Count == 0 ||
            response.Content.Headers.ContentEncoding.All(x =>
                x.Equals("identity", StringComparison.OrdinalIgnoreCase));
        string? etag = response.Headers.ETag?.IsWeak == false ? response.Headers.ETag.Tag : null;
        string? modified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);
        string? suggested = response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName;
        return new RemoteInfo(length, ranges, etag, modified, identity, suggested);
    }

    private async Task<TransferResult> DownloadContentAsync(
        DownloadItem item, RemoteInfo remote, string partDir,
        IProgress<TransferProgress>? progress, CancellationToken cancellation)
    {
        int count = item.SegmentCount;
        IReadOnlyList<ByteRange>? ranges = remote.Length.HasValue
            ? SegmentPlan.Split(remote.Length.Value, count) : null;
        long previousBytes = 0;
        for (int index = 0; index < count; index++)
        {
            string path = PartPath(partDir, index);
            if (!File.Exists(path)) continue;
            long existing = new FileInfo(path).Length;
            if (ranges != null && existing > ranges[index].Length)
            {
                File.Delete(path);
                continue;
            }
            previousBytes += existing;
        }
        var tracker = new ProgressCounter(previousBytes, remote.Length, progress);
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var tasks = new List<Task>();
        for (int i = 0; i < count; i++)
        {
            int index = i;
            tasks.Add(DownloadPartWithRetryAsync(item.Url, item.ReferrerOrigin, remote, partDir, index,
                ranges == null ? null : ranges[index], tracker, siblings.Token));
        }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            await siblings.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { /* preserve original error */ }
            throw;
        }
        cancellation.ThrowIfCancellationRequested();
        if (remote.Length.HasValue && tracker.Bytes != remote.Length.Value)
            throw new IOException($"Incomplete data: {tracker.Bytes} of {remote.Length.Value} bytes.");

        string target = Path.Combine(item.SaveDirectory, FileNameSafety.Sanitize(item.FileName));
        // Never replace a user's existing file. A collision is resolved before merging.
        target = FileNameSafety.FreePath(item.SaveDirectory, Path.GetFileName(target));
        item.FileName = Path.GetFileName(target);
        string merging = target + ".fetching-" + item.Id.ToString("N");
        try
        {
            await using (var output = new FileStream(merging, FileMode.Create,
                FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                for (int index = 0; index < count; index++)
                {
                    await using var input = new FileStream(PartPath(partDir, index), FileMode.Open,
                        FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, BufferSize, cancellation).ConfigureAwait(false);
                }
                output.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            // Reject a mismatched checksum before exposing the final downloaded file.
            await HashVerifier.VerifySha256Async(merging, item.ExpectedSha256, cancellation).ConfigureAwait(false);
            // Same-volume rename from the merge file to the final download.
            File.Move(merging, target, overwrite: false);
            TryMarkInternetZone(target, item.Url);
            EraseParts(partDir);
            tracker.Finish();
            return new TransferResult(target, new FileInfo(target).Length);
        }
        finally
        {
            if (File.Exists(merging)) File.Delete(merging);
        }
    }

    private async Task DownloadPartWithRetryAsync(
        string rawUrl, string? referrerOrigin, RemoteInfo remote, string partDir, int index,
        ByteRange? range, ProgressCounter tracker, CancellationToken ct)
    {
        string path = PartPath(partDir, index);
        for (int attempt = 0; attempt <= 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long have = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (range.HasValue && have == range.Value.Length) return;
                if (attempt > 0 && have > 0 && remote.ETag == null && remote.LastModified == null)
                    throw new InvalidOperationException("Cannot safely retry a partial transfer without an ETag or Last-Modified validator.");
                bool seek = range.HasValue && (remote.Ranges || index != 0 || have > 0);
                if (!range.HasValue && have > 0)
                    throw new IOException("Cannot resume a download with unknown size.");
                if (!remote.Ranges && have > 0)
                    throw new IOException("The server does not permit safe resume.");
                using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
                ApplySafeReferrer(request, referrerOrigin);
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                if (seek && range.HasValue)
                {
                    request.Headers.Range = new RangeHeaderValue(range.Value.Start + have, range.Value.End);
                    if (remote.ETag is { Length: > 0 } tag)
                        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(tag));
                    else if (remote.LastModified is { Length: > 0 } date &&
                             DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
                                 DateTimeStyles.AssumeUniversal, out var parsed))
                        request.Headers.IfRange = new RangeConditionHeaderValue(parsed);
                }
                using var response = await _http.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < 4 && HttpRetryPolicy.IsRetryable(response.StatusCode))
                    {
                        var wait = HttpRetryPolicy.Delay(attempt, response.Headers.RetryAfter);
                        await Task.Delay(wait, ct).ConfigureAwait(false);
                        continue;
                    }
                    throw new NonRetryableHttpException(response.StatusCode);
                }
                // The server must not silently change a strong validator mid-transfer.
                if (remote.ETag is { Length: > 0 } expectedETag &&
                    response.Headers.ETag is { IsWeak: false } returnedETag &&
                    !string.Equals(returnedETag.Tag, expectedETag, StringComparison.Ordinal))
                    throw new RangeNotHonoredException();
                if (seek && range.HasValue)
                {
                    if (response.StatusCode != HttpStatusCode.PartialContent ||
                        response.Content.Headers.ContentRange?.From != range.Value.Start + have ||
                        response.Content.Headers.ContentRange?.To != range.Value.End ||
                        (remote.Length.HasValue &&
                         response.Content.Headers.ContentRange?.Length != remote.Length))
                        throw new RangeNotHonoredException();
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentEncoding.Any(x =>
                        !x.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Server unexpectedly sent compressed data; cannot verify byte offsets.");

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destination = new FileStream(path, FileMode.Append,
                    FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
                byte[] buffer = new byte[BufferSize];
                long received = have;
                int read;
                while (true)
                {
                    int budget = await _limiter.ReserveAsync(buffer.Length, ct).ConfigureAwait(false);
                    read = await source.ReadAsync(buffer.AsMemory(0, budget), ct).ConfigureAwait(false);
                    if (read == 0) break;
                    ct.ThrowIfCancellationRequested();
                    if (range.HasValue && received + read > range.Value.Length)
                        throw new IOException("Server sent more bytes than the specified range.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    tracker.Add(read);
                }
                await destination.FlushAsync(ct).ConfigureAwait(false);
                if (range.HasValue && received != range.Value.Length)
                    throw new IOException($"Truncated range {index}: {received}/{range.Value.Length} bytes.");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (NonRetryableHttpException) { throw; }
            catch (RangeNotHonoredException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (attempt == 4) throw;
                await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private static void ApplySafeReferrer(HttpRequestMessage request, string? sourcePage)
    {
        string? safeOrigin = BrowserLinkPolicy.SafeReferrerOrigin(
            request.RequestUri!.AbsoluteUri, sourcePage);
        if (safeOrigin is not null) request.Headers.Referrer = new Uri(safeOrigin);
    }

    private static string PartPath(string directory, int index) =>
        Path.Combine(directory, $"seg-{index:D2}.part");

    private static void EraseParts(string directory)
    {
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "seg-*.part"))
                File.Delete(file);
    }

    private static void TryMarkInternetZone(string filename, string url)
    {
        // MOTW lets Windows and installed protection software recognize internet files.
        // This does NOT constitute an explicit malware scan.
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            string safeUrl = url.Replace("\r", "").Replace("\n", "");
            File.WriteAllText(filename + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={safeUrl}\r\n");
        }
        catch (IOException) { /* Not all destination file systems support alternate streams. */ }
        catch (UnauthorizedAccessException) { /* Download remains usable. */ }
    }

    public void Dispose() => _http.Dispose();
}
