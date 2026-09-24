# DXF Edit regression tests

Run from the workspace root:

```powershell
dotnet run --project DxfEdit.Tests/DxfEdit.Tests.csproj --configuration Debug --framework net8.0-windows -p:Platform=x64
```

The runner compiles the production DXF parser and edit-session source directly, without additional packages. It writes only generated fixture files to a unique `Macria-DxfEdit-Tests-*` temporary directory inside this test project and removes that directory after the run. It never opens or changes a user's DXF file.

Assertions cover source-record identity, LINE/CIRCLE/ARC deletion, exact untouched-byte preservation, INSERT-derived read-only geometry, Undo/Redo, Save As, confirmed overwrite, collision-safe backups, undo after save, reopen, cross-document selection rejection, stale external changes, locked/missing/read-only source safety, incoming references (including HEADER and normalized handles), duplicate handles, 2D-only edit scope, mixed/CR/LF line endings, non-ASCII payload, empty drawings, malformed ASCII and binary rejection.

One failure test temporarily denies file creation inside its own generated fixture subdirectory to verify that failed backup creation cannot overwrite the original. The original subdirectory ACL is restored in `finally`. Some Windows sandboxes deny the metadata access required by atomic `File.Replace`; in that environment, this runner needs execution permission outside the sandbox to test successful overwrite. A permission failure is not a passing save test.

This is not an interactive UI test. Edit Mode controls, keyboard handling, prompts, dirty title, selection, snap and pan/zoom must also be tested in the running application.
