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
        // "Profilleri STEP olarak yaz": the profile parts of the Profiller tab.
        List<GeometryLabStepProfileListItem> profilGorunur = visible.Where(ProfilStepeUygun).ToList();
        List<GeometryLabStepProfileListItem> profilSecili = selected.Where(ProfilStepeUygun).ToList();
        // A STEP with one part: its file name may be the only meaningful number.
        var parcaSayilari = _externalStepProfileRows.Cast<IAnalizSatiri>().Concat(_montajParcaRows)
            .Where(x => x.ParcaLocalId != null)
            .GroupBy(x => System.IO.Path.GetFullPath(x.KaynakYolu), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ParcaLocalId).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        ProductionPackageItem Ogesi(GeometryLabStepProfileListItem row) =>
            ToProfilStepItem(row, parcaSayilari.GetValueOrDefault(System.IO.Path.GetFullPath(row.SourceStepPath)) == 1);
        ShowProductionPackage(sourceSelected.Select(ToPackageItem), eligibleVisible.Select(ToPackageItem),
            profilSecili.Select(Ogesi).ToList(), profilGorunur.Select(Ogesi).ToList());
    }

    /// <summary>
    /// A part written as its own STEP: a row of the Profiller tab (automatic,
    /// "Profil olarak onayla", "Profil olarak dene", processed profiles) that
    /// is a part of its STEP; round tubes and CATIA-confirmed sheets are not.
    /// </summary>
    private static bool ProfilStepeUygun(GeometryLabStepProfileListItem row) =>
        ((IAnalizSatiri)row).Sekme == AnalizSekmesi.Profiller && row.PartLocalId is int &&
        row.EffectiveProfileTypeDisplay is not ("Boru" or "Sac Parça");

    private static ProductionPackageItem ToProfilStepItem(GeometryLabStepProfileListItem row, bool tekParca)
    {
        (string no, string kaynak) = ProfilStepAdi.ParcaNo(row.PartProductId, row.PartName,
            tekParca ? CatiaStepMatcher.NormalizeFileIdentity(row.SourceStepPath) : null);
        int? catia = int.TryParse(row.CatiaQuantityDisplay, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
        return new ProductionPackageItem
        {
            SourcePath = row.SourceStepPath,
            PartCode = no,
            PartCodeSource = kaynak,
            ProfilStep = true,
            PartLocalId = row.PartLocalId,
            PartName = row.PartName,
            StepQuantity = row.PartQuantity > 0 ? row.PartQuantity : null,
            CatiaQuantity = catia,
            // Not an automatic profile: the profile trial finds the axis the engine needs.
            ProfileTrial = row.Deneme is { Mod: MacriaProje.DenemeProfil } || row.HasUserDecision,
            SectionDisplay = row.SectionDisplay,
            LengthDisplay = row.LengthDisplay
        };
    }

    private static ProductionPackageItem ToPackageItem(GeometryLabStepProfileListItem item)
    {
        int? quantity = int.TryParse(item.CatiaQuantityDisplay, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
        return PackageItem(item.SourceStepPath, quantity);
    }

    private void ShowProductionPackage(IEnumerable<ProductionPackageItem> selected, IEnumerable<ProductionPackageItem> visible,
        List<ProductionPackageItem>? profilSecili = null, List<ProductionPackageItem>? profilGorunur = null)
    {
        List<ProductionPackageItem> selectedRows = selected.ToList(); List<ProductionPackageItem> visibleRows = visible.ToList();
        if (selectedRows.Count == 0 && visibleRows.Count == 0 && profilGorunur is not { Count: > 0 })
        {
            MessageBox.Show(this, "Üretim paketi için uygun kayıt bulunamadı.", "Üretim Paketi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ProductionPackageWindow(selectedRows, visibleRows, profilSecili, profilGorunur) { Owner = this }.ShowDialog();
    }
}
