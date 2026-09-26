using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Macria;

public partial class MainWindow
{
    private readonly ObservableCollection<DxfDwgFileItem> _dxfDwgRows = new();
    private readonly DxfPreviewAdapter _dxfDwgPreviewAdapter = new();
    private ICollectionView? _dxfDwgView;
    private bool _dxfMatched = DxfDwgFileInventory.DefaultFilterVisible(DxfDwgMatchState.Matched);
    private bool _dxfUnmatched = DxfDwgFileInventory.DefaultFilterVisible(DxfDwgMatchState.Unmatched);
    private bool _dxfAmbiguous = DxfDwgFileInventory.DefaultFilterVisible(DxfDwgMatchState.Ambiguous);
    private bool _dxfProblem = DxfDwgFileInventory.DefaultFilterVisible(DxfDwgMatchState.Duplicate);

    private void DxfDwgListesiniKur()
    {
        _dxfDwgView = CollectionViewSource.GetDefaultView(_dxfDwgRows);
        _dxfDwgView.Filter = item => item is DxfDwgFileItem file && IsDxfDwgVisible(file);
        if (gridDxfDwgFiles != null)
            gridDxfDwgFiles.ItemsSource = _dxfDwgView;
        DxfDwgSayilari();
    }

    private bool IsDxfDwgVisible(DxfDwgFileItem item) => item.MatchState switch
    {
        DxfDwgMatchState.Matched => _dxfMatched,
        DxfDwgMatchState.Unmatched => _dxfUnmatched,
        DxfDwgMatchState.Ambiguous => _dxfAmbiguous,
        _ => _dxfProblem
    };

    private void chkDxfDwgFilter_Click(object sender, RoutedEventArgs e)
    {
        _dxfMatched = chkDxfMatched?.IsChecked == true;
        _dxfUnmatched = chkDxfUnmatched?.IsChecked == true;
        _dxfAmbiguous = chkDxfAmbiguous?.IsChecked == true;
        _dxfProblem = chkDxfProblem?.IsChecked == true;
        ClearDxfDwgSelection();
        _dxfDwgView?.Refresh();
        DxfDwgSayilari();
        DxfDwgTumButonunuGuncelle();
    }

    private void btnDxfDwgTum_Click(object sender, RoutedEventArgs e)
    {
        bool allVisible = _dxfMatched && _dxfUnmatched && _dxfAmbiguous && _dxfProblem;
        _dxfMatched = _dxfUnmatched = _dxfAmbiguous = _dxfProblem = !allVisible;
        if (chkDxfMatched != null) chkDxfMatched.IsChecked = _dxfMatched;
        if (chkDxfUnmatched != null) chkDxfUnmatched.IsChecked = _dxfUnmatched;
        if (chkDxfAmbiguous != null) chkDxfAmbiguous.IsChecked = _dxfAmbiguous;
        if (chkDxfProblem != null) chkDxfProblem.IsChecked = _dxfProblem;
        ClearDxfDwgSelection();
        _dxfDwgView?.Refresh();
        DxfDwgSayilari();
        DxfDwgTumButonunuGuncelle();
    }

    private void DxfDwgSayilari()
    {
        if (chkDxfMatched != null)
            chkDxfMatched.Content = $"Eşleşenler ({_dxfDwgRows.Count(item => item.MatchState == DxfDwgMatchState.Matched)})";
        if (chkDxfUnmatched != null)
            chkDxfUnmatched.Content = $"Eşleşmeyenler ({_dxfDwgRows.Count(item => item.MatchState == DxfDwgMatchState.Unmatched)})";
        if (chkDxfAmbiguous != null)
            chkDxfAmbiguous.Content = $"Belirsizler ({_dxfDwgRows.Count(item => item.MatchState == DxfDwgMatchState.Ambiguous)})";
        if (chkDxfProblem != null)
            chkDxfProblem.Content = $"Hatalı / Yinelenen ({_dxfDwgRows.Count(item => item.MatchState is DxfDwgMatchState.Duplicate or DxfDwgMatchState.FileError)})";
    }

