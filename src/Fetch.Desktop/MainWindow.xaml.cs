using System.Windows;
using Fetch.Desktop.Services;
using Fetch.Desktop.ViewModels;

namespace Fetch.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly BrowserPipeServer _browserBridge;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _browserBridge = new BrowserPipeServer(Dispatcher, _vm.QueueBrowserUrl);
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _browserBridge.Dispose();
            _vm.Dispose();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Invoked only from our own native host when Fetch was not running.
        // Direct manual use of --fetch-add-url still validates URL in the view model.
        var args = Environment.GetCommandLineArgs();
        string? requestedUrl = null, sourcePage = null;
        bool media = false;
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] is "--fetch-add-url" or "--fetch-media-url")
            {
                media = args[i] == "--fetch-media-url";
                requestedUrl = args[++i];
            }
            else if (args[i] == "--fetch-source-page") sourcePage = args[++i];
        }
        if (requestedUrl is not null)
            _vm.QueueBrowserUrl(requestedUrl, sourcePage, media, out _);
        _browserBridge.Start();
    }
}
