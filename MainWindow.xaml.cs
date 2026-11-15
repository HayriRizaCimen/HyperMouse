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
    private readonly ProfileManager _profileManager;
    private ProfileConfig _profile => _profileManager?.CurrentProfile ?? ProfileConfig.CreateDefault();

    private readonly ThreadedMouseHook _mouseHook;
    private readonly Win32InputSimulator _inputSimulator;
    private readonly HyperMouseEngine _engine;
    private readonly StateMachineConfig _config;
    private RawInputReceiver? _rawInput;

    private readonly ObservableCollection<RuleDisplayItem> _rulesList = new();
    private readonly ObservableCollection<string> _eventLogs = new();

    private MappingRuleDto? _editingRule;
    private Point _dragStartPoint;
    private RuleDisplayItem? _draggedItem;
    private readonly ConcurrentDictionary<MouseButton, long> _lastHookEventTimes = new();

    private DispatcherTimer? _learnTimer;
    private DispatcherTimer? _deviceRefreshTimer;

    private bool _isRenamingProfile;
    private string _profileToRename = "";
    private string _profileToDelete = "";
    private bool _profileComboUpdating;

    private DispatcherTimer CreateRefreshTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        t.Tick += (_, _) => { t.Stop(); _rawInput?.RefreshDevices(); };
        return t;
    }
    private readonly ObservableCollection<MouseDevice> _miceView = new();

    public MainWindow()
    {
        _config = new StateMachineConfig();
        _mouseHook = new ThreadedMouseHook();
        _inputSimulator = new Win32InputSimulator();
        _engine = new HyperMouseEngine(_mouseHook, _inputSimulator, null, _config);

        // Initialize ProfileManager before InitializeComponent so any early control events have a valid profile
        _profileManager = new ProfileManager();
        _profileManager.ActiveProfileChanged += _ => Dispatcher.Invoke(() => { ApplyProfileToEngine(); RefreshProfileCombo(); });
        _profileManager.LanguageChanged += lang => Dispatcher.Invoke(() => ApplyLanguage(lang));

        InitializeComponent();

        RulesListBox.ItemsSource = _rulesList;
        LiveEventListBox.ItemsSource = _eventLogs;
        MiceList.ItemsSource = _miceView;

        ApplyProfileToEngine();

        // Subscribe to hook and engine
        _mouseHook.MouseEventReceived += OnMouseEventReceived;
        _engine.StateMachine.TriggerFired += OnTriggerFired;
        _engine.QuickDisable.ToggleRequested += OnQuickDisableToggleRequested;

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

    private void SaveProfile() => _profileManager?.SaveCurrent();

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
        RefreshProfileCombo();
        LoadAppSettings();
        ApplyLanguage(_profileManager.Language);
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
    private bool _settingsLoading = true;
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
        string lang = LocalizationService.CurrentLanguage;
        menu.Items.Add(LocalizationService.Get("Tray_Show", lang), null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _trayToggleItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationService.Get("Tray_Pause", lang), null, (_, _) => Dispatcher.Invoke(() => EngineToggle.IsChecked = !(EngineToggle.IsChecked == true)));
        menu.Items.Add(_trayToggleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("Tray_Exit", lang), null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ShowFromTray); };
    }

    private void UpdateTrayState(bool active)
    {
        if (_tray == null) return;
        string lang = LocalizationService.CurrentLanguage;
        _tray.Text = active ? LocalizationService.Get("Tray_ActiveText", lang) : LocalizationService.Get("Tray_PausedText", lang);
        if (_trayToggleItem != null) _trayToggleItem.Text = active ? LocalizationService.Get("Tray_Pause", lang) : LocalizationService.Get("Tray_Resume", lang);
    }

    private void UpdateTrayMenuLanguage(string lang)
    {
        if (_tray?.ContextMenuStrip == null) return;
        var items = _tray.ContextMenuStrip.Items;
        if (items.Count >= 4)
        {
            items[0].Text = LocalizationService.Get("Tray_Show", lang);
            if (_trayToggleItem != null)
            {
                bool active = EngineToggle.IsChecked == true;
                _trayToggleItem.Text = active ? LocalizationService.Get("Tray_Pause", lang) : LocalizationService.Get("Tray_Resume", lang);
            }
            items[3].Text = LocalizationService.Get("Tray_Exit", lang);
        }
        UpdateTrayState(EngineToggle.IsChecked == true);
    }

    private void UpdateEngineStatusText(bool active)
    {
        string lang = LocalizationService.CurrentLanguage;
        if (StatusDot != null && StatusBadgeText != null && StatusSubText != null)
        {
            if (active)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x46, 0xC1, 0x5E));
                StatusBadgeText.Text = LocalizationService.Get("Status_EngineActive", lang);
                StatusSubText.Text = LocalizationService.Get("Status_HookActive", lang);
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
                StatusBadgeText.Text = LocalizationService.Get("Status_EnginePaused", lang);
                StatusSubText.Text = LocalizationService.Get("Status_HookPaused", lang);
            }
        }
        if (TxtToggleShortcuts != null)
        {
            TxtToggleShortcuts.Text = LocalizationService.Get("Status_ShortcutHint", lang);
        }
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
            _tray?.ShowBalloonTip(1500, "HyperMouse", active ? LocalizationService.Get("Tray_EngineStarted") : LocalizationService.Get("Tray_EnginePaused"), System.Windows.Forms.ToolTipIcon.None);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void LoadAppSettings()
    {
        _settingsLoading = true;
        SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
        SliderLongPress.Value = _profile.LongPressThresholdMs;
        SelectComboByTag(CmbLanguage, _profileManager.Language);
        SelectComboByTag(CmbTheme, _profile.Theme);
        ToggleTray.IsChecked = _profile.MinimizeToTray;
        ToggleAutostart.IsChecked = IsAutostartEnabled();
        ToggleQuickDisable.IsChecked = _profile.QuickDisableEnabled;
        SelectComboByTag(CmbQuickDisableButton, _profile.QuickDisableButton.ToString());
        SelectComboByTag(CmbQuickDisableCadence, _profile.QuickDisableCadence.ToString());
        QuickDisableOptionsPanel.Visibility = _profile.QuickDisableEnabled ? Visibility.Visible : Visibility.Collapsed;
        _settingsLoading = false;
        ApplyTheme(_profile.Theme);
    }

    private void ApplyLanguage(string langCode)
    {
        LocalizationService.CurrentLanguage = langCode;
        AppLanguage lang = LocalizationService.CurrentAppLanguage;

        // Declarative UI text bindings (TextBlock -> L10nKey)
        (System.Windows.Controls.TextBlock? Element, L10nKey Key)[] textBindings =
        [
            (TxtAppSubtitle, L10nKey.App_Subtitle),
            (TxtNavKeyMap, L10nKey.Nav_KeyMap),
            (TxtNavLive, L10nKey.Nav_LiveTest),
            (TxtNavSettings, L10nKey.Nav_Settings),
            (TxtHeroTitle, L10nKey.Hero_Title),
            (TxtPageRulesTitle, L10nKey.Rules_Title),
            (TxtPageRulesSubtitle, L10nKey.Rules_Subtitle),
            (TxtDpiTitle, L10nKey.Rules_DpiTitle),
            (TxtEmptyRulesTitle, L10nKey.Rules_EmptyTitle),
            (TxtEmptyRulesSubtitle, L10nKey.Rules_EmptySubtitle),
            (TxtLiveTitle, L10nKey.Live_Title),
            (TxtLiveSubtitle, L10nKey.Live_Subtitle),
            (TxtLiveBtnLog, L10nKey.Live_BtnLog),
            (TxtLiveLastEventTitle, L10nKey.Live_LastEvent),
            (TxtLiveTriggeredCadenceTitle, L10nKey.Live_TriggeredCadence),
            (TxtLiveLogTitle, L10nKey.Live_BtnLog),
            (TxtSettingsTitle, L10nKey.Settings_Title),
            (TxtSettingsCadenceTitle, L10nKey.Settings_CadenceTitle),
            (TxtSettingsCadenceSubtitle, L10nKey.Settings_CadenceSubtitle),
            (TxtSettingsDoubleClick, L10nKey.Settings_DoubleClick),
            (TxtSettingsLongPress, L10nKey.Settings_LongPress),
            (TxtSettingsQuickDisableTitle, L10nKey.Settings_QuickDisableTitle),
            (TxtSettingsQuickDisableSubtitle, L10nKey.Settings_QuickDisableSubtitle),
            (TxtSettingsTriggerButton, L10nKey.Settings_TriggerButton),
            (TxtSettingsTriggerCadence, L10nKey.Settings_TriggerCadence),
            (TxtSettingsConnectedMiceTitle, L10nKey.Settings_ConnectedMiceTitle),
            (TxtSettingsConnectedMiceSubtitle, L10nKey.Settings_ConnectedMiceSubtitle),
            (TxtSettingsAppTitle, L10nKey.Settings_AppTitle),
            (TxtSettingsLanguageTitle, L10nKey.Settings_LanguageTitle),
            (TxtSettingsLanguageSubtitle, L10nKey.Settings_LanguageSubtitle),
            (TxtSettingsThemeTitle, L10nKey.Settings_ThemeTitle),
            (TxtSettingsThemeSubtitle, L10nKey.Settings_ThemeSubtitle),
            (TxtSettingsAutostartTitle, L10nKey.Settings_AutostartTitle),
            (TxtSettingsAutostartSubtitle, L10nKey.Settings_AutostartSubtitle),
            (TxtSettingsTrayTitle, L10nKey.Settings_TrayTitle),
            (TxtSettingsTraySubtitle, L10nKey.Settings_TraySubtitle),
            (TxtSettingsHotkeyTitle, L10nKey.Settings_HotkeyTitle),
            (TxtHotkeyStatus, L10nKey.Settings_HotkeySubtitle),
            (TxtSettingsProfileTitle, L10nKey.Settings_ProfileTitle),
            (TxtSettingsProfileSubtitle, L10nKey.Settings_ProfileSubtitle),
            (TxtBtnExport, L10nKey.Settings_BtnExport),
            (TxtBtnImport, L10nKey.Settings_BtnImport),
            (TxtBtnResetDefaults, L10nKey.Settings_BtnResetDefaults),
            (LblModalStep1, L10nKey.Modal_Step1),
            (LblModalStep2, L10nKey.Modal_Step2),
            (LblModalStep3, L10nKey.Modal_Step3),
            (LblModalChordSecondary, L10nKey.Modal_ChordSecondary),
            (TxtDeleteConfirmTitle, L10nKey.DeleteModal_Title),
            (TxtDeleteConfirmSubtitle, L10nKey.DeleteModal_Subtitle),
            (TxtDeleteConfirmPrompt, L10nKey.DeleteModal_Message),
            (TxtProfileDeleteTitle, L10nKey.Profile_Delete_Title),
            (TxtProfileDeleteWarning, L10nKey.Profile_Delete_Warning)
        ];

        foreach (var (element, key) in textBindings)
        {
            if (element != null) element.Text = LocalizationService.Get(key, lang);
        }

        // Declarative ContentControl bindings (Button / CheckBox -> L10nKey)
        (ContentControl? Control, L10nKey Key)[] contentBindings =
        [
            (BtnToggleAddRule, L10nKey.Rules_BtnNewRule),
            (ChkVerboseHid, L10nKey.Live_Verbose),
            (BtnOpenConfigFolder, L10nKey.Settings_BtnOpenFolder),
            (BtnCancelAddRule, L10nKey.Modal_BtnCancel),
            (BtnSaveRule, L10nKey.Modal_BtnSave),
            (BtnCancelDelete, L10nKey.DeleteModal_BtnCancel),
            (BtnConfirmDelete, L10nKey.DeleteModal_BtnConfirm),
            (BtnProfilePromptCancel, L10nKey.Modal_BtnCancel),
            (BtnProfilePromptConfirm, L10nKey.Modal_BtnSave),
            (BtnProfileDeleteCancel, L10nKey.DeleteModal_BtnCancel),
            (BtnProfileDeleteConfirm, L10nKey.Profile_Delete)
        ];

        foreach (var (control, key) in contentBindings)
        {
            if (control != null) control.Content = LocalizationService.Get(key, lang);
        }

        // Declarative ToolTip bindings (FrameworkElement -> L10nKey)
        (FrameworkElement? Element, L10nKey Key)[] tooltipBindings =
        [
            (BtnExitApp, L10nKey.KillSwitch_Tooltip),
            (BtnRenameMouse, L10nKey.Hero_RenameTooltip),
            (BtnNewProfile, L10nKey.Profile_New),
            (BtnRenameProfile, L10nKey.Profile_Rename),
            (BtnDeleteProfile, L10nKey.Profile_Delete),
            (ChkVerboseHid, L10nKey.Live_VerboseTooltip),
            (BtnClearLog, L10nKey.Live_ClearLogTooltip)
        ];

        foreach (var (element, key) in tooltipBindings)
        {
            if (element != null) element.ToolTip = LocalizationService.Get(key, lang);
        }

        if (ConflictInfo != null) ConflictInfo.Title = LocalizationService.Get(L10nKey.Modal_ConflictTitle, lang);
        if (DpiHintInfo != null)
        {
            DpiHintInfo.Title = LocalizationService.Get(L10nKey.Modal_DpiWarningTitle, lang);
            DpiHintInfo.Message = LocalizationService.Get(L10nKey.Modal_DpiWarningMessage, lang);
        }

        UpdateEngineStatusText(EngineToggle.IsChecked == true);
        UpdateMouseInfo();
        UpdateDpiStatus();
        UpdateComboBoxContents(lang);
        UpdateTrayMenuLanguage(langCode);
        ApplyProfileToEngine();
    }

    private void UpdateComboBoxContents(AppLanguage lang)
    {
        if (CmbTargetButton == null) return;

        foreach (ComboBox cmb in new[] { CmbTargetButton, CmbChordSecondary, CmbQuickDisableButton })
        {
            foreach (var obj in cmb.Items)
            {
                if (obj is ComboBoxItem item && Enum.TryParse<MouseButton>(item.Tag?.ToString(), out var btn))
                    item.Content = LocalizationService.ButtonDisplayName(btn, lang);
            }
        }

        foreach (var obj in CmbCadence.Items)
        {
            if (obj is ComboBoxItem item && Enum.TryParse<CadenceType>(item.Tag?.ToString(), out var cad))
                item.Content = LocalizationService.CadenceDisplayName(cad, lang);
        }

        foreach (var obj in CmbActionType.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag)
                item.Content = LocalizationService.ActionTypeDisplayName(tag, lang);
        }

        foreach (var obj in CmbMediaAction.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag)
                item.Content = LocalizationService.MediaCommandDisplayName(tag, lang);
        }

        foreach (var obj in CmbQuickDisableCadence.Items)
        {
            if (obj is ComboBoxItem item && Enum.TryParse<QuickDisableCadence>(item.Tag?.ToString(), out var cad))
                item.Content = LocalizationService.QuickDisableCadenceDisplayName(cad, lang);
        }

        foreach (var obj in CmbTheme.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag)
            {
                item.Content = tag switch
                {
                    "System" => LocalizationService.Get(L10nKey.Settings_ThemeSystem, lang),
                    "Dark" => LocalizationService.Get(L10nKey.Settings_ThemeDark, lang),
                    "Light" => LocalizationService.Get(L10nKey.Settings_ThemeLight, lang),
                    _ => tag
                };
            }
        }
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading) return;
        string lang = (CmbLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "tr";
        _profileManager.SetLanguage(lang);
        ApplyLanguage(lang);
    }

    private void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "Light": ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Acrylic); break;
            case "Dark": ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Acrylic); break;
            default: ApplicationThemeManager.ApplySystemTheme(); break;
        }

        bool isDark = theme switch
        {
            "Light" => false,
            "Dark" => true,
            _ => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
        };

        UpdateGlassTheme(isDark);
    }

    private void UpdateGlassTheme(bool isDark)
    {
        if (isDark)
        {
            Application.Current.Resources["GlassCardBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["GlassCardBorderBrush"] = new LinearGradientBrush(
                Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF),
                new Point(0, 0), new Point(0, 1));

            AmbientBackdrop.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0xEA, 0x0B, 0x0D, 0x14), 0.0),
                    new(Color.FromArgb(0xEA, 0x10, 0x14, 0x24), 0.5),
                    new(Color.FromArgb(0xEA, 0x15, 0x19, 0x2B), 1.0)
                },
                new Point(0, 0), new Point(1, 1));

            KillSwitchIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xAA, 0xFF));
            KillSwitchText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xD9, 0xFF));
            RuleModalOverlay.Background = new SolidColorBrush(Color.FromArgb(0x88, 0x05, 0x07, 0x10));
            AddRuleCard.Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x13, 0x17, 0x28));
            DeleteConfirmModalOverlay.Background = RuleModalOverlay.Background;
            DeleteConfirmCard.Background = AddRuleCard.Background;
            ProfilePromptModalOverlay.Background = RuleModalOverlay.Background;
            ProfilePromptCard.Background = AddRuleCard.Background;
            ProfileDeleteModalOverlay.Background = RuleModalOverlay.Background;
            ProfileDeleteCard.Background = AddRuleCard.Background;

            Application.Current.Resources["ComboPopupBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0xF5, 0x15, 0x19, 0x2B));
            Application.Current.Resources["ComboPopupBorderBrush"] = new LinearGradientBrush(
                Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                new Point(0, 0), new Point(0, 1));
            Application.Current.Resources["ComboItemSelectedBrush"] = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0xE5, 0xFF));
            Application.Current.Resources["ComboItemSelectedBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xE5, 0xFF));
        }
        else
        {
            Application.Current.Resources["GlassCardBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["GlassCardBorderBrush"] = new LinearGradientBrush(
                Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x33, 0x9D, 0x4E, 0xDD),
                new Point(0, 0), new Point(0, 1));

            AmbientBackdrop.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0xEA, 0xF2, 0xF5, 0xFC), 0.0),
                    new(Color.FromArgb(0xEA, 0xEB, 0xF0, 0xFA), 0.5),
                    new(Color.FromArgb(0xEA, 0xE3, 0xEA, 0xF8), 1.0)
                },
                new Point(0, 0), new Point(1, 1));

            KillSwitchIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0x2C, 0xBF));
            KillSwitchText.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x18, 0x9A));
            RuleModalOverlay.Background = new SolidColorBrush(Color.FromArgb(0x66, 0xD8, 0xE2, 0xF5));
            AddRuleCard.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            DeleteConfirmModalOverlay.Background = RuleModalOverlay.Background;
            DeleteConfirmCard.Background = AddRuleCard.Background;
            ProfilePromptModalOverlay.Background = RuleModalOverlay.Background;
            ProfilePromptCard.Background = AddRuleCard.Background;
            ProfileDeleteModalOverlay.Background = RuleModalOverlay.Background;
            ProfileDeleteCard.Background = AddRuleCard.Background;

            Application.Current.Resources["ComboPopupBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF));
            Application.Current.Resources["ComboPopupBorderBrush"] = new LinearGradientBrush(
                Color.FromArgb(0x22, 0x00, 0x00, 0x00),
                Color.FromArgb(0x14, 0x7B, 0x2C, 0xBF),
                new Point(0, 0), new Point(0, 1));
            Application.Current.Resources["ComboItemSelectedBrush"] = new SolidColorBrush(Color.FromArgb(0x1C, 0x00, 0x78, 0xD4));
            Application.Current.Resources["ComboItemSelectedBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x45, 0x00, 0x78, 0xD4));
        }
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        string theme = (CmbTheme.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System";
        _profile.Theme = theme;
        SaveProfile();
        ApplyTheme(theme);
    }

    private void ToggleTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        _profile.MinimizeToTray = ToggleTray.IsChecked == true;
        SaveProfile();
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
                AddLog(LocalizationService.Get("Log_AutostartEnabled"));
            }
            else
            {
                key.DeleteValue(RunValue, false);
                AddLog(LocalizationService.Get("Log_AutostartDisabled"));
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Error] Autostart: {ex.Message}");
        }
    }

    private void OnQuickDisableToggleRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EngineToggle.IsChecked = !(EngineToggle.IsChecked == true);
            bool active = EngineToggle.IsChecked == true;
            _tray?.ShowBalloonTip(1500, "HyperMouse", active ? LocalizationService.Get("Tray_EngineStarted") : LocalizationService.Get("Tray_EnginePaused"), System.Windows.Forms.ToolTipIcon.None);
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        });
    }

    private void ToggleQuickDisable_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        _profile.QuickDisableEnabled = ToggleQuickDisable.IsChecked == true;
        QuickDisableOptionsPanel.Visibility = _profile.QuickDisableEnabled ? Visibility.Visible : Visibility.Collapsed;
        SaveProfile();
        ApplyProfileToEngine();
    }

    private void QuickDisableSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || _profile == null) return;
        if (CmbQuickDisableButton.SelectedItem is ComboBoxItem btnItem &&
            Enum.TryParse<MouseButton>(btnItem.Tag?.ToString(), out var btn))
        {
            _profile.QuickDisableButton = btn;
        }
        if (CmbQuickDisableCadence.SelectedItem is ComboBoxItem cadItem &&
            Enum.TryParse<QuickDisableCadence>(cadItem.Tag?.ToString(), out var cad))
        {
            _profile.QuickDisableCadence = cad;
        }
        SaveProfile();
        ApplyProfileToEngine();
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        string lang = LocalizationService.CurrentLanguage;
        var dlg = new SaveFileDialog
        {
            Title = LocalizationService.Get("Settings_BtnExport", lang),
            Filter = "HyperMouse profili (*.json)|*.json",
            FileName = $"HyperMouse-{_profile.Name}-{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        ProfileConfig.Save(_profile, dlg.FileName);
        AddLog($"[Profile] Dışa aktarıldı: {dlg.FileName}");
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        string lang = LocalizationService.CurrentLanguage;
        var dlg = new OpenFileDialog { Title = LocalizationService.Get("Settings_BtnImport", lang), Filter = "HyperMouse profili (*.json)|*.json" };
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

        string profName = Path.GetFileNameWithoutExtension(dlg.FileName);
        if (string.IsNullOrWhiteSpace(imported.Name)) imported.Name = profName;
        _profileManager.SaveProfile(imported);
        _profileManager.SwitchProfile(imported.Name);
        RefreshProfileCombo();
        ApplyProfileToEngine();
        SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
        SliderLongPress.Value = _profile.LongPressThresholdMs;
        LoadAppSettings();
        UpdateMouseInfo();
        UpdateDpiStatus();
        AddLog($"[Profile] İçe aktarıldı: {imported.Name} ({imported.Rules.Count} kural)" +
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
            TxtMouseModel.Text = LocalizationService.Get(L10nKey.Hero_NoMouse);
            return;
        }

        var mice = _rawInput.Mice.Where(m => m.Vid != 0).ToList();
        _miceView.Clear();
        foreach (var m in _rawInput.Mice) _miceView.Add(m);
        TxtMouseCount.Text = LocalizationService.Format(L10nKey.Hero_DeviceCount, mice.Count);

        var current = _rawInput.ActiveMouse is { Vid: not 0 } a ? a : mice.FirstOrDefault();
        if (current == null)
        {
            TxtMouseModel.Text = LocalizationService.Get(L10nKey.Hero_NoMouse);
            TxtMouseDetails.Text = LocalizationService.Get(L10nKey.Hero_NoMouseDetails);
            return;
        }

        _currentMouse = current;
        bool hasAlias = _profile.DeviceAliases.TryGetValue(current.VidPid, out var alias) && !string.IsNullOrWhiteSpace(alias);
        TxtMouseModel.Text = hasAlias ? alias : current.DisplayName;
        TxtMouseModel.ToolTip = hasAlias ? current.DisplayName : null;
        string vendor = MouseVendors.Lookup(current.Vid);
        string active = _rawInput.ActiveMouse?.Handle == current.Handle
            ? LocalizationService.Get(L10nKey.Hero_Active)
            : LocalizationService.Get(L10nKey.Hero_Connected);
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
            SaveProfile();
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
        string lang = LocalizationService.CurrentLanguage;
        bool learned = _profile.LearnedSignals.TryGetValue(MouseButton.DpiButton, out var sig);
        TxtDpiStatus.Text = learned ? LocalizationService.Get("Hero_DpiReady", lang) : LocalizationService.Get("Hero_DpiNotLearned", lang);
        BtnForgetDpi.Visibility = learned ? Visibility.Visible : Visibility.Collapsed;
        BtnForgetDpi.Content = LocalizationService.Get("Rules_BtnForget", lang);
        if (_rawInput?.IsLearning != true)
        {
            BtnLearnDpi.Content = LocalizationService.Get("Rules_BtnLearn", lang);
            TxtLearnStatus.Text = learned
                ? LocalizationService.Format("Rules_DpiReady", sig!.Description)
                : LocalizationService.Get("Rules_DpiInstruction", lang);
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
        string lang = LocalizationService.CurrentLanguage;
        if (_rawInput.IsLearning)
        {
            StopLearn("İptal edildi.");
            return;
        }

        _rawInput.BeginLearn();
        BtnLearnDpi.Content = LocalizationService.Get("Rules_BtnCancel", lang);
        TxtLearnStatus.Text = LocalizationService.Get("Rules_DpiInstruction", lang);
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
        BtnLearnDpi.Content = LocalizationService.Get("Rules_BtnLearn");
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
        BtnLearnDpi.Content = LocalizationService.Get("Rules_BtnLearn");
        _profile.LearnedSignals[MouseButton.DpiButton] = sig;
        SaveProfile();
        _rawInput?.SetBindings(_profile.LearnedSignals);
        AddLog($"[Learn ✓] DPI tuşu kaydedildi: {sig.Description}");
        UpdateDpiStatus();
    }

    private void BtnForgetDpi_Click(object sender, RoutedEventArgs e)
    {
        _profile.LearnedSignals.Remove(MouseButton.DpiButton);
        SaveProfile();
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

        _engine.QuickDisable.IsEnabled = _profile.QuickDisableEnabled;
        _engine.QuickDisable.TargetButton = _profile.QuickDisableButton;
        _engine.QuickDisable.Cadence = _profile.QuickDisableCadence;
        _engine.QuickDisable.HoldDurationMs = _profile.QuickDisableHoldMs;

        string lang = LocalizationService.CurrentLanguage;
        foreach (var rule in _profile.Rules)
        {
            var action = rule.CreateAction();
            if (action != null)
            {
                _engine.RegisterAction(rule.Button, rule.Cadence, action, rule.ChordSecondary, rule.Layer);
                _rulesList.Add(CreateDisplayItem(rule, lang));
            }
        }

        RuleCountText.Text = LocalizationService.Format("Rules_Count", _rulesList.Count);
        EmptyRulesPanel.Visibility = _rulesList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rawInput?.SetBindings(_profile.LearnedSignals);
    }

    public static string ButtonDisplayName(MouseButton b) => LocalizationService.ButtonDisplayName(b);

    private static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 0xFF)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    private static RuleDisplayItem CreateDisplayItem(MappingRuleDto rule, string? lang = null)
    {
        string l = LocalizationService.NormalizeLanguage(lang ?? LocalizationService.CurrentLanguage);
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

        string cadenceText = rule.Cadence switch
        {
            CadenceType.Chord => $"+ {LocalizationService.ButtonDisplayName(rule.ChordSecondary, l)}",
            _ => LocalizationService.CadenceDisplayName(rule.Cadence, l)
        };

        (byte r, byte g, byte b) = rule.Cadence switch
        {
            CadenceType.SingleClick => ((byte)0x3B, (byte)0x8E, (byte)0xEA),
            CadenceType.DoubleClick => ((byte)0x8B, (byte)0x5C, (byte)0xF6),
            CadenceType.TripleClick => ((byte)0xD9, (byte)0x46, (byte)0xEF),
            CadenceType.LongPress => ((byte)0xF5, (byte)0x9E, (byte)0x0B),
            CadenceType.PressAndHoldDown => ((byte)0xF9, (byte)0x73, (byte)0x16),
            CadenceType.Chord => ((byte)0x10, (byte)0xB9, (byte)0x81),
            _ => ((byte)0x94, (byte)0xA3, (byte)0xB8)
        };

        string layerSuffix = rule.Layer > 0 ? $"  ·  {LocalizationService.Format("Rules_Layer", rule.Layer)}" : "";
        string buttonName = LocalizationService.ButtonDisplayName(rule.Button, l) + layerSuffix;
        string actionDisplay = LocalizationService.LocalizeActionDisplay(rule.ActionType, rule.Parameter, rule.DisplayName, l);

        return new RuleDisplayItem
        {
            Dto = rule,
            ButtonName = buttonName,
            CadenceDescription = cadenceText,
            ActionDisplay = actionDisplay,
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
            var evWithTime = ev with { TimestampMs = Environment.TickCount64 };
            _engine.QuickDisable.ProcessEvent(evWithTime);
            _engine.StateMachine.ProcessEvent(evWithTime);
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
        _engine.QuickDisable.ProcessEvent(adjusted);
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
            string cadenceStr = trigger.Cadence == CadenceType.Chord
                ? $"{ButtonDisplayName(trigger.Button)} + {ButtonDisplayName(trigger.ChordSecondary)}"
                : LocalizationService.CadenceDisplayName(trigger.Cadence);

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
    private readonly List<Point> _dragSlots = new();
    private readonly List<double> _dragWidths = new();
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
        _dragSlots.Clear();
        _dragWidths.Clear();
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
            _dragSlots.Add(c.TranslatePoint(new Point(0, 0), RulesListBox));
            _dragWidths.Add(c.ActualWidth);
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
        double dx = pos.X - _dragStartPoint.X;
        double dy = pos.Y - _dragStartPoint.Y;

        double minDx = -_dragSlots[idx].X - 12;
        double maxDx = (RulesListBox.ActualWidth - (_dragSlots[idx].X + _dragWidths[idx])) + 12;
        double minY = _dragSlots.Min(s => s.Y);
        double maxBottom = _dragSlots.Select((s, i) => s.Y + _dragHeights[i]).Max();
        double slack = _dragHeights[idx] * 0.45;
        double minDy = minY - _dragSlots[idx].Y - slack;
        double maxDy = maxBottom - (_dragSlots[idx].Y + _dragHeights[idx]) + slack;

        dx = Math.Clamp(dx, minDx, Math.Max(minDx, maxDx));
        dy = Math.Clamp(dy, minDy, maxDy);
        _dragTranslate!.X = dx;
        _dragTranslate.Y = dy;

        AutoScrollWhileDragging(pos);

        // Closest 2D slot center to the dragged card's current center
        double cx = _dragSlots[idx].X + _dragWidths[idx] / 2 + dx;
        double cy = _dragSlots[idx].Y + _dragHeights[idx] / 2 + dy;
        int target = idx;
        double bestDistSq = double.MaxValue;
        for (int i = 0; i <= last; i++)
        {
            double sx = _dragSlots[i].X + _dragWidths[i] / 2;
            double sy = _dragSlots[i].Y + _dragHeights[i] / 2;
            double distSq = (cx - sx) * (cx - sx) + (cy - sy) * (cy - sy);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                target = i;
            }
        }

        if (target == _dragTarget) return;
        _dragTarget = target;

        // Slide every other card smoothly to its shifted 2D grid slot
        for (int i = 0; i <= last; i++)
        {
            if (i == idx) continue;
            int dest = i;
            if (idx < target && i > idx && i <= target) dest = i - 1;
            else if (idx > target && i >= target && i < idx) dest = i + 1;

            double offX = _dragSlots[dest].X - _dragSlots[i].X;
            double offY = _dragSlots[dest].Y - _dragSlots[i].Y;

            if (_dragContainers[i].RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(offX, TimeSpan.FromMilliseconds(180)) { EasingFunction = SlideEase });
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(offY, TimeSpan.FromMilliseconds(180)) { EasingFunction = SlideEase });
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
            for (int i = 0; i < _dragSlots.Count; i++)
                _dragSlots[i] = new Point(_dragSlots[i].X, _dragSlots[i].Y - moved);
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
        double finalX = _dragSlots[target].X - _dragSlots[idx].X;
        double finalY = _dragSlots[target].Y - _dragSlots[idx].Y;

        var settleX = new DoubleAnimation(finalX, TimeSpan.FromMilliseconds(170)) { EasingFunction = SlideEase };
        var settleY = new DoubleAnimation(finalY, TimeSpan.FromMilliseconds(170)) { EasingFunction = SlideEase };
        var unscale = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(170)) { EasingFunction = SlideEase };
        settleY.Completed += (_, _) => CommitReorder(idx, target);
        _dragScale?.BeginAnimation(ScaleTransform.ScaleXProperty, unscale);
        _dragScale?.BeginAnimation(ScaleTransform.ScaleYProperty, unscale);
        _dragTranslate!.BeginAnimation(TranslateTransform.XProperty, settleX);
        _dragTranslate.BeginAnimation(TranslateTransform.YProperty, settleY);
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

            SaveProfile();
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

    private void OpenRuleModal()
    {
        MainContentHost.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
        RuleModalOverlay.Visibility = Visibility.Visible;
    }

    private void CloseRuleModal()
    {
        RuleModalOverlay.Visibility = Visibility.Collapsed;
        if (DeleteConfirmModalOverlay.Visibility != Visibility.Visible &&
            ProfilePromptModalOverlay.Visibility != Visibility.Visible &&
            ProfileDeleteModalOverlay.Visibility != Visibility.Visible)
        {
            MainContentHost.Effect = null;
        }
        _editingRule = null;
    }

    private void RuleModalBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _editingRule = null;
        CloseRuleModal();
    }

    private void AddRuleCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent clicks inside the modal card from bubbling to the backdrop
        e.Handled = true;
    }

    private void BtnToggleAddRule_Click(object sender, RoutedEventArgs e)
    {
        _editingRule = null;
        string lang = LocalizationService.CurrentLanguage;
        AddRuleTitle.Text = LocalizationService.Get("Modal_TitleNew", lang);
        OpenRuleModal();
        ValidateEditor();
    }

    private void BtnCancelAddRule_Click(object sender, RoutedEventArgs e)
    {
        _editingRule = null;
        CloseRuleModal();
    }

    private void BtnEditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is RuleDisplayItem item)
        {
            _editingRule = item.Dto;
            string lang = LocalizationService.CurrentLanguage;
            AddRuleTitle.Text = LocalizationService.Format("Modal_TitleEdit", item.ButtonName);

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

            OpenRuleModal();
            ValidateEditor();
        }
    }

    private static void SelectComboByTag(ComboBox cmb, string tag)
    {
        if (cmb == null) return;
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
            displayName = LocalizationService.MediaCommandDisplayName(parameter, LocalizationService.CurrentLanguage);
        }
        else
        {
            parameter = TxtActionParam?.Text.Trim() ?? "";
            displayName = LocalizationService.LocalizeActionDisplay(actionType, parameter, "", LocalizationService.CurrentLanguage);
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
            string lang = LocalizationService.CurrentLanguage;
            ConflictInfo.Message = $"{conflict.Reason} Mevcut: {LocalizationService.ButtonDisplayName(ex.Button, lang)} · {CreateDisplayItem(ex, lang).CadenceDescription} → " +
                                   $"{(string.IsNullOrEmpty(ex.DisplayName) ? ex.Parameter : LocalizationService.LocalizeActionDisplay(ex.ActionType, ex.Parameter, ex.DisplayName, lang))}";
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
        string lang = LocalizationService.CurrentLanguage;

        if (tag == "Media")
        {
            TxtActionParam.Visibility = Visibility.Collapsed;
            CmbMediaAction.Visibility = Visibility.Visible;
            ActionParamLabel.Text = LocalizationService.Get("Modal_Step4_Media", lang);
        }
        else
        {
            TxtActionParam.Visibility = Visibility.Visible;
            CmbMediaAction.Visibility = Visibility.Collapsed;
            ActionParamLabel.Text = tag switch
            {
                "Hotkey" => LocalizationService.Get("Modal_Step4_Hotkey", lang),
                "Text" => LocalizationService.Get("Modal_Step4_Text", lang),
                "Launch" => LocalizationService.Get("Modal_Step4_Launch", lang),
                "Mouse" => LocalizationService.Get("Modal_Step4_Mouse", lang),
                _ => LocalizationService.Get("Modal_Step4_Param", lang)
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
            string lang = LocalizationService.CurrentLanguage;
            System.Windows.MessageBox.Show(LocalizationService.Get("Modal_ValidationEmptyParam", lang), "HyperMouse", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
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

        SaveProfile();
        ApplyProfileToEngine();

        CloseRuleModal();
        AddLog($"[Rules] Kaydedildi: {ButtonDisplayName(newRule.Button)} {newRule.Cadence} → {newRule.DisplayName}");
    }

    // ==========================================
    // DELETE RULE CONFIRM MODAL
    // ==========================================

    private RuleDisplayItem? _pendingDeleteRule;

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is RuleDisplayItem item)
        {
            _pendingDeleteRule = item;
            DeleteConfirmRuleName.Text = $"{item.ButtonName} · {item.CadenceDescription}";
            DeleteConfirmRuleAction.Text = item.ActionDisplay;
            OpenDeleteConfirmModal();
        }
    }

    private void BtnConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteRule != null)
        {
            var item = _pendingDeleteRule;
            _pendingDeleteRule = null;
            _profile.Rules.Remove(item.Dto);
            SaveProfile();
            ApplyProfileToEngine();
            AddLog($"[Rules] Silindi: {item.ButtonName}");
        }
        CloseDeleteConfirmModal();
    }

    private void BtnCancelDelete_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteRule = null;
        CloseDeleteConfirmModal();
    }

    private void OpenDeleteConfirmModal()
    {
        MainContentHost.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
        DeleteConfirmModalOverlay.Visibility = Visibility.Visible;
    }

    private void CloseDeleteConfirmModal()
    {
        DeleteConfirmModalOverlay.Visibility = Visibility.Collapsed;
        if (RuleModalOverlay.Visibility != Visibility.Visible &&
            ProfilePromptModalOverlay.Visibility != Visibility.Visible &&
            ProfileDeleteModalOverlay.Visibility != Visibility.Visible)
        {
            MainContentHost.Effect = null;
        }
    }

    private void DeleteConfirmBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingDeleteRule = null;
        CloseDeleteConfirmModal();
    }

    private void DeleteConfirmCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    // ==========================================
    // PROFILE MANAGEMENT (SWITCH, NEW, RENAME, DELETE)
    // ==========================================

    private void RefreshProfileCombo()
    {
        if (CmbProfileSelect == null) return;
        _profileComboUpdating = true;
        CmbProfileSelect.Items.Clear();
        var names = _profileManager.GetProfileNames();
        string active = _profileManager.ActiveProfileName;
        int selectIdx = 0;

        for (int i = 0; i < names.Count; i++)
        {
            var item = new ComboBoxItem { Content = names[i], Tag = names[i] };
            CmbProfileSelect.Items.Add(item);
            if (string.Equals(names[i], active, StringComparison.OrdinalIgnoreCase))
                selectIdx = i;
        }

        if (CmbProfileSelect.Items.Count > 0)
            CmbProfileSelect.SelectedIndex = selectIdx;

        if (BtnDeleteProfile != null)
            BtnDeleteProfile.IsEnabled = names.Count > 1;

        _profileComboUpdating = false;
    }

    private void CmbProfileSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_profileComboUpdating) return;
        if (CmbProfileSelect.SelectedItem is ComboBoxItem item && item.Tag is string name)
        {
            if (!string.Equals(name, _profileManager.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _profileManager.SwitchProfile(name);
                ApplyProfileToEngine();
                LoadAppSettings();
                UpdateDpiStatus();
                SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
                SliderLongPress.Value = _profile.LongPressThresholdMs;
                AddLog(LocalizationService.Format("Log_ProfileLoaded", name));
            }
        }
    }

    private void BtnNewProfile_Click(object sender, RoutedEventArgs e)
    {
        _isRenamingProfile = false;
        _profileToRename = "";
        string lang = LocalizationService.CurrentLanguage;
        TxtProfilePromptTitle.Text = LocalizationService.Get("Profile_New_Title", lang);
        TxtProfilePromptSubtitle.Text = LocalizationService.Get("Profile_Name_Placeholder", lang);
        TxtProfilePromptLabel.Text = LocalizationService.Get("Profile_Name_Label", lang);
        TxtProfilePromptInput.Text = "";
        ProfilePromptError.IsOpen = false;
        OpenProfilePromptModal();
        TxtProfilePromptInput.Focus();
    }

    private void BtnRenameProfile_Click(object sender, RoutedEventArgs e)
    {
        _isRenamingProfile = true;
        _profileToRename = _profileManager.ActiveProfileName;
        string lang = LocalizationService.CurrentLanguage;
        TxtProfilePromptTitle.Text = LocalizationService.Get("Profile_Rename_Title", lang);
        TxtProfilePromptSubtitle.Text = _profileToRename;
        TxtProfilePromptLabel.Text = LocalizationService.Get("Profile_Name_Label", lang);
        TxtProfilePromptInput.Text = _profileToRename;
        ProfilePromptError.IsOpen = false;
        OpenProfilePromptModal();
        TxtProfilePromptInput.Focus();
        TxtProfilePromptInput.SelectAll();
    }

    private void TxtProfilePromptInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitProfilePrompt();
        else if (e.Key == Key.Escape) CloseProfilePromptModal();
    }

    private void TxtProfilePromptInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateProfilePrompt();
    }

    private bool ValidateProfilePrompt()
    {
        string input = TxtProfilePromptInput.Text.Trim();
        string lang = LocalizationService.CurrentLanguage;
        if (string.IsNullOrWhiteSpace(input))
        {
            ProfilePromptError.Message = LocalizationService.Get("Profile_Error_Empty", lang);
            ProfilePromptError.IsOpen = true;
            BtnProfilePromptConfirm.IsEnabled = false;
            return false;
        }

        if (!ProfileManager.IsValidProfileName(input, out var errKey))
        {
            ProfilePromptError.Message = LocalizationService.Get(errKey ?? "Profile_Error_InvalidChars", lang);
            ProfilePromptError.IsOpen = true;
            BtnProfilePromptConfirm.IsEnabled = false;
            return false;
        }

        var existing = _profileManager.GetProfileNames();
        bool isDuplicate = existing.Any(n => string.Equals(n, input, StringComparison.OrdinalIgnoreCase));
        if (isDuplicate && (!_isRenamingProfile || !string.Equals(input, _profileToRename, StringComparison.OrdinalIgnoreCase)))
        {
            ProfilePromptError.Message = LocalizationService.Get("Profile_Error_Exists", lang);
            ProfilePromptError.IsOpen = true;
            BtnProfilePromptConfirm.IsEnabled = false;
            return false;
        }

        ProfilePromptError.IsOpen = false;
        BtnProfilePromptConfirm.IsEnabled = true;
        return true;
    }

    private void BtnConfirmProfilePrompt_Click(object sender, RoutedEventArgs e) => CommitProfilePrompt();

    private void CommitProfilePrompt()
    {
        if (!ValidateProfilePrompt()) return;
        string name = TxtProfilePromptInput.Text.Trim();

        if (_isRenamingProfile)
        {
            if (!string.Equals(_profileToRename, name, StringComparison.OrdinalIgnoreCase))
            {
                _profileManager.RenameProfile(_profileToRename, name);
                AddLog(LocalizationService.Format("Log_ProfileRenamed", _profileToRename, name));
            }
        }
        else
        {
            _profileManager.CreateProfile(name, copyCurrentRules: false);
            AddLog(LocalizationService.Format("Log_ProfileCreated", name));
        }

        CloseProfilePromptModal();
        RefreshProfileCombo();
        ApplyProfileToEngine();
        LoadAppSettings();
    }

    private void BtnCancelProfilePrompt_Click(object sender, RoutedEventArgs e) => CloseProfilePromptModal();
    private void ProfilePromptBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseProfilePromptModal();
    private void ProfilePromptCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OpenProfilePromptModal()
    {
        MainContentHost.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
        ProfilePromptModalOverlay.Visibility = Visibility.Visible;
    }

    private void CloseProfilePromptModal()
    {
        ProfilePromptModalOverlay.Visibility = Visibility.Collapsed;
        if (RuleModalOverlay.Visibility != Visibility.Visible &&
            DeleteConfirmModalOverlay.Visibility != Visibility.Visible &&
            ProfileDeleteModalOverlay.Visibility != Visibility.Visible)
        {
            MainContentHost.Effect = null;
        }
    }

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var names = _profileManager.GetProfileNames();
        string lang = LocalizationService.CurrentLanguage;
        if (names.Count <= 1)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get("Profile_Error_CannotDeleteLast", lang),
                LocalizationService.Get("Profile_Delete_Title", lang),
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _profileToDelete = _profileManager.ActiveProfileName;
        TxtProfileDeleteTitle.Text = LocalizationService.Get("Profile_Delete_Title", lang);
        TxtProfileDeleteWarning.Text = LocalizationService.Get("Profile_Delete_Warning", lang);
        TxtProfileDeletePrompt.Text = LocalizationService.Format("Profile_Delete_Confirm", _profileToDelete);
        OpenProfileDeleteModal();
    }

    private void BtnConfirmProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_profileToDelete))
        {
            string deleted = _profileToDelete;
            _profileToDelete = "";
            bool ok = _profileManager.DeleteProfile(deleted);
            if (ok)
            {
                AddLog(LocalizationService.Format("Log_ProfileDeleted", deleted));
                RefreshProfileCombo();
                ApplyProfileToEngine();
                LoadAppSettings();
            }
        }
        CloseProfileDeleteModal();
    }

    private void BtnCancelProfileDelete_Click(object sender, RoutedEventArgs e) => CloseProfileDeleteModal();
    private void ProfileDeleteBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseProfileDeleteModal();
    private void ProfileDeleteCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OpenProfileDeleteModal()
    {
        MainContentHost.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
        ProfileDeleteModalOverlay.Visibility = Visibility.Visible;
    }

    private void CloseProfileDeleteModal()
    {
        ProfileDeleteModalOverlay.Visibility = Visibility.Collapsed;
        if (RuleModalOverlay.Visibility != Visibility.Visible &&
            DeleteConfirmModalOverlay.Visibility != Visibility.Visible &&
            ProfilePromptModalOverlay.Visibility != Visibility.Visible)
        {
            MainContentHost.Effect = null;
        }
    }

    // ==========================================
    // SETTINGS SLIDERS & BUTTONS
    // ==========================================

    private void SliderDoubleClick_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        if (TxtDoubleClickVal != null) TxtDoubleClickVal.Text = $"{val} ms";
        if (!_settingsLoading && _profile != null && _config != null)
        {
            _profile.DoubleClickThresholdMs = val;
            _config.DoubleClickThresholdMs = val;
            SaveProfile();
        }
    }

    private void SliderLongPress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        if (TxtLongPressVal != null) TxtLongPressVal.Text = $"{val} ms";
        if (!_settingsLoading && _profile != null && _config != null)
        {
            _profile.LongPressThresholdMs = val;
            _config.LongPressThresholdMs = val;
            SaveProfile();
        }
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        string lang = LocalizationService.CurrentLanguage;
        var res = System.Windows.MessageBox.Show(
            LocalizationService.Get("Settings_ResetConfirm", lang),
            LocalizationService.Get("Settings_BtnResetDefaults", lang),
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == System.Windows.MessageBoxResult.Yes)
        {
            var def = ProfileConfig.CreateDefault();
            def.Name = _profile.Name;
            _profileManager.SaveProfile(def);
            _profileManager.SwitchProfile(_profile.Name);
            ApplyProfileToEngine();
            UpdateDpiStatus();
            SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
            SliderLongPress.Value = _profile.LongPressThresholdMs;
            AddLog("[System] " + LocalizationService.Get("Settings_ResetConfirm", lang));
        }
    }

    private void BtnOpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_profileManager.ProfilesDirectory))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", _profileManager.ProfilesDirectory) { UseShellExecute = true });
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
        UpdateEngineStatusText(true);
        UpdateTrayState(true);
        AddLog(LocalizationService.Get("Log_EngineStarted"));
    }

    private void EngineToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _engine?.Pause();
        MouseVisual3D?.SetEngineActive(false);
        UpdateEngineStatusText(false);
        UpdateTrayState(false);
        AddLog(LocalizationService.Get("Log_EnginePaused"));
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
                string lang = LocalizationService.CurrentLanguage;
                _tray?.ShowBalloonTip(2500, LocalizationService.Get("Tray_BalloonBackground", lang), LocalizationService.Get("Tray_BalloonHint", lang), System.Windows.Forms.ToolTipIcon.None);
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
