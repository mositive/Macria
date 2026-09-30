using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows;
using Macria;

internal static class Program
{
    private static int _assertions;
    private static string _directory = "";
    private static string _fixtureParent = "";

    [STAThread]
    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;
        DirectoryInfo? project = new DirectoryInfo(AppContext.BaseDirectory);
        while (project != null && !File.Exists(Path.Combine(project.FullName, "DxfEdit.Tests.csproj"))) project = project.Parent;
        if (project == null) throw new InvalidOperationException("Cannot locate the isolated test-project fixture root.");
        _fixtureParent = project.FullName;
        _directory = Path.Combine(_fixtureParent, "Macria-DxfEdit-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        try
        {
            MappingAndPreservation();
            DeleteUndoRedo();
            MultipleDeletesAndCheckpointDiscard();
            BranchAndForeignDocument();
            SaveAs();
            SaveAndBackups();
            SaveFailureSafety();
            ProtectedRecords();
            TwoDimensionalScope();
            NewlinePreservation();
            EmptyDrawing();
            InvalidFormats();
            PreviewContentStage();
            DxfBinarySignatureTests();
            DxfPreviewAdapterTests();
            DxfDwgPreviewMessageTests();
            AtomicSelectionDeletes();
            AnalyticContainment();
            ChamferGeometry();
            FilletGeometry();
            CornerTransactionsAndSave();
            ClosedContourCorners();
            InvalidContourAtomicity();
            HandleAndCornerSafety();
            JoinGeometryAndTransactions();
            ClickedCornerBranches();
            ContourDiscovery();
            SmartColoring();
            Coloring2Planning();
            Console.WriteLine($"PASS: {_assertions} DXF edit assertions. No interactive UI test was performed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_assertions} assertions: {ex}");
            return 1;
        }
        finally
        {
            // Only this run's unique, explicitly checked fixture directory is removed.
            string full = Path.GetFullPath(_directory);
            string expectedParent = Path.GetFullPath(_fixtureParent).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(full) == expectedParent &&
                Path.GetFileName(full).StartsWith("Macria-DxfEdit-Tests-", StringComparison.Ordinal))
            {
                try { Directory.Delete(full, true); }
                catch (Exception ex) { Console.Error.WriteLine($"Fixture cleanup failed: {ex.Message}"); }
            }
        }
    }

    private sealed class ColorNode
    {
        public string Key = "";
        public List<ColorNode> Children = new();
    }

    private static void SmartColoring()
    {
        string keyA = AkilliRenklendirmeMantigi.ReferansAnahtari("part-a", "A")!;
        string keyAAgain = AkilliRenklendirmeMantigi.ReferansAnahtari(" PART-A ", "a")!;
        string keyB = AkilliRenklendirmeMantigi.ReferansAnahtari("part-b", "A")!;
        AkilliRenk colorA = AkilliRenklendirmeMantigi.RenkOlustur(keyA);
        AkilliRenk colorAAgain = AkilliRenklendirmeMantigi.RenkOlustur(keyAAgain);
        AkilliRenk colorB = AkilliRenklendirmeMantigi.RenkOlustur(keyB);
        Check(colorA.Kirmizi == colorAAgain.Kirmizi && colorA.Yesil == colorAAgain.Yesil && colorA.Mavi == colorAAgain.Mavi,
            "smart color: same reference gets same stable color");
        Check(colorA.Kirmizi != colorB.Kirmizi || colorA.Yesil != colorB.Yesil || colorA.Mavi != colorB.Mavi,
            "smart color: sample distinct references get distinct colors");
        Check(AkilliRenklendirmeMantigi.ReferansAnahtari("", "A") == null,
            "smart color: missing PLM identity is not replaced by a name");
        Check(colorA.Kirmizi is >= 40 and <= 240 && colorA.Yesil is >= 40 and <= 240 && colorA.Mavi is >= 40 and <= 240,
            "smart color: generated color avoids extreme channels");

        var one = new ColorNode { Key = keyA };
        var two = new ColorNode { Key = keyB };
        var three = new ColorNode { Key = keyA };
        var missing = new ColorNode();
        var assembly = new ColorNode { Children = { one, two } };
        var root = new ColorNode { Children = { assembly, three, missing } };
        List<ColorNode> leaves = AkilliRenklendirmeMantigi.YapraklariBul(
            new[] { root }, node => node.Children);
        Check(leaves.SequenceEqual(new[] { one, two, three, missing }),
            "smart color: nested assemblies traverse to leaf occurrences");

        var gate = new AkilliRenklendirmeKilidi();
        Check(gate.Baslat() && gate.Calisiyor && !gate.Baslat(),
            "smart color: concurrent second run is rejected");
        gate.Bitir();
        Check(!gate.Calisiyor && gate.Baslat(), "smart color: run lock is released for another run");
        gate.Bitir();
    }

    private static void Coloring2Planning()
    {
        IReadOnlyList<Renklendirme2Rengi> palette = Renklendirme2Paleti.Renkler;
        IReadOnlyList<Renklendirme2Rengi> regenerated = Renklendirme2Paleti.OlusturBaslangicPaleti();
        Check(palette.Count >= 60 && palette.Count == Renklendirme2Paleti.BaslangicRenkSayisi,
            "coloring 2: deterministic initial palette contains at least 60 colors");
        Check(palette.SequenceEqual(regenerated),
            "coloring 2: palette generation is deterministic");
        Check(palette.Distinct().Count() == palette.Count &&
              palette.All(color => color.Hex == $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}"),
            "coloring 2: palette RGB and HEX values are unique and consistent");
        Check(palette.All(color =>
        {
            double luminance = 0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue;
            int spread = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) -
                         Math.Min(color.Red, Math.Min(color.Green, color.Blue));
            return luminance >= 58 && luminance <= 210 && spread >= 72;
        }), "coloring 2: palette avoids near-black, near-white and low-saturation colors");

        string[] keys = Enumerable.Range(1, 60).Select(index => $"PLM:PART-{index:000}|VERSION:A").ToArray();
        Renklendirme2RenkPlani first = Renklendirme2RenkPlani.Olustur(keys.Concat(new[] { keys[0] }));
        Renklendirme2RenkPlani reversed = Renklendirme2RenkPlani.Olustur(keys.Reverse());
        Check(first.Assignments.Count == 60 && first.OverflowReferenceKeys.Count == 0,
            "coloring 2: duplicate reference keys collapse to one assignment");
        Check(first.Assignments.Values.Select(item => item.Color).Distinct().Count() == 60,
            "coloring 2: distinct references receive distinct colors within palette capacity");
        Check(keys.All(key => first.Assignments[key].Color.Equals(reversed.Assignments[key].Color)),
            "coloring 2: traversal order does not change reference colors");
        Check(first.Assignments[keys[0]].Color.Equals(
                  Renklendirme2RenkPlani.Olustur(keys).Assignments[keys[0]].Color),
            "coloring 2: same reference set gives the same reference color");

        string[] overflowKeys = Enumerable.Range(1, palette.Count + 2)
            .Select(index => $"PLM:OVERFLOW-{index:000}|VERSION:A")
            .ToArray();
        Renklendirme2RenkPlani overflow = Renklendirme2RenkPlani.Olustur(overflowKeys);
        Check(overflow.Assignments.Count == palette.Count && overflow.OverflowReferenceKeys.Count == 2,
            "coloring 2: palette overflow is skipped instead of reusing a color");

        Renklendirme2RenkPlani cycleOne = Renklendirme2RenkPlani.Olustur(keys, 1);
        Renklendirme2RenkPlani cycleOneAgain = Renklendirme2RenkPlani.Olustur(keys.Reverse(), 1);
        Check(keys.All(key => cycleOne.Assignments[key].Color.Equals(
                  cycleOneAgain.Assignments[key].Color)),
            "coloring 2 UI: the same cycle and reference set gives the same mapping");
        Check(keys.All(key => !cycleOne.Assignments[key].Color.Equals(first.Assignments[key].Color)),
            "coloring 2 UI: advancing the cycle rotates every assigned color");
        Check(cycleOne.Assignments.Values.Select(item => item.Color).Distinct().Count() == keys.Length,
            "coloring 2 UI: a cycle does not duplicate colors within palette capacity");

        var ui = new Renklendirme2UiSatiri(keys[0], "Test parçası", 3);
        Check(ui.Included && ui.IsAutomatic && ui.StatusText == "Automatic" && ui.Hex.Length == 0,
            "coloring 2 UI: colorable rows start included and Automatic without fake black RGB");
        ui.Included = false;
        Check(!ui.Included, "coloring 2 UI: a unique reference can be excluded");
        ui.MarkAssigned(cycleOne.Assignments[keys[0]]);
        Check(ui.IsAssigned && ui.Hex == cycleOne.Assignments[keys[0]].Color.Hex &&
              ui.ColorToolTip.Contains("RGB:", StringComparison.Ordinal),
            "coloring 2 UI: ReferenceKey assignment updates swatch metadata");
        ui.MarkAutomatic();
        Check(ui.IsAutomatic && ui.PaletteIndex == null && ui.Hex.Length == 0,
            "coloring 2 UI: Automatic clears assigned palette and RGB state");

        var uncolorable = new Renklendirme2UiSatiri("", "Kimliksiz", 1);
        uncolorable.Included = true;
        Check(!uncolorable.IsColorable && !uncolorable.Included,
            "coloring 2 UI: a row without stable ReferenceKey cannot be included");
    }

    private static void MappingAndPreservation()
    {
        var (path, model, bytes) = Fixture("mapping");
        Check(model.KaynakBelge != null && model.DuzenlemeEngeli == null, "valid ASCII source is editable");
        Check(model.Entityler.Count(e => e.KaynakKayit != null) == 4, "four root LINE/CIRCLE/ARC records mapped");
        Check(model.Entityler.Count(e => e.KaynakKayit == null) == 2, "two INSERT-derived lines are not editable");
        Check(model.Entityler.Any(e => e.Tip == "LINE" && e.KaynakKayit == null), "block preview survives");
        Check(model.Yollar.Count > model.Entityler.Count, "unsupported preview paths remain distinct");
        foreach (DxfEntity entity in model.Entityler.Where(e => e.KaynakKayit != null))
        {
            var record = entity.KaynakKayit!;
            string raw = Encoding.Latin1.GetString(bytes, record.Baslangic, record.Bitis - record.Baslangic);
            Check(raw.StartsWith("0\r\n" + entity.Tip + "\r\n", StringComparison.Ordinal), "range begins at real record");
            Check(raw.Contains("5\r\n" + record.Handle + "\r\n", StringComparison.Ordinal), "range handle maps to entity");
            Check(raw.Contains("8\r\nPRESERVE_LAYER\r\n", StringComparison.Ordinal), "common layer code retained");
            Check(ReferenceEquals(model.Yollar.First(p => ReferenceEquals(p, entity.Noktalar)), entity.Noktalar), "entity shares its preview path");
        }
        var session = new DxfEditOturumu(model, path);
        Check(session.Engel == null, "valid session not blocked");
        Bytes(session.Cikti(), bytes, "no edit reproduces original bytes exactly");
        Check(!session.Degisti, "initial clean state");
    }

    private static void DeleteUndoRedo()
    {
        foreach (string type in new[] { "LINE", "CIRCLE", "ARC" })
        {
            var (path, model, bytes) = Fixture("delete-" + type);
            DxfEntity entity = model.Entityler.First(e => e.Tip == type && e.KaynakKayit != null);
            DxfKaynakKayit record = entity.KaynakKayit!;
            var points = entity.Noktalar;
            var session = new DxfEditOturumu(model, path);
            Check(session.Sil(entity, out string? error) && error == null, type + " deletion accepted");
            Check(session.Degisti && session.GeriAlabilir && !session.Yineleabilir, "delete changes state/history");
            Check(!session.Onizleme().Entityler.Contains(entity), "deleted entity absent from preview");
            Check(!session.Onizleme().Yollar.Any(p => ReferenceEquals(p, points)), "deleted path absent from preview");
            Bytes(session.Cikti(), Remove(bytes, record), "only selected raw record removed");
            Bytes(File.ReadAllBytes(path), bytes, "delete performs no disk write");
            session.GeriAl();
            Check(!session.Degisti && session.Yineleabilir, "undo returns to clean original");
            DxfEntity restored = session.Onizleme().Entityler.First(e => ReferenceEquals(e, entity));
            Check(ReferenceEquals(restored.KaynakKayit, record) && ReferenceEquals(restored.Noktalar, points), "undo restores same identity/source/geometry");
            Bytes(session.Cikti(), bytes, "undo restores original bytes");
            session.Yinele();
            Check(session.Degisti && !session.Onizleme().Entityler.Contains(entity), "redo removes original entity");
            Bytes(session.Cikti(), Remove(bytes, record), "redo restores identical patch");
            session.Vazgec();
            Check(!session.Degisti, "discard resets unsaved changes");
            Bytes(File.ReadAllBytes(path), bytes, "undo redo discard perform no disk write");
        }
    }

    private static void BranchAndForeignDocument()
    {
        var (path, model, _) = Fixture("branch");
        var session = new DxfEditOturumu(model, path);
        var first = ByHandle(model, "C1");
        var next = ByHandle(model, "D1");
        Check(session.Sil(first, out _), "branch first delete");
        session.GeriAl();
        Check(session.Yineleabilir, "undo exposes redo");
        Check(session.Sil(next, out _), "branch replacement delete");
        Check(!session.Yineleabilir, "new edit invalidates redo branch");
        var (_, foreign, _) = Fixture("foreign");
        byte[] before = session.Cikti();
        Check(!session.Sil(ByHandle(foreign, "C1"), out string? foreignError) && !string.IsNullOrWhiteSpace(foreignError), "foreign-document entity rejected");
        Bytes(session.Cikti(), before, "foreign entity cannot mutate current document");
        Check(!session.Sil(next, out _), "already deleted entity rejected");
        Check(!session.Sil(model.Entityler.First(e => e.KaynakKayit == null), out _), "INSERT-derived entity deletion rejected");
    }

    private static void MultipleDeletesAndCheckpointDiscard()
    {
        var (path, model, original) = Fixture("multiple-deletes");
        var session = new DxfEditOturumu(model, path);
        var circle = ByHandle(model, "C1");
        var arc = ByHandle(model, "D1");
        var records = new[] { circle.KaynakKayit!, arc.KaynakKayit! };
        int entitiesBefore = model.Entityler.Count;
        Check(session.Sil(arc, out _) && session.Sil(circle, out _), "out-of-order multiple deletes accepted");
        byte[] expected = original.Where((_, i) => !records.Any(r => i >= r.Baslangic && i < r.Bitis)).ToArray();
        Bytes(session.Cikti(), expected, "multiple deletions patch sorted original ranges");
        Check(model.Entityler.Count == entitiesBefore && model.Entityler.Contains(circle) && model.Entityler.Contains(arc), "shared original model is never mutated");
        DxfCizim preview = session.Onizleme();
        Check(preview.Entityler.Count == entitiesBefore - 2, "filtered preview reflects both edits");
        Check(preview.NesneSayisi == model.NesneSayisi - 2, "preview count decreases only supported deletions");
        string saved = Path.Combine(_directory, "multiple-checkpoint.dxf");
        session.FarkliKaydet(saved);
        Check(!session.Degisti, "multiple-delete Save As checkpoint");
        session.GeriAl();
        Check(session.Degisti && session.Onizleme().Entityler.Contains(circle), "undo changes saved checkpoint");
        session.Vazgec();
        Check(!session.Degisti && !session.GeriAlabilir && !session.Yineleabilir, "discard restores checkpoint and clears history");
        Bytes(session.Cikti(), expected, "discard retains saved deletions rather than resurrecting originals");
        Bytes(File.ReadAllBytes(path), original, "multiple edits and Save As leave original file untouched");
    }

    private static void SaveAs()
    {
        var (path, model, bytes) = Fixture("save-as");
        var entity = ByHandle(model, "C1");
        var session = new DxfEditOturumu(model, path);
        Check(session.Sil(entity, out _), "save-as delete");
        string output = Path.Combine(_directory, "save-as-output.dxf");
        session.FarkliKaydet(output);
        byte[] expected = Remove(bytes, entity.KaynakKayit!);
        Bytes(File.ReadAllBytes(path), bytes, "Save As leaves original untouched");
        Bytes(File.ReadAllBytes(output), expected, "Save As writes only deletion patch");
        Check(Path.GetFullPath(session.Yol) == Path.GetFullPath(output) && !session.Degisti, "Save As adopts new path/checkpoint");
        DxfCizim reopened = Read(output);
        Check(!reopened.Entityler.Any(e => e.KaynakKayit?.Handle == "C1"), "reopened save-as excludes deleted record");
        Check(reopened.Entityler.Count(e => e.KaynakKayit == null) == 2, "Save As preserves both INSERT instances");
        session.GeriAl();
        Check(session.Degisti, "undo after Save As is dirty relative to checkpoint");
        Bytes(session.Cikti(), bytes, "undo after Save As restores same original record");
        string backup = session.Kaydet(true);
        Bytes(File.ReadAllBytes(output), bytes, "undo plus Save restores all original bytes");
        Bytes(File.ReadAllBytes(backup), expected, "backup contains previous Save As state");
        Bytes(File.ReadAllBytes(path), bytes, "saving adopted path still leaves original untouched");

        var (otherPath, otherModel, _) = Fixture("save-as-collision");
        var other = new DxfEditOturumu(otherModel, otherPath);
        string collision = Path.Combine(_directory, "existing-target.dxf");
        byte[] sentinel = Encoding.ASCII.GetBytes("DO NOT OVERWRITE");
        File.WriteAllBytes(collision, sentinel);
        Throws(() => other.FarkliKaydet(collision), "Save As refuses existing path without overwrite consent");
        Bytes(File.ReadAllBytes(collision), sentinel, "Save As collision preserves existing file");
    }

    private static void SaveAndBackups()
    {
        var (path, model, bytes) = Fixture("save");
        var session = new DxfEditOturumu(model, path);
        var circle = ByHandle(model, "C1");
        Check(session.Sil(circle, out _), "save delete");
        Throws(() => session.Kaydet(false), "overwrite requires explicit consent");
        Bytes(File.ReadAllBytes(path), bytes, "denied overwrite cannot change original");
        Check(Directory.GetFiles(_directory, "save.dxf*.bak").Length == 0, "denied overwrite creates no backup");
        string backup1 = session.Kaydet(true);
        byte[] afterCircle = Remove(bytes, circle.KaynakKayit!);
        Check(File.Exists(backup1) && Path.GetDirectoryName(backup1) == Path.GetDirectoryName(path), "backup exists beside DXF");
        Bytes(File.ReadAllBytes(backup1), bytes, "backup preserves original exactly");
        Bytes(File.ReadAllBytes(path), afterCircle, "Save writes patched source");
        Check(!session.Degisti, "Save establishes clean checkpoint");
        session.GeriAl();
        Check(session.Degisti, "undo saved delete becomes dirty");
        string backup2 = session.Kaydet(true);
        Check(backup1 != backup2, "backup names do not collide");
        Bytes(File.ReadAllBytes(backup1), bytes, "first backup never overwritten");
        Bytes(File.ReadAllBytes(backup2), afterCircle, "second backup captures actual disk state");
        Bytes(File.ReadAllBytes(path), bytes, "undo plus Save restores exact deleted source record");
        Check(Read(path).Entityler.Any(e => e.KaynakKayit?.Handle == "C1"), "restored record reopens");
    }

    private static void SaveFailureSafety()
    {
        var (path, model, bytes) = Fixture("external-change");
        var session = new DxfEditOturumu(model, path);
        Check(session.Sil(ByHandle(model, "A1"), out _), "external change pending deletion");
        byte[] changed = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(bytes).Replace("PRESERVED_COMMENT", "EXTERNALLY_CHANGED"));
        File.WriteAllBytes(path, changed);
        Throws(() => session.Kaydet(true), "external disk changes block overwrite");
        Bytes(File.ReadAllBytes(path), changed, "external bytes left untouched");
        Check(session.Degisti, "failed Save retains dirty state");

        var (missingPath, missingModel, missingBytes) = Fixture("missing-target");
        var missing = new DxfEditOturumu(missingModel, missingPath);
        Check(missing.Sil(ByHandle(missingModel, "C1"), out _), "missing target pending deletion");
        File.Delete(missingPath);
        Throws(() => missing.Kaydet(true), "missing original cannot be silently recreated without backup");
        Check(!File.Exists(missingPath) && missing.Degisti, "missing original remains absent and edits preserved");
        Bytes(missing.Cikti(), Remove(missingBytes, ByHandle(missingModel, "C1").KaynakKayit!), "failed Save keeps output recoverable");

        var (lockedPath, lockedModel, lockedBytes) = Fixture("locked-target");
        var locked = new DxfEditOturumu(lockedModel, lockedPath);
        Check(locked.Sil(ByHandle(lockedModel, "D1"), out _), "locked target pending deletion");
        using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Throws(() => locked.Kaydet(true), "backup/source read lock rejects Save safely");
        }
        Bytes(File.ReadAllBytes(lockedPath), lockedBytes, "backup/source read failure leaves original unchanged");
        Check(locked.Degisti, "backup failure retains dirty state");

        var (readOnlyPath, readOnlyModel, readOnlyBytes) = Fixture("readonly-target");
        var readOnly = new DxfEditOturumu(readOnlyModel, readOnlyPath);
        Check(readOnly.Sil(ByHandle(readOnlyModel, "C1"), out _), "readonly target pending deletion");
        FileAttributes attributes = File.GetAttributes(readOnlyPath);
        try
        {
            File.SetAttributes(readOnlyPath, attributes | FileAttributes.ReadOnly);
            Throws(() => readOnly.Kaydet(true), "read-only original blocks replacement");
            Bytes(File.ReadAllBytes(readOnlyPath), readOnlyBytes, "failed replacement preserves original bytes");
            Check(readOnly.Degisti, "failed replacement retains dirty state");
            Check(!Directory.GetFiles(_directory, ".macria-dxf-*.tmp").Any(), "failed Save removes only its temporary output");
        }
        finally { File.SetAttributes(readOnlyPath, attributes); }

        // Deny creation only inside one generated fixture folder; restore its ACL in finally.
        // This exercises a real backup-creation failure rather than a source-read failure.
        string deniedDirectory = Path.Combine(_directory, "backup-denied");
        Directory.CreateDirectory(deniedDirectory);
        string deniedPath = Path.Combine(deniedDirectory, "backup-source.dxf");
        byte[] deniedBytes = Encoding.Latin1.GetBytes(FixtureText());
        File.WriteAllBytes(deniedPath, deniedBytes);
        DxfCizim deniedModel = Read(deniedPath);
        var denied = new DxfEditOturumu(deniedModel, deniedPath);
        Check(denied.Sil(ByHandle(deniedModel, "C1"), out _), "backup-denied pending deletion");
        var deniedInfo = new DirectoryInfo(deniedDirectory);
        DirectorySecurity initialAcl = deniedInfo.GetAccessControl();
        DirectorySecurity deniedAcl = deniedInfo.GetAccessControl();
        deniedAcl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            deniedInfo.SetAccessControl(deniedAcl);
            Throws(() => denied.Kaydet(true), "backup creation denied prevents overwrite");
            Bytes(File.ReadAllBytes(deniedPath), deniedBytes, "backup creation failure leaves original exact bytes");
            Check(denied.Degisti, "backup creation failure preserves edits");
            Check(Directory.GetFiles(deniedDirectory).Length == 1, "backup creation failure creates no backup/temp output");
        }
        finally { deniedInfo.SetAccessControl(initialAcl); }
    }

    private static void ProtectedRecords()
    {
        string incoming = FixtureText(extraObjects: Pair(0, "DICTIONARY") + Pair(5, "F1") + Pair(3, "REFERENCED_CIRCLE") + Pair(360, "C1"));
        var (incomingPath, incomingModel, incomingBytes) = Fixture("incoming-reference", incoming);
        var incomingSession = new DxfEditOturumu(incomingModel, incomingPath);
        Check(!incomingSession.Sil(ByHandle(incomingModel, "C1"), out string? incomingError) && !string.IsNullOrWhiteSpace(incomingError), "incoming handle reference blocks deletion");
        Bytes(incomingSession.Cikti(), incomingBytes, "protected reference cannot remove record");

        string duplicates = FixtureText().Replace(Pair(5, "A2"), Pair(5, "C1"), StringComparison.Ordinal);
        var (duplicatePath, duplicateModel, duplicateBytes) = Fixture("duplicate-handle", duplicates);
        var duplicateSession = new DxfEditOturumu(duplicateModel, duplicatePath);
        foreach (DxfEntity entity in duplicateModel.Entityler.Where(e => e.KaynakKayit?.Handle == "C1"))
            Check(!duplicateSession.Sil(entity, out _), "duplicate handle blocks unsafe deletion");
        Bytes(duplicateSession.Cikti(), duplicateBytes, "duplicate handle rejection preserves source");

        string normalizedDuplicate = FixtureText().Replace(Pair(5, "A2"), Pair(5, "000c1"), StringComparison.Ordinal);
        var (normalizedPath, normalizedModel, normalizedBytes) = Fixture("normalized-handle", normalizedDuplicate);
        var normalized = new DxfEditOturumu(normalizedModel, normalizedPath);
        Check(!normalized.Sil(ByHandle(normalizedModel, "C1"), out _), "zero-padded lowercase handle duplicates are recognized");
        Check(!normalized.Sil(ByHandle(normalizedModel, "000c1"), out _), "both normalized duplicate records protected");
        Bytes(normalized.Cikti(), normalizedBytes, "normalized duplicate rejection preserves source");

        string normalizedReference = FixtureText(extraObjects: Pair(0, "DICTIONARY") + Pair(5, "F1") + Pair(3, "TARGET") + Pair(360, "000c1"));
        var (normalizedRefPath, normalizedRefModel, normalizedRefBytes) = Fixture("normalized-reference", normalizedReference);
        var normalizedRef = new DxfEditOturumu(normalizedRefModel, normalizedRefPath);
        Check(!normalizedRef.Sil(ByHandle(normalizedRefModel, "C1"), out _), "zero-padded handle reference recognized");
        Bytes(normalizedRef.Cikti(), normalizedRefBytes, "normalized reference rejection preserves source");

        foreach (int code in new[] { 330, 340 })
        {
            string header = FixtureText().Replace(Pair(9, "$HANDSEED") + Pair(5, "FFFF"),
                Pair(9, "$HANDSEED") + Pair(5, "FFFF") + Pair(9, "$TARGET_REFERENCE") + Pair(code, "000c1"), StringComparison.Ordinal);
            var (headerPath, headerModel, headerBytes) = Fixture("header-reference-" + code, header);
            var headerSession = new DxfEditOturumu(headerModel, headerPath);
            Check(!headerSession.Sil(ByHandle(headerModel, "C1"), out _), "HEADER " + code + " pointer blocks referenced deletion");
            Bytes(headerSession.Cikti(), headerBytes, "header pointer preservation");
        }
    }

    private static void TwoDimensionalScope()
    {
        string circle = Pair(0, "CIRCLE") + Pair(5, "C1");
        foreach ((string name, string extra) in new[]
        {
            ("elevation", Pair(38, "3")),
            ("thickness", Pair(39, "2")),
            ("ocs-x", Pair(210, "1") + Pair(220, "0") + Pair(230, "0")),
            ("ocs-inverse-z", Pair(210, "0") + Pair(220, "0") + Pair(230, "-1")),
            ("invalid-elevation", Pair(30, "NaN"))
        })
        {
            string source = FixtureText().Replace(circle, circle + extra, StringComparison.Ordinal);
            var (path, model, bytes) = Fixture("scope-" + name, source);
            var session = new DxfEditOturumu(model, path);
            Check(!session.Sil(ByHandle(model, "C1"), out string? error) && !string.IsNullOrWhiteSpace(error), "2D edit scope rejects " + name);
            Bytes(session.Cikti(), bytes, "out-of-scope geometry bytes preserved");
        }
        string defaultOcs = FixtureText().Replace(circle, circle + Pair(210, "0") + Pair(220, "0") + Pair(230, "1"), StringComparison.Ordinal);
        var (defaultPath, defaultModel, _) = Fixture("default-ocs", defaultOcs);
        Check(new DxfEditOturumu(defaultModel, defaultPath).Sil(ByHandle(defaultModel, "C1"), out _), "explicit default OCS remains editable");
        string threeDLine = FixtureText().Replace(Pair(31, "0.0"), Pair(31, "2.0"), StringComparison.Ordinal);
        var (threeDPath, threeDModel, _) = Fixture("3d-line", threeDLine);
        Check(!new DxfEditOturumu(threeDModel, threeDPath).Sil(ByHandle(threeDModel, "A1"), out _), "LINE endpoint Z rejects projected 3D edit");
    }

    private static void NewlinePreservation()
    {
        string original = FixtureText();
        foreach ((string name, string text) in new[]
        {
            ("mixed", original),
            ("lf", original.Replace("\r\n", "\n", StringComparison.Ordinal)),
            ("cr", original.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r", StringComparison.Ordinal)),
            ("no-final-newline", original.TrimEnd('\r', '\n'))
        })
        {
            var (path, model, bytes) = Fixture("newline-" + name, text);
            var circle = ByHandle(model, "C1");
            var session = new DxfEditOturumu(model, path);
            Check(session.Sil(circle, out _), "delete works with " + name + " line endings");
            Bytes(session.Cikti(), Remove(bytes, circle.KaynakKayit!), "raw newline and non-ASCII payload preserved: " + name);
            session.GeriAl();
            Bytes(session.Cikti(), bytes, "undo keeps exact newline bytes: " + name);
        }
    }

    private static void EmptyDrawing()
    {
        string header = Pair(0, "SECTION") + Pair(2, "HEADER") + Pair(9, "$ACADVER") + Pair(1, "AC1027") + Pair(0, "ENDSEC");
        string line = Pair(0, "LINE") + Pair(5, "A1") + Pair(10, "0") + Pair(20, "0") + Pair(11, "100") + Pair(21, "0");
        string source = header + Pair(0, "SECTION") + Pair(2, "ENTITIES") + line + Pair(0, "ENDSEC") + Pair(0, "EOF");
        var (path, model, _) = Fixture("empty-drawing", source);
        var session = new DxfEditOturumu(model, path);
        Check(session.Sil(ByHandle(model, "A1"), out _), "last supported entity may be deleted");
        Check(session.Onizleme().Bos && session.Onizleme().Entityler.Count == 0 && session.Onizleme().NesneSayisi == 0, "empty in-memory preview safe");
        string output = Path.Combine(_directory, "empty-output.dxf");
        session.FarkliKaydet(output);
        DxfCizim? reopened = DxfOkuyucu.Oku(output, out _);
        Check(reopened != null && reopened.Bos && reopened.KaynakBelge != null, "empty saved ENTITIES remains valid ASCII DXF");
        session.GeriAl();
        Check(!session.Onizleme().Bos, "undo from empty preview restores geometry");
    }

    private static void InvalidFormats()
    {
        string binaryPath = Path.Combine(_directory, "binary.dxf");
        byte[] binary = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0\0\0");
        File.WriteAllBytes(binaryPath, binary);
        DxfCizim? binaryModel = DxfOkuyucu.Oku(binaryPath, out string? binaryError);
        Check(binaryModel == null && !string.IsNullOrWhiteSpace(binaryError), "binary DXF explicitly rejected");
        Bytes(File.ReadAllBytes(binaryPath), binary, "binary rejection cannot write file");

        string malformed = FixtureText().Replace(Pair(999, "PRESERVED_COMMENT"), "bad-code\r\nPRESERVED_COMMENT\r\n", StringComparison.Ordinal);
        var (malformedPath, malformedModel, malformedBytes) = Fixture("malformed", malformed);
        Check(!malformedModel.Bos, "tolerant malformed preview remains available");
        Check(malformedModel.KaynakBelge == null || !string.IsNullOrWhiteSpace(malformedModel.DuzenlemeEngeli), "malformed DXF edit is blocked");
        var malformedSession = new DxfEditOturumu(malformedModel, malformedPath);
        Check(!string.IsNullOrWhiteSpace(malformedSession.Engel), "malformed edit reason available");
        Check(!malformedSession.Sil(malformedModel.Entityler.First(), out _), "malformed document cannot delete");
        Bytes(File.ReadAllBytes(malformedPath), malformedBytes, "malformed rejection leaves source untouched");
    }

    // DXF preview is two-stage today, as in the Dosya Analiz Merkezi panel: PreviewCoordinator
    // decides from the extension, then DxfOkuyucu decides from the content. Neither stage may
    // throw or write to the source. The messages below are shown to the user as-is.
    private static void PreviewContentStage()
    {
        const string NotFound = "Dosya bulunamadı.";
        const string Binary = "İkili (binary) DXF önizlenemiyor.";
        const string NothingDrawable = "Dosyada çizilebilir bir nesne bulunamadı.";
        var coordinator = new PreviewCoordinator();
        PreviewResult Resolve(string path, PreviewCapability capability, PreviewPresentation? presentation) =>
            coordinator.Resolve(new PreviewRequest { SourcePath = path, Capability = capability, Presentation = presentation });

        string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(_directory, "preview-stage-" + name + ".dxf");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        // ASCII DXF: preview available, edit opens only after content validation.
        byte[] asciiBytes = Encoding.Latin1.GetBytes(FixtureText());
        string ascii = Write("ascii", asciiBytes);
        foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
            Check(Resolve(ascii, PreviewCapability.Preview2D, presentation).IsReady, $"ASCII DXF {presentation} preview request is ready");
        PreviewResult asciiEdit = Resolve(ascii, PreviewCapability.Edit, null);
        Check(asciiEdit.Status == PreviewResultStatus.RequiresContentValidation, "ASCII DXF edit waits for content validation");
        DxfCizim? asciiModel = DxfOkuyucu.Oku(Resolve(ascii, PreviewCapability.Preview2D, PreviewPresentation.Embedded).NormalizedPath!,
            out string? asciiError);
        Check(asciiModel != null && !asciiModel.Bos && asciiError == null && asciiModel.KaynakBelge != null,
            "ASCII DXF content produces a preview model with source records");
        Check(new DxfEditOturumu(asciiModel!, ascii).Engel == null, "valid ASCII DXF passes edit content validation");
        Bytes(File.ReadAllBytes(ascii), asciiBytes, "ASCII preview does not write the source");

        // Binary DXF: the extension stage cannot tell, the content stage rejects it.
        byte[] binaryBytes = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0\0\0");
        string binary = Write("binary", binaryBytes);
        Check(Resolve(binary, PreviewCapability.Preview2D, PreviewPresentation.Embedded).IsReady,
            "extension stage does not inspect binary DXF content");
        DxfCizim? binaryModel = DxfOkuyucu.Oku(binary, out string? binaryError);
        Check(binaryModel == null && binaryError == Binary, "binary DXF content is a controlled preview error");
        Bytes(File.ReadAllBytes(binary), binaryBytes, "binary preview does not write the source");

        // Invalid content with a DXF extension: controlled "nothing drawable", edit blocked.
        var invalid = new (string Name, byte[] Bytes)[]
        {
            ("plain-text", Encoding.ASCII.GetBytes("test dxf placeholder")),
            ("zero-bytes", Array.Empty<byte>()),
            ("dwg-signature", Encoding.ASCII.GetBytes("AC1032\0\0\0\0\0\u0001\u0002\u0003binary")),
        };
        foreach (var (name, bytes) in invalid)
        {
            string path = Write(name, bytes);
            Check(Resolve(path, PreviewCapability.Preview2D, PreviewPresentation.Embedded).IsReady,
                $"extension stage accepts {name} as DXF");
            DxfCizim? model = DxfOkuyucu.Oku(path, out string? error);
            Check(model != null && model.Bos && error == NothingDrawable, $"{name} content is a controlled empty preview");
            Check(!string.IsNullOrWhiteSpace(new DxfEditOturumu(model!, path).Engel), $"{name} content blocks edit with a reason");
            Bytes(File.ReadAllBytes(path), bytes, $"{name} preview does not write the source");
        }

        // Valid structure without drawable entities: empty preview with the same message.
        string noEntities = Write("no-entities", Encoding.Latin1.GetBytes(
            Pair(0, "SECTION") + Pair(2, "ENTITIES") + Pair(0, "ENDSEC") + Pair(0, "EOF")));
        DxfCizim? emptyModel = DxfOkuyucu.Oku(noEntities, out string? emptyError);
        Check(emptyModel != null && emptyModel.Bos && emptyError == NothingDrawable, "DXF without entities is a controlled empty preview");
        // User decision: an empty preview is not an edit ban. Binary, malformed and missing files stay closed
        // (binary/missing have no model above; malformed is pinned in InvalidFormats).
        Check(emptyModel!.KaynakBelge != null && new DxfEditOturumu(emptyModel, noEntities).Engel == null,
            "structurally valid DXF without entities stays editable");

        // Missing file: the extension stage stops first; the reader alone is also controlled.
        string missing = Path.Combine(_directory, "preview-stage-missing.dxf");
        PreviewResult missingResult = Resolve(missing, PreviewCapability.Preview2D, PreviewPresentation.Embedded);
        Check(missingResult.Status == PreviewResultStatus.MissingFile && missingResult.ContentType == PreviewContentType.Dxf,
            "missing DXF stops at the extension stage");
        Check(DxfOkuyucu.Oku(missing, out string? missingError) == null && missingError == NotFound,
            "reader reports a missing DXF without throwing");
        Check(!File.Exists(missing), "preview never creates a missing source");
    }

    private static void DxfBinarySignatureTests()
    {
        byte[] signature = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0");
        Check(DxfBinarySignature.Length == 22 && DxfBinarySignature.Bytes.SequenceEqual(signature), "binary DXF signature is the 22-byte sentinel");
        Check(DxfBinarySignature.Matches(signature), "exact signature matches");
        Check(DxfBinarySignature.Matches(signature.Concat(new byte[] { 0, 1, 2, 3 }).ToArray()), "signature followed by binary data matches");
        Check(!DxfBinarySignature.Matches(signature[..21]), "truncated signature does not match");
        Check(!DxfBinarySignature.Matches(ReadOnlySpan<byte>.Empty), "empty header does not match");
        Check(!DxfBinarySignature.Matches(Encoding.ASCII.GetBytes("AutoCAD Binary DXF\n\u001a\0")), "LF instead of CRLF does not match");
        Check(!DxfBinarySignature.Matches(Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n")), "text first line alone does not match");
        Check(!DxfBinarySignature.Matches(Encoding.ASCII.GetBytes("autocad binary dxf\r\n\u001a\0")), "comparison is byte-exact, not case-insensitive");
        Check(!DxfBinarySignature.Matches(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(signature).ToArray()), "signature must start at byte 0");
        Check(!DxfBinarySignature.Matches(Encoding.ASCII.GetBytes(Pair(0, "SECTION"))), "ASCII DXF start does not match");
    }

    // Stage 4: read-only DXF 2D preview adapter. Rejected and failed content never returns a model.
    private static void DxfPreviewAdapterTests()
    {
        var adapter = new DxfPreviewAdapter();
        PreviewRequest Request(string? path, PreviewCapability capability = PreviewCapability.Preview2D,
            PreviewPresentation? presentation = PreviewPresentation.Embedded) =>
            new() { SourcePath = path, Capability = capability, Presentation = presentation };
        string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(_directory, "preview-adapter-" + name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        void Invariants(DxfPreviewReadResult result, string name)
        {
            Check((result.Model != null) == result.Content.IsAccepted, name + ": model is returned exactly when content is accepted");
            Check(result.Content.Status == PreviewContentCheckStatus.NotChecked || result.Preview.NormalizedPath != null,
                name + ": checked content always has a resolved path");
        }

        // Accepted: drawable ASCII DXF, embedded and large; the tolerant malformed preview stays available.
        byte[] asciiBytes = Encoding.Latin1.GetBytes(FixtureText());
        string ascii = Write("ascii.dxf", asciiBytes);
        foreach (PreviewPresentation presentation in Enum.GetValues<PreviewPresentation>())
        {
            DxfPreviewReadResult accepted = adapter.Read(Request(ascii, presentation: presentation));
            Check(accepted.Preview.IsReady && accepted.Content == PreviewContentCheckResult.Accepted() &&
                  accepted.Model != null && !accepted.Model.Bos && accepted.Model.KaynakBelge != null,
                $"drawable ASCII DXF {presentation} is accepted with a model");
            Invariants(accepted, "ascii " + presentation);
        }
        byte[] malformedBytes = Encoding.Latin1.GetBytes(FixtureText().Replace(Pair(999, "PRESERVED_COMMENT"),
            "bad-code\r\nPRESERVED_COMMENT\r\n", StringComparison.Ordinal));
        string malformed = Write("malformed.dxf", malformedBytes);
        DxfPreviewReadResult tolerant = adapter.Read(Request(malformed));
        Check(tolerant.Content.IsAccepted && tolerant.Model != null && !tolerant.Model.Bos,
            "tolerant malformed DXF keeps its current preview (edit blocking stays with DxfEditOturumu)");

        // Rejected content: request stays Ready (two stages), content says why, no model.
        var rejected = new (string Name, byte[] Bytes, PreviewContentCheckReason Reason)[]
        {
            ("binary.dxf", Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0\0\0\u0001binary"), PreviewContentCheckReason.BinaryDxf),
            ("no-entities.dxf", Encoding.Latin1.GetBytes(Pair(0, "SECTION") + Pair(2, "ENTITIES") + Pair(0, "ENDSEC") + Pair(0, "EOF")),
                PreviewContentCheckReason.NoDrawableEntities),
            ("plain-text.dxf", Encoding.ASCII.GetBytes("test dxf placeholder"), PreviewContentCheckReason.InvalidContent),
            ("zero-bytes.dxf", Array.Empty<byte>(), PreviewContentCheckReason.InvalidContent),
            ("dwg-signature.dxf", Encoding.ASCII.GetBytes("AC1032\0\0\0\0\0\u0001\u0002\u0003binary"), PreviewContentCheckReason.InvalidContent),
            // First text line looks binary but the byte signature is incomplete: no first-line interpretation.
            ("binary-text-line.dxf", Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n0\r\nEOF\r\n"), PreviewContentCheckReason.InvalidContent),
        };
        foreach (var (name, bytes, reason) in rejected)
        {
            string path = Write(name, bytes);
            DxfPreviewReadResult result = adapter.Read(Request(path));
            Check(result.Preview.IsReady && result.Content.Status == PreviewContentCheckStatus.Rejected &&
                  result.Content.Reason == reason && !string.IsNullOrWhiteSpace(result.Content.DiagnosticMessage) && result.Model == null,
                $"{name} is Rejected({reason}) without a model");
            Invariants(result, name);
            Bytes(File.ReadAllBytes(path), bytes, name + " source bytes are unchanged");
        }
        // Rejecting an empty preview does not touch edit: the same file stays editable through DxfEditOturumu.
        string emptyValid = Path.Combine(_directory, "preview-adapter-no-entities.dxf");
        DxfCizim? emptyModel = DxfOkuyucu.Oku(emptyValid, out _);
        Check(emptyModel != null && new DxfEditOturumu(emptyModel, emptyValid).Engel == null,
            "NoDrawableEntities rejection leaves the file editable");

        // Missing: request-level MissingFile, content not checked.
        DxfPreviewReadResult missing = adapter.Read(Request(Path.Combine(_directory, "preview-adapter-missing.dxf")));
        Check(missing.Preview.Status == PreviewResultStatus.MissingFile && missing.Content == PreviewContentCheckResult.NotChecked &&
              missing.Model == null, "missing DXF is MissingFile / NotChecked");

        // Read error: file held exclusively by another handle.
        string locked = Write("locked.dxf", asciiBytes);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            DxfPreviewReadResult failed = adapter.Read(Request(locked));
            Check(failed.Preview.Status == PreviewResultStatus.Failed && failed.Content.Status == PreviewContentCheckStatus.Failed &&
                  failed.Content.Reason == PreviewContentCheckReason.ReadError && failed.Model == null &&
                  !string.IsNullOrWhiteSpace(failed.Preview.DiagnosticDetail), "unreadable DXF is Failed / Failed(ReadError)");
            Invariants(failed, "locked");
        }
        Bytes(File.ReadAllBytes(locked), asciiBytes, "locked source bytes are unchanged");

        // Requests that are not DXF 2D preview never reach the content stage.
        string step = Write("model.stp", Encoding.ASCII.GetBytes("ISO-10303-21;"));
        string dwg = Write("drawing.dwg", Encoding.ASCII.GetBytes("AC1032"));
        string text = Write("notes.txt", Encoding.ASCII.GetBytes("text"));
        var refused = new (string Name, PreviewRequest? Request, PreviewResultStatus Expected)[]
        {
            ("null request", null, PreviewResultStatus.InvalidRequest),
            ("no presentation", Request(ascii, presentation: null), PreviewResultStatus.InvalidRequest),
            ("DXF 3D", Request(ascii, PreviewCapability.Preview3D), PreviewResultStatus.UnsupportedCapability),
            ("DXF edit", Request(ascii, PreviewCapability.Edit, null), PreviewResultStatus.UnsupportedCapability),
            ("STEP 3D", Request(step, PreviewCapability.Preview3D), PreviewResultStatus.UnsupportedCapability),
            ("DWG 2D", Request(dwg), PreviewResultStatus.UnsupportedCapability),
            ("unknown extension", Request(text), PreviewResultStatus.UnsupportedContent),
        };
        foreach (var (name, request, expected) in refused)
        {
            DxfPreviewReadResult result = adapter.Read(request);
            Check(result.Preview.Status == expected && result.Content == PreviewContentCheckResult.NotChecked && result.Model == null &&
                  !string.IsNullOrWhiteSpace(result.Preview.Message), $"{name} is {expected} with content NotChecked");
        }
        Check(adapter.Read(Request(ascii, PreviewCapability.Edit, null)).Preview.SupportLevel == PreviewSupportLevel.Unsupported,
            "the preview adapter never answers an edit request");

        Bytes(File.ReadAllBytes(ascii), asciiBytes, "ASCII source bytes are unchanged after all reads");
        Bytes(File.ReadAllBytes(malformed), malformedBytes, "malformed source bytes are unchanged");
    }

    // Stage 5: what the Dosya Analiz Merkezi panel shows for each adapter outcome.
    private static void DxfDwgPreviewMessageTests()
    {
        var adapter = new DxfPreviewAdapter();
        DxfPreviewReadResult Read(string path) => adapter.Read(new PreviewRequest
        {
            SourcePath = path,
            Capability = PreviewCapability.Preview2D,
            Presentation = PreviewPresentation.Embedded,
            SourceContext = "Dosya Analiz Merkezi"
        });
        string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(_directory, "panel-" + name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        void Expect(DxfPreviewReadResult result, string? expected, string name)
        {
            string? message = DxfDwgPreviewMessages.For(result);
            string? diagnostic = DxfDwgPreviewMessages.Diagnostic(result);
            Check(message == expected, $"{name}: panel message is '{expected ?? "<draw model>"}' (got '{message}')");
            Check(message == null || string.IsNullOrEmpty(diagnostic) || !message.Contains(diagnostic, StringComparison.Ordinal),
                name + ": technical diagnostic never reaches the panel message");
        }

        byte[] asciiBytes = Encoding.Latin1.GetBytes(FixtureText());
        string ascii = Write("ascii.dxf", asciiBytes);
        DxfPreviewReadResult accepted = Read(ascii);
        Expect(accepted, null, "valid ASCII DXF");
        Check(accepted.Model != null && !accepted.Model.Bos, "valid ASCII DXF hands a drawable model to the Path render chain");

        Expect(Read(Write("binary.dxf", Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0\0\0"))),
            "İkili (binary) DXF önizlenemiyor.", "binary DXF");
        Expect(Read(Write("no-entities.dxf", Encoding.Latin1.GetBytes(
                Pair(0, "SECTION") + Pair(2, "ENTITIES") + Pair(0, "ENDSEC") + Pair(0, "EOF")))),
            "Dosyada çizilebilir bir nesne bulunamadı.", "valid DXF without entities");
        Expect(Read(Write("plain-text.dxf", Encoding.ASCII.GetBytes("test dxf placeholder"))),
            "Dosyada çizilebilir bir nesne bulunamadı.", "invalid DXF content");

        string locked = Write("locked.dxf", asciiBytes);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            DxfPreviewReadResult failed = Read(locked);
            Expect(failed, "DXF dosyası okunamadı. Dosyaya erişimi ve dosyanın geçerliliğini kontrol edin.", "unreadable DXF");
            Check(!string.IsNullOrWhiteSpace(DxfDwgPreviewMessages.Diagnostic(failed)), "unreadable DXF keeps a diagnostic for the log");
        }

        // Selected file deleted before the panel refreshes.
        string deleted = Write("deleted.dxf", asciiBytes);
        File.Delete(deleted);
        Expect(Read(deleted), "Seçili DXF dosyası bulunamadı.", "deleted DXF");
        // Same outcome when the file disappears after the coordinator already resolved it.
        PreviewResult resolvedThenGone = new PreviewCoordinator().Resolve(new PreviewRequest
        {
            SourcePath = ascii, Capability = PreviewCapability.Preview2D, Presentation = PreviewPresentation.Embedded
        }) with { Status = PreviewResultStatus.MissingFile };
        Expect(new DxfPreviewReadResult(resolvedThenGone, PreviewContentCheckResult.NotChecked, null),
            "Seçili DXF dosyası bulunamadı.", "DXF deleted during read");

        // DWG keeps the controlled "not supported" message, whether or not the file exists.
        string dwgMessage = "DWG önizleme henüz desteklenmiyor. Dosyayı Aç komutunu kullanabilirsiniz.";
        Expect(Read(Write("drawing.dwg", Encoding.ASCII.GetBytes("AC1032"))), dwgMessage, "DWG");
        Expect(Read(Path.Combine(_directory, "panel-missing.dwg")), dwgMessage, "missing DWG");

        // Other request-level results keep the coordinator message.
        DxfPreviewReadResult unknown = Read(Write("notes.txt", Encoding.ASCII.GetBytes("text")));
        Expect(unknown, unknown.Preview.Message, "unknown extension");
        Check(DxfDwgPreviewMessages.For(new DxfPreviewReadResult(accepted.Preview, PreviewContentCheckResult.Accepted(), null)) ==
              "Dosyada çizilebilir bir nesne bulunamadı.", "accepted result without a model is never drawn");

        Bytes(File.ReadAllBytes(ascii), asciiBytes, "panel reads never write the source");
    }

    private static void AtomicSelectionDeletes()
    {
        string circles = string.Concat(Enumerable.Range(1, 5).Select(i => Circle("C" + i, i * 20, 0, 5)));
        var (path, model, original) = Fixture("atomic-five-circles", WithEntities(circles));
        var session = new DxfEditOturumu(model, path);
        DxfEntity[] selection = model.Entityler.Where(e => e.Tip == "CIRCLE").ToArray();
        Check(selection.Length == 5, "five CIRCLE batch fixture");
        Check(session.Sil(selection, out string? error) && error == null, "five selected circles delete atomically");
        Check(session.Onizleme().Entityler.Count == 0 && session.Onizleme().NesneSayisi == 0, "batch removes all selected preview geometry");
        Bytes(File.ReadAllBytes(path), original, "batch delete never writes to disk");
        Check(session.GeriAl(), "single undo restores batch");
        Check(session.Onizleme().Entityler.Count == 5 && !session.GeriAlabilir && !session.Degisti, "all five restored by exactly one transaction");
        Bytes(session.Cikti(), original, "batch undo restores exact source bytes");
        Check(session.Yinele() && session.Onizleme().Entityler.Count == 0, "single redo removes all five again");
        Check(session.GeriAl(), "batch restored for invalid selection tests");
        var (_, foreign, _) = Fixture("atomic-foreign", WithEntities(Circle("D1", 0, 0, 5)));
        byte[] before = session.Cikti();
        Check(!session.Sil(new[] { selection[0], foreign.Entityler[0] }, out error) && !string.IsNullOrWhiteSpace(error), "mixed foreign selection rejected before deletion");
        Bytes(session.Cikti(), before, "invalid batch leaves every entity untouched");
        Check(!session.GeriAlabilir && session.Yineleabilir, "failed batch neither creates undo nor discards redo");
        Check(!session.Sil(Array.Empty<DxfEntity>(), out error), "empty selection is not an edit");
        Check(session.Sil(new[] { selection[0], selection[0] }, out error), "duplicate selected identity is deleted once");
        Check(session.Onizleme().Entityler.Count == 4, "duplicate selection cannot remove extra entity");
        session.GeriAl();
        Check(!session.GeriAlabilir, "deduplicated selection remains one transaction");

        string referenced = WithEntities(circles).Replace(Pair(2, "OBJECTS"), Pair(2, "OBJECTS") + Pair(0, "DICTIONARY") + Pair(5, "F9") + Pair(360, "C2"), StringComparison.Ordinal);
        var (blockedPath, blockedModel, blockedBytes) = Fixture("atomic-protected", referenced);
        var blocked = new DxfEditOturumu(blockedModel, blockedPath);
        Check(!blocked.Sil(new[] { ByHandle(blockedModel, "C1"), ByHandle(blockedModel, "C2") }, out error), "protected second selection rejects whole deletion");
        Bytes(blocked.Cikti(), blockedBytes, "protected batch does not partially delete first valid entity");
    }

    private static void AnalyticContainment()
    {
        Rect box = new Rect(-10, -10, 20, 20);
        Check(DxfKoseGeometrisi.Cercevede(LineEntity(new Point(-10, -10), new Point(10, 10)), box), "LINE containment includes both boundary endpoints");
        Check(!DxfKoseGeometrisi.Cercevede(LineEntity(new Point(-10, 0), new Point(11, 0)), box), "crossing LINE is not fully contained");
        var circle = new DxfEntity { Tip = "CIRCLE", Merkez = new Point(8, 0), Radius = 5 };
        Check(!DxfKoseGeometrisi.Cercevede(circle, box), "CIRCLE center inside does not imply full containment");
        circle.Merkez = new Point(0, 0);
        Check(DxfKoseGeometrisi.Cercevede(circle, box), "entire CIRCLE inside rectangle selected");
        Rect circleBounds = DxfKoseGeometrisi.GercekBounds(circle);
        Near(circleBounds.Left, -5, "circle analytic left");
        Near(circleBounds.Right, 5, "circle analytic right");

        // The small arc excludes its center but crosses the 90-degree cardinal extremum.
        // The preview deliberately omits that extremum to catch sampled/center-only bounds.
        var arc = new DxfEntity
        {
            Tip = "ARC", Merkez = new Point(0, 0), Radius = 10,
            BaslangicAcisi = 89.99, BitisAcisi = 90.01,
            Noktalar = new[] { Polar(new Point(), 10, 89.99), Polar(new Point(), 10, 90.01) }
        };
        Rect arcBounds = DxfKoseGeometrisi.GercekBounds(arc);
        Near(arcBounds.Bottom, 10, "ARC true cardinal extreme included despite absent preview vertex", 1e-10);
        Check(DxfKoseGeometrisi.Cercevede(arc, new Rect(-0.01, 9.99, 0.02, 0.02)), "ARC containment uses visible arc not center/full circle");
        Check(!DxfKoseGeometrisi.Cercevede(arc, new Rect(-0.01, 9.99, 0.02, 0.00999999)), "tiny excluded true ARC extreme rejects partial containment");
        arc.BaslangicAcisi = 350;
        arc.BitisAcisi = 10;
        Rect wrapped = DxfKoseGeometrisi.GercekBounds(arc);
        Near(wrapped.Right, 10, "wrapped ARC includes zero degree extreme");
        Check(wrapped.Left > 9 && wrapped.Top < 0 && wrapped.Bottom > 0, "wrapped ARC bounds follow minor sweep not full circle");
    }

    private static void ChamferGeometry()
    {
        var (_, model, _) = CornerFixture("chamfer-right-angle");
        DxfEntity a = ByHandle(model, "A1"), b = ByHandle(model, "A2");
        Check(DxfKoseGeometrisi.Pah(a, b, 5, out DxfKosePlani? plan, out string? error) && plan != null && error == null, "90 degree 5x5 chamfer accepted");
        Check(plan!.Degisen.Count == 2 && plan.Eklenen.Count == 1 && plan.Eklenen[0].Entity.Tip == "LINE", "chamfer changes two lines and adds one real LINE");
        PointNear(plan.Degisen[a].Noktalar[0], new Point(5, 0), "first near endpoint trimmed by 5");
        PointNear(plan.Degisen[b].Noktalar[0], new Point(0, 5), "second near endpoint trimmed by 5");
        PointNear(plan.Degisen[a].Noktalar[^1], new Point(100, 0), "chamfer preserves first far endpoint");
        PointNear(plan.Degisen[b].Noktalar[^1], new Point(0, 100), "chamfer preserves second far endpoint");
        Check(UnorderedEndpoints(plan.Eklenen[0].Entity, new Point(5, 0), new Point(0, 5)), "new chamfer LINE joins both trim points");
        PointNear(a.Noktalar[0], new Point(), "planning never mutates original LINE");

        var inclinedA = LineEntity(new Point(), new Point(100, 0));
        var inclinedB = LineEntity(new Point(), new Point(50, 50 * Math.Sqrt(3)));
        Check(DxfKoseGeometrisi.Pah(inclinedA, inclinedB, 5, out plan, out error), "inclined 60 degree equal-distance chamfer accepted");
        Near((plan!.Degisen[inclinedA].Noktalar[0] - new Point()).Length, 5, "inclined first trim distance");
        Near((plan.Degisen[inclinedB].Noktalar[0] - new Point()).Length, 5, "inclined second trim distance");
        Check(DxfKoseGeometrisi.Pah(LineEntity(new Point(10, 0), new Point(100, 0)), LineEntity(new Point(0, 10), new Point(0, 100)), 20, out plan, out error), "virtual intersection outside segments supports unambiguous trim");

        foreach (double value in new[] { 0, -5, 101, double.NaN, double.PositiveInfinity })
            Check(!DxfKoseGeometrisi.Pah(a, b, value, out plan, out error) && !string.IsNullOrWhiteSpace(error), "invalid/oversized chamfer rejected: " + value);
        Check(!DxfKoseGeometrisi.Pah(a, LineEntity(new Point(0, 5), new Point(100, 5)), 5, out plan, out error), "parallel chamfer rejected");
        Check(!DxfKoseGeometrisi.Pah(LineEntity(new Point(-100, 0), new Point(100, 0)), LineEntity(new Point(0, -100), new Point(0, 100)), 5, out plan, out error), "ambiguous interior-intersection chamfer rejected");
        Check(!DxfKoseGeometrisi.Pah(a, a, 5, out plan, out error), "same LINE cannot form chamfer corner");
    }

    private static void FilletGeometry()
    {
        var (_, model, _) = CornerFixture("fillet-right-angle");
        DxfEntity a = ByHandle(model, "A1"), b = ByHandle(model, "A2");
        Check(DxfKoseGeometrisi.Fillet(a, b, 5, out DxfKosePlani? plan, out string? error) && plan != null && error == null, "90 degree R5 fillet accepted");
        Check(plan!.Degisen.Count == 2 && plan.Eklenen.Count == 1, "fillet changes two lines and adds one entity");
        DxfEntity arc = plan.Eklenen[0].Entity;
        Check(arc.Tip == "ARC", "fillet produces real ARC metadata");
        Near(arc.Radius, 5, "R5 exact radius");
        PointNear(arc.Merkez, new Point(5, 5), "90 degree fillet center");
        PointNear(plan.Degisen[a].Noktalar[0], new Point(5, 0), "fillet first tangent point");
        PointNear(plan.Degisen[b].Noktalar[0], new Point(0, 5), "fillet second tangent point");
        Check(UnorderedArcEndpoints(arc, new Point(5, 0), new Point(0, 5)), "ARC angles locate both actual tangent points");
        Near(Sweep(arc), 90, "fillet uses short 90 degree sweep");
        Tangent(plan.Degisen[a], arc, "first fillet tangent");
        Tangent(plan.Degisen[b], arc, "second fillet tangent");

        var inclinedA = LineEntity(new Point(), new Point(100, 0));
        var inclinedB = LineEntity(new Point(), new Point(50, 50 * Math.Sqrt(3)));
        Check(DxfKoseGeometrisi.Fillet(inclinedA, inclinedB, 5, out plan, out error), "inclined 60 degree R5 fillet accepted");
        arc = plan!.Eklenen[0].Entity;
        Near(arc.Radius, 5, "inclined fillet preserves radius");
        Near((arc.Merkez - new Point()).Length, 10, "inclined angle-bisector center distance");
        Near((plan.Degisen[inclinedA].Noktalar[0] - new Point()).Length, 5 * Math.Sqrt(3), "inclined tangent distance R*cot(theta/2)");
        Tangent(plan.Degisen[inclinedA], arc, "inclined first tangent");
        Tangent(plan.Degisen[inclinedB], arc, "inclined second tangent");
        foreach (double radius in new[] { 0, -5, 101, double.NaN, double.PositiveInfinity })
            Check(!DxfKoseGeometrisi.Fillet(a, b, radius, out plan, out error) && !string.IsNullOrWhiteSpace(error), "invalid/oversized fillet rejected: " + radius);
        Check(!DxfKoseGeometrisi.Fillet(a, LineEntity(new Point(0, 5), new Point(100, 5)), 5, out plan, out error), "parallel fillet rejected");
    }

    private static void CornerTransactionsAndSave()
    {
        foreach (bool fillet in new[] { false, true })
        {
            var (path, model, original) = CornerFixture("corner-transaction-" + fillet);
            var session = new DxfEditOturumu(model, path);
            DxfEntity a = ByHandle(model, "A1"), b = ByHandle(model, "A2");
            Check(CornerPlan(a, b, 5, fillet, out DxfKosePlani? plan, out _), "corner transaction geometry planned");
            Check(session.Uygula(plan!, out string? error) && error == null, "corner transaction applied atomically");
            byte[] edited = session.Cikti();
            Check(session.Degisti && session.Onizleme().Entityler.Count == model.Entityler.Count + 1, "corner change and added entity reflected together");
            Bytes(File.ReadAllBytes(path), original, "corner edit remains memory-only");
            string originalText = Encoding.Latin1.GetString(original), editedText = Encoding.Latin1.GetString(edited);
            string expectedPrefix = originalText[..originalText.IndexOf(Pair(0, "SECTION") + Pair(2, "ENTITIES"), StringComparison.Ordinal)]
                .Replace(Pair(9, "$HANDSEED") + Pair(5, "FFFF"), Pair(9, "$HANDSEED") + Pair(5, "10000"), StringComparison.Ordinal);
            Check(editedText.StartsWith(expectedPrefix, StringComparison.Ordinal), "HEADER/TABLES/BLOCKS preserved except required next-handle HANDSEED update");
            Check(editedText.Contains(Pair(1, "UNSUPPORTED_TEXT_MUST_STAY"), StringComparison.Ordinal), "unsupported TEXT survives coordinate patches/new insertion");
            foreach (DxfEntity old in new[] { a, b })
            {
                string record = SourceText(editedText, old.KaynakKayit!.Handle);
                foreach (int code in new[] { 8, 6, 62, 420, 370, 100, 30, 31 })
                    foreach (var pair in old.KaynakKayit.Kodlar.Where(p => p.Kod == code))
                        Check(record.Contains(Pair(pair.Kod, pair.Deger), StringComparison.Ordinal), "existing non-coordinate property retained: " + code);
            }
            Check(session.GeriAl() && !session.GeriAlabilir && !session.Degisti, "one undo restores both original lines and removes added corner entity");
            Bytes(session.Cikti(), original, "corner undo returns exact original byte stream");
            Check(session.Yinele(), "corner redo accepted");
            Bytes(session.Cikti(), edited, "redo returns identical coordinate/handle/added record patch");
            string output = Path.Combine(_directory, "corner-saved-" + fillet + ".dxf");
            session.FarkliKaydet(output);
            Check(!session.Degisti, "corner Save As establishes checkpoint");
            DxfCizim reopened = Read(output);
            Check(reopened.Entityler.Count == model.Entityler.Count + 1, "saved corner reopens with actual added DXF entity");
            DxfEntity newEntity = reopened.Entityler.Single(e => e.KaynakKayit?.Handle != "A1" && e.KaynakKayit?.Handle != "A2" && e.KaynakKayit != null);
            Check(newEntity.Tip == (fillet ? "ARC" : "LINE"), "reopened generated record has correct DXF type");
            Check(newEntity.KaynakKayit!.Handle.Length != 0, "new entity receives actual source handle");
            if (fillet)
            {
                Near(newEntity.Radius, 5, "reopened real ARC radius group 40");
                PointNear(newEntity.Merkez, new Point(5, 5), "reopened ARC center groups 10/20");
                Check(newEntity.KaynakKayit.Kodlar.Any(p => p.Kod == 50) && newEntity.KaynakKayit.Kodlar.Any(p => p.Kod == 51), "saved ARC contains start/end angle group codes");
                Check(UnorderedArcEndpoints(newEntity, new Point(5, 0), new Point(0, 5)), "saved ARC angles preserve tangent geometry");
            }
            string[] allHandles = reopened.Entityler.Where(e => e.KaynakKayit != null).Select(e => NormalizeHandle(e.KaynakKayit!.Handle)).ToArray();
            Check(allHandles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == allHandles.Length, "generated handles are unique among live records");
            Check(session.GeriAl() && session.Degisti, "undo after corner save is dirty against saved geometry");
            Bytes(session.Cikti(), original, "undo after corner save restores initial coordinates and removes added record");
            string backup = session.Kaydet(true);
            Bytes(File.ReadAllBytes(backup), edited, "corner undo+save backs up actual previous generated geometry");
            Bytes(File.ReadAllBytes(output), original, "corner undo+save restores exact original record bytes");
            Bytes(File.ReadAllBytes(path), original, "adopted Save As target never changes old source");
        }

        var (repeatPath, repeatModel, repeatOriginal) = CornerFixture("repeat-generated-edit");
        var repeat = new DxfEditOturumu(repeatModel, repeatPath);
        Check(DxfKoseGeometrisi.Pah(ByHandle(repeatModel, "A1"), ByHandle(repeatModel, "A2"), 20, out DxfKosePlani? first, out _), "first generated LINE planned");
        Check(repeat.Uygula(first!, out _), "first generated LINE applied");
        byte[] firstBytes = repeat.Cikti();
        DxfCizim current = repeat.Onizleme();
        DxfEntity generated = current.Entityler.Single(e => e.Tip == "LINE" && e.KaynakKayit?.Yeni == true);
        DxfEntity changedA = current.Entityler.Single(e => e.KaynakKayit?.Handle == "A1");
        Check(DxfKoseGeometrisi.Fillet(changedA, generated, 2, out DxfKosePlani? second, out _), "generated chamfer LINE can participate in subsequent R2 edit");
        Check(repeat.Uygula(second!, out _), "subsequent generated-entity transaction applied");
        Check(repeat.Onizleme().Entityler.Count == 4, "second corner includes prior generated LINE plus new ARC");
        Check(repeat.GeriAl(), "undo repeated generated edit");
        Bytes(repeat.Cikti(), firstBytes, "second undo restores previous changed and added entity state exactly");
        Check(repeat.GeriAl(), "undo original chamfer");
        Bytes(repeat.Cikti(), repeatOriginal, "all repeated edits undo to exact original");
        Check(repeat.Yinele() && repeat.Yinele(), "redo both dependent transactions");
        current = repeat.Onizleme();
        DxfEntity generatedArc = current.Entityler.Single(e => e.Tip == "ARC");
        byte[] beforeDelete = repeat.Cikti();
        Check(repeat.Sil(new[] { generatedArc }, out _), "generated ARC is selectable/deletable before save");
        Check(repeat.GeriAl(), "undo generated ARC deletion");
        Bytes(repeat.Cikti(), beforeDelete, "generated ARC deletion undo restores assigned handle and geometry");

        var (_, foreignModel, _) = CornerFixture("bad-plan-foreign");
        current = repeat.Onizleme();
        Check(DxfKoseGeometrisi.Pah(current.Entityler.First(e => e.KaynakKayit?.Handle == "A2"), current.Entityler.First(e => e.Tip == "LINE" && e.KaynakKayit?.Yeni == true), 1, out DxfKosePlani? invalid, out _), "validation fixture produces candidate plan");
        invalid!.Degisen.Add(foreignModel.Entityler[0], LineEntity(new Point(1, 1), new Point(10, 1)));
        byte[] beforeInvalid = repeat.Cikti();
        Check(!repeat.Uygula(invalid, out string? invalidError) && !string.IsNullOrWhiteSpace(invalidError), "foreign replacement rejects entire transaction");
        Bytes(repeat.Cikti(), beforeInvalid, "invalid corner plan cannot leave partial line trim");
    }

    private static void ClosedContourCorners()
    {
        Point[][] shapes =
        {
            new[] { new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100) },
            new[] { new Point(0, 0), new Point(100, 0), new Point(100, 40), new Point(40, 40), new Point(40, 100), new Point(0, 100) }
        };
        foreach (bool fillet in new[] { false, true })
        {
            foreach (Point[] vertices in shapes)
            {
                foreach (bool reverse in new[] { false, true })
                {
                    Point[] ring = reverse ? vertices.Reverse().ToArray() : vertices;
                    var (path, model, original) = Fixture("closed-" + fillet + "-" + ring.Length + "-" + reverse, WithEntities(Loop(ring, "1")));
                    var session = new DxfEditOturumu(model, path);
                    Check(DxfKoseGeometrisi.TumKoseler(model.Entityler, 5, fillet, out DxfKosePlani? plan, out string? error) && error == null, "closed rectangle/L-shape all-corner plan accepted independent of winding");
                    Check(plan!.Degisen.Count == ring.Length && plan.Eklenen.Count == ring.Length, "all corners planned with one final trim per source LINE");
                    Check(session.Uygula(plan, out error), "all-corner transaction applies once");
                    Check(session.Onizleme().Entityler.Count == ring.Length * 2, "all corner generated geometry present");
                    Check(session.GeriAl() && !session.GeriAlabilir, "one undo rolls back every contour corner");
                    Bytes(session.Cikti(), original, "contour undo restores original complete records");
                    Check(session.Yinele(), "all-corner redo rolls forward whole transaction");
                    Bytes(File.ReadAllBytes(path), original, "all-corner editing never writes source before save");
                }
            }
            Point[] outer = shapes[0], separate = shapes[0].Select(p => p + new Vector(150, 0)).ToArray();
            Point[] hole = new[] { new Point(30, 30), new Point(70, 30), new Point(70, 70), new Point(30, 70) };
            foreach ((string kind, Point[] inner) in new[] { ("disjoint", separate), ("hole-same-winding", hole), ("hole-reverse-winding", hole.Reverse().ToArray()) })
            {
                var (path, model, original) = Fixture("multiple-" + kind + "-" + fillet, WithEntities(Loop(outer, "1") + Loop(inner, "2")));
                var session = new DxfEditOturumu(model, path);
                Check(DxfKoseGeometrisi.TumKoseler(model.Entityler, 5, fillet, out DxfKosePlani? plan, out string? error), "unfiltered all-corner multiple/nested loops accepted: " + kind);
                Check(plan!.Degisen.Count == 8 && plan.Eklenen.Count == 8, "every valid loop corner participates, without material-side guesses");
                Check(session.Uygula(plan, out error) && session.GeriAl() && !session.GeriAlabilir, "multiple-loop edit remains one transaction");
                Bytes(session.Cikti(), original, "multiple/nested-loop rollback restores both loops");
                Bytes(File.ReadAllBytes(path), original, "nested hole edit does not mutate disk");
            }
        }
    }

    private static void InvalidContourAtomicity()
    {
        Point[] square = { new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100) };
        var cases = new Dictionary<string, string>
        {
            ["open"] = NewLine("11", square[0], square[1]) + NewLine("12", square[1], square[2]) + NewLine("13", square[2], square[3]),
            ["branch"] = Loop(square, "1") + NewLine("15", square[0], new Point(-50, 0)),
            ["self-crossing"] = Loop(new[] { square[0], square[2], square[3], square[1] }, "1"),
            ["touching-loops"] = Loop(square, "1") + Loop(square.Select(p => p + new Vector(100, 100)).ToArray(), "2"),
            ["zero-length"] = Loop(square, "1") + NewLine("15", new Point(200, 200), new Point(200, 200)),
            ["collinear-corner"] = Loop(new[] { square[0], new Point(50, 0), square[1], square[2], square[3] }, "1")
        };
        foreach (bool fillet in new[] { false, true })
        {
            foreach (var test in cases)
            {
                var (path, model, original) = Fixture("invalid-contour-" + test.Key + "-" + fillet, WithEntities(test.Value));
                var session = new DxfEditOturumu(model, path);
                Check(!DxfKoseGeometrisi.TumKoseler(model.Entityler, 5, fillet, out _, out string? error) && !string.IsNullOrWhiteSpace(error), "unreliable contour rejected: " + test.Key);
                Bytes(session.Cikti(), original, "invalid contour leaves source memory byte-exact");
                Check(!session.GeriAlabilir && !session.Degisti, "invalid all-corner validation creates no history");
                Bytes(File.ReadAllBytes(path), original, "invalid contour leaves file untouched");
            }
            var (largePath, largeModel, largeOriginal) = Fixture("overlapping-corners-" + fillet, WithEntities(Loop(square, "1")));
            var largeSession = new DxfEditOturumu(largeModel, largePath);
            Check(!DxfKoseGeometrisi.TumKoseler(largeModel.Entityler, 60, fillet, out _, out string? largeError) && !string.IsNullOrWhiteSpace(largeError), "individually fitting corners that overlap on shared LINE rejected atomically");
            Bytes(largeSession.Cikti(), largeOriginal, "oversize all-corner overlap leaves original state unchanged");
            Check(!largeSession.Degisti && !largeSession.GeriAlabilir, "oversize contour does not leave half transaction");
        }
    }

    private static void HandleAndCornerSafety()
    {
        var shortA = LineEntity(new Point(10, 0), new Point(100, 0));
        var shortB = LineEntity(new Point(0, 10), new Point(0, 100));
        Check(!DxfKoseGeometrisi.Pah(shortA, shortB, 95, out _, out _), "virtual-corner chamfer larger than actual segment length rejected");
        foreach (bool fillet in new[] { false, true })
        {
            var a = LineEntity(new Point(100, 0), new Point());
            var b = LineEntity(new Point(0, 100), new Point());
            Check(CornerPlan(a, b, 5, fillet, out DxfKosePlani? reversed, out _), "reversed source endpoints form valid corner");
            PointNear(reversed!.Degisen[a].Noktalar[0], new Point(100, 0), "reversed corner preserves first far endpoint");
            PointNear(reversed.Degisen[b].Noktalar[0], new Point(0, 100), "reversed corner preserves second far endpoint");
            PointNear(reversed.Degisen[a].Noktalar[^1], new Point(5, 0), "reversed corner trims correct first end");
            PointNear(reversed.Degisen[b].Noktalar[^1], new Point(0, 5), "reversed corner trims correct second end");
        }
        string text = WithEntities(NewLine("A1", new Point(), new Point(100, 0)) + NewLine("A2", new Point(), new Point(0, 100)))
            .Replace(Pair(9, "$HANDSEED") + Pair(5, "FFFF"), Pair(9, "$HANDSEED") + Pair(5, "FFFF") + Pair(9, "$UNRESOLVED_POINTER") + Pair(340, "10001"), StringComparison.Ordinal);
        var (path, model, original) = Fixture("reserved-reference-handle", text);
        var session = new DxfEditOturumu(model, path);
        Check(DxfKoseGeometrisi.Pah(ByHandle(model, "A1"), ByHandle(model, "A2"), 5, out DxfKosePlani? plan, out _), "handle safety corner planned");
        Check(session.Uygula(plan!, out _), "handle safety corner applied");
        DxfEntity generated = session.Onizleme().Entityler.Single(x => x.KaynakKayit?.Yeni == true);
        Check(generated.KaynakKayit!.Handle == "10002", "new handles avoid even dangling HEADER reference targets");
        string edited = Encoding.Latin1.GetString(session.Cikti());
        Check(edited.Contains(Pair(9, "$HANDSEED") + Pair(5, "10003"), StringComparison.Ordinal), "HANDSEED advances beyond emitted entity handles");
        Check(edited.Contains(Pair(9, "$UNRESOLVED_POINTER") + Pair(340, "10001"), StringComparison.Ordinal), "unresolved HEADER pointer byte values preserved");
        Check(session.GeriAl(), "handle safety undo");
        Bytes(session.Cikti(), original, "undo removes new entity and restores original HANDSEED exactly");
        Bytes(File.ReadAllBytes(path), original, "handle allocation never writes DXF before Save");
        var (_, other, _) = CornerFixture("foreign-added-property-source");
        Check(DxfKoseGeometrisi.Pah(ByHandle(model, "A1"), ByHandle(model, "A2"), 5, out plan, out _), "invalid added property source fixture planned");
        plan!.Eklenen[0].OzellikKaynak = other.Entityler[0];
        Check(!session.Uygula(plan, out _), "foreign added property source rejects entire transaction");
        Bytes(session.Cikti(), original, "invalid added source leaves all source LINE coordinates untouched");
    }

    private static void JoinGeometryAndTransactions()
    {
        foreach (double degrees in new[] { 30.0, 60.0, 90.0, 120.0, 150.0 })
        {
            Vector u = new Vector(1, 0), v = new Vector(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));
            foreach (bool overrun in new[] { false, true })
                foreach (bool reverse in new[] { false, true })
                {
                    Point nearA = new Point() + u * (overrun ? -5 : 5), farA = new Point() + u * 100;
                    Point nearB = new Point() + v * (overrun ? -5 : 5), farB = new Point() + v * 100;
                    DxfEntity a = reverse ? LineEntity(farA, nearA) : LineEntity(nearA, farA);
                    DxfEntity b = reverse ? LineEntity(farB, nearB) : LineEntity(nearB, farB);
                    Check(DxfKoseGeometrisi.Birlestir(a, b, farA, farB, out DxfKosePlani? plan, out string? error), "gap/overrun acute/obtuse join accepted: " + degrees);
                    Check(plan!.Eklenen.Count == 0 && plan.Degisen.Count == 2, "join updates two existing LINEs, no third entity");
                    PointNear(plan.Degisen[a].Noktalar[reverse ? 0 : 1], farA, "join preserves first correct far endpoint");
                    PointNear(plan.Degisen[b].Noktalar[reverse ? 0 : 1], farB, "join preserves second correct far endpoint");
                    PointNear(plan.Degisen[a].Noktalar[reverse ? 1 : 0], new Point(), "join first near endpoint becomes intersection");
                    PointNear(plan.Degisen[b].Noktalar[reverse ? 1 : 0], new Point(), "join second near endpoint becomes intersection");
                }
        }
        var a0 = LineEntity(new Point(5, 0), new Point(100, 0));
        Check(!DxfKoseGeometrisi.Birlestir(a0, LineEntity(new Point(0, 5), new Point(100, 5)), null, null, out _, out _), "parallel join rejected");
        Check(!DxfKoseGeometrisi.Birlestir(a0, LineEntity(new Point(0, 1), new Point(100, 1.000001)), null, null, out _, out _), "near-parallel join rejected");
        Check(!DxfKoseGeometrisi.Birlestir(a0, LineEntity(new Point(20, 0), new Point(200, 0)), null, null, out _, out _), "coincident join rejected");
        Check(!DxfKoseGeometrisi.Birlestir(a0, LineEntity(new Point(0, 5), new Point(0, 100)), new Point(-10, 0), new Point(0, 50), out _, out _), "wrong exterior side does not extend wrong endpoint");
        Check(!DxfKoseGeometrisi.Birlestir(a0, LineEntity(new Point(double.NaN, 0), new Point(0, 100)), null, null, out _, out _), "non-finite join rejected");
        var (path, model, original) = CornerFixture("delete-and-join");
        var session = new DxfEditOturumu(model, path);
        Check(DxfKoseGeometrisi.Fillet(ByHandle(model, "A1"), ByHandle(model, "A2"), 5, out DxfKosePlani? fillet, out _), "delete/join R5 planned");
        Check(session.Uygula(fillet!, out _), "delete/join R5 applied");
        DxfEntity arc = session.Onizleme().Entityler.Single(x => x.Tip == "ARC");
        Check(session.Sil(arc, out _), "delete/join ARC deleted");
        byte[] gap = session.Cikti();
        DxfCizim current = session.Onizleme();
        Check(DxfKoseGeometrisi.Birlestir(ByHandle(current, "A1"), ByHandle(current, "A2"), null, null, out DxfKosePlani? join, out _), "deleted fillet virtual intersection solved");
        Check(session.Uygula(join!, out _), "deleted fillet LINEs joined in one transaction");
        Bytes(session.Cikti(), original, "delete + join restores actual initial sharp DXF coordinates and HANDSEED");
        Check(session.GeriAl(), "join undo accepted");
        Bytes(session.Cikti(), gap, "one join undo restores both gap endpoints, not deleted ARC");
        Check(session.Yinele(), "join redo accepted");
        Bytes(session.Cikti(), original, "one join redo restores sharp intersection");
        Bytes(File.ReadAllBytes(path), original, "join sequence remains memory only");
        var (gapPath, gapModel, gapOriginal) = Fixture("join-save", WithEntities(NewLine("A1", new Point(5, 0), new Point(100, 0)) + NewLine("A2", new Point(0, 5), new Point(0, 100))));
        var saved = new DxfEditOturumu(gapModel, gapPath);
        Check(DxfKoseGeometrisi.Birlestir(ByHandle(gapModel, "A1"), ByHandle(gapModel, "A2"), null, null, out join, out _), "gap fixture join planned");
        Check(saved.Uygula(join!, out _) && saved.Degisti, "gap join marks memory dirty");
        string backup = saved.Kaydet(true);
        Bytes(File.ReadAllBytes(backup), gapOriginal, "join Save uses existing original backup");
        PointNear(ByHandle(Read(gapPath), "A1").Noktalar[0], new Point(), "saved joined LINE actually reopens at sharp corner");
    }

    private static void ClickedCornerBranches()
    {
        foreach (bool fillet in new[] { false, true })
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                {
                    var a = LineEntity(new Point(-100, 0), new Point(100, 0));
                    var b = LineEntity(new Point(0, -100), new Point(0, 100));
                    Point click = new Point(sx * 2, sy * 2);
                    bool ok = fillet ? DxfKoseGeometrisi.Fillet(a, b, 5, click, click, out DxfKosePlani? plan, out _)
                        : DxfKoseGeometrisi.Pah(a, b, 5, click, click, out plan, out _);
                    Check(ok, "clicked four-quadrant corner accepted: " + fillet + "/" + sx + "/" + sy);
                    PointNear(plan!.Degisen[a].Noktalar[sx > 0 ? 0 : 1], new Point(sx * 5, 0), "clicked first trim endpoint regression");
                    PointNear(plan.Degisen[b].Noktalar[sy > 0 ? 0 : 1], new Point(0, sy * 5), "clicked second trim endpoint regression");
                    PointNear(plan.Degisen[a].Noktalar[sx > 0 ? 1 : 0], new Point(sx * 100, 0), "clicked first far endpoint preserved");
                    PointNear(plan.Degisen[b].Noktalar[sy > 0 ? 1 : 0], new Point(0, sy * 100), "clicked second far endpoint preserved");
                    if (fillet)
                    {
                        DxfEntity arc = plan.Eklenen[0].Entity;
                        PointNear(arc.Merkez, new Point(sx * 5, sy * 5), "clicked ARC center in correct quadrant");
                        Near(arc.Radius, 5, "clicked ARC R5");
                        Check(UnorderedArcEndpoints(arc, new Point(sx * 5, 0), new Point(0, sy * 5)), "clicked ARC start/end angles give correct tangent points");
                        Tangent(plan.Degisen[a], arc, "clicked first LINE tangent");
                        Tangent(plan.Degisen[b], arc, "clicked second LINE tangent");
                    }
                    Check(!DxfKoseGeometrisi.Fillet(a, b, 5, new Point(), new Point(), out _, out _), "intersection-only click does not guess ambiguous branch");
                    Check(!DxfKoseGeometrisi.Pah(a, b, 101, click, click, out _, out _), "clicked branch insufficient usable length rejects pah");
                    Check(!DxfKoseGeometrisi.Fillet(a, b, 101, click, click, out _, out _), "clicked branch insufficient usable length rejects radius");
                }
        foreach (double angle in new[] { 30.0, 120.0 })
        {
            var a = LineEntity(new Point(), new Point(100, 0));
            var b = LineEntity(new Point(), Polar(new Point(), 100, angle));
            Point click = new Point() + ((a.Noktalar[^1] - new Point()) + (b.Noktalar[^1] - new Point())) / 20;
            Check(DxfKoseGeometrisi.Fillet(a, b, 5, click, click, out DxfKosePlani? plan, out _), "clicked acute/obtuse R5 accepted");
            Near((plan!.Degisen[a].Noktalar[0] - new Point()).Length, 5 / Math.Tan(angle * Math.PI / 360), "clicked angle tangent distance");
            Tangent(plan.Degisen[a], plan.Eklenen[0].Entity, "clicked acute/obtuse tangent");
            Check(DxfKoseGeometrisi.Pah(a, b, 5, click, click, out plan, out _), "clicked acute/obtuse equal pah accepted");
            Near((plan!.Degisen[a].Noktalar[0] - new Point()).Length, 5, "clicked acute/obtuse pah distance");
        }
    }

    private static void ContourDiscovery()
    {
        Point[] square = { new Point(), new Point(100, 0), new Point(100, 100), new Point(0, 100) };
        Point[] other = square.Select(p => p + new Vector(200, 0)).ToArray();
        Point[] lShape = { new Point(), new Point(100, 0), new Point(100, 40), new Point(40, 40), new Point(40, 100), new Point(0, 100) };
        foreach (Point[] vertices in new[] { square, lShape })
        {
            var (path, model, original) = Fixture("discovery-" + vertices.Length, WithEntities(Loop(vertices, "1") + Loop(other, "2")));
            foreach (DxfEntity seed in model.Entityler.Take(vertices.Length))
                Check(DxfKoseGeometrisi.KonturBul(model, seed, out List<DxfEntity> loop, out _) && loop.Count == vertices.Length,
                    "one clicked edge finds only its entire closed rectangle/L loop");
            foreach (bool fillet in new[] { false, true })
            {
                Check(DxfKoseGeometrisi.KonturBul(model, model.Entityler[0], out List<DxfEntity> loop, out _), "selected contour found");
                Check(DxfKoseGeometrisi.TumKoseler(loop, 5, fillet, out DxfKosePlani? plan, out _), "selected contour corner plan valid");
                var session = new DxfEditOturumu(model, path);
                Check(session.Uygula(plan!, out _) && session.GeriAl() && !session.GeriAlabilir, "selected contour is one atomic undo transaction");
                Bytes(session.Cikti(), original, "selected contour undo returns exact document including untouched second loop");
            }
        }
        string[] invalid = {
            NewLine("11", square[0], square[1]) + NewLine("12", square[1], square[2]),
            Loop(square, "1") + NewLine("15", new Point(), new Point(-50, 0)),
            Loop(new[] { square[0], square[2], square[3], square[1] }, "1"),
            Loop(square, "1") + NewLine("15", new Point(20, 0), new Point(80, 0)),
            Loop(square, "1") + NewLine("15", new Point(50, -10), new Point(50, 10)),
            Loop(square, "1") + Pair(0, "ARC") + Pair(5, "30") + Pair(10, "0") + Pair(20, "10") + Pair(40, "10") + Pair(50, "270") + Pair(51, "0") };
        foreach (string text in invalid)
        {
            var (_, model, _) = Fixture("invalid-discovery-" + Guid.NewGuid().ToString("N"), WithEntities(text));
            Check(!DxfKoseGeometrisi.KonturBul(model, model.Entityler[0], out _, out string? error) && error == "Kapalı kontur güvenilir şekilde oluşturulamadı.",
                "open/branch/self-crossing/overlap/external crossing/mixed ARC contour discovery rejected");
        }
        var (_, opaque, _) = Fixture("opaque-contour-path", WithEntities(Loop(square, "1")));
        opaque.Yollar.Add(new[] { new Point(20, 20), new Point(30, 30) });
        Check(!DxfKoseGeometrisi.KonturBul(opaque, opaque.Entityler[0], out _, out _), "unknown sampled spline/polyline path overlapping contour bounds is not guessed");
        var (allPath, all, allOriginal) = Fixture("all-trusted-discovery", WithEntities(Loop(square, "1") + Loop(other, "2") + NewLine("31", new Point(500, 0), new Point(600, 0))));
        foreach (bool fillet in new[] { false, true })
        {
            Check(DxfKoseGeometrisi.TumGuvenilirKonturlar(all, 5, fillet, out DxfKosePlani? plan, out _) && plan!.Degisen.Count == 8,
                "all trustworthy loops found while independent open geometry remains untouched");
            var session = new DxfEditOturumu(all, allPath);
            Check(session.Uygula(plan!, out _) && session.GeriAl() && !session.GeriAlabilir, "all trustworthy loops share one transaction");
            Bytes(session.Cikti(), allOriginal, "one undo restores all trustworthy loops and unedited open geometry");
        }
        Point[] small = { new Point(200, 0), new Point(206, 0), new Point(206, 6), new Point(200, 6) };
        var (smallPath, tooSmall, smallOriginal) = Fixture("all-trusted-invalid-size", WithEntities(Loop(square, "1") + Loop(small, "2")));
        Check(!DxfKoseGeometrisi.TumGuvenilirKonturlar(tooSmall, 5, true, out _, out _), "one invalid corner size prevents all trustworthy loop plan");
        Bytes(new DxfEditOturumu(tooSmall, smallPath).Cikti(), smallOriginal, "all trustworthy plan failure leaves entire model/disk unchanged");
    }

    private static (string Path, DxfCizim Model, byte[] Bytes) CornerFixture(string name) =>
        Fixture(name, WithEntities(NewLine("A1", new Point(), new Point(100, 0)) + NewLine("A2", new Point(), new Point(0, 100)) +
            Pair(0, "TEXT") + Pair(5, "E3") + Pair(1, "UNSUPPORTED_TEXT_MUST_STAY") + Pair(10, "300") + Pair(20, "300")));

    private static DxfEntity LineEntity(Point a, Point b) => new DxfEntity { Tip = "LINE", Noktalar = new[] { a, b } };
    private static string CommonProperties() => Pair(8, "PRESERVE_LAYER") + Pair(6, "PRESERVE_LINETYPE") + Pair(62, "3") + Pair(420, "16711935") + Pair(370, "35") + Pair(100, "AcDbEntity") + Pair(100, "AcDbLine");
    private static string NewLine(string handle, Point a, Point b) => Pair(0, "LINE") + Pair(5, handle) + CommonProperties() +
        Pair(10, a.X.ToString("R", CultureInfo.InvariantCulture)) + Pair(20, a.Y.ToString("R", CultureInfo.InvariantCulture)) + Pair(30, "0.0") +
        Pair(11, b.X.ToString("R", CultureInfo.InvariantCulture)) + Pair(21, b.Y.ToString("R", CultureInfo.InvariantCulture)) + Pair(31, "0.0");
    private static string Circle(string handle, double x, double y, double radius) => Pair(0, "CIRCLE") + Pair(5, handle) +
        Pair(8, "PRESERVE_LAYER") + Pair(10, x.ToString("R", CultureInfo.InvariantCulture)) + Pair(20, y.ToString("R", CultureInfo.InvariantCulture)) + Pair(40, radius.ToString("R", CultureInfo.InvariantCulture));
    private static string Loop(Point[] vertices, string handlePrefix) => string.Concat(Enumerable.Range(0, vertices.Length).Select(i => NewLine(handlePrefix + (i + 1).ToString("X", CultureInfo.InvariantCulture), vertices[i], vertices[(i + 1) % vertices.Length])));
    private static string WithEntities(string entities)
    {
        string text = FixtureText();
        string startMarker = Pair(0, "SECTION") + Pair(2, "ENTITIES");
        int start = text.IndexOf(startMarker, StringComparison.Ordinal) + startMarker.Length;
        int end = text.IndexOf(Pair(0, "ENDSEC"), start, StringComparison.Ordinal);
        return text[..start] + entities + text[end..];
    }
    private static string SourceText(string output, string handle)
    {
        string[] lines = output.Split("\r\n", StringSplitOptions.None);
        int start = 0;
        bool found = false;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (lines[i].Trim() == "0")
            {
                if (found) return string.Join("\r\n", lines[start..i]) + "\r\n";
                start = i;
            }
            if (lines[i].Trim() == "5" && lines[i + 1].Trim() == handle) found = true;
        }
        throw new InvalidOperationException("Missing patched handle " + handle);
    }
    private static string NormalizeHandle(string value) => value.TrimStart('0').ToUpperInvariant();
    private static bool CornerPlan(DxfEntity a, DxfEntity b, double value, bool fillet, out DxfKosePlani? plan, out string? error) =>
        fillet ? DxfKoseGeometrisi.Fillet(a, b, value, out plan, out error) : DxfKoseGeometrisi.Pah(a, b, value, out plan, out error);
    private static Point Polar(Point center, double radius, double degrees) => center + new Vector(radius * Math.Cos(degrees * Math.PI / 180), radius * Math.Sin(degrees * Math.PI / 180));
    private static bool UnorderedEndpoints(DxfEntity entity, Point a, Point b) =>
        ((entity.Noktalar[0] - a).Length < 1e-7 && (entity.Noktalar[^1] - b).Length < 1e-7) ||
        ((entity.Noktalar[0] - b).Length < 1e-7 && (entity.Noktalar[^1] - a).Length < 1e-7);
    private static bool UnorderedArcEndpoints(DxfEntity arc, Point a, Point b) => UnorderedEndpoints(LineEntity(Polar(arc.Merkez, arc.Radius, arc.BaslangicAcisi), Polar(arc.Merkez, arc.Radius, arc.BitisAcisi)), a, b);
    private static double Sweep(DxfEntity arc) { double end = arc.BitisAcisi; while (end <= arc.BaslangicAcisi) end += 360; return end - arc.BaslangicAcisi; }
    private static void Tangent(DxfEntity line, DxfEntity arc, string name)
    {
        Point point = line.Noktalar.OrderBy(p => Math.Abs((p - arc.Merkez).Length - arc.Radius)).First();
        Vector radial = point - arc.Merkez, direction = line.Noktalar[^1] - line.Noktalar[0];
        Near(radial.Length, arc.Radius, name + " touches radius");
        radial.Normalize(); direction.Normalize();
        Near(Vector.Multiply(radial, direction), 0, name + " perpendicular radius");
    }
    private static void PointNear(Point actual, Point expected, string name, double tolerance = 1e-7) => Check((actual - expected).Length <= tolerance, name + $" (actual {actual}, expected {expected})");
    private static void Near(double actual, double expected, string name, double tolerance = 1e-7) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, name + $" (actual {actual}, expected {expected})");

    private static (string Path, DxfCizim Model, byte[] Bytes) Fixture(string name, string? text = null)
    {
        string path = Path.Combine(_directory, name + ".dxf");
        byte[] bytes = Encoding.Latin1.GetBytes(text ?? FixtureText());
        File.WriteAllBytes(path, bytes);
        return (path, Read(path), bytes);
    }

    private static DxfCizim Read(string path)
    {
        DxfCizim? model = DxfOkuyucu.Oku(path, out string? error);
        Check(model != null && string.IsNullOrEmpty(error), "fixture parses: " + Path.GetFileName(path));
        return model!;
    }

    private static DxfEntity ByHandle(DxfCizim model, string handle) =>
        model.Entityler.First(e => e.KaynakKayit?.Handle == handle);

    private static byte[] Remove(byte[] original, DxfKaynakKayit record) =>
        original.Take(record.Baslangic).Concat(original.Skip(record.Bitis)).ToArray();

    private static string Pair(int code, string value) => code.ToString(CultureInfo.InvariantCulture) + "\r\n" + value + "\r\n";

    private static string FixtureText(string extraObjects = "")
    {
        string common = Pair(8, "PRESERVE_LAYER") + Pair(6, "PRESERVE_LINETYPE") + Pair(62, "3") + Pair(420, "16711935") + Pair(370, "35");
        string line(string handle, int x1, int y1, int x2, int y2) => Pair(0, "LINE") + Pair(5, handle) + common +
            Pair(10, x1.ToString()) + Pair(20, y1.ToString()) + Pair(30, "0.0") +
            Pair(11, x2.ToString()) + Pair(21, y2.ToString()) + Pair(31, "0.0");
        return Pair(0, "SECTION") + Pair(2, "HEADER") + Pair(9, "$ACADVER") + Pair(1, "AC1027") +
            Pair(9, "$HANDSEED") + Pair(5, "FFFF") + Pair(9, "$INSUNITS") + Pair(70, "4") +
            // Non-ASCII payload and LF-only comment prove the save path is byte-preserving.
            "999\nPRESERVE_PAYLOAD_é_ü\n" + Pair(999, "PRESERVED_COMMENT") + Pair(0, "ENDSEC") +
            Pair(0, "SECTION") + Pair(2, "TABLES") + Pair(0, "TABLE") + Pair(2, "LAYER") + Pair(70, "1") +
            Pair(0, "LAYER") + Pair(5, "F2") + Pair(2, "PRESERVE_LAYER") + Pair(70, "0") + Pair(62, "3") + Pair(6, "PRESERVE_LINETYPE") +
            Pair(0, "ENDTAB") + Pair(0, "TABLE") + Pair(2, "LTYPE") + Pair(70, "1") + Pair(0, "LTYPE") + Pair(5, "F3") + Pair(2, "PRESERVE_LINETYPE") +
            Pair(3, "PRESERVE_DASH_DESCRIPTION") + Pair(72, "65") + Pair(73, "2") + Pair(40, "1.0") + Pair(49, "0.5") + Pair(74, "0") + Pair(49, "-0.5") + Pair(74, "0") +
            Pair(0, "ENDTAB") + Pair(0, "ENDSEC") + Pair(0, "SECTION") + Pair(2, "BLOCKS") +
            Pair(0, "BLOCK") + Pair(5, "F4") + Pair(2, "PRESERVE_BLOCK") + Pair(70, "0") + Pair(10, "0") + Pair(20, "0") +
            line("B1", 0, 0, 40, 0) + Pair(0, "ENDBLK") + Pair(5, "F5") + Pair(0, "ENDSEC") +
            Pair(0, "SECTION") + Pair(2, "ENTITIES") +
            line("A1", 100, 100, 200, 100) + line("A2", 100, 120, 200, 120) +
            Pair(0, "CIRCLE") + Pair(5, "C1") + common + Pair(100, "AcDbEntity") + Pair(100, "AcDbCircle") + Pair(10, "150.000") + Pair(20, "150.000") + Pair(30, "0") + Pair(40, "20.0") +
            Pair(0, "ARC") + Pair(5, "D1") + common + Pair(10, "220") + Pair(20, "150") + Pair(40, "10") + Pair(50, "15") + Pair(51, "160") +
            Pair(0, "INSERT") + Pair(5, "E1") + Pair(2, "PRESERVE_BLOCK") + Pair(8, "PRESERVE_LAYER") + Pair(10, "100") + Pair(20, "200") + Pair(41, "1") + Pair(42, "1") + Pair(50, "0") +
            Pair(0, "INSERT") + Pair(5, "E2") + Pair(2, "PRESERVE_BLOCK") + Pair(8, "PRESERVE_LAYER") + Pair(10, "150") + Pair(20, "200") + Pair(41, "1") + Pair(42, "1") + Pair(50, "0") +
            Pair(0, "TEXT") + Pair(5, "E3") + common + Pair(10, "100") + Pair(20, "250") + Pair(40, "5") + Pair(1, "UNSUPPORTED_TEXT_MUST_STAY") +
            Pair(0, "SPLINE") + Pair(5, "E4") + common + Pair(70, "0") + Pair(71, "3") + Pair(72, "8") + Pair(73, "4") +
            Pair(10, "100") + Pair(20, "280") + Pair(10, "110") + Pair(20, "290") + Pair(10, "120") + Pair(20, "280") + Pair(10, "130") + Pair(20, "290") +
            Pair(0, "ELLIPSE") + Pair(5, "E5") + common + Pair(10, "220") + Pair(20, "250") + Pair(11, "20") + Pair(21, "0") + Pair(40, "0.5") + Pair(41, "0") + Pair(42, "6.283185307179586") +
            Pair(0, "ENDSEC") + Pair(0, "SECTION") + Pair(2, "OBJECTS") + extraObjects + Pair(0, "ENDSEC") + Pair(0, "EOF");
    }

    private static void Bytes(byte[] actual, byte[] expected, string name) =>
        Check(actual.AsSpan().SequenceEqual(expected), name);

    private static void Check(bool condition, string name)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(name);
    }

    private static void Throws(Action action, string name)
    {
        bool thrown = false;
        try { action(); }
        catch (IOException) { thrown = true; }
        catch (InvalidOperationException) { thrown = true; }
        catch (UnauthorizedAccessException) { thrown = true; }
        Check(thrown, name);
    }
}
