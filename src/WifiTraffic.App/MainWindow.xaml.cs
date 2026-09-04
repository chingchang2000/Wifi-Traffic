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
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public ObservableCollection<TrafficRecord> LiveTraffic { get; } = new();
    public ObservableCollection<DomainRow> TopDomains { get; } = new();

    private bool IsRouterDnsMode => ModeCombo?.SelectedIndex == 1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _capture.TrafficObserved += TrafficObserved;
        _capture.CaptureError += (_, message) =>
            Dispatcher.Invoke(() => StatusText.Text = $"Capture warning: {message}");

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

    private void LoadAdapters()
    {
        var adapters = _capture.GetAdapters();
        AdapterCombo.ItemsSource = adapters;

        if (adapters.Count > 0)
            AdapterCombo.SelectedIndex = 0;
        else
            StatusText.Text = "No capture adapters found. Install Npcap for This PC mode.";
    }

    private void UpdateRouterDnsInfo()
    {
        var info = _networkSetup.GetRouterDnsInfo();

        if (info is null)
        {
            RouterDnsIpText.Text = "Could not detect";
            RouterGatewayText.Text = "Could not detect";
            return;
        }

        RouterDnsIpText.Text = info.LanIp;
        RouterGatewayText.Text = $"{info.GatewayIp} ({info.InterfaceName})";
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RouterDnsPanel is null || AdapterCombo is null || StartButton is null)
            return;

        if (IsRouterDnsMode)
        {
            RouterDnsPanel.Visibility = Visibility.Visible;
            AdapterCombo.IsEnabled = false;
            SubtitleText.Text = "Whole Network mode • devices stay on the normal router Wi-Fi";
            StartButton.Content = "Start DNS sensor";
            UpdateRouterDnsInfo();
        }
        else
        {
            RouterDnsPanel.Visibility = Visibility.Collapsed;
            AdapterCombo.IsEnabled = true;
            SubtitleText.Text = "This PC mode • monitors traffic visible to this computer";
            StartButton.Content = "Start capture";
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
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

                StatusText.Text = $"Router DNS sensor running on {info.LanIp}:53";
                RouterDnsStatusText.Text =
                    $"Running. Set your router's LAN/DHCP DNS server to {info.LanIp}.";
            }
            else
            {
                if (AdapterCombo.SelectedItem is not CaptureAdapter adapter)
                {
                    MessageBox.Show("Select a network adapter first.");
                    return;
                }

                _capture.Start(adapter.Id);

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                AdapterCombo.IsEnabled = false;
                ModeCombo.IsEnabled = false;

                StatusText.Text = $"Capturing this PC on {adapter.Description}";
            }

            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(66, 211, 146));
        }
        catch (Exception ex)
        {
            var message = IsRouterDnsMode
                ? "DNS sensor could not start. Port 53 may already be in use by another DNS program.\n\n"
                : "Capture could not be started. Make sure Npcap is installed and run WiFi Traffic as Administrator.\n\n";

            MessageBox.Show(
                message + ex.Message,
                "WiFi Traffic",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _capture.Stop();
        _dnsProxy.Stop();

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ModeCombo.IsEnabled = true;
        AdapterCombo.IsEnabled = !IsRouterDnsMode;
        StatusText.Text = "Stopped";
        RouterDnsStatusText.Text = "DNS sensor is stopped.";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(97, 112, 131));
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
        RouterDnsStatusText.Text = $"Copied DNS IP {info.LanIp} to clipboard.";
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateRouterDnsInfo();
        await RefreshDashboardAsync();
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
