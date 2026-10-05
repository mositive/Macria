using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Macria;

// STEP / STP Analizi: engine runs on selected parts (docs/YENIDEN_ANALIZ_VE_DENEME_PLANI.md).
// "Yeniden Analiz Et" runs the automatic rules without the per-part time
// limit; the result replaces the parts' rows, is kept as an EkAnaliz of the
// project source and comes back with the project ("Dene" decision).
public partial class MainWindow
{
    /// <summary>One engine run: the parts (localIds) of one STEP and the run's mode (MacriaProje.Deneme*).</summary>
    private sealed record ParcaIsi(string Yol, List<int> Parcalar, string Mod);

    private async void btnAnalizYenidenAnaliz_Click(object sender, RoutedEventArgs e) =>
        await SeciliParcalariCalistirAsync(SeciliAnalizSatirlari(), MacriaProje.DenemeYeniden);

    private async void btnAnalizProfilDene_Click(object sender, RoutedEventArgs e) =>
        await SeciliParcalariCalistirAsync(SeciliAnalizSatirlari(), MacriaProje.DenemeProfil);

    private static string IsBasligi(string mod) => mod switch
    {
        MacriaProje.DenemeProfil => "Profil olarak dene",
        MacriaProje.DenemeSac => "Sac olarak dene",
        _ => "Yeniden analiz"
    };

    private async Task SeciliParcalariCalistirAsync(List<IAnalizSatiri> satirlar, string mod)
    {
        if (_externalStepProfileAnalysisRunning || _profilIslemde || _projeIslemde) return;
        if (ProjeSaltOkunurUyarisi()) return;
        var isler = new List<ParcaIsi>();
        foreach (IGrouping<string, IAnalizSatiri> grup in satirlar.Where(x => x.ParcaLocalId != null)
                     .GroupBy(x => Path.GetFullPath(x.KaynakYolu), StringComparer.OrdinalIgnoreCase))
        {
            if (!_projeKaynaklari.TryGetValue(grup.Key, out ProjeKaynakKaydi? kayit) || kayit.Icerik.AnalysisJson is null)
            {
                LogError(Path.GetFileName(grup.Key) + ": STEP'in kayıtlı analizi yok; önce STEP'i analiz edin.");
                continue;
            }
            isler.Add(new ParcaIsi(grup.Key, grup.Select(x => x.ParcaLocalId!.Value).Distinct().OrderBy(x => x).ToList(), mod));
        }
        if (isler.Count == 0) return;
        await ParcaIsleriniCalistirAsync(isler, IsBasligi(mod));
        ProjeDegisti();
    }

