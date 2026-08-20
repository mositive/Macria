using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Macria
{
    public class SheetRow
    {
        public string ProductName { get; set; } = "";
        public string PartName { get; set; } = "";
        public double Thickness { get; set; }
        public int Quantity { get; set; }
    }

    public class LogEntry
    {
        public string Text { get; set; } = "";
        public Brush Color { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<SheetRow> _rows = new ObservableCollection<SheetRow>();
        private readonly Dictionary<string, object> _repRefs = new Dictionary<string, object>();
        private object _catia;
        private System.ComponentModel.ICollectionView _view;
        private string _searchText = "";
        private readonly ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();
        private ExportPipWindow _pip;
        private bool _stopRequested;

        private const int TAB_FIRST = 15;   // ilk parca
        private const int TAB_REST = 16;    // sonraki parcalar

        public MainWindow()
        {
            InitializeComponent();
            _view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterRow;
            grid.ItemsSource = _view;

            logList.ItemsSource = _logs;
            LogInfo("Macria Hazır.");
        }

        // ================= KONSOL =================

        private void LogInfo(string message) { AddLog(message, "LogInfoBrush"); }
        private void LogSuccess(string message) { AddLog(message, "LogSuccessBrush"); }
        private void LogError(string message) { AddLog(message, "LogErrorBrush"); }

        private void AddLog(string message, string brushKey)
        {
            var entry = new LogEntry
            {
                Text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message,
                Color = (Brush)FindResource(brushKey)
            };
            _logs.Add(entry);
            logScroll.ScrollToEnd();

            // Pip acikken son konsol satirini orada da goster
            if (_pip != null)
                _pip.SetLastLog(entry.Text, entry.Color);
        }

        // ================= ARAMA =================

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = (txtSearch.Text ?? "").Trim();
            _view.Refresh();
        }

        private bool FilterRow(object item)
        {
            if (_searchText.Length == 0) return true;

            SheetRow row = item as SheetRow;
            if (row == null) return false;

            return ContainsText(row.ProductName, _searchText) ||
                   ContainsText(row.PartName, _searchText);
        }

        private static bool ContainsText(string source, string query)
        {
            return source != null &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ================= PENCERE KONTROLLERI =================

        private void btnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnMax_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ================= EXPORT PIP GOSTERGESI =================

        private int _pipSession;

        private ExportPipWindow EnsurePip()
        {
            if (_pip == null)
            {
                _pip = new ExportPipWindow();
                _pip.StopRequested += OnPipStopRequested;
                _pip.Show();
            }
            return _pip;
        }

        private void ShowPipStart(string detail)
        {
            _pipSession++;
            EnsurePip().SetState(ExportPipWindow.PipState.Starting, detail);
        }

        private void ShowPip(string detail)
        {
            EnsurePip().SetState(ExportPipWindow.PipState.Running, detail);
        }

        // Sonuc durumunu bir sure gosterip pip'i kapatir
        private async System.Threading.Tasks.Task FinishPip(ExportPipWindow.PipState state, string detail)
        {
            if (_pip == null) return;

            _pip.SetState(state, detail);

            int session = _pipSession;
            await System.Threading.Tasks.Task.Delay(2500);
            if (_pipSession == session) HidePip();
        }

        private void OnPipStopRequested()
        {
            _stopRequested = true;
            if (_pip != null) _pip.SetDetail("Durduruluyor...");
            LogError("Acil Durdurma İstendi — İşlem Kesiliyor...");
        }

        private void HidePip()
        {
            if (_pip != null)
            {
                _pip.Close();
                _pip = null;
            }
        }

        // Export sonrasi dosyayi/klasoru varsayilan uygulamayla acar
        private static void OpenExported(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        // ================= TARAMA =================

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            _rows.Clear();
            _repRefs.Clear();

            SetScanning(true);
            LogInfo("CATIA Taraması Başlatıldı.");

            try
            {
                ScanOutput result = await System.Threading.Tasks.Task.Run(() => DoScan());

                // Baglanti adimlari ve teshis satirlari konsola dokulur
                foreach (var d in result.Diag) WriteDiag(d);

                if (result.Error != null)
                {
                    LogError(result.Error);
                    return;
                }

                // COM baglantisini UI thread'inden tekrar al (apartman uyumu icin)
                _catia = GetCatia() ?? result.Catia;

                foreach (var row in result.Rows) _rows.Add(row);
                foreach (var kv in result.RepRefs) _repRefs[kv.Key] = kv.Value;

                LogSuccess("Tarama Tamamlandı — Sac Parça Çeşidi: " + _rows.Count +
                           ", Toplam Adet: " + result.Total);
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
            }
            finally
            {
                SetScanning(false);
            }
        }

        private class ScanOutput
        {
            public List<SheetRow> Rows = new List<SheetRow>();
            public Dictionary<string, object> RepRefs = new Dictionary<string, object>();
            public object Catia;
            public int Total;
            public string Error;
            public List<DiagLine> Diag = new List<DiagLine>();
        }

        private void WriteDiag(DiagLine d)
        {
            if (d.Level == DiagLevel.Error) LogError(d.Text);
            else if (d.Level == DiagLevel.Success) LogSuccess(d.Text);
            else LogInfo(d.Text);
        }

        private ScanOutput DoScan()
        {
            var result = new ScanOutput();

            object catiaObj = CatiaConnect.Connect(result.Diag);
            if (catiaObj == null)
            {
                result.Diag.AddRange(CatiaConnect.Teshis());
                result.Error = "CATIA Bağlantısı Kurulamadı. Nedeni İçin Yukarıdaki Teşhis Satırlarına Bakın.";
                return result;
            }

            result.Catia = catiaObj;
            dynamic catia = catiaObj;
            dynamic editor = catia.ActiveEditor;
            dynamic root = editor.ActiveObject;

            if (!HasOccurrences(root))
            {
                result.Error = "Montaj Bulunamadı. Bir Physical Product Açın.";
                return result;
            }

            var found = new Dictionary<string, ScanItem>();
            ScanNode(root, "", found);

            foreach (var kv in found)
            {
                double thk = GetThickness(kv.Value.Part);
                if (thk <= 0) continue;

                result.Rows.Add(new SheetRow
                {
                    ProductName = kv.Value.ProductName,
                    PartName = kv.Value.PartName,
                    Thickness = Math.Round(thk, 2),
                    Quantity = kv.Value.Count
                });

                result.RepRefs[kv.Value.PartName] = kv.Value.RepRef;
                result.Total += kv.Value.Count;
            }

            return result;
        }

        private bool _exporting;

        // Export surerken tarama ve yeni export baslatilamaz
        private void SetExporting(bool active)
        {
            _exporting = active;
            btnScan.IsEnabled = !active;
            btnExportAll.IsEnabled = !active;
        }

        // Tarama sirasinda butondaki donen gostergeyi acip kapatir
        private void SetScanning(bool active)
        {
            btnScan.IsHitTestVisible = !active;
            btnExportAll.IsEnabled = !active;

            scanIcon.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            scanSpinner.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            scanText.Text = active ? "Taranıyor..." : "CATIA'yı Tara";

            if (active)
            {
                var spin = new System.Windows.Media.Animation.DoubleAnimation(0, 360,
                    TimeSpan.FromSeconds(0.9));
                spin.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
                scanSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
            }
            else
            {
                scanSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        // ================= CATIA BAGLANTISI =================

        // Baglanti mantigi CatiaConnect icinde; burasi sadece sessiz bir sarmalayici
        private static object GetCatia()
        {
            try { return CatiaConnect.Connect(null); }
            catch { return null; }
        }

        private static bool HasOccurrences(dynamic node)
        {
            try
            {
                dynamic subs = node.Occurrences;
                if (subs == null) return false;
                int c = subs.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= AGAC TARAMA =================

        private class ScanItem
        {
            public object Part;
            public object RepRef;
            public string ProductName = "";
            public string PartName = "";
            public int Count;
        }

        private void ScanNode(dynamic node, string parentProd,
                              Dictionary<string, ScanItem> found)
        {
            string prodName = parentProd;
            try
            {
                string n = node.Name;
                if (!string.IsNullOrWhiteSpace(n))
                    prodName = StripInstance(n.Trim());
            }
            catch { }

            // Alt occurrence sayisi: 0 ise bu dugum bir parcadir (yaprak)
            int altSayisi = 0;
            try
            {
                dynamic altlar = node.Occurrences;
                if (altlar != null) altSayisi = altlar.Count;
            }
            catch { }

            // Yaprak dugumde "urun adi" ust dugumden gelir; dugumun kendi adi
            // parcanin instance adidir, urun adi degildir
            string repProd = (altSayisi == 0 && parentProd.Length > 0) ? parentProd : prodName;

            try
            {
                dynamic reps = node.RepOccurrences;
                if (reps != null)
                {
                    int cnt = reps.Count;
                    for (int i = 1; i <= cnt; i++)
                    {
                        string key = "";
                        object part = null;
                        object repRefObj = null;

                        try
                        {
                            dynamic repOcc = reps.Item(i);
                            dynamic repInst = repOcc.RelatedRepInstance;
                            dynamic repRef = repInst.ReferenceInstanceOf;
                            repRefObj = repRef;

                            // Parca adi olarak Title kullanilir; bos/yoksa Name'e dusulur
                            string nm = "";
                            try { nm = repRef.Title; } catch { }
                            if (string.IsNullOrWhiteSpace(nm))
                            {
                                try { nm = repRef.Name; } catch { }
                            }

                            key = (nm ?? "").Trim();
                            part = repRef.GetItem("Part");
                        }
                        catch { }

                        if (key.Length == 0) continue;

                        // Ayni parca farkli urunler altinda ayri satir olsun diye
                        // urun+parca ciftiyle grupla
                        string mapKey = repProd + "||" + key;

                        ScanItem item;
                        if (found.TryGetValue(mapKey, out item))
                        {
                            item.Count++;
                        }
                        else
                        {
                            found[mapKey] = new ScanItem
                            {
                                Part = part,
                                RepRef = repRefObj,
                                ProductName = repProd,
                                PartName = key,
                                Count = 1
                            };
                        }
                    }
                }
            }
            catch { }

            try
            {
                dynamic subs = node.Occurrences;
                if (subs != null)
                {
                    int cnt = subs.Count;
                    for (int i = 1; i <= cnt; i++)
                        ScanNode(subs.Item(i), prodName, found);
                }
            }
            catch { }
        }

        private static string StripInstance(string s)
        {
            int p = s.LastIndexOf('.');
            if (p > 0)
            {
                int dummy;
                if (int.TryParse(s.Substring(p + 1), out dummy))
                    return s.Substring(0, p);
            }
            return s;
        }

        // ================= KALINLIK =================

        private static readonly string[] NeutralAnchors =
        {
            "BendTable", "ReliefRadialLength", "ReliefAxialLength",
            "BeadStd", "ExtrudedHoleStd", "SurfaceStampStd", "CurveStampStd",
            "BridgeStd", "StiffeningRibStd", "CircularStampStd",
            "FlangedCutoutStd", "LouverStd", "DowelStd", "CircularCutoutStd",
            "DINNormaFormula"
        };

        private static readonly string[] ThicknessNames =
        {
            "thickness", "kalinlik", "epaisseur", "dicke", "spessore",
            "espesor", "espessura", "dikte", "tjocklek", "tykkelse"
        };

        private static double GetThickness(object partObj)
        {
            if (partObj == null) return 0;

            dynamic part = partObj;
            dynamic prms = null;
            try { prms = part.Parameters; } catch { }
            if (prms == null) return 0;

            int count = 0;
            try { count = prms.Count; } catch { return 0; }

            string prefix = "";
            for (int i = 1; i <= count; i++)
            {
                string name = "";
                try { name = prms.Item(i).Name; } catch { }
                if (string.IsNullOrEmpty(name)) continue;

                if (IsNeutralAnchor(LastSegment(name)))
                {
                    prefix = ParentPath(name);
                    break;
                }
                if (IsNeutralAnchor(LastSegment(ParentPath(name))))
                {
                    prefix = ParentPath(ParentPath(name));
                    break;
                }
            }
            if (prefix.Length == 0) return 0;

            double first = 0;
            for (int i = 1; i <= count; i++)
            {
                string name = "";
                try { name = prms.Item(i).Name; } catch { }
                if (string.IsNullOrEmpty(name)) continue;
                if (!IsUnder(name, prefix)) continue;

                double val = 0;
                try { val = Convert.ToDouble(prms.Item(i).Value); } catch { }

                if (first == 0 && val >= 0.05 && val <= 100) first = val;

                if (IsThicknessName(LastSegment(name)) && val >= 0.05 && val <= 100)
                    return val;
            }

            return first;
        }

        private static bool IsNeutralAnchor(string leaf)
        {
            foreach (string a in NeutralAnchors)
                if (string.Equals(leaf, a, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsThicknessName(string leaf)
        {
            string f = Fold(leaf);
            if (f.Contains("thick")) return true;
            foreach (string t in ThicknessNames)
                if (f == t) return true;
            return false;
        }

        private static string Fold(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                char ch = c;
                if (ch == 'Ç' || ch == 'ç') ch = 'c';
                else if (ch == 'Ğ' || ch == 'ğ') ch = 'g';
                else if (ch == 'İ' || ch == 'ı') ch = 'i';
                else if (ch == 'Ö' || ch == 'ö') ch = 'o';
                else if (ch == 'Ş' || ch == 'ş') ch = 's';
                else if (ch == 'Ü' || ch == 'ü') ch = 'u';

                if (ch < 128 && char.IsLetter(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private static string LastSegment(string path)
        {
            int p = path.LastIndexOf('\\');
            if (p < 0) return path;
            return path.Substring(p + 1);
        }

        private static string ParentPath(string path)
        {
            int p = path.LastIndexOf('\\');
            if (p <= 0) return "";
            return path.Substring(0, p);
        }

        private static bool IsUnder(string path, string prefix)
        {
            if (prefix.Length == 0) return false;
            return path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // ================= WINDOWS API =================

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Windows'un on plan kilidini asarak pencereyi gercekten one getirir
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private static bool ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            if (GetForegroundWindow() == hWnd) return true;

            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint me = GetCurrentThreadId();

            AttachThreadInput(me, fg, true);

            // SADECE simge durumundaysa geri yukle, aksi halde boyuta dokunma
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9);

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            AttachThreadInput(me, fg, false);

            System.Threading.Thread.Sleep(200);
            return GetForegroundWindow() == hWnd;
        }

        // Pano kilitliyse birkac kez dener
        private static bool SafeSetClipboard(string text)
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }
            return false;
        }

        // Windows kaydetme penceresi kapanana kadar bekler
        private async System.Threading.Tasks.Task<bool> WaitForNoSaveDialog(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                IntPtr h = IntPtr.Zero;
                EnumWindows((w, l) =>
                {
                    if (!IsWindowVisible(w)) return true;
                    if (GetCls(w) != "#32770") return true;
                    if (GetDlgItem(w, 1) != IntPtr.Zero) { h = w; return false; }
                    return true;
                }, IntPtr.Zero);

                if (h == IntPtr.Zero) return true;

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        // Montaj penceresine donuldu mu (Part kapandi mi)
        private async System.Threading.Tasks.Task<bool> WaitForAssembly(int timeoutMs)
        {
            dynamic catia = _catia;
            int waited = 0;
            while (waited < timeoutMs)
            {
                try
                {
                    dynamic obj = catia.ActiveEditor.ActiveObject;
                    if (HasOccurrences(obj)) return true;
                }
                catch { }

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint WM_SETTEXT = 0x000C;
        private const uint BM_CLICK = 0x00F5;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static string GetText(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetCls(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static void SelectAll()
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);              // Ctrl down
            keybd_event(0x41, 0, 0, UIntPtr.Zero);              // A down
            keybd_event(0x41, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            System.Threading.Thread.Sleep(150);
        }

        // ================= DXF EXPORT =================

        // Sag tiklanan satiri secili yapar; boylece menu her zaman dogru parcayla calisir
        private void grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridRow))
            {
                if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
                    dep = VisualTreeHelper.GetParent(dep);
                else if (dep is FrameworkContentElement fce)
                    dep = fce.Parent;
                else
                    break;
            }

            if (dep is DataGridRow row)
                row.IsSelected = true;
        }

        private async void mnuExportDxf_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;

            if (grid.SelectedItem == null)
            {
                LogInfo("Önce Listeden Bir Parça Seçin.");
                return;
            }

            SheetRow row = (SheetRow)grid.SelectedItem;

            if (!_repRefs.ContainsKey(row.PartName) || _repRefs[row.PartName] == null)
            {
                LogError("Parça Referansı Bulunamadı: " + row.PartName);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "DXF|*.dxf";
            dlg.FileName = MakeFileName(row);
            if (dlg.ShowDialog() != true) return;

            try
            {
                LogInfo("DXF Export Başladı: " + row.PartName);
                _stopRequested = false;
                ShowPipStart(row.PartName);

                // Islem devam ettigi surece fiziksel girdi kilitli (Macria pencereleri haric)
                SetExporting(true);
                InputGuard.Enable();
                LogInfo("Girdi Kilidi Açık — İşlem Bitene Kadar Tıklamalar Engellenecek.");

                bool ok;
                try
                {
                    ok = await ExportOne(_repRefs[row.PartName], dlg.FileName);
                }
                finally
                {
                    InputGuard.Disable();
                    SetExporting(false);
                    LogInfo("Girdi Kilidi Kapatıldı.");
                }

                if (_stopRequested)
                {
                    LogError("Export Durduruldu: " + row.PartName);
                    await FinishPip(ExportPipWindow.PipState.Stopped, row.PartName);
                }
                else if (ok)
                {
                    if (chkOpenAfter.IsChecked == true)
                        OpenExported(dlg.FileName);
                    await FinishPip(ExportPipWindow.PipState.Done, row.PartName);
                }
                else
                {
                    await FinishPip(ExportPipWindow.PipState.Error, row.PartName);
                }
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
                await FinishPip(ExportPipWindow.PipState.Error, ex.Message);
            }
        }

        private static string MakeFileName(SheetRow row)
        {
            string thk = row.Thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return row.ProductName + "_" + thk + "mm_" + row.Quantity + "adet.dxf";
        }

        private bool _ilkKayitYapildi = false;


        private async System.Threading.Tasks.Task<bool> ExportOne(object repRef, string fullPath)
        {
            if (_stopRequested) return false;

            dynamic catia = _catia;

        
            try { if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath); } catch { }

            // 1) parcayi yeni pencerede ac
            LogInfo("Parça Açılıyor...");
            dynamic svc = catia.ActiveEditor.GetService("PLMOpenService");
            object newEd = null;
            svc.PLMOpenInNewWindow(repRef, ref newEd);

            await System.Threading.Tasks.Task.Delay(2500);

            // Parca acildi: pip "Basladi" durumundan "Suruyor" durumuna gecsin
            if (_pip != null)
                _pip.SetStateKeepDetail(ExportPipWindow.PipState.Running);

            IntPtr hCatia = FindWindow(null, "3DEXPERIENCE");
            IntPtr hSave = IntPtr.Zero;

            // 2-4) komut + Save As butonunu bul ve bas
            bool dogrudanKullan = true; // once butonu pencere agacindan bulup BM_CLICK dene

            for (int deneme = 1; deneme <= 3 && hSave == IntPtr.Zero; deneme++)
            {
                if (_stopRequested) return false;

                LogInfo("DXF Komutu (Deneme " + deneme + ")...");

                ForceForeground(hCatia);
                await System.Threading.Tasks.Task.Delay(600);

                catia.StartCommand("Save As DXF");
                await System.Threading.Tasks.Task.Delay(3000);

                bool saveAsBasildi = false;

                // Yontem 1: "Save As" yazili butonu pencere agacinda bul,
                // odaktan tamamen bagimsiz olarak dogrudan tikla
                if (dogrudanKullan)
                {
                    IntPtr hBtn = FindSaveAsButton();
                    if (hBtn != IntPtr.Zero)
                    {
                        LogInfo("Save As Butonu Bulundu — Doğrudan Tıklanıyor.");
                        SendMessage(hBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        saveAsBasildi = true;
                    }
                    else
                    {
                        // Win32'de yok: UIA agacinda isimle ara
                        LogInfo("Win32'de Bulunamadı — UIA Ağacında Aranıyor...");
                        bool uiaTik = await System.Threading.Tasks.Task.Run(
                            () => TryClickSaveAsUia(hCatia));

                        if (uiaTik)
                        {
                            LogInfo("Save As Butonu UIA ile Bulundu — Tıklandı.");
                            saveAsBasildi = true;
                        }
                        else
                        {
                            LogInfo("Save As Butonu Bulunamadı — Tab Yöntemine Geçiliyor.");
                            dogrudanKullan = false;

                            // Ilk basarisizlikta panel agacini teshis icin dosyaya dok
                            if (!_dumpYazildi)
                            {
                                _dumpYazildi = true;
                                try
                                {
                                    string dumpYolu = await System.Threading.Tasks.Task.Run(
                                        () => DumpPanelTree(hCatia));
                                    LogInfo("Teşhis Dökümü Yazıldı: " + dumpYolu);
                                }
                                catch (Exception dex)
                                {
                                    LogError("Teşhis Dökümü Yazılamadı: " + dex.Message);
                                }
                            }
                        }
                    }
                }

                // Yontem 2 (yedek): sabit tab sayisi
                if (!saveAsBasildi && !dogrudanKullan)
                {
                    int tabSayisi = _ilkKayitYapildi ? TAB_REST : TAB_FIRST;
                    LogInfo("DXF Komutu (Tab=" + tabSayisi + ")...");

                    for (int t = 0; t < tabSayisi; t++)
                    {
                        PressTab();
                        await System.Threading.Tasks.Task.Delay(60);
                    }

                    await System.Threading.Tasks.Task.Delay(300);

                    // Basmadan once odaktaki elemanin adina bak: ad okunabiliyor
                    // ve acikca Iptal/Kapat ise basma, denemeyi yenile
                    string odak = GetFocusedName();
                    if (odak.Length > 0)
                        LogInfo("Odaktaki Öğe: \"" + odak + "\"");

                    if (IsCancelName(odak))
                    {
                        LogError("Odak İptal/Kapat Butonunda — Basılmadı, Yeniden Deneniyor.");
                    }
                    else
                    {
                        PressSpace();
                        saveAsBasildi = true;
                    }
                }

                if (saveAsBasildi)
                    hSave = await WaitForSaveDialog(6000);

                if (hSave == IntPtr.Zero)
                {
                    // Dogrudan tiklama ise yaramadiysa sonraki denemede tab'a don
                    dogrudanKullan = false;
                    PressEscape();
                    await System.Threading.Tasks.Task.Delay(1500);
                }
            }



            // Acil durdurma: acik kalan kaydetme penceresini kapatip cik
            if (_stopRequested)
            {
                if (hSave != IntPtr.Zero)
                {
                    ForceForeground(hSave);
                    PressEscape();
                }
                return false;
            }

            // 5) tam yolu yaz ve kaydet
            ForceForeground(hSave);
            await System.Threading.Tasks.Task.Delay(600);

            SelectAll();
            SendText(fullPath);
            await System.Threading.Tasks.Task.Delay(400);
            PressEnter();

            // 6) dosya olusana kadar bekle
            LogInfo("Dosya Bekleniyor...");
            bool ok = await WaitForFile(fullPath, 20000);
            if (ok) _ilkKayitYapildi = true;

            // 7) pencereler kapansin, sonra parcayi kapat
            await WaitForNoSaveDialog(10000);
            await System.Threading.Tasks.Task.Delay(1000);

            try { catia.ActiveWindow.Close(); } catch { }

            bool geriDondu = await WaitForAssembly(15000);
            if (!geriDondu)
            {
                LogInfo("Montaja Dönülemedi, Bekleniyor...");
                await System.Threading.Tasks.Task.Delay(3000);
            }
            await System.Threading.Tasks.Task.Delay(1500);

            if (ok) LogSuccess("Yazıldı: " + fullPath);
            else LogError("Dosya Oluşmadı: " + fullPath);
            return ok;
        }

        // ================= SAVE AS BULMA (UIA) =================

        // Odaktaki UI elemaninin adini UI Automation ile okur
        private static string GetFocusedName()
        {
            try
            {
                var el = System.Windows.Automation.AutomationElement.FocusedElement;
                if (el == null) return "";
                return (el.Current.Name ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        private const uint MOUSEEVENTF_LEFTUP = 0x04;

        // Sentetik sol tik (injected oldugu icin girdi kilidinden gecer)
        private static void ClickAt(int x, int y)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(60);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        // UIA agacinda adi "Save As" olan ogeyi arar; Invoke edemezse ortasina tiklar
        private static bool TryClickSaveAsUia(IntPtr hCatia)
        {
            var kokler = new List<IntPtr>();
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero) kokler.Add(fg);
            if (hCatia != IntPtr.Zero && !kokler.Contains(hCatia)) kokler.Add(hCatia);

            var adKosulu = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, "save as",
                    PropertyConditionFlags.IgnoreCase),
                new PropertyCondition(AutomationElement.NameProperty, "save as...",
                    PropertyConditionFlags.IgnoreCase),
                new PropertyCondition(AutomationElement.NameProperty, "farklı kaydet",
                    PropertyConditionFlags.IgnoreCase));

            foreach (IntPtr kok in kokler)
            {
                try
                {
                    var root = AutomationElement.FromHandle(kok);
                    var bulunan = root.FindAll(TreeScope.Descendants, adKosulu);

                    foreach (AutomationElement el in bulunan)
                    {
                        object pat;
                        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                        {
                            ((InvokePattern)pat).Invoke();
                            return true;
                        }

                        var r = el.Current.BoundingRectangle;
                        if (!r.IsEmpty && r.Width > 0)
                        {
                            ClickAt((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        // Panel acikken pencere/UIA agacini dosyaya doker (teshis icin, bir kez)
        private static string DumpPanelTree(IntPtr hCatia)
        {
            var sb = new StringBuilder();
            uint catiaPid = 0;
            if (hCatia != IntPtr.Zero) GetWindowThreadProcessId(hCatia, out catiaPid);

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (catiaPid != 0 && pid != catiaPid) return true;

                sb.AppendLine("==== PENCERE: \"" + GetText(h) + "\"  class=" + GetCls(h));

                int n = 0;
                EnumChildWindows(h, (ch, l2) =>
                {
                    n++;
                    if (n > 300) return false;
                    sb.AppendLine("  [win32] class=" + GetCls(ch) +
                                  "  text=\"" + GetText(ch) + "\"");
                    return true;
                }, IntPtr.Zero);

                try
                {
                    var root = AutomationElement.FromHandle(h);
                    var butonlar = root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    int m = 0;
                    foreach (AutomationElement b in butonlar)
                    {
                        m++;
                        if (m > 250) break;
                        string ad = "";
                        try { ad = b.Current.Name; } catch { }
                        sb.AppendLine("  [uia-buton] \"" + ad + "\"");
                    }
                }
                catch { }

                sb.AppendLine();
                return true;
            }, IntPtr.Zero);

            string yol = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "macria_panel_dump_" + DateTime.Now.ToString("HHmmss") + ".txt");
            System.IO.File.WriteAllText(yol, sb.ToString());
            return yol;
        }

        private bool _dumpYazildi;

        // "&Save As" gibi hizlandirici isaretlerini temizler
        private static string CleanButtonText(string s)
        {
            return (s ?? "").Replace("&", "").Trim();
        }

        private static bool IsSaveAsText(string text)
        {
            string f = CleanButtonText(text).ToLowerInvariant();
            return f == "save as" || f == "save as..." || f == "saveas" ||
                   f == "farklı kaydet" || f == "farkli kaydet";
        }

        // Gorunur tum pencerelerin cocuklarinda "Save As" yazili butonu arar.
        // Once class'i Button olan tercih edilir; bulunamazsa ayni yazili
        // herhangi bir kontrol dondurulur.
        private static IntPtr FindSaveAsButton()
        {
            IntPtr adayButon = IntPtr.Zero;
            IntPtr adayDiger = IntPtr.Zero;

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;

                EnumChildWindows(h, (ch, l2) =>
                {
                    if (!IsWindowVisible(ch)) return true;
                    if (!IsSaveAsText(GetText(ch))) return true;

                    if (GetCls(ch).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        adayButon = ch;
                        return false;
                    }

                    if (adayDiger == IntPtr.Zero) adayDiger = ch;
                    return true;
                }, IntPtr.Zero);

                return adayButon == IntPtr.Zero;
            }, IntPtr.Zero);

            return adayButon != IntPtr.Zero ? adayButon : adayDiger;
        }

        // Acikca Iptal/Kapat benzeri bir buton adi mi?
        private static bool IsCancelName(string name)
        {
            if (name.Length == 0) return false;
            string f = name.ToLowerInvariant();
            return f.Contains("cancel") || f.Contains("iptal") ||
                   f.Contains("vazgeç") || f.Contains("vazgec") ||
                   f == "close" || f == "kapat" || f == "no" || f == "hayır" || f == "hayir";
        }

        private static void PressTab()
        {
            keybd_event(0x09, 0, 0, UIntPtr.Zero);
            keybd_event(0x09, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void PressSpace()
        {
            keybd_event(0x20, 0, 0, UIntPtr.Zero);
            keybd_event(0x20, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void PressEscape()
        {
            keybd_event(0x1B, 0, 0, UIntPtr.Zero);
            keybd_event(0x1B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Windows kaydetme dialogunda dosya adi alanini bulur
        private static IntPtr FindFileNameEdit(IntPtr hDlg)
        {
            // once dogrudan
            IntPtr h = GetDlgItem(hDlg, 1001);
            if (h != IntPtr.Zero && GetCls(h) == "Edit") return h;

            // sonra ComboBox icindeki Edit
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(hDlg, (ch, l) =>
            {
                if (GetCls(ch) == "ComboBox")
                {
                    EnumChildWindows(ch, (cch, l2) =>
                    {
                        if (GetCls(cch) == "Edit")
                        {
                            found = cch;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                    if (found != IntPtr.Zero) return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero) return found;

            // son care: id'si 1001 olan herhangi bir Edit
            EnumChildWindows(hDlg, (ch, l) =>
            {
                if (GetCls(ch) == "Edit" && GetDlgCtrlID(ch) == 1001)
                {
                    found = ch;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private async void btnExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;

            if (_rows.Count == 0)
            {
                LogInfo("Önce Tarama Yapın.");
                return;
            }

            // klasoru bir kez sor
            var fd = new Microsoft.Win32.OpenFolderDialog();
            fd.Title = "Çıktı Klasörünü Seçin";
            if (fd.ShowDialog() != true) return;

            string folder = fd.FolderName;
            LogInfo("Toplu DXF Export Başladı: " + _rows.Count + " Parça");
            _stopRequested = false;
            ShowPipStart("Hazırlanıyor...");

            // Islem devam ettigi surece fiziksel girdi kilitli (Macria pencereleri haric)
            SetExporting(true);
            InputGuard.Enable();
            LogInfo("Girdi Kilidi Açık — İşlem Bitene Kadar Tıklamalar Engellenecek.");

            int ok = 0;
            var failed = new List<string>();

            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_stopRequested)
                    {
                        LogError("Toplu Export Durduruldu (" + i + "/" + _rows.Count + " Tamamlandı).");
                        break;
                    }

                    SheetRow row = _rows[i];

                    if (!_repRefs.ContainsKey(row.PartName) || _repRefs[row.PartName] == null)
                    {
                        failed.Add(row.PartName);
                        continue;
                    }

                    string path = System.IO.Path.Combine(folder, MakeFileName(row));

                    LogInfo("(" + (i + 1) + "/" + _rows.Count + ") " + row.PartName);
                    ShowPip("(" + (i + 1) + "/" + _rows.Count + ") " + row.PartName);

                    try
                    {
                        bool done = await ExportOne(_repRefs[row.PartName], path);
                        if (done) ok++;
                        else if (!_stopRequested) failed.Add(row.PartName);
                    }
                    catch
                    {
                        failed.Add(row.PartName);
                    }

                    await System.Threading.Tasks.Task.Delay(800);
                }
            }
            catch (Exception ex)
            {
                LogError("Hata: " + ex.Message);
            }
            finally
            {
                InputGuard.Disable();
                SetExporting(false);
                LogInfo("Girdi Kilidi Kapatıldı.");
            }

            if (ok > 0)
                LogSuccess("Toplu Export Bitti — Başarılı: " + ok + " / " + _rows.Count);
            else
                LogError("Toplu Export Bitti — Başarılı: 0 / " + _rows.Count);
            if (failed.Count > 0)
                LogError("Başarısız: " + string.Join(", ", failed));

            string pipOzet = "Başarılı: " + ok + " / " + _rows.Count;
            if (_stopRequested)
                await FinishPip(ExportPipWindow.PipState.Stopped, pipOzet);
            else if (ok > 0)
                await FinishPip(ExportPipWindow.PipState.Done, pipOzet);
            else
                await FinishPip(ExportPipWindow.PipState.Error, pipOzet);

            // Toplu exportta tek tek dosya yerine cikti klasorunu ac
            if (ok > 0 && chkOpenAfter.IsChecked == true)
                OpenExported(folder);
        }

        private async System.Threading.Tasks.Task<IntPtr> WaitForSaveDialog(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                IntPtr found = IntPtr.Zero;

                EnumWindows((h, l) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    if (GetCls(h) != "#32770") return true;

                    // Kaydet butonu (id=1) yeterli sart
                    if (GetDlgItem(h, 1) != IntPtr.Zero)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (found != IntPtr.Zero) return found;

                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return IntPtr.Zero;
        }

        private static async System.Threading.Tasks.Task<bool> WaitForFile(string path, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                try { if (System.IO.File.Exists(path)) return true; } catch { }
                await System.Threading.Tasks.Task.Delay(300);
                waited += 300;
            }
            return false;
        }

        private static bool SendText(string text)
        {
            if (!SafeSetClipboard(text)) return false;

            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            System.Threading.Thread.Sleep(200);
            return true;
        }
        private static void PressEnter()
        {
            keybd_event(0x0D, 0, 0, UIntPtr.Zero);
            keybd_event(0x0D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

    }
}
