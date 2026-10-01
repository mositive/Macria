namespace Macria;

/// <summary>What the shared 3D preview toolbar and status line show for one viewport state.</summary>
public sealed record Step3BAracGorunumu(
    bool GorunumDugmeleriAcik,
    bool BuyukAcAcik,
    bool ParcaDugmesiGorunur,
    bool ParcaDugmesiAcik,
    string ParcaDugmesiMetni,
    string DurumMetni);

/// <summary>
/// Toolbar and status rules of the shared 3D preview (Step3BPaneli), kept free of WPF so the
/// rules every tab and the "Büyük Aç" window share can be tested without an HwndHost.
/// </summary>
public static class Step3BAracDurumu
{
    public const string MontajIcindeGoster = "Montaj içinde göster";
    public const string YalnizParcayiGoster = "Yalnız parçayı göster";
    public const string YalnizParcaAciklamasi = "Yalnız seçili parça gösteriliyor; montajın kalanı gizli.";
    public const string MontajIcindeAciklamasi = "Seçili parça montaj içinde vurgulanır; diğer parçalar soluk görünür.";

    public static Step3BAracGorunumu Hesapla(
        StepViewportLoadState durum, bool dosyaVar, string? parcaAdi, OcctPartView gorunum, string? viewportMesaji)
    {
        bool yuklu = durum == StepViewportLoadState.Loaded;
        bool parca = !string.IsNullOrEmpty(parcaAdi);
        string mesaj = viewportMesaji ?? "";
        // An assembly part row also says how the part is shown.
        if (yuklu && parca)
            mesaj = (mesaj.Length > 0 ? mesaj + "\n" : "") +
                    (gorunum == OcctPartView.Isolated ? YalnizParcaAciklamasi : MontajIcindeAciklamasi);

        return new Step3BAracGorunumu(
            GorunumDugmeleriAcik: yuklu,
            BuyukAcAcik: dosyaVar,
            ParcaDugmesiGorunur: parca,
            ParcaDugmesiAcik: parca && yuklu,
            // The button names the other view.
            ParcaDugmesiMetni: gorunum == OcctPartView.Isolated ? MontajIcindeGoster : YalnizParcayiGoster,
            DurumMetni: mesaj);
    }
}
