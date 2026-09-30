using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Macria;

internal sealed class OcctViewerNative : IDisposable
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private const int ErrorBufferLength = 1024;
    private const string ViewerFileName = "Macria.GeometryViewer.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate IntPtr CreateDelegate(IntPtr windowHandle, [Out] StringBuilder error, int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDelegate(IntPtr viewerHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int LoadStepDelegate(
        IntPtr viewerHandle,
        [MarshalAs(UnmanagedType.LPWStr)] string stepPath,
        [Out] StringBuilder error,
        int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int SimpleCommandDelegate(IntPtr viewerHandle, [Out] StringBuilder error, int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int HighlightPartDelegate(
        IntPtr viewerHandle,
        [MarshalAs(UnmanagedType.LPWStr)] string partName,
        [Out] StringBuilder error,
        int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int ShowPartDelegate(
        IntPtr viewerHandle,
        [MarshalAs(UnmanagedType.LPWStr)] string partName,
        int partView,
        [Out] StringBuilder error,
        int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int SetViewDelegate(IntPtr viewerHandle, int view, [Out] StringBuilder error, int errorLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProcessMessageDelegate(
        IntPtr viewerHandle,
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int GetDiagnosticsDelegate(
        IntPtr viewerHandle,
        [Out] StringBuilder diagnostic,
        int diagnosticLength);

    private IntPtr _module;
    private readonly CreateDelegate _create;
    private readonly DestroyDelegate _destroy;
    private readonly LoadStepDelegate _loadStep;
    private readonly SimpleCommandDelegate _clear;
    private readonly SimpleCommandDelegate _resize;
    private readonly SimpleCommandDelegate _fitAll;
    private readonly SetViewDelegate _setView;
    private readonly ProcessMessageDelegate _processMessage;
    private readonly GetDiagnosticsDelegate _getDiagnostics;
    // Optional: an older viewer DLL without it still previews, only without highlighting.
    private readonly HighlightPartDelegate? _highlightPart;
    private readonly ShowPartDelegate? _showPart;

    private OcctViewerNative(IntPtr module)
    {
        _module = module;
        _create = GetExport<CreateDelegate>("MacriaGeometryViewer_Create");
        _destroy = GetExport<DestroyDelegate>("MacriaGeometryViewer_Destroy");
        _loadStep = GetExport<LoadStepDelegate>("MacriaGeometryViewer_LoadStep");
        _clear = GetExport<SimpleCommandDelegate>("MacriaGeometryViewer_Clear");
        _resize = GetExport<SimpleCommandDelegate>("MacriaGeometryViewer_Resize");
        _fitAll = GetExport<SimpleCommandDelegate>("MacriaGeometryViewer_FitAll");
        _setView = GetExport<SetViewDelegate>("MacriaGeometryViewer_SetView");
        _processMessage = GetExport<ProcessMessageDelegate>("MacriaGeometryViewer_ProcessMessage");
        _getDiagnostics = GetExport<GetDiagnosticsDelegate>("MacriaGeometryViewer_GetDiagnostics");
        IntPtr highlight = GetProcAddress(_module, "MacriaGeometryViewer_HighlightPart");
        _highlightPart = highlight == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<HighlightPartDelegate>(highlight);
        // Newer DLLs only; older ones fall back to HighlightPart (faded view).
        IntPtr showPart = GetProcAddress(_module, "MacriaGeometryViewer_ShowPart");
        _showPart = showPart == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<ShowPartDelegate>(showPart);
    }

    public bool SupportsHighlight => _highlightPart != null;
    public bool SupportsShowPart => _showPart != null;

    public static bool TryLoad(out OcctViewerNative? native, out string error)
    {
        native = null;
        string viewerPath = Path.Combine(AppContext.BaseDirectory, "GeometryEngine", ViewerFileName);
        if (!File.Exists(viewerPath))
        {
            error = "3B önizleme DLL'i bulunamadı: " + viewerPath;
            return false;
        }

        IntPtr module = LoadLibraryEx(
            viewerPath,
            IntPtr.Zero,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
        if (module == IntPtr.Zero)
        {
            error = "3B önizleme DLL'i veya bağımlılıklarından biri yüklenemedi. " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        try
        {
            native = new OcctViewerNative(module);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            FreeLibrary(module);
            error = "3B önizleme API'si doğrulanamadı: " + exception.Message;
            return false;
        }
    }

    public IntPtr Create(IntPtr windowHandle, out string error)
    {
        StringBuilder buffer = NewErrorBuffer();
        IntPtr result = _create(windowHandle, buffer, buffer.Capacity);
        error = buffer.ToString();
        return result;
    }

    public void Destroy(IntPtr viewerHandle)
    {
        if (viewerHandle != IntPtr.Zero) _destroy(viewerHandle);
    }

    public bool LoadStep(IntPtr viewerHandle, string path, out string error) =>
        Invoke(buffer => _loadStep(viewerHandle, path, buffer, buffer.Capacity), out error);

    public bool Clear(IntPtr viewerHandle, out string error) =>
        Invoke(buffer => _clear(viewerHandle, buffer, buffer.Capacity), out error);

    public bool Resize(IntPtr viewerHandle, out string error) =>
        Invoke(buffer => _resize(viewerHandle, buffer, buffer.Capacity), out error);

    public bool FitAll(IntPtr viewerHandle, out string error) =>
        Invoke(buffer => _fitAll(viewerHandle, buffer, buffer.Capacity), out error);

    /// <summary>Highlights every instance of the named assembly part; "" restores the model.</summary>
    public bool ShowPart(IntPtr viewerHandle, string partName, OcctPartView view, out string error)
    {
        if (_showPart == null)
        {
            error = "3B önizleme DLL'i yalnız parça görünümünü desteklemiyor.";
            return false;
        }
        return Invoke(buffer => _showPart(viewerHandle, partName, (int)view, buffer, buffer.Capacity), out error);
    }

    public bool HighlightPart(IntPtr viewerHandle, string partName, out string error)
    {
        if (_highlightPart == null)
        {
            error = "3B önizleme DLL'i parça vurgulamayı desteklemiyor.";
            return false;
        }
        return Invoke(buffer => _highlightPart(viewerHandle, partName, buffer, buffer.Capacity), out error);
    }

    public bool SetView(IntPtr viewerHandle, OcctStandardView view, out string error) =>
        Invoke(buffer => _setView(viewerHandle, (int)view, buffer, buffer.Capacity), out error);

    public bool ProcessMessage(IntPtr viewerHandle, IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam) =>
        _processMessage(viewerHandle, windowHandle, unchecked((uint)message), (UIntPtr)(nuint)wParam, lParam) != 0;

    public bool TryGetDiagnostics(IntPtr viewerHandle, out string diagnostics)
    {
        StringBuilder buffer = new(2048);
        bool succeeded = _getDiagnostics(viewerHandle, buffer, buffer.Capacity) != 0;
        diagnostics = buffer.ToString();
        return succeeded;
    }

    public void Dispose()
    {
        if (_module == IntPtr.Zero) return;
        FreeLibrary(_module);
        _module = IntPtr.Zero;
    }

    private bool Invoke(Func<StringBuilder, int> command, out string error)
    {
        StringBuilder buffer = NewErrorBuffer();
        bool succeeded = command(buffer) != 0;
        error = buffer.ToString();
        return succeeded;
    }

    private TDelegate GetExport<TDelegate>(string name) where TDelegate : Delegate
    {
        IntPtr address = GetProcAddress(_module, name);
        if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    private static StringBuilder NewErrorBuffer() => new(ErrorBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
}

/// <summary>How a selected assembly part is shown (MacriaGeometryPartView).</summary>
public enum OcctPartView
{
    /// <summary>Only the selected part; the rest of the assembly is hidden.</summary>
    Isolated = 0,
    /// <summary>The selected part highlighted, the rest of the assembly faded.</summary>
    InAssembly = 1
}

public enum OcctStandardView
{
    Isometric = 0,
    Front = 1,
    Back = 2,
    Left = 3,
    Right = 4,
    Top = 5,
    Bottom = 6
}
