using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Macria;

/// <summary>
/// "Üretim Paketi Hazırla": the selected or visible files are copied into a
/// new production folder next to them, named with their production quantity,
/// with an Excel manifest. Opened from STEP / STP Analizi with "Profilleri
/// STEP olarak yaz" (on by default), each profile part of the Profiller tab
/// is instead written by the engine as its own STEP into Profil-STEP\
/// (ParçaNo_XAdet.stp) and read back. The rules (ProductionPackage.cs) decide
/// what is ready; this window shows the rows, why "Paketi Oluştur" is off or
/// what stays out of the package, and the target folder.
/// </summary>
public sealed partial class ProductionPackageWindow : Window
{
    private readonly IReadOnlyList<ProductionPackageItem> _selected;
    private readonly IReadOnlyList<ProductionPackageItem> _visible;
    // Profile parts of the Profiller tab; null when not opened from STEP / STP Analizi.
    private readonly IReadOnlyList<ProductionPackageItem>? _profilSecili;
    private readonly IReadOnlyList<ProductionPackageItem>? _profilGorunur;
    private readonly ObservableCollection<ProductionPackageItem> _rows = new();
    private string? _kaynakKlasor;
    private bool _hazir;
    private bool _calisiyor;

    /// <summary>The last package's folder and summary (what the result message says).</summary>
    public string? SonPaketKlasoru { get; private set; }
    public string SonOzet { get; private set; } = "";

    public ProductionPackageWindow(IReadOnlyList<ProductionPackageItem> selected, IReadOnlyList<ProductionPackageItem> visible,
        IReadOnlyList<ProductionPackageItem>? profilSecili = null, IReadOnlyList<ProductionPackageItem>? profilGorunur = null)
    {
        _selected = selected;
        _visible = visible;
        _profilSecili = profilSecili;
        _profilGorunur = profilGorunur;
        InitializeComponent();
        WindowEffects.RoundCorners(this);
        gridPaket.ItemsSource = _rows;
        pnlSecenekler.Visibility = profilGorunur != null ? Visibility.Visible : Visibility.Collapsed;
        chkProfilStep.IsChecked = profilGorunur != null;
        cmbKapsam.SelectedIndex = (ProfilStepAcik ? profilSecili!.Count : selected.Count) > 0 ? 0 : 1;
        _hazir = true;
        RefreshRows();
    }

    /// <summary>"Profilleri STEP olarak yaz" is on (only when opened from STEP / STP Analizi).</summary>
    public bool ProfilStepAcik => _profilGorunur != null && chkProfilStep.IsChecked == true;

    private void Ayar_Degisti(object sender, EventArgs e)
    {
        if (_hazir && !_calisiyor) RefreshRows();
    }

