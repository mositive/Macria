using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Macria;

/// <summary>"İkon boyutu" (Ayarlar > Görünüm): icon / button size in DIP.</summary>
public enum DugmeBoyutu
{
    Kucuk,
    Orta,
    Buyuk
}

/// <summary>
/// Icons for buttons and tabs on every screen (Ikonlar/MacriaAllIcons.xaml,
/// Ikonlar/MacriaTabResources.xaml; mapping in Tasarim/Macria_Tum_Ikonlar).
/// A button keeps its text Content, Name, Click, Command, ToolTip and
/// bindings; <c>DugmeIkonu.Ikon</c> only gives it the "IkonluDugmeIcerigi"
/// content template (icon + text) and the size of the "İkon boyutu" setting.
/// A CheckBox or ToggleButton keeps its own look and behaviour and only gets
/// the icon. A TabItem gets "IkonluSekmeBasligi" (icon + its header text).
/// "Buton görünümü" and "İkon boyutu" apply at once through application
/// resources.
/// </summary>
public static class DugmeIkonu
{
    public const string YaziGorunurluguAnahtari = "DugmeYazisiGorunurlugu";
    public const string IkonBosluguAnahtari = "DugmeIkonBoslugu";
    public const string IkonBoyutuAnahtari = "DugmeIkonBoyutu";
    public const string YukseklikAnahtari = "DugmeYuksekligi";
    public const string KareGenislikAnahtari = "DugmeKareGenislik";
    public const string IcBoslukAnahtari = "DugmeIcBoslugu";
    public const string IcerikSablonuAnahtari = "IkonluDugmeIcerigi";
    public const string SekmeSablonuAnahtari = "IkonluSekmeBasligi";

    public static readonly DependencyProperty IkonProperty = DependencyProperty.RegisterAttached(
        "Ikon", typeof(ImageSource), typeof(DugmeIkonu), new PropertyMetadata(null, IkonDegisti));

    public static ImageSource? GetIkon(DependencyObject d) => (ImageSource?)d.GetValue(IkonProperty);
    public static void SetIkon(DependencyObject d, ImageSource? value) => d.SetValue(IkonProperty, value);

    /// <summary>
    /// In icon-only mode the button's name heads its tooltip (the ToolTip
    /// style shows it), unless the tooltip already starts with the name; null otherwise.
    /// </summary>
    public static readonly DependencyProperty IpucuBasligiProperty = DependencyProperty.RegisterAttached(
        "IpucuBasligi", typeof(string), typeof(DugmeIkonu), new PropertyMetadata(null));

    public static string? GetIpucuBasligi(DependencyObject d) => (string?)d.GetValue(IpucuBasligiProperty);
    public static void SetIpucuBasligi(DependencyObject d, string? value) => d.SetValue(IpucuBasligiProperty, value);

    /// <summary>
    /// The button shows only its icon in both views (a tight row: Plaka
    /// Yerleşimi's material buttons); its name is in the tooltip and AutomationProperties.Name.
    /// </summary>
    public static readonly DependencyProperty HepIkonProperty = DependencyProperty.RegisterAttached(
        "HepIkon", typeof(bool), typeof(DugmeIkonu), new PropertyMetadata(false));

    public static bool GetHepIkon(DependencyObject d) => (bool)d.GetValue(HepIkonProperty);
    public static void SetHepIkon(DependencyObject d, bool value) => d.SetValue(HepIkonProperty, value);

    /// <summary>The icon turns while the button's work runs (CATIA'yı Tara: "Taranıyor...").</summary>
    public static readonly DependencyProperty DonuyorProperty = DependencyProperty.RegisterAttached(
        "Donuyor", typeof(bool), typeof(DugmeIkonu), new PropertyMetadata(false));

    public static bool GetDonuyor(DependencyObject d) => (bool)d.GetValue(DonuyorProperty);
    public static void SetDonuyor(DependencyObject d, bool value) => d.SetValue(DonuyorProperty, value);

    /// <summary>
    /// The icon's main stroke colour: the template sets it from
    /// {DynamicResource MacriaIconForeground}, so a theme that replaces the
    /// brush recolours every icon at once. (The package's own DynamicResource
    /// inside the shared DrawingImage resources does not follow a change.)
    /// </summary>
    public static readonly DependencyProperty CizgiFircasiProperty = DependencyProperty.RegisterAttached(
        "CizgiFircasi", typeof(Brush), typeof(DugmeIkonu), new PropertyMetadata(null));

