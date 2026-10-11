# Verification

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
