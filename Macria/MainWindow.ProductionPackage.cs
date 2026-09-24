using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace Macria;

public partial class MainWindow
{
    private static ProductionPackageItem PackageItem(string path, int? catiaQuantity) => new()
    {
        SourcePath = path,
        PartCode = ProductionPackageService.PartCode(path),
        CatiaQuantity = catiaQuantity,
        FileNameQuantity = ProductionPackageService.FileNameQuantity(path)
    };

    private void btnDxfDwgUretimPaketi_Click(object sender, RoutedEventArgs e)
    {
        List<DxfDwgFileItem> visible = _dxfDwgView?.Cast<DxfDwgFileItem>().ToList() ?? new();
        List<DxfDwgFileItem> selected = gridDxfDwgFiles?.SelectedItems.Cast<DxfDwgFileItem>().Where(visible.Contains).ToList() ?? new();
        ShowProductionPackage(selected.Select(item => PackageItem(item.FullPath, item.CatiaQuantity)), visible.Select(item => PackageItem(item.FullPath, item.CatiaQuantity)));
    }

    private void btnExternalStepUretimPaketi_Click(object sender, RoutedEventArgs e)
    {
        List<GeometryLabStepProfileListItem> visible = _externalStepProfileView?.Cast<GeometryLabStepProfileListItem>().ToList() ?? new();
        List<GeometryLabStepProfileListItem> selected = GorunenSeciliExternalStepSatirlari();
        IEnumerable<GeometryLabStepProfileListItem> eligibleVisible = visible.Where(item => item.EffectiveCategory is GeometryLabExternalStepResultGroup.DefiniteProfile or GeometryLabExternalStepResultGroup.ProcessedProfile);
        IEnumerable<GeometryLabStepProfileListItem> sourceSelected = selected.Count > 0 ? selected : eligibleVisible;
        ShowProductionPackage(sourceSelected.Select(ToPackageItem), eligibleVisible.Select(ToPackageItem));
    }

    private static ProductionPackageItem ToPackageItem(GeometryLabStepProfileListItem item)
    {
        int? quantity = int.TryParse(item.CatiaQuantityDisplay, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
        return PackageItem(item.SourceStepPath, quantity);
    }

    private void ShowProductionPackage(IEnumerable<ProductionPackageItem> selected, IEnumerable<ProductionPackageItem> visible)
    {
        List<ProductionPackageItem> selectedRows = selected.ToList(); List<ProductionPackageItem> visibleRows = visible.ToList();
        if (selectedRows.Count == 0 && visibleRows.Count == 0)
        {
            MessageBox.Show(this, "Üretim paketi için uygun kayıt bulunamadı.", "Üretim Paketi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ProductionPackageWindow(selectedRows, visibleRows) { Owner = this }.ShowDialog();
    }
}
