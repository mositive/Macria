using System.Collections.Generic;

namespace Macria
{
    /// <summary>
    /// CATIA occurrence agacinin WPF tarafina tasinan, COM nesnesi icermeyen
    /// gorunum modelidir. Hiyerarsik liste occurrence yapisini korur; duz parca
    /// listesi ise ayrica Reference Title'a gore tekillestirilir.
    /// </summary>
    public sealed class UrunAgaciNode
    {
        public string Title { get; set; } = "";
        public string ReferenceName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Revision { get; set; } = "";
        public string InstanceName { get; set; } = "";
        public string RepresentationTitle { get; set; } = "";
        public bool UrunGrubuMu { get; set; }
        public bool GizliMi { get; set; }
        public bool ParcaMi { get; set; }
        public List<UrunAgaciNode> Children { get; } = new List<UrunAgaciNode>();

        public string GosterimAdi
        {
            get
            {
                string ad = string.IsNullOrWhiteSpace(Title)
                    ? "(Title girilmemiş)"
                    : Title.Trim();

                if (!string.IsNullOrWhiteSpace(Revision))
                    ad += "  ·  Rev. " + Revision.Trim();

                return ad;
            }
        }

        public string TurMetni
        {
            get
            {
                if (UrunGrubuMu) return "Ürün grubu";
                if (ParcaMi) return "Parça";
                return "Ürün";
            }
        }

        public string NotMetni
        {
            get { return GizliMi ? "Gizlenmiş öğe" : ""; }
        }

        public string AciklamaMetni
        {
            get
            {
                string metin = TurMetni + ": " + GosterimAdi;
                if (!string.IsNullOrWhiteSpace(Description))
                    metin += "\nTanım: " + Description.Trim();
                if (!string.IsNullOrWhiteSpace(ReferenceName))
                    metin += "\nName: " + ReferenceName.Trim();
                if (!string.IsNullOrWhiteSpace(RepresentationTitle))
                    metin += "\n3B temsil: " + RepresentationTitle.Trim();
                if (GizliMi) metin += "\nGizlenmiş öğe";
                return metin;
            }
        }
    }
}
