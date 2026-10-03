using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Macria;

// .macria project file (docs/MACRIA_PROJE_DOSYASI_TASARIM.md): a ZIP with
// manifest.json, proje.json and, per source STEP, the engine's analysis.json
// and part DXFs. No WPF here; MainWindow.Proje.cs drives it and the tests
// call it directly.

public sealed class MacriaProjeManifest
{
    public string Format { get; set; } = MacriaProje.Format;
    public string SchemaVersion { get; set; } = MacriaProje.SemaSurumu;
    public string? Olusturan { get; set; }
    public DateTime Olusturulma { get; set; }
    public DateTime Kaydedilme { get; set; }
}

public sealed class MacriaProjeVerisi
{
    public MacriaProjeAyarlari Ayarlar { get; set; } = new();
    public List<MacriaProjeKaynagi> Kaynaklar { get; set; } = new();
    public List<MacriaProjeKarari> Kararlar { get; set; } = new();
    public List<MacriaProjeKarari> EslenemeyenKararlar { get; set; } = new();
}

public sealed class MacriaProjeAyarlari
{
    // Project settings: applied to this project when it is opened.
    public double LazerAzamiKalinlikMm { get; set; } = 20;
    public bool BukumBilgisiDxf { get; set; } = true;
    // Record only: what the last analysis ran with.
    public MacriaProjeAnalizAyarlari Analiz { get; set; } = new();
}

public sealed class MacriaProjeAnalizAyarlari
{
    public int ParcaSureSiniriSaniye { get; set; }
    public int MotorIsParcacigi { get; set; }
}

public sealed class MacriaProjeKaynagi
{
    public string Id { get; set; } = "";
    public string Tur { get; set; } = "step";
    public string Yol { get; set; } = "";
    public string? GoreliYol { get; set; }
    public string Sha256 { get; set; } = "";
    public long Boyut { get; set; }
    public DateTime Degistirilme { get; set; }
    public MacriaProjeAnalizi Analiz { get; set; } = new();
}

public sealed class MacriaProjeAnalizi
{
    public DateTime Zaman { get; set; }
    /// <summary>GeometryLabProcessAdapterStatus name; analysis.json is stored only for Succeeded.</summary>
    public string Durum { get; set; } = "";
    public string? Mesaj { get; set; }
    public string? MotorSemaSurumu { get; set; }
    public MacriaProjeMotoru? Motor { get; set; }
    public double? SureSn { get; set; }
    public bool Montaj { get; set; }
}

public sealed class MacriaProjeMotoru
{
    public string? Surum { get; set; }
    public string? Commit { get; set; }
    public string Sha256 { get; set; } = "";

    public static MacriaProjeMotoru Kimliktan(GeometryLabMotorKimligi kimlik) =>
        new() { Surum = kimlik.Surum, Commit = kimlik.Commit, Sha256 = kimlik.Sha256 };

    public GeometryLabMotorKimligi Kimlik() => new() { Surum = Surum, Commit = Commit, Sha256 = Sha256 };
}

public sealed class MacriaProjeKarari
{
    public string Kaynak { get; set; } = "";
    /// <summary>null: the file row of a single-part STEP.</summary>
    public MacriaParcaKimligi? Parca { get; set; }
    /// <summary>"profil" (Profiller row) or "montaj" (Saclar / Kontrol gerekli row).</summary>
    public string Hedef { get; set; } = "";
    public string Karar { get; set; } = "";
    public string? Not { get; set; }
    public MacriaElleProfil? ElleProfil { get; set; }
}

public sealed record MacriaParcaKimligi
{
    public int LocalId { get; set; }
    public string? Ad { get; set; }
    public string? ProductId { get; set; }
}

public sealed class MacriaElleProfil
{
    public string Tur { get; set; } = "";
    public string Kesit { get; set; } = "";
}

/// <summary>One source's stored content, for saving.</summary>
public sealed record MacriaProjeKaynakIcerigi(string? AnalysisJson, string? DxfKlasoru);

