using System;
using System.ComponentModel;

namespace Macria
{
    public enum Renklendirme2UiRenkDurumu
    {
        Automatic,
        Assigned,
        Failed
    }

    // Yalnız güvenli, COM içermeyen tablo durumudur. CATIA hedefleri işlem anında çözülür.
    public sealed class Renklendirme2UiSatiri : INotifyPropertyChanged
    {
        private bool _included;
        private int? _paletteIndex;
        private byte? _red;
        private byte? _green;
        private byte? _blue;
        private Renklendirme2UiRenkDurumu _state;
        private string _error = "";

        public Renklendirme2UiSatiri(
            string referenceKey,
            string referenceTitle,
            int occurrenceCount)
        {
            ReferenceKey = referenceKey?.Trim() ?? "";
            ReferenceTitle = referenceTitle?.Trim() ?? "";
            OccurrenceCount = Math.Max(0, occurrenceCount);
            IsColorable = !string.IsNullOrWhiteSpace(ReferenceKey);
            _included = IsColorable;
            _state = Renklendirme2UiRenkDurumu.Automatic;
        }

        public string ReferenceKey { get; }
        public string ReferenceTitle { get; }
        public int OccurrenceCount { get; }
        public bool IsColorable { get; }

        public bool Included
        {
            get => _included;
            set
            {
                bool next = IsColorable && value;
                if (_included == next) return;
                _included = next;
                OnPropertyChanged(nameof(Included));
            }
        }

        public int? PaletteIndex => _paletteIndex;
        public byte? Red => _red;
        public byte? Green => _green;
        public byte? Blue => _blue;
        public string Hex => _red.HasValue && _green.HasValue && _blue.HasValue
            ? $"#{_red.Value:X2}{_green.Value:X2}{_blue.Value:X2}"
            : "";
        public Renklendirme2UiRenkDurumu State => _state;
        public bool IsAssigned => _state == Renklendirme2UiRenkDurumu.Assigned;
        public bool IsAutomatic => _state == Renklendirme2UiRenkDurumu.Automatic;
        public bool IsFailed => _state == Renklendirme2UiRenkDurumu.Failed;
        public string StatusText => _state switch
        {
            Renklendirme2UiRenkDurumu.Assigned => "",
            Renklendirme2UiRenkDurumu.Failed => "Hata",
            _ => "Automatic"
        };
        public string ColorToolTip => _state switch
        {
            Renklendirme2UiRenkDurumu.Assigned =>
                "HEX: " + Hex + "\nRGB: " + _red + ", " + _green + ", " + _blue,
            Renklendirme2UiRenkDurumu.Failed =>
                (string.IsNullOrWhiteSpace(_error) ? "Renk işlemi başarısız." : _error),
            _ => IsColorable
                ? "Automatic — PartBody üzerinde Macria renk ataması yok."
                : "Automatic — stabil PLM ReferenceKey okunamadığı için renklendirilemez."
        };

        internal void MarkAssigned(Renklendirme2RenkAtamasi assignment)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            if (!string.Equals(ReferenceKey, assignment.ReferenceKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Renk ataması farklı bir ReferenceKey için üretildi.");

            _paletteIndex = assignment.PaletteIndex;
            _red = assignment.Color.Red;
            _green = assignment.Color.Green;
            _blue = assignment.Color.Blue;
            _state = Renklendirme2UiRenkDurumu.Assigned;
            _error = "";
            NotifyColorState();
        }

        public void MarkAutomatic()
        {
            _paletteIndex = null;
            _red = null;
            _green = null;
            _blue = null;
            _state = Renklendirme2UiRenkDurumu.Automatic;
            _error = "";
            NotifyColorState();
        }

        public void MarkFailed(string error)
        {
            _state = Renklendirme2UiRenkDurumu.Failed;
            _error = error?.Trim() ?? "";
            NotifyColorState();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyColorState()
        {
            OnPropertyChanged(nameof(PaletteIndex));
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsAssigned));
            OnPropertyChanged(nameof(IsAutomatic));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ColorToolTip));
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
