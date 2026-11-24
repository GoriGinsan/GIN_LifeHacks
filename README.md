# GIN LifeHacks

**[Download GIN LifeHacks v1.1.1 (installation ZIP)](https://github.com/GoriGinsan/GIN_LifeHacks/releases/download/v1.1.1/GIN_LifeHacks_v1.1.1.zip)**

Animation workflow helpers for **Koikatsu CharaStudio**. Three independent BepInEx 5 plugins improve Timeline and KKPE Advanced mode and add a small posing and expression window (GIN Pad, the `IK QOL` button), while using the existing editing and scene data of Timeline, KKPE and Studio.

## IMPORTANT — Install the LATEST Koikatsu Timeline AND ShalltyUtils first

> [!IMPORTANT]
> **Update BOTH plugins before installing GIN Timeline life hack. Updating HSPlugins alone does not update ShalltyUtils. Choose the KK / Koikatsu files, not KKS, HS or HS2.**

### [Download the latest Timeline — official HSPlugins Releases](https://github.com/IllusionMods/HSPlugins/releases/latest)

### [Download the latest ShalltyUtils — maintained Lusiocc fork](https://github.com/Lusiocc/ShalltyUtils/releases/latest)

Checked on **2026-11-19**: the official latest pages identify **HSPlugins r2.25 / Timeline 1.5.6** and **ShalltyUtils 1.3.2**. The HSPlugins publisher explicitly requires the updated ShalltyUtils fork for this release: [upstream compatibility notice](https://github.com/IllusionMods/HSPlugins/releases/tag/r2.25). Download and replace both separately; remove duplicate old copies.

**GIN Advance mode life hack and GIN Pad life hack also need KKPE**, available from the same [official HSPlugins Releases](https://github.com/IllusionMods/HSPlugins/releases/latest). The tested KKPE version is **2.21.5**. F1 configuration requires your existing BepInEx Configuration Manager.

Timeline helper 1.1.0, Advanced helper 1.1.0 and GIN Pad 1.0.0 were runtime-tested with Timeline 1.5.6 + KKPE 2.21.5 + ShalltyUtils 1.3.2 (the earlier 1.0.6 was also tested with ShalltyUtils 1.3.1; 1.3.1 was not re-run for 1.1.0). Future upstream releases require a fresh compatibility check; this page does not promise support for an unreleased or untested version. This is a Koikatsu build, not a Koikatsu Sunshine build.

Timeline helper 1.1.1 is 1.1.0 without Char Setup, which crashed the game on some installations. It was compiled from the same source with Char Setup left out and was not run again in the game.

## In the source, not yet released — Delay Key and smoother Pad drags

The source on `main` is ahead of the v1.1.1 downloads: **GIN Pad life hack 1.1.0** and **Timeline helper 1.2.0** (Advanced helper 1.1.0 unchanged). They are built but have not been run in the game yet; build them with `src/build.ps1` until a release follows. See [CHANGELOG](CHANGELOG.md).

- **Delay Key** (Pad, Body tab): every body part has its own key delay in seconds, without frame steps. With `Delay Key` on, Auto Key of the Pad's edits, `Key now` and a middle click write each part's key that long after the playhead, so a pose change starts at the hips and runs out through the body (left and right may differ, for example L.Shoulder 0.052 s after R.Shoulder). A key already at the delayed time is updated. Set the delays on the delay bar beside the figure or, with `Edit`, by dragging the figure's buttons up and down; a dragged part takes along the parts further from the origin (Ctrl + click sets the origin, Alt moves one part only). Only keys written from the Pad are delayed. Needs Timeline helper 1.2.0.
- **Root** (Pad, Body tab): a small plain-grey trapezoid between the toes moves the whole character (Studio's own position of the character), neither a bone nor an IK target. Drag moves it on the floor plane as seen from the camera, right drag up and down; a click selects the character's root in Studio, a middle click keys its position row at the playhead. Ctrl + Z takes a move back.
- **Smoother drags:** Pad drags that hold the cursor now move the value in every frame, as Move Controller does.
- **Time button:** a quarter of its former speed.

## Download and install

Get the ZIP files from **[GIN LifeHacks Releases](https://github.com/GoriGinsan/GIN_LifeHacks/releases/latest)**:

- `GIN_LifeHacks_v1.1.1.zip`: all three plugins.
- `GIN_Timeline_LifeHack_v1.1.1.zip`: Timeline helper only (1.1.1: the 1.1.0 tools without Char Setup).
- `GIN_Advance_Mode_LifeHack_v1.1.0.zip`: Advanced mode helper only.
- `GIN_Pad_LifeHack_v1.0.0.zip`: GIN Pad (`IK QOL`) only.

Close Studio, extract your chosen ZIP, and copy its **BepInEx** folder into the game folder containing `CharaStudio.exe`. Allow merging of folders and replacement of these GIN files. Each ZIP contains only the runtime DLLs it installs:

```text
BepInEx/
  plugins/
    GIN Timeline life hack.dll
    GIN Advance mode life hack.dll
    GIN Pad life hack.dll
```

The individual ZIPs contain one of these DLLs. Installed filenames and paths stay fixed for future overwrite updates. Plugin versions remain in DLL metadata and release tags. No README, source, preset, configuration file or third-party DLL is included in the installation ZIPs.

### One-time migration from earlier GIN packages

Earlier GIN DLL filenames included versions and were placed in `BepInEx/plugins/GINLifeHacks`. Before the first fixed-name installation, move those **old GIN Timeline / GIN Advance DLLs outside BepInEx/plugins**, including copies in subfolders. Do not leave both names installed. Keep your configuration files and `UserData/GIN` presets. Subsequent updates overwrite the fixed filenames directly.

## Timeline helper — version 1.1.0

The tools since 1.0.6 (the 1.1.0 additions follow in the next section):

- **Resize-safe tools:** when the Timeline resize handle approaches the GIN buttons, all four buttons and their feedback move below the window. Widening the window restores the upper placement. The previous screen-top fallback and screen-edge visibility are retained. No setting is required.
- **Odd/even key selection:** use `Anchor` to remember a starting key, box-select a range, then press `1 / 3 / 5` or `2 / 4 / 6`. The anchor is number 1; keys are counted in time order independently for each track.
- **Right-side track browser:** `+ Tracks` shows a scrollable Interpolables panel. Candidates remain the actual native providers for the selected target.
- **Add keys with Ctrl + right click:** empty track/group cells use the pointer time; the right-side candidate list uses the playhead. Native middle click remains available. Existing keys are preserved.
- **Use Current Value with S:** updates all selected Timeline keys through the native command, including after viewport, Move Controller or KKPE manipulation. Shift/Alt may remain held with the default S binding; Ctrl+S stays scene save. Text entry, IME composition and F1 settings block editing shortcuts.
- **Group summary keys:** red when unselected and green when any represented child key is selected. Box selection and Alt + left drag work while the group is collapsed, including mixed visible/hidden key selections and ShalltyUtils Performance Mode on/off. A collision with an unselected destination key cancels the move and keeps existing keys.
- **Group insertion:** adds the current values to all descendant tracks, filling missing keys at that time without replacing existing keys.
- **Track/group ordering:** Up / Down (Shift + Up / Down before 1.1.0) moves selected tracks or groups among siblings while preserving their parent and children.

In **F1 → GIN Timeline life hack → Input**, configure `AddKeyframe`, `UseCurrentValue`, `MoveTracksUp` and `MoveTracksDown`. The default insertion binding is `LeftControl + Mouse1`; either Control key works. Keyboard insertion also works when the pointer is over a destination row. The older `RightClickAddsKey` option no longer controls insertion.

## New in 1.1.0 — animation workflow

Timeline helper 1.1.0, Advanced mode helper 1.1.0 and the new GIN Pad life hack 1.0.0 add:

- **Char Setup** (Timeline toolbar; **not in the distributed DLL** - it crashed the game on some installations, so the release ZIPs are built without it and it only exists in a private build from source, see [Development](#development-and-verification)): select one or more characters in the workspace and press it. It rebuilds the author's setup scene around each character (template `src/Timeline/CharacterSetup.xml`, generated from that scene by `src/tools/extract_character_setup.py`):
  - **Rows** with one key at 0:00 in the groups `R_FINGER`, `L_FINGER`, `MUNE_SHIRI`, `O_BODY_A` (one KKPE Blend Shape row per shape of `o_body_a`, in the scene's order, only when the body has Blend Shapes), `FACE`, `BODY` (now also KKPE rotation of `cf_d_shoulder_L` / `_R` and `cf_d_kokan`), the character's own position / rotation, and `STREAM`, `FOG`, `STRINGS` on the items below.
  - **Items with the scene's sizes**: three breath steams (size 0.584) on accessory point `a_n_headside`; on `a_n_dan` the anchor sphere (alpha 0) and a `STRINGS` folder with ten GIN MonaStream strings; on `a_n_kokan` a splash (size 0.001, Material Editor shader and properties), body mist and drifting fog; beside the character a sphere with seven hidden water streams, hidden through Material Editor.
  - **NodesConstraints:** the stream sphere follows the anchor sphere with the scene's position and rotation offset.
  - **Colour themes:** the first character gets the scene's blue rows, the second to fifth rose, green, amber and violet (white and grey rows stay), then blue again. A character keeps its theme on later presses.
  - Only what is missing is added: existing items, the link and rows are kept with their keys; a row found outside its group is moved into it. Items of mods that are not installed (Steam VFX, GIN MonaStream, nashi Splash, Pineapple AddCustomEffect / Vol2) are skipped with their rows and named in the feedback line. An old whole-renderer `o_body_a` row is not deleted (it may hold keys); the feedback line asks you to delete it.
- **Auto Key** (Timeline toolbar, optional `ToggleAutoKey` shortcut): a small record button like Blender's - blue with a red dot while on - and a red line on the Timeline window. The arrow next to it opens a menu with the curve of the keys Auto Key adds: `Previous key` (Timeline's own behaviour: copy the previous key's curve) or one of the first five presets of the keyframe window, `Line`, `Top`, `Bottom`, `Hermite`, `Stairs`. A key that Auto Key only updates keeps its curve. The choice is remembered (`AutoKey > Curve`). With playback stopped, moving an FK / IK guide object, a KKPE bone or a KKPE Blend Shape that already has a row writes its key at the playhead. Rows are never created, playback and seeking never write keys, and S still applies current values to selected keys. `AutoKey > Rows` in F1 lists the row types it handles.
- **Endless drags** (GIN Pad and the Advanced helper): a drag that changes a value by how far the mouse moves (the Pad's bars and `Sens`, the Body figure's buttons, the Move tab's square, turns and `Anim`, the Pad's time button, the Advanced helper's Bones +/- scrubs) no longer stops at the edge of the screen. As in Move Controller, once the drag moves the cursor is hidden and the mouse's own movement is used; it is parked in the middle of the game's screen meanwhile and comes back where the drag began when the button is released. It runs as fast as the visible cursor did (measured from the cursor's ordinary movement, Windows' pointer speed and acceleration included). The Map, the gaze, scroll bars and window moves follow the pointer as before.
- **Up / Down** move the selected rows or groups (click inside Timeline first). Existing Shift + Up / Down defaults are replaced once; any other binding is kept.
- **Ctrl + folder arrow:** Ctrl + click on a folder's open / close arrow opens or closes every folder at once, nested folders included, following the state the clicked folder gets.
- **Timeline 0:00** (KKPE Advanced mode > Blend Shapes): `Key all` registers every Blend Shape of the selected renderer, `Key filtered` only the rows currently listed (Search text, hidden rows, collapsed sections). `cf_O_face`, `cf_O_head` and `cf_O_mayuge` are always registered as one linked group row because keying face shapes one by one breaks the expression; change the list with `BlendShapes > TimelineGroupRenderers`. Needs both GIN plugins.

- **GIN Pad life hack 1.0.0** (new, third plugin): the `IK QOL` button on Studio's left toolbar opens one small window. Its tabs follow Studio's selection: a character gets `Map`, `Face`, `Body`, `Shapes`; items (or several objects, characters included) get `Move` and, when the item has Blend Shapes, `Shapes`. It needs KKPE but not KKPE's Advanced mode window. Only the window background is translucent (`BackgroundOpacity`, 50 %); buttons and bars stay solid. `UiScale` (1.045; a saved 1.1 follows once) sizes the whole window. Outside the window's top left corner, two small buttons follow the window: `▶` plays and pauses the Timeline (`❚❚` while it plays), and dragging `↔` left or right moves the playhead (`Sens` and Shift as on the bars, playback pauses first, endless like the bars). While it is dragged, and a moment after, a large readout on the window shows the time, how far it moved since the press (green later, orange earlier), the frame and a bar of the whole animation with a mark where the drag began. Only Timeline itself is needed for them. Outside the top right corner, a small round knob resizes the whole window (it takes the mouse in an area twice its size, so it is easy to grab): drag it right to enlarge, left to shrink; everything scales together, so the proportions stay, and the size (`UiScale`, 0.6 to 2) is remembered when the knob is let go. Right click on the knob: the default size. The window stays on the screen far enough for these controls to be reachable. Mouse input on the window never reaches Studio's camera, its gizmos or anything else behind the window; while a press that began on the window is held, the camera also keeps its place, whatever else tries to move it (not while Timeline plays). Long lists scroll with the wheel or the Pad's own thin scroll bar (handled like the drag bars, so dragging it never turns the camera). Every value is written through KKPE or Studio.
  - **Drag bars instead of sliders:** press anywhere on a bar and move left or right. `Sens` (default 0.6) sets how fast the value follows, Shift is four times finer, right click returns to the default. The drag keeps working outside the bar and beyond the edge of the screen (the cursor is hidden meanwhile).
  - **Keys:** with the Timeline's Auto Key on, every Pad edit of a row that exists is keyed by the Timeline itself. A middle click on any bar or on the Map keys at the playhead (a key already there is updated): the whole face on the Map and Face tabs, the selected parts on the Body tab, the renderer on the Shapes tabs. The Body tab has a `Key now` button instead, because a middle click on its figure resets a bone. Needs the Timeline helper 1.1.0.
  - **Face:** the eye, mouth, eyebrow and teeth Blend Shapes as bars. The small square in front of a bar pins the shape as a favourite, and three buttons choose what is listed: `Fav` only the pinned shapes, `Used` the pinned shapes plus those that a Timeline key of the character raises above zero or that are not zero right now, `All` everything. The choice is remembered. `ha00_def_cl`, `f00_def_cl`, `f00_def_op`, `mayu00_def_cl` and `mayu00_def_op` are left out of `Used` (setting `HiddenShapes`). The pupils are on the Map tab.
  - **Pupil limits** (`Limit`, on by default): X of `cf_J_hitomi_tx_L` stays within -0.017 .. 0.013 and the right pupil within the mirrored range, so a pupil cannot leave the eye. Z follows X: -0.0015 at the limit towards the nose, -0.0030 at the outer limit. Which direction is the nose side is measured on the character. All four numbers are settings.
  - **Map:** one graph for the whole face, slightly less tall than before (381 x 400). Left drag moves horizontally through 28 mouth poses and vertically through 20 eye poses, blending neighbours in between (Ctrl snaps to a pose); right drag on the same graph moves the gaze, the wheel changes pupil size. Shift + drag (either button) moves only horizontally or only vertically, whichever way the mouse went further. Below the map, two rows of two bars: `Brows` (11 eyebrow poses) | `Spread`, `L / R height` | `Size`, with `Center` and `Limit`. The gaze follows the camera: dragging right moves the pupils right on screen, also on an upside-down face. The poses are the author's own: `src/tools/learn_expression_map.py` learned them from 549 keyed face poses of two finished scenes; every pose is one that was really keyed and no two eye or mouth poses are near-duplicates.
  - **Making your own map:** every pose has a small triangle - above the map for the mouth columns, to its left for the eye rows, above the `Brows` bar for the eyebrow poses. Click a triangle to go straight to that pose. Right click it to open the pose editor: the character shows that pose and its Blend Shapes open as bars to the right of the map, with the same pins and `Fav` / `Used` / `All` choice as the Face tab (shapes the pose consists of are always listed). Whatever the bars change is the pose. While a pose is open, a right click on another triangle of the same part stores the face the character has now as that pose and opens it. While a pose is open its direction of the map is locked, so dragging on the map tries it with every pose of the other direction. `←` `→` (`↑` `↓`) move the pose, `+` adds a copy after it, `-` deletes it, `Undo` returns it to how it was when opened, `Defaults` restores the learned map and keeps yours as `expression-map.bak.txt`. Everything is saved to `UserData/GIN/Pad/expression-map.txt` by itself when the mouse is released.
  - **Tabs:** `Map`, `Face`, `Body` and `Shapes` are toggles; every open tab is shown beside the others (for example Map and Body together), and the open tabs are remembered (`OpenTabs`; item tabs `OpenItemTabs`). Every Blend Shape bar has the Face tab's length.
  - **Body:** a figure of buttons like KKPE's small window, plus what that window lacks. Small green buttons: `R.Sho` / `L.Sho` (rotation of `cf_d_shoulder_R` / `_L`), `kokan` between the knees (rotation of `cf_d_kokan`), and the less used waist, bust 3, buttocks and toes. Grey buttons are Studio IK targets (shoulders, elbows, hands, body, thighs, knees, feet) and are also selected in Studio; green buttons are bones edited through KKPE: head, neck, `cf_j_waist02`, toes (rotation), `cf_d_bust01` / `cf_d_bust03` (move, rotation, scale) and `cf_d_siri01` (rotation, scale). A bone that Studio FK controls is rotated on its FK node. Click selects one part, Ctrl + click adds or removes parts; a selection holds either IK targets or bones, and every selected part receives the same movement or turn as seen on screen (nothing is mirrored). Drag on a button to edit without the bars: a green button turns the bone freely as seen from the camera (right drag rolls it, middle click puts its rotation back to the default), a grey button moves the IK target on the floor plane (right drag moves it up and down). Dragged on the figure, `Neck` only bends on its own X and Z (it never twists) and `R.Sho` / `L.Sho` only swing on Y; the bars below keep all three axes. The figure is 10 % narrower than the other tabs' share. The tall bars at the bottom show the last selected part, coloured X red, Y green, Z blue, with its real name (`cf_t_hips(work)` for `Body`). The small plain-grey trapezoid `Root` between the toes moves the whole character instead (drag on the floor plane, right drag up and down, middle click keys its position). Above them are KKPE's own limb copy and `Swap pose` functions and `Reset bone`. Every press on the Body tab (a drag, a bar, a reset, a limb copy, `Swap pose`) is one step of Studio's Undo: Ctrl + Z takes it back, Shift + Z does it again. That covers what KKPE itself cannot undo, its bone overrides. The `Shapes` tab holds the Blend Shapes of `o_body_a`, never taller than the figure (two columns and a scroll bar when needed).
  - **Move** (items): Move Controller's controls inside the Pad. The square: left drag moves the selection on the floor plane as seen from the camera, right drag moves it up and down (at `Sens` 1 it keeps up with the mouse on screen). `View X / Y / Z` (grey) turn around the camera's right axis, the world's up axis and the viewing direction; a right drag on `View Y` turns the whole selection around the first object. `X / Y / Z` (red, green, blue) turn each object around its own axes, or the selected FK node(s). A click without dragging turns 90 degrees (right click: back). `To camera` moves to the camera's target (right click keeps the selection's arrangement), `Anim` (drag) scrubs the animation of animated items and characters, `Reset FK` zeroes the FK node(s). With an FK node selected, the mouse wheel on the tab steps to the next / previous node; Alt keeps a single node, Shift grows or shrinks a range of nodes that turn together. A child whose parent is also selected moves with its parent only. Every move or turn is one step of Studio's Undo.
  - **Shapes** (items): the Blend Shapes of the first selected item that has any, grouped by renderer, through KKPE (no favourites). KKPE keeps its editor off on a newly added item (normally switched on with right click > `Enable` in KKPE's window); the tab says so, and the first edit on it switches KKPE on for that item.
- **Eyelash fix:** applying a saved expression (and everything on the Pad) now follows KKPE's eye link, as Timeline's `BS (renderer, L)` rows do. Previously the eyelashes stayed behind when an expression closed the eyes, until the Timeline was moved.

Everything these buttons create is ordinary Timeline, KKPE, Studio item and Material Editor data. A recipient needs those plugins as before, not GIN.

## Advanced mode helper — version 1.1.0

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

GIN does not introduce a scene data provider or custom Interpolable provider. Group diamonds display the original child keys; native Timeline keys, groups, interpolation curves and Shallty saved curve files keep their existing format. A recipient does not need GIN solely to replay those native edits, but still needs the scene's original Timeline, KKPE, NodesConstraints, assets and other dependencies. A scene saved with 1.1.0 (with Char Setup's rows and items and an item Blend Shape set on the Pad) was reopened and played in a fresh process with all three LifeHacks DLLs absent: the Timeline came back unchanged, KKPE's values too. See [verification scope](docs/VERIFICATION.md).

## Development and verification

Source is in `src/`; start with the [source guide](docs/SOURCE.md) for the lifecycle, input, UI and diagnostics files. Installation ZIPs contain runtime files only. Build with your own local Koikatsu references:

```powershell
.\src\build.ps1 -GamePath 'your Koikatsu game folder'
python .\src\package.py --output dist
```

The Timeline helper is built twice: `src/Timeline/bin` holds the private edition with Char Setup, `src/Timeline/bin/public` the edition for distribution without it (`-Edition Private` / `Public` builds one of them). `package.py` packs the public edition; `--edition private` packs the other one into ZIPs marked `_private`.

No game assemblies, third-party plugins, character cards or curve presets are redistributed here. [Verification](docs/VERIFICATION.md) and [changes](CHANGELOG.md) describe the tested versions and limitations. Report problems through [Issues](https://github.com/GoriGinsan/GIN_LifeHacks/issues) with plugin versions, the affected operation and the relevant error text.
