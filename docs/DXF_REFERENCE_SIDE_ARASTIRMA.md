# Save As DXF "Reference side" (Top/Bottom) varsayılanı — araştırma

- **Tarih:** 2026-09-27
- **Kurulum:** 3DEXPERIENCE `B428_Cloud` (R428, önbellek adından HF5)
- **Kapsam:** Yalnızca okuma yapıldı. Hiçbir dosya değiştirilmedi, CATIA çalıştırılmadı. ⚠️ işaretli maddeler doğrulanmalı.

## 1. Kullanıcı ayarları (`%APPDATA%\DassaultSystemes\CATSettings\`)

**Arama:** 7.525 dosya tarandı (Roaming + Local).

| Aranan ifade | Sonuç |
|---|---|
| `SaveAsDxf`, `ReferenceSide`, `Reference side`, `RefSide`, `RefFace`, `ReferenceFace`, `TopBottom`, `BendInfo` | Hiçbir dosyada yok |
| `SaveDxf` | Yalnızca 1 dosyada: `SheetMetalSettings.CATSettings` |

### 1.1 `R428\DESKTOP_Cloud\SheetMetalSettings.CATSettings`

- **Dosya:** 2.132 bayt, ikili biçim, son değişiklik 2026-09-27 18:32.
- **Bulunan anahtarlar (tamamı):**
  - `SaveDxf.BendingInformation`, `SaveDxf.BendingLines`, `SaveDxf.MappedElements`, `SaveDxf.StampLines`, `SaveDxf.Sketches`, `SaveDxf.AllTechData`, `SaveDxf.Tolerance` (double)
  - Renk anahtarları: `SaveDxf.{Bend, BendText, MappedElements, Stamp, AdditionalSketches}{Red, Green, Blue}Color`, `SaveDxf.StampBendRedColor`
- **Referans yüz (Top/Bottom) için anahtar yok.**
- ⚠️ Değerlerin çözümü (baytlardan tahmin): `BendingInformation = 0`, `AllTechData = 1`; bazı renkler 255. Bu, "Bend Information" kutusunun durumunun CATIA tarafından **kalıcı olarak hatırlandığını** gösteriyor olabilir. İkili biçim belgelenmemiştir; bu çözüm doğrulanmadı.

### 1.2 `R428\DESKTOP_Cloud\VIDDialogBox.CATPreferences`

- Panelin iç adı burada görünüyor: **`CATSmdDxfVidFraDialog`**.
- Bu panel için yalnızca pencere durumu saklanıyor: `.extractedState`, `.float`, `.version`. Seçenek değeri yok.

### 1.3 Diğer dosyalar

- **`SheetMetalDialog.CATSettings`:** Yalnızca başka sac panellerinin "expander" durumlarını içeriyor. DXF ile ilgisi yok.
- **`DialogEditStack.CATPreferences`**, **`AfrCommandsAccelerators.CATSettings`:** Yalnızca komut adı geçiyor (`Save As DXF`, `SmDxf`).
- **`%LOCALAPPDATA%\DassaultSystemes\CATReport\*.rpt`:** DXF **import** ve STEP export raporları. DXF export ayarı içermiyor.

### 1.4 Kurumsal ayar yolları

`CATEnv\Env.txt` içindeki `CATReferenceSettingPath` şu iki klasörü gösteriyor:

- `C:\Users\Public\Documents\Dassault Systemes\UserCache\B428\…\startup\Settings`
- `C:\Program Files\Dassault Systemes\B428_Cloud\win_b64\startup\Settings`

Bu klasörlerde sac metal ya da DXF ayarı yok. Yönetici tarafından kilitlenmiş bir varsayılan bulunamadı.

## 2. Kurulum klasörü (`C:\Program Files\Dassault Systemes\B428_Cloud\win_b64\`)

### 2.1 Panel metin katalogları (`resources\msgcatalog\`)

**`CATSmdDxfVidWidget.CATNls`** (etiket ve yardım metinleri):

