using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Macria
{
    // Bir COM nesnesinin tip adini ve uye listesini IDispatch tip bilgisinden okur.
    // CATIA/3DEXPERIENCE nesnelerinde hangi ozelligin basligi tasidigini tahmin
    // etmek yerine dogrudan gormek icin kullanilir.
    internal static class ComProbe
    {
        [ComImport]
        [Guid("00020400-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            void GetTypeInfoCount(out int pctinfo);
            void GetTypeInfo(int iTInfo, int lcid, out ITypeInfo ppTInfo);
            void GetIDsOfNames(ref Guid riid, IntPtr rgszNames, int cNames, int lcid, IntPtr rgDispId);
            void Invoke(int dispIdMember, ref Guid riid, int lcid, short wFlags,
                        IntPtr pDispParams, IntPtr pVarResult, IntPtr pExcepInfo, IntPtr puArgErr);
        }

        private static ITypeInfo TipBilgisi(object nesne)
        {
            var disp = nesne as IDispatch;
            if (disp == null) return null;

            int adet;
            disp.GetTypeInfoCount(out adet);
            if (adet == 0) return null;

            ITypeInfo ti;
            disp.GetTypeInfo(0, 0, out ti);
            return ti;
        }

        // COM sinifinin/arayuzunun adi, orn. "VPMRepReference"
        public static string TipAdi(object nesne)
        {
            if (nesne == null) return "(null)";

            try
            {
                ITypeInfo ti = TipBilgisi(nesne);
                if (ti == null) return "(tip bilgisi yok)";

                string ad, dok, yardim;
                int ctx;
                ti.GetDocumentation(-1, out ad, out dok, out ctx, out yardim);
                return string.IsNullOrEmpty(ad) ? "(adsiz)" : ad;
            }
            catch (Exception ex) { return "(okunamadi: " + ex.Message + ")"; }
        }

        // Nesnenin tum ozellik ve metot adlari (alfabetik, tekrarsiz)
        public static List<string> UyeAdlari(object nesne)
        {
            var adlar = new List<string>();
            if (nesne == null) return adlar;

            ITypeInfo ti = null;
            try { ti = TipBilgisi(nesne); }
            catch { }
            if (ti == null) return adlar;

            IntPtr attrPtr = IntPtr.Zero;
            try
            {
                ti.GetTypeAttr(out attrPtr);
                var attr = Marshal.PtrToStructure<TYPEATTR>(attrPtr);

                var kume = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < attr.cFuncs; i++)
                {
                    IntPtr fPtr = IntPtr.Zero;
                    try
                    {
                        ti.GetFuncDesc(i, out fPtr);
                        var fd = Marshal.PtrToStructure<FUNCDESC>(fPtr);

                        string ad, dok, yardim;
                        int ctx;
                        ti.GetDocumentation(fd.memid, out ad, out dok, out ctx, out yardim);

                        if (!string.IsNullOrEmpty(ad)) kume.Add(ad);
                    }
                    catch { }
                    finally { if (fPtr != IntPtr.Zero) ti.ReleaseFuncDesc(fPtr); }
                }

                for (int i = 0; i < attr.cVars; i++)
                {
                    IntPtr vPtr = IntPtr.Zero;
                    try
                    {
                        ti.GetVarDesc(i, out vPtr);
                        var vd = Marshal.PtrToStructure<VARDESC>(vPtr);

                        string ad, dok, yardim;
                        int ctx;
                        ti.GetDocumentation(vd.memid, out ad, out dok, out ctx, out yardim);

                        if (!string.IsNullOrEmpty(ad)) kume.Add(ad);
                    }
                    catch { }
                    finally { if (vPtr != IntPtr.Zero) ti.ReleaseVarDesc(vPtr); }
                }

                adlar.AddRange(kume);
                adlar.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            finally { if (attrPtr != IntPtr.Zero) ti.ReleaseTypeAttr(attrPtr); }

            return adlar;
        }

        // Ad icinde baslik/ad gecen uyeleri one cikarir
        public static List<string> IlginçUyeler(List<string> adlar)
        {
            string[] anahtarlar = { "name", "title", "attr", "desc", "value", "ident", "label", "ref", "instance" };
            var sonuc = new List<string>();

            foreach (string ad in adlar)
            {
                foreach (string a in anahtarlar)
                {
                    if (ad.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sonuc.Add(ad);
                        break;
                    }
                }
            }

            return sonuc;
        }
    }
}
