"""Prototip: sac parçada havşa / imbus / düz delik tanıma (OCCT, B-Rep).

Kullanım: python delik_tanima.py parca.stp
"""
import sys, math, json
from collections import defaultdict
from OCP.STEPControl import STEPControl_Reader
from OCP.TopExp import TopExp_Explorer, TopExp
from OCP.TopAbs import TopAbs_FACE, TopAbs_EDGE, TopAbs_REVERSED
from OCP.TopoDS import TopoDS
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Plane, GeomAbs_Cylinder, GeomAbs_Cone
from OCP.GProp import GProp_GProps
from OCP.BRepGProp import BRepGProp
from OCP.BRepTools import BRepTools

TOL = 1e-3


def oku(yol):
    r = STEPControl_Reader()
    if r.ReadFile(yol) != 1:
        raise RuntimeError("STEP okunamadı")
    r.TransferRoots()
    return r.OneShape()


def v(p):
    return (p.X(), p.Y(), p.Z())


def alan(face):
    g = GProp_GProps()
    BRepGProp.SurfaceProperties_s(face, g)
    return g.Mass()


def yuzler(shape):
    out = []
    ex = TopExp_Explorer(shape, TopAbs_FACE)
    i = 0
    while ex.More():
        f = TopoDS.Face(ex.Current())
        s = BRepAdaptor_Surface(f)
        t = s.GetType()
        d = {"id": i, "face": f, "alan": alan(f)}
        if t == GeomAbs_Plane:
            pl = s.Plane()
            n = pl.Axis().Direction()
            if f.Orientation() == TopAbs_REVERSED:
                n = n.Reversed()
            d.update(tip="Düzlem", normal=v(n), nokta=v(pl.Location()))
        elif t == GeomAbs_Cylinder:
            c = s.Cylinder()
            d.update(tip="Silindir", eksen=v(c.Axis().Direction()),
                     merkez=v(c.Axis().Location()), r=c.Radius())
        elif t == GeomAbs_Cone:
            c = s.Cone()
            d.update(tip="Koni", eksen=v(c.Axis().Direction()),
                     merkez=v(c.Axis().Location()), r=c.RefRadius(),
                     yariAci=math.degrees(c.SemiAngle()))
        else:
            d.update(tip="Diğer")
        # UV sınırlarından yüzeyin eksen boyunca kapsadığı nokta aralığı
        umin, umax, vmin, vmax = BRepTools.UVBounds_s(f)
        pts = [s.Value(u, vv) for u in (umin, (umin + umax) / 2, umax) for vv in (vmin, vmax)]
        d["pts"] = [v(p) for p in pts]
        out.append(d)
        ex.Next()
        i += 1
    return out


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def paralel(a, b):
    return abs(abs(dot(a, b)) - 1) < 1e-6


def eksen_ayni(a, b):
    """İki dönel yüz aynı ekseni paylaşıyor mu?"""
    if not paralel(a["eksen"], b["eksen"]):
        return False
    d = sub(b["merkez"], a["merkez"])
    e = a["eksen"]
    dik = sub(d, tuple(dot(d, e) * x for x in e))
    return math.sqrt(dot(dik, dik)) < 1e-3


def sac_kalinligi(fs):
    """En büyük düzlem yüzü ile ona paralel ve zıt normalli en yakın yüzün mesafesi."""
    dz = sorted([f for f in fs if f["tip"] == "Düzlem"], key=lambda f: -f["alan"])
    for a in dz:
        for b in dz:
            if a is b or dot(a["normal"], b["normal"]) > -0.999:
                continue
            t = abs(dot(sub(b["nokta"], a["nokta"]), a["normal"]))
            if 0.3 < t < 30:
                return t
    return None


