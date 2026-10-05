using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Macria;

public partial class MainWindow
{
    private readonly ObservableCollection<GeometryLabStepProfileListItem> _externalStepProfileRows = new();
    private ICollectionView? _externalStepProfileView;
    private bool _externalStepProfileAnalysisRunning;
    private CatiaScanSnapshot? _lastSuccessfulCatiaSnapshot;

    private void btnExternalStepCatiaKarsilastir_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSuccessfulCatiaSnapshot == null) { MessageBox.Show(this, "Karşılaştırma için önce ana ekrandan CATIA taraması yapın."); return; }
        if (_externalStepProfileRows.Count == 0 && _montajParcaRows.Count == 0) { MessageBox.Show(this, "Karşılaştırılacak STEP analiz sonucu bulunamadı."); return; }
        // Assembly part rows are matched by part name, file rows by file name.
        foreach (var row in _externalStepProfileRows)
            row.ApplyCatiaComparison(row.PartName is null
                ? CatiaStepMatcher.Match(_lastSuccessfulCatiaSnapshot, row.SourceStepPath)
                : CatiaStepMatcher.MatchPartName(_lastSuccessfulCatiaSnapshot, row.PartName));
        MontajCatiaKarsilastir(_lastSuccessfulCatiaSnapshot);
        _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle();
    }

    private void ExternalStepProfilListesiniKur()
    {
        _externalStepProfileView = CollectionViewSource.GetDefaultView(_externalStepProfileRows);
        // Profiller lists the definite profiles only; the rest is on the other tabs.
        _externalStepProfileView.Filter = item => item is GeometryLabStepProfileListItem row &&
            row.Sekme == AnalizSekmesi.Profiller;
        if (gridExternalStepProfil != null) gridExternalStepProfil.ItemsSource = _externalStepProfileView;
        // "Tabloyu Temizle" is enabled only while there is something to clear.
        _externalStepProfileRows.CollectionChanged += (_, _) => ExternalStepAnalizButonunuGuncelle();
        _montajParcaRows.CollectionChanged += (_, _) => ExternalStepAnalizButonunuGuncelle();
        ExternalStepAnalizButonunuGuncelle();
    }

    private void ExternalStepProfilOzetiniGuncelle() => AnalizSekmeleriniGuncelle();

    private void ExternalStepAnalizButonunuGuncelle()
    {
        if (btnExternalStepProfilAnaliz != null)
            btnExternalStepProfilAnaliz.IsEnabled = !_externalStepProfileAnalysisRunning && !_exporting;
        if (btnExternalStepTemizle != null)
            btnExternalStepTemizle.IsEnabled = !_externalStepProfileAnalysisRunning &&
                (_externalStepProfileRows.Count > 0 || _montajParcaRows.Count > 0);
        if (btnProjeAc != null) ProjeDurumunuGoster();
    }

    // Empties Profiller, Saclar and Kontrol gerekli without starting a new
    // analysis; user decisions in them are lost, so it asks first.
    private void btnExternalStepTemizle_Click(object sender, RoutedEventArgs e)
    {
        if (_externalStepProfileAnalysisRunning) return;
        int satir = _externalStepProfileRows.Count + _montajParcaRows.Count;
        if (satir == 0) return;
        if (_projeKirli)
        {
            if (!ProjeDegisiklikleriniSor("Tabloyu temizleme")) return;
        }
        else if (!OnayWindow.Sor(this, "Tabloyu Temizle",
                "STEP analiz sonuçları (Profiller, Saclar, Kontrol gerekli) ve bunlarda verdiğiniz kararlar " +
                "(Liste dışı, sac onayları) silinecek. Geri almak için dosyaları yeniden analiz etmeniz gerekir.",
                "Temizle"))
            return;

        OcctPreviewWindow? buyuk = _externalStepPreviewWindow;
        buyuk?.Close();
        ExternalStepSeciminiTemizle();
        gridSacParcalar?.SelectedItems.Clear();
        gridKontrolParcalar?.SelectedItems.Clear();
        gridTanimsiz?.SelectedItems.Clear();
        gridListeDisi?.SelectedItems.Clear();
        _externalStepProfileRows.Clear();
        _montajParcaRows.Clear();
        Step3BModelHazirlayici.Temizle();
        MotorDxfOturumunuTemizle();
        SagPaneliGuncelle();
        MontajSekmeleriniGuncelle();
        ExternalStepProfilOzetiniGuncelle();
        _externalStepProfileView?.Refresh();
        tabExternalStepSonuc.SelectedItem = tabExternalStepProfiller;
        ProjeyiSifirla();
        LogInfo("STEP analiz tablosu temizlendi: " + satir + " satır.");
    }

    private List<GeometryLabStepProfileListItem> GorunenSeciliExternalStepSatirlari() =>
        gridExternalStepProfil?.SelectedItems.Cast<GeometryLabStepProfileListItem>()
            .Where(item => _externalStepProfileView?.Cast<GeometryLabStepProfileListItem>().Contains(item) == true)
            .ToList() ?? new List<GeometryLabStepProfileListItem>();

    private void gridExternalStepProfil_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExternalStepKomutlariniGuncelle();
        SagPaneliGuncelle();
    }

    private void ExternalStepSeciminiTemizle()
    {
        gridExternalStepProfil?.SelectedItems.Clear();
        ExternalStepKomutlariniGuncelle();
        SagPaneliGuncelle();
    }

    private void ExternalStepKomutlariniGuncelle() => AracCubugunuGuncelle();

    /// <summary>
    /// "Profil Olarak Onayla": rows with a section are confirmed as they are; a
    /// single row without one gets its type and section from the manual dialog.
    /// </summary>
    private void ProfilleriOnayla(List<GeometryLabStepProfileListItem> selected)
    {
        if (selected.Count == 0 || ProjeSaltOkunurUyarisi()) return;
        if (selected.Any(x => !x.CanUserConfirmExistingHollowProfile))
        {
            if (selected.Count != 1)
            {
                MessageBox.Show(this, "Kesit bilgisi olmayan birden fazla kayıt tek işlemde onaylanamaz. Her kaydı ayrı ayrı kontrol edin.", "Profil Olarak Onayla", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new ManualHollowProfileWindow { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                selected[0].ConfirmManualHollowProfile(dialog.ProfileType, dialog.SectionDisplay);
                ProjeDegisti();
            }
            AnalizSekmeleriniGuncelle();
            return;
        }
        if (MessageBox.Show(this, "Seçilen kayıtlar kesin profil olarak onaylansın mı?", "Profil Olarak Onayla", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (GeometryLabStepProfileListItem item in selected) item.ConfirmAsProfile();
        ProjeDegisti();
        AnalizSekmeleriniGuncelle();
    }

    private void ProfilExcelAktar()
    {
        List<GeometryLabStepProfileListItem> gorunenSatirlar = (_externalStepProfileView?.Cast<GeometryLabStepProfileListItem>()
            ?? Enumerable.Empty<GeometryLabStepProfileListItem>()).ToList();
        if (gorunenSatirlar.Count == 0)
        {
            MessageBox.Show(this, "Excel’e aktarılacak görünür sonuç bulunamadı.", "Excel’e Aktar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "External STEP Analiz Sonuçlarını Kaydet",
            Filter = "Excel Çalışma Kitabı (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "Macria_STEP_Analiz_Sonuclari_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ExcelYazici.Yaz(TabloRaporu(gridExternalStepProfil, "STEP Analiz Sonuçları"), dialog.FileName);
            LogSuccess("External STEP Excel dosyası oluşturuldu: " + dialog.FileName);
            MessageBox.Show(this, "Excel dosyası oluşturuldu:\n" + dialog.FileName, "Excel’e Aktar",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            LogError("External STEP Excel dosyası yazılamadı: " + exception.Message);
            MessageBox.Show(this, "Excel dosyası yazılamadı.\n" + exception.Message, "Excel’e Aktar",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnExternalStepProfilAnaliz_Click(object sender, RoutedEventArgs e)
    {
        if (_externalStepProfileAnalysisRunning || _profilIslemde || _exporting) return;

        var stepDialog = new OpenFileDialog
        {
            Title = "Analiz Edilecek Haricî STEP Dosyalarını Seçin",
            Filter = "STEP Dosyaları (*.stp;*.step)|*.stp;*.step",
            CheckFileExists = true,
            Multiselect = true
        };
        if (stepDialog.ShowDialog(this) != true) return;

        string[] stepPaths = stepDialog.FileNames
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (stepPaths.Length == 0) return;

        GeometryLabEngineLocation engine = GeometryLabEngineLocator.Locate();
        if (!engine.IsAvailable)
        {
            LogError("GeometryEngine bulunamadı. Beklenen yol: " + engine.ExpectedPackagedPath +
                " Ayrıntı: " + engine.Detail);
            MessageBox.Show(this,
                "Geometry analiz motoru bulunamadı veya eksik kuruldu. Macria kurulumunu onarın ya da yeniden kurun.",
                "Geometry Analiz Motoru", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        LogInfo("GeometryEngine bulundu: " + engine.ExecutablePath + " (" + engine.Kind + ")");
        // The new analysis replaces the list, and with it an open project.
        if (!ProjeDegisiklikleriniSor("Yeni analiz")) return;

        // A successful new selection explicitly replaces only this session-only external list.
        _externalStepProfileRows.Clear();
        Step3BModelHazirlayici.Temizle();
        _montajParcaRows.Clear();
        ProjeyiSifirla();
        string motorDxfKlasoru = MotorDxfOturumunuYenile();
        MontajSekmeleriniGuncelle();
        foreach (string path in stepPaths)
            _externalStepProfileRows.Add(new GeometryLabStepProfileListItem { SourceStepPath = path });
        ExternalStepProfilOzetiniGuncelle();
        _externalStepProfileView?.Refresh();
        LogInfo("STEP analizi başladı: " + stepPaths.Length + " dosya. Önceki oturum listesi temizlendi.");
        // A snapshot: an assembly STEP replaces its own row with part rows.
        await ExternalStepDosyalariniAnalizEt(_externalStepProfileRows.ToList(), engine, motorDxfKlasoru);
    }

    /// <summary>
    /// Analyses the given file rows one by one with progress and Cancel; each
    /// result becomes rows and a source of the open (or new) project.
    /// </summary>
    private async Task ExternalStepDosyalariniAnalizEt(List<GeometryLabStepProfileListItem> dosyalar,
        GeometryLabEngineLocation engine, string motorDxfKlasoru)
    {
        _externalStepProfileAnalysisRunning = true;
        _profilIslemde = true;
        ExternalStepAnalizButonunuGuncelle();
        ProfilButonlariniGuncelle();

        using var iptal = new CancellationTokenSource();
        _externalStepAnalizIptal = iptal;
        string dosyaOn = "";
        ExternalStepIlerlemesiniGoster(true);
        try
        {
            GeometryLabMotorKimligi? motorKimligi = await Task.Run(() => MotorKimliginiOku(engine));
            var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
            {
                EngineExecutablePath = engine.ExecutablePath!,
                // No whole-run limit by default: progress is shown and the
                // user cancels; the engine limits each part on its own.
                Timeout = Ayarlar.GeometryLabZamanAsimi(),
                PartDxfRootDirectory = motorDxfKlasoru,
                PartTimeLimitSeconds = Ayarlar.ParcaSureSiniriSaniye,
                ThreadCount = Ayarlar.MotorIsParcacigi,
                ProgressChanged = progress => Dispatcher.BeginInvoke(() => ExternalStepIlerlemesiniYaz(dosyaOn, progress))
            });

            for (int index = 0; index < dosyalar.Count; ++index)
            {
                GeometryLabStepProfileListItem item = dosyalar[index];
                if (iptal.IsCancellationRequested)
                {
                    item.Apply(new GeometryLabProcessAdapterResult
                    {
                        Status = GeometryLabProcessAdapterStatus.Cancelled,
                        Message = "Analiz başlamadan iptal edildi."
                    });
                    continue;
                }
                dosyaOn = dosyalar.Count > 1 ? "Dosya " + (index + 1) + " / " + dosyalar.Count + " — " + item.SourceFileName + ": " : item.SourceFileName + ": ";
                ExternalStepIlerlemesiniYaz(dosyaOn, null);
                item.MarkAnalyzing();
                LogInfo("External STEP analiz ediliyor: " + item.SourceFileName);
                var sure = System.Diagnostics.Stopwatch.StartNew();
                GeometryLabProcessAdapterResult result = await adapter.AnalyzeAsync(item.SourceStepPath, iptal.Token);
                sure.Stop();
                item.Apply(result);
                MontajSonucunuDagit(item, result);
                await ProjeKaynaginiKaydetAsync(item.SourceStepPath, result, sure.Elapsed, motorKimligi);
                ExternalStepProfilOzetiniGuncelle();
                _externalStepProfileView?.Refresh();
                string sureMetni = SureMetni(sure.Elapsed);
                if (result.IsSuccess)
                {
                    // The 3D model is read once in the background and shared by every panel.
                    Step3BModelHazirlayici.Hazirla(item.SourceStepPath);
                    int parcaSayisi = result.Analysis?.Parts.Count ?? 0;
                    LogSuccess("External STEP analiz tamamlandı: " + item.SourceFileName + " — " + item.AnalysisStatus);
                    LogInfo(item.SourceFileName + ": " + (parcaSayisi > 0 ? parcaSayisi + " parça, " : "") + sureMetni);
                }
                else if (result.Status == GeometryLabProcessAdapterStatus.Cancelled)
                    LogInfo("External STEP analizi iptal edildi: " + item.SourceFileName + " (" + sureMetni + ")");
                else
                    LogError("External STEP analiz başarısız: " + item.SourceFileName + " — " + item.FailureReason + " (" + sureMetni + ")");
            }
        }
        catch (Exception exception)
        {
            // This path has no CATIA objects and leaves every user-supplied STEP untouched.
            LogError("External STEP profil listesi beklenmeyen hata: " + exception.Message);
        }
        finally
        {
            _externalStepAnalizIptal = null;
            ExternalStepIlerlemesiniGoster(false);
            _externalStepProfileAnalysisRunning = false;
            _profilIslemde = false;
            ExternalStepAnalizButonunuGuncelle();
            ProfilButonlariniGuncelle();
            ExternalStepProfilOzetiniGuncelle();
            _externalStepProfileView?.Refresh();
        }
    }

    // Cancels the running Dosya Analiz Merkezi STEP analysis; the engine
    // process is killed and the files already analysed stay in the list.
    private CancellationTokenSource? _externalStepAnalizIptal;

    private void btnExternalStepIptal_Click(object sender, RoutedEventArgs e)
    {
        if (_externalStepAnalizIptal is not { IsCancellationRequested: false } iptal) return;
        iptal.Cancel();
        btnExternalStepIptal.IsEnabled = false;
        txtExternalStepIlerleme.Text = "İptal ediliyor...";
        LogInfo("External STEP analizi için iptal istendi.");
    }

    private void ExternalStepIlerlemesiniGoster(bool gorunur)
    {
        pnlExternalStepIlerleme.Visibility = gorunur ? Visibility.Visible : Visibility.Collapsed;
        btnExternalStepIptal.IsEnabled = gorunur;
        barExternalStepIlerleme.IsIndeterminate = true;
        IlerlemeDegeriniAyarla(0, animasyonlu: false);
        txtExternalStepIlerleme.Text = "Hazırlanıyor...";
    }

    // Forward steps glide (250 ms) instead of jumping; a new stage that starts
    // again from a lower count is set at once.
    private void IlerlemeDegeriniAyarla(double hedef, bool animasyonlu)
    {
        if (!animasyonlu || hedef < barExternalStepIlerleme.Value)
        {
            barExternalStepIlerleme.BeginAnimation(ProgressBar.ValueProperty, null);
            barExternalStepIlerleme.Value = hedef;
            return;
        }
        barExternalStepIlerleme.BeginAnimation(ProgressBar.ValueProperty,
            new System.Windows.Media.Animation.DoubleAnimation(hedef, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
            });
    }

    // Moving bar while the stage has no count (reading, topology), filling bar
    // with "Parçalar analiz ediliyor: 142 / 195" once parts are counted.
    private void ExternalStepIlerlemesiniYaz(string dosyaOn, GeometryLabProgress? progress)
    {
        if (_externalStepAnalizIptal is null || _externalStepAnalizIptal.IsCancellationRequested) return;
        bool sayili = progress is { Total: > 0 } && (progress.Stage == "parca" || progress.Stage == "sac");
        barExternalStepIlerleme.IsIndeterminate = !sayili;
        if (sayili) IlerlemeDegeriniAyarla(Math.Min(1.0, (double)progress!.Done / progress.Total), animasyonlu: true);
        txtExternalStepIlerleme.Text = dosyaOn + (progress?.Display ?? "başlatılıyor");
    }
}
