using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Macria;

public enum GeometryLabEngineLocationKind
{
    Packaged,
    DevelopmentOverride,
    Unavailable
}

/// <summary>Result of locating the separately deployed GeometryEngine process.</summary>
public sealed record GeometryLabEngineLocation
{
    public required GeometryLabEngineLocationKind Kind { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ExpectedPackagedPath { get; init; }
    public string? Detail { get; init; }
    public bool IsAvailable => Kind != GeometryLabEngineLocationKind.Unavailable && !string.IsNullOrWhiteSpace(ExecutablePath);
}

/// <summary>
/// Which engine produced an analysis: the packaged version from
/// motor-surumu.txt ("2026.10.3.1" and the GeometryLab commit; null when the
/// file is missing) and the SHA-256 of the exe.
/// </summary>
public sealed record GeometryLabMotorKimligi
{
    public const string SurumDosyasi = "motor-surumu.txt";

    public string? Surum { get; init; }
    public string? Commit { get; init; }
    public string Sha256 { get; init; } = "";

    public string Gosterim => (Surum ?? "sürüm bilinmiyor") + (Commit is null ? "" : " (" + Commit + ")");

    private static readonly object Kilit = new();
    private static (string Yol, DateTime Zaman, GeometryLabMotorKimligi Kimlik)? _onbellek;

    /// <summary>Identity of the engine at `exePath`; the hash is cached until the exe changes.</summary>
    public static GeometryLabMotorKimligi Oku(string exePath)
    {
        string yol = Path.GetFullPath(exePath);
        DateTime zaman = File.GetLastWriteTimeUtc(yol);
        lock (Kilit)
        {
            if (_onbellek is { } o && o.Yol == yol && o.Zaman == zaman) return o.Kimlik;
        }
        string? surum = null, commit = null;
        string surumDosyasi = Path.Combine(Path.GetDirectoryName(yol) ?? "", SurumDosyasi);
        if (File.Exists(surumDosyasi))
        {
            string[] satirlar = File.ReadAllLines(surumDosyasi)
                .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToArray();
            if (satirlar.Length > 0 && Version.TryParse(satirlar[0], out _)) surum = satirlar[0];
            if (satirlar.Length > 1) commit = satirlar[1];
        }
        var kimlik = new GeometryLabMotorKimligi { Surum = surum, Commit = commit, Sha256 = DosyaSha256(yol) };
        lock (Kilit) _onbellek = (yol, zaman, kimlik);
        return kimlik;
    }

    /// <summary>
    /// True when this engine is newer than the one that made `onceki`, or is a
    /// different exe under the same (or an unknown) version.
    /// </summary>
    public bool OncekindenFarkli(GeometryLabMotorKimligi? onceki)
    {
        if (onceki is null) return true;
        if (Version.TryParse(Surum, out Version? bu) && Version.TryParse(onceki.Surum, out Version? o) && bu != o)
            return bu > o;
        return !string.Equals(Sha256, onceki.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static string DosyaSha256(string yol)
    {
        using FileStream akis = File.OpenRead(yol);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(akis));
    }
}

/// <summary>
/// Finds the app-local GeometryEngine installation. It deliberately never scans drives,
/// reads the registry, or presents a user-facing file picker.
/// </summary>
public static class GeometryLabEngineLocator
{
    public const string DevelopmentOverrideEnvironmentVariable = "MACRIA_GEOMETRY_ENGINE_PATH";
    public const string EngineFileName = "Macria.GeometryEngine.exe";

    // These files are the verified x64 dependency closure of the separately packaged engine.
    public static readonly string[] RequiredRuntimeFileNames =
    {
        "TKDESTEP.dll", "TKXSBase.dll", "TKBO.dll", "TKShHealing.dll", "TKTopAlgo.dll",
        "TKBRep.dll", "TKGeomBase.dll", "TKG3d.dll", "TKG2d.dll", "TKMath.dll", "TKernel.dll",
        "TKDE.dll", "TKXCAF.dll", "TKLCAF.dll", "TKPrim.dll", "TKGeomAlgo.dll", "TKVCAF.dll",
        "TKV3d.dll", "TKService.dll", "TKCAF.dll", "TKCDF.dll", "TKMesh.dll", "TKHLR.dll",
        "tbb12.dll", "jemalloc.dll", "FreeImage.dll", "openvr_api.dll", "freetype.dll",
        "avcodec-57.dll", "avformat-57.dll", "avutil-55.dll", "swscale-4.dll",
        "MSVCP140.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll"
    };

    public static GeometryLabEngineLocation Locate() =>
        Locate(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(DevelopmentOverrideEnvironmentVariable));

    /// <summary>Explicit inputs keep packaging and test validation deterministic.</summary>
    public static GeometryLabEngineLocation Locate(string applicationBaseDirectory, string? developmentOverridePath)
    {
        string baseDirectory;
        try { baseDirectory = Path.GetFullPath(applicationBaseDirectory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new GeometryLabEngineLocation { Kind = GeometryLabEngineLocationKind.Unavailable, Detail = exception.Message };
        }

        string packagedPath = Path.Combine(baseDirectory, "GeometryEngine", EngineFileName);
        if (TryValidate(packagedPath, out string? packagedDetail))
            return new GeometryLabEngineLocation
            {
                Kind = GeometryLabEngineLocationKind.Packaged,
                ExecutablePath = packagedPath,
                ExpectedPackagedPath = packagedPath
            };

        if (!string.IsNullOrWhiteSpace(developmentOverridePath))
        {
            string developmentPath;
            try { developmentPath = Path.GetFullPath(developmentOverridePath); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Unavailable(packagedPath, "Geliştirme motor yolu geçersiz: " + exception.Message);
            }
            if (TryValidate(developmentPath, out string? developmentDetail))
                return new GeometryLabEngineLocation
                {
                    Kind = GeometryLabEngineLocationKind.DevelopmentOverride,
                    ExecutablePath = developmentPath,
                    ExpectedPackagedPath = packagedPath,
                    Detail = "Geliştirme/test ortam değişkeni kullanıldı."
                };
            return Unavailable(packagedPath,
                "Paketlenmiş motor: " + packagedDetail + " Geliştirme motoru: " + developmentDetail);
        }

        return Unavailable(packagedPath, packagedDetail);
    }

    private static GeometryLabEngineLocation Unavailable(string packagedPath, string? detail) => new()
    {
        Kind = GeometryLabEngineLocationKind.Unavailable,
        ExpectedPackagedPath = packagedPath,
        Detail = detail
    };

    private static bool TryValidate(string executablePath, out string? detail)
    {
        if (!File.Exists(executablePath))
        {
            detail = "Motor EXE bulunamadı: " + executablePath;
            return false;
        }

        string? directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            detail = "Motor klasörü belirlenemedi: " + executablePath;
            return false;
        }
        string[] missing = RequiredRuntimeFileNames
            .Where(file => !File.Exists(Path.Combine(directory, file)))
            .ToArray();
        if (missing.Length != 0)
        {
            detail = "Eksik motor çalışma dosyaları: " + string.Join(", ", missing);
            return false;
        }

        detail = null;
        return true;
    }
}
