using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WifiTraffic.Models;
using WifiTraffic.Services;

namespace WifiTraffic;

public partial class MainWindow : Window
{
    private readonly CaptureService _capture = new();
    private readonly DatabaseService _database = new();
    private readonly GatewayModeService _gatewayMode = new();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public ObservableCollection<TrafficRecord> LiveTraffic { get; } = new();
    public ObservableCollection<DomainRow> TopDomains { get; } = new();

    private bool IsWholeNetworkMode => ModeCombo?.SelectedIndex == 1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _capture.TrafficObserved += Capture_TrafficObserved;
        _capture.CaptureError += (_, message) =>
            Dispatcher.Invoke(() => StatusText.Text = $"Capture warning: {message}");

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDashboardAsync();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _capture.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();

            LoadAdapters(preferGateway: false);

            foreach (var item in await _database.GetRecentAsync(250))
                LiveTraffic.Add(item);

            await RefreshDashboardAsync();
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WiFi Traffic could not start.\n\n{ex.Message}",
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadAdapters(bool preferGateway)
    {
        var adapters = _capture.GetAdapters();
        AdapterCombo.ItemsSource = adapters;

        if (adapters.Count == 0)
        {
            AdapterCombo.SelectedItem = null;
            StatusText.Text = "No capture adapters found. Install Npcap and restart.";
            return;
        }

        CaptureAdapter? preferred;

        if (preferGateway)
        {
            preferred = adapters.FirstOrDefault(x => x.IsGatewayCandidate);
            preferred ??= adapters.FirstOrDefault();
        }
        else
        {
            preferred = adapters.FirstOrDefault(x => !x.IsGatewayCandidate);
            preferred ??= adapters.FirstOrDefault();
        }

        AdapterCombo.SelectedItem = preferred;

        if (preferGateway)
            UpdateGatewayStatus(adapters);
    }

    private void UpdateGatewayStatus(IReadOnlyList<CaptureAdapter>? adapters = null)
    {
        adapters ??= _capture.GetAdapters();

        var marked = adapters.Count(x => x.IsGatewayCandidate);
        var interfaces = _gatewayMode.GetGatewayInterfaceSummary();

        if (marked > 0)
        {
            GatewayStatusText.Text =
                $"Found {marked} likely hotspot/gateway capture adapter(s). Select the one marked ★ WHOLE NETWORK.";
            return;
        }

        if (interfaces.Count > 0)
        {
            GatewayStatusText.Text =
                "Windows hotspot interface exists, but Npcap did not match it automatically. " +
                "Try the virtual/Wi-Fi Direct adapter in the list.";
            return;
        }

        GatewayStatusText.Text =
            "No active Windows hotspot adapter found yet. Turn on Mobile Hotspot, connect at least one device, then click Refresh adapters.";
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GatewayPanel is null || SubtitleText is null || AdapterCombo is null)
            return;

        if (IsWholeNetworkMode)
        {
            GatewayPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "Whole Network mode • devices connected through this PC";
            LoadAdapters(preferGateway: true);
        }
        else
        {
            GatewayPanel.Visibility = Visibility.Collapsed;
            SubtitleText.Text = "This PC mode • monitors traffic visible to this computer";
            LoadAdapters(preferGateway: false);
        }
    }

    private void OpenHotspotButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _gatewayMode.OpenMobileHotspotSettings();
            GatewayStatusText.Text =
                "Windows Mobile Hotspot settings opened. Turn it on, connect the other devices to the hotspot, then return here and click Refresh adapters.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open Windows Mobile Hotspot settings.\n\n{ex.Message}",
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAdapters(preferGateway: IsWholeNetworkMode);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterCombo.SelectedItem is not CaptureAdapter adapter)
        {
            MessageBox.Show("Select a network adapter first.");
            return;
        }

        if (IsWholeNetworkMode && !adapter.IsGatewayCandidate)
        {
            var choice = MessageBox.Show(
                "This adapter is not automatically recognized as a Windows hotspot/gateway adapter.\n\n" +
                "For other devices to appear, they must be connected through this PC. " +
                "If you know this is the correct virtual/shared adapter, you can continue.\n\n" +
                "Start capture on this adapter anyway?",
                "Whole Network adapter check",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
                return;
        }

        try
        {
            _capture.Start(adapter.Id);

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            AdapterCombo.IsEnabled = false;
            ModeCombo.IsEnabled = false;

            StatusText.Text = IsWholeNetworkMode
                ? $"Whole Network capture running on {adapter.Description}"
                : $"Capturing this PC on {adapter.Description}";

            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(66, 211, 146));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Capture could not be started. Make sure Npcap is installed and run WiFi Traffic as Administrator.\n\n" + ex.Message,
                "Capture error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _capture.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        AdapterCombo.IsEnabled = true;
        ModeCombo.IsEnabled = true;
        StatusText.Text = "Stopped";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(97, 112, 131));
    }

    private void Capture_TrafficObserved(object? sender, TrafficRecord record)
    {
        _database.Enqueue(record);

        Dispatcher.BeginInvoke(() =>
        {
            LiveTraffic.Insert(0, record);
            while (LiveTraffic.Count > 500)
                LiveTraffic.RemoveAt(LiveTraffic.Count - 1);
        });
    }

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var stats = await _database.GetStatsAsync();
            PacketCountText.Text = stats.PacketCount.ToString("N0");
            TrafficBytesText.Text = FormatBytes(stats.TotalBytes);
            DomainCountText.Text = stats.UniqueDomains.ToString("N0");
            SourceCountText.Text = stats.UniqueSources.ToString("N0");

            var domains = await _database.GetTopDomainsAsync(100);
            TopDomains.Clear();

            foreach (var row in domains)
                TopDomains.Add(new DomainRow(row.Domain, row.Hits, FormatBytes(row.Bytes)));
        }
        catch
        {
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshDashboardAsync();

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Delete all stored traffic history?",
                "Clear history",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await _database.ClearAsync();
        LiveTraffic.Clear();
        TopDomains.Clear();
        await RefreshDashboardAsync();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    public sealed record DomainRow(string Domain, long Hits, string Bytes);
}
