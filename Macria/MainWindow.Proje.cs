using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Macria;

/// <summary>
/// .macria project of Dosya Analiz Merkezi → STEP / STP Analizi (stage 1 of
/// docs/MACRIA_PROJE_DOSYASI_TASARIM.md): Proje Aç / Kaydet / Farklı Kaydet,
/// unsaved-change questions, and opening without the engine when the STEP and
/// the engine are unchanged.
/// </summary>
public partial class MainWindow
{
    private sealed record ProjeKaynakKaydi(MacriaProjeKaynagi Kaynak, MacriaProjeKaynakIcerigi Icerik);

    // Key: the STEP path the rows use (full path).
    private readonly Dictionary<string, ProjeKaynakKaydi> _projeKaynaklari = new(StringComparer.OrdinalIgnoreCase);
    private List<MacriaProjeKarari> _eslenemeyenKararlar = new();
    private string? _projeYolu;
    private bool _projeKirli;
    private string? _projeSaltOkunurNedeni;
    private DateTime? _projeOlusturulma;
    private bool _projeIslemde;
    private bool _projeYukleniyor;
    // The project's laser limit: the rows are grouped with it (Ayarlar when no project is open).
    private double _projeLazerMm = 20;
    private MacriaProjeAnalizAyarlari _projeAnalizAyarlari = new();

    private void ProjeKur()
    {
        _projeLazerMm = Ayarlar.LazerAzamiKalinlikMm;
        PreviewKeyDown += Proje_PreviewKeyDown;
        ProjeDurumunuGoster();
    }