```
MainDxfGrid.ReferenceSideLabel.Text = "Reference side";
MainDxfGrid.TopRadioButton.Label = "Top";
MainDxfGrid.BottomRadioButton.Label = "Bottom";
MainDxfGrid.TopRadioButton.TooltipShortHelp = "Defines the reference skin used for the extraction of the outline of the 3D shape.";
MainDxfGrid.BottomRadioButton.TooltipLongHelp = "The geometry saved as a DXF document is an extraction of the outline of the reference skin.";
```

**`CATSmdDxfPanel.CATNls`** (eski panel):

```
MainFrame._TopBottomFrame._TopRadioButton.LongHelp = "The skin used for contour extraction is the top one.";
MainFrame._TopBottomFrame._BottomRadioButton.LongHelp = "The skin used for contour extraction is the bottom one.";
```

**`CATSHMLiveDxfVidDlg.CATNls`:** `CATSHMLiveDxfSkinTop = "Top"`, `CATSHMLiveDxfSkinBottom = "Bottom"`.

**Anlamı:** DXF, seçilen **tek bir sac yüzeyinin ("skin") dış hatlarının** çıkarılmasıyla üretiliyor. Bu, havşa ve imbus davranışını doğrudan açıklıyor:

- Delik başı hangi yüzdeyse, o yüzün konturunda delik baş çapıyla görünür.
- Karşı yüzün konturunda delik geçiş çapıyla görünür.

**Dil desteği:** Panel katalogları French, German, Japanese, Russian ve Simplified_Chinese için var; **Turkish klasöründe yok**. ⚠️ Türkçe arayüzde bu panelin İngilizce etiketlerle ("Reference side", "Top", "Bottom") görünmesi beklenir. Bu, görüntü eşleştirmeyi kolaylaştırır.

### 2.2 Panel kodu

Kaynak: `code\bin\CATSmdDxfUI.dll`, `CATSmdDUI.dll`, `CATSmDxf.dll`. Yalnızca DLL içindeki okunabilir metinler (sembol adları) incelendi.

- **Kalıcı ayar listesi:** Her iki UI DLL'inde tanımlı `SaveDxf.*` anahtarlarının tam listesi §1.1'deki listeyle aynı. **Referans yüz ya da skin için kalıcı ayar anahtarı tanımlı değil.**
- **Seçenek panel kodunda yönetiliyor:**
  - `CATSmdDxfVidWidget::GetReferenceFaceType(unsigned short&)`
  - `CATSmdDxfOnTopFaceRadioButtonClick`, `CATSmdDxfOnBottomFaceRadioButtonClick`
  - `CATSmdTopBottomNotification`
- **Export servisi:** `CATSmDxf.dll` içinde `CATSheetmetalDxfCreateService::SetSkinType(int)` ve `SetSkinGraphicsProperties(...)` var. Yüz seçimi bu servise bir tamsayı olarak veriliyor.
  - ⚠️ Bu servis Dassault'nun iç (CAA internal) API'si gibi görünüyor (`DASSAULT_SYSTEMES_CAA2_INTERNAL_…` işareti). COM/Automation üzerinden erişilebildiğine dair bir kanıt yok.

### 2.3 Yardım belgeleri

- Yerelde kullanıcı yardımı yok. `win_b64\Help` yalnızca birkaç GIF içeriyor; `win_b64\docs` Java API belgeleri.
- Cloud kurulumunda yardım çevrimiçi olmalı ⚠️.
- "Save as DXF" açıklaması olarak bulunabilen tek kaynak §2.1'deki metin kataloglarıdır.

### 2.4 Ek bulgular (tam kurulum taraması)

