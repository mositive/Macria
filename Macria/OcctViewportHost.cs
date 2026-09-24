using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Macria;

public enum OcctViewportState
{
    Initializing,
    Idle,
    Loading,
    Loaded,
    Unavailable,
    Error
}

public sealed class OcctViewportStatusChangedEventArgs(OcctViewportState state, string message) : EventArgs
{
    public OcctViewportState State { get; } = state;
    public string Message { get; } = message;
}

public sealed class OcctViewportDiagnosticEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class OcctViewportHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int SsNotify = 0x00000100;
    private const int GwlStyle = -16;

    private const int WmSetFocus = 0x0007;
    private const int WmKillFocus = 0x0008;
    private const int WmCancelMode = 0x001F;
    private const int WmCaptureChanged = 0x0215;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;

    private IntPtr _viewerHandle;
    private OcctViewerNative? _native;
    private string? _pendingPath;
    private string? _loadedPath;
    private bool _shuttingDown;
    private IntPtr _parentWindow;
    private IntPtr _childWindow;
    private ulong _leftDown;
    private ulong _leftUp;
    private ulong _middleDown;
    private ulong _middleUp;
    private ulong _mouseMove;
    private ulong _mouseWheel;
    private ulong _setFocus;
    private ulong _killFocus;
    private ulong _captureLost;

    public event EventHandler<OcctViewportStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<OcctViewportDiagnosticEventArgs>? Diagnostic;

    public OcctViewportState State { get; private set; } = OcctViewportState.Initializing;
    public string StatusMessage { get; private set; } = "3B önizleme hazırlanıyor...";
    public bool HasLoadedModel => State == OcctViewportState.Loaded;
    public string DiagnosticName { get; set; } = "OCCT 3B";

    public void LoadStep(string path)
    {
        if (_shuttingDown) return;

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception)
        {
            SetStatus(OcctViewportState.Error, "STEP önizleme yolu geçersiz: " + exception.Message);
            return;
        }

        if (string.Equals(_loadedPath, fullPath, StringComparison.OrdinalIgnoreCase) && HasLoadedModel)
        {
            SetStatus(OcctViewportState.Loaded, "3B önizleme hazır: " + Path.GetFileName(fullPath));
            return;
        }

        _pendingPath = fullPath;
        if (_viewerHandle == IntPtr.Zero)
        {
            if (State != OcctViewportState.Unavailable)
                SetStatus(OcctViewportState.Initializing, "3B önizleme hazırlanıyor...");
            return;
        }

        LoadPendingStep();
    }

    public void ClearModel()
    {
        _pendingPath = null;
        _loadedPath = null;
        if (_native != null && _viewerHandle != IntPtr.Zero && !_native.Clear(_viewerHandle, out string error))
        {
            SetStatus(OcctViewportState.Error, "3B önizleme temizlenemedi: " + error);
            return;
        }
        if (State is not OcctViewportState.Unavailable)
            SetStatus(OcctViewportState.Idle, "Önizlemek için listeden tek bir STEP seçin.");
    }

    public void FitAll() => InvokeViewerCommand((OcctViewerNative native, IntPtr handle, out string error) => native.FitAll(handle, out error));

    public void SetView(OcctStandardView view) =>
        InvokeViewerCommand((OcctViewerNative native, IntPtr handle, out string error) => native.SetView(handle, view, out error));

    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        Dispose();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _parentWindow = hwndParent.Handle;
        IntPtr childWindow = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren | SsNotify,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (childWindow == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "3B önizleme child HWND oluşturulamadı.");
        _childWindow = childWindow;

        if (!OcctViewerNative.TryLoad(out OcctViewerNative? native, out string loadError) || native == null)
        {
            SetStatusDeferred(OcctViewportState.Unavailable, loadError);
            return new HandleRef(this, childWindow);
        }

        _native = native;
        _viewerHandle = native.Create(childWindow, out string createError);
        if (_viewerHandle == IntPtr.Zero)
        {
            _native.Dispose();
            _native = null;
            SetStatusDeferred(OcctViewportState.Unavailable, "OCCT viewer başlatılamadı: " + createError);
            return new HandleRef(this, childWindow);
        }

        PublishDiagnosticsDeferred("HWND zinciri oluşturuldu", childWindow);
        SetStatusDeferred(OcctViewportState.Idle, "Önizlemek için listeden tek bir STEP seçin.");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(LoadPendingStep));
        return new HandleRef(this, childWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_native != null)
        {
            _native.Destroy(_viewerHandle);
            _viewerHandle = IntPtr.Zero;
            _native.Dispose();
            _native = null;
        }

        if (hwnd.Handle != IntPtr.Zero) DestroyWindow(hwnd.Handle);
        _childWindow = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        CountInputMessage(msg);

        bool baseHandled = false;
        IntPtr result = base.WndProc(hwnd, msg, wParam, lParam, ref baseHandled);
        bool viewerHandled = false;
        if (!baseHandled && _native != null && _viewerHandle != IntPtr.Zero)
        {
            try { viewerHandled = _native.ProcessMessage(_viewerHandle, hwnd, msg, wParam, lParam); }
            catch (Exception exception)
            {
                SetStatusDeferred(OcctViewportState.Error, "3B önizleme input hatası: " + exception.Message);
            }
        }
        handled = baseHandled || viewerHandled;

        if (ShouldPublishInputDiagnostics(msg))
            PublishDiagnosticsDeferred(MessageName(msg), hwnd);

        return result;
    }

    private void CountInputMessage(int message)
    {
        switch (message)
        {
            case WmLeftButtonDown: ++_leftDown; break;
            case WmLeftButtonUp: ++_leftUp; break;
            case WmMiddleButtonDown: ++_middleDown; break;
            case WmMiddleButtonUp: ++_middleUp; break;
            case WmMouseMove: ++_mouseMove; break;
            case WmMouseWheel: ++_mouseWheel; break;
            case WmSetFocus: ++_setFocus; break;
            case WmKillFocus: ++_killFocus; break;
            case WmCaptureChanged:
            case WmCancelMode: ++_captureLost; break;
        }
    }

    private static bool ShouldPublishInputDiagnostics(int message) => message is
        WmLeftButtonDown or WmLeftButtonUp or
        WmMiddleButtonDown or WmMiddleButtonUp or
        WmMouseWheel or WmSetFocus or WmKillFocus or
        WmCaptureChanged or WmCancelMode;

    private static string MessageName(int message) => message switch
    {
        WmLeftButtonDown => "WM_LBUTTONDOWN",
        WmLeftButtonUp => "WM_LBUTTONUP",
        WmMiddleButtonDown => "WM_MBUTTONDOWN",
        WmMiddleButtonUp => "WM_MBUTTONUP",
        WmMouseWheel => "WM_MOUSEWHEEL",
        WmSetFocus => "WM_SETFOCUS",
        WmKillFocus => "WM_KILLFOCUS",
        WmCaptureChanged => "WM_CAPTURECHANGED",
        WmCancelMode => "WM_CANCELMODE",
        _ => $"0x{message:X}"
    };

    private void PublishDiagnosticsDeferred(string reason, IntPtr messageWindow)
    {
        string native = _native != null
            && _viewerHandle != IntPtr.Zero
            && _native.TryGetDiagnostics(_viewerHandle, out string value)
                ? value
                : "Native teşhis kullanılamıyor.";
        string message =
            $"[{DiagnosticName}] {reason} | WPF HWND: parent={FormatHandle(_parentWindow)}, " +
            $"child/input={FormatHandle(_childWindow)}, messageTarget={FormatHandle(messageWindow)}, " +
            $"actualParent={FormatHandle(_childWindow == IntPtr.Zero ? IntPtr.Zero : GetParent(_childWindow))}, " +
            $"class=STATIC, style=0x{(_childWindow == IntPtr.Zero ? 0 : GetWindowLongPtr(_childWindow, GwlStyle).ToInt64()):X} | " +
            $"MouseInput: LBDown={_leftDown}, LBUp={_leftUp}, MBDown={_middleDown}, MBUp={_middleUp}, " +
            $"Move={_mouseMove}, Wheel={_mouseWheel}, SetFocus={_setFocus}, KillFocus={_killFocus}, " +
            $"CaptureLost={_captureLost} | {native}";
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            Diagnostic?.Invoke(this, new OcctViewportDiagnosticEventArgs(message))));
    }

    private static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private void LoadPendingStep()
    {
        if (_shuttingDown || _native == null || _viewerHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(_pendingPath))
            return;

        string path = _pendingPath;
        if (!File.Exists(path))
        {
            _loadedPath = null;
            SetStatus(OcctViewportState.Error, "STEP dosyası artık belirtilen konumda bulunmuyor.");
            return;
        }

        SetStatus(OcctViewportState.Loading, "3B önizleme yükleniyor: " + Path.GetFileName(path));
        if (_native.LoadStep(_viewerHandle, path, out string error))
        {
            _loadedPath = path;
            SetStatus(OcctViewportState.Loaded, "3B önizleme hazır: " + Path.GetFileName(path));
        }
        else
        {
            _loadedPath = null;
            SetStatus(OcctViewportState.Error, "STEP önizleme yüklenemedi: " + error);
        }
    }

    private delegate bool ViewerCommand(OcctViewerNative native, IntPtr handle, out string error);

    private void InvokeViewerCommand(ViewerCommand command)
    {
        if (_native == null || _viewerHandle == IntPtr.Zero || !HasLoadedModel) return;
        if (!command(_native, _viewerHandle, out string error))
            SetStatus(OcctViewportState.Error, "3B görünüş komutu başarısız: " + error);
    }

    private void SetStatusDeferred(OcctViewportState state, string message) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SetStatus(state, message)));

    private void SetStatus(OcctViewportState state, string message)
    {
        State = state;
        StatusMessage = message;
        StatusChanged?.Invoke(this, new OcctViewportStatusChangedEventArgs(state, message));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
