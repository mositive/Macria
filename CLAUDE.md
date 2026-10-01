# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Read [AGENTS.md](AGENTS.md) first.** It holds the binding project rules (in Turkish): what to check before and after changing code, which Obsidian note to update for which area, the critical areas, the DXF Edit Mode rules, and the end-of-task report format. This file does not repeat those rules.

## What this is

Macria is a Windows x64 WPF desktop app (.NET 8, `net8.0-windows`). It connects over COM to a running CATIA V5 / 3DEXPERIENCE session and scans the active assembly. From that scan it:

- lists sheet-metal parts and the product tree,
- exports DXF files by driving CATIA's own "Save As DXF" UI,
- measures weight and cost,
- runs an experimental box-profile diagnosis with STEP export,
- and provides DXF preview and a controlled DXF Edit Mode.

Code identifiers, UI text, logs and docs are mostly Turkish.

## Commands

Run all commands from the repo root (the folder that contains `Macria.slnx`).

```powershell
# Build (same as the default VS Code task)
dotnet build Macria/Macria.csproj --configuration Debug --framework net8.0-windows -p:Platform=x64

# Release build / portable single-file exe
dotnet build Macria/Macria.csproj -c Release
dotnet publish Macria/Macria.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# DXF parser / edit-session regression tests (no CATIA needed)
dotnet run --project DxfEdit.Tests/DxfEdit.Tests.csproj --configuration Debug --framework net8.0-windows -p:Platform=x64

# GeometryLab adapter, inventory, production package and PreviewCore tests
dotnet run --project GeometryLabAdapter.Tests/GeometryLabAdapter.Tests.csproj

# DXF Edit UI smoke test: loads a built Macria.dll by reflection and drives OnizlemeWindow
dotnet run --project DxfEdit.UiTests/DxfEdit.UiTests.csproj -p:Platform=x64 -- <path to built x64 Macria.dll>
```

About the tests:

- None of the three test projects uses xUnit or NUnit. Each is a console program whose `Main` calls every test method in order and exits with code 0 (PASS) or 1 (FAIL).
- There is no filter for running a single test. To run one, temporarily comment out the other calls in `Main`.
- `DxfEdit.Tests` and `GeometryLabAdapter.Tests` compile production files from `Macria/` directly through `<Compile Include="../Macria/..." Link=...>`. If a linked file starts depending on a new Macria file, add that file to the test `.csproj` too.
- The "Real*Engine" cases in `GeometryLabAdapter.Tests` run the packaged `GeometryEngineRuntime/Macria.GeometryEngine.exe`.
- `DxfEdit.Tests` has one save/overwrite test that needs filesystem ACL and `File.Replace` permissions. A sandbox may block it. A permission failure there does not count as a pass (see `DxfEdit.Tests/README.md`).

Debug-only shortcuts, for testing without CATIA:

- `F9` runs a fake bulk-DXF run with the progress window and animation.
- `F10` loads sample rows into the box-profile tab.

Both are behind `#if DEBUG` and are not in Release builds.

Most real behaviour needs a running CATIA and cannot be verified on a dev machine. Report such changes as "not verified" rather than "done". After startup, the first console line shows the running version (`Macria vX.Y.Z Hazır`). Check it to make sure you are not running an old exe. The version comes from `Macria/Macria.csproj`.

## Architecture

**The `MainWindow` partial class is the application.** There is no separate service layer.

- `MainWindow.xaml.cs` (~5k lines) holds the shared state: the `_catia` COM object, the `ObservableCollection`s behind each tab, and the scan-reference dictionaries.
- Each feature lives in its own `MainWindow.<Feature>.cs` partial, for example `Maliyet`, `Profil`, `DxfDwgFiles`, `GeometryLab`, `ExternalStep*`, `ProductionPackage` and `Renklendirme2`.
- Many `MainWindow.*Teshisi.cs` / `*AbTesti.cs` / `*Testi.cs` partials are diagnostic experiments for CATIA color/reset behaviour. They are not the core pipeline.

**Scan pipeline:**

