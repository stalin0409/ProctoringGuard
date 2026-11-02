using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.Advertisement;
using ProctoringGuard.Models;
using ProctoringGuard.Native;

namespace ProctoringGuard.Scanners;

/// <summary>
/// Live Bluetooth detection only — nothing from Windows' "remembered/paired" history.
///  1. BLE advertisement watcher: devices broadcasting right now, with REAL signal strength.
///  2. Win32 enumeration limited to devices that are CONNECTED right now.
/// </summary>
public class BluetoothScanner : IDisposable
{
    private readonly Action<DetectedDevice> _callback;
    private BluetoothLEAdvertisementWatcher? _watcher;
    private System.Threading.Timer?          _timer;
    private bool                             _disposed;

    private const int PollMs = 5_000;

    public BluetoothScanner(Action<DetectedDevice> callback) => _callback = callback;

    public void Start()
    {
        Stop();
        StartBle();
        _timer = new System.Threading.Timer(_ => ScanConnected(), null, 0, PollMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        var w = _watcher;
        _watcher = null;
        if (w != null)
        {
            try { w.Received -= OnAdvertisement; w.Stop(); } catch { }
        }
    }

    // ── BLE advertisements (real RSSI, real presence) ────────────────────────
    private void StartBle()
    {
        try
        {
            // Active = also asks devices for their name. Use Passive for zero radio emission.
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisement;
            _watcher.Stopped  += (s, e) => Log($"BLE watcher stopped: {e.Error}");
            _watcher.Start();
        }
        catch (Exception ex) { Log($"BLE start failed: {ex.Message}"); }
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender,
                                 BluetoothLEAdvertisementReceivedEventArgs e)
    {
        try
        {
            int rssi = e.RawSignalStrengthInDBm;
            if (rssi == -127) return;                       // "unavailable"

            string mac  = FormatMac(e.BluetoothAddress);
            string name = (e.Advertisement.LocalName ?? "").Trim();
            string mfr  = ManufacturerName(e.Advertisement);
            bool   real = name.Length > 0;

            if (real && DeviceClassifier.IsInternal(name)) return;

            var cat = real ? DeviceClassifier.Classify(name, "ble") : DeviceCategory.Unknown;
            if (cat == DeviceCategory.InternalSystem) return;
            if (cat == DeviceCategory.Unknown) cat = DeviceCategory.UnknownBluetooth;

            string tail = mac[^5..];
            string display = real ? name
                           : mfr.Length > 0 ? $"{mfr} device {tail}"
                           : $"BLE device {tail}";

            _callback(new DetectedDevice
            {
                Id          = mac,
                Name        = display,
                HasRealName = real,
                Signal      = SignalType.Bluetooth,
                Rssi        = rssi,
                Extra       = mfr.Length > 0 ? $"BLE|mfr:{mfr}" : "BLE",
                Category    = cat
            });
        }
        catch (Exception ex) { Log($"BLE: {ex.Message}"); }
    }

    // ── Classic Bluetooth: only devices connected right now ──────────────────
    private void ScanConnected()
    {
        IntPtr hFind = IntPtr.Zero;
        try
        {
            var info = new BLUETOOTH_DEVICE_INFO
                { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
            var prms = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize               = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = false,   // no paired history
                fReturnRemembered    = false,   // no remembered history
                fReturnUnknown       = false,
                fReturnConnected     = true,    // connected right now only
                fIssueInquiry        = false,
                cTimeoutMultiplier   = 2,
                hRadio               = IntPtr.Zero
            };

            hFind = BluetoothApi.BluetoothFindFirstDevice(ref prms, ref info);
            if (hFind == IntPtr.Zero) return;

            do { EmitConnected(info); }
            while (BluetoothApi.BluetoothFindNextDevice(hFind, ref info));
        }
        catch (Exception ex) { Log($"Win32: {ex.Message}"); }
        finally
        {
            if (hFind != IntPtr.Zero) BluetoothApi.BluetoothFindDeviceClose(hFind);
        }
    }

    private void EmitConnected(BLUETOOTH_DEVICE_INFO info)
    {
        if (!info.fConnected) return;

        string name = (info.szName ?? "").Trim();
        if (name.Length == 0 || DeviceClassifier.IsInternal(name)) return;

        string state = "connected|" + CodType(info.ulClassofDevice);
        var cat = DeviceClassifier.Classify(name, state);
        if (cat == DeviceCategory.InternalSystem) return;
        if (cat == DeviceCategory.Unknown) cat = DeviceCategory.UnknownBluetooth;

        _callback(new DetectedDevice
        {
            Id       = info.Address.ToString(),
            Name     = name,
            Signal   = SignalType.Bluetooth,
            Rssi     = -55,                // connected => within a few metres (estimate)
            Extra    = state,
            Category = cat
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string FormatMac(ulong addr)
    {
        var b = BitConverter.GetBytes(addr);
        return string.Join(":", Enumerable.Range(0, 6).Select(i => b[5 - i].ToString("X2")));
    }

    private static string ManufacturerName(BluetoothLEAdvertisement adv)
    {
        foreach (var m in adv.ManufacturerData)
        {
            return m.CompanyId switch
            {
                0x004C => "Apple",
                0x0006 => "Microsoft",
                0x0075 => "Samsung",
                0x00E0 => "Google",
                0x012D => "Sony",
                0x009E => "Bose",
                0x038F => "Xiaomi",
                _      => ""
            };
        }
        return "";
    }

    private static string CodType(uint cod)
    {
        uint major = (cod >> 8) & 0x1F;
        return major switch
        {
            0x01 => "type:Computer",
            0x02 => "type:Phone",
            0x04 => "type:AudioVideo(Headset/Speaker)",
            0x05 => "type:Peripheral(Keyboard/Mouse)",
            0x07 => "type:Wearable",
            0x09 => "type:Health",
            _    => "type:Unknown"
        };
    }

    private static void Log(string m) => Console.Error.WriteLine($"[BT] {m}");

    public void Dispose()
    {
        if (!_disposed) { Stop(); _disposed = true; }
    }
}
