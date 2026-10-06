using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Macria
{
    // Raporu gercek bir .xlsx dosyasi olarak yazar.
    //
    // xlsx, icinde birkac XML dosyasi bulunan bir zip arsividir; disaridan
    // kutuphane eklemeden elle uretiliyor. Metinler paylasilan dizin yerine
    // hucre icinde (inlineStr) tutulur, sayilar gercek sayi olarak yazilir;
    // boylece Excel'de toplama/siralama calisir.
    internal static class ExcelYazici
    {
        // Hucre bicimleri (styles.xml icindeki cellXfs sirasi)
        private const int StilNormal = 0;
        private const int StilKalin = 1;
        private const int StilBaslik = 2;   // rapor basligi (14 punto kalin)
        private const int StilSutun = 3;    // tablo sutun basligi
        private const int StilSayi2 = 4;    // #,##0.00
        private const int StilSayi3 = 5;    // #,##0.000
        private const int StilTamsayi = 6;  // #,##0
        private const int StilToplamSayi = 7;
        private const int StilToplamMetin = 8;

        public static void Yaz(Rapor rapor, string yol) => Yaz(new[] { rapor }, yol);

        /// <summary>One workbook, one sheet per report, in order.</summary>
        public static void Yaz(IReadOnlyList<Rapor> sayfalar, string yol)
        {
            if (sayfalar.Count == 0) throw new ArgumentException("En az bir sayfa gerekir.", nameof(sayfalar));
            using (var akis = new FileStream(yol, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(akis, ZipArchiveMode.Create))
            {
                DosyaEkle(zip, "[Content_Types].xml", IcerikTurleri(sayfalar.Count));
                DosyaEkle(zip, "_rels/.rels", KokIliskiler());
                DosyaEkle(zip, "xl/workbook.xml", CalismaKitabi(sayfalar.Select(x => x.SayfaAdi).ToList()));
                DosyaEkle(zip, "xl/_rels/workbook.xml.rels", KitapIliskileri(sayfalar.Count));
                DosyaEkle(zip, "xl/styles.xml", Stiller());
                for (int i = 0; i < sayfalar.Count; ++i)
                    DosyaEkle(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", Sayfa(sayfalar[i]));
            }
        }

        private static void DosyaEkle(ZipArchive zip, string ad, string icerik)
        {
            ZipArchiveEntry giris = zip.CreateEntry(ad, CompressionLevel.Optimal);

            using (Stream s = giris.Open())
            using (var yazici = new StreamWriter(s, new UTF8Encoding(false)))
                yazici.Write(icerik);
        }

        // ================= SAYFA =================

        private static string Sayfa(Rapor rapor)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            if (rapor.IlkSatiriDondur)
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");

            // Sutun genislikleri
            sb.Append("<cols>");
            for (int i = 0; i < rapor.Sutunlar.Count; i++)
            {
                double genislik = Math.Max(9, rapor.Sutunlar[i].Genislik * 11);
                sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1)
                  .Append("\" width=\"").Append(genislik.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");

            sb.Append("<sheetData>");

            int satir = 1;
            if (!rapor.TabloIlkSatirdanBaslar)
            {
                // Baslik blogu
                MetinSatiri(sb, satir++, rapor.Baslik, StilBaslik);

                if (rapor.AltBaslik.Length > 0)
                    MetinSatiri(sb, satir++, rapor.AltBaslik, StilNormal);

                MetinSatiri(sb, satir++,
                    "Rapor Tarihi: " + rapor.Tarih.ToString("dd.MM.yyyy HH:mm",
                        CultureInfo.CurrentCulture), StilNormal);

                foreach (string bilgi in rapor.Bilgiler)
                    MetinSatiri(sb, satir++, bilgi, StilNormal);

                foreach (RaporOzet ozet in rapor.Ozetler)
                    MetinSatiri(sb, satir++, ozet.Baslik + ": " + ozet.Deger, StilKalin);

                satir++;   // bos satir
            }

            // Tablo basligi
            int tabloBaslikSatiri = satir;
            sb.Append("<row r=\"").Append(satir).Append("\">");
            for (int i = 0; i < rapor.Sutunlar.Count; i++)
                Metin(sb, i, satir, rapor.Sutunlar[i].Ad, StilSutun);
            sb.Append("</row>");
            satir++;

            // Veri satirlari
            foreach (object?[] veri in rapor.Satirlar)
            {
                sb.Append("<row r=\"").Append(satir).Append("\">");

                for (int i = 0; i < rapor.Sutunlar.Count && i < veri.Length; i++)
                    Hucre(sb, i, satir, veri[i], rapor.Sutunlar[i], false);

                sb.Append("</row>");
                satir++;
            }

            // Toplam satiri
            if (rapor.Toplam != null)
            {
                sb.Append("<row r=\"").Append(satir).Append("\">");

                for (int i = 0; i < rapor.Sutunlar.Count && i < rapor.Toplam.Length; i++)
                    Hucre(sb, i, satir, rapor.Toplam[i], rapor.Sutunlar[i], true);

                sb.Append("</row>");
            }

            sb.Append("</sheetData>");
            if (rapor.OtomatikFiltre && rapor.Sutunlar.Count > 0)
                sb.Append("<autoFilter ref=\"A").Append(tabloBaslikSatiri).Append(":")
                  .Append(Ad(rapor.Sutunlar.Count - 1, Math.Max(tabloBaslikSatiri, satir - 1)))
                  .Append("\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void MetinSatiri(StringBuilder sb, int satir, string metin, int stil)
        {
            sb.Append("<row r=\"").Append(satir).Append("\">");
            Metin(sb, 0, satir, metin, stil);
            sb.Append("</row>");
        }

        private static void Hucre(StringBuilder sb, int sutun, int satir,
                                  object? deger, RaporSutun tanim, bool toplam)
        {
            if (deger == null) return;

            if (deger is double)
            {
                int stil = toplam
                    ? StilToplamSayi
                    : (tanim.Ondalik == 0 ? StilTamsayi
                       : tanim.Ondalik >= 3 ? StilSayi3 : StilSayi2);

                sb.Append("<c r=\"").Append(Ad(sutun, satir)).Append("\" s=\"").Append(stil).Append("\"><v>")
                  .Append(((double)deger).ToString("R", CultureInfo.InvariantCulture))
                  .Append("</v></c>");
                return;
            }

            Metin(sb, sutun, satir, Convert.ToString(deger, CultureInfo.CurrentCulture),
                  toplam ? StilToplamMetin : StilNormal);
        }

        private static void Metin(StringBuilder sb, int sutun, int satir, string? metin, int stil)
        {
            sb.Append("<c r=\"").Append(Ad(sutun, satir)).Append("\" s=\"").Append(stil)
              .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(Kacir(metin))
              .Append("</t></is></c>");
        }

        // 0 -> A1, 26 -> AA1
        private static string Ad(int sutun, int satir)
        {
            string harf = "";
            int n = sutun;

            do
            {
                harf = (char)('A' + n % 26) + harf;
                n = n / 26 - 1;
            } while (n >= 0);

            return harf + satir;
        }

        private static string Kacir(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);

            foreach (char c in s)
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c < 0x20 && c != '\t' && c != '\n') continue;   // XML'de gecersiz
                else sb.Append(c);
            }

            return sb.ToString();
        }

        // ================= SABIT PARCALAR =================

        private static string IcerikTurleri(int sayfaSayisi)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                      "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                      "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                      "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                      "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int i = 1; i <= sayfaSayisi; ++i)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                      "</Types>");
            return sb.ToString();
        }

        private static string KokIliskiler()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private static string CalismaKitabi(IReadOnlyList<string> sayfaAdlari)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                      "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                      "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            // Sheet names are unique in a workbook (case ignored).
            var kullanilan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sayfaAdlari.Count; ++i)
            {
                string kok = ExcelSayfaAdi(sayfaAdlari[i]);
                string ad = kok;
                for (int sira = 2; !kullanilan.Add(ad); ++sira)
                {
                    string ek = " (" + sira + ")";
                    ad = (kok.Length + ek.Length > 31 ? kok.Substring(0, 31 - ek.Length) : kok) + ek;
                }
                sb.Append("<sheet name=\"" + Kacir(ad) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            }
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string ExcelSayfaAdi(string sayfaAdi)
        {
            string ad = string.IsNullOrWhiteSpace(sayfaAdi) ? "Rapor" : sayfaAdi.Trim();
            foreach (char gecersiz in new[] { '[', ']', ':', '*', '?', '/', '\\' })
                ad = ad.Replace(gecersiz, ' ');

            ad = ad.Trim().Trim('\'');
            if (ad.Length == 0) ad = "Rapor";
            if (ad.Length > 31) ad = ad.Substring(0, 31);
            return ad;
        }

        private static string KitapIliskileri(int sayfaSayisi)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                      "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sayfaSayisi; ++i)
                sb.Append("<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>");
            sb.Append("<Relationship Id=\"rId" + (sayfaSayisi + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                      "</Relationships>");
            return sb.ToString();
        }

        private static string Stiller()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +

                   "<numFmts count=\"1\">" +
                   "<numFmt numFmtId=\"164\" formatCode=\"#,##0.000\"/>" +
                   "</numFmts>" +

                   "<fonts count=\"3\">" +
                   "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
                   "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
                   "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/></font>" +
                   "</fonts>" +

                   "<fills count=\"3\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\">" +
                   "<fgColor rgb=\"FFEDEDED\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +

                   "<borders count=\"3\">" +
                   "<border><left/><right/><top/><bottom/><diagonal/></border>" +
                   "<border><left/><right/><top/><bottom style=\"thin\">" +
                   "<color rgb=\"FF9E9E9E\"/></bottom><diagonal/></border>" +
                   "<border><left/><right/><top style=\"thin\">" +
                   "<color rgb=\"FF9E9E9E\"/></top><bottom/><diagonal/></border>" +
                   "</borders>" +

                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +

                   "<cellXfs count=\"9\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"0\" borderId=\"2\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"2\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>" +
                   "</cellXfs>" +

                   "</styleSheet>";
        }
    }
}
