using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MouseButton = HyperMouse.Core.Common.MouseButton;

namespace HyperMouse.Controls;

public partial class Mouse3DVisual : UserControl
{
    private static readonly Point3D CameraTarget = new(0, 0.38, -0.02);

    private double _yawDeg = -42.0;
    private double _pitchDeg = 38.0;
    private const double CameraDistance = 10.5;

    private bool _isDragging;
    private Point _dragStart;
    private double _dragStartYaw;
    private double _dragStartPitch;

    // Camera preset smooth transition
    private DispatcherTimer? _camAnimTimer;
    private double _camFromYaw, _camFromPitch, _camToYaw, _camToPitch;
    private DateTime _camAnimStart;
    private const double CamAnimMs = 280.0;

    // Interactive 3D models & hit-test mapping
    private GeometryModel3D _leftButtonModel = null!;
    private GeometryModel3D _rightButtonModel = null!;
    private GeometryModel3D _wheelTireModel = null!;
    private GeometryModel3D _wheelHubModel = null!;
    private GeometryModel3D _dpiButtonModel = null!;
    private GeometryModel3D _x1ButtonModel = null!;
    private GeometryModel3D _x2ButtonModel = null!;

    private readonly Dictionary<GeometryModel3D, MouseButton> _hitMap = new();

    // Animated button emissive brushes
    private SolidColorBrush _leftEmissive = null!;
    private SolidColorBrush _rightEmissive = null!;
    private SolidColorBrush _wheelEmissive = null!;
    private SolidColorBrush _dpiEmissive = null!;
    private SolidColorBrush _x1Emissive = null!;
    private SolidColorBrush _x2Emissive = null!;
    private SolidColorBrush _rgbUnderglowEmissive = null!;
    private SolidColorBrush _palmLogoEmissive = null!;

    // Animated 3D mechanical transforms
    private AxisAngleRotation3D _leftPressRot = null!;
    private AxisAngleRotation3D _rightPressRot = null!;
    private AxisAngleRotation3D _wheelSpinRot = null!;
    private AxisAngleRotation3D _wheelTiltRot = null!;
    private TranslateTransform3D _wheelPressTrans = null!;
    private TranslateTransform3D _dpiPressTrans = null!;
    private TranslateTransform3D _x1PressTrans = null!;
    private TranslateTransform3D _x2PressTrans = null!;

    private double _wheelAccumAngle;
    private MouseButton _hoveredButton = MouseButton.None;
    private readonly HashSet<MouseButton> _pressedButtons = new();
    private readonly Dictionary<MouseButton, DispatcherTimer> _momentaryTimers = new();
    private DispatcherTimer? _hudHideTimer;

    private static readonly Color IdleEmissive = Color.FromRgb(0x00, 0x00, 0x00);
    private static readonly Color HoverEmissive = Color.FromRgb(0x0D, 0x28, 0x48);
    private static readonly Color ActivePrimaryEmissive = Color.FromRgb(0x16, 0x5E, 0xCC);
    private static readonly Color ActiveWheelEmissive = Color.FromRgb(0x1D, 0x72, 0xEB);
    private static readonly Color ActiveSideEmissive = Color.FromRgb(0x18, 0x6A, 0xDF);
    private static readonly Color ActiveDpiEmissive = Color.FromRgb(0x22, 0x7E, 0xF5);

    private static readonly IEasingFunction PressEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public Mouse3DVisual()
    {
        InitializeComponent();
        BuildScene();
        UpdateCamera();
    }

    private void BuildScene()
    {
        // Procedural tactile textures
        var shellTex = new ImageBrush(MouseMeshBuilder.CreateShellTexture(
            Color.FromRgb(0x32, 0x39, 0x46),
            Color.FromRgb(0x18, 0x1C, 0x23),
            grainStrength: 4.5))
        { Stretch = Stretch.Fill };
        shellTex.Freeze();

        var triggerTex = new ImageBrush(MouseMeshBuilder.CreateShellTexture(
            Color.FromRgb(0x2C, 0x33, 0x40),
            Color.FromRgb(0x16, 0x1A, 0x21),
            grainStrength: 3.5))
        { Stretch = Stretch.Fill };
        triggerTex.Freeze();

        var gripTex = new ImageBrush(MouseMeshBuilder.CreateRubberGripTexture())
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.Tile
        };
        gripTex.Freeze();

