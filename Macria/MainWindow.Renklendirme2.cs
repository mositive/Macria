using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Macria
{
    public partial class MainWindow
    {
        private Renklendirme2ResetState? _renklendirme2ResetState;
        private int _renklendirme2PaletteCycleIndex;
        private string _renklendirme2SonDurum = "";

        private async void Renklendirme2Uygula_Click(object sender, RoutedEventArgs e)
        {
            if (!_akilliRenklendirmeKilidi.Baslat())
            {
                LogError("Renklendirme 2.0 başlatılamadı: başka bir renklendirme işlemi devam ediyor.");
                return;
            }

            btnAkilliRenklendirme.IsEnabled = false;
            SetRenklendirme2PanelEnabled(false);
            var stopwatch = Stopwatch.StartNew();
            dynamic? selection = null;
            List<object>? previousSelection = null;
            CatiaLightInventorySnapshot? inventory = null;
            var appliedTargets = new List<Renklendirme2AppliedTarget>();
            bool recolor = ReferenceEquals(sender, btnRenklendirme2Yeniden);
            int paletteCycleIndex = recolor
                ? (_renklendirme2PaletteCycleIndex + 1) % Renklendirme2Paleti.Renkler.Count
                : 0;
            Dictionary<string, Renklendirme2UiSatiri> uiByReference =
                Renklendirme2UiSatirlariByReference();
            var includedReferenceKeys = new HashSet<string>(
                uiByReference.Values
                    .Where(item => item.IsColorable && item.Included)
                    .Select(item => item.ReferenceKey),
                StringComparer.Ordinal);
            try
            {
                if (uiByReference.Count == 0)
                    throw new InvalidOperationException(
                        "Eşsiz Ürün Ağacı renk listesi boş. Önce CATIA'yı Tarayın.");
                if (includedReferenceKeys.Count == 0)
                    throw new InvalidOperationException(
                        "Renklendirilecek parça yok. En az bir benzersiz referansı dahil edin.");

                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");

                object editorObject = (object)((dynamic)catiaObject).ActiveEditor;
                object? rootObject = ((dynamic)editorObject).ActiveObject;
                if (rootObject is not object activeRoot)
                    throw new InvalidOperationException("Aktif CATIA montaj kökü alınamadı.");
                if (!HasOccurrences((dynamic)activeRoot))
                {
                    throw new InvalidOperationException(
                        "Aktif CATIA nesnesi occurrence içeren bir Physical Product montajı değil.");
                }

                Renklendirme2RuntimeIdentity runtimeIdentity = CaptureRenklendirme2RuntimeIdentity(
                    catiaObject,
                    editorObject,
                    activeRoot);
                if (_renklendirme2ResetState != null &&
                    _renklendirme2ResetState.Targets.Count > 0 &&
                    !string.Equals(
                        _renklendirme2ResetState.RuntimeIdentity.Root.SemanticKey,
                        runtimeIdentity.Root.SemanticKey,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Eşsiz Ürün Ağacı renk durumu farklı bir aktif montaja ait. " +
                        "Önce önceki montajı etkinleştirip Automatic'e döndürün.");
                }
                selection = ((dynamic)editorObject).Selection;
                previousSelection = SecimiSakla(selection);

                LogInfo(
                    "Renklendirme 2.0 — hedef planı hazırlanıyor. Yalnız PartBody hedefleri çözülecek; " +
                    "henüz renk yazılmayacak.");
                LogInfo(Renklendirme2RuntimeDiagnostic("PLANLANAN", runtimeIdentity));

                var inventoryReader = new CatiaLightInventoryReader();
                CatiaLightInventoryReadResult inventoryResult = inventoryReader.Read(activeRoot);
                if (!inventoryResult.Success || inventoryResult.Snapshot == null)
                {
                    throw new InvalidOperationException(
                        "Hafif occurrence envanteri alınamadı: " + inventoryResult.Error);
                }

                inventory = inventoryResult.Snapshot;
                BulkPartBodyResolutionBatch targetBatch = ResolveBulkPartBodyTargets(
                    inventory,
                    inventoryResult.OccurrencesByReference,
                    includedReferenceKeys);
                string[] currentIncludedKeys = inventory.UniqueParts
                    .Select(item => item.ReferenceKey)
                    .Where(includedReferenceKeys.Contains)
                    .ToArray();
                Renklendirme2RenkPlani colorPlan = Renklendirme2RenkPlani.Olustur(
                    currentIncludedKeys,
                    paletteCycleIndex);

                foreach (string missingReference in includedReferenceKeys.Except(
                             currentIncludedKeys,
                             StringComparer.Ordinal))
                {
                    string error = "Aktif montajın güncel envanterinde ReferenceKey bulunamadı.";
                    uiByReference[missingReference].MarkFailed(error);
                    LogError(
                        "Renklendirme 2.0 plan — referans=" + missingReference +
                        " | ATLANDI | " + error);
                }

                var readyTargets = new List<Renklendirme2ReadyTarget>();
                foreach (BulkPartBodyResolution resolution in targetBatch.Resolutions.Where(item => !item.Success))
                {
                    LogError(
                        "Renklendirme 2.0 plan — referans=" + resolution.Item.ReferenceKey +
                        " | ATLANDI | " + resolution.Error);
                    Renklendirme2UiHata(uiByReference, resolution.Item.ReferenceKey, resolution.Error);
                }

                foreach (KeyValuePair<string, IReadOnlyList<string>> collision in targetBatch.Collisions)
                {
                    LogError(
                        "Renklendirme 2.0 plan — farklı referanslar aynı PartBody COM hedefini bildirdi; " +
                        "tamamı atlandı | referanslar=" + string.Join(", ", collision.Value) +
                        " | hedef=" + collision.Key);
                    foreach (string referenceKey in collision.Value)
                        Renklendirme2UiHata(uiByReference, referenceKey, "PartBody hedef çakışması.");
                }

                foreach (string overflowReference in colorPlan.OverflowReferenceKeys)
                {
                    LogError(
                        "Renklendirme 2.0 plan — palet kapasitesi aşıldı; renk tekrarı yapılmadan referans atlandı: " +
                        overflowReference);
                    Renklendirme2UiHata(
                        uiByReference,
                        overflowReference,
                        "72 renkli palet kapasitesi aşıldı; renk tekrarı yapılmadı.");
                }

                foreach (BulkPartBodyResolution resolution in targetBatch.SafeResolutions)
                {
                    if (resolution.Target == null || resolution.RepresentativeOccurrence == null)
                        continue;
                    if (!colorPlan.Assignments.TryGetValue(
                            resolution.Item.ReferenceKey,
                            out Renklendirme2RenkAtamasi? assignment))
                    {
                        continue;
                    }

                    readyTargets.Add(new Renklendirme2ReadyTarget(resolution, assignment));
                }

                LogInfo(
                    "Renklendirme 2.0 — hedef planı hazır\n" +
                    "Toplam occurrence: " + inventory.TotalOccurrenceCount + "\n" +
                    "Unique reference: " + inventory.UniqueParts.Count + "\n" +
                    "Dahil unique reference: " + includedReferenceKeys.Count + "\n" +
                    "Palet cycle: " + paletteCycleIndex + "\n" +
                    "Yazmaya hazır unique reference: " + readyTargets.Count + "\n" +
                    "Başarısız/atlanmış dahil reference: " + (includedReferenceKeys.Count - readyTargets.Count) + "\n" +
                    "Palet kapasitesi: " + Renklendirme2Paleti.Renkler.Count + "\n" +
                    "Envanter durumu: " + (inventoryResult.IsComplete ? "tam" : "EKSİK"));

                if (!inventoryResult.IsComplete)
                {
                    foreach (string diagnostic in inventoryResult.Diagnostics.Take(20))
                        LogError("Renklendirme 2.0 envanter tanısı — " + diagnostic);
                }

                if (readyTargets.Count == 0)
                    throw new InvalidOperationException("Güvenle renklendirilebilecek PartBody hedefi bulunamadı.");

                if (!OnayWindow.Sor(
                        this,
                        recolor ? "Yeniden Renklendir" : "Renklendir",
                        readyTargets.Count + " benzersiz parçanın PartBody rengi değiştirilecek.\n\n" +
                        "Yalnız dahil edilen referanslar işlenecek; Product/Occurrence seviyesine dokunulmayacak.\n" +
                        "Devam edilsin mi?",
                        recolor ? "Yeniden renklendir" : "Renklendir"))
                {
                    LogInfo("Renklendirme 2.0 kullanıcı tarafından onaydan önce iptal edildi; renk yazılmadı.");
                    return;
                }

                EnsureRenklendirme2Runtime(runtimeIdentity, "Kullanıcı onayı sonrası", true);

                // Hiçbir renk yazmadan önce bütün hazırlanan Body COM kimliklerini yeniden doğrula.
                var verifiedTargets = new List<Renklendirme2ReadyTarget>();
                foreach (Renklendirme2ReadyTarget ready in readyTargets)
                {
                    if (TryValidateRenklendirme2Target(ready.Resolution, out string validationError))
                    {
                        verifiedTargets.Add(ready);
                    }
                    else
                    {
                        LogError(
                            "Renklendirme 2.0 — referans=" + ready.ReferenceKey +
                            " | YAZMADAN ATLANDI | " + validationError);
                        Renklendirme2UiHata(uiByReference, ready.ReferenceKey, validationError);
                    }
                }

                var targetService = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
                for (int index = 0; index < verifiedTargets.Count; index++)
                {
                    Renklendirme2ReadyTarget ready = verifiedTargets[index];
                    EnsureRenklendirme2Runtime(runtimeIdentity, "PartBody yazımı öncesi", false);

                    // Dispatcher yield veya önceki COM çağrısından sonra hedef değişmiş olabilir.
                    // Her yazımdan hemen önce planlanan aynı PartBody COM hedefini tekrar doğrula.
                    if (!TryValidateRenklendirme2Target(ready.Resolution, out string writeValidationError))
                    {
                        LogError(
                            "Renklendirme 2.0 — referans=" + ready.ReferenceKey +
                            " | YAZMADAN ATLANDI | " + writeValidationError);
                        Renklendirme2UiHata(uiByReference, ready.ReferenceKey, writeValidationError);
                        continue;
                    }

                    CatiaColorOperationResult writeResult = targetService.TryApplyRgb(
                        selection,
                        ready.Target,
                        ready.Color.Red,
                        ready.Color.Green,
                        ready.Color.Blue);
                    if (!writeResult.Success)
                    {
                        LogError(
                            "Renklendirme 2.0 — referans=" + ready.ReferenceKey +
                            " | RENK YAZILAMADI | " + Renklendirme2OperationError(writeResult));
                        Renklendirme2UiHata(
                            uiByReference,
                            ready.ReferenceKey,
                            Renklendirme2OperationError(writeResult));
                        if (writeResult.Exception != null && KritikComHatasiMi(writeResult.Exception))
                        {
                            LogError("Renklendirme 2.0 — CATIA bağlantısı kritik hata verdi; kalan hedefler işlenmedi.");
                            break;
                        }

                        continue;
                    }

                    var applied = new Renklendirme2AppliedTarget(
                        ready.ReferenceKey,
                        ready.Resolution.Item.DisplayName,
                        ready.Resolution.OccurrenceCount,
                        ready.Resolution.RepresentativeOccurrence!,
                        ready.Target.Identity.SessionObjectKey,
                        ready.Assignment.PaletteIndex,
                        ready.Color);
                    appliedTargets.Add(applied);
                    // Beklenmeyen bir sonraki COM hatasında bile başarıyla yazılan hedefler
                    // Automatic'e dönüş komutunca erişilebilir kalsın.
                    MergeRenklendirme2ResetState(runtimeIdentity, new[] { applied });
                    if (uiByReference.TryGetValue(applied.ReferenceKey, out Renklendirme2UiSatiri? uiRow))
                        uiRow.MarkAssigned(ready.Assignment);
                    LogInfo(
                        "Renklendirme 2.0 — referans=" + applied.ReferenceKey +
                        " | occurrence=" + applied.OccurrenceCount +
                        " | PartBody=" + applied.OriginalTargetComDiagnostic +
                        " | RGB/HEX=" + applied.Color);

                    if ((index + 1) % 8 == 0)
                        await Dispatcher.Yield(DispatcherPriority.Background);
                }

                if (appliedTargets.Count > 0)
                    _renklendirme2PaletteCycleIndex = paletteCycleIndex;

                stopwatch.Stop();
                int usedColorCount = appliedTargets.Select(item => item.Color.Hex)
                    .Distinct(StringComparer.Ordinal).Count();
                int failedOrSkipped = includedReferenceKeys.Count - appliedTargets.Count;
                string summary =
                    "Renklendirme 2.0 — UYGULAMA ÖZETİ\n" +
                    "Toplam occurrence: " + inventory.TotalOccurrenceCount + "\n" +
                    "Unique reference: " + inventory.UniqueParts.Count + "\n" +
                    "Dahil unique reference: " + includedReferenceKeys.Count + "\n" +
                    "Palet cycle: " + paletteCycleIndex + "\n" +
                    "Başarıyla renklendirilen unique reference: " + appliedTargets.Count + "\n" +
                    "Başarısız/atlanmış reference: " + failedOrSkipped + "\n" +
                    "Kullanılan renk sayısı: " + usedColorCount + "\n" +
                    "Toplam süre: " + stopwatch.ElapsedMilliseconds + " ms";
                if (appliedTargets.Count > 0)
                    LogSuccess(summary);
                else
                    LogError(summary);
                Renklendirme2DurumunuGuncelle(
                    appliedTargets.Count + "/" + includedReferenceKeys.Count +
                    " parça renklendirildi — " + SureMetni(stopwatch.Elapsed));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (appliedTargets.Count > 0 && _renklendirme2ResetState == null)
                {
                    LogError(
                        "Renklendirme 2.0 beklenmeyen biçimde durdu; " + appliedTargets.Count +
                        " PartBody rengi yazılmış olabilir. Automatic'e dönüş state'i oluşturulamadı; belgeyi kaydetmeyin.");
                }

                LogError("Renklendirme 2.0 uygulanamadı: " + ComDiagnostic(ex));
                Renklendirme2DurumunuGuncelle("Renklendirme tamamlanamadı — ayrıntı konsolda");
            }
            finally
            {
                if (selection != null && previousSelection != null)
                    SecimiGeriYukle(selection, previousSelection);
                btnAkilliRenklendirme.IsEnabled = true;
                SetRenklendirme2PanelEnabled(true);
                _akilliRenklendirmeKilidi.Bitir();
            }
        }

        private async void Renklendirme2AutomaticDon_Click(object sender, RoutedEventArgs e)
        {
            if (!_akilliRenklendirmeKilidi.Baslat())
            {
                LogError("Renklendirme 2.0 Automatic'e dönüş başlatılamadı: başka bir renklendirme işlemi devam ediyor.");
                return;
            }

            btnAkilliRenklendirme.IsEnabled = false;
            SetRenklendirme2PanelEnabled(false);
            var stopwatch = Stopwatch.StartNew();
            dynamic? selection = null;
            List<object>? previousSelection = null;
            try
            {
                Renklendirme2ResetState? state = _renklendirme2ResetState;
                if (state == null || state.Targets.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Bu Macria oturumunda Renklendirme 2.0 tarafından başarıyla boyanmış bekleyen PartBody hedefi yok.");
                }

                object? catiaObject = GetCatia();
                if (catiaObject == null)
                    throw new InvalidOperationException("CATIA bağlantısı kurulamadı.");
                object editorObject = (object)((dynamic)catiaObject).ActiveEditor;
                selection = ((dynamic)editorObject).Selection;
                previousSelection = SecimiSakla(selection);

                EnsureRenklendirme2Runtime(state.RuntimeIdentity, "Automatic onayı öncesi", true);

                if (!OnayWindow.Sor(
                        this,
                        "Renklendirme 2.0 — Automatic'e Dön",
                        "Renklendirme 2.0 kapsamındaki " + state.Targets.Count +
                        " parçanın rengi Automatic'e döndürülecek.\n\n" +
                        "Yalnız PartBody color property kaldırılacak; Product/Occurrence seviyesine dokunulmayacak.\n" +
                        "Devam edilsin mi?",
                        "Automatic'e döndür"))
                {
                    LogInfo("Renklendirme 2.0 Automatic'e dönüş kullanıcı tarafından iptal edildi; reset yapılmadı.");
                    return;
                }

                EnsureRenklendirme2Runtime(state.RuntimeIdentity, "Automatic kullanıcı onayı sonrası", true);

                Renklendirme2AppliedTarget[] targetsToReset = state.Targets.ToArray();
                var failedTargets = new List<Renklendirme2AppliedTarget>();
                var pendingTargets = targetsToReset.ToList();
                Dictionary<string, Renklendirme2UiSatiri> uiByReference =
                    Renklendirme2UiSatirlariByReference();
                var targetService = new CatiaColorTargetService(node => ReferansAl((dynamic)node));
                int resetCount = 0;
                for (int index = 0; index < targetsToReset.Length; index++)
                {
                    Renklendirme2AppliedTarget applied = targetsToReset[index];
                    EnsureRenklendirme2Runtime(state.RuntimeIdentity, "PartBody reset öncesi", false);

                    if (!TryResolveRenklendirme2AutomaticTarget(
                            targetService,
                            applied,
                            out CatiaColorTarget? resetTarget,
                            out string validationError))
                    {
                        failedTargets.Add(applied);
                        Renklendirme2UiHata(
                            uiByReference,
                            applied.ReferenceKey,
                            validationError);
                        LogError(
                            "Renklendirme 2.0 Automatic — referans=" + applied.ReferenceKey +
                            " | RESET ATLANDI | " + validationError);
                        continue;
                    }

                    CatiaColorOperationResult resetResult = targetService.TryResetColor(selection, resetTarget!);
                    if (!resetResult.Success)
                    {
                        failedTargets.Add(applied);
                        Renklendirme2UiHata(
                            uiByReference,
                            applied.ReferenceKey,
                            Renklendirme2OperationError(resetResult));
                        LogError(
                            "Renklendirme 2.0 Automatic — referans=" + applied.ReferenceKey +
                            " | RESET BAŞARISIZ | " + Renklendirme2OperationError(resetResult));
                        if (resetResult.Exception != null && KritikComHatasiMi(resetResult.Exception))
                        {
                            for (int remaining = index + 1; remaining < targetsToReset.Length; remaining++)
                                failedTargets.Add(targetsToReset[remaining]);
                            LogError("Renklendirme 2.0 Automatic — CATIA bağlantısı kritik hata verdi; kalan hedefler işlenmedi.");
                            break;
                        }

                        continue;
                    }

                    resetCount++;
                    if (uiByReference.TryGetValue(
                            applied.ReferenceKey,
                            out Renklendirme2UiSatiri? uiRow))
                    {
                        uiRow.MarkAutomatic();
                    }
                    pendingTargets.Remove(applied);
                    // Sonraki COM çağrısı beklenmedik biçimde durursa yalnız henüz
                    // resetlenmeyen hedefler yeniden denenecek listede kalsın.
                    state.ReplaceTargets(pendingTargets);
                    LogInfo(
                        "Renklendirme 2.0 Automatic — referans=" + applied.ReferenceKey +
                        " | yeniden çözülen PartBody=" + resetTarget!.Identity.SessionObjectKey +
                        " | ilk yazım hedefi (yalnız tanı)=" + applied.OriginalTargetComDiagnostic +
                        " | color property resetlendi.");

                    if ((index + 1) % 8 == 0)
                        await Dispatcher.Yield(DispatcherPriority.Background);
                }

                if (failedTargets.Count == 0)
                {
                    _renklendirme2ResetState = null;
                    _renklendirme2PaletteCycleIndex = 0;
                }
                else
                    state.ReplaceTargets(failedTargets);

                stopwatch.Stop();
                string summary =
                    "Renklendirme 2.0 — AUTOMATIC ÖZETİ\n" +
                    "Automatic'e dönen unique reference: " + resetCount + "\n" +
                    "Başarısız/atlanmış reference: " + failedTargets.Count + "\n" +
                    "Toplam süre: " + stopwatch.ElapsedMilliseconds + " ms";
                if (failedTargets.Count == 0)
                    LogSuccess(summary);
                else
                    LogError(summary);
                Renklendirme2DurumunuGuncelle(
                    resetCount + "/" + targetsToReset.Length + " parça Automatic'e döndü");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogError("Renklendirme 2.0 Automatic'e dönüş uygulanamadı: " + ComDiagnostic(ex));
                Renklendirme2DurumunuGuncelle("Automatic'e dönüş tamamlanamadı — ayrıntı konsolda");
            }
            finally
            {
                if (selection != null && previousSelection != null)
                    SecimiGeriYukle(selection, previousSelection);
                btnAkilliRenklendirme.IsEnabled = true;
                SetRenklendirme2PanelEnabled(true);
                _akilliRenklendirmeKilidi.Bitir();
            }
        }

        private void Renklendirme2TumunuSec_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetRow row in _urunAgaciRows)
                if (row.Renklendirme2 != null && row.Renklendirme2.IsColorable)
                    row.Renklendirme2.Included = true;
            Renklendirme2DurumunuGuncelle();
        }

        private void Renklendirme2TumunuKaldir_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetRow row in _urunAgaciRows)
                if (row.Renklendirme2 != null)
                    row.Renklendirme2.Included = false;
            Renklendirme2DurumunuGuncelle();
        }

        private void Renklendirme2Dahil_Click(object sender, RoutedEventArgs e) =>
            Renklendirme2DurumunuGuncelle();

        private void Renklendirme2SatirlariniHazirla(IEnumerable<SheetRow> rows)
        {
            Dictionary<string, Renklendirme2AppliedTarget> applied =
                _renklendirme2ResetState?.Targets.ToDictionary(
                    item => item.ReferenceKey,
                    StringComparer.Ordinal) ??
                new Dictionary<string, Renklendirme2AppliedTarget>(StringComparer.Ordinal);

            foreach (SheetRow row in rows)
            {
                var ui = new Renklendirme2UiSatiri(
                    row.Renklendirme2ReferenceKey,
                    row.ProductName,
                    row.Quantity);
                if (applied.TryGetValue(ui.ReferenceKey, out Renklendirme2AppliedTarget? target))
                {
                    ui.MarkAssigned(new Renklendirme2RenkAtamasi(
                        target.ReferenceKey,
                        target.PaletteIndex,
                        target.Color));
                }
                row.Renklendirme2 = ui;
            }

            Renklendirme2DurumunuGuncelle();
        }

        private Dictionary<string, Renklendirme2UiSatiri> Renklendirme2UiSatirlariByReference() =>
            _urunAgaciRows
                .Where(row => row.Renklendirme2 != null &&
                              !string.IsNullOrWhiteSpace(row.Renklendirme2.ReferenceKey))
                .GroupBy(row => row.Renklendirme2!.ReferenceKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Renklendirme2!, StringComparer.Ordinal);

        private static void Renklendirme2UiHata(
            IReadOnlyDictionary<string, Renklendirme2UiSatiri> uiByReference,
            string referenceKey,
            string error)
        {
            if (uiByReference.TryGetValue(referenceKey, out Renklendirme2UiSatiri? row))
                row.MarkFailed(error);
        }

        private void MergeRenklendirme2ResetState(
            Renklendirme2RuntimeIdentity runtimeIdentity,
            IEnumerable<Renklendirme2AppliedTarget> appliedTargets)
        {
            var merged = new Dictionary<string, Renklendirme2AppliedTarget>(StringComparer.Ordinal);
            if (_renklendirme2ResetState != null)
            {
                foreach (Renklendirme2AppliedTarget target in _renklendirme2ResetState.Targets)
                    merged[target.ReferenceKey] = target;
            }

            foreach (Renklendirme2AppliedTarget target in appliedTargets)
                merged[target.ReferenceKey] = target;

            _renklendirme2ResetState = new Renklendirme2ResetState(
                runtimeIdentity,
                merged.Values.OrderBy(item => item.ReferenceKey, StringComparer.Ordinal));
        }

        private void SetRenklendirme2PanelEnabled(bool enabled)
        {
            if (btnRenklendirme2Renklendir != null) btnRenklendirme2Renklendir.IsEnabled = enabled;
            if (btnRenklendirme2Yeniden != null) btnRenklendirme2Yeniden.IsEnabled = enabled;
            if (btnRenklendirme2Automatic != null) btnRenklendirme2Automatic.IsEnabled = enabled;
            if (btnRenklendirme2TumunuSec != null) btnRenklendirme2TumunuSec.IsEnabled = enabled;
            if (btnRenklendirme2TumunuKaldir != null) btnRenklendirme2TumunuKaldir.IsEnabled = enabled;
        }

        private void Renklendirme2DurumunuGuncelle(string? operationStatus = null)
        {
            if (operationStatus != null)
                _renklendirme2SonDurum = operationStatus;

            if (txtRenklendirme2Durum == null) return;
            Renklendirme2UiSatiri[] rows = _urunAgaciRows
                .Select(row => row.Renklendirme2)
                .Where(row => row != null && row.IsColorable)
                .Cast<Renklendirme2UiSatiri>()
                .ToArray();
            if (rows.Length == 0)
            {
                txtRenklendirme2Durum.Text = "Önce CATIA'yı tarayın";
                return;
            }

            int included = rows.Count(row => row.Included);
            int assigned = rows.Count(row => row.IsAssigned);
            string counts = rows.Length + " parça • " + included + " dahil • " + assigned + " renklendirildi";
            txtRenklendirme2Durum.Text = string.IsNullOrWhiteSpace(_renklendirme2SonDurum)
                ? counts
                : _renklendirme2SonDurum + "   •   " + counts;
        }

        private void EnsureRenklendirme2Runtime(
            Renklendirme2RuntimeIdentity planned,
            string stage,
            bool logSuccessfulComparison)
        {
            object? currentCatia = GetCatia();
            if (currentCatia == null)
            {
                throw new InvalidOperationException(
                    stage + " — CATIA oturumu alınamadı; Renklendirme 2.0 işlemi durduruldu.\n" +
                    Renklendirme2RuntimeDiagnostic("PLANLANAN", planned));
            }

            Renklendirme2RuntimeIdentity current;
            try
            {
                object currentEditor = (object)((dynamic)currentCatia).ActiveEditor;
                object? currentRoot = ((dynamic)currentEditor).ActiveObject;
                if (currentRoot == null)
                    throw new InvalidOperationException("ActiveEditor.ActiveObject alınamadı.");

                current = CaptureRenklendirme2RuntimeIdentity(
                    currentCatia,
                    currentEditor,
                    currentRoot);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    stage + " — mevcut editor/root kimliği okunamadı; Renklendirme 2.0 işlemi durduruldu.\n" +
                    Renklendirme2RuntimeDiagnostic("PLANLANAN", planned) + "\n" +
                    "MEVCUT kimlik okuma hatası=" + ComDiagnostic(ex),
                    ex);
            }

            var failedComparisons = new List<string>();
            // Application, ActiveEditor ve ActiveObject yeniden okunduğunda aynı mantıksal
            // bağlam farklı COM wrapper/interface pointer'ları verebilir. Bu adresler yalnız
            // tanıda gösterilir. Güvenlik karşılaştırması, güncel ActiveEditor.ActiveObject
            // üzerinden inventory ile aynı PLM ReferenceKey + occurrence/path semantiğidir.
            if (!string.Equals(
                    current.Root.SemanticKey,
                    planned.Root.SemanticKey,
                    StringComparison.Ordinal))
            {
                failedComparisons.Add("aktif montaj kökü semantik PLM/path identity");
            }

            string diagnostic =
                stage + " — Renklendirme 2.0 runtime kimlik karşılaştırması\n" +
                Renklendirme2RuntimeDiagnostic("PLANLANAN", planned) + "\n" +
                Renklendirme2RuntimeDiagnostic("MEVCUT", current) + "\n" +
                "Başarısız karşılaştırma=" +
                (failedComparisons.Count == 0 ? "yok" : string.Join(", ", failedComparisons));

            if (failedComparisons.Count > 0)
            {
                throw new InvalidOperationException(
                    stage + " — runtime hedefi değişti; Renklendirme 2.0 işlemi durduruldu.\n" + diagnostic);
            }

            if (logSuccessfulComparison)
                LogInfo(diagnostic);
        }

        private static Renklendirme2RuntimeIdentity CaptureRenklendirme2RuntimeIdentity(
            object catiaObject,
            object editorObject,
            object activeRoot)
        {
            Renklendirme2RootIdentity root = CaptureRenklendirme2RootIdentity(activeRoot);
            return new Renklendirme2RuntimeIdentity(
                CatiaColorTargetService.GetSessionObjectKey(catiaObject),
                CatiaColorTargetService.GetSessionObjectKey(editorObject),
                root,
                CatiaColorTargetService.GetSessionObjectKey(activeRoot));
        }

        private static Renklendirme2RootIdentity CaptureRenklendirme2RootIdentity(object activeRoot)
        {
            object? reference = ReferansAl((dynamic)activeRoot);
            string referenceKey = Renklendirme2PlmReferenceKey(reference);

            object? occurrenceEntity = null;
            try { occurrenceEntity = ((dynamic)activeRoot).PLMEntity; }
            catch { }
            string occurrenceKey = Renklendirme2PlmReferenceKey(occurrenceEntity);

            string rootName = "";
            try { rootName = Convert.ToString(((dynamic)activeRoot).Name)?.Trim() ?? ""; }
            catch { }

            if (string.IsNullOrWhiteSpace(referenceKey) &&
                string.IsNullOrWhiteSpace(occurrenceKey))
            {
                throw new InvalidOperationException(
                    "Aktif montaj kökü için stabil PLM reference/occurrence kimliği okunamadı.");
            }

            return new Renklendirme2RootIdentity(
                referenceKey,
                occurrenceKey,
                "ROOT/" + (string.IsNullOrWhiteSpace(rootName) ? "(adsız)" : rootName),
                ComProbe.TipAdi(activeRoot));
        }

        private static string Renklendirme2PlmReferenceKey(object? source)
        {
            if (source == null) return "";

            string externalId = PlmDeger(source, "PLM_ExternalID");
            string version = PlmDeger(source, "V_version");
            if (string.IsNullOrWhiteSpace(version))
                version = PlmDeger(source, "revision");
            if (string.IsNullOrWhiteSpace(version))
                version = PlmDeger(source, "Revision");

            return AkilliRenklendirmeMantigi.ReferansAnahtari(externalId, version) ?? "";
        }

        private static string Renklendirme2RuntimeDiagnostic(
            string label,
            Renklendirme2RuntimeIdentity identity) =>
            label + " CATIA COM (yalnız tanı; karşılaştırılmaz)=" + identity.CatiaSessionKey + "\n" +
            label + " ActiveEditor COM (yalnız tanı; karşılaştırılmaz)=" + identity.EditorSessionKey + "\n" +
            label + " root semantic=" + identity.Root.SemanticKey + "\n" +
            label + " root COM type (yalnız tanı)=" + identity.Root.ComType + "\n" +
            label + " root COM (yalnız tanı; karşılaştırılmaz)=" + identity.RootComDiagnostic;

        private static bool TryResolveRenklendirme2AutomaticTarget(
            CatiaColorTargetService targetService,
            Renklendirme2AppliedTarget applied,
            out CatiaColorTarget? target,
            out string error)
        {
            target = null;
            try
            {
                string? currentReferenceKey =
                    AkilliRenklendirmeReferansAnahtari(applied.RepresentativeOccurrence);
                if (string.IsNullOrWhiteSpace(currentReferenceKey))
                {
                    error = "Representative occurrence ReferenceKey değeri yeniden okunamadı.";
                    return false;
                }

                if (!string.Equals(
                        currentReferenceKey,
                        applied.ReferenceKey,
                        StringComparison.Ordinal))
                {
                    error =
                        "Representative occurrence farklı reference bildirdi " +
                        "(planlanan=" + applied.ReferenceKey +
                        ", mevcut=" + currentReferenceKey + ").";
                    return false;
                }

                PartBodyDiscoveryResult discovery = ResolveMountedPartBody(
                    targetService,
                    applied.RepresentativeOccurrence);
                if (!discovery.Success || discovery.Target == null)
                {
                    error = "PartBody yeniden çözümlenemedi: " + discovery.Error;
                    return false;
                }

                CatiaColorTarget resolvedTarget = discovery.Target;
                if (resolvedTarget.Identity.TargetType != CatiaColorTargetType.PartBody ||
                    resolvedTarget.Identity.ComType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error =
                        "Yeniden çözülen hedef PartBody/Body değil (" +
                        resolvedTarget.Identity.ComType + ").";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(resolvedTarget.Identity.ReferenceKey) &&
                    !string.Equals(
                        resolvedTarget.Identity.ReferenceKey,
                        applied.ReferenceKey,
                        StringComparison.Ordinal))
                {
                    error =
                        "Yeniden çözülen PartBody farklı ReferenceKey bildirdi " +
                        "(planlanan=" + applied.ReferenceKey +
                        ", mevcut=" + resolvedTarget.Identity.ReferenceKey + ").";
                    return false;
                }

                target = resolvedTarget;
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = "PartBody semantic yeniden çözümleme hatası: " + ComDiagnostic(ex);
                return false;
            }
        }

        private static bool TryValidateRenklendirme2Target(
            BulkPartBodyResolution resolution,
            out string error)
        {
            if (!resolution.Success || resolution.Target == null)
            {
                error = "PartBody hedefi çözümlenmemiş.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(resolution.Target.Identity.ReferenceKey) &&
                !string.Equals(
                    resolution.Item.ReferenceKey,
                    resolution.Target.Identity.ReferenceKey,
                    StringComparison.Ordinal))
            {
                error = "PartBody hedefinin ReferenceKey değeri planlanan referansla eşleşmiyor.";
                return false;
            }

            return TryValidateRenklendirme2Target(resolution.Target, out error);
        }

        private static bool TryValidateRenklendirme2Target(CatiaColorTarget target, out string error)
        {
            if (target.Identity.TargetType != CatiaColorTargetType.PartBody ||
                target.Identity.ComType.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = "Hedef PartBody/Body olarak doğrulanamadı (" + target.Identity.ComType + ").";
                return false;
            }

            try
            {
                string currentTargetKey = CatiaColorTargetService.GetSessionObjectKey(target.ComObject);
                if (!string.Equals(
                        currentTargetKey,
                        target.Identity.SessionObjectKey,
                        StringComparison.Ordinal))
                {
                    error = "PartBody COM identity değişti.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "PartBody COM identity doğrulanamadı: " + ComDiagnostic(ex);
                return false;
            }

            error = "";
            return true;
        }

        private static string Renklendirme2OperationError(CatiaColorOperationResult result) =>
            result.Exception == null
                ? result.Error
                : ComDiagnostic(result.Exception);

        private sealed class Renklendirme2ReadyTarget
        {
            public Renklendirme2ReadyTarget(
                BulkPartBodyResolution resolution,
                Renklendirme2RenkAtamasi assignment)
            {
                Resolution = resolution;
                Assignment = assignment;
            }

            public BulkPartBodyResolution Resolution { get; }
            public Renklendirme2RenkAtamasi Assignment { get; }
            public string ReferenceKey => Resolution.Item.ReferenceKey;
            public CatiaColorTarget Target => Resolution.Target!;
            public Renklendirme2Rengi Color => Assignment.Color;
        }

        private sealed class Renklendirme2AppliedTarget
        {
            public Renklendirme2AppliedTarget(
                string referenceKey,
                string referenceTitle,
                int occurrenceCount,
                object representativeOccurrence,
                string originalTargetComDiagnostic,
                int paletteIndex,
                Renklendirme2Rengi color)
            {
                ReferenceKey = referenceKey;
                ReferenceTitle = referenceTitle;
                OccurrenceCount = occurrenceCount;
                RepresentativeOccurrence = representativeOccurrence;
                OriginalTargetComDiagnostic = originalTargetComDiagnostic;
                PaletteIndex = paletteIndex;
                Color = color;
            }

            public string ReferenceKey { get; }
            public string ReferenceTitle { get; }
            public int OccurrenceCount { get; }
            public object RepresentativeOccurrence { get; }
            public string OriginalTargetComDiagnostic { get; }
            public int PaletteIndex { get; }
            public Renklendirme2Rengi Color { get; }
        }

        private sealed class Renklendirme2RootIdentity
        {
            public Renklendirme2RootIdentity(
                string referenceKey,
                string occurrenceKey,
                string rootPath,
                string comType)
            {
                ReferenceKey = referenceKey;
                OccurrenceKey = occurrenceKey;
                RootPath = rootPath;
                ComType = comType;
                SemanticKey =
                    "REFERENCE:" + (string.IsNullOrWhiteSpace(referenceKey) ? "(okunamadı)" : referenceKey) +
                    "|OCCURRENCE:" + (string.IsNullOrWhiteSpace(occurrenceKey) ? "(okunamadı)" : occurrenceKey) +
                    "|PATH:" + rootPath;
            }

            public string ReferenceKey { get; }
            public string OccurrenceKey { get; }
            public string RootPath { get; }
            public string ComType { get; }
            public string SemanticKey { get; }
        }

        private sealed class Renklendirme2RuntimeIdentity
        {
            public Renklendirme2RuntimeIdentity(
                string catiaSessionKey,
                string editorSessionKey,
                Renklendirme2RootIdentity root,
                string rootComDiagnostic)
            {
                CatiaSessionKey = catiaSessionKey;
                EditorSessionKey = editorSessionKey;
                Root = root;
                RootComDiagnostic = rootComDiagnostic;
            }

            public string CatiaSessionKey { get; }
            public string EditorSessionKey { get; }
            public Renklendirme2RootIdentity Root { get; }
            public string RootComDiagnostic { get; }
        }

        private sealed class Renklendirme2ResetState
        {
            private List<Renklendirme2AppliedTarget> _targets;

            public Renklendirme2ResetState(
                Renklendirme2RuntimeIdentity runtimeIdentity,
                IEnumerable<Renklendirme2AppliedTarget> targets)
            {
                RuntimeIdentity = runtimeIdentity;
                _targets = targets.ToList();
            }

            public Renklendirme2RuntimeIdentity RuntimeIdentity { get; }
            public IReadOnlyList<Renklendirme2AppliedTarget> Targets => _targets;

            public void ReplaceTargets(IEnumerable<Renklendirme2AppliedTarget> targets)
            {
                _targets = targets.ToList();
            }
        }
    }
}
