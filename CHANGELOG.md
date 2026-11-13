# Changelog

## 1.0.7 — November 13, 2026

- Updated the default biome levels for Ocean to 77, AshLands to 160, and Deep North to 190. Existing configurations keep their saved values.

## 1.0.6 — November 11, 2026

- Added a synchronized option to prevent new Altars of Power and block offerings at existing ones. Previously accumulated monster power can still be reset at an altar.

## 1.0.5 — November 11, 2026

- Fixed client-side administrator bypass to use the status validated by the server.
- Added rate-limited administrator bypass details to diagnostic logging.
- Kept Marketplace level checks on the client before currency is deducted.
- Made Marketplace purchase and chest swap restrictions respect the pickup setting.
- Added level requirements to VNEI item and building-piece tooltips.
- Aligned the VNEI crafting tab with the Path of Valheiman interface.
- Reduced optional integration lookup overhead and repeated VNEI tab repositioning.

## 1.0.4 — November 11, 2026

- Fixed administrator bypass checks for remote players on dedicated servers.
- Level requirements remain visible to administrators while bypass is enabled.

## 1.0.3 — November 11, 2026

- Added a local setting that hides the Path of Valheiman Discord button in the main menu by default.
- Added optional replacement of that button's destination, limited to validated HTTPS Discord invite URLs.

## 1.0.2 — November 11, 2026

- Added synchronized controls for Path of Valheiman encounters: Ragn stalls, Haldor's finds, the Wanderer, and Sigrid.
- Added a configurable allow-list of Path of Valheiman commands retained in the F5 console, including the selected administrator commands.

## 1.0.1 — November 10, 2026

- Added native Path of Valheiman item tooltips when hovering over Marketplace buy and sell listings.
- Preserved item data and prefab identification so configured level requirements appear in listing tooltips.

## 1.0.0 — November 9, 2026

### Initial release

- Progression based exclusively on the character's Path of Valheiman level.
- Configurable biome-based requirements for items, equipment, building pieces, resources, and altars.
- Configuration and YAML rule synchronization between server and clients.
- Level requirements displayed in relevant interfaces.
- Automatic generation of initial YAML rules.
- Protection against item loss when swapping with a level-locked item in a chest.
