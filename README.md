# GIN LifeHacks

**[Download GIN LifeHacks v1.0.6 (installation ZIP)](https://github.com/GoriGinsan/GIN_LifeHacks/releases/download/v1.0.6/GIN_LifeHacks_v1.0.6.zip)**

Animation workflow helpers for **Koikatsu CharaStudio**. Two independent BepInEx 5 plugins improve Timeline and KKPE Advanced mode while using their existing editing and scene data.

## IMPORTANT — Install the LATEST Koikatsu Timeline AND ShalltyUtils first

> [!IMPORTANT]
> **Update BOTH plugins before installing GIN Timeline life hack. Updating HSPlugins alone does not update ShalltyUtils. Choose the KK / Koikatsu files, not KKS, HS or HS2.**

### [Download the latest Timeline — official HSPlugins Releases](https://github.com/IllusionMods/HSPlugins/releases/latest)

### [Download the latest ShalltyUtils — maintained Lusiocc fork](https://github.com/Lusiocc/ShalltyUtils/releases/latest)

Checked on **2026-10-29**: the official latest pages identify **HSPlugins r2.25 / Timeline 1.5.6** and **ShalltyUtils 1.3.2**. The HSPlugins publisher explicitly requires the updated ShalltyUtils fork for this release: [upstream compatibility notice](https://github.com/IllusionMods/HSPlugins/releases/tag/r2.25). Download and replace both separately; remove duplicate old copies.

**GIN Advance mode life hack also needs KKPE**, available from the same [official HSPlugins Releases](https://github.com/IllusionMods/HSPlugins/releases/latest). The tested KKPE version is **2.21.5**. F1 configuration requires your existing BepInEx Configuration Manager.

Timeline helper 1.0.6 and Advanced helper 1.0.3 were runtime-tested with Timeline 1.5.6 + KKPE 2.21.5, using **both ShalltyUtils 1.3.1 and 1.3.2**. Updating ShalltyUtils from 1.3.1 to 1.3.2 is supported; existing 1.3.1 installations remain compatible. Future upstream releases require a fresh compatibility check; this page does not promise support for an unreleased or untested version. This is a Koikatsu build, not a Koikatsu Sunshine build.

## Download and install

Get the ZIP files from **[GIN LifeHacks Releases](https://github.com/GoriGinsan/GIN_LifeHacks/releases/latest)**:

- `GIN_LifeHacks_v1.0.6.zip`: both plugins.
- `GIN_Timeline_LifeHack_v1.0.6.zip`: Timeline helper only.
- `GIN_Advance_Mode_LifeHack_v1.0.3.zip`: Advanced mode helper only.

Close Studio, extract your chosen ZIP, and copy its **BepInEx** folder into the game folder containing `CharaStudio.exe`. Allow merging of folders and replacement of these GIN files. Each ZIP contains only the runtime DLLs it installs:

```text
BepInEx/
  plugins/
    GIN Timeline life hack.dll
    GIN Advance mode life hack.dll
```

The individual ZIPs contain one of these DLLs. Installed filenames and paths stay fixed for future overwrite updates. Plugin versions remain in DLL metadata and release tags. No README, source, preset, configuration file or third-party DLL is included in the installation ZIPs.

### One-time migration from earlier GIN packages

Earlier GIN DLL filenames included versions and were placed in `BepInEx/plugins/GINLifeHacks`. Before the first fixed-name installation, move those **old GIN Timeline / GIN Advance DLLs outside BepInEx/plugins**, including copies in subfolders. Do not leave both names installed. Keep your configuration files and `UserData/GIN` presets. Subsequent updates overwrite the fixed filenames directly.

## Timeline helper — version 1.0.6

- **Resize-safe tools:** when the Timeline resize handle approaches the GIN buttons, all four buttons and their feedback move below the window. Widening the window restores the upper placement. The previous screen-top fallback and screen-edge visibility are retained. No setting is required.
- **Odd/even key selection:** use `Anchor` to remember a starting key, box-select a range, then press `1 / 3 / 5` or `2 / 4 / 6`. The anchor is number 1; keys are counted in time order independently for each track.
- **Right-side track browser:** `+ Tracks` shows a scrollable Interpolables panel. Candidates remain the actual native providers for the selected target.
- **Add keys with Ctrl + right click:** empty track/group cells use the pointer time; the right-side candidate list uses the playhead. Native middle click remains available. Existing keys are preserved.
- **Use Current Value with S:** updates all selected Timeline keys through the native command, including after viewport, Move Controller or KKPE manipulation. Shift/Alt may remain held with the default S binding; Ctrl+S stays scene save. Text entry, IME composition and F1 settings block editing shortcuts.
- **Group summary keys:** red when unselected and green when any represented child key is selected. Box selection and Alt + left drag work while the group is collapsed, including mixed visible/hidden key selections and ShalltyUtils Performance Mode on/off. A collision with an unselected destination key cancels the move and keeps existing keys.
- **Group insertion:** adds the current values to all descendant tracks, filling missing keys at that time without replacing existing keys.
- **Track/group ordering:** Shift + Up / Down moves selected tracks or groups among siblings while preserving their parent and children.

In **F1 → GIN Timeline life hack → Input**, configure `AddKeyframe`, `UseCurrentValue`, `MoveTracksUp` and `MoveTracksDown`. The default insertion binding is `LeftControl + Mouse1`; either Control key works. Keyboard insertion also works when the pointer is over a destination row. The older `RightClickAddsKey` option no longer controls insertion.

## Advanced mode helper — version 1.0.3

Right-click the original **KKPE** button to open Advanced mode as usual.

- Larger Advanced mode buttons, slider handles and scrollbars, with reduced spacing.
- Hide/reveal individual Blend Shape rows without changing their values. Save named visibility templates.
- Collapse Eyes and Mouth individually or together.
- Hold and drag the native Bones +/- controls horizontally: right increases, left decreases. Shift gives finer control, Ctrl coarser control, and Escape reverses the current scrub gesture.
- L/R selection buttons beside Copy Position / Rotation / Scale.
- **Expressions** library: save a name and center-camera thumbnail, apply by clicking a thumbnail, and organize presets in named columns with vertical and horizontal scrolling. Delete asks for confirmation.

Expression presets capture all existing Eyes / Mouth, Teeth and Eyebrows Blend Shapes, plus the exact named morph `kuti_ha.ha00_name_op`, independently of its numeric index. Bone scope:

| Bones | Stored components |
| --- | --- |
| `cf_J_hitomi_tx_L`, `cf_J_hitomi_tx_R` | Position, Scale |
| `cf_j_tang_01` | Position, Rotation, Scale |
| `cf_j_tang_03`, `cf_j_tang_04`, `cf_j_tang_05` | Rotation, Scale |

Missing bones or morphs are skipped and reported rather than mapped to unrelated indices. Different head mods may produce different results. Timeline playback can overwrite values it controls. Whole-expression Undo is not guaranteed.

Templates: `UserData/GIN/AdvancedDisplay`. Expressions and thumbnails: `UserData/GIN/Expressions`. Back up these folders to move your library. All UI text added by GIN is English; native labels and user-created names keep their existing text.

## Disable, uninstall and scene sharing

Set the relevant plugin's **General → Enabled = false** in F1, or close Studio and remove only its GIN DLL. Saved expressions, templates and native scene edits remain. This setting removes the owned UI/hooks; it does not unload an assembly during runtime.

GIN does not introduce a scene data provider or custom Interpolable provider. Group diamonds display the original child keys; native Timeline keys, groups, interpolation curves and Shallty saved curve files keep their existing format. A recipient does not need GIN solely to replay those native edits, but still needs the scene's original Timeline, KKPE, NodesConstraints, assets and other dependencies. A scene saved with 1.0.6 and ShalltyUtils 1.3.2 was reopened and played in a fresh process with both LifeHacks DLLs absent and ShalltyUtils 1.3.1; see [verification scope](docs/VERIFICATION.md).

## Development and verification

Source is in `src/`; start with the [source guide](docs/SOURCE.md) for the lifecycle, input, UI and diagnostics files. Installation ZIPs contain runtime files only. Build with your own local Koikatsu references:

```powershell
.\src\build.ps1 -GamePath 'your Koikatsu game folder'
python .\src\package.py --output dist
```

No game assemblies, third-party plugins, character cards or curve presets are redistributed here. [Verification](docs/VERIFICATION.md) and [changes](CHANGELOG.md) describe the tested versions and limitations. Report problems through [Issues](https://github.com/GoriGinsan/GIN_LifeHacks/issues) with plugin versions, the affected operation and the relevant error text.
