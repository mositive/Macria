using System;
using System.Windows;

namespace Macria;

/// <summary>
/// Tests and automated runs must not show windows to the user. With
/// MACRIA_OTOMASYON=1 (or Ac() from a test harness that loads Macria.dll) the
/// main window opens off-screen, out of the taskbar and without focus, and
/// questions that would wait for the user (unsaved .macria project on close)
/// are skipped.
/// </summary>
public static class OtomasyonModu
{
    public const string OrtamDegiskeni = "MACRIA_OTOMASYON";
    private static bool _zorla;

    public static bool Acik => _zorla || Environment.GetEnvironmentVariable(OrtamDegiskeni) == "1";

    /// <summary>For a harness that creates MainWindow itself.</summary>
    public static void Ac() => _zorla = true;

    /// <summary>MACRIA_OTOMASYON_KAPAN=&lt;seconds&gt;: the window closes itself (through the normal close path).</summary>
    public const string KapanmaDegiskeni = "MACRIA_OTOMASYON_KAPAN";

    /// <summary>
    /// A hidden window is not the process' main window, so a test cannot close
    /// it from outside; with MACRIA_OTOMASYON_KAPAN set it closes itself.
    /// </summary>
    public static void KapanisiPlanla(Window pencere)
    {
        if (!Acik || !int.TryParse(Environment.GetEnvironmentVariable(KapanmaDegiskeni), out int saniye) || saniye <= 0) return;
        pencere.Loaded += (_, _) =>
        {
            var zamanlayici = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(saniye) };
            zamanlayici.Tick += (_, _) =>
            {
                zamanlayici.Stop();
                pencere.Close();
            };
            zamanlayici.Start();
        };
    }

    /// <summary>Keeps a window off the screen, out of the taskbar and from taking focus.</summary>
    public static void Gizle(Window pencere)
    {
        if (!Acik) return;
        pencere.ShowInTaskbar = false;
        pencere.ShowActivated = false;
        pencere.WindowStartupLocation = WindowStartupLocation.Manual;
        pencere.WindowState = WindowState.Normal;
        pencere.Left = -32000;
        pencere.Top = -32000;
    }
}
