using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

public sealed class OcctViewportDiagnosticEventArgs(string message, bool isTrace = false) : EventArgs
{
    public string Message { get; } = message;

    /// <summary>
    /// Input/HWND trace (WM_* messages); for the console only, never a
    /// notification. False for real problems such as a failed highlight.
    /// </summary>
    public bool IsTrace { get; } = isTrace;
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
    // Assembly part to show once the model is loaded; "" = none.
    private string _pendingHighlight = "";
    private OcctPartView _pendingPartView = OcctPartView.InAssembly;
    // What the native viewer shows now; "" = the plain model.
    private string _appliedPart = "";
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
    // WPF elements drawn over the viewport (e.g. notification cards). The
    // native child HWND always paints above WPF content in the same window,
    // so their areas are cut out of the child window's region.
    private readonly List<FrameworkElement> _overlays = new();
    private string _clipKey = "";

    /// <summary>
    /// Publishes WM_* input traces through <see cref="Diagnostic"/>; off by
    /// default because every click and wheel notch produces one.
    /// </summary>
    public static bool InputTraceEnabled { get; set; }

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

        // A different file starts without the previous part highlighted.
        _pendingHighlight = "";
        _appliedPart = "";
        _pendingPath = fullPath;
        if (_viewerHandle == IntPtr.Zero)
        {
            if (State != OcctViewportState.Unavailable)
                SetStatus(OcctViewportState.Initializing, "3B önizleme hazırlanıyor...");
            return;
        }

