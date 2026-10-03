using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Macria;

/// <summary>
/// Prepares a STEP's 3D model in the background, once per file: the viewer DLL
/// reads and tessellates it into its process-wide cache, which every
/// Step3BPaneli and the "Büyük Aç" window then share. A panel asked to show a
/// file still being prepared waits for it ("3B hazırlanıyor…") instead of
/// reading the file itself on the UI thread.
/// </summary>
internal static class Step3BModelHazirlayici
{
    private static readonly object Kilit = new();
    private static readonly Dictionary<string, Task<string?>> Gorevler = new(StringComparer.OrdinalIgnoreCase);
    private static OcctViewerNative? _native;
    private static bool _yuklemeDenendi;

    /// <summary>Console line when a model is ready or fails ("3B model hazır: … (4,3 sn)").</summary>
    public static Action<string, bool>? Gunluk { get; set; }

    /// <summary>The preparation of `path`, or null when none was started.</summary>
    public static Task<string?>? Gorev(string? path)
    {
        string? anahtar = Anahtar(path);
        if (anahtar == null) return null;
        lock (Kilit)
            return Gorevler.TryGetValue(anahtar, out Task<string?>? gorev) ? gorev : null;
    }

    /// <summary>
    /// Starts preparing `path` in the background (once); the task's result is
    /// null on success or the error message. Without a viewer DLL that supports
    /// preloading nothing is started and null is returned.
    /// </summary>
    public static Task<string?>? Hazirla(string path)
    {
        string? anahtar = Anahtar(path);
        if (anahtar == null || !File.Exists(anahtar)) return null;
        lock (Kilit)
        {
            if (Gorevler.TryGetValue(anahtar, out Task<string?>? mevcut)) return mevcut;
            OcctViewerNative? native = Native();
            if (native is not { SupportsPreload: true }) return null;
            Task<string?> gorev = Task.Run(() =>
            {
                var sure = Stopwatch.StartNew();
                string? hata = native.PreloadStep(anahtar, out string error) ? null : error;
                sure.Stop();
                string ad = Path.GetFileName(anahtar);
                Gunluk?.Invoke(hata == null
                    ? "3B model hazır: " + ad + " (" + sure.Elapsed.TotalSeconds.ToString("0.0") + " sn)"
                    : "3B model hazırlanamadı: " + ad + " — " + hata, hata == null);
                return hata;
            });
            Gorevler[anahtar] = gorev;
            return gorev;
        }
    }

    /// <summary>
    /// Forgets the prepared models (a new analysis starts). Panels keep the
    /// models they show; the cache is released once running preparations end.
    /// </summary>
    public static void Temizle()
    {
        Task<string?>[] bekleyen;
        lock (Kilit)
        {
            bekleyen = new Task<string?>[Gorevler.Count];
            Gorevler.Values.CopyTo(bekleyen, 0);
            Gorevler.Clear();
        }
        OcctViewerNative? native = _native;
        if (native == null) return;
        Task.WhenAll(bekleyen).ContinueWith(_ =>
        {
            lock (Kilit)
            {
                // A preparation started after Temizle must keep its model.
                if (Gorevler.Count == 0) native.ReleaseModels();
            }
        }, TaskScheduler.Default);
    }

    private static OcctViewerNative? Native()
    {
        if (_native != null || _yuklemeDenendi) return _native;
        _yuklemeDenendi = true;
        // Kept for the process lifetime: the cache lives in the loaded DLL.
        return OcctViewerNative.TryLoad(out _native, out _) ? _native : null;
    }

    private static string? Anahtar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception) { return null; }
    }
}