    private void RefreshRows()
    {
        IReadOnlyList<ProductionPackageItem> source = ProfilStepAcik
            ? (cmbKapsam.SelectedIndex == 0 ? _profilSecili! : _profilGorunur!)
            : (cmbKapsam.SelectedIndex == 0 ? _selected : _visible);
        int multiplier = int.TryParse(txtCarpan.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
        _rows.Clear();
        foreach (ProductionPackageItem item in source)
        {
            item.Multiplier = multiplier;
            item.Validate();
            _rows.Add(item);
        }
        if (ProfilStepAcik) ProductionPackageService.ResolveProfileNames(_rows);
        _kaynakKlasor = ProductionPackageService.ValidateCommonFolder(_rows);
        txtHedef.Text = _kaynakKlasor == null
            ? "Kaynak dosyalar aynı klasörde olmalıdır."
            : Path.Combine(_kaynakKlasor, $"{multiplier} Kat Üretim - {DateTime.Now:yyyy-MM-dd HH-mm}") +
              (ProfilStepAcik ? Path.DirectorySeparatorChar + ProfilStepAdi.Klasor : "");
        txtHedef.ToolTip = _kaynakKlasor == null ? null : txtHedef.Text + "\nTıklayın: kaynak klasörü açar.";
        btnKlasoruAc.IsEnabled = _kaynakKlasor != null && Directory.Exists(_kaynakKlasor);

        // Same rule as before: a common folder and at least one ready row.
        (bool canCreate, string? message) = ProductionPackageService.PackageState(_rows, _kaynakKlasor);
        btnOlustur.IsEnabled = canCreate && !_calisiyor;
        pnlUyari.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        txtUyari.Text = message ?? "";
        // Off: a warning (amber); on with rows left out: a note.
        var vurgu = (Brush)FindResource(canCreate ? "HamSacReminderTextBrush" : "WarnTextBrush");
        txtUyari.Foreground = vurgu;
        pnlUyari.BorderBrush = canCreate ? (Brush)FindResource("HamSacReminderBorderBrush") : vurgu;
        int hazir = _rows.Count(item => item.Status == "Hazır");
        txtOzet.Text = _rows.Count + (ProfilStepAcik ? " profil, " : " satır, ") + hazir + " hazır";
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

    private void btnIptal_Click(object sender, RoutedEventArgs e)
    {
        if (!_calisiyor) Close();
    }

    private async void btnOlustur_Click(object? sender, RoutedEventArgs e) => await OlusturAsync();

    // Questions and the result message are skipped in automation (tests).
    private bool Sor(string metin) =>
        OtomasyonModu.Acik || MessageBox.Show(this, metin, "Üretim Paketi", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Bildir(string metin, bool basarili)
    {
        SonOzet = metin;
        if (!OtomasyonModu.Acik)
            MessageBox.Show(this, metin, "Üretim Paketi", MessageBoxButton.OK, basarili ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>Paketi Oluştur: copies (or, with "Profilleri STEP olarak yaz", the profile STEPs) and the Excel manifest.</summary>
    public async Task OlusturAsync()
    {
        RefreshRows();
        List<ProductionPackageItem> ready = _rows.Where(item => item.Status == "Hazır").ToList();
        string? folder = ProductionPackageService.ValidateCommonFolder(ready);
        if (folder == null || ready.Count == 0) return;
        if (ProfilStepAcik)
        {
            if (!Sor($"{ready.Count} profil, {ready[0].Multiplier} çarpanı ile yeni üretim klasörünün {ProfilStepAdi.Klasor} klasörüne ayrı STEP olarak yazılacak. Orijinal dosyalar değiştirilmeyecek.")) return;
            await ProfilStepleriniYazAsync(ready, folder);
            return;
        }
        if (!Sor($"{ready.Count} dosya, {ready[0].Multiplier} çarpanı ile yeni üretim klasörüne kopyalanacak. Orijinal dosyalar değiştirilmeyecek.")) return;
        string target = ProductionPackageService.CreateFolder(folder, ready[0].Multiplier, DateTime.Now);
        int failed = 0;
        foreach (ProductionPackageItem item in ready)
            try { ProductionPackageService.CopyReady(item, target); }
            catch (Exception) { failed++; item.Validate(); }
        string manifestMessage = "";
        try { ExcelYazici.Yaz(CreateManifest(ready, target), Path.Combine(target, "Üretim Paketi.xlsx")); }
        catch (Exception exception) { manifestMessage = "\nDosyalar oluşturuldu; Excel manifesti oluşturulamadı: " + exception.Message; }
        SonPaketKlasoru = target;
        Bildir($"Üretim klasörü: {target}\nBaşarılı: {ready.Count - failed}, başarısız: {failed}{manifestMessage}", failed == 0);
    }

    // The engine writes the parts of each STEP in one run (the STEP is read
    // once); rows from "Profil olarak dene" or a user's profile decision run
    // with the profile trial so their axis is found. Each file is read back by
    // the engine; one that fails is reported "Yazılamadı" and the package goes on.
    private async Task ProfilStepleriniYazAsync(List<ProductionPackageItem> ready, string folder)
    {
        _calisiyor = true;
        btnOlustur.IsEnabled = false;
        btnIptal.IsEnabled = false;
        string target = ProductionPackageService.CreateFolder(folder, ready[0].Multiplier, DateTime.Now);
        string profilKlasoru = Path.Combine(target, ProfilStepAdi.Klasor);
        Directory.CreateDirectory(profilKlasoru);
        string gecici = Path.Combine(Path.GetTempPath(), "Macria", "ProfilStep", Guid.NewGuid().ToString("N"));
        try
        {
            GeometryLabEngineLocation engine = GeometryLabEngineLocator.Locate();
            var gruplar = ready.GroupBy(item => (Kaynak: Path.GetFullPath(item.SourcePath).ToUpperInvariant(), item.ProfileTrial)).ToList();
            int sira = 0;
            foreach (var grup in gruplar)
            {
                ++sira;
                txtOzet.Text = $"Profil STEP'leri yazılıyor — {sira} / {gruplar.Count}: {Path.GetFileName(grup.First().SourcePath)} ({grup.Count()} parça)";
                List<ProductionPackageItem> parcalar = grup.ToList();
                if (!engine.IsAvailable)
                {
                    foreach (ProductionPackageItem item in parcalar)
                        item.SetStepResult(ProductionPackageService.StepResult(null, "GeometryEngine bulunamadı. " + engine.Detail));
                    continue;
                }
                string grupKlasoru = Path.Combine(gecici, sira.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(grupKlasoru);
                var adapter = new GeometryLabProcessAdapter(new GeometryLabProcessAdapterOptions
                {
                    EngineExecutablePath = engine.ExecutablePath!,
                    Timeout = TimeSpan.Zero,
                    PartTimeLimitSeconds = 0,
                    ThreadCount = Ayarlar.MotorIsParcacigi,
                    SelectedPartIds = parcalar.Where(x => x.PartLocalId is int).Select(x => x.PartLocalId!.Value).ToList(),
                    Deneme = grup.Key.ProfileTrial ? MotorDenemesi.Profil : MotorDenemesi.Yok,
                    PartStepDirectory = grupKlasoru
                });
                GeometryLabProcessAdapterResult sonuc = await adapter.AnalyzeAsync(parcalar[0].SourcePath, CancellationToken.None);
                foreach (ProductionPackageItem item in parcalar)
                {
                    GeometryLabPartStepTransport? kayit = sonuc.IsSuccess
                        ? sonuc.Analysis!.PartSteps.FirstOrDefault(x => x.PartId == item.PartLocalId)
                        : null;
                    ProfilStepSonucu durum = ProductionPackageService.StepResult(kayit, sonuc.IsSuccess ? "parça motor çıktısında yok" : sonuc.Message);
                    if (durum.Durum == "Yazıldı")
                    {
                        try { File.Copy(Path.Combine(grupKlasoru, kayit!.File!), Path.Combine(profilKlasoru, item.PackageFileName), false); }
                        catch (Exception exception) { durum = durum with { Durum = "Yazılamadı", Aciklama = "kopyalanamadı: " + exception.Message }; }
                    }
                    item.SetStepResult(durum);
                }
            }
        }
        finally
        {
            try { Directory.Delete(gecici, true); } catch (Exception) { }
            _calisiyor = false;
            btnIptal.IsEnabled = true;
        }

        string manifestMessage = "";
        try
        {
            ExcelYazici.Yaz(new[] { CreateManifest(ready, target), ProfilSayfasi(ready) }, Path.Combine(target, "Üretim Paketi.xlsx"));
            File.WriteAllText(Path.Combine(profilKlasoru, "BENIOKU.txt"), ProfilBenioku(ready), new UTF8Encoding(true));
        }
        catch (Exception exception) { manifestMessage = "\nExcel manifesti / BENIOKU yazılamadı: " + exception.Message; }
        SonPaketKlasoru = target;
        int yazilan = ready.Count(x => x.StepResult?.Durum == "Yazıldı");
        int atlanan = ready.Count(x => x.StepResult?.Durum == "Atlandı");
        int yazilamayan = ready.Count - yazilan - atlanan;
        var metin = new StringBuilder($"Üretim klasörü: {target}\n{ProfilStepAdi.Klasor}: {yazilan} yazıldı, {yazilamayan} yazılamadı, {atlanan} atlandı.");
        foreach (ProductionPackageItem item in ready.Where(x => x.StepResult?.Durum != "Yazıldı").Take(10))
            metin.Append("\n• " + item.PartCode + ": " + item.StepResult?.Durum + " — " + item.StepResult?.Aciklama);
        metin.Append(manifestMessage);
        RefreshRows();
        txtOzet.Text = $"{ProfilStepAdi.Klasor}: {yazilan} yazıldı, {yazilamayan} yazılamadı, {atlanan} atlandı";
        Bildir(metin.ToString(), yazilamayan == 0 && atlanan == 0 && manifestMessage.Length == 0);
    }

    private static string Sayi(double? deger, string bicim = "0.##") =>
        deger is double d ? d.ToString(bicim, CultureInfo.GetCultureInfo("tr-TR")) : "";

    // Excel "Profiller": every profile part, its file and the read-back check.
    private static Rapor ProfilSayfasi(IEnumerable<ProductionPackageItem> items)
    {
        var report = new Rapor { SayfaAdi = "Profiller", TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        foreach (string header in new[] { "Parça No", "Ad Kaynağı", "Parça Adı", "Kaynak STEP", "STEP dosyası", "Adet", "Adet Kaynağı", "Kesit", "Boy",
                     "Hizalama", "Profil Boyu (mm)", "Yazılan Boy (mm)", "Hacim Farkı (%)", "Durum", "Açıklama" })
            report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 2 });
        foreach (ProductionPackageItem item in items)
        {
            ProfilStepSonucu? s = item.StepResult;
            report.Satirlar.Add(new object?[]
            {
                item.PartCode, item.PartCodeSource, item.PartName, Path.GetFileName(item.SourcePath),
                s?.Durum == "Yazıldı" ? ProfilStepAdi.Klasor + "\\" + item.PackageFileName : null,
                (double?)item.FinalQuantity, item.QuantitySourceDisplay, item.SectionDisplay, item.LengthDisplay,
                s == null || s.Durum == "Atlandı" ? null : s.Hizali ? "boy ekseni X, bir uç orijinde" : "eksensiz (kendi koordinatlarında)",
                s?.BoyMm is double boy ? Math.Round(boy, 2) : null, s?.KutuBoyuMm is double kutu ? Math.Round(kutu, 2) : null,
                s?.HacimFarkiYuzde is double fark ? Math.Round(fark, 4) : null, s?.Durum, s?.Aciklama
            });
        }
        return report;
    }

    private static string ProfilBenioku(IEnumerable<ProductionPackageItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Macria — Profil-STEP");
        sb.AppendLine();
        sb.AppendLine("Her dosya bir profil parçasının kendi katısıdır: boy ekseni X'e paralel, bir uç orijinde, kesit Y / Z'de ortalı.");
        sb.AppendLine("Ekseni bulunamayan parça (\"eksensiz\") kaynaktaki kendi koordinatlarında yazılır. Birim mm; ürün adı = parça adı.");
        sb.AppendLine("Renk, katman ve malzeme taşınmaz. Her dosya yazıldıktan sonra geri okundu: tek katı, geçerli B-Rep, hacim kaynakla aynı (%0,1),");
        sb.AppendLine("sınır kutusunun uzun kenarı profil boyuna eşit. Doğrulamadan geçemeyen dosya pakete konmaz (\"Yazılamadı\").");
        sb.AppendLine("Dosya adı: ParçaNo_XAdet.stp (X = temel adet × üretim çarpanı); aynı ad ikinci kez gelirse _2, _3 eklenir.");
        sb.AppendLine("Farklı STEP'lerdeki aynı parça numarasının adetleri toplanmaz; her dosya kendi adedini taşır.");
        sb.AppendLine();
        sb.AppendLine("Parça No\tAd kaynağı\tDosya\tDurum\tAçıklama");
        foreach (ProductionPackageItem item in items)
            sb.AppendLine(string.Join("\t", item.PartCode, item.PartCodeSource,
                item.StepResult?.Durum == "Yazıldı" ? item.PackageFileName : "—", item.StepResult?.Durum ?? "", item.StepResult?.Aciklama ?? ""));
        return sb.ToString();
    }

    private static Rapor CreateManifest(IEnumerable<ProductionPackageItem> items, string folder)
    {
        var report = new Rapor { SayfaAdi = "Üretim Paketi", TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        foreach (string header in new[] { "Orijinal Dosya", "Yeni Dosya", "Parça Kodu", "Temel Adet", "Adet Kaynağı", "Çarpan", "Üretim Adedi", "CATIA Eşleşme Durumu", "Oluşturulma Tarihi", "Kopyalama Durumu", "Açıklama" })
            report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 2 });
        foreach (ProductionPackageItem item in items)
        {
            if (item.ProfilStep)
            {
                bool yazildi = item.StepResult?.Durum == "Yazıldı";
                report.Satirlar.Add(new object?[] { item.SourcePath, item.NewFileDisplay, item.PartCode, item.BaseQuantity, item.QuantitySourceDisplay, item.Multiplier, item.FinalQuantity, item.CatiaQuantity is > 0 ? "Eşleşti" : "Eşleşmedi", DateTime.Now, yazildi ? "Yazıldı" : item.StepResult?.Durum ?? "Yazılamadı", item.StepResult?.Aciklama ?? item.Explanation });
                continue;
            }
            bool copied = File.Exists(Path.Combine(folder, item.TargetFileName));
            report.Satirlar.Add(new object?[] { item.SourcePath, item.TargetFileName, item.PartCode, item.BaseQuantity, item.QuantitySource, item.Multiplier, item.FinalQuantity, item.CatiaQuantity is > 0 ? "Eşleşti" : "Eşleşmedi", DateTime.Now, copied ? "Kopyalandı" : "Başarısız", item.Explanation });
        }
        return report;
    }
}