/// <summary>An opened project: data, stored engine output and the folders its DXFs were extracted to.</summary>
public sealed class MacriaProjeAcilisi
{
    public required string Yol { get; init; }
    public required MacriaProjeManifest Manifest { get; init; }
    public required MacriaProjeVerisi Veri { get; init; }
    /// <summary>Saved by a newer Macria (same major schema): opened, saving is off.</summary>
    public string? SaltOkunurNedeni { get; init; }
    public Dictionary<string, string> AnalysisJson { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> DxfKlasoru { get; } = new(StringComparer.Ordinal);
}

public sealed class MacriaProjeHatasi : Exception
{
    public MacriaProjeHatasi(string message, Exception? inner = null) : base(message, inner) { }
}

public enum MacriaKaynakDurumu
{
    /// <summary>Same STEP (SHA-256), same engine: no analysis needed.</summary>
    Ayni,
    /// <summary>Same STEP, but the installed engine is newer than the one that analysed it.</summary>
    MotorYeni,
    /// <summary>The STEP's content changed, or its stored engine output can no longer be read.</summary>
    Degismis,
    Bulunamadi
}

public sealed record MacriaKaynakDenetimi(MacriaKaynakDurumu Durum, string? BulunanYol, string? Aciklama);

/// <summary>A row a decision can be applied to.</summary>
public sealed record MacriaKararAdayi(string KaynakId, string Hedef, MacriaParcaKimligi? Parca, object Satir);

public static class MacriaProje
{
    public const string Format = "macria-proje";
    public const string SemaSurumu = "1.0";
    public const string Uzanti = ".macria";

    public const string HedefProfil = "profil";
    public const string HedefMontaj = "montaj";

    public const string KararProfilOnayla = "ProfilOnayla";
    public const string KararElleProfil = "ElleProfil";
    public const string KararIncelemeye = "Incelemeye";
    public const string KararListeDisi = "ListeDisi";
    public const string KararSacOnayla = "SacOnayla";
    public const string KararKontrole = "Kontrole";

    private const string ManifestAdi = "manifest.json";
    private const string ProjeAdi = "proje.json";
    // Opening refuses projects that would unpack beyond these (zip bomb).
    private const long AzamiAcilmisBoyut = 2L * 1024 * 1024 * 1024;
    private const int AzamiKayitSayisi = 200_000;

