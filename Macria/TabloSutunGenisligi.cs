using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Macria;

/// <summary>
/// Gives every column of a result grid a minimum width that keeps its header
/// readable (and a fixed-width column its designed width), so neither a
/// narrow window nor a dragged divider squeezes it; a grid narrower than its
/// columns then scrolls horizontally.
/// </summary>
internal static class TabloSutunGenisligi
{
    // Cell padding on both sides and the sort arrow.
    private const double BaslikPayi = 30;

    public static void Uygula(params DataGrid[] tablolar)
    {
        foreach (DataGrid tablo in tablolar)
        {
            tablo.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            // Header fonts are known only once the grid is in the visual tree.
            tablo.Loaded += Tablo_Loaded;
        }
    }

    private static void Tablo_Loaded(object sender, RoutedEventArgs e)
    {
        var tablo = (DataGrid)sender;
        tablo.Loaded -= Tablo_Loaded;
        var yaziTipi = new Typeface(tablo.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double pikselBasinaNokta = VisualTreeHelper.GetDpi(tablo).PixelsPerDip;
        foreach (DataGridColumn sutun in tablo.Columns)
        {
            if (sutun.Header is not string baslik || baslik.Length == 0) continue;
            var olcu = new FormattedText(baslik, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                yaziTipi, tablo.FontSize, Brushes.Black, pikselBasinaNokta);
            // Only the header decides the minimum: a minimum pinned to the
            // designed width (2026-10-04, Aşama 2b) kept the user from
            // dragging any column wider or narrower. The result grids have no
            // star column any more, so nothing squeezes the others.
            sutun.MinWidth = Math.Max(sutun.MinWidth, Math.Ceiling(olcu.Width + BaslikPayi));
        }
    }
}
