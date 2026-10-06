"""R2: selected-part analysis (--parcalar) against the full analysis.

  python parca-esdeger.py hepsi <engine.exe> <tam-klasor> <cikti>
      every STEP with parts, --parcalar <all ids>: JSON and DXFs must equal the
      full run (only analysisMode / selectedPartIds differ).
  python parca-esdeger.py tekli <engine.exe> <tam-klasor> <cikti>
      montaj-1 every part, WGRV004423 20 parts of every class, one at a time:
      the part's class, reasons, sheet and profile results and DXF must equal
      the full run (solid / face ids differ by design).
"""
import hashlib, json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(__file__))
from regresyon import KOK, anahtar, stepler, temizle, farklar

PARCA_ALANLARI = ["name", "productId", "productName", "quantity", "classification", "classificationCode",
                  "recognitionEvidence", "classificationReasons", "sheetCandidate", "profileCandidate",
                  "machiningPresent", "machiningKinds", "dxfFile", "dxfCutOnlyFile", "geometryValid",
                  "validityIssues", "analysisTimedOut"]
SAC_ALANLARI = ["status", "rejectionReason", "thicknessMm", "closedSection", "machiningPresent", "machiningKinds"]
ACINIM_ALANLARI = ["status", "widthMm", "heightMm", "minimumRectangle", "rejectionReason"]
PROFIL_ALANLARI = ["status", "rejectionReason", "profileType", "outerWidthMm", "outerHeightMm", "innerWidthMm",
                   "innerHeightMm", "outerDiameterMm", "innerDiameterMm", "wallThicknessMm", "formattedDesignation"]


def calistir(exe, step, ids, json_yol, dxf):
    os.makedirs(dxf, exist_ok=True)
    bas = time.time()
    p = subprocess.run([exe, "--input", step, "--output", json_yol, "--dxf-klasor", dxf, "--parcalar",
                        ",".join(str(i) for i in ids), "--parca-sure-siniri", "0"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, time.time() - bas


def dxf_ozet(klasor):
    return {f: hashlib.sha256(open(os.path.join(klasor, f), "rb").read()).hexdigest()[:16]
            for f in sorted(os.listdir(klasor))} if os.path.isdir(klasor) else {}


def lid(x):
    return x.get("localId") if isinstance(x, dict) else x


def yuzsuz(deger):
    """Face ids (F123) are numbered per analysis: a selected-part run numbers them anew."""
    if isinstance(deger, str):
        return re.sub(r"\bF[0-9]+\b", "F#", deger)
    if isinstance(deger, list):
        return [yuzsuz(v) for v in deger]
    if isinstance(deger, dict):
        return {k: yuzsuz(v) for k, v in deger.items()}
    return deger


def parca_ozeti(veri, parca, dxfler):
    solidler = {lid(s) for s in parca["solidIds"]}
    ozet = {k: parca.get(k) for k in PARCA_ALANLARI}
    ozet["sac"] = [dict({k: s.get(k) for k in SAC_ALANLARI}, flat={k: s["flatPattern"].get(k) for k in ACINIM_ALANLARI},
                        bukum=len(s.get("bends", [])))
                   for s in veri.get("sheetMetalAnalyses", []) if lid(s["solidId"]) in solidler]
    ozet["profil"] = [{k: r.get(k) for k in PROFIL_ALANLARI}
                      for r in veri.get("profileRecognitions", []) if lid(r.get("solidId")) in solidler]
    ozet["dxf"] = {f: dxfler.get(f) for f in (parca.get("dxfFile"), parca.get("dxfCutOnlyFile")) if f}
    return yuzsuz(ozet)


def hepsi(exe, tam, cikti):
    fark = 0
    for step in stepler():
        ad = anahtar(step)
        tam_json = os.path.join(tam, ad + ".json")
        if not os.path.exists(tam_json):
            continue
        veri = json.load(open(tam_json, encoding="utf-8"))
        ids = [p["localId"] for p in veri.get("parts", [])]
        if not ids:
            continue
        j, d = os.path.join(cikti, ad + ".json"), os.path.join(cikti, ad + ".dxf")
        cikis, sure = calistir(exe, step, ids, j, d)
        yeni = json.load(open(j, encoding="utf-8")) if os.path.exists(j) else {}
        a = temizle(veri)
        b = temizle({k: v for k, v in yeni.items() if k not in ("analysisMode", "selectedPartIds")})
        a = {k: v for k, v in a.items() if k not in ("analysisMode", "selectedPartIds")}
        liste = list(farklar(a, b))
        dxf_esit = dxf_ozet(os.path.join(tam, ad + ".dxf")) == dxf_ozet(d)
        tamam = cikis == 0 and not liste and dxf_esit and yeni.get("analysisMode") == "Parts"
        fark += 0 if tamam else 1
        print(f"{'ayni ' if tamam else 'FARK '} {sure:6.1f}s {len(ids):3} parca {os.path.relpath(step, KOK)}", flush=True)
        for yol, x, y in liste[:8]:
            print(f"      {yol}: {str(x)[:90]} -> {str(y)[:90]}")
    print(f"\nfarkli dosya: {fark}")


def tekli(exe, tam, cikti):
    secim = []
    for yol, sayi in ((os.path.join(KOK, "B-Rep Calisma A", "B-Rep Calisma A.stp"), None),
                      (os.path.join(KOK, "WGRV004423 A", "WGRV004423 A.stp"), 20)):
        veri = json.load(open(os.path.join(tam, anahtar(yol) + ".json"), encoding="utf-8"))
        parcalar = [p for p in veri["parts"] if p.get("geometryValid", True) and not p.get("analysisTimedOut")]
        if sayi:
            # Every class / code first, then the rest in order, up to `sayi`.
            gruplar = {}
            for p in parcalar:
                gruplar.setdefault((p["classification"], p.get("classificationCode")), []).append(p)
            secilen = []
            while len(secilen) < sayi and any(gruplar.values()):
                for g in list(gruplar.values()):
                    if g and len(secilen) < sayi:
                        secilen.append(g.pop(0))
            parcalar = secilen
        secim += [(yol, veri, p) for p in parcalar]
    fark = 0
    for yol, veri, parca in secim:
        ad = f"{anahtar(yol)}.p{parca['localId']}"
        j, d = os.path.join(cikti, ad + ".json"), os.path.join(cikti, ad + ".dxf")
        cikis, sure = calistir(exe, yol, [parca["localId"]], j, d)
        yeni = json.load(open(j, encoding="utf-8")) if os.path.exists(j) else {}
        yp = (yeni.get("parts") or [None])[0]
        once = parca_ozeti(veri, parca, dxf_ozet(os.path.join(tam, anahtar(yol) + ".dxf")))
        sonra = parca_ozeti(yeni, yp, dxf_ozet(d)) if yp else None
        liste = list(farklar(once, sonra)) if sonra else [("parca yok", cikis, None)]
        fark += 1 if liste else 0
        print(f"{'ayni ' if not liste else 'FARK '} {sure:6.1f}s #{parca['localId']:<4} {parca['classification']:<14} "
              f"{str(parca.get('classificationCode')):<20} {parca['name']}", flush=True)
        for yol_, x, y in liste[:8]:
            print(f"      {yol_}: {str(x)[:90]} -> {str(y)[:90]}")
    print(f"\nfarkli parca: {fark} / {len(secim)}")


if __name__ == "__main__":
    {"hepsi": hepsi, "tekli": tekli}[sys.argv[1]](sys.argv[2], sys.argv[3], sys.argv[4])