    public static Brush? GetCizgiFircasi(DependencyObject d) => (Brush?)d.GetValue(CizgiFircasiProperty);
    public static void SetCizgiFircasi(DependencyObject d, Brush? value) => d.SetValue(CizgiFircasiProperty, value);

    /// <summary>MultiBinding converter: (icon, stroke brush) -> the icon with its main pens in that brush.</summary>
    public static IMultiValueConverter Boyayici { get; } = new IkonBoyayici();

    // Icon buttons without a ToolTip of their own (3B view buttons): in
    // icon-only mode their name becomes the tooltip.
    private static readonly List<WeakReference<ButtonBase>> IpucusuzDugmeler = new();
    private static bool _yalnizIkon;
    private static DugmeBoyutu _boyut = DugmeBoyutu.Kucuk;

    /// <summary>"Buton görünümü": false = İkon ve ad (default), true = Yalnız ikon. Applies at once.</summary>
    public static bool YalnizIkon
    {
        get => _yalnizIkon;
        set => Uygula(value, _boyut);
    }

    /// <summary>"İkon boyutu". Applies at once.</summary>
    public static DugmeBoyutu Boyut
    {
        get => _boyut;
        set => Uygula(_yalnizIkon, value);
    }

    /// <summary>The size a mode starts with until the user picks one: İkon ve ad Küçük, Yalnız ikon Büyük.</summary>
    public static DugmeBoyutu VarsayilanBoyut(bool yalnizIkon) => yalnizIkon ? DugmeBoyutu.Buyuk : DugmeBoyutu.Kucuk;

    /// <summary>Icon and button size (DIP): Küçük 16 / 36, Orta 24 / 40, Büyük 32 / 48.</summary>
    public static (double Ikon, double Dugme) Olculer(DugmeBoyutu boyut) => boyut switch
    {
        DugmeBoyutu.Orta => (24, 40),
        DugmeBoyutu.Buyuk => (32, 48),
        _ => (16, 36)
    };

    /// <summary>Both settings at once; every icon button and tab follows.</summary>
    public static void Uygula(bool yalnizIkon, DugmeBoyutu boyut)
    {
        _yalnizIkon = yalnizIkon;
        _boyut = boyut;
        if (Application.Current is not Application uygulama) return;
        (double ikon, double dugme) = Olculer(boyut);
        uygulama.Resources[YaziGorunurluguAnahtari] = yalnizIkon ? Visibility.Collapsed : Visibility.Visible;
        uygulama.Resources[IkonBosluguAnahtari] = yalnizIkon ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        uygulama.Resources[IkonBoyutuAnahtari] = ikon;
        uygulama.Resources[YukseklikAnahtari] = dugme;
        // Icon only: a square button; with its name: the width the text needs.
        uygulama.Resources[KareGenislikAnahtari] = yalnizIkon ? dugme : 0.0;
        uygulama.Resources[IcBoslukAnahtari] = yalnizIkon ? new Thickness(0) : new Thickness(10, 0, 10, 0);
        IpucusuzDugmeler.RemoveAll(x => !x.TryGetTarget(out _));
        foreach (WeakReference<ButtonBase> referans in IpucusuzDugmeler)
            if (referans.TryGetTarget(out ButtonBase? d))
                YedekIpucunuUygula(d);
    }

    /// <summary>The name a harness or screen reader finds the button by.</summary>
    public static string Ad(ButtonBase dugme) => AutomationProperties.GetName(dugme) is { Length: > 0 } ad
        ? ad
        : dugme.Content as string ?? string.Empty;

