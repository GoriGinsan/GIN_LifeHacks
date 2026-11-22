# Verification

## v1.1.1, built 2026-11-22

Timeline helper 1.1.1 is the 1.1.0 source compiled without Char Setup (`build.ps1` public edition). Checked: it compiles; the DLL contains none of the Char Setup code, the `Char Setup` button label or the embedded `CharacterSetup.xml`; Auto Key, track registration (`RegisterTracksFromXml`) and the Pad services (`KeyFaceRows`, `KeyNamedRows`) are present. It was **not run in the game** again. The Advanced helper and GIN Pad in the v1.1.1 ZIPs are the unchanged v1.1.0 binaries (byte-equal to the release assets of v1.1.0).

| Runtime DLL | Plugin version | SHA256 |
| --- | --- | --- |
| GIN Timeline life hack.dll | 1.1.1 | `C0F7FBDD978AC5C75236F0B04CE6518B89C180F960C76A32BA24EC6A31421B1D` |
| GIN Advance mode life hack.dll | 1.1.0 | `C278AC00C25B885CB3699CCF7F30E62AAF859542BB08F3F4A5FCC3AE5C101774` |
| GIN Pad life hack.dll | 1.0.0 | `A545D4B7C488B94C154B7DACA4495434CBCE56C6A149C18C556763801F7B2BD8` |

## v1.1.0, checked 2026-11-19

Timeline helper 1.1.0 + Advanced helper 1.1.0 + GIN Pad life hack 1.0.0, Koikatsu CharaStudio / Unity 5.6.2f1 / BepInEx 5.4.23.2 / Timeline 1.5.6 / KKPE 2.21.5 / ShalltyUtils 1.3.2, in the marked isolated test game with a vanilla character. Material Editor was added to that game for this run.

