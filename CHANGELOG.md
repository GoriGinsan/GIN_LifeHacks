# Changes

## v1.0.5 — Resize-safe Timeline tools and ShalltyUtils 1.3.2 verification

Bundle: Timeline helper 1.0.5 + unchanged Advanced mode helper 1.0.2.

- Move the GIN buttons and feedback below Timeline before they cover its resize handle; restore the preferred upper position when space is available.
- Compare the actual screen rectangles with a small clearance, including after screen-edge clamping, rather than assuming a fixed window width or ShalltyUtils version.
- Retain the local 1.0.4 screen-top fallback and screen-edge visibility fixes. Version 1.0.4 was a local update and was not a GitHub release.
- Verify the same DLL with ShalltyUtils 1.3.1 and 1.3.2, including Performance Mode, native resize events, candidate toggles, UI scale and screen edges.
- Verify scene PNG save and fresh-process reload/playback without either LifeHacks DLL.
- Build with explicit .NET 3.5-era game/framework references rather than the compiler default reference list.


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
