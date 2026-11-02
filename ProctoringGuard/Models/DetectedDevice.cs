namespace ProctoringGuard.Models;

public enum SignalType { WiFi, Bluetooth }

public enum DeviceCategory
{
    Unknown,
    InternalSystem,
    Phone,
    Computer,
    Headphones,
    Wearable,
    Peripheral,
    Hotspot,
    ISPRouter,
    OfficeRouter,
    UnknownBluetooth   // a live BLE/BT device we could not identify by name
}

public enum RiskLevel { Low, Medium, High }

public enum ProximityZone { VeryClose, Nearby, Far }

public class AlertEvent
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string DeviceName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public SignalType Signal { get; set; }
    public int Rssi { get; set; }
    public DeviceCategory Category { get; set; }
    public string Proximity { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ScanSession
{
    public string CandidateName { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public int BaselineCount { get; set; }
    public List<AlertEvent> Alerts { get; set; } = new();
}

public class DetectedDevice
{
    /// <summary>A device not heard from for this many seconds is considered gone.</summary>
    public const int StaleSeconds = 30;

    private const int SampleWindow = 5;          // short window = responsive proximity
    private readonly Queue<int> _samples = new();
    private int _rssi;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Unknown";
    public bool HasRealName { get; set; } = true;
    public SignalType Signal { get; set; }
    public string Extra { get; set; } = "";
    public DateTime FirstSeen { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public DateTime LastUiPush { get; set; } = DateTime.MinValue;
    public DeviceCategory Category { get; set; }
    public bool InBaseline { get; set; }
    public bool Alerted { get; set; }
    public int SeenCount { get; set; } = 1;

    public int Rssi
    {
        get => _rssi;
        set { _rssi = value; if (_samples.Count == 0) AvgRssi = value; }
    }

    public int AvgRssi { get; private set; }

    public void AddSample(int rssi)
    {
        _rssi = rssi;
        _samples.Enqueue(rssi);
        while (_samples.Count > SampleWindow) _samples.Dequeue();
        AvgRssi = (int)Math.Round(_samples.Average());
    }

    // ── Live-ness ───────────────────────────────────────────────────────────
    public bool IsActive => (DateTime.Now - LastSeen).TotalSeconds <= StaleSeconds;

    public bool IsVisible =>
        IsActive &&
        Category != DeviceCategory.Unknown &&
        Category != DeviceCategory.InternalSystem &&
        AvgRssi > -85;

    public bool IsRelevant => IsVisible;

    public ProximityZone Proximity =>
        AvgRssi >= -60 ? ProximityZone.VeryClose :
        AvgRssi >= -75 ? ProximityZone.Nearby :
                         ProximityZone.Far;

    public RiskLevel Risk => Category switch
    {
        DeviceCategory.Hotspot          => RiskLevel.High,
        DeviceCategory.Phone            => RiskLevel.High,
        DeviceCategory.Headphones       => RiskLevel.High,
        DeviceCategory.Wearable         => RiskLevel.High,
        DeviceCategory.Computer         => RiskLevel.Medium,
        DeviceCategory.Peripheral       => RiskLevel.Medium,
        DeviceCategory.UnknownBluetooth => RiskLevel.Medium,
        _                               => RiskLevel.Low
    };

    public bool IsThreat =>
        !InBaseline && IsVisible && Risk != RiskLevel.Low &&
        (Category != DeviceCategory.UnknownBluetooth || Proximity != ProximityZone.Far);

    // UI helpers
    public string SignalLabel    => $"{AvgRssi} dBm";
    public string RiskLabel      => Risk.ToString().ToUpper();
    public string CategoryLabel  => Category.ToString();
    public string ProximityLabel => Proximity switch
    {
        ProximityZone.VeryClose => "< 2m",
        ProximityZone.Nearby    => "~ 5-8m",
        _                       => "> 10m"
    };
}