    private void DxfDwgTumButonunuGuncelle()
    {
        if (btnDxfDwgTum == null)
            return;
        btnDxfDwgTum.Content = _dxfMatched && _dxfUnmatched && _dxfAmbiguous && _dxfProblem
            ? "Tümünü Gizle"
            : "Tümünü Göster";
    }

    private void ClearDxfDwgSelection()
    {
        if (gridDxfDwgFiles != null)
            gridDxfDwgFiles.SelectedItems.Clear();
        if (btnDxfDwgAc != null)
            btnDxfDwgAc.IsEnabled = false;
        DxfDwgOnizlemeBosalt("Önizlemek için listeden bir DXF seçin.");
    }

    private void gridDxfDwgFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (btnDxfDwgAc == null || gridDxfDwgFiles == null || _dxfDwgView == null)
            return;
        btnDxfDwgAc.IsEnabled = gridDxfDwgFiles.SelectedItems.Count == 1
            && _dxfDwgView.Cast<DxfDwgFileItem>().Contains(gridDxfDwgFiles.SelectedItem);

        if (btnDxfDwgAc.IsEnabled && gridDxfDwgFiles.SelectedItem is DxfDwgFileItem selected)
            DxfDwgOnizlemeyiYukle(selected);
        else
            DxfDwgOnizlemeBosalt("Önizlemek için listeden bir DXF seçin.");
    }

    private void btnDxfDwgTara_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "DXF / DWG klasörü seçin" };
        if (dialog.ShowDialog() != true)
            return;
        _dxfDwgRows.Clear();
        foreach (DxfDwgFileItem item in DxfDwgFileInventory.Scan(dialog.FolderName, chkDxfDwgAltKlasor?.IsChecked == true))
            _dxfDwgRows.Add(item);
        ClearDxfDwgSelection();
        _dxfDwgView?.Refresh();
        DxfDwgSayilari();
    }

    private void btnDxfDwgKarsilastir_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSuccessfulCatiaSnapshot == null)
        {
            MessageBox.Show(this, "Karşılaştırma için önce ana ekrandan CATIA taraması yapın.");
            return;
        }
        foreach (DxfDwgFileItem item in _dxfDwgRows)
            item.Apply(_lastSuccessfulCatiaSnapshot, item.IsDuplicate);
        ClearDxfDwgSelection();
        _dxfDwgView?.Refresh();
        DxfDwgSayilari();
    }

    private void btnDxfDwgTemizle_Click(object sender, RoutedEventArgs e)
    {
        _dxfDwgRows.Clear();
        ClearDxfDwgSelection();
        _dxfDwgView?.Refresh();
        DxfDwgSayilari();
    }

    private void btnDxfDwgAc_Click(object sender, RoutedEventArgs e)
    {
        DxfDwgFileItem? selected = gridDxfDwgFiles?.SelectedItems.Cast<DxfDwgFileItem>().SingleOrDefault();
        if (selected == null)
            return;
        if (!selected.FileType.Equals("DXF", StringComparison.OrdinalIgnoreCase)
            && !selected.FileType.Equals("DWG", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Yalnız DXF veya DWG dosyaları açılabilir.");
            return;
        }
        if (!System.IO.File.Exists(selected.FullPath))
        {
            MessageBox.Show(this, "Seçili dosya artık bulunamadı. Listeyi yeniden tarayın.");
            ClearDxfDwgSelection();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(selected.FullPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Dosya varsayılan Windows uygulamasıyla açılamadı.\n{exception.Message}");
        }
    }

    private void btnDxfDwgExcel_Click(object sender, RoutedEventArgs e)
    {
        List<DxfDwgFileItem> rows = _dxfDwgView?.Cast<DxfDwgFileItem>().ToList() ?? new();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Excel'e aktarılacak görünür sonuç bulunamadı.");
            return;
        }
        var dialog = new SaveFileDialog { Filter = "Excel Çalışma Kitabı (*.xlsx)|*.xlsx", AddExtension = true, DefaultExt = "xlsx" };
        if (dialog.ShowDialog() != true)
            return;
        var report = new Rapor { SayfaAdi = "DXF DWG Dosya Tarama", TabloIlkSatirdanBaslar = true };
        foreach (string header in new[] { "Durum", "Dosya Adı", "Dosya Türü", "Kaynak Klasör", "Tam Yol", "CATIA Adedi", "CATIA Eşleşme Durumu", "CATIA Reference Title", "Dosya Boyutu", "Değiştirilme Tarihi", "Açıklama" })
            report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 2 });
        foreach (DxfDwgFileItem item in rows)
            report.Satirlar.Add(new object?[] { item.StatusDisplay, item.FileName, item.FileType, item.Folder, item.FullPath, item.CatiaQuantity, item.StatusDisplay, item.CatiaReferenceTitle, item.SizeBytes, item.LastWriteTime, item.Explanation });
        ExcelYazici.Yaz(report, dialog.FileName);
    }

    private void DxfDwgOnizlemeyiYukle(DxfDwgFileItem item)
    {
        DxfPreviewReadResult result = _dxfDwgPreviewAdapter.Read(new PreviewRequest
        {
            SourcePath = item.FullPath,
            Capability = PreviewCapability.Preview2D,
            Presentation = PreviewPresentation.Embedded,
            SourceContext = "Dosya Analiz Merkezi"
        });

        string? message = DxfDwgPreviewMessages.For(result);
        if (message != null || result.Model is not DxfCizim drawing)
        {
            // Only real read errors reach the console; the technical text never goes into the panel.
            if (result.Content.Status == PreviewContentCheckStatus.Failed)
                LogError("DXF \u00d6nizleme Okunamad\u0131 \u2014 " + item.FileName + ": " + DxfDwgPreviewMessages.Diagnostic(result));
            DxfDwgOnizlemeBosalt(message ?? DxfDwgPreviewMessages.NothingDrawable, item.FileName);
            return;
        }

        try
        {
            if (dxfDwgOnizlemeCizim != null)
            {
                dxfDwgOnizlemeCizim.Data = drawing.Geometri();
                dxfDwgOnizlemeCizim.Visibility = Visibility.Visible;
            }
            if (txtDxfDwgOnizlemeMesaj != null)
                txtDxfDwgOnizlemeMesaj.Visibility = Visibility.Collapsed;
            if (txtDxfDwgOnizlemeDosya != null)
                txtDxfDwgOnizlemeDosya.Text = item.FileName;
            if (txtDxfDwgOnizlemeOlcu != null)
                txtDxfDwgOnizlemeOlcu.Text = $"{drawing.Genislik:N1} \u00d7 {drawing.Yukseklik:N1} mm \u00b7 {drawing.NesneSayisi} nesne";
        }
        catch (Exception exception)
        {
            DxfDwgOnizlemeBosalt(DxfDwgPreviewMessages.RenderFailed + "\n" + exception.Message, item.FileName);
        }
    }

    private void DxfDwgOnizlemeBosalt(string message, string fileName = "")
    {
        if (dxfDwgOnizlemeCizim != null)
        {
            dxfDwgOnizlemeCizim.Data = null;
            dxfDwgOnizlemeCizim.Visibility = Visibility.Collapsed;
        }
        if (txtDxfDwgOnizlemeMesaj != null)
        {
            txtDxfDwgOnizlemeMesaj.Text = message;
            txtDxfDwgOnizlemeMesaj.Visibility = Visibility.Visible;
        }
        if (txtDxfDwgOnizlemeDosya != null)
            txtDxfDwgOnizlemeDosya.Text = fileName;
        if (txtDxfDwgOnizlemeOlcu != null)
            txtDxfDwgOnizlemeOlcu.Text = "";
    }
}