1. `btnScan_Click` runs `DoScan()` inside `Task.Run`.
2. `CatiaConnect.Connect()` tries, in order: ProgID, then versioned ProgID, then a fixed CLSID, then a ROT scan.
3. `ScanNode()` walks the occurrence tree.
4. The scan returns a `ScanOutput` holding rows, tree roots, profile rows, diagnostics and `RepRefs`.
5. Back on the UI thread, the code fetches the CATIA COM object again (`GetCatia()`) for apartment compatibility and fills the collections.

**Part identity:**

- Parts are deduplicated by `Reference Title`. Extra occurrences only increase `Quantity`.
- `SheetRow.ReferenceKey` is the join key into `_repRefs` (the scan's representation references). DXF export, preview and profile code use it to get back to the CATIA object.
- The cost view keeps its own `_costRepRefs`.
- Changing how keys are built or when these dictionaries are cleared affects every downstream feature.

**DXF export:**

- CATIA's Save As DXF panel draws itself and exposes no UIA tree or child windows. Keyboard, Win32 and UIA approaches were tried and removed, so export works by real mouse clicks.
- `SaveAsBulucu` finds the Save As button by matching a user-taught image patch against a screen capture (`GorselEslesme`, a 3-stage coarse-to-fine search).
- If the match is below the threshold, the code falls back to a taught screen coordinate (`PencereAraclari.OgretilmisNokta`). If there is no taught coordinate either, it does not click at all.
- `BukumBulucu` uses the same matcher to untick "Bend Information" before Save As, and only does so when the box is clearly ticked.
- Multi-Body parts are deliberately kept out of bulk DXF export.

**DXF read/edit:**

- `DxfOkuyucu` parses ASCII DXF into a `DxfCizim` model that keeps the source-record identity of each entity.
- `OnizlemeWindow` renders the model and handles selection, pan/zoom and edit commands.
- `DxfKoseGeometrisi` plans join, chamfer and fillet operations.
- `DxfEditOturumu` owns the edit transactions: Undo/Redo, dirty state, backups and safe Save/Save As.
- `PreviewCore` (new) holds preview logic that does not depend on WPF, so it can be tested.

**GeometryEngine:**

- An out-of-process native engine (OCCT-based) that is shipped as prebuilt binaries in `GeometryEngineRuntime/`.
- `Macria.csproj` copies it to `GeometryEngine\` in the build and publish output.
- The analysis engine (`Macria.GeometryEngine.exe`) always runs as a separate child process.
- The 3D STEP viewer is different: `OcctViewerNative` loads `Macria.GeometryViewer.dll` (OCCT) from the same folder into the Macria process with `LoadLibraryEx`, and `OcctViewportHost` hosts it. A native crash in the viewer therefore takes Macria down with it.
- The csproj comment and `GeometryEngineRuntime/README.md` still say nothing is loaded in-process. That is outdated.
- `GeometryLabProcessAdapter` starts it with `--input <step> --output <json>` and deserializes the result with `GeometryLabTransportDtos`. `GeometryLabEngineLocator` resolves the exe path.
- Only verified GeometryLab x64 Release outputs go into `GeometryEngineRuntime/` (see its README).

**Reporting:** `RaporModel` is a format-neutral report model. `ExcelYazici` and `PdfYazici` render it to Excel and PDF.

## Documentation (Obsidian)

- The technical memory lives outside the repo, at `C:\Users\enesy\OneDrive\Belgeler\Macria\Obsidian\Macria\` (notes `00`–`06` and `99 - Yapılacaklar.md`).
- AGENTS.md defines which note to update for which area, and when.
- Source code is the ground truth. If code and notes disagree, say so openly. The notes still mention v1.10.4 in places; the code is currently 1.11.x.

## Repo layout notes

- `Macria.slnx` contains only `Macria/Macria.csproj`. Build the test projects separately.
- Leave these alone unless explicitly asked: `ExeArsivi/`, `Macria-v1.11.0-IsYeri-Test/`, `Macria_v1.11.1_IsYeri_Test/` and the `*.zip` files are release/test archives (gitignored); `.line-line-verification/` is a leftover verification build output.
