using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Fetch.Core;

namespace Fetch.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly JobStore _store = new();
    private readonly TransferEngine _engine;
    private readonly SemaphoreSlim _slots = new(3, 3);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private string _urlInput = "";
    private string _downloadFolder;
    private DownloadJobViewModel? _selectedJob;
    private string _message = "";
    private bool _disposing;

    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    public string UrlInput
    {
        get => _urlInput;
        set { _urlInput = value; Changed(); }
    }

    public string DownloadFolder
    {
        get => _downloadFolder;
        set { _downloadFolder = value; Changed(); }
    }

    public DownloadJobViewModel? SelectedJob
    {
        get => _selectedJob;
        set { _selectedJob = value; Changed(); Changed(nameof(SelectedError)); }
    }

    public string SelectedError => SelectedJob is { Error.Length: > 0 } vm ? vm.Error : _message;
    public string JobCountText => $"{Jobs.Count} download{(Jobs.Count == 1 ? "" : "s")}";

    public ICommand AddCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public MainViewModel()
    {
        _downloadFolder = _store.LoadDownloadDirectory();
        _engine = new TransferEngine(_store);
        AddCommand = new RelayCommand(Add);
        PauseCommand = new RelayCommand(Pause);
        ResumeCommand = new RelayCommand(Resume);
        RemoveCommand = new RelayCommand(Remove);
        BrowseCommand = new RelayCommand(Browse);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        try
        {
            foreach (var item in _store.Load()) Jobs.Add(new DownloadJobViewModel(item));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _message = "Saved job history could not be opened. Original jobs.json was not deleted.";
        }
    }

    private void Add()
    {
        string url = UrlInput.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowMessage("Enter a valid http:// or https:// URL.");
            return;
        }
        string fileName = FileNameSafety.FromUrl(url);
        string chosen = FileNameSafety.FreePath(DownloadFolder, fileName, path =>
            File.Exists(path) || Jobs.Any(x =>
                string.Equals(Path.Combine(x.Item.SaveDirectory, x.Item.FileName),
                    path, StringComparison.OrdinalIgnoreCase)));
        var item = new DownloadItem
        {
            Url = url,
            FileName = Path.GetFileName(chosen),
            SaveDirectory = DownloadFolder,
            Status = JobStatus.Queued
        };
        var vm = new DownloadJobViewModel(item);
        Jobs.Add(vm);
        SelectedJob = vm;
        UrlInput = "";
        Changed(nameof(JobCountText));
        Persist();
        Start(vm);
    }

    private void Start(DownloadJobViewModel? target)
    {
        if (target == null || _disposing || target.Item.Status == JobStatus.Completed ||
            _active.ContainsKey(target.Item.Id)) return;
        var cancellation = new CancellationTokenSource();
        if (!_active.TryAdd(target.Item.Id, cancellation)) { cancellation.Dispose(); return; }
        target.Item.Status = JobStatus.Queued;
        target.Item.Error = null;
        target.Refresh();
        Persist();
        _ = RunAsync(target, cancellation);
    }

    private async Task RunAsync(DownloadJobViewModel vm, CancellationTokenSource cancellation)
    {
        bool entered = false;
        try
        {
            await _slots.WaitAsync(cancellation.Token);
            entered = true;
            if (_disposing) return;
            vm.Item.Status = JobStatus.Downloading;
            vm.Refresh();
            Persist();
            var progress = new Progress<TransferProgress>(p =>
            {
                if (!_disposing && Jobs.Contains(vm)) vm.Update(p);
            });
            async Task MetadataReady()
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposing) return;
                    vm.Refresh();
                    Persist();
                });
            }
            var result = await _engine.DownloadAsync(vm.Item, progress,
                MetadataReady, cancellation.Token);
            if (!_disposing)
            {
                vm.Item.FileName = Path.GetFileName(result.SavedPath);
                vm.Item.DownloadedBytes = result.Bytes;
                vm.Item.Status = JobStatus.Completed;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_disposing) vm.Item.Status = JobStatus.Paused;
        }
        catch (Exception ex)
        {
            if (!_disposing)
            {
                vm.Item.Status = JobStatus.Failed;
                vm.Item.Error = ex.Message;
            }
        }
        finally
        {
            _active.TryRemove(vm.Item.Id, out _);
            if (entered) _slots.Release();
            cancellation.Dispose();
            if (!_disposing)
            {
                vm.Refresh();
                Changed(nameof(SelectedError));
                Persist();
            }
        }
    }

    private void Pause()
    {
        if (SelectedJob is { } vm &&
            _active.TryGetValue(vm.Item.Id, out var cancellation)) cancellation.Cancel();
    }

    private void Resume() => Start(SelectedJob);

    private void Remove()
    {
        if (SelectedJob is not { } vm) return;
        if (_active.TryGetValue(vm.Item.Id, out var cancellation)) cancellation.Cancel();
        Jobs.Remove(vm);
        SelectedJob = null;
        Changed(nameof(JobCountText));
        Persist();
        // Partial data remains available for recovery; remove deletes only the queue entry.
    }

    private void Browse()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where Fetch saves files",
            UseDescriptionForTitle = true,
            SelectedPath = DownloadFolder,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        DownloadFolder = dialog.SelectedPath;
        _store.SaveDownloadDirectory(DownloadFolder);
    }

    private void OpenFolder()
    {
        var folder = SelectedJob?.Item.SaveDirectory ?? DownloadFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            ShowMessage(ex.Message);
        }
    }

    private void ShowMessage(string message)
    {
        _message = message;
        Changed(nameof(SelectedError));
    }

    private void Persist()
    {
        try { _store.Save(Jobs.Select(j => j.Item)); }
        catch (IOException ex) { ShowMessage("Could not save download history: " + ex.Message); }
    }

    private void Changed([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void Dispose()
    {
        _disposing = true;
        foreach (var source in _active.Values) source.Cancel();
        // Deliberately don't dispose the engine while active network tasks unwind.
        try { _store.Save(Jobs.Select(j => j.Item)); } catch (IOException) { }
    }
}
