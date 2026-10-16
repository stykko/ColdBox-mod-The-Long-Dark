# ColdBox — a snow-powered cooler for The Long Dark

A craftable, placeable cooler box for **The Long Dark 2.55** (MelonLoader 0.7.2, Il2Cpp). Fill it with snow and the food
stored inside decays more slowly for several game days. When the snow has melted you can collect the water.

Spanish quick guide: [`LEEME.es.md`](LEEME.es.md).

## Features
- **Craftable** at the workbench: 10 cedar firewood + 5 scrap metal + 5 tarps + quality tools (tools are not consumed).
- The crafted **Snow Cooler** is an inventory item. Select it and use **Place** (or *Use*) to place it with the
  placement mode (click to place, right click / Esc to cancel, mouse wheel or Q/E to rotate).
- **Remove** (cooler must be empty) puts the item back in your inventory. **Move** lets you re-place it.
- Hold the interact button on it to open the lid (it hinges away from you and opens part of the way). The game's own
  action picker offers: load snow, food storage, collect water, move / remove, close lid.
- **Food storage** is the game's real container inventory (food only). Stored food is also shown inside the box.
- **Snow load** lasts 5-10 game days (setting). While snow remains, stored food decays at a fraction of the normal speed
  (default: half speed). The tarp texture changes when there is snow inside.
- **Water:** after the whole load has melted you can collect non-potable water (added to your water supply, no bottles).
- A puff of **cold mist** comes out when you open the lid while there is snow.
- **Saving:** coolers, their snow, water and stored food are written to `UserData\ColdBox\<save slot>.json`.
- Everything is configurable in **Mod Settings**.

## Requirements
| What | Why |
|---|---|
| The Long Dark 2.55 | target game version |
| [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.2 | mod loader |
| ModSettings (`ModSettings.dll`) | options menu; must be in `Mods\` and is referenced by the project |

No other mod is required. The model, textures and icons are embedded in `ColdBox.dll`.

## Install (players)
Copy `ColdBox.dll` into `<game folder>\Mods\` next to `ModSettings.dll`.

## Build (developers)
1. Open `ColdBox.sln` in Visual Studio (".NET desktop development"; the project targets `net6.0`).
2. If the game is not in `C:\Program Files (x86)\Steam\steamapps\common\TheLongDark`, edit `GameDir.props`.
3. Run the game once with MelonLoader (it generates `MelonLoader\Il2CppAssemblies`) and close it.
4. Build (`Ctrl+Shift+B`). `ColdBox.dll` is copied to `<game>\Mods\` automatically.
5. Logs: `MelonLoader\Logs\Latest.log`.

The project references game and MelonLoader assemblies from your game folder (nothing from other mods is included
except `ModSettings.dll`, which must already be in `Mods\`). Do not commit those DLLs.

## Settings (Mod Settings > Snow Cooler)
Snow load duration, decay while cold, water per load, food capacity, enable water, hold time, interaction distance,
lid opening angle, cold mist, flip lid side, placement rotation step, crafting on/off and crafting time, and the
**interface language** (English by default, Russian optional; item and container names change after a game restart).

## How it works
| File | Purpose |
|---|---|
| `Core.cs` | MelonMod entry point |
| `ColdBoxManager.cs` | registry, hold-to-open, action menu, placement mode, spawning |
| `ColdBoxBehaviour.cs` | lid animation, snow load, melting, pending water, tarp swap, mist |
| `ColdBoxContainer.cs` | attaches and configures the game's `Container` |
| `ColdBoxItem.cs` | the inventory item: a runtime clone of a vanilla gear prefab, Harmony patches for prefab lookup, inventory *Place*, crafting safety net |
| `ColdBoxCrafting.cs` | workbench recipe (a copy of a vanilla `BlueprintData` with new requirements) |
| `ColdBoxIcon.cs` / `ColdBoxIcons.cs` | item picture and list glyph (embedded PNGs), action picker sprite lookup |
| `ColdBoxLoc.cs` | English/Russian texts (English text is the key), translation of the Mod Settings labels |
| `ColdBoxSteam.cs` | cold mist: a few billboards (hand-painted mist textures) animated from code, kept clear of the open lid |
| `ColdBoxSave.cs` | JSON persistence and Harmony hooks into the game's save / scene load |
| `ColdBoxDecay.cs` | Harmony patch on `GearItem.DecayOverTODHours` |
| `ColdBoxBuilder.cs` | builds the box from the embedded mesh and textures |
| `GameBridge.cs` | direct calls to game classes (time, water, audio, HUD) |
| `Settings.cs` | Mod Settings menu |

The gear prefab is created at runtime (no Unity project): the game looks gear up by name, including when loading a save,
so the lookups for `GEAR_ColdBoxKit` are answered by Harmony patches.

## Model pipeline
`tools/export_parts.py` converts `tools/fridge.fbx` into `ColdBox/Resources/parts.bin` + `meta.txt` (Python 3 + numpy):

    python tools/export_parts.py tools/fridge.fbx ColdBox/Resources/parts.bin ColdBox/Resources/meta.txt

Parts: Shell, Tarp, Jugs, Lid, Snow. The script checks that every triangle of the FBX ends up in a part and scales the
model so the lid is 1.2 m long (`TARGET_LEN`). Textures: `texture.jpg` (plain tarp) and `texture_snow.jpg` (tarp with snow).

## Diagnostics
- `UserData\ColdBox\gear_names.txt` — every gear name the game has loaded (used to find the recipe ingredients).
- `UserData\ColdBox\sprite_names.txt` — UI sprite names used for the menu icons.

## Known limitations
- Tested by the author on game version 2.55 only. Saving and loading with placed coolers / crafted items should be
  checked after big game updates.
- The inventory item is a clone of a vanilla item; if the game changes how gear is loaded, the item may need adjusting.
- Lid sounds are copied at runtime from any vanilla container in the scene; without one the lid is silent.
- Other mods that bypass crafting (cheat/"free craft" mods) may not hand over the item; a safety net adds it, but crafting
  normally is the supported way.
- No integration with the game's furniture editor (no public API found); the mod has its own placement mode.

## License
Source code: MIT (see `LICENSE`). met original concept and idea, custom 3D models, textures, and particle effects. stykko mod development and implementation, bringing the concept and assets together into a functional mod, with assistance from the AI coding assistant Claude.
