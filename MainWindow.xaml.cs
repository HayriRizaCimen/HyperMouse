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
using HyperMouse.Core.Config;
using HyperMouse.Core.Engine;
using HyperMouse.Core.Input;
using HyperMouse.Core.Input.RawInput;
using Wpf.Ui.Controls;

namespace HyperMouse;

public sealed class RuleDisplayItem
{
    public MappingRuleDto Dto { get; set; } = null!;
    public string ButtonName { get; set; } = "";
    public string CadenceDescription { get; set; } = "";
    public string ActionDisplay { get; set; } = "";
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

    private readonly SolidColorBrush _activeBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));
    private readonly SolidColorBrush _dimBrush = new(Color.FromRgb(0x30, 0x36, 0x3D));
    private readonly SolidColorBrush _dimButtonBrush = new(Color.FromRgb(0x21, 0x26, 0x2D));

    public MainWindow()
    {
        InitializeComponent();

        RulesListBox.ItemsSource = _rulesList;
        LiveEventListBox.ItemsSource = _eventLogs;

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
            System.Windows.MessageBox.Show($"Kanca başlatılırken hata: {ex.Message}", "HyperMouse Hatası", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
        }

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
                _rawInput.Diagnostic += msg => Dispatcher.BeginInvoke(() => AddLog(msg));
                _rawInput.RawMouseEventReceived += OnRawMouseEventReceived;
                source.AddHook(_rawInput.ProcessMessage);
                if (_rawInput.Register(helper.Handle))
                {
                    AddLog("[SİSTEM] RawInput (Fare + Klavye + Consumer Control + Vendor HID) ve Shell AppCommand dinleyicisi bağlandı.");
                }
            }
        }
        catch (Exception ex)
        {
            AddLog($"[UYARI] RawInput başlatılamadı: {ex.Message}");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
        SliderLongPress.Value = _profile.LongPressThresholdMs;
        AddLog("[SİSTEM] HyperMouse hazır. Donanım kancası ve klavye kancası devrede.");
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

        RuleCountText.Text = $"({_rulesList.Count} Kural)";
    }

    private static RuleDisplayItem CreateDisplayItem(MappingRuleDto rule)
    {
        string btnName = rule.Button switch
        {
            MouseButton.XButton1 => "◀ Yan Tuş 1 (Geri Tuşu)",
            MouseButton.XButton2 => "▶ Yan Tuş 2 (İleri Tuşu)",
            MouseButton.Middle => "🔘 Orta Tuş (Tekerlek Tıkı)",
            MouseButton.Right => "🔘 Sağ Tuş",
            MouseButton.Left => "🔘 Sol Tuş",
            MouseButton.WheelUp => "🔼 Tekerlek Yukarı",
            MouseButton.WheelDown => "🔽 Tekerlek Aşağı",
            _ => rule.Button.ToString()
        };

        string cadenceDesc = rule.Cadence switch
        {
            CadenceType.SingleClick => "Tetikleyici: Tek Tık (Single Click)",
            CadenceType.DoubleClick => "Tetikleyici: Çift Tık (Double Click)",
            CadenceType.TripleClick => "Tetikleyici: Üçlü Tık (Triple Click)",
            CadenceType.LongPress => "Tetikleyici: Uzun Basma (Long Press > 320ms)",
            CadenceType.PressAndHoldDown => "Tetikleyici: Bas-Çek (Press & Hold)",
            CadenceType.Chord => $"Tetikleyici: Kombinasyon (Tuşa Basılıyken + {rule.ChordSecondary})",
            _ => rule.Cadence.ToString()
        };

        return new RuleDisplayItem
        {
            Dto = rule,
            ButtonName = btnName,
            CadenceDescription = cadenceDesc,
            ActionDisplay = !string.IsNullOrEmpty(rule.DisplayName) ? rule.DisplayName : $"{rule.ActionType}: {rule.Parameter}"
        };
    }

    private void OnMouseEventReceived(RawMouseEvent ev)
    {
        _lastHookEventTimes[ev.Button] = Environment.TickCount64;

        Dispatcher.BeginInvoke(() =>
        {
            TxtDetectedEvent.Text = $"{ev.Button} [{ev.EventType}]";
            HighlightVisualButton(ev.Button, ev.EventType == MouseEventType.Down);

            if (ev.EventType is MouseEventType.Down or MouseEventType.Wheel or MouseEventType.HWheel)
            {
                AddLog($"[KANCADAN] {ev.Button} {ev.EventType} yakalandı.");
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
            RawSource.ConsumerControl => "CONSUMER CONTROL",
            RawSource.Keyboard => "HID KLAVYE",
            RawSource.AppCommand => "APPCOMMAND",
            _ => "RAW FARE"
        };

        TxtDetectedEvent.Text = $"[{srcName}] {ev.Button} [{ev.EventType}]";
        HighlightVisualButton(ev.Button, ev.EventType == MouseEventType.Down);
        if (ev.EventType == MouseEventType.Down)
        {
            AddLog($"[YAN TUŞ ✓] {ev.Button} yakalandı (kaynak: {srcName}).");
        }

        var adjusted = ev with { TimestampMs = now };
        _engine.StateMachine.ProcessEvent(adjusted);
    }

    private void HighlightVisualButton(MouseButton button, bool isDown)
    {
        var wheelBrush = isDown ? _activeBrush : _dimBrush;

        switch (button)
        {
            case MouseButton.Left:
                VisualLeftButton.Background = isDown ? _activeBrush : _dimButtonBrush;
                break;
            case MouseButton.Right:
                VisualRightButton.Background = isDown ? _activeBrush : _dimButtonBrush;
                break;
            case MouseButton.Middle:
            case MouseButton.WheelUp:
            case MouseButton.WheelDown:
                VisualMiddleButton.Background = wheelBrush;
                break;
            case MouseButton.XButton1:
                VisualX1Button.Background = isDown ? _activeBrush : _dimBrush;
                break;
            case MouseButton.XButton2:
                VisualX2Button.Background = isDown ? _activeBrush : _dimBrush;
                break;
        }

        if (!isDown)
        {
            if (button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.Middle)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (s, e) =>
                {
                    VisualMiddleButton.Background = _dimBrush;
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    private void OnTriggerFired(GestureTrigger trigger)
    {
        Dispatcher.BeginInvoke(() =>
        {
            string cadenceStr = trigger.Cadence switch
            {
                CadenceType.SingleClick => "Tek Tık (Single Click)",
                CadenceType.DoubleClick => "Çift Tık (Double Click)",
                CadenceType.TripleClick => "Üçlü Tık (Triple Click)",
                CadenceType.LongPress => "Uzun Basma (Long Press)",
                CadenceType.Chord => $"Kombinasyon: {trigger.Button} + {trigger.ChordSecondary}",
                _ => trigger.Cadence.ToString()
            };

            TxtDetectedCadence.Text = cadenceStr;
            AddLog($"[EYLEM TETİKLENDİ] {trigger.Button} -> {cadenceStr}");
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

        // Keep the card inside the list bounds
        double minDy = _dragTops[0] - _dragTops[idx];
        double maxDy = (_dragTops[last] + _dragHeights[last]) - (_dragTops[idx] + _dragHeights[idx]);
        dy = Math.Clamp(dy, minDy, maxDy);
        _dragTranslate!.Y = dy;

        // Determine target slot from the dragged card's centre
        double center = _dragTops[idx] + _dragHeights[idx] / 2 + dy;
        int target = idx;
        for (int i = idx + 1; i <= last; i++)
            if (center > _dragTops[i] + _dragHeights[i] / 2) target = i;
        for (int i = idx - 1; i >= 0; i--)
            if (center < _dragTops[i] + _dragHeights[i] / 2) target = i;

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
            AddLog($"[SIRALAMA] '{moved.ButtonName}' kuralı {oldIndex + 1}. sıradan {newIndex + 1}. sıraya taşındı.");
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
        AddRuleTitle.Text = "✨ Yeni Tuş Eşlemesi Ekle";
        AddRuleCard.Visibility = AddRuleCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
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
            AddRuleTitle.Text = $"✏️ Kuralı Düzenle ({item.ButtonName})";

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
            ActionParamLabel.Text = "Medya Komutu:";
        }
        else
        {
            TxtActionParam.Visibility = Visibility.Visible;
            CmbMediaAction.Visibility = Visibility.Collapsed;
            ActionParamLabel.Text = tag switch
            {
                "Hotkey" => "Kısayol Tuşu (Örn: Ctrl+C):",
                "Text" => "Yazılacak Metin:",
                "Launch" => "Program / URL Yolu:",
                "Mouse" => "Fare Tuşu (Left, Right, Middle):",
                _ => "Parametre:"
            };
        }
    }

    private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
    {
        var btnItem = CmbTargetButton.SelectedItem as ComboBoxItem;
        var cadenceItem = CmbCadence.SelectedItem as ComboBoxItem;
        var actionItem = CmbActionType.SelectedItem as ComboBoxItem;

        if (btnItem == null || cadenceItem == null || actionItem == null) return;

        if (!Enum.TryParse<MouseButton>(btnItem.Tag?.ToString(), out var targetBtn)) return;
        if (!Enum.TryParse<CadenceType>(cadenceItem.Tag?.ToString(), out var cadence)) return;

        string actionType = actionItem.Tag?.ToString() ?? "Hotkey";
        string parameter = "";
        string displayName = "";

        if (actionType == "Media")
        {
            var mediaItem = CmbMediaAction.SelectedItem as ComboBoxItem;
            parameter = mediaItem?.Tag?.ToString() ?? "VolumeUp";
            displayName = mediaItem?.Content?.ToString() ?? parameter;
        }
        else
        {
            parameter = TxtActionParam.Text.Trim();
            if (string.IsNullOrEmpty(parameter))
            {
                System.Windows.MessageBox.Show("Lütfen eylem parametresini giriniz (Örn: Ctrl+C)", "Eksik Bilgi", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            displayName = $"{actionType}: {parameter}";
        }

        MouseButton chordSecondary = MouseButton.None;
        if (cadence == CadenceType.Chord)
        {
            var chordItem = CmbChordSecondary.SelectedItem as ComboBoxItem;
            Enum.TryParse(chordItem?.Tag?.ToString(), out chordSecondary);
        }

        if (_editingRule != null)
        {
            // Update existing rule
            _profile.Rules.Remove(_editingRule);
            _editingRule = null;
        }
        else
        {
            // Remove any matching rule to prevent duplicates
            _profile.Rules.RemoveAll(r => r.Button == targetBtn && r.Cadence == cadence && r.ChordSecondary == chordSecondary);
        }

        var newRule = new MappingRuleDto
        {
            Button = targetBtn,
            Cadence = cadence,
            ChordSecondary = chordSecondary,
            ActionType = actionType,
            Parameter = parameter,
            DisplayName = displayName
        };

        _profile.Rules.Add(newRule);
        ProfileConfig.Save(_profile, ConfigPath);
        ApplyProfileToEngine();

        AddRuleCard.Visibility = Visibility.Collapsed;
        AddLog($"[KURAL KAYDEDİLDİ] {targetBtn} -> {displayName}");
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is RuleDisplayItem item)
        {
            _profile.Rules.Remove(item.Dto);
            ProfileConfig.Save(_profile, ConfigPath);
            ApplyProfileToEngine();
            AddLog($"[KURAL SİLİNDİ] {item.ButtonName} eşlemesi kaldırıldı.");
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
        var res = System.Windows.MessageBox.Show("Tüm tuş eşlemeleri varsayılan fabrika ayarlarına döndürülecek. Onaylıyor musunuz?", "Varsayılana Sıfırla", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == System.Windows.MessageBoxResult.Yes)
        {
            _profile = ProfileConfig.CreateDefault();
            ProfileConfig.Save(_profile, ConfigPath);
            ApplyProfileToEngine();
            SliderDoubleClick.Value = _profile.DoubleClickThresholdMs;
            SliderLongPress.Value = _profile.LongPressThresholdMs;
            AddLog("[SİSTEM] Tüm ayarlar varsayılana sıfırlandı.");
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
        if (StatusBadge != null && StatusBadgeText != null)
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x27, 0xA2, 0x44));
            StatusBadgeText.Text = "AKTİF / KANCA DEVREDE";
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x46, 0xC1, 0x5E));
            AddLog("[SİSTEM] Motor başlatıldı.");
        }
    }

    private void EngineToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _engine?.Pause();
        if (StatusBadge != null && StatusBadgeText != null)
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xD7, 0x3A, 0x49));
            StatusBadgeText.Text = "DURAKLATILDI";
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
            AddLog("[SİSTEM] Motor duraklatıldı. Tüm fare girdileri serbest.");
        }
    }

    private static readonly string DiagLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperMouse", "diag.log");

    private void AddLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        _eventLogs.Insert(0, line);
        if (_eventLogs.Count > 300) _eventLogs.RemoveAt(_eventLogs.Count - 1);

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
        _rawInput?.Dispose();
        _engine?.Dispose();
    }
}