    private static void IkonDegisti(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue != null) return;
        if (d is HeaderedContentControl sekme)
        {
            sekme.SetResourceReference(HeaderedContentControl.HeaderTemplateProperty, SekmeSablonuAnahtari);
            if (string.IsNullOrEmpty(AutomationProperties.GetName(sekme)))
                sekme.SetBinding(AutomationProperties.NameProperty,
                    new Binding(nameof(HeaderedContentControl.Header)) { RelativeSource = RelativeSource.Self });
            return;
        }
        if (d is not ButtonBase dugme) return;
        dugme.SetResourceReference(ContentControl.ContentTemplateProperty, IcerikSablonuAnahtari);
        dugme.ToolTipOpening += (_, _) => SetIpucuBasligi(dugme, IpucuBasligiHesapla(dugme));
        ToolTipService.SetShowOnDisabled(dugme, true);
        // XAML sets the other attributes after or before this one: look once all are in.
        if (dugme.IsInitialized) Hazirla(dugme);
        else dugme.Initialized += (_, _) => Hazirla(dugme);
    }

    private static void Hazirla(ButtonBase dugme)
    {
        // The name follows the text (e.g. "Montaj içinde göster" / "Tek başına göster").
        if (string.IsNullOrEmpty(AutomationProperties.GetName(dugme)))
            dugme.SetBinding(AutomationProperties.NameProperty,
                new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.Self });
        if (dugme.ReadLocalValue(FrameworkElement.ToolTipProperty) == DependencyProperty.UnsetValue)
        {
            IpucusuzDugmeler.Add(new WeakReference<ButtonBase>(dugme));
            YedekIpucunuUygula(dugme);
        }
        // A push button (or toggle button) takes the size setting; a check box
        // or radio button keeps its own look.
        if (dugme is CheckBox or RadioButton) return;
        dugme.SetResourceReference(FrameworkElement.HeightProperty, YukseklikAnahtari);
        dugme.SetResourceReference(FrameworkElement.MinWidthProperty, KareGenislikAnahtari);
        dugme.SetResourceReference(Control.PaddingProperty, IcBoslukAnahtari);
    }

    private static void YedekIpucunuUygula(ButtonBase dugme)
    {
        if (_yalnizIkon)
            dugme.SetBinding(FrameworkElement.ToolTipProperty,
                new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.Self });
        else
            BindingOperations.ClearBinding(dugme, FrameworkElement.ToolTipProperty);
    }

    private static string? IpucuBasligiHesapla(ButtonBase dugme)
    {
        if (!_yalnizIkon && !GetHepIkon(dugme)) return null;
        string ad = Ad(dugme);
        return ad.Length == 0 || (dugme.ToolTip as string)?.StartsWith(ad, StringComparison.Ordinal) == true ? null : ad;
    }

    private sealed class IkonBoyayici : IMultiValueConverter
    {
        // The package's main stroke colour where it is written as a literal (tab icons).
        private static readonly Color PaketCizgisi = Color.FromRgb(0xEC, 0xEA, 0xE2);

        // Per icon and colour; icons are application resources, so few entries.
        private readonly ConditionalWeakTable<DrawingImage, Dictionary<Color, DrawingImage>> _onbellek = new();

        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 1 || values[0] is not ImageSource ikon) return null;
            if (ikon is not DrawingImage cizim || values.Length < 2 || values[1] is not SolidColorBrush firca) return ikon;
            Dictionary<Color, DrawingImage> renkler = _onbellek.GetOrCreateValue(cizim);
            if (renkler.TryGetValue(firca.Color, out DrawingImage? hazir)) return hazir;

            // The main strokes: pens with a resource reference (button icons)
            // or in the package's literal foreground (tab icons). The accent
            // colours stay.
            var ana = new List<bool>();
            Kalemler(cizim.Drawing, kalem => ana.Add(kalem.ReadLocalValue(Pen.BrushProperty) is Expression ||
                                                     kalem.Brush is SolidColorBrush { Color: var c } && c == PaketCizgisi));
            DrawingImage kopya = cizim.CloneCurrentValue();
            int sira = 0;
            var renk = new SolidColorBrush(firca.Color);
            renk.Freeze();
            Kalemler(kopya.Drawing, kalem => { if (sira < ana.Count && ana[sira++]) kalem.Brush = renk; });
            kopya.Freeze();
            renkler[firca.Color] = kopya;
            return kopya;
        }

        private static void Kalemler(Drawing? cizim, Action<Pen> ziyaret)
        {
            if (cizim is DrawingGroup grup)
                foreach (Drawing alt in grup.Children) Kalemler(alt, ziyaret);
            else if (cizim is GeometryDrawing { Pen: Pen kalem })
                ziyaret(kalem);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
