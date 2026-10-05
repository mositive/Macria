using System.IO;

namespace Macria;

/// <summary>
/// Third-party notices shown in the Hakkında window. The full texts are in
/// THIRD_PARTY_NOTICES.txt next to Macria.exe, licenses\ and
/// GeometryEngine\licenses\; the source code location here and in that file
/// must stay the same (checked by GeometryLabAdapter.Tests).
/// </summary>
public static class UcuncuTarafBildirimleri
{
    public const string DosyaAdi = "THIRD_PARTY_NOTICES.txt";

    /// <summary>Where the LGPL/FIPL source code is kept (OCCT, FFmpeg, FreeImage).</summary>
    public const string KaynakKoduYeri = "[iş yeri paylaşım klasörü – Enes ekleyecek]";

    /// <summary>The prominent notice the Open CASCADE exception asks for.</summary>
    public const string OcctBildirimi =
        "Macria, Open CASCADE Technology (OCCT) yazılımının sağladığı olanakları kullanır. " +
        "OCCT, GNU LGPL 2.1 ve Open CASCADE exception 1.0 ile lisanslıdır.";

    public const string Ozet =
        "FFmpeg 3.3.4 (LGPL 2.1), FreeImage 3.18.0 (FIPL 1.0), FreeType 2.13.3 (FTL), " +
        "oneTBB 2021.13.0 (Apache 2.0), jemalloc 5.3.0 (BSD), OpenVR 1.14.15 (BSD), " +
        "WPF-UI 4.3.0 (MIT), .NET 8 (MIT).";

    public static string KaynakKoduMetni =>
        "Open CASCADE Technology 8.0.1, FFmpeg 3.3.4 ve FreeImage 3.18.0 (LibRaw dahil) kaynak kodu:\n\n" +
        KaynakKoduYeri + "\n\n" +
        "LGPL kütüphaneleri GeometryEngine klasöründe ayrı DLL olarak dağıtılır; " +
        "aynı sürümün uyumlu bir derlemesiyle değiştirilebilir.";

    /// <summary>THIRD_PARTY_NOTICES.txt in the application folder, or null when it is missing.</summary>
    public static string? BildirimDosyasi(string uygulamaKlasoru)
    {
        string yol = Path.Combine(uygulamaKlasoru, DosyaAdi);
        return File.Exists(yol) ? yol : null;
    }
}
