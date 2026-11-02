using ManagedNativeWifi;
using ProctoringGuard.Models;

namespace ProctoringGuard.Scanners;

public class WiFiScanner : IDisposable
{
    private readonly Action<DetectedDevice> _callback;
    private CancellationTokenSource?       _cts;
    private bool                           _disposed;
    private const int IntervalSec = 8;

    public WiFiScanner(Action<DetectedDevice> callback) => _callback = callback;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => Loop(token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoScan(ct);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSec), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WiFi] {ex.Message}");
                try { await Task.Delay(10_000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task DoScan(CancellationToken ct)
    {
        // Ask Windows for a FRESH scan so the list reflects what is in the air now
        try { await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(5)); } catch { }
        if (ct.IsCancellationRequested) return;

        foreach (var bss in NativeWifi.EnumerateBssNetworks())
        {
            if (ct.IsCancellationRequested) break;

            string ssid  = bss.Ssid.ToString();
            string bssid = bss.Bssid.ToString();

            // Windows link quality 0-100  ->  approx dBm (0 => -100, 100 => -50)
            int rssi = bss.LinkQuality > 0 ? bss.LinkQuality / 2 - 100 : -95;

            bool real = !string.IsNullOrWhiteSpace(ssid);
            var cat   = DeviceClassifier.Classify(ssid, "access-point");

            _callback(new DetectedDevice
            {
                Id          = bssid,
                Name        = real ? ssid : $"Hidden-{bssid[^5..]}",
                HasRealName = real,
                Signal      = SignalType.WiFi,
                Rssi        = rssi,
                Extra       = BuildExtra(ssid, cat),
                Category    = cat
            });
        }
    }

    private static string BuildExtra(string ssid, DeviceCategory cat)
    {
        if (cat == DeviceCategory.Hotspot)
            return $"HOTSPOT:{DeviceClassifier.GetHotspotBrand(ssid)}";
        if (cat == DeviceCategory.ISPRouter)    return "ISP-router";
        if (cat == DeviceCategory.OfficeRouter) return "office-ap";
        return "access-point";
    }

    public void Dispose()
    {
        if (!_disposed) { Stop(); _disposed = true; }
    }
}
