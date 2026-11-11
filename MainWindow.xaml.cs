using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HyperMouse.Core.Actions;
using HyperMouse.Core.Common;
using MouseButton = HyperMouse.Core.Common.MouseButton;
using MouseDevice = HyperMouse.Core.Input.RawInput.MouseDevice;
using HyperMouse.Core.Config;
using HyperMouse.Core.Engine;
using HyperMouse.Core.Input;
using HyperMouse.Core.Input.RawInput;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HyperMouse;

public sealed class RuleDisplayItem
{
    public MappingRuleDto Dto { get; set; } = null!;
    public string ButtonName { get; set; } = "";
    public string CadenceDescription { get; set; } = "";
    public string ActionDisplay { get; set; } = "";
    public SymbolRegular Icon { get; set; } = SymbolRegular.Cursor24;
    public Brush Accent { get; set; } = Brushes.DodgerBlue;
    public Brush AccentSoft { get; set; } = Brushes.Transparent;
}

public partial class MainWindow : FluentWindow
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HyperMouse",
        "profiles.json"
    );

    private readonly ThreadedMouseHook _mouseHook;
    private readonly Win32InputSimulator _inputSimulator;
    private readonly HyperMouseEngine _engine;
    private readonly StateMachineConfig _config;
    private RawInputReceiver? _rawInput;

    private ProfileConfig _profile;
    private readonly ObservableCollection<RuleDisplayItem> _rulesList = new();
    private readonly ObservableCollection<string> _eventLogs = new();

    private MappingRuleDto? _editingRule;
    private Point _dragStartPoint;
    private RuleDisplayItem? _draggedItem;
    private readonly ConcurrentDictionary<MouseButton, long> _lastHookEventTimes = new();

    private DispatcherTimer? _learnTimer;
    private DispatcherTimer? _deviceRefreshTimer;

    private DispatcherTimer CreateRefreshTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        t.Tick += (_, _) => { t.Stop(); _rawInput?.RefreshDevices(); };
        return t;
    }
    private readonly ObservableCollection<MouseDevice> _miceView = new();

    public MainWindow()
    {
        InitializeComponent();

        RulesListBox.ItemsSource = _rulesList;
        LiveEventListBox.ItemsSource = _eventLogs;
        MiceList.ItemsSource = _miceView;

        _config = new StateMachineConfig();
        _mouseHook = new ThreadedMouseHook();
        _inputSimulator = new Win32InputSimulator();
        _engine = new HyperMouseEngine(_mouseHook, _inputSimulator, null, _config);

        // Load profile configuration
        _profile = ProfileConfig.Load(ConfigPath);
        ApplyProfileToEngine();

        // Subscribe to hook and engine
        _mouseHook.MouseEventReceived += OnMouseEventReceived;
        _engine.StateMachine.TriggerFired += OnTriggerFired;

        try
        {
            _engine.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Hook başlatılamadı: {ex.Message}", "HyperMouse Hatası", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
        }

        InitTray();
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // Register RawInput for direct USB HID mouse capture (Rampage SMX-R44 & gaming mice)
        try
        {
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            if (source != null)
            {
                _rawInput = new RawInputReceiver();
                _rawInput.Diagnostic += msg => Dispatcher.BeginInvoke(() => AddLog(msg, detail: !msg.StartsWith("[Learn]")));
                _rawInput.RawMouseEventReceived += OnRawMouseEventReceived;
                _rawInput.MiceChanged += () => Dispatcher.BeginInvoke(UpdateMouseInfo);
                _rawInput.DevicesChanged += () =>
                {
                    // Windows sends one notification per device (also for all devices at registration): debounce.
                    _deviceRefreshTimer ??= CreateRefreshTimer();
                    _deviceRefreshTimer.Stop();
                    _deviceRefreshTimer.Start();
                };
                _rawInput.ActiveMouseChanged += _ => Dispatcher.BeginInvoke(UpdateMouseInfo);
                _rawInput.LearnCandidate += sig => Dispatcher.BeginInvoke(() => OnLearnCandidate(sig));
                _rawInput.SignalLearned += sig => Dispatcher.BeginInvoke(() => OnSignalLearned(sig));
                _rawInput.SetBindings(_profile.LearnedSignals);
                source.AddHook(_rawInput.ProcessMessage);
                source.AddHook(WndProc);
                RegisterEmergencyHotkey(helper.Handle);
                if (_rawInput.Register(helper.Handle))
                {
                    AddLog("[System] RawInput ve AppCommand hazır.");
                }
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Error] RawInput başlatılamadı: {ex.Message}");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
        SliderLongPress.Value = _profile.LongPressThresholdMs;
        LoadAppSettings();
        AddLog("[System] Hazır. Hook aktif.");
        foreach (var dropped in _profile.DroppedConflicts)
            AddLog($"[Conflict] Yinelenen kural silindi: {dropped.Button} {dropped.Cadence} → {dropped.DisplayName}");
        UpdateMouseInfo();
        UpdateDpiStatus();
    }

    // ==========================================
    // TRAY, HOTKEY, THEME, AUTOSTART, IMPORT/EXPORT
    // ==========================================

    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _trayToggleItem;
    private bool _exiting;
    private bool _trayHintShown;
    private bool _settingsLoading;
    private IntPtr _hwnd;

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4D4D;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "HyperMouse";

    /// <summary>Called by App when launched with --tray (autostart).</summary>
    public void StartHiddenInTray()
    {
        // Create the HWND (hooks, RawInput, hotkey) without showing the window.
        new WindowInteropHelper(this).EnsureHandle();
        _trayHintShown = true;
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void InitTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "HyperMouse",
            Icon = (Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null) ?? System.Drawing.SystemIcons.Application,
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _trayToggleItem = new System.Windows.Forms.ToolStripMenuItem("Duraklat", null, (_, _) => Dispatcher.Invoke(() => EngineToggle.IsChecked = !(EngineToggle.IsChecked == true)));
        menu.Items.Add(_trayToggleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ShowFromTray); };
    }

    private void UpdateTrayState(bool active)
    {
        if (_tray == null) return;
        _tray.Text = active ? "HyperMouse — aktif" : "HyperMouse — duraklatıldı";
        if (_trayToggleItem != null) _trayToggleItem.Text = active ? "Duraklat" : "Başlat";
    }

    private void BtnExitApp_Click(object sender, RoutedEventArgs e) => ExitApp();

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void RegisterEmergencyHotkey(IntPtr hwnd)
    {
        _hwnd = hwnd;
        bool ok = RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, (uint)'M');
        AddLog(ok ? "[Hotkey] Ctrl+Alt+Shift+M kayıtlı." : "[Hotkey] Ctrl+Alt+Shift+M başka bir program tarafından kullanılıyor.");
        if (!ok && TxtHotkeyStatus != null) TxtHotkeyStatus.Text = "Kısayol başka bir program tarafından kullanılıyor.";
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == HotkeyId)
        {
            EngineToggle.IsChecked = !(EngineToggle.IsChecked == true);
            bool active = EngineToggle.IsChecked == true;
            _tray?.ShowBalloonTip(1500, "HyperMouse", active ? "Motor açıldı." : "Motor duraklatıldı.", System.Windows.Forms.ToolTipIcon.None);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void LoadAppSettings()
    {
        _settingsLoading = true;
        SelectComboByTag(CmbTheme, _profile.Theme);
        ToggleTray.IsChecked = _profile.MinimizeToTray;
        ToggleAutostart.IsChecked = IsAutostartEnabled();
        _settingsLoading = false;
        ApplyTheme(_profile.Theme);
    }

    private void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "Light": ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica); break;
            case "Dark": ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica); break;
            default: ApplicationThemeManager.ApplySystemTheme(); break;
        }
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        string theme = (CmbTheme.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System";
        _profile.Theme = theme;
        ProfileConfig.Save(_profile, ConfigPath);
        ApplyTheme(theme);
    }

    private void ToggleTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        _profile.MinimizeToTray = ToggleTray.IsChecked == true;
        ProfileConfig.Save(_profile, ConfigPath);
    }

    private static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        catch
        {
            return false;
        }
    }

    private void ToggleAutostart_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (ToggleAutostart.IsChecked == true && Environment.ProcessPath is { } exe)
            {
                key.SetValue(RunValue, $"\"{exe}\" --tray");
                AddLog("[System] Windows ile başlatma açıldı.");
            }
            else
            {
                key.DeleteValue(RunValue, false);
                AddLog("[System] Windows ile başlatma kapatıldı.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Error] Autostart: {ex.Message}");
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Profili dışa aktar",
            Filter = "HyperMouse profili (*.json)|*.json",
            FileName = $"HyperMouse-{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        ProfileConfig.Save(_profile, dlg.FileName);
        AddLog($"[Profile] Dışa aktarıldı: {dlg.FileName}");
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Profil içe aktar", Filter = "HyperMouse profili (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;

        ProfileConfig imported;
        try
        {
            imported = ProfileConfig.Import(dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Dosya okunamadı: {ex.Message}", "İçe aktar", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = System.Windows.MessageBox.Show($"{imported.Rules.Count} kural yüklenecek, mevcut kurallar değişecek. Devam edilsin mi?",
            "İçe aktar", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != System.Windows.MessageBoxResult.Yes) return;

        _profile = imported;
        ProfileConfig.Save(_profile, ConfigPath);
        ApplyProfileToEngine();
        SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
        SliderLongPress.Value = _profile.LongPressThresholdMs;
        LoadAppSettings();
        UpdateMouseInfo();
        UpdateDpiStatus();
        AddLog($"[Profile] İçe aktarıldı: {imported.Rules.Count} kural" +
               (imported.DroppedConflicts.Count > 0 ? $", {imported.DroppedConflicts.Count} çakışan kural atlandı." : "."));
    }

    // ==========================================
    // NAVIGATION & DEVICE INFO
    // ==========================================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageRules == null || PageLive == null || PageSettings == null) return;
        string tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Rules";
        PageRules.Visibility = tag == "Rules" ? Visibility.Visible : Visibility.Collapsed;
        PageLive.Visibility = tag == "Live" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMouseInfo()
    {
        if (_rawInput == null)
        {
            TxtMouseModel.Text = "Fare bilgisi okunamadı";
            return;
        }

        var mice = _rawInput.Mice.Where(m => m.Vid != 0).ToList();
        _miceView.Clear();
        foreach (var m in _rawInput.Mice) _miceView.Add(m);
        TxtMouseCount.Text = $"{mice.Count} aygıt";

        var current = _rawInput.ActiveMouse is { Vid: not 0 } a ? a : mice.FirstOrDefault();
        if (current == null)
        {
            TxtMouseModel.Text = "Tanınan fare yok";
            TxtMouseDetails.Text = "Tuşlar yine de çalışır.";
            return;
        }

        _currentMouse = current;
        bool hasAlias = _profile.DeviceAliases.TryGetValue(current.VidPid, out var alias) && !string.IsNullOrWhiteSpace(alias);
        TxtMouseModel.Text = hasAlias ? alias : current.DisplayName;
        TxtMouseModel.ToolTip = hasAlias ? $"Orijinal ad: {current.DisplayName}" : null;
        string vendor = MouseVendors.Lookup(current.Vid);
        string active = _rawInput.ActiveMouse?.Handle == current.Handle ? "Kullanılıyor" : "Bağlı";
        TxtMouseDetails.Text = $"{active} · {current.Connection} · VID:PID {current.VidPid}" + (vendor.Length > 0 ? $" · {vendor}" : "");
        MouseConnIcon.Symbol = current.Connection == "Bluetooth" ? SymbolRegular.Bluetooth24 : SymbolRegular.UsbStick24;
    }

    private MouseDevice? _currentMouse;

    private void BtnRenameMouse_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMouse == null) return;
        if (TxtMouseAlias.Visibility == Visibility.Visible)
        {
            CommitMouseAlias();
            return;
        }
        TxtMouseAlias.Text = TxtMouseModel.Text;
        TxtMouseModel.Visibility = Visibility.Collapsed;
        TxtMouseAlias.Visibility = Visibility.Visible;
        BtnRenameMouse.Icon = new SymbolIcon { Symbol = SymbolRegular.Checkmark16 };
        TxtMouseAlias.Focus();
        TxtMouseAlias.SelectAll();
    }

    private void TxtMouseAlias_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitMouseAlias();
        else if (e.Key == Key.Escape) EndAliasEdit();
    }

    private void CommitMouseAlias()
    {
        if (_currentMouse != null)
        {
            string name = TxtMouseAlias.Text.Trim();
            if (name.Length == 0 || name == _currentMouse.DisplayName) _profile.DeviceAliases.Remove(_currentMouse.VidPid);
            else _profile.DeviceAliases[_currentMouse.VidPid] = name;
            ProfileConfig.Save(_profile, ConfigPath);
        }
        EndAliasEdit();
        UpdateMouseInfo();
    }

    private void EndAliasEdit()
    {
        TxtMouseAlias.Visibility = Visibility.Collapsed;
        TxtMouseModel.Visibility = Visibility.Visible;
        BtnRenameMouse.Icon = new SymbolIcon { Symbol = SymbolRegular.Edit16 };
    }

    private void UpdateDpiStatus()
    {
        bool learned = _profile.LearnedSignals.TryGetValue(MouseButton.DpiButton, out var sig);
        TxtDpiStatus.Text = learned ? "DPI tuşu hazır" : "DPI tuşu öğretilmedi";
        BtnForgetDpi.Visibility = learned ? Visibility.Visible : Visibility.Collapsed;
        if (_rawInput?.IsLearning != true)
        {
            TxtLearnStatus.Text = learned
                ? $"Hazır: {sig!.Description}. Kurallarda 'DPI Tuşu'nu seçebilirsiniz."
                : "'Öğren'e, sonra DPI tuşuna basın.";
        }
        ValidateEditor();
    }

    // ==========================================
    // LEARN MODE (DPI / vendor buttons)
    // ==========================================

    private const double LearnSeconds = 3.0;
    private DateTime _learnStarted;
    private DispatcherTimer? _countdownTimer;

    private void BtnLearnDpi_Click(object sender, RoutedEventArgs e)
    {
        if (_rawInput == null) return;
        if (_rawInput.IsLearning)
        {
            StopLearn("İptal edildi.");
            return;
        }

        _rawInput.BeginLearn();
        BtnLearnDpi.Content = "İptal";
        TxtLearnStatus.Text = "DPI tuşuna şimdi basın.";
        AddLog("[Learn] DPI tuşu bekleniyor…");

        // Countdown ring
        _learnStarted = DateTime.Now;
        LearnIcon.Visibility = Visibility.Collapsed;
        LearnCountdown.Visibility = Visibility.Visible;
        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _countdownTimer.Tick += (_, _) =>
        {
            double left = LearnSeconds - (DateTime.Now - _learnStarted).TotalSeconds;
            if (left <= 0)
            {
                StopLearn("Sinyal gelmedi. Bu farenin DPI tuşu bilgisayara sinyal göndermiyor olabilir.");
                return;
            }
            LearnRing.Progress = left / LearnSeconds * 100;
            LearnCountdownText.Text = Math.Ceiling(left).ToString("0");
        };
        _countdownTimer.Start();
    }

    private void HideCountdown()
    {
        _countdownTimer?.Stop();
        LearnCountdown.Visibility = Visibility.Collapsed;
        LearnIcon.Visibility = Visibility.Visible;
    }

    private void StopLearn(string message)
    {
        HideCountdown();
        _learnTimer?.Stop();
        _rawInput?.CancelLearn();
        BtnLearnDpi.Content = "Öğren";
        TxtLearnStatus.Text = message;
        AddLog($"[Learn] {message}");
    }

    private void OnLearnCandidate(LearnedSignal sig)
    {
        HideCountdown();
        TxtLearnStatus.Text = $"Sinyal alındı: {sig.Description}";
        // One-shot signals never send a release; finish after a short grace period.
        _learnTimer?.Stop();
        _learnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _learnTimer.Tick += (_, _) => { _learnTimer!.Stop(); _rawInput?.CompleteLearn(); };
        _learnTimer.Start();
    }

    private void OnSignalLearned(LearnedSignal sig)
    {
        _learnTimer?.Stop();
        BtnLearnDpi.Content = "Öğren";
        _profile.LearnedSignals[MouseButton.DpiButton] = sig;
        ProfileConfig.Save(_profile, ConfigPath);
        _rawInput?.SetBindings(_profile.LearnedSignals);
        AddLog($"[Learn ✓] DPI tuşu kaydedildi: {sig.Description}");
        UpdateDpiStatus();
    }

    private void BtnForgetDpi_Click(object sender, RoutedEventArgs e)
    {
        _profile.LearnedSignals.Remove(MouseButton.DpiButton);
        ProfileConfig.Save(_profile, ConfigPath);
        _rawInput?.SetBindings(_profile.LearnedSignals);
        AddLog("[Learn] DPI tuşu silindi.");
        UpdateDpiStatus();
    }

    private void ChkVerboseHid_Changed(object sender, RoutedEventArgs e)
    {
        if (_rawInput != null) _rawInput.VerboseDiagnostics = ChkVerboseHid.IsChecked == true;
    }

    private void ApplyProfileToEngine()
    {
        _engine.ClearActions();
        _rulesList.Clear();

        _config.DoubleClickThresholdMs = _profile.DoubleClickThresholdMs;
        _config.LongPressThresholdMs = _profile.LongPressThresholdMs;

        foreach (var rule in _profile.Rules)
        {
            var action = rule.CreateAction();
            if (action != null)
            {
                _engine.RegisterAction(rule.Button, rule.Cadence, action, rule.ChordSecondary, rule.Layer);
                _rulesList.Add(CreateDisplayItem(rule));
            }
        }

        RuleCountText.Text = $"{_rulesList.Count} kural";
        EmptyRulesPanel.Visibility = _rulesList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rawInput?.SetBindings(_profile.LearnedSignals);
    }

    public static string ButtonDisplayName(MouseButton b) => b switch
    {
        MouseButton.XButton1 => "Yan Tuş 1 (Geri)",
        MouseButton.XButton2 => "Yan Tuş 2 (İleri)",
        MouseButton.Middle => "Orta Tuş",
        MouseButton.Right => "Sağ Tuş",
        MouseButton.Left => "Sol Tuş",
        MouseButton.WheelUp => "Tekerlek Yukarı",
        MouseButton.WheelDown => "Tekerlek Aşağı",
        MouseButton.WheelTiltLeft => "Tekerlek Sola",
        MouseButton.WheelTiltRight => "Tekerlek Sağa",
        MouseButton.DpiButton => "DPI Tuşu",
        _ => b.ToString()
    };

    private static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 0xFF)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    private static RuleDisplayItem CreateDisplayItem(MappingRuleDto rule)
    {
        var icon = rule.Button switch
        {
            MouseButton.XButton1 => SymbolRegular.ArrowCircleLeft24,
            MouseButton.XButton2 => SymbolRegular.ArrowCircleRight24,
            MouseButton.WheelUp => SymbolRegular.ArrowCircleUp24,
            MouseButton.WheelDown => SymbolRegular.ArrowCircleDown24,
            MouseButton.WheelTiltLeft or MouseButton.WheelTiltRight => SymbolRegular.ArrowBidirectionalLeftRight24,
            MouseButton.DpiButton => SymbolRegular.Gauge24,
            _ => SymbolRegular.CursorClick24
        };

        (string text, byte r, byte g, byte b) = rule.Cadence switch
        {
            CadenceType.SingleClick => ("Tek Tık", (byte)0x3B, (byte)0x8E, (byte)0xEA),
            CadenceType.DoubleClick => ("Çift Tık", (byte)0x8B, (byte)0x5C, (byte)0xF6),
            CadenceType.TripleClick => ("Üçlü Tık", (byte)0xD9, (byte)0x46, (byte)0xEF),
            CadenceType.LongPress => ("Uzun Basma", (byte)0xF5, (byte)0x9E, (byte)0x0B),
            CadenceType.PressAndHoldDown => ("Bas-Çek", (byte)0xF9, (byte)0x73, (byte)0x16),
            CadenceType.Chord => ($"+ {ButtonDisplayName(rule.ChordSecondary)}", (byte)0x10, (byte)0xB9, (byte)0x81),
            _ => (rule.Cadence.ToString(), (byte)0x94, (byte)0xA3, (byte)0xB8)
        };

        return new RuleDisplayItem
        {
            Dto = rule,
            ButtonName = ButtonDisplayName(rule.Button) + (rule.Layer > 0 ? $"  ·  Katman {rule.Layer}" : ""),
            CadenceDescription = text,
            ActionDisplay = !string.IsNullOrEmpty(rule.DisplayName) ? rule.DisplayName : $"{rule.ActionType}: {rule.Parameter}",
            Icon = icon,
            Accent = Rgb(r, g, b),
            AccentSoft = Rgb(r, g, b, 0x26)
        };
    }

    private void OnMouseEventReceived(RawMouseEvent ev)
    {
        _lastHookEventTimes[ev.Button] = Environment.TickCount64;

        Dispatcher.BeginInvoke(() =>
        {
            TxtDetectedEvent.Text = $"{ev.Button} {ev.EventType}";
            HighlightVisualButton(ev.Button, ev.EventType is MouseEventType.Down or MouseEventType.Wheel or MouseEventType.HWheel);

            if (ev.EventType is MouseEventType.Down or MouseEventType.Wheel or MouseEventType.HWheel)
            {
                if (ev.EventType == MouseEventType.Down) AddLog($"{ButtonDisplayName(ev.Button)} basıldı");
                else AddLog($"[Hook] {ev.Button} {ev.EventType}", detail: true);
            }
        });
    }

    // Which fallback source currently "owns" a pressed button (so its UP is the one that releases it)
    private readonly Dictionary<MouseButton, RawSource> _fallbackOwner = new();
    private readonly Dictionary<MouseButton, long> _fallbackDownTime = new();

    /// <summary>
    /// Fusion of fallback sources (RawInput mouse / keyboard collection / Consumer Control / AppCommand).
    /// The low-level hook is authoritative; fallback events are only used when the hook did not see the button.
    /// Runs on the UI thread (WM_INPUT arrives through the window message pump).
    /// </summary>
    private void OnRawMouseEventReceived(RawMouseEvent ev, RawSource source)
    {
        // Learned vendor buttons (DPI) are never seen by WH_MOUSE_LL: feed them straight to the engine.
        if (ev.Button == MouseButton.DpiButton)
        {
            TxtDetectedEvent.Text = $"DpiButton {ev.EventType}";
            HighlightVisualButton(ev.Button, ev.EventType == MouseEventType.Down);
            if (ev.EventType == MouseEventType.Down) AddLog("DPI Tuşu basıldı");
            _engine.StateMachine.ProcessEvent(ev with { TimestampMs = Environment.TickCount64 });
            return;
        }

        // Only side buttons need fallback; L/R/M/wheel are always seen by WH_MOUSE_LL.
        if (ev.Button is not (MouseButton.XButton1 or MouseButton.XButton2)) return;

        // Give the hook thread a moment to report the same physical press, then decide.
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(35) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyFallbackEvent(ev, source);
        };
        timer.Start();
    }

    private void ApplyFallbackEvent(RawMouseEvent ev, RawSource source)
    {
        long now = Environment.TickCount64;
        bool hookSawIt = _lastHookEventTimes.TryGetValue(ev.Button, out long hookTime) && now - hookTime < 300;
        if (hookSawIt) return;

        if (ev.EventType == MouseEventType.Down)
        {
            // Another fallback source already delivered this press
            if (_fallbackOwner.ContainsKey(ev.Button)) return;
            if (_fallbackDownTime.TryGetValue(ev.Button, out long last) && now - last < 250 && source == RawSource.AppCommand) return;

            _fallbackOwner[ev.Button] = source;
            _fallbackDownTime[ev.Button] = now;
        }
        else if (ev.EventType == MouseEventType.Up)
        {
            if (!_fallbackOwner.TryGetValue(ev.Button, out var owner) || owner != source) return;
            _fallbackOwner.Remove(ev.Button);
        }

        string srcName = source switch
        {
            RawSource.ConsumerControl => "Consumer Control",
            RawSource.Keyboard => "HID Keyboard",
            RawSource.AppCommand => "AppCommand",
            _ => "RawInput"
        };

        TxtDetectedEvent.Text = $"{ev.Button} {ev.EventType} ({srcName})";
        HighlightVisualButton(ev.Button, ev.EventType == MouseEventType.Down);
        if (ev.EventType == MouseEventType.Down)
        {
            AddLog($"{ButtonDisplayName(ev.Button)} basıldı ({srcName})");
        }

        var adjusted = ev with { TimestampMs = now };
        _engine.StateMachine.ProcessEvent(adjusted);
    }

    private void HighlightVisualButton(MouseButton button, bool isDown)
    {
        MouseVisual3D?.SetButtonState(button, isDown);
    }

    private void OnTriggerFired(GestureTrigger trigger)
    {
        Dispatcher.BeginInvoke(() =>
        {
            string cadenceStr = trigger.Cadence switch
            {
                CadenceType.SingleClick => "Tek Tık",
                CadenceType.DoubleClick => "Çift Tık",
                CadenceType.TripleClick => "Üçlü Tık",
                CadenceType.LongPress => "Uzun Basma",
                CadenceType.PressAndHoldDown => "Bas-Çek",
                CadenceType.Chord => $"{ButtonDisplayName(trigger.Button)} + {ButtonDisplayName(trigger.ChordSecondary)}",
                _ => trigger.Cadence.ToString()
            };

            TxtDetectedCadence.Text = $"{ButtonDisplayName(trigger.Button)} · {cadenceStr}";
            AddLog($"▶ {ButtonDisplayName(trigger.Button)} · {cadenceStr}");
        });
    }

    // ==========================================
    // LIVE DRAG & DROP REORDERING (SortableJS / Fluent "reorder" pattern)
    // - The grabbed card lifts (shadow + slight scale) and follows the cursor 1:1.
    // - Other cards smoothly slide out of the way (TranslateTransform + easing) to open a gap.
    // - On release the card glides into the gap, then the collection is reordered once.
    // ==========================================

    private bool _dragPending;
    private bool _isDragging;
    private bool _dropAnimating;
    private int _dragIndex = -1;
    private int _dragTarget = -1;
    private ListBoxItem? _dragContainer;
    private TranslateTransform? _dragTranslate;
    private ScaleTransform? _dragScale;
    private readonly List<ListBoxItem> _dragContainers = new();
    private readonly List<double> _dragTops = new();
    private readonly List<double> _dragHeights = new();

    private static readonly IEasingFunction SlideEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void RulesListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_dropAnimating || _isDragging) return;

        var src = e.OriginalSource as DependencyObject;
        // Let edit/delete buttons work normally
        if (FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(src) != null) return;

        var container = FindVisualParent<ListBoxItem>(src);
        if (container == null) return;

        _dragContainer = container;
        _draggedItem = container.DataContext as RuleDisplayItem;
        _dragIndex = RulesListBox.ItemContainerGenerator.IndexFromContainer(container);
        _dragStartPoint = e.GetPosition(RulesListBox);
        _dragPending = _dragIndex >= 0;
        if (_dragPending)
        {
            RulesListBox.CaptureMouse();
            e.Handled = true;
        }
    }

    private void RulesListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending && !_isDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        Point pos = e.GetPosition(RulesListBox);

        if (_dragPending && !_isDragging)
        {
            if (Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance &&
                Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance)
                return;
            BeginDrag();
        }

        if (_isDragging) UpdateDrag(pos);
    }

    private void RulesListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragPending || _isDragging)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    private void RulesListBox_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragPending || _isDragging) EndDrag();
    }

    private void BeginDrag()
    {
        _dragContainers.Clear();
        _dragTops.Clear();
        _dragHeights.Clear();

        for (int i = 0; i < _rulesList.Count; i++)
        {
            if (RulesListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem c)
            {
                // Container not realised; abort gracefully
                _dragPending = false;
                RulesListBox.ReleaseMouseCapture();
                return;
            }
            _dragContainers.Add(c);
            _dragTops.Add(c.TranslatePoint(new Point(0, 0), RulesListBox).Y);
            _dragHeights.Add(c.ActualHeight);
            c.RenderTransform = new TranslateTransform();
        }

        // Lift the grabbed card
        _dragTranslate = new TranslateTransform();
        _dragScale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(_dragScale);
        group.Children.Add(_dragTranslate);
        _dragContainer!.RenderTransformOrigin = new Point(0.5, 0.5);
        _dragContainer.RenderTransform = group;
        Panel.SetZIndex(_dragContainer, 100);
        _dragContainer.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 28, ShadowDepth = 8, Opacity = 0.55, Color = Colors.Black, Direction = 270
        };
        var lift = new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(120)) { EasingFunction = SlideEase };
        _dragScale.BeginAnimation(ScaleTransform.ScaleXProperty, lift);
        _dragScale.BeginAnimation(ScaleTransform.ScaleYProperty, lift);

        _dragTarget = _dragIndex;
        _dragPending = false;
        _isDragging = true;
    }

    private void UpdateDrag(Point pos)
    {
        int idx = _dragIndex;
        int last = _dragContainers.Count - 1;
        double dy = pos.Y - _dragStartPoint.Y;

        // Let the card travel a little past the ends so the first/last slot is always reachable
        double slack = _dragHeights[idx] * 0.6;
        double minDy = _dragTops[0] - _dragTops[idx] - slack;
        double maxDy = (_dragTops[last] + _dragHeights[last]) - (_dragTops[idx] + _dragHeights[idx]) + slack;
        dy = Math.Clamp(dy, minDy, maxDy);
        _dragTranslate!.Y = dy;

        AutoScrollWhileDragging(pos);

        // Target slot = where the dragged card's centre is; crossing a neighbour's midpoint moves the gap (>= / <=)
        double center = _dragTops[idx] + _dragHeights[idx] / 2 + dy;
        int target = idx;
        for (int i = idx + 1; i <= last; i++)
            if (center >= _dragTops[i] + _dragHeights[i] / 2) target = i;
        for (int i = idx - 1; i >= 0; i--)
            if (center <= _dragTops[i] + _dragHeights[i] / 2) target = i;

        if (target == _dragTarget) return;
        _dragTarget = target;

        // Slide the other cards to open a gap at the target slot
        double gap = _dragHeights[idx];
        for (int i = 0; i <= last; i++)
        {
            if (i == idx) continue;
            double offset = 0;
            if (idx < target && i > idx && i <= target) offset = -gap;
            else if (idx > target && i >= target && i < idx) offset = gap;

            if (_dragContainers[i].RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(offset, TimeSpan.FromMilliseconds(180)) { EasingFunction = SlideEase });
            }
        }
    }

    private ScrollViewer? _rulesScroll;

    private void AutoScrollWhileDragging(Point pos)
    {
        _rulesScroll ??= FindVisualChild<ScrollViewer>(RulesListBox);
        if (_rulesScroll == null || _rulesScroll.ScrollableHeight <= 0) return;
        const double edge = 36;
        double before = _rulesScroll.VerticalOffset;
        if (pos.Y < edge) _rulesScroll.ScrollToVerticalOffset(before - 8);
        else if (pos.Y > RulesListBox.ActualHeight - edge) _rulesScroll.ScrollToVerticalOffset(before + 8);
        double moved = _rulesScroll.VerticalOffset - before;
        if (moved != 0)
        {
            // Content moved under the cursor: keep cached slot positions in sync
            for (int i = 0; i < _dragTops.Count; i++) _dragTops[i] -= moved;
            _dragStartPoint.Y -= moved;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindVisualChild<T>(child) is T found) return found;
        }
        return null;
    }

    private void EndDrag()
    {
        if (_dragPending && !_isDragging)
        {
            _dragPending = false;
            if (RulesListBox.IsMouseCaptured) RulesListBox.ReleaseMouseCapture();
            return;
        }
        if (!_isDragging) return;

        _isDragging = false;
        _dropAnimating = true;
        if (RulesListBox.IsMouseCaptured) RulesListBox.ReleaseMouseCapture();

        int idx = _dragIndex, target = _dragTarget;
        double finalY = 0;
        if (target > idx) for (int i = idx + 1; i <= target; i++) finalY += _dragHeights[i];
        else if (target < idx) for (int i = target; i < idx; i++) finalY -= _dragHeights[i];

        var settle = new DoubleAnimation(finalY, TimeSpan.FromMilliseconds(170)) { EasingFunction = SlideEase };
        var unscale = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(170)) { EasingFunction = SlideEase };
        settle.Completed += (_, _) => CommitReorder(idx, target);
        _dragScale?.BeginAnimation(ScaleTransform.ScaleXProperty, unscale);
        _dragScale?.BeginAnimation(ScaleTransform.ScaleYProperty, unscale);
        _dragTranslate!.BeginAnimation(TranslateTransform.YProperty, settle);
    }

    private void CommitReorder(int oldIndex, int newIndex)
    {
        // Reset visuals in the same frame as the collection move -> no flicker
        foreach (var c in _dragContainers)
        {
            c.RenderTransform = Transform.Identity;
            c.Effect = null;
            Panel.SetZIndex(c, 0);
        }

        if (oldIndex != newIndex && oldIndex >= 0 && newIndex >= 0)
        {
            var moved = _rulesList[oldIndex];
            _rulesList.Move(oldIndex, newIndex);

            // Rebuild profile order from the visible list (keeps rules without UI item at the end)
            var ordered = _rulesList.Select(r => r.Dto).ToList();
            ordered.AddRange(_profile.Rules.Where(r => !ordered.Contains(r)));
            _profile.Rules.Clear();
            _profile.Rules.AddRange(ordered);

            ProfileConfig.Save(_profile, ConfigPath);
            AddLog($"[Rules] '{moved.ButtonName}' {oldIndex + 1} → {newIndex + 1}");
        }

        _dragContainers.Clear();
        _dragContainer = null;
        _draggedItem = null;
        _dragTranslate = null;
        _dragScale = null;
        _dropAnimating = false;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    // ==========================================
    // ADD & EDIT RULE IMPLEMENTATION
    // ==========================================

    private void BtnToggleAddRule_Click(object sender, RoutedEventArgs e)
    {
        _editingRule = null;
        AddRuleTitle.Text = "Yeni Tuş Eşlemesi";
        AddRuleCard.Visibility = AddRuleCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ValidateEditor();
    }

    private void BtnCancelAddRule_Click(object sender, RoutedEventArgs e)
    {
        _editingRule = null;
        AddRuleCard.Visibility = Visibility.Collapsed;
    }

    private void BtnEditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is RuleDisplayItem item)
        {
            _editingRule = item.Dto;
            AddRuleTitle.Text = $"Kuralı Düzenle — {item.ButtonName}";

            // Select Target Button
            SelectComboByTag(CmbTargetButton, item.Dto.Button.ToString());

            // Select Cadence
            SelectComboByTag(CmbCadence, item.Dto.Cadence.ToString());

            // Select Action Type
            SelectComboByTag(CmbActionType, item.Dto.ActionType);

            // Populate Parameter
            if (item.Dto.ActionType == "Media")
            {
                SelectComboByTag(CmbMediaAction, item.Dto.Parameter);
            }
            else
            {
                TxtActionParam.Text = item.Dto.Parameter;
            }

            // Populate Chord Secondary if applicable
            if (item.Dto.Cadence == CadenceType.Chord)
            {
                SelectComboByTag(CmbChordSecondary, item.Dto.ChordSecondary.ToString());
            }

            AddRuleCard.Visibility = Visibility.Visible;
            ValidateEditor();
        }
    }

    private static void SelectComboByTag(ComboBox cmb, string tag)
    {
        foreach (var obj in cmb.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedItem = item;
                break;
            }
        }
    }

    private void CmbCadence_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChordSecondaryPanel == null) return;
        var item = CmbCadence.SelectedItem as ComboBoxItem;
        string tag = item?.Tag?.ToString() ?? "";
        ChordSecondaryPanel.Visibility = tag == "Chord" ? Visibility.Visible : Visibility.Collapsed;
        ValidateEditor();
    }

    private void Editor_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateEditor();
    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => ValidateEditor();

    /// <summary>Builds a rule from the editor fields (null if incomplete).</summary>
    private MappingRuleDto? BuildCandidateRule()
    {
        if (CmbTargetButton?.SelectedItem is not ComboBoxItem btnItem ||
            CmbCadence?.SelectedItem is not ComboBoxItem cadenceItem ||
            CmbActionType?.SelectedItem is not ComboBoxItem actionItem) return null;
        if (!Enum.TryParse<MouseButton>(btnItem.Tag?.ToString(), out var targetBtn)) return null;
        if (!Enum.TryParse<CadenceType>(cadenceItem.Tag?.ToString(), out var cadence)) return null;

        string actionType = actionItem.Tag?.ToString() ?? "Hotkey";
        string parameter, displayName;
        if (actionType == "Media")
        {
            var mediaItem = CmbMediaAction.SelectedItem as ComboBoxItem;
            parameter = mediaItem?.Tag?.ToString() ?? "VolumeUp";
            displayName = mediaItem?.Content?.ToString() ?? parameter;
        }
        else
        {
            parameter = TxtActionParam?.Text.Trim() ?? "";
            displayName = actionType switch
            {
                "Hotkey" => $"Kısayol: {parameter}",
                "Text" => $"Metin: {parameter}",
                "Launch" => $"Aç: {parameter}",
                "Mouse" => $"Fare: {parameter}",
                _ => $"{actionType}: {parameter}"
            };
        }

        MouseButton chordSecondary = MouseButton.None;
        if (cadence == CadenceType.Chord)
            Enum.TryParse((CmbChordSecondary.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out chordSecondary);

        return new MappingRuleDto
        {
            Button = targetBtn,
            Cadence = cadence,
            ChordSecondary = chordSecondary,
            Layer = _editingRule?.Layer ?? 0,
            ActionType = actionType,
            Parameter = parameter,
            DisplayName = displayName
        };
    }

    /// <summary>Live check: a button + cadence may carry only one action. Disables Save on conflict.</summary>
    private RuleConflict? ValidateEditor()
    {
        if (ConflictInfo == null || BtnSaveRule == null || DpiHintInfo == null) return null;
        var candidate = BuildCandidateRule();
        RuleConflict? conflict = candidate == null ? null : RuleConflictChecker.FindConflict(_profile.Rules, candidate, _editingRule);

        if (conflict != null)
        {
            var ex = conflict.Existing;
            ConflictInfo.Message = $"{conflict.Reason} Mevcut: {ButtonDisplayName(ex.Button)} · {CreateDisplayItem(ex).CadenceDescription} → " +
                                   $"{(string.IsNullOrEmpty(ex.DisplayName) ? ex.Parameter : ex.DisplayName)}";
        }
        ConflictInfo.IsOpen = conflict != null;
        BtnSaveRule.IsEnabled = conflict == null;
        DpiHintInfo.IsOpen = candidate?.Button == MouseButton.DpiButton && !_profile.LearnedSignals.ContainsKey(MouseButton.DpiButton);
        return conflict;
    }

    private void CmbActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtActionParam == null || CmbMediaAction == null) return;
        var item = CmbActionType.SelectedItem as ComboBoxItem;
        string tag = item?.Tag?.ToString() ?? "";

        if (tag == "Media")
        {
            TxtActionParam.Visibility = Visibility.Collapsed;
            CmbMediaAction.Visibility = Visibility.Visible;
            ActionParamLabel.Text = "4 · Medya komutu";
        }
        else
        {
            TxtActionParam.Visibility = Visibility.Visible;
            CmbMediaAction.Visibility = Visibility.Collapsed;
            ActionParamLabel.Text = tag switch
            {
                "Hotkey" => "4 · Kısayol (Ctrl+C)",
                "Text" => "4 · Yazılacak metin",
                "Launch" => "4 · Program / URL",
                "Mouse" => "4 · Fare tuşu (Left/Right/Middle)",
                _ => "4 · Parametre"
            };
        }
        ValidateEditor();
    }

    private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
    {
        var newRule = BuildCandidateRule();
        if (newRule == null) return;

        if (newRule.ActionType != "Media" && string.IsNullOrEmpty(newRule.Parameter))
        {
            System.Windows.MessageBox.Show("Eylem değerini girin (örn. Ctrl+C).", "Eksik bilgi", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ValidateEditor() != null) return; // conflict: never overwrite silently

        if (_editingRule != null)
        {
            int idx = _profile.Rules.IndexOf(_editingRule);
            if (idx >= 0) _profile.Rules[idx] = newRule;
            else _profile.Rules.Add(newRule);
            _editingRule = null;
        }
        else
        {
            _profile.Rules.Add(newRule);
        }

        ProfileConfig.Save(_profile, ConfigPath);
        ApplyProfileToEngine();

        AddRuleCard.Visibility = Visibility.Collapsed;
        AddLog($"[Rules] Kaydedildi: {ButtonDisplayName(newRule.Button)} {newRule.Cadence} → {newRule.DisplayName}");
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is RuleDisplayItem item)
        {
            _profile.Rules.Remove(item.Dto);
            ProfileConfig.Save(_profile, ConfigPath);
            ApplyProfileToEngine();
            AddLog($"[Rules] Silindi: {item.ButtonName}");
        }
    }

    private void SliderDoubleClick_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        if (TxtDoubleClickVal != null) TxtDoubleClickVal.Text = $"{val} ms";
        if (_profile != null)
        {
            _profile.DoubleClickThresholdMs = val;
            _config.DoubleClickThresholdMs = val;
            ProfileConfig.Save(_profile, ConfigPath);
        }
    }

    private void SliderLongPress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        if (TxtLongPressVal != null) TxtLongPressVal.Text = $"{val} ms";
        if (_profile != null)
        {
            _profile.LongPressThresholdMs = val;
            _config.LongPressThresholdMs = val;
            ProfileConfig.Save(_profile, ConfigPath);
        }
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var res = System.Windows.MessageBox.Show("Tüm kurallar silinip varsayılanlar yüklenecek. Emin misiniz?", "Sıfırla", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == System.Windows.MessageBoxResult.Yes)
        {
            _profile = ProfileConfig.CreateDefault();
            ProfileConfig.Save(_profile, ConfigPath);
            ApplyProfileToEngine();
            UpdateDpiStatus();
            SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
            SliderLongPress.Value = _profile.LongPressThresholdMs;
            AddLog("[System] Varsayılana sıfırlandı.");
        }
    }

    private void BtnOpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(ConfigPath) ?? "";
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _eventLogs.Clear();
    }

    private void EngineToggle_Checked(object sender, RoutedEventArgs e)
    {
        _engine?.Resume();
        MouseVisual3D?.SetEngineActive(true);
        if (StatusDot != null && StatusBadgeText != null)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x46, 0xC1, 0x5E));
            StatusBadgeText.Text = "Motor aktif";
            StatusSubText.Text = "Hook aktif";
            UpdateTrayState(true);
            AddLog("[System] Motor açık.");
        }
    }

    private void EngineToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _engine?.Pause();
        MouseVisual3D?.SetEngineActive(false);
        if (StatusDot != null && StatusBadgeText != null)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
            StatusBadgeText.Text = "Duraklatıldı";
            StatusSubText.Text = "Hook pasif";
            UpdateTrayState(false);
            AddLog("[System] Motor duraklatıldı.");
        }
    }

    private static string StripTag(string msg) =>
        msg.StartsWith('[') && msg.IndexOf("] ", StringComparison.Ordinal) is int k and > 0 ? msg[(k + 2)..] : msg;

    private void BtnToggleLog_Changed(object sender, RoutedEventArgs e)
    {
        if (LogCard != null) LogCard.Visibility = BtnToggleLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperMouse", "diag.log");

    /// <summary>
    /// Adds a line to the event log. <paramref name="detail"/> lines (Hook / RawInput / HID internals) are
    /// always written to diag.log but only shown in the UI when "Ayrıntılı" is checked.
    /// </summary>
    private void AddLog(string msg, bool detail = false)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        if (!detail || ChkVerboseHid?.IsChecked == true)
        {
            _eventLogs.Insert(0, detail ? line : $"{DateTime.Now:HH:mm:ss}  {StripTag(msg)}");
            if (_eventLogs.Count > 200) _eventLogs.RemoveAt(_eventLogs.Count - 1);
        }

        // Persist diagnostics so hardware behaviour can be analysed after a test session
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagLogPath)!);
            File.AppendAllText(DiagLogPath, line + Environment.NewLine);
        }
        catch { /* diagnostics must never crash the app */ }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _profile.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.ShowBalloonTip(2500, "HyperMouse arka planda", "Tepsideki simgeden açabilir veya kapatabilirsiniz.", System.Windows.Forms.ToolTipIcon.None);
            }
            return;
        }

        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _learnTimer?.Stop();
        _rawInput?.Dispose();
        _engine?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