    private static readonly Regex AnalizKaydi = new(@"^kaynaklar/(k[0-9]{1,6})/analysis\.json$", RegexOptions.CultureInvariant);
    private static readonly Regex DxfKaydi = new(@"^kaynaklar/(k[0-9]{1,6})/dxf/([^/\\:*?""<>|]+\.dxf)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static readonly JsonSerializerOptions JsonAyarlari = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Default place to save: next to the first STEP, with its name ("WGRV004423 A.macria").</summary>
    public static string VarsayilanYol(string ilkStep) => Path.ChangeExtension(Path.GetFullPath(ilkStep), Uzanti);

    // ---------------------------------------------------------------- saving

    /// <summary>
    /// Writes the project to `hedef`: a temporary file next to it first, then
    /// replaced in one step, so a failed save leaves the previous file intact.
    /// </summary>
    public static void Kaydet(string hedef, MacriaProjeVerisi veri,
        IReadOnlyDictionary<string, MacriaProjeKaynakIcerigi> icerik, string olusturan, DateTime? olusturulma = null)
    {
        hedef = Path.GetFullPath(hedef);
        string klasor = Path.GetDirectoryName(hedef) ?? throw new MacriaProjeHatasi("Proje klasörü belirlenemedi.");
        foreach (MacriaProjeKaynagi kaynak in veri.Kaynaklar)
            kaynak.GoreliYol = GoreliYol(klasor, kaynak.Yol);

        DateTime simdi = DateTime.UtcNow;
        var manifest = new MacriaProjeManifest
        {
            Olusturan = olusturan,
            Olusturulma = olusturulma ?? simdi,
            Kaydedilme = simdi
        };
        string gecici = hedef + ".tmp";
        try
        {
            using (FileStream akis = new(gecici, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new(akis, ZipArchiveMode.Create))
            {
                YaziEkle(zip, ManifestAdi, JsonSerializer.Serialize(manifest, JsonAyarlari));
                YaziEkle(zip, ProjeAdi, JsonSerializer.Serialize(veri, JsonAyarlari));
                foreach (MacriaProjeKaynagi kaynak in veri.Kaynaklar)
                {
                    if (!icerik.TryGetValue(kaynak.Id, out MacriaProjeKaynakIcerigi? kaynakIcerigi)) continue;
                    if (kaynakIcerigi.AnalysisJson is string json)
                        YaziEkle(zip, "kaynaklar/" + kaynak.Id + "/analysis.json", json);
                    if (kaynakIcerigi.DxfKlasoru is string dxfKlasoru && Directory.Exists(dxfKlasoru))
                        foreach (string dxf in Directory.GetFiles(dxfKlasoru, "*.dxf").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                            zip.CreateEntryFromFile(dxf, "kaynaklar/" + kaynak.Id + "/dxf/" + Path.GetFileName(dxf), CompressionLevel.Optimal);
                }
            }
            if (File.Exists(hedef)) File.Replace(gecici, hedef, null);
            else File.Move(gecici, hedef);
        }
        catch (Exception istisna) when (istisna is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(gecici)) File.Delete(gecici); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw new MacriaProjeHatasi("Proje kaydedilemedi: " + istisna.Message, istisna);
        }
    }

    private static void YaziEkle(ZipArchive zip, string ad, string metin)
    {
        ZipArchiveEntry kayit = zip.CreateEntry(ad, CompressionLevel.Optimal);
        using Stream akis = kayit.Open();
        byte[] baytlar = new UTF8Encoding(false).GetBytes(metin);
        akis.Write(baytlar, 0, baytlar.Length);
    }

    private static string? GoreliYol(string projeKlasoru, string yol)
    {
        try
        {
            string goreli = Path.GetRelativePath(projeKlasoru, yol);
            return Path.IsPathRooted(goreli) ? null : goreli;
        }
        catch (ArgumentException) { return null; }
    }

    // ---------------------------------------------------------------- opening

    /// <summary>
    /// Reads `yol`; part DXFs are extracted under `dxfKokKlasoru` (one folder
    /// per source). Throws MacriaProjeHatasi for a file that is not a Macria
    /// project, a newer major schema, or an unsafe ZIP.
    /// </summary>
    public static MacriaProjeAcilisi Ac(string yol, string dxfKokKlasoru)
    {
        yol = Path.GetFullPath(yol);
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(yol);
            if (zip.Entries.Count > AzamiKayitSayisi)
                throw new MacriaProjeHatasi("Proje dosyasında çok fazla kayıt var.");
            long toplam = 0;
            foreach (ZipArchiveEntry kayit in zip.Entries)
            {
                GuvenliAdMi(kayit.FullName);
                toplam += kayit.Length;
            }
            if (toplam > AzamiAcilmisBoyut)
                throw new MacriaProjeHatasi("Proje dosyası açıldığında çok büyük.");

            MacriaProjeManifest manifest = JsonOku<MacriaProjeManifest>(zip, ManifestAdi)
                ?? throw new MacriaProjeHatasi("Bu bir Macria projesi değil (manifest.json yok).");
            if (manifest.Format != Format)
                throw new MacriaProjeHatasi("Bu bir Macria projesi değil (biçim: " + manifest.Format + ").");
            string? saltOkunur = SemaDenetle(manifest.SchemaVersion);
            MacriaProjeVerisi veri = JsonOku<MacriaProjeVerisi>(zip, ProjeAdi)
                ?? throw new MacriaProjeHatasi("Proje dosyasında proje.json yok.");
            KaynaklariDenetle(veri);

            var acilis = new MacriaProjeAcilisi { Yol = yol, Manifest = manifest, Veri = veri, SaltOkunurNedeni = saltOkunur };
            var kaynakIdleri = new HashSet<string>(veri.Kaynaklar.Select(x => x.Id), StringComparer.Ordinal);
            foreach (ZipArchiveEntry kayit in zip.Entries)
            {
                Match analiz = AnalizKaydi.Match(kayit.FullName);
                if (analiz.Success && kaynakIdleri.Contains(analiz.Groups[1].Value))
                {
                    acilis.AnalysisJson[analiz.Groups[1].Value] = MetinOku(kayit);
                    continue;
                }
                Match dxf = DxfKaydi.Match(kayit.FullName);
                if (dxf.Success && kaynakIdleri.Contains(dxf.Groups[1].Value))
                {
                    string id = dxf.Groups[1].Value;
                    if (!acilis.DxfKlasoru.TryGetValue(id, out string? klasor))
                    {
                        klasor = Path.Combine(dxfKokKlasoru, id);
                        Directory.CreateDirectory(klasor);
                        acilis.DxfKlasoru[id] = klasor;
                    }
                    kayit.ExtractToFile(Path.Combine(klasor, dxf.Groups[2].Value), true);
                }
                // Other entries (later stages: model/, catia/) are not read by this version.
            }
            return acilis;
        }
        catch (Exception istisna) when (istisna is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            throw new MacriaProjeHatasi("Proje açılamadı: " + istisna.Message, istisna);
        }
    }

    /// <summary>null when this Macria can save the schema; a reason when it opens read-only. Throws when it cannot read it.</summary>
    public static string? SemaDenetle(string? surum)
    {
        if (!Version.TryParse(surum, out Version? dosya) || !Version.TryParse(SemaSurumu, out Version? bu))
            throw new MacriaProjeHatasi("Proje şema sürümü okunamadı: " + surum);
        if (dosya.Major > bu.Major)
            throw new MacriaProjeHatasi("Bu proje daha yeni bir Macria ile kaydedilmiş (şema " + surum + "). Macria'yı güncelleyin.");
        if (dosya.Major == bu.Major && dosya.Minor > bu.Minor)
            return "Proje daha yeni bir Macria ile kaydedilmiş (şema " + surum + "); kaydetmek yeni bilgileri silebileceği için salt-okunur açıldı.";
        return null;
    }

    private static void GuvenliAdMi(string ad)
    {
        string[] parcalar = ad.Replace('\\', '/').Split('/');
        if (ad.Length == 0 || ad.StartsWith('/') || ad.StartsWith('\\') || ad.Contains(':') ||
            parcalar.Any(p => p == ".." || p == "."))
            throw new MacriaProjeHatasi("Proje dosyasında güvensiz bir kayıt yolu var: " + ad);
    }

    private static void KaynaklariDenetle(MacriaProjeVerisi veri)
    {
        var gorulen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MacriaProjeKaynagi kaynak in veri.Kaynaklar)
        {
            if (!Regex.IsMatch(kaynak.Id, @"^k[0-9]{1,6}$") || !gorulen.Add(kaynak.Id))
                throw new MacriaProjeHatasi("Proje dosyasında geçersiz kaynak kimliği: " + kaynak.Id);
            if (string.IsNullOrWhiteSpace(kaynak.Yol))
                throw new MacriaProjeHatasi("Proje dosyasında yolu olmayan kaynak: " + kaynak.Id);
        }
    }

