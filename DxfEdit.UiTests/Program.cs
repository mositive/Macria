using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

internal static class Program
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _assertions;
    private static Window? _window;
    private static Application? _application;
    private static string? _fixtureDirectory;
    private static readonly RoutedEventArgs Event = new();

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Usage: dotnet DxfEdit.UiTests.dll <Macria x64 DLL>");
            string applicationPath = System.IO.Path.GetFullPath(args[0]);
            Assembly assembly = Assembly.LoadFrom(applicationPath);
            _application = (Application)Activator.CreateInstance(assembly.GetType("Macria.App", true)!)!;
            Call(_application, "InitializeComponent");
            _application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = (Window)Activator.CreateInstance(assembly.GetType("Macria.OnizlemeWindow", true)!)!;
            _fixtureDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Macria-DxfEdit-UiTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_fixtureDirectory);
            string sourcePath = System.IO.Path.Combine(_fixtureDirectory, "source.dxf");
            File.WriteAllText(sourcePath, Fixture, new UTF8Encoding(false));
            byte[] originalBytes = File.ReadAllBytes(sourcePath);
            object?[] readerArguments = { sourcePath, null };
            object model = assembly.GetType("Macria.DxfOkuyucu", true)!
                .GetMethod("Oku", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, readerArguments) ?? throw new InvalidOperationException("Fixture parse failed: " + readerArguments[1]);
            Call(_window, "Goster", "Smoke fixture", model, "test", sourcePath);
            Layout();
            object initialSession = Session;
            object[] rootEntities = Entities(model).Where(entity => Field(entity, "KaynakKayit") != null).ToArray();
            object line = rootEntities.First(entity => TextField(entity, "Tip") == "LINE");
            object secondLine = rootEntities.Where(entity => TextField(entity, "Tip") == "LINE").Skip(1).First();
            object circle = rootEntities.Single(entity => TextField(entity, "Tip") == "CIRCLE");
            object arc = rootEntities.Single(entity => TextField(entity, "Tip") == "ARC");
            int originalEntityCount = Entities(model).Length;
            int originalPathCount = Paths(model).Length;
            string originalGeometry = Drawing("cizim").Data.ToString();

            foreach (string name in new[] { "btnSec", "btnMesafe", "btnCizgiCizgi", "btnTemizle", "btnSigdir", "btnAc", "chkEditModu", "txtEditDurumu", "btnSil", "btnUndo", "btnRedo", "btnKaydet", "btnFarkliKaydet" })
                Check(_window.FindName(name) != null, "Control exists: " + name);
            Check(!BoolField(_window, "_editModu"), "Edit mode starts off");
            Check(!Button("btnSil").IsEnabled && !Button("btnUndo").IsEnabled && !Button("btnKaydet").IsEnabled, "Edit commands disabled initially");
            Call(_window, "Sec", line);
            DispatchDelete();
            Call(_window, "btnSil_Click", _window, Event);
            Check(!Dirty && !Property<bool>(Session, "GeriAlabilir"), "Edit-off Delete/button cannot dirty document or history");
            Check(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(originalBytes), "Edit-off Delete preserves physical file");
            Check(Entities(Field(_window, "_cizimModel")!).Length == originalEntityCount, "Edit-off Delete preserves preview entities");

            Call(_window, "btnMesafe_Click", _window, Event);
            Call(_window, "MesafeNoktasi", new Point(10, 0));
            Check(Drawing("mesafeNoktasiIsareti").Data != null, "Existing point measurement first marker exists");
            EditCheckBox.IsChecked = true;
            Check(BoolField(_window, "_editModu"), "Edit mode enables");
            Check((string)Field(_window, "_kip")! == "Sec", "Entering edit mode selects entity mode");
            Check(Drawing("mesafeNoktasiIsareti").Data == null && Drawing("mesafeCizgisi").Data == null, "Entering edit mode clears visual measurement");
            Check(!Button("btnMesafe").IsEnabled && !Button("btnCizgiCizgi").IsEnabled, "Measurement modes disabled while editing");
            Check(((TextBlock)_window.FindName("txtEditDurumu")!).Visibility == Visibility.Visible, "EDIT indicator visible");
            Check(!Property<bool>(Session, "GeriAlabilir"), "Visual measurement excluded from edit history");

            Point remainingPoint = ((Point[])Field(secondLine, "Noktalar")!)[0];
            Point screenBeforeDelete = Screen(remainingPoint);
            int snapsBeforeDelete = SnapCount;
            foreach (object entity in new[] { line, circle, arc })
            {
                int countBefore = Entities(Field(_window, "_cizimModel")!).Length;
                Call(_window, "Sec", entity);
                Check(Button("btnSil").IsEnabled, TextField(entity, "Tip") + " source entity can be selected for deletion");
                Check(Drawing("seciliCizim").Data != null, "Selection geometry exists before delete");
                if (ReferenceEquals(entity, circle)) DispatchDelete();
                else Call(_window, "btnSil_Click", _window, Event);
                object editedModel = Field(_window, "_cizimModel")!;
                Check(Entities(editedModel).Length == countBefore - 1, TextField(entity, "Tip") + " deleted from metadata");
                Check(!Entities(editedModel).Contains(entity), "Deleted source identity removed from preview");
                Check(!Paths(editedModel).Contains((Point[])Field(entity, "Noktalar")!), "Deleted source path removed from preview drawing");
                Check(Dirty && _window.Title.EndsWith(" *", StringComparison.Ordinal), "Delete sets dirty title");
                Check(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(originalBytes), "Delete remains memory-only");
            }
            Check(SnapCount < snapsBeforeDelete, "Deleted LINE/CIRCLE/ARC snap candidates removed");
            CheckPoint(Screen(remainingPoint), screenBeforeDelete, "Delete does not reposition remaining geometry");
            CheckPoint(Model(Screen(remainingPoint)), remainingPoint, "Post-delete common transform round-trip");
            foreach (object entity in new[] { arc, circle, line })
            {
                object? sourceRecord = Field(entity, "KaynakKayit");
                Call(_window, "btnUndo_Click", _window, Event);
                object restored = Entities(Field(_window, "_cizimModel")!).Single(candidate => ReferenceEquals(candidate, entity));
                Check(ReferenceEquals(Field(restored, "KaynakKayit"), sourceRecord), "Undo restores identical raw-record identity");
                Check(ReferenceEquals(Field(restored, "Noktalar"), Field(entity, "Noktalar")), "Undo restores identical geometry point array");
            }
            Check(!Dirty, "Undo all returns to saved dirty baseline");
            Check(Entities(Field(_window, "_cizimModel")!).Length == originalEntityCount, "Undo all restores entity count");
            Check(Paths(Field(_window, "_cizimModel")!).Length == originalPathCount, "Undo all restores path count");
            Check(Drawing("cizim").Data.ToString() == originalGeometry, "Undo all restores exact preview geometry");
            Call(_window, "btnRedo_Click", _window, Event);
            Check(Dirty && !Entities(Field(_window, "_cizimModel")!).Contains(line), "Redo deletes same LINE again");

            Call(_window, "Sec", secondLine);
            Matrix matrixBeforePanZoom = ((MatrixTransform)Field(_window, "_modelToScreen")!).Matrix;
            ((ScaleTransform)_window.FindName("olcek")!).ScaleX = 2;
            ((ScaleTransform)_window.FindName("olcek")!).ScaleY = 2;
            ((TranslateTransform)_window.FindName("kaydir")!).X = 35;
            ((TranslateTransform)_window.FindName("kaydir")!).Y = -20;
            Call(_window, "CizgiKalinligi");
            Layout();
            Check(((MatrixTransform)Field(_window, "_modelToScreen")!).Matrix == matrixBeforePanZoom, "Zoom does not refit post-delete bounds");
            CheckPoint(Model(Screen(remainingPoint)), remainingPoint, "Pan/zoom common transform round-trip");
            Point endpointScreen = Screen(remainingPoint);
            Point nearEndpointScreen = endpointScreen + new Vector(0, 6);
            object? snap = Call(_window, "SnapBul", Model(nearEndpointScreen), nearEndpointScreen);
            Check(snap != null, "Existing snap captures endpoint within six pixels after pan/zoom");
            CheckPoint((Point)Field(snap!, "Nokta")!, remainingPoint, "Pan/zoom endpoint snap resolves to actual model endpoint");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("seciliCizim").RenderTransform), "Main geometry and selection share transform instance");
            Point[] selectionScreen = ((Point[])Field(secondLine, "Noktalar")!).Select(Screen).ToArray();
            Point pathScreen = Drawing("seciliCizim").TranslatePoint(new Point(remainingPoint.X, -remainingPoint.Y), (UIElement)_window.FindName("cizimAlani")!);
            CheckPoint(pathScreen, selectionScreen[0], "Selection screen position matches main screen position after pan/zoom");

            EditCheckBox.IsChecked = false;
            Check(Button("btnMesafe").IsEnabled && Button("btnCizgiCizgi").IsEnabled, "Measurement modes restored outside edit");
            bool undoBefore = Property<bool>(Session, "GeriAlabilir");
            bool redoBefore = Property<bool>(Session, "Yineleabilir");
            int entityBeforeOffCommands = Entities(Field(_window, "_cizimModel")!).Length;
            Call(_window, "btnUndo_Click", _window, Event);
            Call(_window, "btnRedo_Click", _window, Event);
            Call(_window, "btnSil_Click", _window, Event);
            Call(_window, "btnKaydet_Click", _window, Event);
            DispatchDelete();
            Check(entityBeforeOffCommands == Entities(Field(_window, "_cizimModel")!).Length, "Edit-off command methods preserve in-memory edits");
            Check(Property<bool>(Session, "GeriAlabilir") == undoBefore && Property<bool>(Session, "Yineleabilir") == redoBefore, "Edit-off command methods preserve history");
            Check(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(originalBytes), "Edit-off save command cannot write");

            Call(_window, "btnMesafe_Click", _window, Event);
            Call(_window, "MesafeNoktasi", new Point(10, 30));
            Call(_window, "MesafeNoktasi", new Point(110, 30));
            AssertDimension("Existing point dimension after edit exit");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("mesafeCizgisi").RenderTransform), "Point dimension shares model transform");
            Call(_window, "btnTemizle_Click", _window, Event);
            Check(Dirty && Entities(Field(_window, "_cizimModel")!).Length == entityBeforeOffCommands, "Clear removes overlays but preserves pending edits");
            Check(Drawing("mesafeCizgisi").Data == null && Drawing("olcuYazisi").Data == null, "Clear removes dimension and text");
            Check(Property<bool>(Session, "GeriAlabilir") == undoBefore && Property<bool>(Session, "Yineleabilir") == redoBefore, "Point measurement and Clear excluded from Undo history");

            EditCheckBox.IsChecked = true;
            Call(_window, "btnUndo_Click", _window, Event);
            EditCheckBox.IsChecked = false;
            Check(!Dirty, "Undo before line dimension returns saved baseline");
            Call(_window, "btnCizgiCizgi_Click", _window, Event);
            Call(_window, "CizgiOlcuSec", line);
            Call(_window, "CizgiOlcuSec", secondLine);
            Call(_window, "CizgiOlcuYerlesiminiGuncelle", new Point(135, 15));
            Check(Field(_window, "_cizgiOlcuAsamasi")!.ToString() == "PlacementWaiting", "Existing line dimension enters placement preview");
            AssertDimension("Existing line dimension placement preview");
            Call(_window, "CizgiOlcuTikla", new Point(135, 15));
            Check(Field(_window, "_cizgiOlcuAsamasi")!.ToString() == "Completed", "Third click completes line dimension");
            AssertDimension("Existing completed line dimension");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("mesafeCizgisi").RenderTransform) &&
                  ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("olcuYazisi").RenderTransform), "Line dimension/text share common transform");
            Check(!Dirty && !Property<bool>(Session, "GeriAlabilir"), "Line selection and placement excluded from edit history");

            EditCheckBox.IsChecked = true;
            Call(_window, "Sec", circle);
            Call(_window, "btnSil_Click", _window, Event);
            object dirtyModel = Field(_window, "_cizimModel")!;
            Call(_window, "Goster", "Repeat feed", model, "test", sourcePath);
            Check(ReferenceEquals(Session, initialSession) && ReferenceEquals(Field(_window, "_cizimModel"), dirtyModel) && Dirty, "Same source feed preserves dirty session/model");
            Call(_window, "Bosalt", "Same-source failure", "test", sourcePath);
            Check(ReferenceEquals(Session, initialSession) && Dirty, "Same source Bosalt preserves dirty session");
            string saveAsPath = System.IO.Path.Combine(_fixtureDirectory, "saved-as.dxf");
            Call(Session, "FarkliKaydet", saveAsPath);
            Call(_window, "EditKayitSonucunuGuncelle", "Smoke Save As");
            Check(!Dirty && TextField(_window, "_yol") == saveAsPath, "Save As refreshes preview source path and clean state");
            Check(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(originalBytes), "Save As preserves original physical source");
            Call(_window, "Goster", "Original feed after Save As", model, "test", sourcePath);
            Check(ReferenceEquals(Session, initialSession) && TextField(_window, "_yol") == saveAsPath, "Original same-source feed cannot replace Save As document");
            object blockEntity = Entities(model).First(entity => Field(entity, "KaynakKayit") == null);
            Call(_window, "Sec", blockEntity);
            Check(!Button("btnSil").IsEnabled, "BLOCK/INSERT entity remains read-only in edit UI");
            int countBeforeBlocked = Entities(Field(_window, "_cizimModel")!).Length;
            DispatchDelete();
            Check(!Dirty && Entities(Field(_window, "_cizimModel")!).Length == countBeforeBlocked, "Read-only BLOCK/INSERT Delete cannot modify document");
            VerifyAxisDimensions(assembly);
            Check(File.ReadAllBytes(sourcePath).AsSpan().SequenceEqual(originalBytes), "Axis dimension placement never writes the DXF");
            VerifyStageTwo(assembly);
            VerifyCornerCommands(assembly);
            VerifyCodeHealthContracts(assembly);
            Console.WriteLine($"PASS: {_assertions} noninteractive WPF UI assertions. No window shown; interactive acceptance still required.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL after " + _assertions + " assertions: " + Unwrap(exception));
            return 1;
        }
        finally
        {
            try
            {
                if (_window != null)
                {
                    object? session = Field(_window, "_editOturumu");
                    if (session != null)
                    {
                        Call(session, "Vazgec");
                        Call(_window, "EditOnizlemesiniGuncelle", "Smoke cleanup");
                    }
                    _window.Close();
                }
                _application?.Shutdown();
            }
            catch (Exception cleanupException)
            {
                Console.Error.WriteLine("WPF cleanup: " + Unwrap(cleanupException).Message);
            }
            if (_fixtureDirectory != null)
            {
                try
                {
                    string temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())
                        .TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                    string fixtureTarget = System.IO.Path.GetFullPath(_fixtureDirectory);
                    if (!fixtureTarget.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                        !System.IO.Path.GetFileName(fixtureTarget).StartsWith("Macria-DxfEdit-UiTests-", StringComparison.Ordinal))
                        throw new InvalidOperationException("Refusing cleanup outside this smoke test's temporary fixture directory.");
                    Directory.Delete(fixtureTarget, true);
                }
                catch (Exception cleanupException) { Console.Error.WriteLine("Fixture cleanup: " + cleanupException.Message); }
            }
        }
    }

    private static object Session => Field(_window!, "_editOturumu")!;
    private static bool Dirty => Property<bool>(Session, "Degisti");
    private static int SnapCount => ((ICollection)Field(_window!, "_snapNoktalari")!).Count;
    private static CheckBox EditCheckBox => (CheckBox)_window!.FindName("chkEditModu")!;
    private static Button Button(string name) => (Button)_window!.FindName(name)!;
    private static ShapePath Drawing(string name) => (ShapePath)_window!.FindName(name)!;
    private static Point Screen(Point modelPoint) => (Point)Call(_window!, "EkranNoktasi", modelPoint)!;
    private static Point Model(Point screenPoint) => (Point)Call(_window!, "ModelNoktasi", screenPoint)!;
    private static object[] Entities(object model) => ((IEnumerable)Field(model, "Entityler")!).Cast<object>().ToArray();
    private static Point[][] Paths(object model) => ((IEnumerable)Field(model, "Yollar")!).Cast<Point[]>().ToArray();
    private static string TextField(object instance, string name) => (string)Field(instance, name)!;
    private static bool BoolField(object instance, string name) => (bool)Field(instance, name)!;
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, InstanceFlags)?.GetValue(instance)
        ?? (instance.GetType().GetField(name, InstanceFlags) != null ? null : throw new MissingFieldException(instance.GetType().FullName, name));
    private static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name, InstanceFlags)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] arguments) => instance.GetType().GetMethod(name, InstanceFlags)!
        .Invoke(instance, arguments);
    private static void Layout()
    {
        _window!.Measure(new Size(860, 640));
        _window.Arrange(new Rect(0, 0, 860, 640));
        _window.UpdateLayout();
    }
    private static void DispatchDelete()
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticSource(), Environment.TickCount, Key.Delete)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Call(_window!, "OnizlemeWindow_PreviewKeyDown", _window, key);
    }
    private static void AssertDimension(string label)
    {
        Geometry? geometry = Drawing("mesafeCizgisi").Data;
        Check(geometry != null && !geometry.Bounds.IsEmpty, label + ": dimension geometry exists");
        PathGeometry paths = PathGeometry.CreateFromGeometry(geometry!);
        Check(paths.Figures.Count == 5, label + ": two extensions, dimension and two ticks exist (figures=" + paths.Figures.Count + ", geometry=" + geometry + ")");
        Geometry? text = Drawing("olcuYazisi").Data;
        Check(text != null && !text.Bounds.IsEmpty, label + ": measurement text geometry exists");
    }

    private static void VerifyStageTwo(Assembly assembly)
    {
        string Pair(int code, string value) => code + "\r\n" + value + "\r\n";
        string Line(string handle, Point a, Point b) => Pair(0, "LINE") + Pair(5, handle) + Pair(8, "PART") +
            Pair(10, a.X.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Pair(20, a.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
            Pair(11, b.X.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Pair(21, b.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string Document(string entities) => Pair(0, "SECTION") + Pair(2, "HEADER") + Pair(9, "$ACADVER") + Pair(1, "AC1015") +
            Pair(9, "$HANDSEED") + Pair(5, "FFFF") + Pair(0, "ENDSEC") + Pair(0, "SECTION") + Pair(2, "ENTITIES") + entities + Pair(0, "ENDSEC") + Pair(0, "EOF");
        object Read(string path)
        {
            object?[] arguments = { path, null };
            return assembly.GetType("Macria.DxfOkuyucu", true)!.GetMethod("Oku", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, arguments) ?? throw new InvalidOperationException("Stage two fixture parse failed: " + arguments[1]);
        }
        int SelectionCount() => ((IEnumerable)Field(_window!, "_editSecimi")!).Cast<object>().Count();
        void Select(object entity, bool ctrl = false) => Call(_window!, "EditSec", entity, ctrl);
        Array Typed(object[] selection)
        {
            Array array = Array.CreateInstance(assembly.GetType("Macria.DxfEntity", true)!, selection.Length);
            for (int i = 0; i < selection.Length; i++) array.SetValue(selection[i], i);
            return array;
        }
        string source = System.IO.Path.Combine(_fixtureDirectory!, "stage-two.dxf");
        string entitiesText = Line("11", new Point(), new Point(100, 0)) + Line("12", new Point(), new Point(0, 100));
        for (int i = 0; i < 5; i++) entitiesText += Pair(0, "CIRCLE") + Pair(5, (32 + i).ToString("X")) + Pair(8, "PART") +
            Pair(10, (200 + i * 25).ToString()) + Pair(20, "100") + Pair(40, "5");
        entitiesText += Pair(0, "ARC") + Pair(5, "40") + Pair(8, "PART") + Pair(10, "400") + Pair(20, "100") + Pair(40, "10") + Pair(50, "10") + Pair(51, "100");
        File.WriteAllText(source, Document(entitiesText), new UTF8Encoding(false));
        byte[] original = File.ReadAllBytes(source);
        object model = Read(source);
        Call(_window!, "Goster", "Stage two", model, "test", source);
        Layout();
        EditCheckBox.IsChecked = true;
        foreach (string name in new[] { "btnPah", "btnRadius", "btnBirlestir", "rbTekKose", "rbSeciliKontur", "rbTumKonturlar", "secimCercevesi" })
            Check(_window!.FindName(name) != null, "Stage two actual compiled XAML contains " + name);
        Check(_window!.FindName("cmbKoseKapsami") == null && ((RadioButton)_window.FindName("rbTekKose")!).IsChecked == true,
            "Obsolete geometry ComboBox removed; single-corner scope is default");
        object[] all = Entities(model), circles = all.Where(e => TextField(e, "Tip") == "CIRCLE").ToArray();
        Select(circles[0]); Select(circles[1]);
        Check(SelectionCount() == 1, "Normal click replaces prior selection");
        Select(circles[0]);
        foreach (object circle in circles.Skip(1)) Select(circle, true);
        Check(SelectionCount() == 5, "Ctrl selects five CIRCLE entities");
        Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 5, "All five selected entities highlighted");
        Check(((TextBlock)_window.FindName("txtEntityInfo")!).Text.Contains("5 öğe seçildi"), "Multiple selection count shown");
        Select(circles[2], true);
        Check(SelectionCount() == 4, "Ctrl toggles selected circle out");
        Select(circles[2], true);
        Check(SelectionCount() == 5, "Ctrl restores circle without clearing others");
        _window.GetType().GetField("_cerceveSeciliyor", InstanceFlags)!.SetValue(_window, true);
        ((System.Windows.Shapes.Rectangle)_window.FindName("secimCercevesi")!).Visibility = Visibility.Visible;
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticSource(), Environment.TickCount, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Call(_window, "OnizlemeWindow_PreviewKeyDown", _window, escape);
        Check(SelectionCount() == 0 && !BoolField(_window, "_cerceveSeciliyor") &&
            ((System.Windows.Shapes.Rectangle)_window.FindName("secimCercevesi")!).Visibility == Visibility.Collapsed,
            "ESC cancels rectangle and multi-selection without modifying entities");
        Check(!Dirty && !Property<bool>(Session, "GeriAlabilir"), "Selection ESC excluded from edit transaction history");
        Select(circles[0]);
        foreach (object circle in circles.Skip(1)) Select(circle, true);
        ((ScaleTransform)_window.FindName("olcek")!).ScaleX = 2;
        ((ScaleTransform)_window.FindName("olcek")!).ScaleY = 2;
        ((TranslateTransform)_window.FindName("kaydir")!).X = -24;
        ((TranslateTransform)_window.FindName("kaydir")!).Y = 18;
        Call(_window, "CizgiKalinligi");
        Layout();
        Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 5 &&
            ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("seciliCizim").RenderTransform), "Pan/zoom retains all multi-selection highlights on shared transform");
        Point center = (Point)Field(circles[0], "Merkez")!;
        object? centerSnap = Call(_window, "SnapBul", Model(Screen(center)), Screen(center));
        Check(centerSnap != null && TextField(centerSnap, "Tip") == "Center", "Center snap remains aligned after multi-selection pan/zoom");
        Call(_window, "EditCerceveSec", new Rect(195, 95, 110, 10), false);
        Check(SelectionCount() == 5, "Rectangle completely contains five circles");
        Call(_window, "EditCerceveSec", new Rect(195, 95, 107, 10), false);
        Check(SelectionCount() == 4, "Rectangle rejects partially contained last circle");
        Select(all[0]);
        Call(_window, "EditCerceveSec", new Rect(195, 95, 110, 10), true);
        Check(SelectionCount() == 6, "Ctrl rectangle adds without removing existing LINE");
        Call(_window, "EditCerceveSec", new Rect(195, 95, 110, 10), false);
        Point stablePoint = new Point(50, 0), stableScreen = Screen(stablePoint);
        Call(_window, "btnSil_Click", _window, Event);
        Check(Entities(Field(_window, "_cizimModel")!).Length == 3 && SelectionCount() == 0 && Dirty, "Bulk Delete removes all five and clears overlays");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Bulk Delete does not write source");
        CheckPoint(Screen(stablePoint), stableScreen, "Bulk Delete preserves common geometry transform");
        Call(_window, "btnUndo_Click", _window, Event);
        Check(Entities(Field(_window, "_cizimModel")!).Length == 8 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "One Undo restores all five circles");
        Call(_window, "btnRedo_Click", _window, Event);
        Check(Entities(Field(_window, "_cizimModel")!).Length == 3 && Dirty, "One Redo deletes all five circles");
        Call(_window, "btnUndo_Click", _window, Event);
        foreach (bool fillet in new[] { false, true })
        {
            object[] lines = Entities(Field(_window, "_cizimModel")!).Where(e => TextField(e, "Tip") == "LINE").ToArray();
            Select(lines[0]); Select(lines[1], true);
            Check((bool)Call(_window, "KoseUygula", Typed(lines), 5.0, fillet, false)!, "Actual preview applies " + (fillet ? "Radius" : "Pah"));
            Check(Entities(Field(_window, "_cizimModel")!).Length == 9 && Dirty && SelectionCount() == 0, "Corner trim and new entity redraw together");
            object generated = Entities(Field(_window, "_cizimModel")!).Single(e =>
                Field(e, "KaynakKayit") is object record && Property<bool>(record, "Yeni"));
            Select(generated);
            Check(Button("btnSil").IsEnabled && Drawing("seciliCizim").Data != null, "Generated entity selectable using real session record identity");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("seciliCizim").RenderTransform), "Generated highlight uses shared matrix");
            CheckPoint(Model(Screen(stablePoint)), stablePoint, "Corner preview shared transform round-trip");
            Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Corner edit memory only until Save");
            Call(_window, "btnUndo_Click", _window, Event);
            Check(!Dirty && Entities(Field(_window, "_cizimModel")!).Length == 8, "Single corner Undo restores original preview");
            Check(!(bool)Call(_window, "KoseUygula", Typed(lines), 1000.0, fillet, false)!, "Oversize corner rejected in actual preview flow");
            Check(!Dirty && Entities(Field(_window, "_cizimModel")!).Length == 8, "Rejected corner creates no partial preview edit");
        }
        object row = Activator.CreateInstance(assembly.GetType("Macria.SheetRow", true)!)!;
        int statusNotifications = 0;
        Action<string> saved = path => { Check(path == source, "Edited row event identifies original DXF"); Call(row, "DxfEditKaydedildi"); statusNotifications++; };
        _window.GetType().GetEvent("OrijinalEditKaydedildi", InstanceFlags)!.GetAddMethod(true)!.Invoke(_window, new object[] { saved });
        Check(Property<string>(row, "DxfDurumKodu") == "" && statusNotifications == 0, "Opening, measuring and memory-only editing never marks row saved");
        Select(circles[0]); Call(_window, "btnSil_Click", _window, Event);
        Check((bool)Call(_window, "EditOnayliKaydet")!, "Explicitly approved original Save succeeds");
        Check(statusNotifications == 1 && Property<string>(row, "DxfDurumKodu") == "Editlendi" &&
            Property<string>(row, "DxfDurumAciklamasi") == "🛠️ Editlendi ve kaydedildi" && Property<string>(row, "DxfDurumIkonu") == "🛠️", "Successful original edit Save sets precise row status/emoji");
        Check(!Dirty && Entities(Read(source)).Length == 7, "Saved edit reopens with actual changed geometry");
        Call(_window, "EditOnayliKaydet");
        Check(statusNotifications == 1, "No-op Save does not emit edited row event");
        Call(_window, "btnUndo_Click", _window, Event);
        Action<string> failingNotification = _ => throw new InvalidOperationException("synthetic notification failure");
        var originalSaveEvent = _window.GetType().GetEvent("OrijinalEditKaydedildi", InstanceFlags)!;
        originalSaveEvent.GetAddMethod(true)!.Invoke(_window, new object[] { failingNotification });
        Check((bool)Call(_window, "EditOnayliKaydet")!, "Post-commit notification exception cannot report successful disk Save as failed");
        Check(!Dirty && ((TextBlock)_window.FindName("txtEntityInfo")!).Text.Contains("Arayüz bildirimi güncellenemedi"), "Successful Save remains clean and reports only UI notification warning");
        originalSaveEvent.GetRemoveMethod(true)!.Invoke(_window, new object[] { failingNotification });
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Undo followed by approved Save restores initial DXF bytes");
        string saveAs = System.IO.Path.Combine(_fixtureDirectory!, "stage-two-as.dxf");
        Select(circles[1]); Call(_window, "btnSil_Click", _window, Event);
        int beforeSaveAs = statusNotifications;
        Call(Session, "FarkliKaydet", saveAs);
        Call(_window, "EditKayitSonucunuGuncelle", "Stage two Save As");
        Check(statusNotifications == beforeSaveAs && File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Save As leaves original row and DXF untouched");
        Select(circles[2]); Call(_window, "btnSil_Click", _window, Event);
        Call(_window, "EditOnayliKaydet");
        Check(statusNotifications == beforeSaveAs && File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Later Save to adopted Save As target cannot mark old source row");
        Call(_window, "btnTemizle_Click", _window, Event);
        Check(SelectionCount() == 0 && Drawing("seciliCizim").Data == null && !Dirty, "Clear removes edit selection without changing saved file/history");
        _window.GetType().GetEvent("OrijinalEditKaydedildi", InstanceFlags)!.GetRemoveMethod(true)!.Invoke(_window, new object[] { saved });
        foreach (bool fillet in new[] { false, true })
        {
            string contourPath = System.IO.Path.Combine(_fixtureDirectory!, "stage-two-contour-" + fillet + ".dxf");
            Point[] vertices = { new Point(0, 0), new Point(100, 0), new Point(100, 100), new Point(0, 100) };
            string contourText = string.Concat(Enumerable.Range(0, 4).Select(i => Line((80 + i).ToString("X"), vertices[i], vertices[(i + 1) % 4])));
            File.WriteAllText(contourPath, Document(contourText), new UTF8Encoding(false));
            byte[] contourOriginal = File.ReadAllBytes(contourPath);
            object contourModel = Read(contourPath);
            Call(_window, "Goster", "All corners", contourModel, "test", contourPath);
            Layout();
            EditCheckBox.IsChecked = true;
            object[] contourLines = Entities(contourModel);
            Call(_window, "EditCerceveSec", new Rect(-1, -1, 102, 102), false);
            Check(SelectionCount() == 4, "Actual preview rectangle selects complete closed contour");
            Check((bool)Call(_window, "KoseUygula", Typed(contourLines), 5.0, fillet, true)!, "All-corner command applies to actual preview: " + fillet);
            Check(Entities(Field(_window, "_cizimModel")!).Length == 8 && Dirty && SelectionCount() == 0, "All-corner trim/new geometry redraw atomically");
            Check(File.ReadAllBytes(contourPath).AsSpan().SequenceEqual(contourOriginal), "All-corner preview never writes source");
            Call(_window, "btnUndo_Click", _window, Event);
            Check(Entities(Field(_window, "_cizimModel")!).Length == 4 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "One Undo restores every contour corner in actual UI");
            Call(_window, "btnRedo_Click", _window, Event);
            Check(Entities(Field(_window, "_cizimModel")!).Length == 8 && Dirty, "One Redo reapplies every contour corner");
            Call(_window, "EditOnayliKaydet");
            Check(!Dirty && Entities(Read(contourPath)).Length == 8, "Saved all-corner geometry reopens as actual DXF entities");
            Call(_window, "btnUndo_Click", _window, Event);
            Call(_window, "EditOnayliKaydet");
            Check(File.ReadAllBytes(contourPath).AsSpan().SequenceEqual(contourOriginal), "Saved all-corner Undo restores exact initial DXF bytes");
            Check(!(bool)Call(_window, "KoseUygula", Typed(contourLines[..3]), 5.0, fillet, true)!, "Open contour rejected in actual preview flow");
            Check(!Dirty && Entities(Field(_window, "_cizimModel")!).Length == 4, "Open contour rejection leaves no partial UI edit");
        }
    }

    private static void VerifyCornerCommands(Assembly assembly)
    {
        string Pair(int code, string value) => code + "\r\n" + value + "\r\n";
        string Line(int id, Point a, Point b) => Pair(0, "LINE") + Pair(5, id.ToString("X")) + Pair(8, "PART") +
            Pair(10, a.X.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Pair(20, a.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
            Pair(11, b.X.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Pair(21, b.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        object Load(string name, string entities)
        {
            Call(Session, "Vazgec");
            Call(_window!, "EditOnizlemesiniGuncelle", "reset");
            string path = System.IO.Path.Combine(_fixtureDirectory!, "ux-" + name + ".dxf");
            string doc = Pair(0, "SECTION") + Pair(2, "HEADER") + Pair(9, "$ACADVER") + Pair(1, "AC1015") + Pair(9, "$HANDSEED") + Pair(5, "FFFF") +
                Pair(0, "ENDSEC") + Pair(0, "SECTION") + Pair(2, "ENTITIES") + entities + Pair(0, "ENDSEC") + Pair(0, "EOF");
            File.WriteAllText(path, doc, new UTF8Encoding(false));
            object?[] arguments = { path, null };
            object model = assembly.GetType("Macria.DxfOkuyucu", true)!.GetMethod("Oku", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, arguments) ?? throw new InvalidOperationException("UX fixture parse failed");
            Call(_window!, "Goster", name, model, "test", path);
            Layout(); EditCheckBox.IsChecked = true; Layout();
            return model;
        }
        void Activate(string button) { Call(_window!, button + "_Click", _window, Event); Layout(); }
        void Click(Point p) => Call(_window!, "KoseKomutTikla", p);
        void Hover(Point p) => Call(_window!, "KoseKomutOnizle", p);
        int Count() => Entities(Field(_window!, "_cizimModel")!).Length;
        void Undo() => Call(_window!, "btnUndo_Click", _window, Event);
        void AssertPreview(string label)
        {
            Check(Drawing("koseOnizlemesi").Data != null && Drawing("koseIsareti").Data != null && Drawing("seciliCizim").Data != null, label + ": preview, intersection marker and LINE highlights exist");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("koseOnizlemesi").RenderTransform) &&
                ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("koseIsareti").RenderTransform), label + ": all preview layers share model matrix");
        }
        string corner = Line(1, new Point(), new Point(100, 0)) + Line(2, new Point(), new Point(0, 100));
        object model = Load("prepared", corner);
        object[] lines = Entities(model);
        Call(_window!, "EditSec", lines[0], false); Call(_window!, "EditSec", lines[1], true);
        Activate("btnPah");
        var toolbarScroll = (ScrollViewer)((Border)_window!.FindName("editAraclari")!).Child;
        Check(toolbarScroll.ScrollableWidth == 0, "Edit toolbar fits one line without horizontal scrolling at default window width");
        Check(Button("btnSec").Visibility == Visibility.Collapsed && Button("btnEditSec").Visibility == Visibility.Visible,
            "Edit mode displays one Select control, not duplicated across tool groups");
        Check(Field(_window!, "_koseKomutAsamasi")!.ToString() == "Prepared" && Count() == 2 && !Dirty, "Preselected LINE pair activates prepared command without memory edit");
        Check(((TextBlock)_window!.FindName("txtKoseKomutu")!).Text == "Pah:" && ((TextBox)_window.FindName("txtKoseDegeri")!).Text == "5.00", "Inline pah value is clear and defaults to 5.00 mm");
        AssertPreview("Prepared pah");
        Call(_window, "btnKoseUygula_Click", _window, Event);
        Check(Count() == 3 && Dirty && Field(_window, "_aktifKoseKomutu")!.ToString() == "Pah", "Prepared Apply commits one pah and keeps command active");
        Check(Drawing("koseOnizlemesi").Data == null && !Button("btnKoseUygula").IsEnabled, "Apply clears candidate and preview while retaining Pah");
        Undo(); Check(Count() == 2 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "One pah Undo restores original pair");
        Activate("btnRadius");
        Click(new Point(50, 0));
        Check(Field(_window, "_koseKomutAsamasi")!.ToString() == "SecondLineWaiting" && !Dirty, "First sequential LINE click requires no Ctrl and no model edit");
        Hover(new Point(0, 50)); AssertPreview("Sequential radius");
        Check(Count() == 2 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "Sequential preview excluded from edit history");
        ((ScaleTransform)_window.FindName("olcek")!).ScaleX = 2;
        ((ScaleTransform)_window.FindName("olcek")!).ScaleY = 2;
        ((TranslateTransform)_window.FindName("kaydir")!).X = 25;
        ((TranslateTransform)_window.FindName("kaydir")!).Y = -17;
        Call(_window, "CizgiKalinligi"); Layout();
        AssertPreview("Pan/zoom radius");
        Point marker = Drawing("koseIsareti").TranslatePoint(new Point(), (UIElement)_window.FindName("cizimAlani")!);
        CheckPoint(marker, Screen(new Point()), "Pan/zoom intersection marker overlays actual DXF corner");
        Click(new Point(0, 50));
        Check(Count() == 2 && !Dirty && !Property<bool>(Session, "GeriAlabilir") && Button("btnKoseUygula").IsEnabled, "Second sequential click locks preview without mutation/history");
        Hover(new Point(80, 80)); AssertPreview("Locked sequential target");
        Call(_window, "btnKoseUygula_Click", _window, Event);
        Check(Count() == 3 && Dirty, "Second sequential LINE click applies actual ARC");
        object arc = Entities(Field(_window, "_cizimModel")!).Single(e => TextField(e, "Tip") == "ARC");
        Check(Property<bool>(Field(arc, "KaynakKayit")!, "Yeni"), "Radius preview commits a real new session entity identity");
        Call(_window, "EditSec", arc, false); DispatchDelete();
        Check(Count() == 2, "Delete removes fillet ARC, leaves trimmed LINE pair");
        lines = Entities(Field(_window, "_cizimModel")!);
        Call(_window, "EditSec", lines[0], false); Call(_window, "EditSec", lines[1], true);
        Activate("btnBirlestir"); AssertPreview("Prepared join after ARC Delete");
        Check(((TextBox)_window.FindName("txtKoseDegeri")!).Visibility == Visibility.Collapsed, "Join does not ask irrelevant radius/pah value");
        Call(_window, "btnKoseUygula_Click", _window, Event);
        Check(Count() == 2 && Entities(Field(_window, "_cizimModel")!).All(e => ((Point[])Field(e, "Noktalar")!).Any(p => p == new Point())), "Delete + Join restores sharp corner and never adds third LINE");
        Undo(); Check(Count() == 2 && Entities(Field(_window, "_cizimModel")!).All(e => !((Point[])Field(e, "Noktalar")!).Contains(new Point())), "One Join Undo restores both previous trimmed endpoints");
        Call(_window, "btnRedo_Click", _window, Event);
        Check(Entities(Field(_window, "_cizimModel")!).All(e => ((Point[])Field(e, "Noktalar")!).Contains(new Point())), "One Join Redo restores both sharp endpoints");
        model = Load("single-click", corner);
        Activate("btnRadius"); Hover(new Point(1, 1)); AssertPreview("Corner hover");
        Check(!Dirty && Count() == 2, "Hover does not mutate model");
        Check(!Button("btnKoseUygula").IsEnabled, "Hover candidate is not an explicitly selected target");
        Call(_window, "btnKoseUygula_Click", _window, Event);
        Check(!Dirty && Count() == 2, "Disabled Apply cannot commit a hover candidate");
        Hover(new Point(80, 80));
        Check(Drawing("koseOnizlemesi").Data == null && Drawing("koseIsareti").Data == null, "Mouse leaving corner clears transient preview/marker");
        Hover(new Point(1, 1)); Click(new Point(1, 1));
        Check(Count() == 2 && !Dirty && !Property<bool>(Session, "GeriAlabilir") && Button("btnKoseUygula").IsEnabled, "Single corner click only prepares Radius");
        object[] unchanged = Entities(Field(_window, "_cizimModel")!);
        Check(unchanged.SequenceEqual(Entities(model)), "Click preserves original entity identities");
        Hover(new Point(80, 80)); AssertPreview("Locked single corner");
        ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "1000";
        Check(!Button("btnKoseUygula").IsEnabled && !Dirty && !((TextBlock)_window.FindName("txtEntityInfo")!).Text.Contains("Uygula'ya basın"), "Oversize locked preview disables Apply synchronously without misleading status");
        ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "5";
        Check(Button("btnKoseUygula").IsEnabled && !Dirty, "Valid value restores same locked target");
        Call(_window, "btnKoseUygula_Click", _window, Event);
        Check(Count() == 3 && Dirty && Field(_window, "_aktifKoseKomutu")!.ToString() == "Radius", "Only Apply commits Radius and retains active tool");
        Undo();
        Activate("btnBirlestir"); Click(new Point(50, 0));
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticSource(), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Call(_window, "OnizlemeWindow_PreviewKeyDown", _window, esc);
        Check(Field(_window, "_aktifKoseKomutu")!.ToString() == "Yok" && Drawing("seciliCizim").Data == null && Drawing("koseIsareti").Data == null && !Dirty,
            "ESC cancels active Join and all temporary selection/preview without edit");
        model = Load("ambiguous", corner + Line(3, new Point(), new Point(100, 100)));
        Activate("btnPah"); Click(new Point(1, 1));
        Check(!Dirty && Count() == 3 && !Property<bool>(Session, "GeriAlabilir") && ((TextBlock)_window.FindName("txtEntityInfo")!).Text.Contains("Birden fazla"), "Ambiguous nearby corner pairs rejected without choosing by endpoint order");
        model = Load("invalid-value", corner);
        lines = Entities(model); Call(_window, "EditSec", lines[0], false); Call(_window, "EditSec", lines[1], true);
        Activate("btnRadius");
        ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "NaN";
        Check(!Button("btnKoseUygula").IsEnabled && Drawing("koseOnizlemesi").Data == null && !Dirty, "Invalid inline value cannot retain stale committable preview");
        ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "6";
        AssertPreview("Live value R6"); Check(!Dirty, "Live numeric preview does not add history");
        Point[] vertices = { new Point(), new Point(100, 0), new Point(100, 100), new Point(0, 100) };
        string Rectangle(int firstId, Vector shift) => string.Concat(Enumerable.Range(0, 4).Select(i => Line(firstId + i, vertices[i] + shift, vertices[(i + 1) % 4] + shift)));
        foreach (bool fillet in new[] { false, true })
        {
            model = Load("selected-loop-" + fillet, Rectangle(16, new Vector()) + Rectangle(32, new Vector(200, 0)));
            Activate(fillet ? "btnRadius" : "btnPah");
            ((RadioButton)_window.FindName("rbSeciliKontur")!).IsChecked = true;
            Hover(new Point(50, 0)); AssertPreview("Selected contour hover");
            Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 4 && !Dirty, "One hovered LINE identifies only its closed loop, no Ctrl selection");
            Click(new Point(50, 0));
            Check(Count() == 8 && !Dirty && Button("btnKoseUygula").IsEnabled && !Property<bool>(Session, "GeriAlabilir"), "One contour edge click only locks four-corner preview");
            Hover(new Point(250, 0));
            Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 4 && Field(_window!, "_editSecimi") is System.Collections.IEnumerable selection && !selection.Cast<object>().Any(), "Command highlight stays locked and does not pollute normal selection");
            ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "1000";
            Check(!Button("btnKoseUygula").IsEnabled && !Dirty, "Invalid contour value rejects whole preview atomically");
            ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "5";
            Check(Button("btnKoseUygula").IsEnabled, "Contour target survives invalid value and mouse leaving target");
            Call(_window, "btnKoseUygula_Click", _window, Event);
            Check(Count() == 12 && Dirty, "Contour Apply edits four corners, leaves second rectangle intact");
            Undo(); Check(Count() == 8 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "One contour Undo restores all four corners");
            Activate(fillet ? "btnRadius" : "btnPah");
            ((RadioButton)_window.FindName("rbTumKonturlar")!).IsChecked = true;
            AssertPreview("All trustworthy contours");
            Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 8 && !Dirty, "All trustworthy contours preview includes both rectangles");
            Check(Button("btnKoseUygula").IsEnabled && ((TextBlock)_window.FindName("txtEntityInfo")!).Text == "2 güvenilir kontur / 8 köşe hazır.", "All-contour activation immediately enables Apply with accurate count/status");
            ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "1000";
            Check(!Button("btnKoseUygula").IsEnabled && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "One invalid all-contour plan rejects every change");
            ((TextBox)_window.FindName("txtKoseDegeri")!).Text = "5";
            Check(Button("btnKoseUygula").IsEnabled, "All-contour value recalculates without mouse input");
            Call(_window, "btnKoseUygula_Click", _window, Event);
            Check(Count() == 16 && Dirty, "All trustworthy contours commit in one operation");
            Undo(); Check(Count() == 8 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "Single all-contour Undo restores all eight corners");
            Check(File.ReadAllBytes(Property<string>(Session, "Yol")).AsSpan().SequenceEqual((byte[])Call(Session, "Cikti")!), "Command selection/preview/edit never writes physical DXF before Save");
        }
        foreach (string name in new[] { "btnEditSec", "btnSil", "btnBirlestir", "btnPah", "btnRadius", "btnUndo", "btnRedo", "btnKaydet", "btnFarkliKaydet" })
        {
            Button button = Button(name);
            Check(button.Width == 40 && button.Height == 40 && button.FontSize >= 24, name + ": compact icon-only button with readable size");
            Check(button.Content is string content && !content.Any(char.IsLetter), name + ": no toolbar text label");
            Check(button.ToolTip is string tooltip && tooltip.Contains('\n') && ToolTipService.GetInitialShowDelay(button) == 500, name + ": name/instructions tooltip and 500 ms delay");
            Check(!string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)), name + ": accessible tool name retained");
        }
        foreach (bool fillet in new[] { false, true })
        {
            model = Load("repeat-single-" + fillet, Rectangle(16, new Vector()));
            Activate(fillet ? "btnRadius" : "btnPah");
            Check(!Button("btnKoseUygula").IsEnabled, "New single-corner command starts with Apply disabled");
            Check(ReferenceEquals(Button(fillet ? "btnRadius" : "btnPah").Background, _window!.FindResource("AccentBrush")), "Active command has Macria accent highlight");
            Click(new Point(1, 1));
            byte[] before = File.ReadAllBytes(Property<string>(Session, "Yol"));
            Check(Count() == 4 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "First click is memory/history read-only");
            Call(_window, "btnKoseUygula_Click", _window, Event);
            Check(Count() == 5 && Dirty && !Button("btnKoseUygula").IsEnabled, "First Apply commits and clears target");
            Click(new Point(99, 99));
            Check(Count() == 5 && Button("btnKoseUygula").IsEnabled, "Second corner preview requires no command reactivation");
            Call(_window, "btnKoseUygula_Click", _window, Event);
            Check(Count() == 6 && File.ReadAllBytes(Property<string>(Session, "Yol")).AsSpan().SequenceEqual(before), "Second Apply remains memory-only");
            Undo(); Check(Count() == 5 && Dirty, "Second corner is a separate single transaction");
            Undo(); Check(Count() == 4 && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "Two Undo calls restore original whole model");
        }
        foreach (bool all in new[] { false, true })
        {
            model = Load("open-preview-" + all, Line(16, new Point(), new Point(100, 0)) + Line(17, new Point(100, 0), new Point(100, 100)));
            Activate("btnRadius");
            ((RadioButton)_window!.FindName(all ? "rbTumKonturlar" : "rbSeciliKontur")!).IsChecked = true;
            if (!all) Click(new Point(50, 0));
            Check(!Button("btnKoseUygula").IsEnabled && Drawing("koseOnizlemesi").Data == null && !Dirty && !Property<bool>(Session, "GeriAlabilir"), "Open loop disables Apply without partial geometry/history");
            Check(!((TextBlock)_window.FindName("txtEntityInfo")!).Text.Contains("Uygula'ya basın"), "Open-loop rejection cannot instruct disabled Apply");
            if (all) Check(((TextBlock)_window.FindName("txtEntityInfo")!).Text == "Uygulanabilir güvenilir kapalı LINE konturu bulunamadı.", "No trusted contours reports immediate explicit failure");
        }
        model = Load("cancel-preview", Rectangle(16, new Vector()));
        Activate("btnPah"); Click(new Point(1, 1));
        Call(_window!, "OnizlemeWindow_PreviewKeyDown", _window, esc);
        Check(!Dirty && !Property<bool>(Session, "GeriAlabilir") && Drawing("koseOnizlemesi").Data == null && Drawing("seciliCizim").Data == null, "ESC cancels valid preview without commit");
        Activate("btnRadius"); Click(new Point(1, 1));
        Call(_window!, "btnKoseIptal_Click", _window, Event);
        Check(Field(_window!, "_aktifKoseKomutu")!.ToString() == "Yok" && Drawing("koseOnizlemesi").Data == null && !Dirty, "Cancel returns to clean normal selection command state");
        Activate("btnSec");
        lines = Entities(model);
        Call(_window!, "EditSec", lines[0], false); Call(_window!, "EditSec", lines[1], true);
        Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 2, "Normal additive selection works after command cancellation");
        Call(_window!, "EditSec", lines[0], true);
        Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 1, "Normal Ctrl-style deselection works after command cancellation");
        Call(_window!, "EditCerceveSec", new Rect(new Point(-1, -1), new Point(101, 101)), false);
        Check(((GeometryGroup)Drawing("seciliCizim").Data).Children.Count == 4 && Drawing("koseOnizlemesi").Data == null, "Normal rectangle selection works without stale preview");
    }

    private static void VerifyCodeHealthContracts(Assembly assembly)
    {
        object? Invoke(string type, string method, params object?[] args) =>
            assembly.GetType("Macria." + type, true)!.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args);
        var values = new Dictionary<string, double?> { ["a"] = 5, ["eksik"] = null };
        var cases = new (string Expression, double? Expected, bool Error)[]
        {
            ("abs(-2)", 2, false), ("mutlak(-2)", 2, false), ("sqrt(9)", 3, false),
            ("kok(-1)", null, false), ("ceil(1.2)", 2, false), ("taban(2.8)", 2, false),
            ("round(2.55;1)", 2.6, false), ("round(2.55;99)", 2.55, false),
            ("min(3;4)", 3, false), ("max(3;4)", 4, false), ("a+2", 7, false),
            ("eksik+2", null, false), ("1/0", null, false), ("min(eksik;4)", null, false),
            ("unknown(1)", null, true), ("abs()", null, true), ("min(1)", null, true),
            ("", null, true), ("yok+1", null, true)
        };
        foreach (var test in cases)
        {
            object?[] args = { test.Expression, values, null };
            object? result = Invoke("Formul", "Hesapla", args);
            Check(Equals(result, test.Expected), "Code health preserves formula result: " + test.Expression);
            Check((args[2] != null) == test.Error, "Code health preserves missing-value/error distinction: " + test.Expression);
        }
        foreach (int hr in new[] { 0, 1 })
            Check(((string)Invoke("CatiaConnect", "Aciklama", hr)!).EndsWith("Bilinmeyen Hata."), "Successful/unknown HRESULT preserves existing diagnostic fallback");
        Check((string)Invoke("ComProbe", "TipAdi", new object?[] { null })! == "(null)", "COM diagnostic null input retains existing output");
        Check(((System.Collections.ICollection)Invoke("ComProbe", "UyeAdlari", new object?[] { null })!).Count == 0, "COM diagnostic null input retains empty member list");
        var canvas = new Canvas();
        canvas.Measure(new Size(300, 200)); canvas.Arrange(new Rect(0, 0, 300, 200));
        Invoke("GrafikCizer", "Halka", canvas, null, "mm", 2, "");
        Check(canvas.Children.Count > 0, "Null graph input retains empty-graph visual without dereference");
        var row = (System.ComponentModel.INotifyPropertyChanged)Activator.CreateInstance(assembly.GetType("Macria.CostRow", true)!)!;
        Call(row, "Bildir");
        int notifications = 0;
        System.ComponentModel.PropertyChangedEventHandler handler = (_, _) => notifications++;
        row.PropertyChanged += handler; Call(row, "Bildir"); row.PropertyChanged -= handler;
        Check(notifications == 9, "Nullable event contract preserves all original cost notifications");
    }

    private static void VerifyAxisDimensions(Assembly assembly)
    {
        EditCheckBox.IsChecked = false;
        Type entityType = assembly.GetType("Macria.DxfEntity", true)!;
        object Line(Point first, Point last)
        {
            object entity = Activator.CreateInstance(entityType, true)!;
            entityType.GetField("Tip")!.SetValue(entity, "LINE");
            entityType.GetField("Noktalar")!.SetValue(entity, new[] { first, last });
            return entity;
        }
        var cases = new[]
        {
            ("Horizontal/right", true, new Point(10, 0), new Point(110, 0), new Point(20, 30), new Point(90, 30), new Point(140, 15)),
            ("Horizontal/left", true, new Point(10, 0), new Point(110, 0), new Point(20, 30), new Point(90, 30), new Point(-20, 15)),
            ("Vertical/up", false, new Point(0, 10), new Point(0, 110), new Point(30, 20), new Point(30, 90), new Point(15, 140)),
            ("Vertical/down", false, new Point(0, 10), new Point(0, 110), new Point(30, 20), new Point(30, 90), new Point(15, -20)),
            ("Horizontal/interior", true, new Point(10, 0), new Point(110, 0), new Point(20, 30), new Point(90, 30), new Point(50, 15)),
            ("Vertical/interior", false, new Point(0, 10), new Point(0, 110), new Point(30, 20), new Point(30, 90), new Point(15, 50)),
            ("Horizontal/reversed", true, new Point(110, 0), new Point(10, 0), new Point(90, 30), new Point(20, 30), new Point(140, 15)),
            ("Vertical/reversed", false, new Point(0, 110), new Point(0, 10), new Point(30, 90), new Point(30, 20), new Point(15, 140)),
            ("Near-horizontal/right", true, new Point(10, 0), new Point(110, 0.001), new Point(20, 30), new Point(90, 30.0007), new Point(140, 15)),
            ("Near-horizontal/left", true, new Point(10, 0), new Point(110, 0.001), new Point(20, 30), new Point(90, 30.0007), new Point(-20, 15)),
            ("Near-vertical/up", false, new Point(0, 10), new Point(0.001, 110), new Point(30, 20), new Point(30.0007, 90), new Point(15, 140)),
            ("Near-vertical/down", false, new Point(0, 10), new Point(0.001, 110), new Point(30, 20), new Point(30.0007, 90), new Point(15, -20))
        };
        foreach (var (name, horizontal, p0, p1, q0, q1, placement) in cases)
        {
            Call(_window!, "btnSigdir_Click", _window, Event);
            Call(_window!, "btnCizgiCizgi_Click", _window, Event);
            Call(_window!, "CizgiOlcuSec", Line(p0, p1));
            Call(_window!, "CizgiOlcuSec", Line(q0, q1));
            Check(Field(_window!, "_cizgiOlcuAsamasi")!.ToString() == "PlacementWaiting", name + ": placement begins");
            Point referenceFirst = (Point)Field(_window!, "_olcuBaslangicNoktasi")!;
            Point referenceLast = (Point)Field(_window!, "_sonMesafeNoktasi")!;
            Vector normal = (Vector)Field(_window!, "_cizgiOlcuYonu")!;
            double distance = Math.Abs(Vector.Multiply(referenceLast - referenceFirst, normal));
            Call(_window!, "CizgiOlcuYerlesiminiGuncelle", placement);
            AssertDimension(name);
            var preview = DimensionSegments();
            AssertAxisSegments(preview, horizontal, placement, p0, p1, q0, q1, name);
            Point irrelevantMovement = horizontal ? placement + new Vector(0, 75) : placement + new Vector(75, 0);
            Call(_window!, "CizgiOlcuYerlesiminiGuncelle", irrelevantMovement);
            AssertSameDimension(preview, DimensionSegments(), name + ": other mouse component ignored");

            ((ScaleTransform)_window!.FindName("olcek")!).ScaleX = 2.5;
            ((ScaleTransform)_window.FindName("olcek")!).ScaleY = 2.5;
            ((TranslateTransform)_window.FindName("kaydir")!).X = 37;
            ((TranslateTransform)_window.FindName("kaydir")!).Y = -23;
            Call(_window, "CizgiKalinligi");
            Layout();
            AssertSameDimension(preview, DimensionSegments(), name + ": preview model placement survives pan/zoom");
            Call(_window, "CizgiOlcuTikla", placement);
            Check(Field(_window, "_cizgiOlcuAsamasi")!.ToString() == "Completed", name + ": third click fixes placement");
            Call(_window, "CizgiOlcuYerlesiminiGuncelle", placement + new Vector(100, 100));
            AssertSameDimension(preview, DimensionSegments(), name + ": completed placement ignores movement");
            string info = ((TextBlock)_window.FindName("txtEntityInfo")!).Text;
            Check(info.Contains(distance.ToString("N2") + " mm", StringComparison.Ordinal), name + ": unchanged true perpendicular distance");
            Check(ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("mesafeCizgisi").RenderTransform) &&
                  ReferenceEquals(Drawing("cizim").RenderTransform, Drawing("olcuYazisi").RenderTransform), name + ": shared transform for extensions/ticks/text");
            foreach (var (start, end) in preview.Take(3))
            {
                foreach (Point point in new[] { start, end })
                {
                    Point rendered = Drawing("mesafeCizgisi").TranslatePoint(new Point(point.X, -point.Y), (UIElement)_window.FindName("cizimAlani")!);
                    CheckPoint(rendered, Screen(point), name + ": dimension and main drawing screen alignment");
                }
            }
            ((ScaleTransform)_window.FindName("olcek")!).ScaleX = 4;
            ((ScaleTransform)_window.FindName("olcek")!).ScaleY = 4;
            ((TranslateTransform)_window.FindName("kaydir")!).X = -42;
            ((TranslateTransform)_window.FindName("kaydir")!).Y = 19;
            Call(_window, "CizgiKalinligi");
            AssertSameDimension(preview, DimensionSegments(), name + ": fixed dimension survives pan/zoom");
            Call(_window, "btnTemizle_Click", _window, Event);
            Check(Drawing("mesafeCizgisi").Data == null && Drawing("olcuYazisi").Data == null, name + ": Clear removes dimension and text");
        }

        // The inclined branch must keep its pre-existing tangent/normal offset behavior.
        Call(_window!, "btnCizgiCizgi_Click", _window, Event);
        Call(_window!, "CizgiOlcuSec", Line(new Point(0, 0), new Point(100, 100)));
        Call(_window!, "CizgiOlcuSec", Line(new Point(0, 20), new Point(100, 120)));
        Point inclinedFirst = (Point)Field(_window!, "_olcuBaslangicNoktasi")!;
        Point inclinedLast = (Point)Field(_window!, "_sonMesafeNoktasi")!;
        Vector inclinedNormal = (Vector)Field(_window!, "_cizgiOlcuYonu")!;
        double tangentOffset = (double)Field(_window!, "_cizgiOlcuUzantiOfseti")!;
        Point inclinedMouse = new(150, -30);
        Point midpoint = inclinedFirst + (inclinedLast - inclinedFirst) / 2;
        Vector offset = new Vector(inclinedNormal.Y, -inclinedNormal.X) * tangentOffset +
                        inclinedNormal * Vector.Multiply(inclinedMouse - midpoint, inclinedNormal);
        Call(_window!, "CizgiOlcuYerlesiminiGuncelle", inclinedMouse);
        var inclined = DimensionSegments();
        CheckPoint(inclined[2].Start, inclinedFirst + offset, "Inclined start retains existing normal placement");
        CheckPoint(inclined[2].End, inclinedLast + offset, "Inclined end retains existing normal placement");
        Call(_window!, "btnTemizle_Click", _window, Event);
    }

    private static (Point Start, Point End)[] DimensionSegments()
    {
        PathGeometry geometry = PathGeometry.CreateFromGeometry(Drawing("mesafeCizgisi").Data);
        return geometry.Figures.Select(figure =>
        {
            PathSegment segment = figure.Segments.Last();
            Point end = segment switch
            {
                LineSegment line => line.Point,
                PolyLineSegment polyline => polyline.Points.Last(),
                _ => throw new InvalidOperationException("Expected a straight dimension segment.")
            };
            return (new Point(figure.StartPoint.X, -figure.StartPoint.Y), new Point(end.X, -end.Y));
        }).ToArray();
    }

    private static void AssertAxisSegments((Point Start, Point End)[] lines, bool horizontal,
        Point placement, Point p0, Point p1, Point q0, Point q1, string name)
    {
        foreach (var (start, end) in lines.Take(2))
            Check(Math.Abs(horizontal ? end.Y - start.Y : end.X - start.X) < 1e-8, name + ": extension has no diagonal component");
        Check(Math.Abs(horizontal ? lines[2].Start.X - lines[2].End.X : lines[2].Start.Y - lines[2].End.Y) < 1e-8, name + ": perpendicular axis dimension");
        Check(Math.Abs(horizontal ? lines[2].Start.X - placement.X : lines[2].Start.Y - placement.Y) < 1e-8, name + ": exact third-click coordinate");
        Point Projection(Point first, Point last)
        {
            double value = horizontal ? placement.X : placement.Y;
            double t = Math.Clamp((value - (horizontal ? first.X : first.Y)) /
                                  (horizontal ? last.X - first.X : last.Y - first.Y), 0, 1);
            return first + (last - first) * t;
        }
        CheckPoint(lines[0].Start, Projection(p0, p1), name + ": first attachment is segment projection");
        CheckPoint(lines[1].Start, Projection(q0, q1), name + ": second attachment is segment projection");
    }

    private static void AssertSameDimension((Point Start, Point End)[] expected,
        (Point Start, Point End)[] actual, string name)
    {
        for (int i = 0; i < 3; i++)
        {
            CheckPoint(actual[i].Start, expected[i].Start, name);
            CheckPoint(actual[i].End, expected[i].End, name);
        }
    }
    private static void CheckPoint(Point actual, Point expected, string message) => Check((actual - expected).Length < 1e-7, message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
    private static Exception Unwrap(Exception exception) => exception is TargetInvocationException { InnerException: not null } wrapped
        ? Unwrap(wrapped.InnerException!) : exception;
    private sealed class SyntheticSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private const string Fixture = """
        0
        SECTION
        2
        HEADER
        9
        $ACADVER
        1
        AC1015
        0
        ENDSEC
        0
        SECTION
        2
        BLOCKS
        0
        BLOCK
        2
        READONLY
        10
        0
        20
        0
        0
        LINE
        5
        B1
        8
        BLOCK_LAYER
        10
        -40
        20
        -40
        11
        -30
        21
        -30
        0
        ENDBLK
        0
        ENDSEC
        0
        SECTION
        2
        ENTITIES
        0
        LINE
        5
        101
        8
        PART
        10
        10
        20
        0
        11
        110
        21
        0
        0
        LINE
        5
        102
        8
        PART
        10
        10
        20
        30
        11
        110
        21
        30
        0
        CIRCLE
        5
        103
        8
        HOLES
        10
        60
        20
        100
        40
        10
        0
        ARC
        5
        104
        8
        PART
        10
        120
        20
        100
        40
        15
        50
        0
        51
        90
        0
        INSERT
        5
        105
        2
        READONLY
        10
        200
        20
        150
        0
        ENDSEC
        0
        EOF
        """;
}
