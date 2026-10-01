using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Macria;

public enum Step3BPaneliBoyutu { Kompakt, Normal }

/// <summary>
/// The shared 3D STEP preview: toolbar, OCCT viewport and status line. Every Dosya Analiz
/// Merkezi preview and the "Büyük Aç" window use it, so a toolbar feature exists everywhere.
/// The owner keeps the part view (alone / in the assembly) shared across panels and pushes it
/// in with <see cref="Goster"/> or <see cref="SetPartView"/>.
/// </summary>
public partial class Step3BPaneli : UserControl
{
    private const string EksikDosyaMesaji = "STEP dosyası artık belirtilen konumda bulunmuyor.";

    private readonly OcctViewportHostPort _port;
    private readonly OcctStepPreviewAdapter _adapter;
    private string? _yol;
    private string? _parcaAdi;
    private OcctPartView _gorunum = OcctPartView.Isolated;
    private bool _dosyaVar;
    // Shown instead of the viewport message while the viewport is empty (e.g. "Birden fazla
    // parça seçildi."); loading, errors and the loaded model always show the viewport's own text.
    private string? _bosMesaj;
    private Step3BPaneliBoyutu _boyut = Step3BPaneliBoyutu.Kompakt;

    /// <summary>The panel asks its owner to open the "Büyük Aç" window for <see cref="StepYolu"/>.</summary>
    public event EventHandler? BuyukAcIstendi;

    /// <summary>The panel asks its owner to switch the shared part view.</summary>
    public event EventHandler? ParcaGorunumuDegistirIstendi;

    public event EventHandler<OcctViewportDiagnosticEventArgs>? Diagnostic;

    public Step3BPaneli()
    {
        InitializeComponent();
        _port = new OcctViewportHostPort(viewport);
        _adapter = new OcctStepPreviewAdapter(_port);
        viewport.StatusChanged += Viewport_StatusChanged;
        viewport.Diagnostic += Viewport_Diagnostic;
        BoyutuUygula();
        Guncelle();
    }

    public string DiagnosticName
    {
        get => viewport.DiagnosticName;
        set => viewport.DiagnosticName = value;
    }

