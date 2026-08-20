using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

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
            logList.ItemsSource = _logs;
            _logs.CollectionChanged += Logs_CollectionChanged;

            Loaded += (s, e) => logScroll.ScrollToEnd();
            Closed += (s, e) => _logs.CollectionChanged -= Logs_CollectionChanged;
        }

        private void Logs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                logScroll.ScrollToEnd();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            _logs.Clear();
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
