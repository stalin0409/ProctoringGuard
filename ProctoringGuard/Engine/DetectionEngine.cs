using Newtonsoft.Json;
using ProctoringGuard.Models;
using ProctoringGuard.Scanners;

namespace ProctoringGuard.Engine;

public enum EngineState { Idle, Baselining, Monitoring, Stopped }

public class DetectionEngine : IDisposable
{
    public EngineState State   { get; private set; } = EngineState.Idle;
    public ScanSession Session { get; private set; } = new();

    private readonly Dictionary<string, DetectedDevice> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DetectedDevice> _live     = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private const int PruneEverySec = 5;
    private const int UiThrottleMs  = 1000;

    private readonly BluetoothScanner _bt;
    private readonly WiFiScanner      _wifi;
    private System.Threading.Timer?   _pruneTimer;
    private CancellationTokenSource?  _baselineCts;
    private bool _baselineDone;

    public event Action<DetectedDevice>? OnDeviceUpdated;
    public event Action<DetectedDevice>? OnDeviceRemoved;
    public event Action<DetectedDevice>? OnNewAlert;
    public event Action<string>?         OnLog;
    public event Action<EngineState>?    OnStateChanged;

    public int VisibleCount { get { lock (_lock) return _live.Values.Count(d => d.IsVisible); } }
    public int TotalAlerts  { get { lock (_lock) return Session.Alerts.Count; } }

