# Verification

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