    private static T? JsonOku<T>(ZipArchive zip, string ad) where T : class
    {
        ZipArchiveEntry? kayit = zip.GetEntry(ad);
        return kayit is null ? null : JsonSerializer.Deserialize<T>(MetinOku(kayit), JsonAyarlari);
    }

    private static string MetinOku(ZipArchiveEntry kayit)
    {
        using Stream akis = kayit.Open();
        using StreamReader okuyucu = new(akis, new UTF8Encoding(false));
        return okuyucu.ReadToEnd();
    }

    // ---------------------------------------------------------- source checks

    /// <summary>
    /// Is the source STEP still the one analysed, and by an engine that is not
    /// older than the installed one? Looks at the saved path, then the path
    /// relative to the project file; compares SHA-256.
    /// </summary>
    public static MacriaKaynakDenetimi Denetle(MacriaProjeKaynagi kaynak, string projeYolu, bool kayitliCiktiVar,
        GeometryLabMotorKimligi? kuruluMotor)
    {
        string? bulunan = File.Exists(kaynak.Yol) ? kaynak.Yol : null;
        if (bulunan is null && kaynak.GoreliYol is string goreli)
        {
            string aday = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projeYolu) ?? "", goreli));
            if (File.Exists(aday)) bulunan = aday;
        }
        if (bulunan is null)
            return new MacriaKaynakDenetimi(MacriaKaynakDurumu.Bulunamadi, null, "STEP bulunamadı: " + kaynak.Yol);
        if (!string.Equals(GeometryLabMotorKimligi.DosyaSha256(bulunan), kaynak.Sha256, StringComparison.OrdinalIgnoreCase))
            return new MacriaKaynakDenetimi(MacriaKaynakDurumu.Degismis, bulunan, "STEP kaydedildikten sonra değişmiş.");
        bool basarili = kaynak.Analiz.Durum == nameof(GeometryLabProcessAdapterStatus.Succeeded);
        if (basarili && !kayitliCiktiVar)
            return new MacriaKaynakDenetimi(MacriaKaynakDurumu.Degismis, bulunan, "Kayıtlı motor çıktısı projede yok.");
        if (basarili && !GeometryLabProcessAdapter.IsSupportedSchemaVersion(kaynak.Analiz.MotorSemaSurumu))
            return new MacriaKaynakDenetimi(MacriaKaynakDurumu.Degismis, bulunan,
                "Kayıtlı motor çıktısının şeması (" + kaynak.Analiz.MotorSemaSurumu + ") artık okunmuyor.");
        if (basarili && kuruluMotor is not null && kuruluMotor.OncekindenFarkli(kaynak.Analiz.Motor?.Kimlik()))
            return new MacriaKaynakDenetimi(MacriaKaynakDurumu.MotorYeni, bulunan,
                "Eski motorla taranmış (" + (kaynak.Analiz.Motor?.Kimlik().Gosterim ?? "sürüm bilinmiyor") +
                " → " + kuruluMotor.Gosterim + ").");
        return new MacriaKaynakDenetimi(MacriaKaynakDurumu.Ayni, bulunan, null);
    }

    // -------------------------------------------------------- decision mapping

    /// <summary>
    /// Pairs decisions with rows. Same engine output: by localId (the name must
    /// agree). After a rescan: by productId, then by name; only a single
    /// candidate is accepted. The rest is returned as unmatched.
    /// </summary>
    public static (List<(MacriaProjeKarari Karar, MacriaKararAdayi Aday)> Eslenen, List<MacriaProjeKarari> Eslenemeyen)
        KararlariEsle(IEnumerable<MacriaProjeKarari> kararlar, IReadOnlyList<MacriaKararAdayi> adaylar, Func<string, bool> yenidenTarandi)
    {
        var eslenen = new List<(MacriaProjeKarari, MacriaKararAdayi)>();
        var eslenemeyen = new List<MacriaProjeKarari>();
        var kullanilan = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (MacriaProjeKarari karar in kararlar)
        {
            List<MacriaKararAdayi> ayniHedef = adaylar
                .Where(a => a.KaynakId == karar.Kaynak && a.Hedef == karar.Hedef && !kullanilan.Contains(a.Satir))
                .ToList();
            MacriaKararAdayi? aday;
            if (karar.Parca is null)
                aday = Tek(ayniHedef.Where(a => a.Parca is null));
            else if (!yenidenTarandi(karar.Kaynak))
                aday = Tek(ayniHedef.Where(a => a.Parca?.LocalId == karar.Parca.LocalId &&
                                                (karar.Parca.Ad is null || a.Parca.Ad == karar.Parca.Ad)));
            else
                aday = (karar.Parca.ProductId is string urun
                           ? Tek(ayniHedef.Where(a => a.Parca?.ProductId == urun))
                           : null)
                       ?? (karar.Parca.Ad is string ad ? Tek(ayniHedef.Where(a => a.Parca?.Ad == ad)) : null);
            if (aday is null) { eslenemeyen.Add(karar); continue; }
            kullanilan.Add(aday.Satir);
            eslenen.Add((karar, aday));
        }
        return (eslenen, eslenemeyen);
    }

    private static MacriaKararAdayi? Tek(IEnumerable<MacriaKararAdayi> adaylar)
    {
        MacriaKararAdayi? bulunan = null;
        foreach (MacriaKararAdayi aday in adaylar)
        {
            if (bulunan is not null) return null;
            bulunan = aday;
        }
        return bulunan;
    }
}
