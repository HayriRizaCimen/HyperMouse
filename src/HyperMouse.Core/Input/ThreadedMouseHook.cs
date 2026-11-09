using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using HyperMouse.Core.Common;
using HyperMouse.Core.Input.Win32;

namespace HyperMouse.Core.Input;

public sealed class ThreadedMouseHook : IMouseHook
{
    private readonly Channel<RawMouseEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();
    
    // Explicit references to prevent Garbage Collector from collecting delegates
    private readonly LowLevelMouseProc _mouseProcDelegate;
    private readonly LowLevelKeyboardProc _kbdProcDelegate;
    private GCHandle _mouseGcHandle;
    private GCHandle _kbdGcHandle;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private IntPtr _kbdHookHandle = IntPtr.Zero;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private bool _isDisposed;

    public event Action<RawMouseEvent>? MouseEventReceived;
    public MouseEventFilter? Filter { get; set; }
    public bool IsRunning => _mouseHookHandle != IntPtr.Zero && !_isDisposed;

    public ThreadedMouseHook()
    {
        _mouseProcDelegate = MouseHookCallback;
        _kbdProcDelegate = KeyboardHookCallback;

        _mouseGcHandle = GCHandle.Alloc(_mouseProcDelegate);
        _kbdGcHandle = GCHandle.Alloc(_kbdProcDelegate);

        // Bounded channel to prevent memory leaks if consumer stalls
        _eventChannel = Channel.CreateBounded<RawMouseEvent>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

        // Start background event consumer
        Task.Factory.StartNew(ProcessEventQueueAsync, TaskCreationOptions.LongRunning);
    }

    public void Start()
    {
        if (IsRunning) return;

        _hookThread = new Thread(HookThreadStart)
        {
            Name = "HyperMouse.HookThread",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };

        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        // Wait up to 3 seconds for hook installation
        if (!_startedEvent.Wait(3000) || _mouseHookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to install low-level mouse hook.");
        }
    }

    public void Stop()
    {
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessageW(_hookThreadId, NativeConstants.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(1500);
            _hookThread = null;
            _hookThreadId = 0;
            _mouseHookHandle = IntPtr.Zero;
            _kbdHookHandle = IntPtr.Zero;
            _startedEvent.Reset();
        }
    }

