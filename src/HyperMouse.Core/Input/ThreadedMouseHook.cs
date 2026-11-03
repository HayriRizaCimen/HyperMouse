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
    
    // Explicit reference to prevent Garbage Collector from collecting the delegate
    private readonly LowLevelMouseProc _procDelegate;
    private GCHandle _gcHandle;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private bool _isDisposed;

    public event Action<RawMouseEvent>? MouseEventReceived;
    public MouseEventFilter? Filter { get; set; }
    public bool IsRunning => _hookHandle != IntPtr.Zero && !_isDisposed;

    public ThreadedMouseHook()
    {
        _procDelegate = HookCallback;
        _gcHandle = GCHandle.Alloc(_procDelegate);

        // Bounded channel to prevent memory leaks if consumer stalls
        _eventChannel = Channel.CreateBounded<RawMouseEvent>(new BoundedChannelOptions(1024)
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
        if (!_startedEvent.Wait(3000) || _hookHandle == IntPtr.Zero)
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
            _hookHandle = IntPtr.Zero;
            _startedEvent.Reset();
        }
    }

    private void HookThreadStart()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        
        // Load module handle
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr moduleHandle = curModule?.BaseAddress ?? IntPtr.Zero;

        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeConstants.WH_MOUSE_LL,
            _procDelegate,
            moduleHandle,
            0
        );

        if (_hookHandle == IntPtr.Zero)
        {
            _startedEvent.Set();
            return;
        }

        _startedEvent.Set();

        // Standard Win32 Message Pump for Low-Level Hook
        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            if (msg.message == NativeConstants.WM_QUIT)
            {
                break;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
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

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static RawMouseEvent ParseRawMouseEvent(int msg, MSLLHOOKSTRUCT hookStruct)
    {
        MouseButton button = MouseButton.None;
        MouseEventType eventType = MouseEventType.Move;
        int delta = 0;

        switch (msg)
        {
            case NativeConstants.WM_LBUTTONDOWN:
                button = MouseButton.Left;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_LBUTTONUP:
                button = MouseButton.Left;
                eventType = MouseEventType.Up;
                break;
            case NativeConstants.WM_RBUTTONDOWN:
                button = MouseButton.Right;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_RBUTTONUP:
                button = MouseButton.Right;
                eventType = MouseEventType.Up;
                break;
            case NativeConstants.WM_MBUTTONDOWN:
                button = MouseButton.Middle;
                eventType = MouseEventType.Down;
                break;
            case NativeConstants.WM_MBUTTONUP:
                button = MouseButton.Middle;
                eventType = MouseEventType.Up;
                break;
            case NativeConstants.WM_XBUTTONDOWN:
            case NativeConstants.WM_XBUTTONUP:
                int xButton = (int)((hookStruct.mouseData >> 16) & 0xFFFF);
                button = xButton == NativeConstants.XBUTTON1 ? MouseButton.XButton1 : MouseButton.XButton2;
                eventType = msg == NativeConstants.WM_XBUTTONDOWN ? MouseEventType.Down : MouseEventType.Up;
                break;
            case NativeConstants.WM_MOUSEWHEEL:
                short wheelDelta = unchecked((short)((hookStruct.mouseData >> 16) & 0xFFFF));
                delta = wheelDelta;
                eventType = MouseEventType.Wheel;
                button = wheelDelta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
                break;
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

        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }
    }
}

