using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace Fetch.Desktop.Services;

public delegate bool QueueUrlHandler(string url, out string error);

/// <summary>Current-user-only named-pipe bridge for the browser native host.
/// No open HTTP port, no cookies, and no privileged service.</summary>
public sealed class BrowserPipeServer : IDisposable
{
    public const string PipeName = "FetchDownloadManagerBrowser_v1";
    private readonly Dispatcher _dispatcher;
    private readonly QueueUrlHandler _enqueue;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;

    public BrowserPipeServer(Dispatcher dispatcher, QueueUrlHandler enqueue)
    {
        _dispatcher = dispatcher;
        _enqueue = enqueue;
    }

    public void Start() => _listener ??= Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false),
                    detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false),
                    bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
                // A single JSON line per connection, max 8 KiB.
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var line = await reader.ReadLineAsync(requestTimeout.Token);
                bool ok = false;
                string error = "Invalid browser request.";
                try
                {
                    if (line is { Length: <= 8192 } && line.Length > 0)
                    {
                        using var doc = JsonDocument.Parse(line);
                        var data = doc.RootElement;
                        if (data.TryGetProperty("command", out var command) &&
                            command.GetString() == "add" &&
                            data.TryGetProperty("url", out var urlProperty) &&
                            urlProperty.ValueKind == JsonValueKind.String)
                        {
                            var url = urlProperty.GetString() ?? "";
                            if (url.Length <= 4096)
                            {
                                // ViewModel and ObservableCollection are always accessed on the UI thread.
                                (ok, error) = await _dispatcher.InvokeAsync(() =>
                                {
                                    var queued = _enqueue(url, out var reason);
                                    return (queued, reason);
                                });
                            }
                            else error = "URL too long.";
                        }
                    }
                }
                catch (JsonException) { error = "Invalid JSON request."; }
                var response = JsonSerializer.Serialize(new { ok, error });
                await writer.WriteLineAsync(response);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or
                UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
            {
                if (_shutdown.IsCancellationRequested) break;
                // A broken client must never crash the WPF desktop application.
                await Task.Delay(200);
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
