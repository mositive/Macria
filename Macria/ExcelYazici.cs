using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

        public static void Yaz(Rapor rapor, string yol)
        {
            using (var akis = new FileStream(yol, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(akis, ZipArchiveMode.Create))
            {
                DosyaEkle(zip, "[Content_Types].xml", IcerikTurleri());
                DosyaEkle(zip, "_rels/.rels", KokIliskiler());
                DosyaEkle(zip, "xl/workbook.xml", CalismaKitabi());
                DosyaEkle(zip, "xl/_rels/workbook.xml.rels", KitapIliskileri());
                DosyaEkle(zip, "xl/styles.xml", Stiller());
                DosyaEkle(zip, "xl/worksheets/sheet1.xml", Sayfa(rapor));
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

            // Tablo basligi
            sb.Append("<row r=\"").Append(satir).Append("\">");
            for (int i = 0; i < rapor.Sutunlar.Count; i++)
                Metin(sb, i, satir, rapor.Sutunlar[i].Ad, StilSutun);
            sb.Append("</row>");
            satir++;

            // Veri satirlari
            foreach (object[] veri in rapor.Satirlar)
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

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void MetinSatiri(StringBuilder sb, int satir, string metin, int stil)
        {
            sb.Append("<row r=\"").Append(satir).Append("\">");
            Metin(sb, 0, satir, metin, stil);
            sb.Append("</row>");
        }

        private static void Hucre(StringBuilder sb, int sutun, int satir,
                                  object deger, RaporSutun tanim, bool toplam)
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

        private static void Metin(StringBuilder sb, int sutun, int satir, string metin, int stil)
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

        private static string Kacir(string s)
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

        private static string IcerikTurleri()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                   "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                   "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                   "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                   "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                   "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                   "</Types>";
        }

        private static string KokIliskiler()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private static string CalismaKitabi()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets><sheet name=\"Maliyet\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        }

        private static string KitapIliskileri()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                   "</Relationships>";
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