def delikleri_bul(fs, t):
    donel = [f for f in fs if f["tip"] in ("Silindir", "Koni")]
    gruplar = []
    for f in donel:
        for g in gruplar:
            if eksen_ayni(g[0], f):
                g.append(f)
                break
        else:
            gruplar.append([f])

    duzlemler = [f for f in fs if f["tip"] == "Düzlem"]
    sonuc = []
    for g in gruplar:
        e = g[0]["eksen"]
        o = g[0]["merkez"]
        # büküm silindirlerini ele: yarıçapları farkı ~t olan, deliğe benzemeyen büyük dönel yüzler
        # (sac yüzeyine dik eksenli olmayanlar). Delik ekseni sac yüzlerinden birine paralel normal olmalı.
        sac_yuzleri = [p for p in duzlemler if paralel(p["normal"], e)]
        if len(sac_yuzleri) < 2:
            continue
        # deliğin iki ucundaki sac yüzleri (eksen boyunca konum)
        konumlar = sorted({round(dot(sub(p["nokta"], o), e), 4) for p in sac_yuzleri})
        # her dönel yüzün eksen boyunca kapsadığı aralık
        parca = []
        for f in g:
            h = [dot(sub(p, o), e) for p in f["pts"]]
            yar = [math.sqrt(max(dot(sub(p, o), sub(p, o)) - dot(sub(p, o), e) ** 2, 0)) for p in f["pts"]]
            parca.append({"tip": f["tip"], "hmin": min(h), "hmax": max(h),
                          "rmin": min(yar), "rmax": max(yar), "yariAci": f.get("yariAci")})
        hmin = min(p["hmin"] for p in parca)
        hmax = max(p["hmax"] for p in parca)
        derinlik = hmax - hmin
        if t and abs(derinlik - t) > 0.05:
            continue  # sacı boydan boya geçmiyor -> büküm silindiri vb.
        r_gecis = min(p["rmin"] for p in parca)
        r_kafa = max(p["rmax"] for p in parca)
        koni = [p for p in parca if p["tip"] == "Koni"]
        sil = [p for p in parca if p["tip"] == "Silindir"]
        if koni:
            tip = "Havşa (Countersink)"
        elif len({round(p["rmin"], 3) for p in sil}) > 1:
            tip = "İmbus yuvası (Counterbore)"
        else:
            tip = "Düz delik"
        # açılış tarafı: kafa çapının bulunduğu uç
        kafa_parca = max(parca, key=lambda p: p["rmax"])
        uc = kafa_parca["hmax"] if abs(kafa_parca["hmax"] - hmax) < 1e-3 else kafa_parca["hmin"]
        acilis_normal = tuple(x if uc == hmax else -x for x in e)
        acilis_nokta = tuple(o[i] + uc * e[i] for i in range(3))
        sonuc.append({
            "tip": tip,
            "gecisCapi": round(2 * r_gecis, 3),
            "kafaCapi": round(2 * r_kafa, 3),
            "havsaAcisi": round(2 * koni[0]["yariAci"], 1) if koni else None,
            "eksen": [round(x, 4) for x in e],
            "acildigiYuzNoktasi": [round(x, 3) for x in acilis_nokta],
            "acildigiYuzNormali": [round(x, 4) for x in acilis_normal],
        })
    return sonuc


def bukumler(fs, t):
    sil = [f for f in fs if f["tip"] == "Silindir"]
    out = []
    for a in sil:
        for b in sil:
            if a is b or a["r"] >= b["r"] or not eksen_ayni(a, b):
                continue
            if t and abs((b["r"] - a["r"]) - t) < 0.05:
                out.append({"icYaricap": round(a["r"], 3), "disYaricap": round(b["r"], 3),
                            "eksen": [round(x, 4) for x in a["eksen"]],
                            "merkez": [round(x, 3) for x in a["merkez"]]})
    return out


if __name__ == "__main__":
    shape = oku(sys.argv[1])
    fs = yuzler(shape)
    t = sac_kalinligi(fs)
    print(json.dumps({
        "yuzSayisi": len(fs),
        "yuzTipleri": {k: sum(1 for f in fs if f["tip"] == k) for k in {f["tip"] for f in fs}},
        "sacKalinligi": round(t, 3) if t else None,
        "bukumler": bukumler(fs, t),
        "delikler": delikleri_bul(fs, t),
    }, ensure_ascii=False, indent=2))