    private void HookThreadStart()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        if (moduleHandle == IntPtr.Zero)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            moduleHandle = curModule?.BaseAddress ?? IntPtr.Zero;
        }

        // Install WH_MOUSE_LL
        _mouseHookHandle = NativeMethods.SetWindowsHookExW(
            NativeConstants.WH_MOUSE_LL,
            _mouseProcDelegate,
            moduleHandle,
            0
        );

        // Install WH_KEYBOARD_LL (to catch gaming mice sending Browser Back / Browser Forward)
        _kbdHookHandle = NativeMethods.SetWindowsHookExW(
            NativeConstants.WH_KEYBOARD_LL,
            _kbdProcDelegate,
            moduleHandle,
            0
        );

        _startedEvent.Set();

        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        // Standard Win32 Message Pump for Low-Level Hooks
        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            if (msg.message == NativeConstants.WM_QUIT)
            {
                break;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        if (_kbdHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_kbdHookHandle);
            _kbdHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            
            // Fast skip for raw mouse movements to avoid overhead
            if (msg != NativeConstants.WM_MOUSEMOVE)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Anti-recursion check: ignore injected inputs from HyperMouse
                bool isInjectedByUs = hookStruct.dwExtraInfo == NativeConstants.HyperMouse_EXTRA_INFO;
                if (!isInjectedByUs)
                {
                    var rawEvent = ParseRawMouseEvent(msg, hookStruct);
                    if (rawEvent.Button != MouseButton.None)
                    {
                        // Synchronous filter to decide whether to suppress Windows event
                        bool suppress = Filter?.Invoke(rawEvent) ?? false;

                        // Non-blocking write into background channel
                        _eventChannel.Writer.TryWrite(rawEvent);

                        if (suppress)
                        {
                            // 1 suppresses the event from propagating to other apps
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Check if injected by HyperMouse
            bool isInjectedByUs = kbd.dwExtraInfo == NativeConstants.HyperMouse_EXTRA_INFO;
            if (!isInjectedByUs)
            {
                MouseButton button = MouseButton.None;
                if (kbd.vkCode == NativeConstants.VK_BROWSER_BACK)
                {
                    button = MouseButton.XButton1;
                }
                else if (kbd.vkCode == NativeConstants.VK_BROWSER_FORWARD)
                {
                    button = MouseButton.XButton2;
                }

                if (button != MouseButton.None)
                {
                    bool isDown = msg is NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN;
                    bool isUp = msg is NativeConstants.WM_KEYUP or NativeConstants.WM_SYSKEYUP;

                    if (isDown || isUp)
                    {
                        var rawEvent = new RawMouseEvent(
                            Button: button,
                            EventType: isDown ? MouseEventType.Down : MouseEventType.Up,
                            X: 0,
                            Y: 0,
                            Delta: 0,
                            TimestampMs: Environment.TickCount64,
                            Injected: (kbd.flags & NativeConstants.LLMHF_INJECTED) != 0,
                            ExtraInfo: kbd.dwExtraInfo
                        );

                        bool suppress = Filter?.Invoke(rawEvent) ?? false;
                        _eventChannel.Writer.TryWrite(rawEvent);

                        if (suppress)
                        {
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_kbdHookHandle, nCode, wParam, lParam);
    }

    private static RawMouseEvent ParseRawMouseEvent(int msg, MSLLHOOKSTRUCT hookStruct)
    {
        MouseButton button = MouseButton.None;
        MouseEventType eventType = MouseEventType.Move;
        int delta = 0;

        switch (msg)
        {
            // Left Button
            case NativeConstants.WM_LBUTTONDOWN:
            case NativeConstants.WM_NCLBUTTONDOWN:
                button = MouseButton.Left;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_LBUTTONUP:
            case NativeConstants.WM_NCLBUTTONUP:
                button = MouseButton.Left;
                eventType = MouseEventType.Up;
                break;

            // Right Button
            case NativeConstants.WM_RBUTTONDOWN:
            case NativeConstants.WM_NCRBUTTONDOWN:
                button = MouseButton.Right;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_RBUTTONUP:
            case NativeConstants.WM_NCRBUTTONUP:
                button = MouseButton.Right;
                eventType = MouseEventType.Up;
                break;

            // Middle Button
            case NativeConstants.WM_MBUTTONDOWN:
            case NativeConstants.WM_NCMBUTTONDOWN:
                button = MouseButton.Middle;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_MBUTTONUP:
            case NativeConstants.WM_NCMBUTTONUP:
                button = MouseButton.Middle;
                eventType = MouseEventType.Up;
                break;

            // XButtons (Thumb / Side Buttons)
            case NativeConstants.WM_XBUTTONDOWN:
            case NativeConstants.WM_NCXBUTTONDOWN:
            case NativeConstants.WM_XBUTTONDBLCLK:
            case NativeConstants.WM_NCXBUTTONDBLCLK:
            {
                ushort hiword = (ushort)((hookStruct.mouseData >> 16) & 0xFFFF);
                ushort loword = (ushort)(hookStruct.mouseData & 0xFFFF);
                button = ((hiword & NativeConstants.XBUTTON2) != 0 || hiword == 2 || loword == 2)
                    ? MouseButton.XButton2 
                    : MouseButton.XButton1;
                eventType = MouseEventType.Down;
                break;
            }

            case NativeConstants.WM_XBUTTONUP:
            case NativeConstants.WM_NCXBUTTONUP:
            {
                ushort hiword = (ushort)((hookStruct.mouseData >> 16) & 0xFFFF);
                ushort loword = (ushort)(hookStruct.mouseData & 0xFFFF);
                button = ((hiword & NativeConstants.XBUTTON2) != 0 || hiword == 2 || loword == 2)
                    ? MouseButton.XButton2 
                    : MouseButton.XButton1;
                eventType = MouseEventType.Up;
                break;
            }

            // Wheel
            case NativeConstants.WM_MOUSEWHEEL:
                short wheelDelta = unchecked((short)((hookStruct.mouseData >> 16) & 0xFFFF));
                delta = wheelDelta;
                eventType = MouseEventType.Wheel;
                button = wheelDelta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
                break;

            // Horizontal Wheel (Tilt)
            case NativeConstants.WM_MOUSEHWHEEL:
                short hWheelDelta = unchecked((short)((hookStruct.mouseData >> 16) & 0xFFFF));
                delta = hWheelDelta;
                eventType = MouseEventType.HWheel;
                button = hWheelDelta > 0 ? MouseButton.WheelTiltRight : MouseButton.WheelTiltLeft;
                break;
        }

        bool isInjected = (hookStruct.flags & NativeConstants.LLMHF_INJECTED) != 0;

        return new RawMouseEvent(
            Button: button,
            EventType: eventType,
            X: hookStruct.pt.X,
            Y: hookStruct.pt.Y,
            Delta: delta,
            TimestampMs: Environment.TickCount64,
            Injected: isInjected,
            ExtraInfo: hookStruct.dwExtraInfo
        );
    }

    private async Task ProcessEventQueueAsync()
    {
        var reader = _eventChannel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var ev))
                {
                    try
                    {
                        MouseEventReceived?.Invoke(ev);
                    }
                    catch
                    {
                        // Catch observer exceptions so background consumer never terminates
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        _cts.Cancel();
        _cts.Dispose();
        _startedEvent.Dispose();

        if (_mouseGcHandle.IsAllocated)
        {
            _mouseGcHandle.Free();
        }

        if (_kbdGcHandle.IsAllocated)
        {
            _kbdGcHandle.Free();
        }
    }
}
