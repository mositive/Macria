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