    public DetectionEngine()
    {
        _bt   = new BluetoothScanner(Handle);
        _wifi = new WiFiScanner(Handle);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async Task StartBaselineAsync(string candidate, int seconds = 60)
    {
        _baselineCts?.Cancel();
        _baselineCts = new CancellationTokenSource();
        var token = _baselineCts.Token;

        _bt.Stop(); _wifi.Stop();

        lock (_lock)
        {
            Session = new ScanSession { CandidateName = candidate };
            _baseline.Clear();
            _live.Clear();
            _baselineDone = false;
        }

        SetState(EngineState.Baselining);
        Log($"Baselining for {seconds}s — learning devices that are present right now...");

        _bt.Start();
        _wifi.Start();
        _pruneTimer?.Dispose();
        _pruneTimer = new System.Threading.Timer(_ => Prune(), null,
                          PruneEverySec * 1000, PruneEverySec * 1000);

        try { await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
        catch (OperationCanceledException) { return; }

        lock (_lock)
        {
            foreach (var kv in _live)
            {
                kv.Value.InBaseline = true;
                _baseline[kv.Key]   = kv.Value;
            }
            Session.BaselineCount = _baseline.Values.Count(d => d.IsVisible);
            _baselineDone = true;
        }
        Log($"Baseline complete — {Session.BaselineCount} visible devices recorded as SAFE.");
    }

    public void StartMonitoring()
    {
        if (State != EngineState.Baselining || !_baselineDone)
            throw new InvalidOperationException("Run baseline first.");
        SetState(EngineState.Monitoring);
        Log("Monitoring LIVE. Watching for new unauthorized devices...");
    }

    public void Stop()
    {
        SetState(EngineState.Stopped);          // first, so late scanner callbacks are ignored
        _baselineCts?.Cancel();
        _pruneTimer?.Dispose(); _pruneTimer = null;
        _bt.Stop(); _wifi.Stop();
        lock (_lock) Session.EndTime = DateTime.Now;
        Log("Scanning stopped.");
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public List<DetectedDevice> GetVisible()
    {
        lock (_lock)
            return _live.Values
                .Where(d => d.IsVisible)
                .OrderByDescending(d => !d.InBaseline)
                .ThenByDescending(d => d.AvgRssi)
                .ThenByDescending(d => d.LastSeen)
                .ToList();
    }

    /// <summary>Unauthorized devices that are in range RIGHT NOW.</summary>
    public List<DetectedDevice> GetAlerts()
    {
        lock (_lock)
            return _live.Values.Where(d => d.IsThreat)
                .OrderByDescending(d => d.AvgRssi).ToList();
    }

    public async Task ExportReportAsync(string path)
    {
        string json;
        lock (_lock)
        {
            var report = new
            {
                Session,
                BaselineDevices = _baseline.Values
                    .Where(d => d.Category != DeviceCategory.Unknown &&
                                d.Category != DeviceCategory.InternalSystem)
                    .Select(d => new { d.Id, d.Name, d.Signal, d.Category, d.Proximity, d.AvgRssi })
                    .ToList(),
                CurrentAlertDevices = _live.Values.Where(d => d.IsThreat)
                    .Select(d => new { d.Id, d.Name, d.Signal, d.Category, d.AvgRssi,
                                       d.ProximityLabel, d.FirstSeen, d.Extra })
                    .ToList(),
                Summary = new
                {
                    TotalAlerts = Session.Alerts.Count,
                    Duration = (Session.EndTime ?? DateTime.Now) - Session.StartTime
                }
            };
            json = JsonConvert.SerializeObject(report, Formatting.Indented);
        }
        await File.WriteAllTextAsync(path, json);
        Log($"Report saved: {path}");
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private static bool IsSpecific(DeviceCategory c) =>
        c != DeviceCategory.Unknown &&
        c != DeviceCategory.InternalSystem &&
        c != DeviceCategory.UnknownBluetooth;

    private void Handle(DetectedDevice incoming)
    {
        if (incoming.Category == DeviceCategory.InternalSystem) return;
        if (State != EngineState.Baselining && State != EngineState.Monitoring) return;

        lock (_lock)
        {
            var now = DateTime.Now;

            if (_live.TryGetValue(incoming.Id, out var ex))
            {
                ex.LastSeen = now;
                ex.SeenCount++;
                ex.AddSample(incoming.Rssi);

                if (incoming.HasRealName)            { ex.Name = incoming.Name; ex.HasRealName = true; }
                if (IsSpecific(incoming.Category))   ex.Category = incoming.Category;
                if (!string.IsNullOrEmpty(incoming.Extra)) ex.Extra = incoming.Extra;

                if (State == EngineState.Monitoring && ex.IsThreat && !ex.Alerted)
                    FireAlert(ex);

                // Throttle UI so busy Bluetooth environments don't flood the form
                if ((now - ex.LastUiPush).TotalMilliseconds >= UiThrottleMs)
                {
                    ex.LastUiPush = now;
                    OnDeviceUpdated?.Invoke(ex);
                }
                return;
            }

            // Brand-new device (or one that left range and came back)
            incoming.AddSample(incoming.Rssi);
            incoming.FirstSeen   = now;
            incoming.LastSeen    = now;
            incoming.LastUiPush  = now;
            incoming.InBaseline  = _baseline.ContainsKey(incoming.Id);
            _live[incoming.Id]   = incoming;

            if (State == EngineState.Monitoring && incoming.IsThreat)
                FireAlert(incoming);

            OnDeviceUpdated?.Invoke(incoming);
        }
    }

    /// <summary>Removes devices that have not been heard from recently.</summary>
    private void Prune()
    {
        List<DetectedDevice> gone;
        lock (_lock)
        {
            gone = _live.Values.Where(d => !d.IsActive).ToList();
            foreach (var d in gone)
            {
                _live.Remove(d.Id);
                // Seen during the learning phase but left before it ended => still safe
                if (State == EngineState.Baselining)
                {
                    d.InBaseline = true;
                    _baseline[d.Id] = d;
                }
            }
        }
        foreach (var d in gone)
        {
            if (d.Category != DeviceCategory.Unknown &&
                d.Category != DeviceCategory.InternalSystem)
                Log($"Left range: {d.Name} ({d.CategoryLabel})");
            OnDeviceRemoved?.Invoke(d);
        }
    }

    // Caller already holds _lock
    private void FireAlert(DetectedDevice d)
    {
        d.Alerted = true;

        string desc =
            $"[{d.Risk}] {d.CategoryLabel}: \"{d.Name}\" | {d.ProximityLabel}" +
            (string.IsNullOrEmpty(d.Extra) ? "" : $" | {d.Extra}");

        Log($"[ALERT] {desc}");
        Session.Alerts.Add(new AlertEvent
        {
            DeviceName  = d.Name,   DeviceId = d.Id,
            Signal      = d.Signal, Rssi     = d.AvgRssi,
            Category    = d.Category,
            Proximity   = d.ProximityLabel,
            Description = desc
        });
        OnNewAlert?.Invoke(d);
    }

    private void SetState(EngineState s) { State = s; OnStateChanged?.Invoke(s); }

    private void Log(string m) => OnLog?.Invoke(m);

    public void Dispose()
    {
        _baselineCts?.Cancel();
        _pruneTimer?.Dispose();
        _bt.Dispose();
        _wifi.Dispose();
    }
}
