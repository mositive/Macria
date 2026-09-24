using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Macria;

/// <summary>Small session-only confirmation dialog; it never writes to the STEP or GeometryLab JSON.</summary>
internal sealed class ManualHollowProfileWindow : Window
{
    private readonly ComboBox _type = new() { MinWidth = 210 };
    private readonly TextBox _first = new() { MinWidth = 140 };
    private readonly TextBox _second = new() { MinWidth = 140 };
    private readonly TextBox _wall = new() { MinWidth = 140 };
    private readonly TextBlock _firstLabel = new();
    private readonly TextBlock _secondLabel = new();
    private Grid? _secondMeasurementRow;

    public string ProfileType { get; private set; } = "";
    public string SectionDisplay { get; private set; } = "";

    public ManualHollowProfileWindow()
    {
        Title = "Manuel Kutu Profil Onayı";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _type.Items.Add("Kare Kutu Profil");
        _type.Items.Add("Dikdörtgen Kutu Profil");
        _type.Items.Add("Boru");
        _type.SelectedIndex = 0;
        _type.SelectionChanged += (_, _) => UpdateLabels();

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Yalnız profil türü ve kesit bu oturum için onaylanır. Boy ve kesim bilgisi girilmez.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        AddRow(panel, "Profil türü", _type);
        AddRow(panel, _firstLabel, _first);
        _secondMeasurementRow = AddRow(panel, _secondLabel, _second);
        AddRow(panel, "Et kalınlığı (mm)", _wall);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "İptal", MinWidth = 80, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Onayla", MinWidth = 80, IsDefault = true };
        ok.Click += (_, _) => Confirm();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;
        UpdateLabels();
    }

    private static Grid AddRow(Panel panel, string label, Control input)
    {
        var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        return AddRow(panel, labelBlock, input);
    }

    private static Grid AddRow(Panel panel, TextBlock label, Control input)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(label, 0); Grid.SetColumn(input, 1);
        row.Children.Add(label); row.Children.Add(input); panel.Children.Add(row);
        return row;
    }

    private void UpdateLabels()
    {
        string type = _type.SelectedItem as string ?? "Kare Kutu Profil";
        bool square = type == "Kare Kutu Profil";
        bool pipe = type == "Boru";
        _firstLabel.Text = pipe ? "Dış çap (mm)" : square ? "Dış ölçü (mm)" : "Genişlik (mm)";
        _secondLabel.Text = "Yükseklik (mm)";
        if (_secondMeasurementRow != null)
            _secondMeasurementRow.Visibility = square || pipe ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Confirm()
    {
        if (!TryParsePositive(_first.Text, out double first) || !TryParsePositive(_wall.Text, out double wall))
        {
            ShowValidation("Dış ölçü/çap ve et kalınlığı pozitif bir sayı olmalıdır.");
            return;
        }

        string type = _type.SelectedItem as string ?? "";
        double second = first;
        if (type == "Dikdörtgen Kutu Profil" && !TryParsePositive(_second.Text, out second))
        {
            ShowValidation("Genişlik, yükseklik ve et kalınlığı pozitif bir sayı olmalıdır.");
            return;
        }
        if (wall * 2 >= Math.Min(first, second))
        {
            ShowValidation("Et kalınlığı dış ölçünün fiziksel sınırları içinde olmalıdır.");
            return;
        }

        ProfileType = type;
        SectionDisplay = type switch
        {
            "Kare Kutu Profil" => $"{Format(first)} × {Format(first)} × {Format(wall)} mm",
            "Dikdörtgen Kutu Profil" => $"{Format(first)} × {Format(second)} × {Format(wall)} mm",
            _ => $"Ø{Format(first)} × {Format(wall)} mm"
        };
        DialogResult = true;
    }

    private static bool TryParsePositive(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && value > 0;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private void ShowValidation(string message) => MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
