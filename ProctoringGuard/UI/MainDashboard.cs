using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using ProctoringGuard.Engine;
using ProctoringGuard.Models;

namespace ProctoringGuard.UI;

public class MainDashboard : Form
{
    private readonly DetectionEngine _engine = new();
    private readonly Dictionary<string, ListViewItem> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    // -- UI Controls --
    private Panel       _header      = null!;
    private Label       _lblTitle    = null!;
    private Label       _lblStatus   = null!;
    private Label       _lblStats    = null!;
    private Label       _lblTimer    = null!;
    private Panel       _toolbar     = null!;
    private TextBox     _txName      = null!;
    private Button      _btnBaseline = null!;
    private Button      _btnMonitor  = null!;
    private Button      _btnStop     = null!;
    private Panel       _alertBanner = null!;
    private Label       _lblAlert    = null!;
    private ListView    _list        = null!;
    private RichTextBox _log         = null!;
    private SplitContainer _split    = null!;

    public MainDashboard()
    {
        this.SuspendLayout();
        BuildUI();
        Wire();
        this.ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            _split.SplitterDistance = (int)(this.ClientSize.Width * 0.6);
            _split.Panel1MinSize = 300;
            _split.Panel2MinSize = 150;
        }
        catch { _split.SplitterDistance = 250; }
    }

    private void BuildUI()
    {
        Text          = "ProctoringGuard v5 — Sentinel System";
        Size          = new Size(1120, 740);
        MinimumSize   = new Size(920, 620);
        BackColor     = Color.FromArgb(240, 241, 248);
        Font          = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;

        _header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.FromArgb(18, 18, 36) };
        _lblTitle  = MkLbl("ProctoringGuard", 15, FontStyle.Bold, Color.White, new Point(14, 10));
        _lblStatus = MkLbl("Status: IDLE", 9, FontStyle.Regular, Color.FromArgb(130,130,180), new Point(14, 42));
        _lblStats  = MkLbl("Nearby: 0 | Active alerts: 0 | Total alerts: 0", 9, FontStyle.Regular, Color.FromArgb(130,130,180), new Point(210, 42));
        _lblTimer  = MkLbl("", 11, FontStyle.Bold, Color.FromArgb(65,205,105), new Point(920, 20));
        _header.Controls.AddRange(new Control[] { _lblTitle, _lblStatus, _lblStats, _lblTimer });

        _toolbar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(26,26,46), Padding = new Padding(10) };
        _txName = new TextBox { PlaceholderText = "Candidate Name", Width = 160, Location = new Point(10, 12), BackColor = Color.FromArgb(44,44,66), ForeColor = Color.White };
        _btnBaseline = MkBtn("1. Baseline", Color.FromArgb(20,112,168), new Point(180, 11));
        _btnMonitor  = MkBtn("2. Start",    Color.FromArgb(30,140,68),  new Point(340, 11));
        _btnStop     = MkBtn("Stop",        Color.FromArgb(148,46,46),  new Point(500, 11));
        _toolbar.Controls.AddRange(new Control[] { _txName, _btnBaseline, _btnMonitor, _btnStop });

        _alertBanner = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.DarkRed, Visible = false };
        _lblAlert = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        _alertBanner.Controls.Add(_lblAlert);

        _split = new SplitContainer { Dock = DockStyle.Fill, Panel1MinSize = 0, Panel2MinSize = 0, BorderStyle = BorderStyle.None };
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = Color.White };
        _list.Columns.Add("Last seen", 80); _list.Columns.Add("Signal", 70); _list.Columns.Add("Risk", 70);
        _list.Columns.Add("Category", 120); _list.Columns.Add("Name", 190); _list.Columns.Add("Proximity", 90);
        _list.Columns.Add("Status", 80);

        _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(10,10,20), ForeColor = Color.Lime };

        _split.Panel1.Controls.Add(_list);
        _split.Panel2.Controls.Add(_log);

        Controls.Add(_split);
        Controls.Add(_alertBanner);
        Controls.Add(_toolbar);
        Controls.Add(_header);
    }

    private void Wire()
    {
        _engine.OnDeviceUpdated += d => UI(() => UpsertRow(d));
        _engine.OnDeviceRemoved += d => UI(() => RemoveRow(d));
        _engine.OnNewAlert      += d => UI(() => ShowAlert(d));
        _engine.OnLog           += m => UI(() => AppendLog(m));
        _engine.OnStateChanged  += s => UI(() => _lblStatus.Text = $"Status: {s.ToString().ToUpper()}");

        _btnBaseline.Click += OnBaseline;
        _btnMonitor.Click  += OnMonitor;
        _btnStop.Click     += OnStop;
        _btnMonitor.Enabled = false;
        _btnStop.Enabled    = false;

        _tick.Tick += (_, _) => RefreshHeader();
        _tick.Start();

        FormClosing += (_, _) => { _tick.Stop(); _engine.Dispose(); };
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private async void OnBaseline(object? s, EventArgs e)
    {
        _btnBaseline.Enabled = false;
        _btnMonitor.Enabled  = false;
        _btnStop.Enabled     = true;
        _list.Items.Clear();
        _rows.Clear();
        _alertBanner.Visible = false;
        try
        {
            await _engine.StartBaselineAsync(_txName.Text, 60);
            if (_engine.State == EngineState.Baselining)
            {
                _btnMonitor.Enabled = true;
                foreach (var d in _engine.GetVisible()) UpsertRow(d);   // refresh "Baseline" status
            }
        }
        catch (Exception ex) { AppendLog($"Baseline error: {ex.Message}"); _btnBaseline.Enabled = true; }
    }

    private void OnMonitor(object? s, EventArgs e)
    {
        try { _engine.StartMonitoring(); _btnMonitor.Enabled = false; }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private void OnStop(object? s, EventArgs e)
    {
        _engine.Stop();
        _btnStop.Enabled     = false;
        _btnMonitor.Enabled  = false;
        _btnBaseline.Enabled = true;
    }

    // ── Live list ────────────────────────────────────────────────────────────

    private void UpsertRow(DetectedDevice d)
    {
        _rows.TryGetValue(d.Id, out var item);

        // Moved out of range / no longer relevant => take it off the list
        if (!d.IsVisible)
        {
            if (item != null) { _list.Items.Remove(item); _rows.Remove(d.Id); }
            return;
        }

        string status = d.InBaseline ? "Baseline"
                      : _engine.State == EngineState.Baselining ? "Learning" : "NEW";

        string[] cells =
        {
            d.LastSeen.ToString("HH:mm:ss"), d.SignalLabel, d.RiskLabel,
            d.CategoryLabel, d.Name, d.ProximityLabel, status
        };

        if (item == null)
        {
            item = new ListViewItem(cells) { Tag = d.Id };
            _list.Items.Insert(0, item);
            _rows[d.Id] = item;
        }
        else
        {
            for (int i = 0; i < cells.Length; i++) item.SubItems[i].Text = cells[i];
        }

        item.BackColor = d.IsThreat ? Color.FromArgb(255, 214, 214) : Color.White;
    }

    private void RemoveRow(DetectedDevice d)
    {
        if (_rows.TryGetValue(d.Id, out var item))
        {
            _list.Items.Remove(item);
            _rows.Remove(d.Id);
        }
    }

    // ── Header / banner refresh (runs every second) ──────────────────────────

    private void RefreshHeader()
    {
        var state = _engine.State;
        var threats = state == EngineState.Monitoring ? _engine.GetAlerts() : new List<DetectedDevice>();

        _lblStats.Text = $"Nearby: {_engine.VisibleCount} | Active alerts: {threats.Count} | Total alerts: {_engine.TotalAlerts}";

        if (threats.Count > 0)
        {
            _lblAlert.Text = $"ALERT: {threats.Count} unauthorized device(s) — " +
                             string.Join(", ", threats.Take(3).Select(t => t.Name));
            _alertBanner.Visible = true;
        }
        else _alertBanner.Visible = false;

        if (state == EngineState.Baselining || state == EngineState.Monitoring)
            _lblTimer.Text = (DateTime.Now - _engine.Session.StartTime).ToString(@"hh\:mm\:ss");
    }

    private void ShowAlert(DetectedDevice d)
    {
        System.Media.SystemSounds.Exclamation.Play();
        RefreshHeader();
    }

    private void AppendLog(string msg)
    {
        _log.AppendText($"{DateTime.Now:HH:mm:ss} > {msg}\n");
        _log.ScrollToCaret();
    }

    private void UI(Action a) { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }

    private static Label MkLbl(string t, float sz, FontStyle fs, Color c, Point p) =>
        new Label { Text = t, Font = new Font("Segoe UI", sz, fs), ForeColor = c, AutoSize = true, Location = p, BackColor = Color.Transparent };

    private static Button MkBtn(string t, Color bg, Point p) =>
        new Button { Text = t, Location = p, Size = new Size(150, 32), BackColor = bg, ForeColor = Color.White,
                     FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                     FlatAppearance = { BorderSize = 0 } };
}
