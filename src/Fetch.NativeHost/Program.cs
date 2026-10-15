using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

// Chromium Native Messaging wire format: 4-byte native-endian length + UTF-8 JSON.
// Only reads URLs supplied by the browser extension and sends them to the
// current user's Fetch instance over a restricted named pipe.
const string pipeName = "FetchDownloadManagerBrowser_v1";
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
var prefix = new byte[4];
try
{
    await input.ReadExactlyAsync(prefix);
    int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
    if (length < 2 || length > 8192)
    {
        await ReplyAsync(false, "Invalid request size.");
        return;
    }
    var bytes = new byte[length];
    await input.ReadExactlyAsync(bytes);
    using var doc = JsonDocument.Parse(bytes);
    var root = doc.RootElement;
    if (!root.TryGetProperty("type", out var type) || type.GetString() != "download" ||
        !root.TryGetProperty("url", out var field) || field.ValueKind != JsonValueKind.String)
    {
        await ReplyAsync(false, "Unsupported request.");
        return;
    }
    var url = field.GetString() ?? "";
    if (url.Length < 8 || url.Length > 4096 ||
        !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
        uri.Scheme is not ("http" or "https") ||
        !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host))
    {
        await ReplyAsync(false, "Only direct HTTP(S) URLs without credentials are supported.");
        return;
    }
    try
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "add", url }));
        using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var line = await reader.ReadLineAsync(readTimeout.Token);
        if (line is null) throw new IOException("Fetch did not reply.");
        using var ack = JsonDocument.Parse(line);
        bool ok = ack.RootElement.GetProperty("ok").GetBoolean();
        string reason = ack.RootElement.TryGetProperty("error", out var e) ?
            e.GetString() ?? "" : "";
        await ReplyAsync(ok, ok ? "Link queued in Fetch." : reason);
    }
    catch (Exception ex) when (ex is TimeoutException or IOException or
        OperationCanceledException or UnauthorizedAccessException)
    {
        // Fetch isn't running. Launch the actual installed executable with
        // an ArgumentList (never a shell command) to avoid argument injection.
        string exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Fetch.Desktop.exe"));
        if (!File.Exists(exe))
        {
            await ReplyAsync(false, "Fetch.Desktop.exe was not found. Reinstall Fetch.");
            return;
        }
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add("--fetch-add-url");
            start.ArgumentList.Add(url);
            using var process = Process.Start(start);
            // Launching the UI is not proof of a queued download. Return ok=false
            // so optional automatic interception does NOT cancel Chrome's copy.
            await ReplyAsync(false,
                process is null ? "Could not start Fetch." :
                "Fetch started with the link. Confirm it appears in the app; browser download was not canceled.");
        }
        catch (Exception launchError) when (launchError is IOException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            await ReplyAsync(false, "Could not launch Fetch: " + launchError.Message);
        }
    }
}
catch (Exception ex) when (ex is JsonException or EndOfStreamException or IOException or
    ArgumentException or InvalidOperationException)
{
    await ReplyAsync(false, "Invalid browser message: " + ex.Message);
}

async Task ReplyAsync(bool ok, string message)
{
    var json = JsonSerializer.SerializeToUtf8Bytes(new { ok, message });
    var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, json.Length);
    await output.WriteAsync(header);
    await output.WriteAsync(json);
    await output.FlushAsync();
}
