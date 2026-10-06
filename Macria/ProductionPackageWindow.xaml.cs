using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Macria;

/// <summary>
/// "Üretim Paketi Hazırla": the selected or visible files are copied into a
/// new production folder next to them, named with their production quantity,
/// with an Excel manifest. The rules (ProductionPackage.cs) decide what is
/// ready; this window shows the rows, why "Paketi Oluştur" is off or what
/// stays out of the package, and the target folder.
/// </summary>
public sealed partial class ProductionPackageWindow : Window
{
    private readonly IReadOnlyList<ProductionPackageItem> _selected;
    private readonly IReadOnlyList<ProductionPackageItem> _visible;
    private readonly ObservableCollection<ProductionPackageItem> _rows = new();
    private string? _kaynakKlasor;
    private bool _hazir;

    public ProductionPackageWindow(IReadOnlyList<ProductionPackageItem> selected, IReadOnlyList<ProductionPackageItem> visible)
    {
        _selected = selected;
        _visible = visible;
        InitializeComponent();
        WindowEffects.RoundCorners(this);
        gridPaket.ItemsSource = _rows;
        cmbKapsam.SelectedIndex = selected.Count > 0 ? 0 : 1;
        _hazir = true;
        RefreshRows();
    }

    private void Ayar_Degisti(object sender, EventArgs e)
    {
        if (_hazir) RefreshRows();
    }

    private void RefreshRows()
    {
        IReadOnlyList<ProductionPackageItem> source = cmbKapsam.SelectedIndex == 0 ? _selected : _visible;
        int multiplier = int.TryParse(txtCarpan.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
        _rows.Clear();
        foreach (ProductionPackageItem item in source)
        {
            item.Multiplier = multiplier;
            item.Validate();
            _rows.Add(item);
        }
        _kaynakKlasor = ProductionPackageService.ValidateCommonFolder(_rows);
        txtHedef.Text = _kaynakKlasor == null
            ? "Kaynak dosyalar aynı klasörde olmalıdır."
            : Path.Combine(_kaynakKlasor, $"{multiplier} Kat Üretim - {DateTime.Now:yyyy-MM-dd HH-mm}");
        txtHedef.ToolTip = _kaynakKlasor == null ? null : txtHedef.Text + "\nTıklayın: kaynak klasörü açar.";
        btnKlasoruAc.IsEnabled = _kaynakKlasor != null && Directory.Exists(_kaynakKlasor);

        // Same rule as before: a common folder and at least one ready row.
        (bool canCreate, string? message) = ProductionPackageService.PackageState(_rows, _kaynakKlasor);
        btnOlustur.IsEnabled = canCreate;
        pnlUyari.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        txtUyari.Text = message ?? "";
        // Off: a warning (amber); on with rows left out: a note.
        var vurgu = (Brush)FindResource(canCreate ? "HamSacReminderTextBrush" : "WarnTextBrush");
        txtUyari.Foreground = vurgu;
        pnlUyari.BorderBrush = canCreate ? (Brush)FindResource("HamSacReminderBorderBrush") : vurgu;
        int hazir = _rows.Count(item => item.Status == "Hazır");
        txtOzet.Text = _rows.Count + " satır, " + hazir + " hazır";
    }

    // A typed base quantity re-checks the rows (and the button) after the edit.
    private void gridPaket_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
            Dispatcher.BeginInvoke(new Action(RefreshRows), DispatcherPriority.Background);
    }

    private void txtHedef_Tiklandi(object sender, MouseButtonEventArgs e) => KlasoruAc();

    private void btnKlasoruAc_Click(object sender, RoutedEventArgs e) => KlasoruAc();

    // The production folder is made on Paketi Oluştur, inside the source folder: that one opens.
    private void KlasoruAc()
    {
        if (_kaynakKlasor == null || !Directory.Exists(_kaynakKlasor) || OtomasyonModu.Acik) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _kaynakKlasor + "\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void btnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void btnMax_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void btnIptal_Click(object sender, RoutedEventArgs e) => Close();

    private void btnOlustur_Click(object? sender, RoutedEventArgs e)
    {
        RefreshRows();
        List<ProductionPackageItem> ready = _rows.Where(item => item.Status == "Hazır").ToList();
        string? folder = ProductionPackageService.ValidateCommonFolder(ready);
        if (folder == null || ready.Count == 0) return;
        if (MessageBox.Show(this, $"{ready.Count} dosya, {ready[0].Multiplier} çarpanı ile yeni üretim klasörüne kopyalanacak. Orijinal dosyalar değiştirilmeyecek.",
                "Üretim Paketi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        string target = ProductionPackageService.CreateFolder(folder, ready[0].Multiplier, DateTime.Now);
        int failed = 0;
        foreach (ProductionPackageItem item in ready)
            try { ProductionPackageService.CopyReady(item, target); }
            catch (Exception) { failed++; item.Validate(); }
        string manifestMessage = "";
        try { ExcelYazici.Yaz(CreateManifest(ready, target), Path.Combine(target, "Üretim Paketi.xlsx")); }
        catch (Exception exception) { manifestMessage = "\nDosyalar oluşturuldu; Excel manifesti oluşturulamadı: " + exception.Message; }
        MessageBox.Show(this, $"Üretim klasörü: {target}\nBaşarılı: {ready.Count - failed}, başarısız: {failed}{manifestMessage}", "Üretim Paketi",
            MessageBoxButton.OK, failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static Rapor CreateManifest(IEnumerable<ProductionPackageItem> items, string folder)
    {
        var report = new Rapor { SayfaAdi = "Üretim Paketi", TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        foreach (string header in new[] { "Orijinal Dosya", "Yeni Dosya", "Parça Kodu", "Temel Adet", "Adet Kaynağı", "Çarpan", "Üretim Adedi", "CATIA Eşleşme Durumu", "Oluşturulma Tarihi", "Kopyalama Durumu", "Açıklama" })
            report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 2 });
        foreach (ProductionPackageItem item in items)
        {
            bool copied = File.Exists(Path.Combine(folder, item.TargetFileName));
            report.Satirlar.Add(new object?[] { item.SourcePath, item.TargetFileName, item.PartCode, item.BaseQuantity, item.QuantitySource, item.Multiplier, item.FinalQuantity, item.CatiaQuantity is > 0 ? "Eşleşti" : "Eşleşmedi", DateTime.Now, copied ? "Kopyalandı" : "Başarısız", item.Explanation });
        }
        return report;
    }
}