        LoadPendingStep();
    }

    /// <summary>
    /// Highlights every instance of the named assembly part in the loaded (or
    /// next loaded) model and fades the rest; null or "" shows the plain model.
    /// </summary>
    public void HighlightPart(string? partName) => ShowPart(partName, OcctPartView.InAssembly);

    /// <summary>
    /// Shows the named assembly part alone (<see cref="OcctPartView.Isolated"/>)
    /// or highlighted in the faded assembly, in the loaded (or next loaded)
    /// model; null or "" shows the plain model.
    /// </summary>
    public void ShowPart(string? partName, OcctPartView view)
    {
        _pendingHighlight = partName ?? "";
        _pendingPartView = view;
        ApplyPendingHighlight();
    }

    private void ApplyPendingHighlight()
    {
        if (_shuttingDown || _native == null || _viewerHandle == IntPtr.Zero || !HasLoadedModel) return;
        // Nothing shown and nothing asked: leave the camera alone.
        if (_pendingHighlight.Length == 0 && _appliedPart.Length == 0) return;

        bool ok;
        string error;
        if (_native.SupportsShowPart)
        {
            ok = _native.ShowPart(_viewerHandle, _pendingHighlight, _pendingPartView, out error);
        }
        else if (_native.SupportsHighlight)
        {
            // Older DLL: only the faded assembly view exists.
            ok = _native.HighlightPart(_viewerHandle, _pendingHighlight, out error);
            if (ok && _pendingPartView == OcctPartView.Isolated && _pendingHighlight.Length > 0)
                Diagnostic?.Invoke(this, new OcctViewportDiagnosticEventArgs(
                    DiagnosticName + ": yalnız parça görünümü bu DLL'de yok; parça montaj içinde gösteriliyor."));
        }
        else
        {
            if (_pendingHighlight.Length > 0)
                Diagnostic?.Invoke(this, new OcctViewportDiagnosticEventArgs(DiagnosticName + ": parça vurgulama bu DLL'de yok."));
            return;
        }

        if (ok)
        {
            _appliedPart = _pendingHighlight;
            return;
        }
        // The native side drops the previous highlight before it fails.
        _appliedPart = "";
        Diagnostic?.Invoke(this, new OcctViewportDiagnosticEventArgs(
            DiagnosticName + ": \"" + _pendingHighlight + "\" gösterilemedi: " + error));
    }

    public void ClearModel()
    {
        _pendingPath = null;
        _loadedPath = null;
        _pendingHighlight = "";
        _appliedPart = "";
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
        LayoutUpdated -= OnLayoutUpdated;
        Dispose();
    }

    /// <summary>
    /// Keeps the viewport from painting over <paramref name="overlay"/>; for a
    /// panel, each visible child is cut out separately.
    /// </summary>
    public void AddOverlay(FrameworkElement overlay)
    {
        if (!_overlays.Contains(overlay)) _overlays.Add(overlay);
        UpdateClipRegion();
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        // The OCCT view only renders at its old size until it is told the
        // window changed; do it here instead of relying on WM_SIZE alone.
        if (!_shuttingDown && _native != null && _viewerHandle != IntPtr.Zero)
            _native.Resize(_viewerHandle, out _);
        UpdateClipRegion();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateClipRegion();

    // A child HWND ignores WPF clipping and z-order: it paints its whole
    // rectangle above everything WPF draws in the window. The region limits it
    // to the part its WPF ancestors actually show, minus the overlays.
    private void UpdateClipRegion()
    {
        if (_shuttingDown || _childWindow == IntPtr.Zero) return;
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null || ActualWidth <= 0 || ActualHeight <= 0) return;

        Rect full = new(0, 0, ActualWidth, ActualHeight);
        Rect visible = full;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this);
             parent is Visual && !visible.IsEmpty;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement ancestor && TryGetBounds(ancestor, out Rect bounds))
                visible.Intersect(bounds);
        }

        var holes = new List<Rect>();
        if (!visible.IsEmpty)
        {
            foreach (FrameworkElement overlay in _overlays)
            {
                if (!overlay.IsVisible) continue;
                if (overlay is Panel panel)
                {
                    foreach (UIElement child in panel.Children)
                        AddHole(child, visible, holes);
                }
                else
                {
                    AddHole(overlay, visible, holes);
                }
            }
        }

        Matrix toDevice = source.CompositionTarget.TransformToDevice;
        bool whole = holes.Count == 0 && !visible.IsEmpty &&
                     Math.Abs(visible.Width - full.Width) < 0.5 && Math.Abs(visible.Height - full.Height) < 0.5;
        int[] area = visible.IsEmpty ? new[] { 0, 0, 0, 0 } : ToDevice(visible, toDevice, false);
        var keyParts = new List<string> { whole ? "whole" : string.Join(",", area) };
        var holeAreas = new List<int[]>();
        foreach (Rect hole in holes)
        {
            int[] device = ToDevice(hole, toDevice, true);
            holeAreas.Add(device);
            keyParts.Add(string.Join(",", device));
        }
        string key = string.Join(";", keyParts);
        if (key == _clipKey) return;
        _clipKey = key;

        if (whole)
        {
            SetWindowRgn(_childWindow, IntPtr.Zero, true);
            return;
        }

        IntPtr region = CreateRectRgn(area[0], area[1], area[2], area[3]);
        if (region == IntPtr.Zero) return;
        foreach (int[] hole in holeAreas)
        {
            IntPtr cut = CreateRectRgn(hole[0], hole[1], hole[2], hole[3]);
            if (cut == IntPtr.Zero) continue;
            CombineRgn(region, region, cut, RgnDiff);
            DeleteObject(cut);
        }
        // On success the system owns the region.
        if (SetWindowRgn(_childWindow, region, true) == 0)
            DeleteObject(region);
    }

    private void AddHole(UIElement element, Rect visible, List<Rect> holes)
    {
        if (!element.IsVisible || element.Opacity <= 0) return;
        if (!TryGetBounds(element, out Rect bounds)) return;
        bounds.Intersect(visible);
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0) holes.Add(bounds);
    }

    private bool TryGetBounds(UIElement element, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0) return false;
        try
        {
            bounds = element.TransformToVisual(this).TransformBounds(new Rect(element.RenderSize));
            return true;
        }
        catch (InvalidOperationException)
        {
            // Not in the same visual tree (e.g. a closed or detached element).
            return false;
        }
    }

    // Visible area rounds inward, holes outward, so no native pixel is left
    // over an overlay.
    private static int[] ToDevice(Rect rect, Matrix toDevice, bool outward)
    {
        double left = rect.Left * toDevice.M11, top = rect.Top * toDevice.M22;
        double right = rect.Right * toDevice.M11, bottom = rect.Bottom * toDevice.M22;
        return outward
            ? new[] { (int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom) }
            : new[] { (int)Math.Ceiling(left), (int)Math.Ceiling(top), (int)Math.Floor(right), (int)Math.Floor(bottom) };
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
        _clipKey = "";
        LayoutUpdated -= OnLayoutUpdated;
        LayoutUpdated += OnLayoutUpdated;

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

        if (InputTraceEnabled) PublishDiagnosticsDeferred("HWND zinciri oluşturuldu", childWindow);
        SetStatusDeferred(OcctViewportState.Idle, "Önizlemek için listeden tek bir STEP seçin.");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(LoadPendingStep));
        return new HandleRef(this, childWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        LayoutUpdated -= OnLayoutUpdated;
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

        if (InputTraceEnabled && ShouldPublishInputDiagnostics(msg))
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
            Diagnostic?.Invoke(this, new OcctViewportDiagnosticEventArgs(message, isTrace: true))));
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
            ApplyPendingHighlight();
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

    private const int RgnDiff = 4;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