    // Ctrl+O / Ctrl+S / Ctrl+Shift+S on the STEP / STP Analizi screen.
    private void Proje_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (fileAnalysisStepWorkspace.Visibility != Visibility.Visible ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key == Key.O && !shift) { e.Handled = true; _ = ProjeyiAcAsync(null); }
        else if (e.Key == Key.S) { e.Handled = true; ProjeyiKaydet(farkli: shift); }
    }

    private void btnProjeAc_Click(object sender, RoutedEventArgs e) => _ = ProjeyiAcAsync(null);
    private void btnProjeKaydet_Click(object sender, RoutedEventArgs e) => ProjeyiKaydet(farkli: false);
    private void btnProjeFarkliKaydet_Click(object sender, RoutedEventArgs e) => ProjeyiKaydet(farkli: true);

    // ------------------------------------------------------------- state

    /// <summary>Something the project stores changed (analysis, decision, setting).</summary>
    private void ProjeDegisti()
    {
        if (_projeYukleniyor || _projeSaltOkunurNedeni != null) return;
        _projeKirli = true;
        ProjeDurumunuGoster();
    }

    /// <summary>No project: a new analysis or Tabloyu Temizle starts from scratch.</summary>
    private void ProjeyiSifirla()
    {
        _projeKaynaklari.Clear();
        _eslenemeyenKararlar = new List<MacriaProjeKarari>();
        _projeYolu = null;
        _projeKirli = false;
        _projeSaltOkunurNedeni = null;
        _projeOlusturulma = null;
        _projeLazerMm = Ayarlar.LazerAzamiKalinlikMm;
        _projeAnalizAyarlari = new MacriaProjeAnalizAyarlari
        {
            ParcaSureSiniriSaniye = Ayarlar.ParcaSureSiniriSaniye,
            MotorIsParcacigi = Ayarlar.MotorIsParcacigi
        };
        ProjeDurumunuGoster();
    }

    /// <summary>Ayarlar → lazer limit changed: the rows follow it, and so does the project.</summary>
    private void ProjeLazerSiniriniGuncelle(double lazerMm)
    {
        if (lazerMm.Equals(_projeLazerMm)) return;
        _projeLazerMm = lazerMm;
        if (_projeKaynaklari.Count > 0) ProjeDegisti();
    }

    private void ProjeDurumunuGoster()
    {
        if (txtProjeDurumu == null) return;
        bool mesgul = _externalStepProfileAnalysisRunning || _projeIslemde;
        string ad = _projeYolu is null ? "Kaydedilmemiş analiz" : Path.GetFileName(_projeYolu);
        txtProjeDurumu.Text = _projeKaynaklari.Count == 0 && _projeYolu is null
            ? ""
            : ad + (_projeKirli ? " *" : "") + (_projeSaltOkunurNedeni != null ? "  [salt-okunur]" : "");
        txtProjeDurumu.ToolTip = _projeSaltOkunurNedeni ?? _projeYolu;
        bool kaydedilebilir = !mesgul && _projeKaynaklari.Count > 0 && _projeSaltOkunurNedeni == null;
        btnProjeKaydet.IsEnabled = kaydedilebilir;
        btnProjeFarkliKaydet.IsEnabled = kaydedilebilir;
        btnProjeAc.IsEnabled = !mesgul;
    }

    /// <summary>A read-only project: decisions cannot change. Shows why and returns true.</summary>
    private bool ProjeSaltOkunurUyarisi()
    {
        if (_projeSaltOkunurNedeni == null) return false;
        OnayWindow.Sor(this, "Salt-okunur Proje", _projeSaltOkunurNedeni + "\n\nKararlar bu projede değiştirilemez.", "Tamam", "Kapat");
        return true;
    }

    /// <summary>
    /// Before something that drops the current list: with unsaved changes asks
    /// Kaydet / Kaydetme / İptal. False: stop.
    /// </summary>
    private bool ProjeDegisiklikleriniSor(string islem)
    {
        if (!_projeKirli) return true;
        string ad = _projeYolu is null ? "STEP analizi henüz bir projeye kaydedilmedi" : Path.GetFileName(_projeYolu) + " kaydedilmemiş değişiklikler içeriyor";
        int secim = OnayWindow.Sec(this, "Kaydedilmemiş Proje",
            ad + ". " + islem + " öncesinde kaydedilsin mi?", "İptal", "Kaydetme", "Kaydet");
        return secim switch
        {
            2 => ProjeyiKaydet(farkli: false),
            1 => true,
            _ => false
        };
    }

    // ------------------------------------------------------------ sources

    private static GeometryLabMotorKimligi? MotorKimliginiOku(GeometryLabEngineLocation engine)
    {
        if (!engine.IsAvailable) return null;
        try { return GeometryLabMotorKimligi.Oku(engine.ExecutablePath!); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>An analysed STEP becomes (or replaces) a project source with its engine output and DXFs.</summary>
    private async Task ProjeKaynaginiKaydetAsync(string stepPath, GeometryLabProcessAdapterResult result, TimeSpan sure,
        GeometryLabMotorKimligi? motor)
    {
        string yol = Path.GetFullPath(stepPath);
        string sha = "";
        long boyut = 0;
        DateTime degistirilme = default;
        try
        {
            (sha, boyut, degistirilme) = await Task.Run(() =>
                (GeometryLabMotorKimligi.DosyaSha256(yol), new FileInfo(yol).Length, File.GetLastWriteTimeUtc(yol)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogError("STEP'in SHA-256'sı alınamadı (proje açılışında değişmiş sayılır): " + exception.Message);
        }
        string id = _projeKaynaklari.TryGetValue(yol, out ProjeKaynakKaydi? eski) ? eski.Kaynak.Id : YeniKaynakId();
        var kaynak = new MacriaProjeKaynagi
        {
            Id = id,
            Yol = yol,
            Sha256 = sha,
            Boyut = boyut,
            Degistirilme = degistirilme,
            Analiz = new MacriaProjeAnalizi
            {
                Zaman = DateTime.UtcNow,
                Durum = result.Status.ToString(),
                Mesaj = result.IsSuccess ? null : result.Message,
                MotorSemaSurumu = result.Analysis?.SchemaVersion,
                Motor = motor is null ? null : MacriaProjeMotoru.Kimliktan(motor),
                SureSn = Math.Round(sure.TotalSeconds, 1),
                Montaj = result.IsSuccess && MontajParcaSatiri.IsAssembly(result.Analysis)
            }
        };
        _projeKaynaklari[yol] = new ProjeKaynakKaydi(kaynak, new MacriaProjeKaynakIcerigi(
            result.IsSuccess ? result.AnalysisJson : null, result.IsSuccess ? result.PartDxfDirectory : null));
        _projeAnalizAyarlari = new MacriaProjeAnalizAyarlari
        {
            ParcaSureSiniriSaniye = Ayarlar.ParcaSureSiniriSaniye,
            MotorIsParcacigi = Ayarlar.MotorIsParcacigi
        };
        ProjeDegisti();
    }

    private string YeniKaynakId()
    {
        int enBuyuk = _projeKaynaklari.Values
            .Select(x => int.TryParse(x.Kaynak.Id.AsSpan(1), out int n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return "k" + (enBuyuk + 1);
    }

    private string? ProjeKaynakId(string stepPath)
    {
        try { return _projeKaynaklari.TryGetValue(Path.GetFullPath(stepPath), out ProjeKaynakKaydi? k) ? k.Kaynak.Id : null; }
        catch (ArgumentException) { return null; }
    }

    // ------------------------------------------------------------- saving

    private bool ProjeyiKaydet(bool farkli)
    {
        if (_externalStepProfileAnalysisRunning || _projeIslemde) return false;
        if (ProjeSaltOkunurUyarisi()) return false;
        if (_projeKaynaklari.Count == 0)
        {
            OnayWindow.Sor(this, "Proje Kaydet", "Kaydedilecek STEP analizi yok. Önce STEP dosyalarını analiz edin.", "Tamam", "Kapat");
            return false;
        }

        string? hedef = !farkli && _projeYolu != null ? _projeYolu : KayitYeriSor();
        if (hedef == null) return false;

        List<ProjeKaynakKaydi> kaynaklar = _projeKaynaklari.Values
            .OrderBy(x => int.TryParse(x.Kaynak.Id.AsSpan(1), out int n) ? n : int.MaxValue).ToList();
        var veri = new MacriaProjeVerisi
        {
            Ayarlar = new MacriaProjeAyarlari
            {
                LazerAzamiKalinlikMm = _projeLazerMm,
                BukumBilgisiDxf = Ayarlar.BukumBilgisiDxf,
                Analiz = _projeAnalizAyarlari
            },
            Kaynaklar = kaynaklar.Select(x => x.Kaynak).ToList(),
            Kararlar = MacriaProjeSatirlari.KararlariTopla(_externalStepProfileRows, _montajParcaRows, ProjeKaynakId),
            EslenemeyenKararlar = _eslenemeyenKararlar.ToList()
        };
        var sure = Stopwatch.StartNew();
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            MacriaProje.Kaydet(hedef, veri, kaynaklar.ToDictionary(x => x.Kaynak.Id, x => x.Icerik),
                "Macria " + AboutWindow.SurumMetni(), _projeOlusturulma);
        }
        catch (MacriaProjeHatasi exception)
        {
            LogError(exception.Message);
            OnayWindow.Sor(this, "Proje Kaydedilemedi", exception.Message, "Tamam", "Kapat");
            return false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        _projeYolu = hedef;
        _projeOlusturulma ??= DateTime.UtcNow;
        _projeKirli = false;
        ProjeDurumunuGoster();
        LogSuccess("Proje kaydedildi: " + hedef + " (" + veri.Kaynaklar.Count + " STEP, " + veri.Kararlar.Count + " karar, " +
                   (new FileInfo(hedef).Length / 1024) + " KB, " + SureMetni(sure.Elapsed) + ")");
        return true;
    }

    private string? KayitYeriSor()
    {
        string varsayilan = _projeYolu ?? MacriaProje.VarsayilanYol(_projeKaynaklari.Values
            .OrderBy(x => int.TryParse(x.Kaynak.Id.AsSpan(1), out int n) ? n : int.MaxValue).First().Kaynak.Yol);
        var dialog = new SaveFileDialog
        {
            Title = "Macria Projesini Kaydet",
            Filter = "Macria Projesi (*.macria)|*.macria",
            DefaultExt = MacriaProje.Uzanti,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Path.GetDirectoryName(varsayilan),
            FileName = Path.GetFileName(varsayilan)
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    // ------------------------------------------------------------ opening

    private async Task ProjeyiAcAsync(string? yol)
    {
        if (_externalStepProfileAnalysisRunning || _profilIslemde || _exporting || _projeIslemde) return;
        if (!ProjeDegisiklikleriniSor("Başka bir proje açma")) return;
        if (yol == null)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Macria Projesi Aç",
                Filter = "Macria Projesi (*.macria)|*.macria",
                CheckFileExists = true,
                InitialDirectory = _projeYolu is null ? null : Path.GetDirectoryName(_projeYolu)
            };
            if (dialog.ShowDialog(this) != true) return;
            yol = dialog.FileName;
        }

        var sure = Stopwatch.StartNew();
        string dxfKok = Path.Combine(Path.GetTempPath(), "Macria", "MotorDxf", Guid.NewGuid().ToString("N"));
        GeometryLabEngineLocation engine = GeometryLabEngineLocator.Locate();
        MacriaProjeAcilisi acilis;
        List<(MacriaProjeKaynagi Kaynak, MacriaKaynakDenetimi Denetim)> denetimler;
        _projeIslemde = true;
        ProjeDurumunuGoster();
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            GeometryLabMotorKimligi? kuruluMotor = await Task.Run(() => MotorKimliginiOku(engine));
            acilis = await Task.Run(() => MacriaProje.Ac(yol, dxfKok));
            denetimler = await Task.Run(() => acilis.Veri.Kaynaklar
                .Select(k => (k, MacriaProje.Denetle(k, acilis.Yol, acilis.AnalysisJson.ContainsKey(k.Id), kuruluMotor)))
                .ToList());
        }
        catch (MacriaProjeHatasi exception)
        {
            KlasoruSil(dxfKok);
            LogError(exception.Message);
            OnayWindow.Sor(this, "Proje Açılamadı", exception.Message, "Tamam", "Kapat");
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _projeIslemde = false;
            ProjeDurumunuGoster();
        }
        TimeSpan denetimSuresi = sure.Elapsed;

        // One question for every source that is not as it was saved.
        bool yenidenTara = false;
        var sorunlu = denetimler.Where(x => x.Denetim.Durum != MacriaKaynakDurumu.Ayni).ToList();
        if (sorunlu.Count > 0)
        {
            bool degismis = sorunlu.Any(x => x.Denetim.Durum is MacriaKaynakDurumu.Degismis or MacriaKaynakDurumu.Bulunamadi);
            bool taranabilir = engine.IsAvailable && sorunlu.Any(x => x.Denetim.BulunanYol != null);
            string kayitli = degismis ? "Salt-okunur aç" : "Kayıtlı sonuçla aç";
            string mesaj = string.Join("\n", sorunlu.Select(x => "• " + Path.GetFileName(x.Kaynak.Yol) + ": " + x.Denetim.Aciklama)) +
                           (taranabilir ? "\n\nYeniden tara: bulunan dosyalar motorla yeniden analiz edilir; kararlar parçalara yeniden bağlanır." : "") +
                           (degismis
                               ? "\nSalt-okunur aç: kayıtlı sonuçlar ve kararlar gösterilir; kararlar değiştirilemez, proje kaydedilemez."
                               : "\nKayıtlı sonuçla aç: eski motorun sonuçları gösterilir.");
            string[] secenekler = taranabilir ? new[] { "İptal", kayitli, "Yeniden tara" } : new[] { "İptal", kayitli };
            int secim = OnayWindow.Sec(this, "Proje: " + Path.GetFileName(acilis.Yol), mesaj, secenekler);
            if (secim <= 0)
            {
                KlasoruSil(dxfKok);
                return;
            }
            yenidenTara = taranabilir && secim == 2;
        }

        // Replace the list with the project.
        _projeYukleniyor = true;
        _externalStepPreviewWindow?.Close();
        ExternalStepSeciminiTemizle();
        gridSacParcalar?.SelectedItems.Clear();
        gridKontrolParcalar?.SelectedItems.Clear();
        _externalStepProfileRows.Clear();
        _montajParcaRows.Clear();
        Step3BModelHazirlayici.Temizle();
        MotorDxfOturumunuTemizle();
        _motorDxfOturumKlasoru = dxfKok;
        ProjeyiSifirla();
        _projeYukleniyor = true;
        _projeLazerMm = acilis.Veri.Ayarlar.LazerAzamiKalinlikMm;
        _projeAnalizAyarlari = acilis.Veri.Ayarlar.Analiz;
        _projeOlusturulma = acilis.Manifest.Olusturulma;
        string? saltOkunur = acilis.SaltOkunurNedeni;
        bool yolDegisti = false;
        var taranacak = new List<GeometryLabStepProfileListItem>();
        var taranan = new HashSet<string>(StringComparer.Ordinal);
        int taramasiz = 0;
        foreach ((MacriaProjeKaynagi kaynak, MacriaKaynakDenetimi denetim) in denetimler)
        {
            string satirYolu = Path.GetFullPath(denetim.BulunanYol ?? kaynak.Yol);
            if (denetim.BulunanYol != null && !string.Equals(Path.GetFullPath(kaynak.Yol), satirYolu, StringComparison.OrdinalIgnoreCase))
            {
                kaynak.Yol = satirYolu;
                yolDegisti = true;
            }
            if (yenidenTara && denetim.BulunanYol != null && denetim.Durum != MacriaKaynakDurumu.Ayni)
            {
                // Keeps its id; the analysis records it again.
                _projeKaynaklari[satirYolu] = new ProjeKaynakKaydi(kaynak, new MacriaProjeKaynakIcerigi(null, null));
                var dosyaSatiri = new GeometryLabStepProfileListItem { SourceStepPath = satirYolu };
                _externalStepProfileRows.Add(dosyaSatiri);
                taranacak.Add(dosyaSatiri);
                taranan.Add(kaynak.Id);
                continue;
            }
            if (denetim.Durum is MacriaKaynakDurumu.Degismis or MacriaKaynakDurumu.Bulunamadi)
                saltOkunur ??= "Bazı STEP dosyaları kaydedildikten sonra değişmiş ya da bulunamadı; kayıtlı sonuçlar salt-okunur açıldı.";
            string? json = acilis.AnalysisJson.GetValueOrDefault(kaynak.Id);
            string dxfKlasoru = acilis.DxfKlasoru.GetValueOrDefault(kaynak.Id) ?? Path.Combine(dxfKok, kaynak.Id);
            GeometryLabProcessAdapterResult sonuc = KayitliSonuc(kaynak, json, dxfKlasoru);
            var (profil, montaj) = MacriaProjeSatirlari.Kur(satirYolu, sonuc, _projeLazerMm);
            foreach (GeometryLabStepProfileListItem row in profil) _externalStepProfileRows.Add(row);
            foreach (MontajParcaSatiri row in montaj) _montajParcaRows.Add(row);
            _projeKaynaklari[satirYolu] = new ProjeKaynakKaydi(kaynak,
                new MacriaProjeKaynakIcerigi(json, acilis.DxfKlasoru.GetValueOrDefault(kaynak.Id)));
            if (sonuc.IsSuccess && denetim.BulunanYol != null) Step3BModelHazirlayici.Hazirla(satirYolu);
            ++taramasiz;
        }
        _projeYolu = acilis.Yol;
        _projeSaltOkunurNedeni = saltOkunur;
        _projeYukleniyor = false;
        SacGrubunuSec(!_montajParcaRows.Any(x => x.IsInSheetTab && !x.IsThickPlate) &&
                      _montajParcaRows.Any(x => x.IsInSheetTab && x.IsThickPlate));
        MontajSekmeleriniGuncelle();
        ExternalStepProfilOzetiniGuncelle();
        _externalStepProfileView?.Refresh();
        tabExternalStepSonuc.SelectedItem = tabExternalStepProfiller;
        ProjeDurumunuGoster();

        if (taranacak.Count > 0)
        {
            LogInfo("Proje: " + taranacak.Count + " STEP yeniden taranıyor.");
            await ExternalStepDosyalariniAnalizEt(taranacak, engine, dxfKok);
        }

        // Decisions (and those not matched last time) go back to their rows.
        List<MacriaProjeKarari> kararlar = acilis.Veri.Kararlar.Concat(acilis.Veri.EslenemeyenKararlar).ToList();
        var (eslenen, eslenemeyen) = MacriaProje.KararlariEsle(kararlar,
            MacriaProjeSatirlari.Adaylar(_externalStepProfileRows, _montajParcaRows, ProjeKaynakId), taranan.Contains);
        _projeYukleniyor = true;
        foreach ((MacriaProjeKarari karar, MacriaKararAdayi aday) in eslenen)
            if (!MacriaProjeSatirlari.Uygula(karar, aday.Satir)) eslenemeyen.Add(karar);
        _projeYukleniyor = false;
        _eslenemeyenKararlar = eslenemeyen;
        MontajSekmeleriniGuncelle();
        ExternalStepProfilOzetiniGuncelle();
        _externalStepProfileView?.Refresh();
        _projeKirli = _projeSaltOkunurNedeni == null && (taranacak.Count > 0 || yolDegisti);
        ProjeDurumunuGoster();

        LogSuccess("Proje açıldı: " + Path.GetFileName(acilis.Yol) + " — " + denetimler.Count + " STEP (" +
                   taramasiz + " taramasız, " + taranacak.Count + " yeniden tarandı), " +
                   (kararlar.Count - eslenemeyen.Count) + " karar uygulandı" +
                   (eslenemeyen.Count > 0 ? ", " + eslenemeyen.Count + " karar eşlenemedi" : "") +
                   (_projeSaltOkunurNedeni != null ? ", salt-okunur" : "") +
                   " (denetim " + SureMetni(denetimSuresi) + ", toplam " + SureMetni(sure.Elapsed) + ").");
        foreach (MacriaProjeKarari karar in eslenemeyen)
            LogInfo("Eşlenemeyen karar: " + karar.Karar + " — " + (karar.Parca?.Ad ?? "dosya satırı") + " (" + karar.Kaynak + ")");
        if (_projeSaltOkunurNedeni != null) LogInfo(_projeSaltOkunurNedeni);
        if (!_projeLazerMm.Equals(Ayarlar.LazerAzamiKalinlikMm))
            LogInfo("Proje lazer sınırı " + MontajParcaSatiri.FormatNumber(_projeLazerMm) + " mm ile açıldı (Ayarlar: " +
                    MontajParcaSatiri.FormatNumber(Ayarlar.LazerAzamiKalinlikMm) + " mm).");
        if (acilis.Veri.Ayarlar.BukumBilgisiDxf != Ayarlar.BukumBilgisiDxf)
            LogInfo("Proje büküm bilgisi " + (acilis.Veri.Ayarlar.BukumBilgisiDxf ? "açık" : "kapalı") +
                    " kaydedilmişti; DXF için Ayarlar'daki değer kullanılıyor.");
        if (_montajParcaRows.Count > 0)
            LogInfo("CATIA karşılaştırması projede saklanmaz; gerekiyorsa \"Aktif 3B Tarama Verileriyle Karşılaştır\" ile yeniden yapın.");
    }

    /// <summary>The stored result of a source: its analysis.json, or its saved failure.</summary>
    private static GeometryLabProcessAdapterResult KayitliSonuc(MacriaProjeKaynagi kaynak, string? json, string dxfKlasoru)
    {
        if (json != null) return GeometryLabProcessAdapter.SonucuJsondanKur(json, dxfKlasoru);
        return new GeometryLabProcessAdapterResult
        {
            Status = Enum.TryParse(kaynak.Analiz.Durum, out GeometryLabProcessAdapterStatus durum) &&
                     durum != GeometryLabProcessAdapterStatus.Succeeded
                ? durum
                : GeometryLabProcessAdapterStatus.JsonMissing,
            Message = kaynak.Analiz.Mesaj ?? "Kayıtlı motor çıktısı yok."
        };
    }

    private static void KlasoruSil(string klasor)
    {
        try { if (Directory.Exists(klasor)) Directory.Delete(klasor, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
