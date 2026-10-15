using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
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
    private DownloadFilter? _selectedFilter;
    private string _message = "Ready";
    private bool _disposing;

    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = new();
    public ObservableCollection<DownloadFilter> Filters { get; } = new()
    {
        new("All downloads", "all"), new("Downloading", "active"),
        new("Unfinished", "unfinished"), new("Finished", "done"),
        new("Video", "Video"), new("Music", "Music"),
        new("Compressed", "Compressed"), new("Programs", "Programs"),
        new("Documents", "Documents"), new("Images", "Images"), new("Other", "Other")
    };
    public ICollectionView JobsView { get; }
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

    public DownloadFilter? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            _selectedFilter = value;
            Changed();
            JobsView.Refresh();
            Changed(nameof(FilterTitle));
        }
    }
    public string FilterTitle => SelectedFilter?.Label ?? "All downloads";

    public DownloadJobViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            _selectedJob = value;
            Changed();
            Changed(nameof(SelectedError));
            Changed(nameof(SelectedUrl));
            Changed(nameof(SelectedPath));
        }
    }

    public string SelectedError => SelectedJob is { Error.Length: > 0 } vm ? vm.Error : _message;
    public string SelectedUrl => SelectedJob?.Url ?? "Select a download to see its details";
    public string SelectedPath => SelectedJob?.Location ?? DownloadFolder;
    public string JobCountText => $"{Jobs.Count} download{(Jobs.Count == 1 ? "" : "s")}";
    public string SummaryText => $"{Jobs.Count(j => j.Item.Status == JobStatus.Downloading)} active  •  " +
                                 $"{Jobs.Count(j => j.Item.Status == JobStatus.Completed)} completed  •  " +
                                 $"{Jobs.Count(j => j.Item.Status != JobStatus.Completed)} unfinished";

    public ICommand AddCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand ResumeAllCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand PasteUrlCommand { get; }
    public ICommand AboutCommand { get; }

    public MainViewModel()
    {
        _downloadFolder = _store.LoadDownloadDirectory();
        _engine = new TransferEngine(_store);
        JobsView = CollectionViewSource.GetDefaultView(Jobs);
        JobsView.Filter = MatchesFilter;
        Jobs.CollectionChanged += OnJobsChanged;
        AddCommand = new RelayCommand(Add);
        PauseCommand = new RelayCommand(Pause);
        ResumeCommand = new RelayCommand(() => Start(SelectedJob));
        RemoveCommand = new RelayCommand(Remove);
        PauseAllCommand = new RelayCommand(PauseAll);
        ResumeAllCommand = new RelayCommand(ResumeAll);
        ClearCompletedCommand = new RelayCommand(ClearCompleted);
        BrowseCommand = new RelayCommand(Browse);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenFileCommand = new RelayCommand(OpenFile);
        CopyUrlCommand = new RelayCommand(CopyUrl);
        PasteUrlCommand = new RelayCommand(PasteUrl);
        AboutCommand = new RelayCommand(About);
        try
        {
            foreach (var item in _store.Load()) Jobs.Add(new DownloadJobViewModel(item));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _message = "Saved job history could not be opened. Original jobs.json was not deleted.";
        }
        SelectedFilter = Filters[0];
    }

    private bool MatchesFilter(object candidate)
    {
        if (candidate is not DownloadJobViewModel vm) return false;
        return SelectedFilter?.Key switch
        {
            null or "all" => true,
            "active" => vm.Item.Status == JobStatus.Downloading || vm.Item.Status == JobStatus.Queued,
            "unfinished" => vm.Item.Status != JobStatus.Completed,
            "done" => vm.Item.Status == JobStatus.Completed,
            string category => string.Equals(vm.Category, category, StringComparison.Ordinal)
        };
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshSummary();

    private void RefreshSummary()
    {
        if (_disposing) return;
        Changed(nameof(JobCountText));
        Changed(nameof(SummaryText));
        JobsView.Refresh();
    }

    private void Add()
    {
        try
        {
            string url = UrlInput.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ShowMessage("Enter a direct http:// or https:// URL.");
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
            ShowMessage("Added: " + vm.Name);
            Persist();
            Start(vm);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            ShowMessage("Cannot add download: " + ex.Message);
        }
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
        RefreshSummary();
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
            RefreshSummary();
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
                    JobsView.Refresh();
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
                RefreshSummary();
                Persist();
            }
        }
    }

    private void Pause()
    {
        if (SelectedJob is { } vm && _active.TryGetValue(vm.Item.Id, out var cancellation))
            cancellation.Cancel();
    }

    private void PauseAll()
    {
        foreach (var source in _active.Values) source.Cancel();
        ShowMessage("Pausing active and queued downloads…");
    }

    private void ResumeAll()
    {
        foreach (var job in Jobs.ToArray())
            if (job.Item.Status != JobStatus.Completed) Start(job);
        ShowMessage("Resuming unfinished downloads.");
    }

    private void Remove()
    {
        if (SelectedJob is not { } vm) return;
        if (_active.TryGetValue(vm.Item.Id, out var cancellation)) cancellation.Cancel();
        Jobs.Remove(vm);
        SelectedJob = null;
        Persist();
        // Parts are deliberately retained for recovery; this only removes the queue entry.
    }

    private void ClearCompleted()
    {
        foreach (var vm in Jobs.Where(x => x.Item.Status == JobStatus.Completed).ToArray())
            Jobs.Remove(vm);
        if (SelectedJob?.Item.Status == JobStatus.Completed) SelectedJob = null;
        Persist();
        ShowMessage("Removed completed entries from history; downloaded files were kept.");
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
        Changed(nameof(SelectedPath));
    }

    private void OpenFolder()
    {
        var folder = SelectedJob?.Item.SaveDirectory ?? DownloadFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message);
        }
    }

    private void OpenFile()
    {
        if (SelectedJob is not { Item.Status: JobStatus.Completed } vm)
        {
            ShowMessage("Select a completed download to open it.");
            return;
        }
        try
        {
            if (!File.Exists(vm.Location)) { ShowMessage("Downloaded file was moved or deleted."); return; }
            Process.Start(new ProcessStartInfo(vm.Location) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message);
        }
    }

    private void CopyUrl()
    {
        if (SelectedJob == null) return;
        try { System.Windows.Clipboard.SetText(SelectedJob.Url); ShowMessage("Download URL copied."); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowMessage("Clipboard is unavailable."); }
    }

    private void PasteUrl()
    {
        try { if (System.Windows.Clipboard.ContainsText()) UrlInput = System.Windows.Clipboard.GetText().Trim(); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowMessage("Clipboard is unavailable."); }
    }

    private static void About() => System.Windows.MessageBox.Show(
        "Fetch Download Manager 0.2 — Community Edition\n\n" +
        "Direct HTTP(S) downloads, multi-part transfer, resume, categories and queue controls.\n\n" +
        "Browser capture, site grabber, streaming-media extraction and advanced scheduler are not included.\n" +
        "Fetch is independently developed and is not affiliated with Internet Download Manager.",
        "About Fetch", MessageBoxButton.OK, MessageBoxImage.Information);

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
        try { _store.Save(Jobs.Select(j => j.Item)); } catch (IOException) { }
    }
}