    /// <summary>
    /// Runs the engine once per STEP on its parts, without the per-part time
    /// limit, with progress and İptal. A success replaces the parts' rows and
    /// becomes an EkAnaliz of the source; a failure or İptal leaves them.
    /// </summary>
    private async Task ParcaIsleriniCalistirAsync(List<ParcaIsi> isler, string baslik)
    {
        GeometryLabEngineLocation engine = GeometryLabEngineLocator.Locate();
        if (!engine.IsAvailable)
        {
            LogError(baslik + ": GeometryEngine bulunamadı. " + engine.Detail);
            return;
        }
        _externalStepProfileAnalysisRunning = true;
        _profilIslemde = true;
        ExternalStepAnalizButonunuGuncelle();
        ProfilButonlariniGuncelle();
        AracCubugunuGuncelle();
        ProjeDurumunuGoster();
        using var iptal = new CancellationTokenSource();
        _externalStepAnalizIptal = iptal;
        ExternalStepIlerlemesiniGoster(true);
        string dosyaOn = "";
        try
        {
            GeometryLabMotorKimligi? motor = await Task.Run(() => MotorKimliginiOku(engine));
            LogInfo(MotorSurumuMetni(motor));
            _motorDxfOturumKlasoru ??= Path.Combine(Path.GetTempPath(), "Macria", "MotorDxf", Guid.NewGuid().ToString("N"));
            for (int sira = 0; sira < isler.Count; ++sira)
            {
                ParcaIsi is_ = isler[sira];
                if (iptal.IsCancellationRequested) break;
                string ad = Path.GetFileName(is_.Yol);
                dosyaOn = baslik + (isler.Count > 1 ? " — dosya " + (sira + 1) + " / " + isler.Count : "") + " — " + ad + ": ";
                ExternalStepIlerlemesiniYaz(dosyaOn, null);
                var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
                {
                    EngineExecutablePath = engine.ExecutablePath!,
                    Timeout = TimeSpan.Zero,
                    PartDxfRootDirectory = _motorDxfOturumKlasoru,
                    PartTimeLimitSeconds = 0,
                    ThreadCount = Ayarlar.MotorIsParcacigi,
                    SelectedPartIds = is_.Parcalar,
                    Deneme = is_.Mod switch
                    {
                        MacriaProje.DenemeProfil => MotorDenemesi.Profil,
                        MacriaProje.DenemeSac => MotorDenemesi.Sac,
                        _ => MotorDenemesi.Yok
                    },
                    ProgressChanged = progress => Dispatcher.BeginInvoke(() => ExternalStepIlerlemesiniYaz(dosyaOn, progress))
                });
                LogInfo(baslik + ": " + ad + " — " + is_.Parcalar.Count + " parça (süre sınırı yok).");
                var sure = System.Diagnostics.Stopwatch.StartNew();
                GeometryLabProcessAdapterResult sonuc = await adapter.AnalyzeAsync(is_.Yol, iptal.Token);
                sure.Stop();
                if (sonuc.Status == GeometryLabProcessAdapterStatus.Cancelled)
                {
                    LogInfo(baslik + " iptal edildi: " + ad + " (" + SureMetni(sure.Elapsed) + "); satırlar değişmedi.");
                    break;
                }
                if (!sonuc.IsSuccess || sonuc.Analysis!.Otomatik)
                {
                    LogError(baslik + " başarısız: " + ad + " — " + (sonuc.IsSuccess ? "motor seçili parça analizi yapmadı" : sonuc.Message) +
                             " (" + SureMetni(sure.Elapsed) + "); satırlar değişmedi.");
                    continue;
                }
                EkSonucunuKaydet(is_, sonuc, sure.Elapsed, motor, baslik);
            }
        }
        catch (Exception exception)
        {
            LogError(baslik + ": beklenmeyen hata: " + exception.Message);
        }
        finally
        {
            _externalStepAnalizIptal = null;
            ExternalStepIlerlemesiniGoster(false);
            _externalStepProfileAnalysisRunning = false;
            _profilIslemde = false;
            ExternalStepAnalizButonunuGuncelle();
            ProfilButonlariniGuncelle();
            ProjeDurumunuGoster();
            ExternalStepProfilOzetiniGuncelle();
            AnalizSekmeleriniGuncelle();
            SagPaneliGuncelle();
        }
    }

    /// <summary>A run's output becomes an EkAnaliz of the source and replaces its parts' rows.</summary>
    private void EkSonucunuKaydet(ParcaIsi is_, GeometryLabProcessAdapterResult sonuc, TimeSpan sure, GeometryLabMotorKimligi? motor,
        string baslik)
    {
        ProjeKaynakKaydi kayit = _projeKaynaklari[is_.Yol];
        int enBuyuk = kayit.Kaynak.EkAnalizler.Select(x => int.TryParse(x.Id.AsSpan(1), out int n) ? n : 0).DefaultIfEmpty(0).Max();
        var ek = new MacriaEkAnaliz
        {
            Id = "e" + (enBuyuk + 1),
            Mod = is_.Mod,
            Parcalar = is_.Parcalar.ToList(),
            Zaman = DateTime.UtcNow,
            MotorSemaSurumu = sonuc.Analysis!.SchemaVersion,
            Motor = motor is null ? null : MacriaProjeMotoru.Kimliktan(motor),
            SureSn = Math.Round(sure.TotalSeconds, 1)
        };
        kayit.Kaynak.EkAnalizler.Add(ek);
        var ekler = new Dictionary<string, MacriaEkIcerik>(kayit.Icerik.Ekler ?? new Dictionary<string, MacriaEkIcerik>(), StringComparer.Ordinal)
        {
            [ek.Id] = new MacriaEkIcerik(sonuc.AnalysisJson!, sonuc.PartDxfDirectory)
        };
        _projeKaynaklari[is_.Yol] = kayit with { Icerik = kayit.Icerik with { Ekler = ekler } };

        var once = SatirSekmeleri(is_.Yol, is_.Parcalar);
        var (profil, montaj) = MacriaProjeSatirlari.EkSatirlari(is_.Yol, sonuc, _projeLazerMm, new SatirDenemesi(is_.Mod, ek.Id));
        ParcaSatirlariniDegistir(is_.Yol, is_.Parcalar, profil, montaj);
        var sonra = SatirSekmeleri(is_.Yol, is_.Parcalar);
        int degisen = is_.Parcalar.Count(id => once.GetValueOrDefault(id) != sonra.GetValueOrDefault(id));
        LogSuccess(baslik + ": " + Path.GetFileName(is_.Yol) + " — " + is_.Parcalar.Count + " parça, " + degisen + " sekme değiştirdi, " +
                   SureMetni(sure) + ".");
        foreach (int id in is_.Parcalar.Where(id => once.GetValueOrDefault(id) != sonra.GetValueOrDefault(id)))
            LogInfo("   #" + id + " " + SatirAdi(is_.Yol, id) + ": " + SekmeAdi(once[id]) + " → " + SekmeAdi(sonra[id]));
        // A trial that did not move a part says why (the engine's reason starts with the trial).
        if (is_.Mod != MacriaProje.DenemeYeniden)
            foreach (IAnalizSatiri satir in ParcaSatirlari(is_.Yol, is_.Parcalar)
                         .Where(x => once.GetValueOrDefault(x.ParcaLocalId!.Value) == x.Sekme))
                LogInfo("   #" + satir.ParcaLocalId + " " + satir.ParcaAdi + ": tanınmadı — " + satir.MotorGerekcesi);
    }

    /// <summary>Tab of each listed part of the STEP, by localId.</summary>
    private Dictionary<int, AnalizSekmesi> SatirSekmeleri(string yol, IReadOnlyCollection<int> idler) =>
        ParcaSatirlari(yol, idler).GroupBy(x => x.ParcaLocalId!.Value).ToDictionary(g => g.Key, g => g.First().Sekme);

    private string SatirAdi(string yol, int id) => ParcaSatirlari(yol, new[] { id }).FirstOrDefault()?.ParcaAdi ?? "";

    private IEnumerable<IAnalizSatiri> ParcaSatirlari(string yol, IReadOnlyCollection<int> idler) =>
        _externalStepProfileRows.Cast<IAnalizSatiri>().Concat(_montajParcaRows)
            .Where(x => x.ParcaLocalId is int id && idler.Contains(id) &&
                        string.Equals(Path.GetFullPath(x.KaynakYolu), yol, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The parts' rows are replaced by `profil` / `montaj`. Liste dışı, the
    /// user thickness and the written DXFs go with the part; a category
    /// decision belongs to the old result and is dropped.
    /// </summary>
    private void ParcaSatirlariniDegistir(string yol, IReadOnlyCollection<int> idler,
        List<GeometryLabStepProfileListItem> profil, List<MontajParcaSatiri> montaj)
    {
        List<GeometryLabStepProfileListItem> eskiProfil = ParcaSatirlari(yol, idler).OfType<GeometryLabStepProfileListItem>().ToList();
        List<MontajParcaSatiri> eskiMontaj = ParcaSatirlari(yol, idler).OfType<MontajParcaSatiri>().ToList();
        List<MacriaProjeKarari> tasinan = MacriaProjeSatirlari.KararlariTopla(eskiProfil, eskiMontaj, _ => "")
            .Where(k => k.Karar is MacriaProje.KararListeDisi or MacriaProje.KararKalinlik or MacriaProje.KararDxfDosyasi)
            .ToList();
        int sira = eskiProfil.Count > 0 ? _externalStepProfileRows.IndexOf(eskiProfil[0]) : _externalStepProfileRows.Count;
        foreach (GeometryLabStepProfileListItem row in eskiProfil) _externalStepProfileRows.Remove(row);
        foreach (MontajParcaSatiri row in eskiMontaj) _montajParcaRows.Remove(row);
        sira = Math.Min(sira, _externalStepProfileRows.Count);
        foreach (GeometryLabStepProfileListItem row in profil) _externalStepProfileRows.Insert(sira++, row);
        foreach (MontajParcaSatiri row in montaj) _montajParcaRows.Add(row);
        var yeni = profil.Cast<IAnalizSatiri>().Concat(montaj).ToList();
        foreach (MacriaProjeKarari karar in tasinan)
        {
            object? hedef = yeni.FirstOrDefault(x => x.ParcaLocalId == karar.Parca?.LocalId);
            if (hedef is null || !MacriaProjeSatirlari.Uygula(karar, hedef))
                LogInfo("Karar yeni sonuca taşınamadı: " + karar.Karar + " — " + karar.Parca?.Ad);
        }
    }

    /// <summary>"Otomatik Karara Dön" on rows from a run on selected parts: back to the STEP's own analysis.</summary>
    private void DenemeleriKaldir(List<IAnalizSatiri> satirlar)
    {
        foreach (IGrouping<string, IAnalizSatiri> grup in satirlar.Where(x => x.ParcaLocalId != null)
                     .GroupBy(x => Path.GetFullPath(x.KaynakYolu), StringComparer.OrdinalIgnoreCase))
        {
            if (!_projeKaynaklari.TryGetValue(grup.Key, out ProjeKaynakKaydi? kayit) || kayit.Icerik.AnalysisJson is null) continue;
            GeometryLabProcessAdapterResult ana = MacriaProjeSatirlari.AnaAnaliz(
                GeometryLabProcessAdapter.SonucuJsondanKur(kayit.Icerik.AnalysisJson, kayit.Icerik.DxfKlasoru));
            if (!MacriaProjeSatirlari.ParcaYolundan(ana)) continue;
            var idler = grup.Select(x => x.ParcaLocalId!.Value).ToHashSet();
            var (profil, montaj) = MacriaProjeSatirlari.MontajSatirlari(grup.Key, ana, _projeLazerMm);
            ParcaSatirlariniDegistir(grup.Key, idler,
                profil.Where(x => x.PartLocalId is int id && idler.Contains(id)).ToList(),
                montaj.Where(x => idler.Contains(x.PartLocalId)).ToList());
            LogInfo("Otomatik karara dönüldü: " + Path.GetFileName(grup.Key) + " — " + idler.Count + " parça STEP'in kendi analizine döndü.");
        }
        ProjeDegisti();
        AnalizSekmeleriniGuncelle();
        SagPaneliGuncelle();
    }

    /// <summary>
    /// Opening a project: the "Dene" decisions matched to parts. A source that
    /// was not rescanned takes the stored run; a rescanned one (or a missing
    /// run) runs the engine again on the same parts. Returns the decisions
    /// that could not be applied.
    /// </summary>
    private async Task<List<MacriaProjeKarari>> DenemeleriUygulaAsync(
        List<(MacriaProjeKarari Karar, MacriaKararAdayi Aday)> eslenen, MacriaProjeAcilisi acilis, Func<string, bool> yenidenTarandi)
    {
        var uygulanamayan = new List<MacriaProjeKarari>();
        var calistirilacak = new Dictionary<(string Yol, string Mod), List<int>>();
        foreach (var grup in eslenen.GroupBy(x => (x.Karar.Kaynak, x.Karar.EkAnaliz ?? "", x.Karar.DenemeModu ?? MacriaProje.DenemeYeniden)))
        {
            var satirlar = grup.Select(x => (Karar: x.Karar, Satir: (IAnalizSatiri)x.Aday.Satir)).ToList();
            string yol = Path.GetFullPath(satirlar[0].Satir.KaynakYolu);
            List<int> idler = satirlar.Select(x => x.Satir.ParcaLocalId!.Value).Distinct().OrderBy(x => x).ToList();
            string anahtar = MacriaProje.EkAnahtari(grup.Key.Kaynak, grup.Key.Item2);
            if (!yenidenTarandi(grup.Key.Kaynak) && acilis.EkAnalysisJson.TryGetValue(anahtar, out string? json) &&
                _projeKaynaklari.TryGetValue(yol, out ProjeKaynakKaydi? kayit))
            {
                GeometryLabProcessAdapterResult ek = GeometryLabProcessAdapter.SonucuJsondanKur(json, acilis.EkDxfKlasoru.GetValueOrDefault(anahtar));
                if (ek.IsSuccess && !ek.Analysis!.Otomatik)
                {
                    var deneme = new SatirDenemesi(grup.Key.Item3, grup.Key.Item2);
                    var (profil, montaj) = MacriaProjeSatirlari.EkSatirlari(yol, ek, _projeLazerMm, deneme);
                    ParcaSatirlariniDegistir(yol, idler,
                        profil.Where(x => x.PartLocalId is int id && idler.Contains(id)).ToList(),
                        montaj.Where(x => idler.Contains(x.PartLocalId)).ToList());
                    continue;
                }
            }
            if (!calistirilacak.TryGetValue((yol, grup.Key.Item3), out List<int>? liste))
                calistirilacak[(yol, grup.Key.Item3)] = liste = new List<int>();
            liste.AddRange(idler);
        }
        if (calistirilacak.Count > 0 && acilis.SaltOkunurNedeni == null)
        {
            LogInfo("Proje: " + calistirilacak.Values.Sum(x => x.Distinct().Count()) + " parçanın yeniden analizi / denemesi yeniden uygulanıyor.");
            await ParcaIsleriniCalistirAsync(calistirilacak
                .Select(x => new ParcaIsi(x.Key.Yol, x.Value.Distinct().OrderBy(id => id).ToList(), x.Key.Mod)).ToList(),
                "Denemeler yeniden uygulanıyor");
        }
        else if (calistirilacak.Count > 0)
        {
            foreach (var (karar, aday) in eslenen)
                if (calistirilacak.ContainsKey((Path.GetFullPath(((IAnalizSatiri)aday.Satir).KaynakYolu), karar.DenemeModu ?? MacriaProje.DenemeYeniden)))
                    uygulanamayan.Add(karar);
        }
        return uygulanamayan;
    }

    /// <summary>Stored runs of a source as they were opened, for saving them again.</summary>
    private static Dictionary<string, MacriaEkIcerik> EkIcerikleri(MacriaProjeAcilisi acilis, MacriaProjeKaynagi kaynak)
    {
        var ekler = new Dictionary<string, MacriaEkIcerik>(StringComparer.Ordinal);
        foreach (MacriaEkAnaliz ek in kaynak.EkAnalizler)
        {
            string anahtar = MacriaProje.EkAnahtari(kaynak.Id, ek.Id);
            if (acilis.EkAnalysisJson.TryGetValue(anahtar, out string? json))
                ekler[ek.Id] = new MacriaEkIcerik(json, acilis.EkDxfKlasoru.GetValueOrDefault(anahtar));
        }
        return ekler;
    }

    /// <summary>Only runs that a "Dene" decision still names are saved.</summary>
    private void KullanilmayanEkleriAyikla(IEnumerable<MacriaProjeKarari> kararlar)
    {
        var kullanilan = kararlar.Where(k => k.Karar == MacriaProje.KararDene)
            .Select(k => MacriaProje.EkAnahtari(k.Kaynak, k.EkAnaliz ?? "")).ToHashSet(StringComparer.Ordinal);
        foreach (ProjeKaynakKaydi kayit in _projeKaynaklari.Values)
            kayit.Kaynak.EkAnalizler.RemoveAll(e => !kullanilan.Contains(MacriaProje.EkAnahtari(kayit.Kaynak.Id, e.Id)));
    }
}
