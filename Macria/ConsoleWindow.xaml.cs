using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Macria
{
    // Konsolun genis gorunumu. Ana penceredeki kayit listesinin ta kendisini
    // gosterir; iki gorunum ayni koleksiyonu paylastigi icin canli kalir.
    public partial class ConsoleWindow : Window
    {
        private readonly ObservableCollection<LogEntry> _logs;

        public ConsoleWindow(ObservableCollection<LogEntry> logs)
        {
            InitializeComponent();
            WindowEffects.RoundCorners(this);

            _logs = logs;
            _logs.CollectionChanged += Logs_CollectionChanged;
            KonsolBelgesiniYenile();

            Loaded += (s, e) => logText.ScrollToEnd();
            Closed += (s, e) => _logs.CollectionChanged -= Logs_CollectionChanged;
        }

        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (object? item in e.NewItems)
                {
                    if (item is LogEntry entry)
                        KonsolSatiriEkle(entry);
                }
            }
            else
            {
                KonsolBelgesiniYenile();
            }

            txtLogEmpty.Visibility = _logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            logText.ScrollToEnd();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            _logs.Clear();
        }

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            string metin = TumLogMetni();
            if (string.IsNullOrWhiteSpace(metin))
            {
                MessageBox.Show(this, "Kopyalanacak konsol kaydı yok.", "Konsol",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(metin);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Konsol panoya kopyalanamadı: " + ex.Message, "Konsol",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnCopySelected_Click(object sender, RoutedEventArgs e)
        {
            RichTextBox? kaynak = null;
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu menu)
                kaynak = menu.PlacementTarget as RichTextBox;

            string metin = kaynak?.Selection.Text ?? "";
            if (string.IsNullOrWhiteSpace(metin))
            {
                MessageBox.Show(this, "Önce konsoldan bir metin seçin.", "Konsol",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(metin);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Seçili metin panoya kopyalanamadı: " + ex.Message, "Konsol",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string TumLogMetni()
        {
            var sonuc = new StringBuilder();
            foreach (LogEntry entry in _logs)
            {
                if (sonuc.Length > 0) sonuc.AppendLine();
                sonuc.Append(entry.Text);
            }

            return sonuc.ToString();
        }

        private void KonsolSatiriEkle(LogEntry entry)
        {
            var satir = new Paragraph(new Run(entry.Text) { Foreground = entry.Color })
            {
                Margin = new Thickness(0, 1, 0, 1)
            };
            logText.Document.Blocks.Add(satir);
        }

        private void KonsolBelgesiniYenile()
        {
            logText.Document.Blocks.Clear();
            foreach (LogEntry entry in _logs)
                KonsolSatiriEkle(entry);

            txtLogEmpty.Visibility = _logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

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
    }
}
