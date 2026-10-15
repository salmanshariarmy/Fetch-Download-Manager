using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Fetch.Core;

namespace Fetch.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly JobStore _store = new();
    private readonly TransferEngine _engine;
    private readonly MediaDownloader _media = new();
    private string _mediaQuality = "Best";
    private readonly DownloadPreferences _preferences;
    private readonly SemaphoreSlim _slots;
    private readonly DispatcherTimer _scheduleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private string _connectionsText = "8";
    private string _concurrentText = "3";
    private string _rateLimitText = "0";
    private string _sha256Text = "";
    private string _scheduleText = "";
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
        new("Scheduled", "scheduled"),
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

    public string ConnectionsText { get => _connectionsText; set { _connectionsText = value; Changed(); } }
    public string ConcurrentText { get => _concurrentText; set { _concurrentText = value; Changed(); } }
    public string RateLimitText { get => _rateLimitText; set { _rateLimitText = value; Changed(); } }
    public string ExpectedSha256Text { get => _sha256Text; set { _sha256Text = value; Changed(); } }
    public string ScheduleText { get => _scheduleText; set { _scheduleText = value; Changed(); } }
    public string[] MediaQualityChoices { get; } = new[] { "Best", "1080p", "720p", "480p", "Audio only" };
    public string MediaQuality
    {
        get => _mediaQuality;
        set { _mediaQuality = value; Changed(); }
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
            ScheduleText = value?.Item.ScheduledAt?.ToLocalTime().ToString(SchedulePolicy.Format) ?? "";
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
    public ICommand AddMediaCommand { get; }
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
    public ICommand ApplyPreferencesCommand { get; }
    public ICommand ScheduleCommand { get; }
    public ICommand ClearScheduleCommand { get; }

    public MainViewModel()
    {
        _downloadFolder = _store.LoadDownloadDirectory();
        _preferences = _store.LoadPreferences();
        _connectionsText = _preferences.DesiredConnections.ToString(CultureInfo.InvariantCulture);
        _concurrentText = _preferences.MaxConcurrentJobs.ToString(CultureInfo.InvariantCulture);
        _rateLimitText = _preferences.RateLimitKiB.ToString(CultureInfo.InvariantCulture);
        _slots = new SemaphoreSlim(_preferences.MaxConcurrentJobs, _preferences.MaxConcurrentJobs);
        _engine = new TransferEngine(_store, _preferences);
        JobsView = CollectionViewSource.GetDefaultView(Jobs);
        JobsView.Filter = MatchesFilter;
        Jobs.CollectionChanged += OnJobsChanged;
        AddCommand = new RelayCommand(Add);
        AddMediaCommand = new RelayCommand(AddMedia);
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
        ApplyPreferencesCommand = new RelayCommand(ApplyPreferences);
        ScheduleCommand = new RelayCommand(ScheduleSelected);
        ClearScheduleCommand = new RelayCommand(ClearSchedule);
        try
        {
            foreach (var item in _store.Load()) Jobs.Add(new DownloadJobViewModel(item));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _message = "Saved job history could not be opened. Original jobs.json was not deleted.";
        }
        SelectedFilter = Filters[0];
        _scheduleTimer.Tick += (_, _) => StartDueScheduled();
        _scheduleTimer.Start();
        StartDueScheduled();
    }

    private bool MatchesFilter(object candidate)
    {
        if (candidate is not DownloadJobViewModel vm) return false;
        return SelectedFilter?.Key switch
        {
            null or "all" => true,
            "active" => vm.Item.Status == JobStatus.Downloading || vm.Item.Status == JobStatus.Queued,
            "scheduled" => vm.Item.Status == JobStatus.Scheduled,
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

    private void Add() => TryAddUrl(UrlInput, true, out _);
    private void AddMedia() => TryAddUrl(UrlInput, true, out _, forceMedia: true);

    // Executed on the WPF dispatcher by BrowserPipeServer.
    // Returns success only when the job has been queued in Fetch.
    public bool QueueBrowserUrl(string url, out string error) => TryAddUrl(url, false, out error);

    private bool TryAddUrl(string input, bool clearInput, out string error, bool forceMedia = false)
    {
        error = "";
        try
        {
            string url = input.Trim();
            if (url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Enter a direct HTTP(S) URL without embedded credentials (maximum 4096 characters).";
                ShowMessage(error);
                return false;
            }
            bool isMedia = forceMedia || MediaDownloader.IsKnownMediaUrl(url);
            string checksum = ExpectedSha256Text.Trim();
            if (!HashVerifier.IsValidSha256(checksum))
            {
                error = "SHA-256 must contain 64 hexadecimal characters, or be empty.";
                ShowMessage(error);
                return false;
            }
            if (isMedia && checksum.Length > 0)
            {
                error = "SHA-256 verification is currently for direct file downloads only.";
                ShowMessage(error);
                return false;
            }
            string fileName = isMedia ? "video.mp4" : FileNameSafety.FromUrl(url);
            string chosen = FileNameSafety.FreePath(DownloadFolder, fileName, path =>
                File.Exists(path) || Jobs.Any(x =>
                    string.Equals(Path.Combine(x.Item.SaveDirectory, x.Item.FileName),
                        path, StringComparison.OrdinalIgnoreCase)));
            var item = new DownloadItem
            {
                Url = url,
                FileName = Path.GetFileName(chosen),
                SaveDirectory = DownloadFolder,
                Status = JobStatus.Queued,
                IsMedia = isMedia,
                MediaQuality = isMedia ? MediaQuality : "Best",
                RequestedConnections = _preferences.DesiredConnections,
                ExpectedSha256 = checksum.Length > 0 ? checksum.ToUpperInvariant() : null
            };
            var vm = new DownloadJobViewModel(item);
            Jobs.Add(vm);
            SelectedJob = vm;
            if (clearInput) { UrlInput = ""; ExpectedSha256Text = ""; }
            ShowMessage("Added: " + vm.Name);
            Persist();
            Start(vm);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            error = "Cannot add download: " + ex.Message;
            ShowMessage(error);
            return false;
        }
    }

    private void Start(DownloadJobViewModel? target)
    {
        if (target == null || _disposing || target.Item.Status == JobStatus.Completed ||
            _active.ContainsKey(target.Item.Id)) return;
        var cancellation = new CancellationTokenSource();
        if (!_active.TryAdd(target.Item.Id, cancellation)) { cancellation.Dispose(); return; }
        target.Item.ScheduledAt = null; // Manual resume overrides a future schedule.
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
            var result = vm.Item.IsMedia
                ? await _media.DownloadAsync(vm.Item, progress, cancellation.Token)
                : await _engine.DownloadAsync(vm.Item, progress, MetadataReady, cancellation.Token);
            if (!_disposing)
            {
                vm.Item.FileName = Path.GetFileName(result.SavedPath);
                vm.Item.DownloadedBytes = result.Bytes;
                vm.Item.Status = JobStatus.Completed;
                vm.Item.ScheduledAt = null;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_disposing && vm.Item.Status != JobStatus.Scheduled)
                vm.Item.Status = JobStatus.Paused;
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

    private void ApplyPreferences()
    {
        if (!int.TryParse(ConnectionsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int connections) ||
            !int.TryParse(ConcurrentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int concurrent) ||
            !int.TryParse(RateLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate))
        {
            ShowMessage("Enter whole numbers in download options.");
            return;
        }
        var proposed = new DownloadPreferences
        {
            DesiredConnections = connections,
            MaxConcurrentJobs = concurrent,
            RateLimitKiB = rate
        };
        try
        {
            proposed.Validate();
            _store.SavePreferences(proposed);
            _preferences.DesiredConnections = connections;
            _preferences.MaxConcurrentJobs = concurrent;
            _preferences.RateLimitKiB = rate;
            _engine.SetSpeedLimitKiB(rate);
            ShowMessage("Settings saved. Speed limit applies immediately; concurrency applies after restart. " +
                "Connection count applies to newly added direct downloads.");
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException or UnauthorizedAccessException)
        {
            ShowMessage("Could not save options: " + ex.Message);
        }
    }

    private void ScheduleSelected()
    {
        if (SelectedJob is not { } job || job.Item.Status == JobStatus.Completed)
        {
            ShowMessage("Select an unfinished download to schedule.");
            return;
        }
        if (!SchedulePolicy.TryParseLocal(ScheduleText, out var when) || when <= DateTimeOffset.Now)
        {
            ShowMessage("Enter a future local start time in yyyy-MM-dd HH:mm format.");
            return;
        }
        // Cancel an in-flight transfer; the cancellation handler retains Scheduled status.
        job.Item.ScheduledAt = when;
        job.Item.Status = JobStatus.Scheduled;
        if (_active.TryGetValue(job.Item.Id, out var source)) source.Cancel();
        job.Refresh();
        RefreshSummary();
        Persist();
        ShowMessage("Scheduled for " + when.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ". Keep Fetch open to start on time.");
    }

    private void ClearSchedule()
    {
        if (SelectedJob is not { } job) return;
        job.Item.ScheduledAt = null;
        if (job.Item.Status == JobStatus.Scheduled) job.Item.Status = JobStatus.Paused;
        job.Refresh();
        ScheduleText = "";
        RefreshSummary();
        Persist();
    }

    private void StartDueScheduled()
    {
        if (_disposing) return;
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (var job in Jobs.ToArray())
            if (job.Item.Status == JobStatus.Scheduled &&
                SchedulePolicy.IsDue(job.Item.ScheduledAt, now) &&
                !_active.ContainsKey(job.Item.Id)) Start(job);
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
        "Fetch Download Manager 0.6 — Community Edition\n\n" +
        "Direct HTTP(S) downloads, multi-part transfer, resume, categories and queue controls.\n\n" +
        "Chrome/Edge extension: manual link forwarding, opt-in capture and direct video buttons.\n" +
        "Media downloads require user-installed yt-dlp and optional FFmpeg.\n" +
        "Scheduled starts, configurable connections, speed limit and optional SHA-256 are available.\n" +
        "A full site grabber, FTP and complete browser interception are not included.\n" +
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
        _scheduleTimer.Stop();
        foreach (var source in _active.Values) source.Cancel();
        try { _store.Save(Jobs.Select(j => j.Item)); } catch (IOException) { }
    }
}
