using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Macria;

public partial class MainWindow
{
    private readonly ObservableCollection<GeometryLabStepProfileListItem> _externalStepProfileRows = new();
    private readonly GeometryLabExternalStepFilterState _externalStepProfileFilters = new();
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
        _externalStepProfileView.Filter = item => item is GeometryLabStepProfileListItem row &&
            _externalStepProfileFilters.IsVisible(row.ResultGroup);
        if (gridExternalStepProfil != null) gridExternalStepProfil.ItemsSource = _externalStepProfileView;
        ExternalStepProfilOzetiniGuncelle();
        ExternalStepFilterDenetimleriniGuncelle();
        ExternalStepAnalizButonunuGuncelle();
    }

    private void chkExternalStepFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox) return;
        bool visible = checkBox.IsChecked == true;
        switch (checkBox.Name)
        {
            case "chkExternalStepDefinite": _externalStepProfileFilters.DefiniteProfilesVisible = visible; break;
            case "chkExternalStepReview": _externalStepProfileFilters.ReviewRequiredVisible = visible; break;
            case "chkExternalStepExcluded": _externalStepProfileFilters.ExcludedVisible = visible; break;
            case "chkExternalStepUnclassified": _externalStepProfileFilters.UnclassifiedVisible = visible; break;
            default: return;
        }
        _externalStepProfileView?.Refresh();
        ExternalStepSeciminiTemizle();
        ExternalStepFilterDenetimleriniGuncelle();
    }

    private void ExternalStepProfilOzetiniGuncelle()
    {
        int definite = Count(GeometryLabExternalStepResultGroup.DefiniteProfile);
        int review = Count(GeometryLabExternalStepResultGroup.ReviewRequired);
        int excluded = Count(GeometryLabExternalStepResultGroup.Excluded);
        int unclassified = Count(GeometryLabExternalStepResultGroup.Unclassified);
        SetFilterCaption(chkExternalStepDefinite, "Kesin profiller", definite);
        SetFilterCaption(chkExternalStepReview, "İnceleme gerekli", review);
        SetFilterCaption(chkExternalStepExcluded, "Liste dışı", excluded);
        SetFilterCaption(chkExternalStepUnclassified, "Tanımsız", unclassified);
    }

    private void btnExternalStepTumFiltreler_Click(object sender, RoutedEventArgs e)
    {
        _externalStepProfileFilters.TumunuGorunurYap(!_externalStepProfileFilters.TumGruplarGorunur);
        _externalStepProfileView?.Refresh();
        ExternalStepSeciminiTemizle();
        ExternalStepFilterDenetimleriniGuncelle();
    }

    private void ExternalStepFilterDenetimleriniGuncelle()
    {
        if (chkExternalStepDefinite != null) chkExternalStepDefinite.IsChecked = _externalStepProfileFilters.DefiniteProfilesVisible;
        if (chkExternalStepReview != null) chkExternalStepReview.IsChecked = _externalStepProfileFilters.ReviewRequiredVisible;
        if (chkExternalStepExcluded != null) chkExternalStepExcluded.IsChecked = _externalStepProfileFilters.ExcludedVisible;
        if (chkExternalStepUnclassified != null) chkExternalStepUnclassified.IsChecked = _externalStepProfileFilters.UnclassifiedVisible;
        if (btnExternalStepTumFiltreler != null) btnExternalStepTumFiltreler.Content = _externalStepProfileFilters.TumunuDegistirMetni;
        ExternalStepKomutlariniGuncelle();
    }

    private int Count(GeometryLabExternalStepResultGroup group) => _externalStepProfileRows.Count(x => x.ResultGroup == group);

    private static void SetFilterCaption(CheckBox? checkBox, string caption, int count)
    {
        if (checkBox != null) checkBox.Content = $"{caption} ({count})";
    }

    private void ExternalStepAnalizButonunuGuncelle()
    {
        if (btnExternalStepProfilAnaliz != null)
            btnExternalStepProfilAnaliz.IsEnabled = !_externalStepProfileAnalysisRunning && !_exporting;
    }

    private List<GeometryLabStepProfileListItem> GorunenSeciliExternalStepSatirlari() =>
        gridExternalStepProfil?.SelectedItems.Cast<GeometryLabStepProfileListItem>()
            .Where(item => _externalStepProfileView?.Cast<GeometryLabStepProfileListItem>().Contains(item) == true)
            .ToList() ?? new List<GeometryLabStepProfileListItem>();

    private void gridExternalStepProfil_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExternalStepKomutlariniGuncelle();
        ExternalStepAyrintiyiGuncelle();
    }

    private void ExternalStepSeciminiTemizle()
    {
        gridExternalStepProfil?.SelectedItems.Clear();
        ExternalStepKomutlariniGuncelle();
        ExternalStepAyrintiyiGuncelle();
    }

    private void ExternalStepAyrintiyiGuncelle()
    {
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari();
        ExternalStepOnizlemesiniGuncelle(selected.Count == 1 ? selected[0] : null);
        if (selected.Count != 1)
        {
            if (pnlExternalStepDetail != null) pnlExternalStepDetail.Visibility = Visibility.Collapsed;
            if (txtExternalStepDetailMessage != null)
            {
                txtExternalStepDetailMessage.Visibility = Visibility.Visible;
                txtExternalStepDetailMessage.Text = selected.Count > 1
                    ? "Birden fazla parça seçildi."
                    : "Ayrıntıları görmek için listeden bir STEP seçin.";
            }
            return;
        }

        GeometryLabStepProfileListItem item = selected[0];
        if (txtExternalStepDetailMessage != null) txtExternalStepDetailMessage.Visibility = Visibility.Collapsed;
        if (pnlExternalStepDetail != null) pnlExternalStepDetail.Visibility = Visibility.Visible;
        if (txtExternalStepDetailFile != null) txtExternalStepDetailFile.Text = "STEP dosyası: " + item.SourceFileName;
        if (txtExternalStepDetailStatus != null) txtExternalStepDetailStatus.Text = "Durum: " + item.EffectiveStatusDisplay;
        if (txtExternalStepDetailType != null) txtExternalStepDetailType.Text = "Parça türü: " + item.EffectiveProfileTypeDisplay;
        if (txtExternalStepDetailSection != null) txtExternalStepDetailSection.Text = "Kesit: " + item.SectionDisplay;
        if (txtExternalStepDetailLength != null) txtExternalStepDetailLength.Text = "Boy: " + item.LengthDisplay;
        if (txtExternalStepDetailTopology != null) txtExternalStepDetailTopology.Text = "Topoloji bilgisi: " + item.TopologyDisplay;
        if (txtExternalStepDetailCut != null) txtExternalStepDetailCut.Text = "Açılı kesim: " + item.CutDisplay;
        if (txtExternalStepDetailOperation != null) txtExternalStepDetailOperation.Text = "İşlem durumu: " + item.OperationDisplay;
        if (txtExternalStepDetailCatia != null) txtExternalStepDetailCatia.Text = "CATIA adedi: " + item.CatiaQuantityDisplay;
        if (txtExternalStepDetailMatch != null) txtExternalStepDetailMatch.Text = "Eşleşme: " + item.CatiaMatchDisplay;
        if (txtExternalStepDetailExplanation != null) txtExternalStepDetailExplanation.Text = "Açıklama: " + item.ExplanationDisplay;
    }

    private void ExternalStepKomutlariniGuncelle()
    {
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari();
        if (btnExternalStepDosyayiAc != null) btnExternalStepDosyayiAc.IsEnabled = selected.Count == 1;
        if (btnExternalStepKesin != null) btnExternalStepKesin.IsEnabled = selected.Count > 0;
        if (btnExternalStepIncele != null) btnExternalStepIncele.IsEnabled = selected.Count > 0;
        if (btnExternalStepListeDisi != null) btnExternalStepListeDisi.IsEnabled = selected.Count > 0;
        if (btnExternalStepOtomatik != null) btnExternalStepOtomatik.IsEnabled = selected.Any(x => x.HasUserDecision);
    }

    private void btnExternalStepDosyayiAc_Click(object sender, RoutedEventArgs e)
    {
        GeometryLabStepProfileListItem? item = GorunenSeciliExternalStepSatirlari().SingleOrDefault();
        if (item == null) return;
        string path = item.SourceStepPath;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "Seçilen STEP dosyası artık belirtilen konumda bulunmuyor.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Path.GetExtension(path).ToLowerInvariant() is not ".stp" and not ".step")
        {
            MessageBox.Show(this, "Seçilen dosya desteklenen bir STEP dosyası değil.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception exception)
        {
            LogError("STEP dosyası açılamadı: " + exception.Message);
            MessageBox.Show(this, "Bu dosya türü için Windows'ta varsayılan bir uygulama tanımlı değil.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            LogError("STEP dosyası açılamadı: " + exception.Message);
            MessageBox.Show(this, "Seçilen STEP dosyası açılamadı.", "Dosyayı Aç", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void btnExternalStepKesin_Click(object sender, RoutedEventArgs e)
    {
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari();
        if (selected.Count == 0) return;
        if (selected.Any(x => !x.CanUserConfirmExistingHollowProfile))
        {
            if (selected.Count != 1)
            {
                MessageBox.Show(this, "Kesit bilgisi olmayan birden fazla kayıt tek işlemde onaylanamaz. Her kaydı ayrı ayrı kontrol edin.", "Kesin Profile Aktar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new ManualHollowProfileWindow { Owner = this };
            if (dialog.ShowDialog() == true)
                selected[0].ConfirmManualHollowProfile(dialog.ProfileType, dialog.SectionDisplay);
            _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle(); ExternalStepKomutlariniGuncelle();
            return;
        }
        if (MessageBox.Show(this, "Seçilen kayıtlar kesin profil olarak onaylansın mı?", "Kesin Profile Aktar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (GeometryLabStepProfileListItem item in selected) item.ConfirmAsProfile();
        _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle(); ExternalStepKomutlariniGuncelle();
    }

    private void btnExternalStepIncele_Click(object sender, RoutedEventArgs e)
    {
        foreach (GeometryLabStepProfileListItem item in GorunenSeciliExternalStepSatirlari()) item.MoveToReview();
        _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle(); ExternalStepKomutlariniGuncelle();
    }

    private void btnExternalStepListeDisi_Click(object sender, RoutedEventArgs e)
    {
        foreach (GeometryLabStepProfileListItem item in GorunenSeciliExternalStepSatirlari()) item.ExcludeFromList();
        _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle(); ExternalStepKomutlariniGuncelle();
    }

    private void btnExternalStepOtomatik_Click(object sender, RoutedEventArgs e)
    {
        foreach (GeometryLabStepProfileListItem item in GorunenSeciliExternalStepSatirlari().Where(x => x.HasUserDecision)) item.RestoreAutomaticDecision();
        _externalStepProfileView?.Refresh(); ExternalStepProfilOzetiniGuncelle(); ExternalStepKomutlariniGuncelle();
    }

    private void btnExternalStepExcelAktar_Click(object sender, RoutedEventArgs e)
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
            ExcelYazici.Yaz(ExternalStepExcelRaporuHazirla(gorunenSatirlar), dialog.FileName);
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

    private static Rapor ExternalStepExcelRaporuHazirla(IEnumerable<GeometryLabStepProfileListItem> rows)
    {
        var rapor = new Rapor
        {
            SayfaAdi = "STEP Analiz Sonuçları",
            TabloIlkSatirdanBaslar = true,
            IlkSatiriDondur = true,
            OtomatikFiltre = true
        };
        foreach ((string name, double width) in new[]
        {
            ("Durum", 1.25), ("STEP Dosyası", 2.4), ("Parça Türü", 1.8), ("Kesit", 1.7),
            ("Boy", 1.35), ("Topoloji Bilgisi", 2.7), ("Açılı Kesim", 1.4),
            ("İşlem Durumu", 1.5), ("Kanıt / Açıklama", 3.3), ("CATIA Adedi", 1.1),
            ("CATIA Eşleşme Durumu", 1.8), ("CATIA Reference Title", 2.2), ("Karar Kaynağı", 1.5), ("Karar Notu", 2.3)
        })
            rapor.Sutunlar.Add(new RaporSutun { Ad = name, Genislik = width });

        foreach (GeometryLabStepProfileListItem row in rows)
            rapor.Satirlar.Add(new object?[]
            {
                row.AnalysisStatus, row.SourceFileName, row.EffectiveProfileTypeDisplay, row.SectionDisplay, row.LengthDisplay,
                row.TopologyDisplay, row.CutDisplay, row.OperationDisplay, row.ExplanationDisplay,
                row.CatiaQuantityDisplay, row.CatiaMatchDisplay, row.CatiaReferenceTitle,
                row.DecisionSource == GeometryLabDecisionSource.User ? "Kullanıcı" : row.DecisionSource == GeometryLabDecisionSource.ThreeDScan ? "CATIA 3B tarama" : "Otomatik", row.UserDecisionNote
            });
        return rapor;
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

        // A successful new selection explicitly replaces only this session-only external list.
        _externalStepProfileRows.Clear();
        _montajParcaRows.Clear();
        string motorDxfKlasoru = MotorDxfOturumunuYenile();
        MontajSekmeleriniGuncelle();
        foreach (string path in stepPaths)
            _externalStepProfileRows.Add(new GeometryLabStepProfileListItem { SourceStepPath = path });
        ExternalStepProfilOzetiniGuncelle();
        _externalStepProfileView?.Refresh();

        _externalStepProfileAnalysisRunning = true;
        _profilIslemde = true;
        ExternalStepAnalizButonunuGuncelle();
        ProfilButonlariniGuncelle();
        LogInfo("External STEP profil analizi başladı: " + stepPaths.Length + " dosya. Önceki oturum listesi temizlendi.");

        try
        {
            var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
            {
                EngineExecutablePath = engine.ExecutablePath!,
                Timeout = Ayarlar.GeometryLabZamanAsimi(),
                PartDxfRootDirectory = motorDxfKlasoru,
                PartTimeLimitSeconds = Ayarlar.ParcaSureSiniriSaniye,
                ThreadCount = Ayarlar.MotorIsParcacigi
            });

            // A snapshot: an assembly STEP replaces its own row with part rows.
            foreach (GeometryLabStepProfileListItem item in _externalStepProfileRows.ToList())
            {
                item.MarkAnalyzing();
                LogInfo("External STEP analiz ediliyor: " + item.SourceFileName);
                GeometryLabProcessAdapterResult result = await adapter.AnalyzeAsync(item.SourceStepPath);
                item.Apply(result);
                MontajSonucunuDagit(item, result);
                ExternalStepProfilOzetiniGuncelle();
                _externalStepProfileView?.Refresh();
                if (result.IsSuccess)
                    LogSuccess("External STEP analiz tamamlandı: " + item.SourceFileName + " — " + item.AnalysisStatus);
                else
                    LogError("External STEP analiz başarısız: " + item.SourceFileName + " — " + item.FailureReason);
            }
        }
        catch (Exception exception)
        {
            // This path has no CATIA objects and leaves every user-supplied STEP untouched.
            LogError("External STEP profil listesi beklenmeyen hata: " + exception.Message);
        }
        finally
        {
            _externalStepProfileAnalysisRunning = false;
            _profilIslemde = false;
            ExternalStepAnalizButonunuGuncelle();
            ProfilButonlariniGuncelle();
            ExternalStepProfilOzetiniGuncelle();
            _externalStepProfileView?.Refresh();
        }
    }
}
