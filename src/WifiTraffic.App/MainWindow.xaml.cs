using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using WifiTraffic.Models;
using WifiTraffic.Services;

namespace WifiTraffic;

public partial class MainWindow : Window
{
    private readonly CaptureService _capture = new();
    private readonly DatabaseService _database = new();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public ObservableCollection<TrafficRecord> LiveTraffic { get; } = new();
    public ObservableCollection<DomainRow> TopDomains { get; } = new();

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

            var adapters = _capture.GetAdapters();
            AdapterCombo.ItemsSource = adapters;

            if (adapters.Count > 0)
                AdapterCombo.SelectedIndex = 0;

            foreach (var item in await _database.GetRecentAsync(250))
                LiveTraffic.Add(item);

            await RefreshDashboardAsync();
            _refreshTimer.Start();

            if (adapters.Count == 0)
                StatusText.Text = "No capture adapters found. Install Npcap and restart.";
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

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterCombo.SelectedItem is not CaptureAdapter adapter)
        {
            MessageBox.Show("Select a network adapter first.");
            return;
        }

        try
        {
            _capture.Start(adapter.Id);
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            AdapterCombo.IsEnabled = false;
            StatusText.Text = $"Capturing on {adapter.Description}";
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
