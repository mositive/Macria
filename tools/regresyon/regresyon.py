"""Engine regression: run every STEP of the regression set, normalise the JSON, compare two runs.

  python regresyon.py calistir <engine.exe> <cikti-klasoru>
  python regresyon.py karsilastir <eski-klasor> <yeni-klasor>
"""
import hashlib, json, os, subprocess, sys, time

# The regression set: Masaüstü\Macria-Regresyon\stepler (README there). MACRIA_REGRESYON overrides.
KOK = os.environ.get("MACRIA_REGRESYON") or os.path.join(
    os.path.expanduser("~"), "OneDrive", "Desktop", "Macria-Regresyon", "stepler")
DEGISKEN = {"analysisId", "startedAt", "completedAt", "inputFilePath", "analysisSeconds"}


def stepler():
    for dizin, _, dosyalar in os.walk(KOK):
        for ad in sorted(dosyalar):
            if ad.lower().endswith((".stp", ".step")):
                yield os.path.join(dizin, ad)


def anahtar(yol):
    return os.path.relpath(yol, KOK).replace("\\", "__").replace(" ", "_")


def temizle(deger):
    if isinstance(deger, dict):
        return {k: temizle(v) for k, v in deger.items() if k not in DEGISKEN}
    if isinstance(deger, list):
        return [temizle(v) for v in deger]
    return deger


def calistir(exe, cikti):
    os.makedirs(cikti, exist_ok=True)
    toplam = time.time()
    for step in stepler():
        ad = anahtar(step)
        json_yol = os.path.join(cikti, ad + ".json")
        dxf = os.path.join(cikti, ad + ".dxf")
        os.makedirs(dxf, exist_ok=True)
        bas = time.time()
        p = subprocess.run([exe, "--input", step, "--output", json_yol, "--dxf-klasor", dxf],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        sure = time.time() - bas
        ozet = {"cikis": p.returncode, "sure": round(sure, 1)}
        if os.path.exists(json_yol):
            veri = json.load(open(json_yol, encoding="utf-8"))
            dxfler = {}
            for f in sorted(os.listdir(dxf)):
                dxfler[f] = hashlib.sha256(open(os.path.join(dxf, f), "rb").read()).hexdigest()[:16]
            json.dump({"json": temizle(veri), "dxf": dxfler, "ozet": ozet},
                      open(os.path.join(cikti, ad + ".norm.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{sure:6.1f}s cikis={p.returncode} {os.path.relpath(step, KOK)}", flush=True)
    print(f"toplam {time.time() - toplam:.0f}s")


def parcalar(veri):
    sonuc = {}
    for p in veri["json"].get("parts", []):
        sonuc[p.get("localId")] = p
    return sonuc


def farklar(a, b, yol=""):
    if type(a) != type(b):
        yield yol, a, b
    elif isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                yield yol + "." + k, a.get(k, "<yok>"), b.get(k, "<yok>")
            else:
                yield from farklar(a[k], b[k], yol + "." + k)
    elif isinstance(a, list):
        if len(a) != len(b):
            yield yol + f"[len {len(a)}->{len(b)}]", None, None
        for i, (x, y) in enumerate(zip(a, b)):
            yield from farklar(x, y, f"{yol}[{i}]")
    elif a != b:
        yield yol, a, b


def yoksay(deger, anahtarlar):
    if isinstance(deger, dict):
        return {k: yoksay(v, anahtarlar) for k, v in deger.items() if k not in anahtarlar}
    if isinstance(deger, list):
        return [yoksay(v, anahtarlar) for v in deger]
    return deger


def karsilastir(eski, yeni, anahtarlar=()):
    toplam_fark = 0
    sinif_degisen = []
    kodlar = {}
    for ad in sorted(f for f in os.listdir(eski) if f.endswith(".norm.json")):
        a = json.load(open(os.path.join(eski, ad), encoding="utf-8"))
        yb = os.path.join(yeni, ad)
        if not os.path.exists(yb):
            print("YENIDE YOK:", ad)
            toplam_fark += 1
            continue
        b = json.load(open(yb, encoding="utf-8"))
        for p in b["json"].get("parts", []):
            if "classificationCode" in p:
                k = (p.get("classification"), p.get("classificationCode"), p.get("recognitionEvidence"))
                kodlar[k] = kodlar.get(k, 0) + 1
        if anahtarlar:
            a = dict(a, json=yoksay(a["json"], anahtarlar))
            b = dict(b, json=yoksay(b["json"], anahtarlar))
        liste = list(farklar(a["json"], b["json"]))
        dxf_fark = a["dxf"] != b["dxf"]
        pa, pb = parcalar(a), parcalar(b)
        for lid in sorted(set(pa) | set(pb), key=lambda x: (x is None, x)):
            x, y = pa.get(lid, {}), pb.get(lid, {})
            if x.get("classification") != y.get("classification"):
                sinif_degisen.append((ad.replace(".norm.json", ""), lid, x.get("name"), x.get("quantity"),
                                      x.get("classification"), y.get("classification"),
                                      x.get("classificationReasons"), y.get("classificationReasons")))
        if liste or dxf_fark:
            toplam_fark += 1
            print(f"== {ad}: {len(liste)} JSON farki" + (", DXF farki" if dxf_fark else ""))
            for yol, x, y in liste[:12]:
                print(f"   {yol}: {str(x)[:110]} -> {str(y)[:110]}")
            if dxf_fark:
                for f in sorted(set(a["dxf"]) | set(b["dxf"])):
                    if a["dxf"].get(f) != b["dxf"].get(f):
                        print(f"   dxf {f}: {a['dxf'].get(f)} -> {b['dxf'].get(f)}")
    if kodlar:
        print("\nsinif / kod / kanit dagilimi (yeni):")
        for k, v in sorted(kodlar.items(), key=lambda x: (str(x[0][0]), -x[1])):
            print(f"   {v:4} {k[0]:<15} {str(k[1]):<24} kanit={k[2]}")
    print(f"\nfarkli dosya: {toplam_fark}")
    print(f"sinifi degisen parca: {len(sinif_degisen)}")
    for d in sinif_degisen:
        print(f"   {d[0]} #{d[1]} {d[2]} (adet {d[3]}): {d[4]} -> {d[5]}")
        print(f"      once: {d[6]}")
        print(f"      sonra: {d[7]}")


if __name__ == "__main__":
    if sys.argv[1] == "calistir":
        calistir(sys.argv[2], sys.argv[3])
    else:
        karsilastir(sys.argv[2], sys.argv[3], tuple(sys.argv[4].split(",")) if len(sys.argv) > 4 else ())
