# PoV Levelbound

PoV Levelbound is a Valheim progression mod that ties access to game content to the character level provided by **Path of Valheiman**. It lets server owners define biome-based requirements for items, equipment, building pieces, world resources, and boss altars, keeping player progression consistent with their character development.

## Requirements

- Valheim
- BepInEx 5
- Path of Valheiman 6.0.5 or a compatible version, installed on both the server and every client

Clients and server must use the same Path of Valheiman version.

## Features

- Blocks pickup of items when the required level has not been reached.
- Blocks crafting and upgrading restricted items.
- Blocks equipping weapons, tools, armor, ammunition, and accessories.
- Blocks consumption of configured items.
- Blocks placement, repair, and removal of building pieces.
- Blocks interaction with world resources such as ores, trees, rocks, and pickables.
- Blocks activation of boss altars.
- Shows level requirements in item tooltips, recipes, building-piece descriptions, and altar hover text.
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

`Dorn.PoVLevelbound.cfg` controls the required level for each biome and allows each restriction group to be enabled or disabled independently: pickup, crafting, equipment, consumption, building, resources, and altars.

On its first run, `GenerateFilesAutomatically` creates initial rules in the YAML files and then disables itself. Enable it again after adding mods that introduce new items or building pieces.

### YAML Files

The YAML files are stored in `BepInEx/config/PoVLevelbound/`:

- `materiais.yml`: materials, ingredients, food, and general inventory items.
- `equipamentos.yml`: weapons, tools, armor, ammunition, and accessories.
- `construcoes.yml`: hammer building pieces.
- `recursos.yml`: trees, rocks, ores, and other world resources.
- `altares.yml`: specific requirements for boss altars.

Each entry is assigned to a biome. The configured level for that biome becomes its minimum requirement.

## Multiplayer Synchronization

The server is the source of truth for configuration and YAML rules. Clients automatically receive the active rules when they connect. When `LockConfiguration` is enabled, clients cannot override server-defined settings.

## Notes

- `AdminBypass` allows administrators to ignore restrictions when enabled.
- Set a biome or altar level to `0` to disable its requirement.
- Configuration and YAML changes are reloaded by the mod while it is running.
- Back up YAML files before making broad progression changes.
