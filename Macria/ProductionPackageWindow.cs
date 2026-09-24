using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Macria;

public sealed class ProductionPackageWindow : Window
{
    private readonly IReadOnlyList<ProductionPackageItem> _selected;
    private readonly IReadOnlyList<ProductionPackageItem> _visible;
    private readonly ObservableCollection<ProductionPackageItem> _rows = new();
    private readonly ComboBox _scope = new();
    private readonly TextBox _multiplier = new() { Text = "1", Width = 70 };
    private readonly TextBlock _folder = new();
    private readonly DataGrid _grid = new();
    private readonly Button _create = new() { Content = "Paketi Oluştur", MinWidth = 120 };

    public ProductionPackageWindow(IReadOnlyList<ProductionPackageItem> selected, IReadOnlyList<ProductionPackageItem> visible)
    {
        _selected = selected; _visible = visible;
        Title = "Üretim Paketi Hazırla"; Width = 980; Height = 580; MinWidth = 760; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("CardBrush");
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var controls = new WrapPanel();
        controls.Children.Add(new TextBlock { Text = "Paket kapsamı:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _scope.Items.Add("Seçili satırlar"); _scope.Items.Add("Görünür satırlar"); _scope.SelectedIndex = selected.Count > 0 ? 0 : 1; _scope.SelectionChanged += (_, _) => RefreshRows(); controls.Children.Add(_scope);
        controls.Children.Add(new TextBlock { Text = "Üretim çarpanı:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) }); _multiplier.TextChanged += (_, _) => RefreshRows(); controls.Children.Add(_multiplier);
        root.Children.Add(controls);
        _folder.Margin = new Thickness(0, 10, 0, 10); Grid.SetRow(_folder, 1); root.Children.Add(_folder);
        _grid.ItemsSource = _rows; _grid.IsReadOnly = false; _grid.AutoGenerateColumns = false; _grid.CanUserAddRows = false;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dosya", Binding = new System.Windows.Data.Binding("SourcePath"), Width = 180 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Parça Kodu", Binding = new System.Windows.Data.Binding("PartCode"), Width = 130 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Temel Adet", Binding = new System.Windows.Data.Binding("DisplayedBaseQuantity") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = 80, EditingElementStyle = ManualQuantityStyle() });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Adet Kaynağı", Binding = new System.Windows.Data.Binding("QuantitySourceDisplay"), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Üretim Çarpanı", Binding = new System.Windows.Data.Binding("Multiplier"), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Üretim Adedi", Binding = new System.Windows.Data.Binding("FinalQuantity"), Width = 90 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Yeni Dosya", Binding = new System.Windows.Data.Binding("TargetFileName"), Width = 170 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Durum / Açıklama", Binding = new System.Windows.Data.Binding("Explanation"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 2); root.Children.Add(_grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        _create.Click += Create_Click; buttons.Children.Add(_create); var cancel = new Button { Content = "İptal", Margin = new Thickness(10, 0, 0, 0), MinWidth = 90 }; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        Content = root; RefreshRows();
    }

    private void RefreshRows()
    {
        IReadOnlyList<ProductionPackageItem> source = _scope.SelectedIndex == 0 ? _selected : _visible;
        int multiplier = int.TryParse(_multiplier.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
        _rows.Clear(); foreach (ProductionPackageItem item in source) { item.Multiplier = multiplier; item.Validate(); _rows.Add(item); }
        string? common = ProductionPackageService.ValidateCommonFolder(_rows); _folder.Text = common == null ? "Hedef klasör: Kaynak dosyalar aynı klasörde olmalıdır." : $"Hedef klasör: {multiplier} Kat Üretim - {DateTime.Now:yyyy-MM-dd HH-mm}";
        _create.IsEnabled = common != null && _rows.Any(item => item.Status == "Hazır");
    }

    private static Style ManualQuantityStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(IsEnabledProperty, new System.Windows.Data.Binding("ManualEntryAllowed")));
        style.Setters.Add(new Setter(ToolTipProperty, "CATIA veya dosya adı adedi bulunamadı. Üretim için temel adedi girin."));
        return style;
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        RefreshRows(); List<ProductionPackageItem> ready = _rows.Where(item => item.Status == "Hazır").ToList(); string? folder = ProductionPackageService.ValidateCommonFolder(ready);
        if (folder == null || ready.Count == 0) return;
        if (MessageBox.Show(this, $"{ready.Count} dosya, {ready[0].Multiplier} çarpanı ile yeni üretim klasörüne kopyalanacak. Orijinal dosyalar değiştirilmeyecek.", "Üretim Paketi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        string target = ProductionPackageService.CreateFolder(folder, ready[0].Multiplier, DateTime.Now); int failed = 0;
        foreach (ProductionPackageItem item in ready) try { ProductionPackageService.CopyReady(item, target); } catch (Exception) { failed++; item.Validate(); }
        string manifestMessage = "";
        try { ExcelYazici.Yaz(CreateManifest(ready, target), Path.Combine(target, "Üretim Paketi.xlsx")); }
        catch (Exception exception) { manifestMessage = "\nDosyalar oluşturuldu; Excel manifesti oluşturulamadı: " + exception.Message; }
        MessageBox.Show(this, $"Üretim klasörü: {target}\nBaşarılı: {ready.Count - failed}, başarısız: {failed}{manifestMessage}", "Üretim Paketi", MessageBoxButton.OK, failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static Rapor CreateManifest(IEnumerable<ProductionPackageItem> items, string folder)
    {
        var report = new Rapor { SayfaAdi = "Üretim Paketi", TabloIlkSatirdanBaslar = true, IlkSatiriDondur = true, OtomatikFiltre = true };
        foreach (string header in new[] { "Orijinal Dosya", "Yeni Dosya", "Parça Kodu", "Temel Adet", "Adet Kaynağı", "Çarpan", "Üretim Adedi", "CATIA Eşleşme Durumu", "Oluşturulma Tarihi", "Kopyalama Durumu", "Açıklama" }) report.Sutunlar.Add(new RaporSutun { Ad = header, Genislik = 2 });
        foreach (ProductionPackageItem item in items)
        {
            bool copied = File.Exists(Path.Combine(folder, item.TargetFileName));
            report.Satirlar.Add(new object?[] { item.SourcePath, item.TargetFileName, item.PartCode, item.BaseQuantity, item.QuantitySource, item.Multiplier, item.FinalQuantity, item.CatiaQuantity is > 0 ? "Eşleşti" : "Eşleşmedi", DateTime.Now, copied ? "Kopyalandı" : "Başarısız", item.Explanation });
        }
        return report;
    }
}