- **İkinci kurulum `C:\Program Files\Dassault Systemes\B31\`** ⚠️ (sürüm/ürün adı doğrulanmadı; olasılıkla V5-6 tabanlı bir kurulum):
  - Bu kurulumda da aynı "Reference side: Top/Bottom" paneli var (`CATSmdDxfPanel.CATNls`).
  - `CATSmdDxfUI.dll` içindeki `SaveDxf.*` anahtarları da yalnızca renk ayarları ve `Tolerance`'tan ibaret. **Referans yüz anahtarı burada da yok.**
- **`B428_Cloud\win_b64\code\dictionary\CATSmDXF.dic`:** `MechanicalPart` nesnesine `CATISmDxfCreator`, `CATIStmDxfCreator` ve `CATIStmInternalDxfCreator` arayüzleri bağlanıyor. Uygulayıcı kütüphane `libCATSmDxf`.
  - ⚠️ Bunlar CAA (C++) arayüzleri. COM/Automation (VBA/.NET) üzerinden erişilebildiğine dair bir kanıt yok. Macria'nın bugünkü `dynamic` COM yoluyla kullanılamayacakları varsayılmalı.
- **`resources\graphic\CATSYPStyle\CATSmdDxfVidWidget.sypstyle`:** Yalnızca panelin görsel stil dosyası.

## 3. Sonuç

1. **Reference side, CATSettings'e kaydedilmiyor.** Kullanıcı ayarlarında, kurumsal ayar yollarında ve DLL'deki `SaveDxf.*` anahtar listesinde karşılığı yok. Buna karşılık Bend Information gibi diğer seçenekler kaydediliyor.
2. **Varsayılan değer ayar dosyasından gelmiyor; panelin kendi kodunda belirleniyor.** ⚠️ İki olasılık var ve hangisi olduğu dosyalardan anlaşılamaz:
   - Panel her açıldığında sabit bir varsayılanla (muhtemelen Top) başlıyor.
   - Seçim yalnızca CATIA oturumu boyunca bellekte hatırlanıyor.
   
   Bu yüzden Macria'nın bir ayar dosyasını değiştirerek Bottom'u varsayılan yapması **mümkün görünmüyor**. Ayrıca ayar dosyalarını değiştirmek zaten önerilmez.
3. **Top/Bottom, DXF'in hangi sac yüzeyinden çıkarılacağını belirliyor.** Havşa ve imbus farkı bundan kaynaklanıyor. Bu, `DXF_HAVSA_ARASTIRMA.md`'deki "iki yüzden export + küçük çapı seçme (A)" yaklaşımını destekliyor.
4. **Programlı seçim:** Bilinen tek yol panel otomasyonu, yani radyo düğmesini görüntüden bulup tıklamak. `SetSkinType` servisine COM ile erişim ⚠️ belirsiz ve muhtemelen kapalı.
5. **Yan bulgu:** `SaveDxf.BendingInformation` kalıcı bir ayar. Çözümüm doğruysa değeri şu an 0. ⚠️ Bu doğruysa Bend Information kutusu CATIA tarafından hatırlanıyor; Macria'nın `BukumBulucu` ile her export'ta kontrol etmesi bir güvenlik katmanı olarak kalır.

## 4. CATIA'da doğrulamanız önerilen denemeler

| # | Deneme | Neyi doğrular |
|---|---|---|
| R1 | Yeni CATIA oturumunda Save As DXF'i açın; hangi radyo seçili? | Varsayılan değer |
| R2 | Bottom seçip export edin. Paneli aynı oturumda başka bir parça için yeniden açın. | Oturum içinde hatırlanıyor mu |
| R3 | CATIA'yı kapatıp açın, paneli tekrar açın. | Oturumlar arası hatırlama. §1'e göre beklenen: hatırlamaz |
| R4 | R2'den önce ve sonra `SheetMetalSettings.CATSettings`'in değişiklik saatini karşılaştırın (dosyayı yalnızca okuyun). | Yüz seçimi dosyaya yazılıyor mu. Beklenen: yazılmıyor, ya da yalnızca diğer `SaveDxf.*` değerleri yazılıyor |
| R5 | Bend Information kutusunu değiştirip paneli yeniden açın. | `SaveDxf.BendingInformation` yorumunun doğruluğu |
| R6 | Türkçe arayüzde panelin ekran görüntüsünü alın. | Etiketlerin İngilizce olup olmadığı; Macria'da görüntü öğretme için referans |
