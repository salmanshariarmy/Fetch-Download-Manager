using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Fetch.Core;

static class Assert
{
    public static void That(bool value, string message)
    {
        if (!value) throw new Exception("TEST FAILED: " + message);
    }
}

sealed class LocalRangeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly byte[] _data;
    private readonly Task _accepting;
    public string Url { get; }
    public string ETag { get; set; } = "\"v1\"";

    public LocalRangeServer(byte[] data)
    {
        _data = data;
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}/test.bin";
        _accepting = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) when (_stop.IsCancellationRequested) { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string line = await reader.ReadLineAsync() ?? "";
                string method = line.Split(' ').FirstOrDefault() ?? "";
                string? range = null;
                string? ifRange = null;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                        range = line[6..].Trim();
                    if (line.StartsWith("If-Range:", StringComparison.OrdinalIgnoreCase))
                        ifRange = line[9..].Trim();
                }
                int from = 0, to = _data.Length - 1;
                bool partial = range != null && (ifRange == null || ifRange == ETag);
                if (partial)
                {
                    string[] pieces = range!["bytes=".Length..].Split('-');
                    from = int.Parse(pieces[0]);
                    to = int.Parse(pieces[1]);
                }
                int size = to - from + 1;
                string response = (partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n") +
                    "Content-Type: application/octet-stream\r\n" +
                    "Accept-Ranges: bytes\r\n" +
                    $"ETag: {ETag}\r\n" +
                    $"Content-Length: {size}\r\n" +
                    (partial ? $"Content-Range: bytes {from}-{to}/{_data.Length}\r\n" : "") +
                    "Connection: close\r\n\r\n";
                byte[] headers = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(headers);
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                    await stream.WriteAsync(_data.AsMemory(from, size));
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // A cancelled client may close a local test connection mid-write.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        _stop.Dispose();
    }
}

static class Program
{
    private static int _passed;

    public static async Task Main()
    {
        Unit("Sanitize reserved filenames", () =>
        {
            Assert.That(FileNameSafety.Sanitize("CON.txt") == "_CON.txt", "CON reserved");
            Assert.That(FileNameSafety.Sanitize("COM1.tar.gz") == "_COM1.tar.gz", "reserved with multiple extensions");
            Assert.That(FileNameSafety.Sanitize("../evil.exe") == "evil.exe", "no traversal");
            Assert.That(FileNameSafety.Sanitize("C:\\tmp\\bad?.exe") == "bad_.exe", "safe chars");
            Assert.That(FileNameSafety.Sanitize(". ") == "download.bin", "empty name");
        });
        Unit("Range split uses all bytes exactly once", () =>
        {
            var plan = SegmentPlan.Split(1_000_001, 8);
            Assert.That(plan.Count == 8 && plan[0].Start == 0 && plan[^1].End == 1_000_000,
                "range boundaries");
            for (int i = 1; i < plan.Count; i++)
                Assert.That(plan[i - 1].End + 1 == plan[i].Start, "contiguous ranges");
            Assert.That(plan.Sum(x => x.Length) == 1_000_001, "total length");
        });
        Unit("Recommend connections", () =>
        {
            Assert.That(SegmentPlan.Recommend(8 * 1_048_576, true) == 8, "8 segments");
            Assert.That(SegmentPlan.Recommend(null, true) == 1, "unknown length");
            Assert.That(SegmentPlan.Recommend(999_999, false) == 1, "no range support");
        });

        string root = Path.Combine(Path.GetTempPath(), "FetchTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JobStore(Path.Combine(root, "data"));
            var persisted = new DownloadItem { Url = "https://example.com/file", Status = JobStatus.Downloading };
            store.Save(new[] { persisted });
            Assert.That(store.Load().Single().Status == JobStatus.Paused, "restore as paused");
            Console.WriteLine("PASS  Restore job state"); _passed++;

            var payload = new byte[3 * 1_048_576 + 733];
            new Random(42).NextBytes(payload);
            await using var server = new LocalRangeServer(payload);
            using var downloader = new TransferEngine(store);

            // Pretend Fetch was terminated with a partially written first segment.
            var job = new DownloadItem
            {
                Url = server.Url,
                FileName = "test.bin",
                SaveDirectory = Path.Combine(root, "downloads"),
                Status = JobStatus.Paused,
                TotalBytes = payload.Length,
                SegmentCount = SegmentPlan.Recommend(payload.Length, true),
                ETag = server.ETag
            };
            var partDir = Path.Combine(store.PartsFolder, job.Id.ToString("N"));
            Directory.CreateDirectory(partDir);
            await File.WriteAllBytesAsync(Path.Combine(partDir, "seg-00.part"), payload[..31_000]);
            var result = await downloader.DownloadAsync(job, null, null, CancellationToken.None);
            Assert.That(File.ReadAllBytes(result.SavedPath).SequenceEqual(payload),
                "resume + merge exact bytes");
            Assert.That(job.SegmentCount > 1, "segmentation enabled");
            Console.WriteLine("PASS  Local HTTP range transfer, resume, and byte-perfect merge"); _passed++;

            // Validator change must discard incompatible old partial bytes.
            server.ETag = "\"v2\"";
            var newJob = new DownloadItem
            {
                Url = server.Url,
                FileName = "again.bin",
                SaveDirectory = job.SaveDirectory,
                TotalBytes = payload.Length,
                SegmentCount = job.SegmentCount,
                ETag = "\"v1\""
            };
            partDir = Path.Combine(store.PartsFolder, newJob.Id.ToString("N"));
            Directory.CreateDirectory(partDir);
            await File.WriteAllBytesAsync(Path.Combine(partDir, "seg-00.part"),
                Enumerable.Repeat((byte)255, 31_000).ToArray());
            var changed = await downloader.DownloadAsync(newJob, null, null, CancellationToken.None);
            Assert.That(File.ReadAllBytes(changed.SavedPath).SequenceEqual(payload),
                "stale parts must be deleted");
            Console.WriteLine("PASS  ETag change restarts instead of corrupting file"); _passed++;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
        Console.WriteLine($"All {_passed} tests passed.");
    }

    private static void Unit(string name, Action test)
    {
        test();
        Console.WriteLine("PASS  " + name);
        _passed++;
    }
}