        var floorTex = new ImageBrush(MouseMeshBuilder.CreateFloorShadowTexture(
            Color.FromRgb(0x2B, 0x7E, 0xEC)))
        { Stretch = Stretch.Fill };
        floorTex.Freeze();

        // Shared static materials
        var mainShellMat = CreateSatinMaterial(shellTex, specOpacity: 0.42, specPower: 46);
        var innerCavityMat = CreateFlatMaterial(Color.FromRgb(0x09, 0x0B, 0x0F), specOpacity: 0.05, specPower: 10);
        var gripMat = CreateSatinMaterial(gripTex, specOpacity: 0.12, specPower: 14);
        var basePlateMat = CreateFlatMaterial(Color.FromRgb(0x10, 0x13, 0x18), specOpacity: 0.14, specPower: 20);
        var skateMat = CreateFlatMaterial(Color.FromRgb(0x38, 0x40, 0x4D), specOpacity: 0.35, specPower: 45);
        var sensorMat = CreateEmissiveAccentMaterial(Color.FromRgb(0x1A, 0x08, 0x0C), Color.FromRgb(0xB9, 0x1C, 0x1C), 0.25, 65);

        _rgbUnderglowEmissive = new SolidColorBrush(Color.FromRgb(0x10, 0x58, 0xBD));
        var rgbMat = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x0E, 0x35, 0x6B))),
                new EmissiveMaterial(_rgbUnderglowEmissive)
            }
        };

        _palmLogoEmissive = new SolidColorBrush(Color.FromRgb(0x18, 0x68, 0xD2));
        var logoMat = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1C, 0x4E, 0x8E))),
                new EmissiveMaterial(_palmLogoEmissive),
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 70)
            }
        };

        // 1. Studio ground shadow plane
        var floorMat = new DiffuseMaterial(floorTex);
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildStudioFloor(), floorMat));

        // 2. Base plate, skates, optical sensor & RGB underglow
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildBasePlate(), basePlateMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildBottomSkates(), skateMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildOpticalSensor(), sensorMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildRgbUnderglow(), rgbMat));

        // 3. Recessed dark inner cavity & main chassis shell
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildInnerCavity(), innerCavityMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildPalmAndSpineShell(), mainShellMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildSideGrip(isLeft: true), gripMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildSideGrip(isLeft: false), gripMat));
        SceneRoot.Children.Add(new GeometryModel3D(MouseMeshBuilder.BuildPalmLogo(), logoMat));

        // 4. Interactive Primary Triggers (LMB / RMB)
        _leftEmissive = new SolidColorBrush(IdleEmissive);
        _leftPressRot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        _leftButtonModel = new GeometryModel3D(
            MouseMeshBuilder.BuildTriggerButton(isLeft: true),
            CreateInteractiveMaterial(triggerTex, _leftEmissive, specOpacity: 0.48, specPower: 58))
        {
            Transform = new RotateTransform3D(_leftPressRot, new Point3D(0, 0.88, MouseMeshBuilder.TriggerHingeZ))
        };
        SceneRoot.Children.Add(_leftButtonModel);
        _hitMap[_leftButtonModel] = MouseButton.Left;

        _rightEmissive = new SolidColorBrush(IdleEmissive);
        _rightPressRot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        _rightButtonModel = new GeometryModel3D(
            MouseMeshBuilder.BuildTriggerButton(isLeft: false),
            CreateInteractiveMaterial(triggerTex, _rightEmissive, specOpacity: 0.48, specPower: 58))
        {
            Transform = new RotateTransform3D(_rightPressRot, new Point3D(0, 0.88, MouseMeshBuilder.TriggerHingeZ))
        };
        SceneRoot.Children.Add(_rightButtonModel);
        _hitMap[_rightButtonModel] = MouseButton.Right;

        // 5. Interactive 3D Scroll Wheel (Spin + Tilt + Middle Depress)
        _wheelEmissive = new SolidColorBrush(IdleEmissive);
        _wheelSpinRot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        _wheelTiltRot = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        _wheelPressTrans = new TranslateTransform3D();

        var wheelCenter = new Point3D(0, MouseMeshBuilder.WheelCenterY, MouseMeshBuilder.WheelCenterZ);
        var wheelTransforms = new Transform3DGroup();
        wheelTransforms.Children.Add(new RotateTransform3D(_wheelSpinRot, wheelCenter));
        wheelTransforms.Children.Add(new RotateTransform3D(_wheelTiltRot, wheelCenter));
        wheelTransforms.Children.Add(_wheelPressTrans);

        var wheelGroup = new Model3DGroup { Transform = wheelTransforms };

        var wheelTireMat = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x28))),
                new EmissiveMaterial(_wheelEmissive),
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)), 20)
            }
        };
        _wheelTireModel = new GeometryModel3D(MouseMeshBuilder.BuildScrollWheelTire(), wheelTireMat);
        wheelGroup.Children.Add(_wheelTireModel);
        _hitMap[_wheelTireModel] = MouseButton.Middle;

        var wheelHubMat = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x52, 0x5D, 0x6E))),
                new EmissiveMaterial(_wheelEmissive),
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), 95)
            }
        };
        _wheelHubModel = new GeometryModel3D(MouseMeshBuilder.BuildScrollWheelHub(), wheelHubMat);
        wheelGroup.Children.Add(_wheelHubModel);
        _hitMap[_wheelHubModel] = MouseButton.Middle;

        SceneRoot.Children.Add(wheelGroup);

        // 6. Interactive DPI Button
        _dpiEmissive = new SolidColorBrush(IdleEmissive);
        _dpiPressTrans = new TranslateTransform3D();
        _dpiButtonModel = new GeometryModel3D(
            MouseMeshBuilder.BuildDpiButton(),
            CreateInteractiveFlatMaterial(Color.FromRgb(0x28, 0x2F, 0x3B), _dpiEmissive, 0.72, 85))
        {
            Transform = _dpiPressTrans
        };
        SceneRoot.Children.Add(_dpiButtonModel);
        _hitMap[_dpiButtonModel] = MouseButton.DpiButton;

        // 7. Interactive Side Buttons (X2 Forward, X1 Back)
        _x2Emissive = new SolidColorBrush(IdleEmissive);
        _x2PressTrans = new TranslateTransform3D();
        _x2ButtonModel = new GeometryModel3D(
            MouseMeshBuilder.BuildSideButton(isForward: true),
            CreateInteractiveFlatMaterial(Color.FromRgb(0x2C, 0x34, 0x42), _x2Emissive, 0.60, 70))
        {
            Transform = _x2PressTrans
        };
        SceneRoot.Children.Add(_x2ButtonModel);
        _hitMap[_x2ButtonModel] = MouseButton.XButton2;

        _x1Emissive = new SolidColorBrush(IdleEmissive);
        _x1PressTrans = new TranslateTransform3D();
        _x1ButtonModel = new GeometryModel3D(
            MouseMeshBuilder.BuildSideButton(isForward: false),
            CreateInteractiveFlatMaterial(Color.FromRgb(0x2C, 0x34, 0x42), _x1Emissive, 0.60, 70))
        {
            Transform = _x1PressTrans
        };
        SceneRoot.Children.Add(_x1ButtonModel);
        _hitMap[_x1ButtonModel] = MouseButton.XButton1;
    }

    // =========================================================
    // PUBLIC API
    // =========================================================

    public void SetEngineActive(bool active)
    {
        var rgbTarget = active ? Color.FromRgb(0x14, 0x62, 0xC8) : Color.FromRgb(0x16, 0x1A, 0x22);
        var logoTarget = active ? Color.FromRgb(0x1E, 0x78, 0xE8) : Color.FromRgb(0x12, 0x16, 0x1D);
        AnimateColor(_rgbUnderglowEmissive, rgbTarget, 220);
        AnimateColor(_palmLogoEmissive, logoTarget, 220);
    }

    public void SetButtonState(MouseButton button, bool isDown)
    {
        if (button == MouseButton.None) return;

        bool isWheelTick = button is MouseButton.WheelUp or MouseButton.WheelDown
            or MouseButton.WheelTiltLeft or MouseButton.WheelTiltRight;

        if (isWheelTick)
        {
            if (!isDown) return;
            TriggerWheelAction(button);
            return;
        }

        if (isDown)
        {
            _pressedButtons.Add(button);
            ApplyButtonVisual(button, down: true);

            // One-shot buttons (e.g. DpiButton) or fast clicks may not send a separate Up event:
            // ensure a clean auto-release safety fallback for DpiButton.
            if (button == MouseButton.DpiButton)
            {
                ScheduleMomentaryRelease(MouseButton.DpiButton, 220, () =>
                {
                    _pressedButtons.Remove(MouseButton.DpiButton);
                    ApplyButtonVisual(MouseButton.DpiButton, down: false);
                });
            }
        }
        else
        {
            if (button == MouseButton.DpiButton)
            {
                // Let DpiButton stay illuminated briefly so quick pulses remain clearly visible
                ScheduleMomentaryRelease(MouseButton.DpiButton, 120, () =>
                {
                    _pressedButtons.Remove(MouseButton.DpiButton);
                    ApplyButtonVisual(MouseButton.DpiButton, down: false);
                });
                return;
            }

            _pressedButtons.Remove(button);
            ApplyButtonVisual(button, down: false);
        }
    }

    private void TriggerWheelAction(MouseButton wheelAction)
    {
        // Spin or tilt wheel physically
        switch (wheelAction)
        {
            case MouseButton.WheelUp:
                _wheelAccumAngle -= 42.0;
                _wheelSpinRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(_wheelAccumAngle, TimeSpan.FromMilliseconds(140)) { EasingFunction = PressEase });
                ShowHud("▲ Tekerlek Yukarı", isActive: true);
                break;

            case MouseButton.WheelDown:
                _wheelAccumAngle += 42.0;
                _wheelSpinRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(_wheelAccumAngle, TimeSpan.FromMilliseconds(140)) { EasingFunction = PressEase });
                ShowHud("▼ Tekerlek Aşağı", isActive: true);
                break;

            case MouseButton.WheelTiltLeft:
                _wheelTiltRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(16.0, TimeSpan.FromMilliseconds(60)) { EasingFunction = PressEase });
                ShowHud("◀ Tekerlek Sola Eğ", isActive: true);
                break;

            case MouseButton.WheelTiltRight:
                _wheelTiltRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(-16.0, TimeSpan.FromMilliseconds(60)) { EasingFunction = PressEase });
                ShowHud("▶ Tekerlek Sağa Eğ", isActive: true);
                break;
        }

        // Light up wheel rim & tire
        AnimateColor(_wheelEmissive, ActiveWheelEmissive, 40);

        ScheduleMomentaryRelease(MouseButton.WheelUp, 190, () =>
        {
            if (!_pressedButtons.Contains(MouseButton.Middle))
            {
                var targetColor = _hoveredButton == MouseButton.Middle ? HoverEmissive : IdleEmissive;
                AnimateColor(_wheelEmissive, targetColor, 180);
            }
            _wheelTiltRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = PressEase });
        });
    }

    private void ApplyButtonVisual(MouseButton button, bool down)
    {
        int ms = down ? 40 : 150;
        switch (button)
        {
            case MouseButton.Left:
                _leftPressRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(down ? -2.8 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_leftEmissive, down ? ActivePrimaryEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;

            case MouseButton.Right:
                _rightPressRot.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                    new DoubleAnimation(down ? -2.8 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_rightEmissive, down ? ActivePrimaryEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;

            case MouseButton.Middle:
                _wheelPressTrans.BeginAnimation(TranslateTransform3D.OffsetYProperty,
                    new DoubleAnimation(down ? -0.042 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_wheelEmissive, down ? ActiveWheelEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;

            case MouseButton.DpiButton:
                _dpiPressTrans.BeginAnimation(TranslateTransform3D.OffsetYProperty,
                    new DoubleAnimation(down ? -0.032 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_dpiEmissive, down ? ActiveDpiEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;

            case MouseButton.XButton2:
                _x2PressTrans.BeginAnimation(TranslateTransform3D.OffsetXProperty,
                    new DoubleAnimation(down ? 0.048 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_x2Emissive, down ? ActiveSideEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;

            case MouseButton.XButton1:
                _x1PressTrans.BeginAnimation(TranslateTransform3D.OffsetXProperty,
                    new DoubleAnimation(down ? 0.048 : 0.0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = PressEase });
                AnimateColor(_x1Emissive, down ? ActiveSideEmissive : (_hoveredButton == button ? HoverEmissive : IdleEmissive), ms);
                break;
        }
    }

    private void ScheduleMomentaryRelease(MouseButton key, int delayMs, Action onRelease)
    {
        if (_momentaryTimers.TryGetValue(key, out var existing))
        {
            existing.Stop();
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            onRelease();
        };
        _momentaryTimers[key] = timer;
        timer.Start();
    }

    // =========================================================
    // CAMERA ORBIT, PRESETS & 3D HIT-TESTING
    // =========================================================

    private void UpdateCamera()
    {
        double yawRad = _yawDeg * Math.PI / 180.0;
        double pitchRad = _pitchDeg * Math.PI / 180.0;

        double cosP = Math.Cos(pitchRad);
        double x = CameraTarget.X + CameraDistance * cosP * Math.Sin(yawRad);
        double y = CameraTarget.Y + CameraDistance * Math.Sin(pitchRad);
        double z = CameraTarget.Z + CameraDistance * cosP * Math.Cos(yawRad);

        var pos = new Point3D(x, y, z);
        Camera.Position = pos;
        Camera.LookDirection = CameraTarget - pos;
    }

    private void AnimateCameraTo(double targetYaw, double targetPitch)
    {
        _camAnimTimer?.Stop();
        _camFromYaw = _yawDeg;
        _camFromPitch = _pitchDeg;
        _camToYaw = targetYaw;
        _camToPitch = targetPitch;
        _camAnimStart = DateTime.UtcNow;

        _camAnimTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(12)
        };
        _camAnimTimer.Tick += (_, _) =>
        {
            double t = Math.Clamp((DateTime.UtcNow - _camAnimStart).TotalMilliseconds / CamAnimMs, 0.0, 1.0);
            double ease = 1.0 - Math.Pow(1.0 - t, 3.0);
            _yawDeg = _camFromYaw + (_camToYaw - _camFromYaw) * ease;
            _pitchDeg = _camFromPitch + (_camToPitch - _camFromPitch) * ease;
            UpdateCamera();
            if (t >= 1.0) _camAnimTimer.Stop();
        };
        _camAnimTimer.Start();
    }

    private void BtnViewDefault_Click(object sender, RoutedEventArgs e) => AnimateCameraTo(-42.0, 38.0);
    private void BtnViewSide_Click(object sender, RoutedEventArgs e) => AnimateCameraTo(-78.0, 18.0);
    private void BtnViewTop_Click(object sender, RoutedEventArgs e) => AnimateCameraTo(0.0, 82.0);

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _camAnimTimer?.Stop();
        _isDragging = true;
        _dragStart = e.GetPosition(Viewport);
        _dragStartYaw = _yawDeg;
        _dragStartPitch = _pitchDeg;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(Viewport);

        if (_isDragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndOrbitDrag();
                return;
            }
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;
            _yawDeg = _dragStartYaw - dx * 0.45;
            _pitchDeg = Math.Clamp(_dragStartPitch + dy * 0.38, -32.0, 85.0);
            UpdateCamera();
            return;
        }

        UpdateHoverButton(pos);
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndOrbitDrag();

    private void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        EndOrbitDrag();
        SetHoveredButton(MouseButton.None);
    }

    private void EndOrbitDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
    }

    private void UpdateHoverButton(Point pos)
    {
        MouseButton hitButton = MouseButton.None;
        VisualTreeHelper.HitTest(Viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                meshHit.ModelHit is GeometryModel3D gm &&
                _hitMap.TryGetValue(gm, out var mapped))
            {
                hitButton = mapped;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(pos));

        SetHoveredButton(hitButton);
    }

    private void SetHoveredButton(MouseButton button)
    {
        if (_hoveredButton == button) return;
        MouseButton prev = _hoveredButton;
        _hoveredButton = button;

        if (prev != MouseButton.None && !_pressedButtons.Contains(prev))
        {
            AnimateEmissiveFor(prev, IdleEmissive, 140);
        }

        if (button != MouseButton.None)
        {
            if (!_pressedButtons.Contains(button))
            {
                AnimateEmissiveFor(button, HoverEmissive, 90);
            }
            ShowHud(HoverDisplayName(button), isActive: false);
        }
        else if (_hudHideTimer is not { IsEnabled: true })
        {
            HideHud();
        }
    }

    private void AnimateEmissiveFor(MouseButton button, Color target, int ms)
    {
        SolidColorBrush? br = button switch
        {
            MouseButton.Left => _leftEmissive,
            MouseButton.Right => _rightEmissive,
            MouseButton.Middle => _wheelEmissive,
            MouseButton.DpiButton => _dpiEmissive,
            MouseButton.XButton1 => _x1Emissive,
            MouseButton.XButton2 => _x2Emissive,
            _ => null
        };
        if (br != null) AnimateColor(br, target, ms);
    }

    private static string HoverDisplayName(MouseButton b) => b switch
    {
        MouseButton.Left => "Sol Tuş (Left)",
        MouseButton.Right => "Sağ Tuş (Right)",
        MouseButton.Middle => "Tekerlek / Orta Tuş (Middle)",
        MouseButton.DpiButton => "DPI Tuşu",
        MouseButton.XButton1 => "Yan Tuş 1 · Geri (X1)",
        MouseButton.XButton2 => "Yan Tuş 2 · İleri (X2)",
        _ => ""
    };

    private void ShowHud(string text, bool isActive)
    {
        HudText.Text = text;
        HudDot.Fill = isActive
            ? new SolidColorBrush(Color.FromRgb(0x3B, 0x8E, 0xEA))
            : new SolidColorBrush(Color.FromRgb(0x7D, 0x90, 0xA8));
        HudBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(90)));

        _hudHideTimer?.Stop();
        if (isActive)
        {
            _hudHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _hudHideTimer.Tick += (_, _) =>
            {
                _hudHideTimer.Stop();
                if (_hoveredButton != MouseButton.None) ShowHud(HoverDisplayName(_hoveredButton), isActive: false);
                else HideHud();
            };
            _hudHideTimer.Start();
        }
    }

    private void HideHud()
    {
        HudBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)));
    }

    // =========================================================
    // MATERIAL & ANIMATION HELPERS
    // =========================================================

    private static MaterialGroup CreateSatinMaterial(Brush diffuseBrush, double specOpacity, double specPower)
    {
        byte a = (byte)(Math.Clamp(specOpacity, 0, 1) * 255);
        var mg = new MaterialGroup();
        mg.Children.Add(new DiffuseMaterial(diffuseBrush));
        mg.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)), specPower));
        mg.Freeze();
        return mg;
    }

    private static MaterialGroup CreateFlatMaterial(Color color, double specOpacity, double specPower)
    {
        return CreateSatinMaterial(new SolidColorBrush(color), specOpacity, specPower);
    }

    private static MaterialGroup CreateEmissiveAccentMaterial(Color diffuse, Color emissive, double specOpacity, double specPower)
    {
        byte a = (byte)(Math.Clamp(specOpacity, 0, 1) * 255);
        var mg = new MaterialGroup();
        mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(diffuse)));
        mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(emissive)));
        mg.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)), specPower));
        mg.Freeze();
        return mg;
    }

    private static MaterialGroup CreateInteractiveMaterial(Brush diffuseBrush, SolidColorBrush emissiveBrush, double specOpacity, double specPower)
    {
        byte a = (byte)(Math.Clamp(specOpacity, 0, 1) * 255);
        return new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(diffuseBrush),
                new EmissiveMaterial(emissiveBrush),
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)), specPower)
            }
        };
    }

    private static MaterialGroup CreateInteractiveFlatMaterial(Color color, SolidColorBrush emissiveBrush, double specOpacity, double specPower)
    {
        return CreateInteractiveMaterial(new SolidColorBrush(color), emissiveBrush, specOpacity, specPower);
    }

    private static void AnimateColor(SolidColorBrush brush, Color to, int durationMs)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = PressEase });
    }
}
