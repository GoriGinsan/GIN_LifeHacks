# Changes

## v1.0.3 — Timeline interaction fixes and first GitHub distribution

Bundle: Timeline helper 1.0.3 + Advanced mode helper 1.0.2.

- Group summary keys now follow native red/green selection colors.
- Native box selection includes real descendant keys represented on group rows.
- Alt dragging works for collapsed groups and mixed visible/hidden selections, with ShalltyUtils Performance Mode enabled or disabled.
- A destination collision rejects the complete group move before changing keys.
- Default insertion is Ctrl + right click, configurable through F1. Native middle click remains available.
- S invokes native Use Current Value after viewport/KKPE manipulation without requiring Timeline pointer focus. Default S permits held Shift/Alt and reserves Ctrl+S for scene save.
- Actual text fields, IME composition and the F1 settings screen suppress editing shortcuts.
- Installed DLL filenames no longer contain versions. ZIPs contain only BepInEx/plugins and runtime DLLs; documentation and source are maintained in this repository.

The runtime DLL contents are the same verified Timeline 1.0.3 and Advanced 1.0.2 binaries previously distributed with versioned filenames. Only the installation filenames and ZIP contents were changed for publication.

## v1.0.2 — English GIN interface

- All UI text added by GIN uses English; native labels and user-created names are retained.
- Old-Shallty compatibility errors identify the detected version and official fork download.
- Plugin GUIDs, native scene formats and local expression/template formats remain unchanged.
