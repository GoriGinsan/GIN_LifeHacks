# Source guide

Both plugins target Koikatsu CharaStudio, BepInEx 5 and the game's .NET 3.5-era runtime. Build with `src/build.ps1`; the .NET SDK is not a game runtime dependency.

## Timeline

Start with `src/Timeline/Plugin.cs` for state, settings and the Unity update loop.

| File | Responsibility |
| --- | --- |
| `Attachment.cs` | Attach to native Timeline and restore owned changes on detach |
| `NativeReflection.cs`, `Compatibility.cs` | Native member access, Harmony callbacks and supported API contracts |
| `Layout.cs`, `ToolbarPlacement.cs` | UI elements and placement around the resize handle and screen edges |
| `CandidatePanel.cs` | Right-side Interpolables browser and original row restoration |
| `Input.cs`, `InputFocus.cs`, `PointerHooks.cs` | Shortcut routing, typing protection and native pointer hooks |
| `KeySelection.cs` | Current-value updates, anchor and alternating key selection |
| `Groups.cs`, `GroupInteraction.cs` | Group summaries, selection, drag previews and native commits |
| `CameraGuard.cs` | Preserve camera input conditions while interacting with added UI |

## Advanced mode

Start with `src/Advanced/Plugin.cs` for settings and lifecycle. `AdvancedUi.cs` owns the adapter state and patch installation. Its partial files keep the same runtime class while separating styles, Blend Shape visibility, bone controls, transpilers and display templates.

`ExpressionData.cs` defines the existing XML data and storage helpers. `ExpressionRuntime.cs` captures and applies native character values. `ExpressionLibrary.cs` manages the library and storage; `ExpressionLibrary.Drawing.cs` draws it. `NativeReflection.cs` contains the shared `ReflectionAccess` helper.

## Compatibility rules

- Keep plugin GUIDs, configuration section/key names and saved XML identities stable.
- Retain native method/field names used by Harmony and reflection. Small changes to those strings can break attachment or input routing.
- Keep partial-class field initializers in their original files and order; method files may be separated without moving startup state.
- Do not add GIN-only scene providers for native Timeline keys or KKPE values.
- Compile against local game assemblies without redistributing them. Keep installed DLL filenames fixed.

Each plugin's `Diagnostics/` folder contains guarded regression entry points. They require the explicit verification launch flag and a marked disposable game. They do not run during normal use. See [verification](VERIFICATION.md) for runtime coverage and limits.
