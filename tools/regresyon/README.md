# Motor regresyonu

Regresyon seti repoda değil, `Masaüstü\Macria-Regresyon\` altındadır (OneDrive). Başka bir yerdeyse `MACRIA_REGRESYON` ortam değişkeni `stepler` klasörünü gösterir.

```
Macria-Regresyon\
  stepler\       12 STEP (her biri kendi klasöründe)
  projeler\      WGRV004423 A.macria, B-Rep Calisma A.macria, toplucalisma.macria, toplucalismaR1 A.macria
  referans-dxf\  CATIA DXF'leri: 1201_Baski_Kolu, 1201_Mafsal (t = 2 mm);
                 sac-lazer\ 7 dosya (55RS100111-1, -2, -3, -4, -8, -9, -12; motor DXF'iyle karşılaştırma için)
  temel\<motor sürümü>\   o motorla alınmış temel çıktılar
```

Motor değişikliğinden sonra:

```powershell
# 1) Otomatik analiz: bütün STEP'ler, temel ile karşılaştır (fark 0 olmalı)
python tools/regresyon/regresyon.py calistir <Macria.GeometryEngine.exe> <yeni-klasör>
python tools/regresyon/regresyon.py karsilastir <Macria-Regresyon\temel\<sürüm>> <yeni-klasör> [yok sayılacak,anahtarlar]

# 2) Seçili parça analizi = tam analiz
python tools/regresyon/parca-esdeger.py hepsi <exe> <yeni-klasör> <çıktı>   # her STEP, bütün parçalar --parcalar ile
python tools/regresyon/parca-esdeger.py tekli <exe> <yeni-klasör> <çıktı>   # montaj-1 her parça, WGRV 20 parça tek tek
```

Yeni bir motor sürümü paketlendiğinde (`GeometryEngineRuntime/motor-surumu.txt`) temel çıktıyı `temel\<yeni sürüm>\` altına alın; eskisi silinmez.
