# GeometryLabAdapter.Tests — sabit fikstürler

Testler kullanıcının çalışma dosyalarını (Masaüstü `Macria-4A-Gercek-Test` gibi) okumaz. Gereken dosyaların sabit kopyası buradadır. Bu dosyalar değiştirilmez; yeni bir durum gerekiyorsa yeni dosya eklenir.

| Klasör | İçerik | Kullanan test | Ortam değişkeni (verilirse onu kullanır) |
|---|---|---|---|
| `projeler/WGRV004423 A.macria` | WGRV projesi, şema 1.3, 2 "Dene" kararı (2026-10-05 kaydı) | Eski / gerçek proje açılışı | `MACRIA_ESKI_PROJELER` |
| `projeler/B-Rep Calisma A.macria` | montaj-1 projesi, şema 1.0, 2 karar | Eski / gerçek proje açılışı | `MACRIA_ESKI_PROJELER` |
| `stepler/montaj-1/` | `B-Rep Calisma A.stp` + `beklenen.txt` (7 parça) | Gerçek montaj motoru, seçili parça, proje kaydet/aç | `MACRIA_GEOMETRY_ENGINE_ASSEMBLY_DIR` |
| `stepler/55RS100111-*/` | 8 tek parça STEP + `beklenen.txt` | Tek parçalı STEP satırları (`stepler/` kökünden) | `MACRIA_TEK_PARCA_KOK` |

- Motor: varsayılan olarak depodaki `GeometryEngineRuntime/Macria.GeometryEngine.exe` (`MACRIA_GEOMETRY_ENGINE_EXE`).
- Kaynak: 2026-10-06'da `Desktop\Macria-4A-Gercek-Test\Fixture` ve `claude-test` klasörlerinden kopyalandı.
- Toplam yaklaşık 10 MB.
