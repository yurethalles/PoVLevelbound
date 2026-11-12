# PoV Levelbound

PoV Levelbound is a Valheim progression mod that ties access to game content to the character level provided by **Path of Valheiman**. It lets server owners define biome-based requirements for items, equipment, building pieces, world resources, and boss altars, keeping player progression consistent with their character development.

## Requirements

- Valheim
- BepInExPack Valheim 5.4.2360 or newer
- Path of Valheiman 6.0.9 or newer, installed on both the server and every client

Clients and server must use the same Path of Valheiman version.

## Features

- Blocks pickup of items when the required level has not been reached.
- Blocks taking restricted items from containers, including individual transfers and the take-all action.
- Blocks crafting and upgrading restricted items.
- Blocks equipping weapons, tools, armor, ammunition, and accessories.
- Blocks consumption of configured items.
- Blocks placement, repair, and removal of building pieces.
- Blocks interaction with world resources such as ores, trees, rocks, and pickables.
- Blocks activation of boss altars.
- Shows level requirements in item tooltips, recipes, building-piece descriptions, and altar hover text.
- When Marketplace and Server NPCs Revamped is installed, hovering over an item in the buy or sell list shows the native Path of Valheiman item tooltip, including configured level requirements.
- When VNEI is installed, its item and building-piece tooltips include configured level requirements, and its crafting tab is aligned with the Path of Valheiman interface.
- Disables the Marketplace buy button for items restricted by the player's level and checks again before currency is deducted. This protection runs on the client and does not validate direct requests from modified clients.
- Displays a configurable message when a player attempts a blocked action.
- Prevents item loss during inventory-to-chest swaps when the chest item is level-locked.
- Synchronizes configuration and YAML rules from the server to all connected clients.

## Default Biome Levels

| Biome | Minimum level |
|---|---:|
| Meadows | 1 |
| Black Forest | 19 |
| Swamp | 43 |
| Mountain | 77 |
| Plains | 97 |
| Mistlands | 130 |
| Ocean | 130 |
| AshLands | 181 |
| Deep North | 200 |

These values are defaults only and can be changed in the server configuration file.

## Installation

1. Install Path of Valheiman with the same version on the server and all clients.
2. Copy `PoVLevelbound.dll` to `BepInEx/plugins/` on the server and all clients.
3. Start the game or server once.
4. Configure `BepInEx/config/Dorn.PoVLevelbound.cfg` and the YAML files in `BepInEx/config/PoVLevelbound/` to match the desired progression.

Path of Valheiman is required. PoV Levelbound will not run without it.

## Configuration

`Dorn.PoVLevelbound.cfg` controls the required level for each biome. Pickup, crafting, upgrading, equipping, consuming, placement, repair, removal, resources, and altars each have their own restriction switch.

The server can also configure the blocked-action message and the minimum interval between messages. The placeholders `{required}` and `{current}` insert the required and current player levels. The older `{nivel}` and `{atual}` placeholders are also supported.

### Path of Valheiman options

The synchronized encounter switches control new Ragn stalls, Haldor's Path of Valheiman finds, the Wanderer, Sigrid, and Altars of Power. Disabling Ragn does not remove stalls that already exist. Setting `EnablePowerAltars = false` prevents new Altars of Power and blocks offerings at existing altars. It does not remove altars or reset previously accumulated monster power; the existing reset interaction remains available.

The `Commands` setting lists the Path of Valheiman commands to retain in the F5 console. Commands still require `devcommands`; the configuration description identifies additional development commands that should only be enabled with care.

The Path of Valheiman Discord button is hidden in the main menu by default. `ShowPoVDiscordButton` controls its visibility and takes effect after a restart. `PoVDiscordButtonUrl` can replace the destination with an HTTPS `discord.gg` or `discord.com/invite` link; leave it empty to use the original destination. These interface settings are local to each player.

### Rule generation and diagnostics

On its first run, `GenerateFilesAutomatically` creates initial rules in the YAML files and then disables itself. Enable it again after adding mods that introduce new items or building pieces.

`GenerateEquipableItemList` creates `todos-itens-equipaveis.yml` as a reference list grouped by source. The reference list is not applied as rules or synchronized; copy entries into the appropriate rule files. To regenerate it, delete the existing reference file and restart with the setting enabled. `DiagnosticMode` adds information about loaded rules, unresolved recipes, and administrator bypass decisions to the log. Bypass diagnostics are rate-limited.

### YAML Files

The YAML files are stored in `BepInEx/config/PoVLevelbound/`:

- `materiais.yml`: materials, ingredients, food, and general inventory items.
- `equipamentos.yml`: weapons, tools, armor, ammunition, and accessories.
- `construcoes.yml`: hammer building pieces.
- `recursos.yml`: trees, rocks, ores, and other world resources.
- `altares.yml`: specific requirements for boss altars.

In the first four files, each prefab is assigned to a biome, and the configured level for that biome becomes its minimum requirement. For example:

```yaml
Swamp:
  - Iron
```

`Reference` and `Unclassified` sections do not apply restrictions. Altars instead use direct prefab-to-level entries in `altares.yml`, such as `Bonemass: 20`; the key can identify the altar/location or its associated boss prefab.

## Multiplayer Synchronization

The server is the source of truth for configuration and YAML rules. Clients automatically receive the active rules when they connect. When `LockConfiguration` is enabled, clients cannot override server-defined settings.

## Notes

- `AdminBypass` allows administrators to ignore restrictions when enabled; level requirements remain visible in the interface.
- Set a biome or altar level to `0` to disable its requirement.
- Configuration and YAML changes are reloaded by the mod while it is running.
- Back up YAML files before making broad progression changes.