    public bool BuyukAcGorunur
    {
        get => btnBuyukAc.Visibility == Visibility.Visible;
        set => btnBuyukAc.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool BaslikGorunur
    {
        get => pnlBaslik.Visibility == Visibility.Visible;
        set => pnlBaslik.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public Step3BPaneliBoyutu Boyut
    {
        get => _boyut;
        set { _boyut = value; BoyutuUygula(); }
    }

    /// <summary>The STEP the panel shows (or last tried to show); null when cleared.</summary>
    public string? StepYolu => _yol;

    /// <summary>The assembly part shown in <see cref="StepYolu"/>; null for a whole-file row.</summary>
    public string? ParcaAdi => _parcaAdi;

    public OcctViewportState State => viewport.State;

    /// <summary>
    /// Shows a STEP file; for an assembly part row, <paramref name="parcaAdi"/> is shown alone or
    /// inside the faded assembly as <paramref name="gorunum"/> says. A null path clears the panel.
    /// </summary>
    public void Goster(string? stepYolu, string? parcaAdi, OcctPartView gorunum)
    {
        if (string.IsNullOrWhiteSpace(stepYolu))
        {
            Temizle(null);
            return;
        }

        _yol = stepYolu;
        _parcaAdi = string.IsNullOrWhiteSpace(parcaAdi) ? null : parcaAdi;
        _gorunum = gorunum;
        _dosyaVar = File.Exists(stepYolu);
        BasligiGuncelle();

        PreviewResult result = _adapter.Load(new PreviewRequest
        {
            SourcePath = stepYolu,
            Capability = PreviewCapability.Preview3D,
            Presentation = BuyukAcGorunur ? PreviewPresentation.Embedded : PreviewPresentation.Large,
            SourceContext = DiagnosticName
        });

        if (result.IsReady)
        {
            _bosMesaj = null;
            viewport.ShowPart(_parcaAdi, _gorunum);
        }
        else if (result.Status == PreviewResultStatus.Failed)
        {
            // The viewport usually reports the error itself; this covers a thrown load.
            _bosMesaj = result.Message;
        }
        else
        {
            viewport.ClearModel();
            _bosMesaj = result.Status == PreviewResultStatus.MissingFile ? EksikDosyaMesaji : result.Message;
        }
        Guncelle();
    }

    /// <summary>Empties the panel; <paramref name="mesaj"/> replaces the viewport's empty-state text.</summary>
    public void Temizle(string? mesaj)
    {
        _yol = null;
        _parcaAdi = null;
        _dosyaVar = false;
        _bosMesaj = mesaj;
        BasligiGuncelle();
        _adapter.Clear();
        Guncelle();
    }

    public void SetPartView(OcctPartView gorunum)
    {
        _gorunum = gorunum;
        if (_parcaAdi != null && _dosyaVar) viewport.ShowPart(_parcaAdi, _gorunum);
        Guncelle();
    }

    /// <summary>Keeps the native viewport from painting over <paramref name="overlay"/>.</summary>
    public void AddOverlay(FrameworkElement overlay) => viewport.AddOverlay(overlay);

    public void Shutdown()
    {
        viewport.StatusChanged -= Viewport_StatusChanged;
        viewport.Diagnostic -= Viewport_Diagnostic;
        viewport.Shutdown();
    }

    private void Viewport_StatusChanged(object? sender, OcctViewportStatusChangedEventArgs e) => Guncelle();

    private void Viewport_Diagnostic(object? sender, OcctViewportDiagnosticEventArgs e) => Diagnostic?.Invoke(this, e);

    private void Guncelle()
    {
        StepViewportLoadState durum = _port.LoadState;
        string mesaj = durum == StepViewportLoadState.Empty && _bosMesaj != null ? _bosMesaj : _port.StatusMessage;
        Step3BAracGorunumu g = Step3BAracDurumu.Hesapla(durum, _dosyaVar, _parcaAdi, _gorunum, mesaj);

        btnBuyukAc.IsEnabled = g.BuyukAcAcik;
        btnParcaGorunumu.Visibility = g.ParcaDugmesiGorunur ? Visibility.Visible : Visibility.Collapsed;
        btnParcaGorunumu.IsEnabled = g.ParcaDugmesiAcik;
        btnParcaGorunumu.Content = g.ParcaDugmesiMetni;
        foreach (Button button in new[] { btnIzometrik, btnOn, btnArka, btnSol, btnSag, btnUst, btnAlt, btnSigdir })
            button.IsEnabled = g.GorunumDugmeleriAcik;
        txtDurum.Text = g.DurumMetni;
    }

    private void BasligiGuncelle()
    {
        if (_yol == null)
        {
            txtBaslik.Text = "Listeden Bir STEP Seçin";
            txtYol.Text = string.Empty;
            txtYol.ToolTip = null;
            return;
        }
        string dosya = Path.GetFileName(_yol);
        txtBaslik.Text = _parcaAdi == null ? dosya : _parcaAdi + " — " + dosya;
        txtYol.Text = _yol;
        txtYol.ToolTip = _yol;
    }

    // Kompakt matches the embedded tab toolbars, Normal the "Büyük Aç" window.
    private void BoyutuUygula()
    {
        bool normal = _boyut == Step3BPaneliBoyutu.Normal;
        foreach (UIElement child in aracCubugu.Children)
        {
            if (child is not Button button) continue;
            button.Height = normal ? 30 : 25;
            button.Padding = normal ? new Thickness(10, 0, 10, 0)
                : button == btnBuyukAc ? new Thickness(9, 0, 9, 0) : new Thickness(7, 0, 7, 0);
            button.Margin = normal ? new Thickness(0, 0, 5, 5) : new Thickness(0, 0, 4, 4);
        }
    }

    private void btnBuyukAc_Click(object sender, RoutedEventArgs e) => BuyukAcIstendi?.Invoke(this, EventArgs.Empty);
    private void btnParcaGorunumu_Click(object sender, RoutedEventArgs e) => ParcaGorunumuDegistirIstendi?.Invoke(this, EventArgs.Empty);
    private void btnIzometrik_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Isometric);
    private void btnOn_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Front);
    private void btnArka_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Back);
    private void btnSol_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Left);
    private void btnSag_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Right);
    private void btnUst_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Top);
    private void btnAlt_Click(object sender, RoutedEventArgs e) => _adapter.SetView(OcctStandardView.Bottom);
    private void btnSigdir_Click(object sender, RoutedEventArgs e) => _adapter.FitAll();
}