| Check | Result |
| --- | --- |
| Existing regression suite (insertion, S, odd/even, groups, Performance Mode, drag, collisions, Enabled OFF/ON, camera guard, Advanced UI, expressions) | Passed, batch and rendered runs |
| Up / Down reorder | Selected group block and row move among siblings; a block already at the edge keeps its order |
| Blend Shapes `Timeline 0:00` | `Key all` 39 teeth rows; `Key filtered` follows Search and hidden rows; face renderer becomes one linked group row; repeat adds nothing |
| Auto Key control | Small record icon without text, blue with a red dot while on (seen in the screenshots, off and on); the arrow menu has 6 rows, each preset read from Timeline's own fields with a curve icon; a row click chooses and closes the menu. With Stairs chosen, the three keys Auto Key added carry the Stairs curve and a key it only updated keeps its curve. The open menu was not captured in a screenshot |
| Ctrl + folder arrow | On every visible folder header the handler sits after Timeline's own open / close handler, so it sees the clicked folder's new state; all folders (2 on the fixture) close and open together. The Ctrl + physical click itself was not performed |
| Char Setup | The five item mods were copied into the test game for the run. 26 objects added: breath steams on point 8 (`a_n_headside`, size 0.584), anchor sphere on `a_n_dan` (alpha 0, the scene's local position), STRINGS folder with 10 items, splash on `a_n_kokan` (size 0.001, render queue 9999 from Material Editor), stream sphere beside the character (renderer hidden by Material Editor) with 7 hidden water streams; the NodesConstraints link from the anchor to the stream sphere exists. 100 rows added, 4 existing; two placed wrongly on purpose were moved into `BODY` with their template place and colour. Groups R_FINGER 15, L_FINGER 15, MUNE_SHIRI 12, FACE, BODY 25 (ends with `cf_d_shoulder_L`, `_R`, `cf_d_kokan`), STREAM 11, FOG 2, STRINGS 10. A second press adds no object, link or row. The `o_body_a` order was checked on the face mesh (the vanilla body has no Blend Shapes). A second character added in the run got rose rows while the first stayed blue. The splash's KKUSSalpha shader is not installed in the test game, so the shader swap itself was not seen |
| Auto Key | IK target, KKPE bone rotation and face Blend Shape edits keyed at the playhead with the edited values; a further edit updates the same key; seeking, playback, changes without input and Auto Key off write nothing |
| Pad: toolbar and target | `IK QOL` toggle added to KKAPI's left toolbar; the selected character is found through KKPE with Advanced mode closed |
| Pad: eye link | Setting `eye_face.f00_def_cl` on the face reached all 7 linked renderers (upper / lower eyelashes, tears, pupils). This is the cause of the vanishing eyelashes: expressions were applied without the link |
| Pad: Face tab | `Used` lists a shape once it is not zero and never a `def` shape; `All` lists 154; `Fav` lists exactly the pinned shapes, also when another shape is not zero, and nothing without pins. A Timeline row of a single shape counts as used only when one of its keys is above zero. Labels drop only the game's mesh prefix (`eye_face.`, `mayuge.` ...); names with their own dots such as GIN Kupaa3 `P.Open1.L` or GIN Puffy `vagina.open` are shown whole |
| Pad: tabs and figure | Map, Face, Body, Shapes are toggles, open tabs drawn side by side (screenshot of Map + Body + Shapes) and remembered; item tabs Move, Shapes with Move open by default. Sizes: `UiScale` default 1.045 (5 % below 1.1), Body figure 306 wide (10 % narrower) with its bar and button rows fitting, every Blend Shape bar 156 long like the Face tab's, map 381 x 400 with two rows of bars and a button below it. Body figure: kokan between the knees, R.Sho / L.Sho above and outside the shoulders (rotation only, `cf_d_shoulder_R` / `_L`), small buttons for buttocks, toes, waist, bust 3; no two buttons overlap. Map triangles: a click selects the pose directly and closes that part's editor; selecting an eye row leaves an open mouth pose open. The default map is the author's own (20 x 28 + 11) |
| Pad: scrolling | The Map pose editor was opened by storing a face with 36 mouth shapes (right click on a triangle): its list scrolls with the Pad's own bar, inside the window (screenshot). The bar jumps to a press on its track, follows a drag and stops at both ends; a MouseDown event on the window claims the pointer for the camera block, one outside does not. A physical drag in the user's game was not performed |
| Pad: real mouse in the test game | With real (injected) mouse input in the isolated test game: on the previous build a click on the Face tab and a drag of the window header turned Studio's camera, and the log showed the press counted as outside the window (`began on window False`) while the pointer was inside. Cause: the hit test used the window rectangle that every layout pass shrinks to 10 x 10. With the fix (hit tests use the rectangle as last drawn) the same click, header drags (slow and fast), map pad, Sens bar, empty spot and scroll bar drags left the camera still, with no camera lines in the log |
| Pad: camera pin | During a held press that began on the window, a camera turn made by something else is undone before Studio applies it; after release the camera is free again. Not applied while Timeline plays |
| Pad: gizmo block | With the window placed over Studio's move gizmo, the EventSystem's raycast returns only the Pad's own inert object; with the window beside it, the gizmo is hit again. (Before this change the gizmo was hit even under the window.) |
| Pad: camera block | Both camera input methods of `Studio.CameraControl` carry the Pad's prefix. While a press that began on the window is held they return without running (the camera's drag latch is untouched); afterwards the original runs again |
| Pad: pupil limits | Gaze far beyond both limits stops at -0.017 / 0.013 (left) and -0.013 / 0.017 (right); Z is -0.0015 at the nose-side limit and -0.0030 at the outer limit; spread, height difference and size are stored by KKPE and read back. On the vanilla head the left pupil's +X points towards the nose |
| Pad: Map | 28 x 20 + 11 learned poses load (closest two eye poses 42.8 and mouth poses 30.0 weight units apart); blending between rows and clamping at the ends; file round trip; 81 weights verified on the character; mouth-only apply leaves the eyes alone; the current face reads back as a pose |
| Pad: Map pose editor | Opening mouth pose 6 shows it on the character and locks its direction (a map drag moves only the eyes); with `Fav` the editor still lists the shapes the pose consists of, and the teeth row; a bar edit becomes the pose and the file is written when the mouse is released, not before; later / earlier, add copy, delete and undo; right click stores the current face as an eye pose; `Defaults` restores the learned poses and keeps a backup file |
| Pad: Body | All 11 bones and 13 IK targets found by exact name (`Body` is `cf_t_hips(work)`); bone rotation / scale / position stored and removed through KKPE; Ctrl adds and removes parts and never mixes IK targets with bones; a button drag moves both selected hands the same way on the floor plane and the right button only in height; a drag on a toe button gives both toes the same world turn; a bar edit moves both hands the same way; a middle click removes the rotation override of the whole selection, or of the clicked bone alone when it is not selected; KKPE accepted a hand copy |
| Whole-face key | Requested from the Pad: 45 face rows keyed at the playhead with the applied values two frames after the request; tongue rows untouched |
| Pad: Body drag limits | A drag (and a right drag) on `Neck` changed its X and Z but not Y; on `R.Sho` only Y; the bars still turned all three axes; a turn through X = 90 degrees keeps Y (Unity's second set of Euler angles is handled) |
| Pad: Shift axis lock | With Shift a press does not move the marker; the larger direction of the movement wins (horizontal or vertical only); letting go of Shift frees the other direction; on the map a Shift drag changed only the mouth column |
| Pad: keys | A middle click request is taken once by the tab that drew the control; the Body tab's `Key now` keyed the neck's row at the playhead through the Timeline helper. Timeline: keying by name (`cf_t_hips(work)`, `cf_j_waist02`) keyed exactly those rows at 2 s, not the face; a repeat at the same time updated the keys instead of adding |
| Pad: item mode | With a vanilla FK item (24 nodes) selected the window switches to item mode, also with a character selected as well; the Move square moved it on the floor plane (left) and only up (right); a `View Y` click turned it 90 degrees around up, an `Own X` right click -90 degrees on its own X; Studio's Undo took the four steps back. With an FK node selected: wheel to the next node, Shift range of 2, the range slides along the chain, `Own Z` turned only the range's nodes, `Reset FK` zeroed them, Alt went back to a single node |
| Pad: item Shapes | A GIN Puffy Studio item just added: the Shapes tab is offered once KKPE has found its Blend Shapes (15 bars, 2 renderers, in the screenshot two columns under the renderer name). KKPE's editor on it was off and the tab said so (screenshot); the first bar edit switched it on, and ten frames later the mesh itself showed the weight (42). The item had no Timeline rows, so its middle-click key reported "No Timeline rows" |
| Pad: Body undo | One press with a neck drag and a hand IK move became one step; Studio's Undo put both back and Redo did them again; Undo after `Reset bone` brought the neck's rotation back; a press that changed nothing added no step; a KKPE step pushed during the press (as Swap pose and the limb copies do) was folded in, so one Undo took the whole press back; the step is made 2 frames after the release, not in the release frame |
| Pad: corner controls | Functions: play and time buttons stacked outside the window's top left corner, the knob outside its top right corner (drawn 7 px, half the 14 px area that takes the mouse; screenshot), all three counted as the window by the camera / EventSystem guards; play and pause; a press on the time button pauses playback, 60 px moved the playhead 1.00 s with the readout showing `+1.00 s`, a drag far left stops at 0 with a `-` change, the readout stays after the release; the knob: a quarter wider is a quarter larger, the scale stops at 0.6 and 2, and the chosen size is written to `UiScale`; the window is kept on screen with room for the controls. Real mouse in the test game: the play button showed `❚❚` while playing; a `↔` drag showed the readout (`4.61 s`, `of 10.00 s`, `+3.40 s` in green, `frame 277 / 600`, the bar with the start mark; screenshot); dragging the knob 240 px right made the window 1.27 (from 1.045) with the knob under the pointer and `UiScale = 1.271` in the settings file; a right click on the knob put back 1.045. The toolbar buttons of the Timeline helper were removed |
| Endless drags (real mouse) | In the isolated test game, with real relative mouse input (the visible cursor moved 120 px per step of 60 counts): a Body `Move X` bar drag of 60 steps from x 570 changed the value by 0.864 (7200 px at `Sens` 0.6, where the screen edge would have allowed about 1350 px); a slow drag (50 ms per step) on `Move Z` likewise; the time button (then still on Studio's toolbar, since replaced by the Pad's, which uses the bars' drag code) dragged right to the end and then 4 steps left from x 58 went from 10 s to 3.03 s (the edge alone allows about 1 s). During each drag the cursor sat in the middle of the game's screen (963, 565) and after the release it was back where the drag began. Two earlier builds failed here and were fixed: a cursor held where the drag began stopped the drag at the screen edge, and the time button counted its first step twice |
| Endless drags (functions) | The first movement starts holding; movement three screen widths wide is passed on once; a Body drag of two screen widths moved the hand 11.52; release, and a release no control reported, both let go; 20 px per axis unit until measured, then the measured speed, older movement fading |
| Replay without GIN | Saved with all three GIN DLLs: a scene with the regression fixture, Char Setup on the character (107 Timeline rows and its items, the five item mods installed) and a GIN Puffy item whose Blend Shape was set on the Pad (KKPE switched on by the Pad, `Insert_20` = 42). The scene's ExtendedSave data holds no LifeHacks entry. Reloaded in a fresh process with the Timeline, Advanced and Pad DLLs and assemblies absent: the native Timeline XML matched exactly (rows, groups, order, key times, values, curves), KKPE's 6 bones and 343 morphs came back and reached the transforms, the item's KKPE editor was on with `Insert_20` = 42 on the mesh, and Seek / playback interpolated as saved |
| Native serialization | Timeline's own scene writer serialized the registered rows, groups and colours |
| Screens | Toolbar with six buttons, red Auto Key state and window line, Blend Shapes `Timeline 0:00` row, and the Pad (with its corner controls) on the Face, Map (pupil bars below), Body, Shapes and item Move + Shapes tabs inspected at 1920 x 1080 |

| Runtime DLL | Plugin version | SHA256 |
| --- | --- | --- |
| GIN Timeline life hack.dll | 1.1.0 | `F649430ECC2123440B57C78BA6BF175E6EF8B1B93BF41F79F022A95EE147EF15` |
| GIN Advance mode life hack.dll | 1.1.0 | `C278AC00C25B885CB3699CCF7F30E62AAF859542BB08F3F4A5FCC3AE5C101774` |
| GIN Pad life hack.dll | 1.0.0 | `A545D4B7C488B94C154B7DACA4495434CBCE56C6A149C18C556763801F7B2BD8` |

Limits: KK only, not KKS. The last 2026-11-19 run (the Pad's corner controls; the Timeline helper without its toolbar buttons) passed 90 checks with all five Char Setup item mods. The endless drags were tried with injected mouse input (relative moves as a mouse sends them), not with a hand on a physical mouse; the Move tab's buttons, the Body figure's buttons and the Advanced helper's scrub buttons share the bars' code but were not dragged live; a second monitor beside the game was present but a game window spanning monitors was not tried. The Pad was driven through its own edit functions and inspected in screenshots. The new item Move tab, the Shift lock, middle-click keys, the FK wheel and the Body tab's Undo were exercised through their functions, not with a physical mouse or a real Ctrl + Z; Swap pose and the limb copies were not run under the Undo (their KKPE step was simulated); with the Timeline's Auto Key on, Pad edits are keyed by the Timeline's own detection (tested with native edits, not with Pad drags); real mouse drags and clicks on the bars, pads, map, triangles, body buttons and window (including the middle click and that the camera stays still), the mouse wheel, toolbar clicks, and the camera-following direction of the pupil pads were not exercised by hand. The pupil limits and the eyelash fix were checked on the vanilla head only, not on a modded head. The learned map was checked for its numbers, not for how the blended faces look. The body shapes panel was laid out with the face's Blend Shapes, because the vanilla body has none. Auto Key was driven by changing native values and marking input time in the test; real gizmo drags, KKPE sliders, typed values, physical arrow keys and long editing sessions were not exercised. The Char Setup row list was not run against a modded body or head. ShalltyUtils 1.3.1 was not rerun.

---


## v1.0.6, checked 2026-10-29

Timeline helper 1.0.6 + Advanced helper 1.0.3, built for Koikatsu CharaStudio / Unity 5.6.2f1 / BepInEx 5.4.23.2 / Timeline 1.5.6 / KKPE 2.21.5.

This maintenance update reorganizes source without changing editing features. A syntax-member comparison confirmed all 489 member definitions were preserved through file separation, apart from the internal reflection-helper name and version log messages. Plugin GUIDs, configuration keys, native callback names and expression/template XML identities are retained. Release builds omit debug symbols; the resulting 389 Timeline and 344 Advanced method/field records matched the tested builds, after normalizing the compiler's per-build private-data identifier.

| Check | Result |
| --- | --- |
| ShalltyUtils 1.3.1 | Functional and rendered layout regression suites passed |
| ShalltyUtils 1.3.2 | Functional and rendered layout regression suites passed |
| Resize handle | Native drag events and raycasts passed while shrinking/widening; tools retreat below the window and return above when space is available |
| Screen/layout cases | Widths 615 through 1100, 80/100/125% canvas scale, top/right/bottom edges, candidate OFF/ON and 120-frame stability passed |
| Timeline editing | Insertion, text guards, S/current values, alternating keys, groups, Performance Mode ON/OFF, mixed drag, collisions, ordering and Enabled OFF/ON passed |
| Advanced editing | Native L/R, display-template disk round trips, expressions with 154 morphs / 6 bones, native KKPE XML and rendered thumbnails passed |
| Scene compatibility | Native PNG save, process restart and reload/playback with both LifeHacks DLLs absent passed |

The scene test saved using ShalltyUtils 1.3.2 and reloaded with ShalltyUtils 1.3.1. It checked exact native Timeline XML, KKPE morph/bone data, runtime transforms, camera seeking and continuous playback. Neither LifeHacks GUID was an ExtendedSave provider.

| Runtime DLL | Plugin version | SHA256 |
| --- | --- | --- |
| GIN Timeline life hack.dll | 1.0.6 | `D50317C3232C782F7CD7FE86D46CB167935FFE40A0084D71D5AE99C4EE41F4EF` |
| GIN Advance mode life hack.dll | 1.0.3 | `48DE9D4E5AF194347A264206D4917F300E4B5F59667BC7D4F790ABC269DDA6E1` |

Installation ZIPs were checked for an exact runtime-only file list and equality to these DLLs. No game assemblies, third-party plugins, test drivers, presets or config files are included.

Limits: this is KK verification, not KKS or all third-party combinations. Native calls and Unity pointer events cover the regression suite; exhaustive physical keyboard/mouse delivery, dense production scenes and long editing sessions are untested. Graphical test runs had no LifeHacks exceptions. A test-only driver can initialize the existing native input module during hidden, unfocused launches. Native shutdown may report Unity JobTempAlloc warnings.

---

## Historical v1.0.5 verification, checked 2026-10-29

Koikatsu CharaStudio / Unity 5.6.2f1 / BepInEx 5.4.23.2 / Timeline 1.5.6 / KKPE 2.21.5. The Advanced helper remains the original 1.0.2 binary.

| Combination | Result |
| --- | --- |
| Existing local Timeline helper 1.0.4 + Advanced 1.0.2 + ShalltyUtils 1.3.1 | Major functional regression suite passed |
| Existing local Timeline helper 1.0.4 + Advanced 1.0.2 + ShalltyUtils 1.3.2 | Major functional regression suite passed; resize-handle obstruction reproduced at native minimum width 615 |
| Timeline helper 1.0.5 + Advanced 1.0.2 + ShalltyUtils 1.3.1 | Functional and rendered layout regression suites passed |
| Same Timeline 1.0.5 DLL + Advanced 1.0.2 + ShalltyUtils 1.3.2 | Functional and rendered layout regression suites passed |

The old toolbar covered the handle at minimum width: Unity's first raycast hit was `+ Tracks`. With 1.0.5 the first hit is `Resize Handle`, with all four GIN buttons below Timeline. Widening restores the upper placement.

Layout coverage uses the real Timeline resize handle's Unity drag handler, screen rectangles and EventSystem raycasts. It includes shrinking and expanding across widths 615, 640, 645, 650, 700, 800 and 1100; 80%, 100% and 125% canvas scales; top, right and bottom screen edges; candidate panel OFF/ON; and 120 consecutive frames without layout oscillation. Screenshots were inspected at 1920 x 1080. When there is insufficient room below the window, screen-edge clamping keeps the buttons visible near the lower edge.

Functional checks cover Enabled OFF/ON and restoration, native candidate relocation and insertion, Ctrl + right-click routing through the Shallty postfix, native middle click, alternate/disabled bindings, text guards, S/current values, odd/even selection, folded group selection, marker colors, mixed Alt drag with Shallty Performance Mode ON/OFF, collisions, ordering and camera guards. Advanced checks cover native L/R, visibility-template disk round trips, 154 expression morphs / 6 bones, native KKPE XML and rendered thumbnails.

A native scene PNG saved with LifeHacks 1.0.5/1.0.2 and ShalltyUtils 1.3.2 was reopened in a fresh Studio process using ShalltyUtils 1.3.1 with both LifeHacks DLLs and their assemblies absent. Exact Timeline XML (tree, order, times, values, curves, duration) matched, native KKPE restored 154 morphs / 6 bones, and native camera FOV seeking and continuous playback passed. Neither LifeHacks GUID appeared as an ExtendedSave provider. Other installed scene providers were retained.

| Runtime DLL | Plugin version | SHA256 |
| --- | --- | --- |
| GIN Timeline life hack.dll | 1.0.5 | `9747753B3A190F13DB4302AE9F1C3B5389D861CCCFE6BD3A45606D670F472A05` |
| GIN Advance mode life hack.dll | 1.0.2 | `95C4C1AFBDB0A16FBD3A491DED55FC5B3D47509A97EADA11985F75CB1DCAF2DE` |
| KK_ShalltyUtils.dll (upstream) | 1.3.1 | `7CCA95ACE39E6317B4D63D78C2532D7A57589153E2AA6C5F8D1EB78DC40EBCBF` |
| KK_ShalltyUtils.dll (upstream) | 1.3.2 | `1218296091923D674FA691AB8D7686CACDC6802996589FB9C04B6F290D30DD27` |

All installation ZIPs contain only the stated GIN runtime DLLs, with fixed filenames and byte equality to the tested binaries. No third-party DLLs, test drivers, personal files or configuration files are redistributed.

Limits: this is KK verification, not KKS. Future ShalltyUtils versions, every third-party plugin combination, dense production scenes and long editing sessions are untested. Functional input regressions use native method calls and Unity pointer events; this is not exhaustive physical keyboard/mouse coverage. Graphics runs had no LifeHacks exceptions. An initial graphics-disabled harness run produced KKAPI tooltip errors with a null input module; rendered runs used the native input module. The test driver initializes that module for hidden, unfocused launches only and retains original KKAPI processing. Native shutdown can report Unity JobTempAlloc warnings.

---

## Historical v1.0.3 verification

## Runtime-tested combination

Koikatsu CharaStudio / Unity 5.6.2f1 / BepInEx 5.4.23.2 / HarmonyX 2.9.

Timeline 1.5.6, KKPE 2.21.5, ShalltyUtils 1.3.1, NodesConstraints 1.6.3, KKAPI 1.48.1 and BepisPlugins 21.1.4.1.

The marked isolated Studio test passed with actual native plugin calls and programmatically supplied Unity pointer events and modifier states. It covered:

- Enabled OFF/ON, restoration of native candidates and removal of owned UI/hooks.
- Ctrl + right-click routing through both Timeline and the Shallty postfix, native middle click, plain-right preservation, alternate mouse/keyboard bindings and disabled bindings.
- S after focus loss, native value assignment, retained times/curves, Shift/Alt allowance, Ctrl+S reservation and actual text-field suppression.
- Odd/even anchor selection per track, candidate relocation and non-destructive duplicate insertion.
- Red/green group marker meshes, folded group rectangle selection, expanded-row deduplication, Alt drag previews/commits, collision rejection and cancellation.
- Actual Shallty Performance Mode hover UI, delegate replacement, line preview, folded descendants and drag-state restoration; mixed native drag with Performance Mode off.
- Sibling track/group ordering and native camera input guard regression checks.
- Companion Advanced 1.0.2 regressions: OFF/ON, native L/R selection, template disk round trips, expression capture/application, native KKPE scene XML and center-camera thumbnails.

Installation DLLs were renamed without modifying their bytes:

| Installed DLL | Plugin version | SHA256 |
| --- | --- | --- |
| GIN Timeline life hack.dll | 1.0.3 | `C46B3C0F4BBC59F64CEE3BCDD3C8AC143E27E52BD08616161CA0813269029BB6` |
| GIN Advance mode life hack.dll | 1.0.2 | `95C4C1AFBDB0A16FBD3A491DED55FC5B3D47509A97EADA11985F75CB1DCAF2DE` |

All three release ZIPs were checked for integrity, an exact runtime-only file list and byte equality with these DLLs. Configurations, presets, logs, source and test drivers are excluded from those ZIPs.

## Limits

This does not establish exhaustive physical mouse/keyboard delivery, dense production scenes, long editing sessions or compatibility with every third-party mod. Native Undo capabilities apply; bulk edits are not promised as a single Undo transaction.

A complete scene-PNG save, process restart and reload without GIN was not repeated for 1.0.3. The release uses native animation/KKPE serialization and introduces no GIN scene data provider. Older Timeline 1.4.2 and KKPE 2.18.1 paths remain in source, but this release's runtime verification used the versions listed above.

Guarded self-check entry points are present in the source/DLL; they run only with explicit isolated-test launch flags and a marked disposable test game. The isolated driver and its hidden-launch input initialization are not distributed.
