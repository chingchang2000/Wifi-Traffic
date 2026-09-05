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
    private readonly DnsProxyService _dnsProxy = new();
    private readonly NetworkSetupService _networkSetup = new();
    private readonly LanDiscoveryService _lanDiscovery = new();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public ObservableCollection<TrafficRecord> LiveTraffic { get; } = new();
    public ObservableCollection<DomainRow> TopDomains { get; } = new();
    public ObservableCollection<LanDevice> Devices { get; } = new();

    private bool IsRouterDnsMode => ModeCombo?.SelectedIndex == 1;
    private bool IsNoSetupMode => ModeCombo?.SelectedIndex == 2;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _capture.TrafficObserved += TrafficObserved;
        _capture.CaptureError += (_, message) =>
            Dispatcher.Invoke(() => SetStatus($"Capture warning: {message}", warning: true));

        _dnsProxy.DomainObserved += TrafficObserved;
        _dnsProxy.StatusChanged += (_, message) =>
            Dispatcher.Invoke(() => RouterDnsStatusText.Text = message);

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDashboardAsync();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _capture.Dispose();
            _dnsProxy.Dispose();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            LoadAdapters();
            UpdateRouterDnsInfo();

            foreach (var item in await _database.GetRecentAsync(250))
                LiveTraffic.Add(item);

            await RefreshDashboardAsync();
            _refreshTimer.Start();

            SetStatus("Ready");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WiFi Traffic could not start.\n\n{ex.Message}",
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            SetStatus("Startup error", warning: true);
        }
    }

    private void LoadAdapters()
    {
        var adapters = _capture.GetAdapters();
        AdapterCombo.ItemsSource = adapters;
        AdapterCountText.Text = $"{adapters.Count} FOUND";

        if (adapters.Count > 0)
        {
            var preferred =
                adapters.FirstOrDefault(x =>
                    x.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    x.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                ?? adapters.FirstOrDefault(x =>
                    x.Description.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
                ?? adapters.First();

            AdapterCombo.SelectedItem = preferred;
            AdapterCombo.ToolTip = preferred.DisplayName;
        }
        else
        {
            AdapterCombo.SelectedItem = null;
            AdapterCombo.ToolTip = "No adapters found";
            SetStatus("No adapters found — install Npcap", warning: true);
        }
    }

    private void UpdateRouterDnsInfo()
    {
        var info = _networkSetup.GetRouterDnsInfo();

        if (info is null)
        {
            RouterDnsIpText.Text = "Not detected";
            RouterGatewayText.Text = "Not detected";
            return;
        }

        RouterDnsIpText.Text = info.LanIp;
        RouterGatewayText.Text = $"{info.GatewayIp} • {info.InterfaceName}";
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RouterDnsPanel is null ||
            NoSetupPanel is null ||
            DefaultInfoPanel is null ||
            AdapterField is null ||
            StartButton is null)
            return;

        RouterDnsPanel.Visibility = Visibility.Collapsed;
        NoSetupPanel.Visibility = Visibility.Collapsed;
        DefaultInfoPanel.Visibility = Visibility.Collapsed;
        AdapterField.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;

        if (IsRouterDnsMode)
        {
            RouterDnsPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "Whole Network • domain visibility through Router DNS";
            StartButton.Content = "Start DNS sensor";
            UpdateRouterDnsInfo();
        }
        else if (IsNoSetupMode)
        {
            NoSetupPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "Zero setup • discover devices on your LAN";
            StartButton.Content = "Scan devices";
            StopButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            DefaultInfoPanel.Visibility = Visibility.Visible;
            AdapterField.Visibility = Visibility.Visible;
            SubtitleText.Text = "This PC mode • live network visibility";
            StartButton.Content = "Start capture";

            if (AdapterCombo.Items.Count == 0)
                LoadAdapters();
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsNoSetupMode)
            {
                StartButton.IsEnabled = false;
                ModeCombo.IsEnabled = false;
                NoSetupStatusText.Text = "Scanning local network...";
                SetStatus("Scanning devices...");

                var progress = new Progress<string>(message =>
                {
                    NoSetupStatusText.Text = message;
                    SetStatus(message);
                });

                var devices = await _lanDiscovery.ScanAsync(progress);

                Devices.Clear();
                foreach (var device in devices)
                    Devices.Add(device);

                NoSetupStatusText.Text = $"Found {Devices.Count} devices • no router login used";
                SetStatus($"Scan complete • {Devices.Count} devices");

                StartButton.IsEnabled = true;
                ModeCombo.IsEnabled = true;
                await RefreshDashboardAsync();
                return;
            }

            if (IsRouterDnsMode)
            {
                var info = _networkSetup.GetRouterDnsInfo();

                if (info is null)
                {
                    MessageBox.Show(
                        "Could not detect this PC's LAN IP or default router. Make sure the PC is connected to your normal network.",
                        "Router DNS Mode",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _networkSetup.EnsureDnsFirewallRules();
                await _dnsProxy.StartAsync();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                ModeCombo.IsEnabled = false;

                SetStatus($"DNS sensor • {info.LanIp}:53", active: true);
                RouterDnsStatusText.Text = $"Running • set router LAN/DHCP DNS to {info.LanIp}";
            }
            else
            {
                if (AdapterCombo.SelectedItem is not CaptureAdapter adapter)
                {
                    MessageBox.Show(
                        "Choose a network adapter first.",
                        "WiFi Traffic",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _capture.Start(adapter.Id);

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                AdapterCombo.IsEnabled = false;
                ModeCombo.IsEnabled = false;

                SetStatus($"Live • {ShortAdapterName(adapter)}", active: true);
            }
        }
        catch (Exception ex)
        {
            StartButton.IsEnabled = true;
            ModeCombo.IsEnabled = true;
            AdapterCombo.IsEnabled = true;

            var message = IsNoSetupMode
                ? "Device discovery failed.\n\n"
                : IsRouterDnsMode
                    ? "DNS sensor could not start. Port 53 may already be in use by another DNS program.\n\n"
                    : "Capture could not be started. Make sure Npcap is installed and run WiFi Traffic as Administrator.\n\n";

            MessageBox.Show(
                message + ex.Message,
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            SetStatus("Action failed", warning: true);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _capture.Stop();
        _dnsProxy.Stop();

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ModeCombo.IsEnabled = true;
        AdapterCombo.IsEnabled = true;
        RouterDnsStatusText.Text = "DNS sensor is stopped.";

        SetStatus("Ready");
    }

    private void CopyDnsIpButton_Click(object sender, RoutedEventArgs e)
    {
        var info = _networkSetup.GetRouterDnsInfo();

        if (info is null)
        {
            MessageBox.Show("Could not detect this PC's LAN IP.");
            return;
        }

        Clipboard.SetText(info.LanIp);
        RouterDnsStatusText.Text = $"Copied {info.LanIp} to clipboard";
    }

    private void OpenRouterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _networkSetup.OpenRouterAdminPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the router page.\n\n{ex.Message}",
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void TrafficObserved(object? sender, TrafficRecord record)
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
            SourceCountText.Text = Math.Max(stats.UniqueSources, Devices.Count).ToString("N0");

            var domains = await _database.GetTopDomainsAsync(100);
            TopDomains.Clear();

            foreach (var row in domains)
                TopDomains.Add(new DomainRow(row.Domain, row.Hits, FormatBytes(row.Bytes)));
        }
        catch
        {
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAdapters();
        UpdateRouterDnsInfo();
        await RefreshDashboardAsync();
        SetStatus("Dashboard refreshed");
    }

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
        SetStatus("History cleared");
    }

    private void SetStatus(string message, bool active = false, bool warning = false)
    {
        StatusText.Text = message;

        StatusDot.Fill = warning
            ? new SolidColorBrush(Color.FromRgb(255, 189, 90))
            : active
                ? new SolidColorBrush(Color.FromRgb(56, 217, 150))
                : new SolidColorBrush(Color.FromRgb(97, 112, 131));
    }

    private static string ShortAdapterName(CaptureAdapter adapter)
    {
        if (!string.IsNullOrWhiteSpace(adapter.Description))
            return adapter.Description.Length <= 34
                ? adapter.Description
                : adapter.Description[..31] + "...";

        return adapter.Name.Length <= 34
            ? adapter.Name
            : adapter.Name[..31] + "...";
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
