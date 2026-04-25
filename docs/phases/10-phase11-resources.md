# Phase 11: Resource Gathering & Crafting
*Goal: Harvest resources, craft items, material progression*

## 11.1 — Inventory System
- [ ] `IInventoryProvider` interface (allows swapping between limitless and limited inventory later)
- [ ] `Inventory` class: list of item stacks with max stack sizes (limitless mode for now)
- [ ] `ItemDefinition` ScriptableObject: name, icon, stack size, category, description
- [ ] Item categories: Resource, Tool, Building Material, Consumable, Equipment, Special (mana orbs)
- [ ] Inventory UI: grid-based display, drag-and-drop, quick-bar

## 11.2 — Tool System
- [ ] `Tool` base class: damage type, harvest speed, durability
- [ ] Tool types: Axe (trees), Pickaxe (rocks/ore), Shovel (terrain), Hammer (building)
- [ ] Tool tiers: wood → stone → iron → steel (each tier harvests faster, accesses harder materials)
- [ ] Tool durability: degrades with use, repairable at workbench

## 11.3 — Resource Harvesting (Valheim-Style)
- [ ] `HarvestAction` (implements `IWorldAction`): damage entity, yield loot table drops on death
- [ ] Hit feedback: particles, sound, damage numbers
- [ ] Tree falling: physics-enabled fall on death → breaks into log segments → player chops logs for wood
- [ ] Rock breaking: fracture into smaller pieces, yield stone/ore from loot table
- [ ] Resource drops + mana orbs: `PickupEntity` instances spawn near destroyed entity
- [ ] Auto-collect within radius or manual pickup

## 11.4 — Crafting System
- [ ] `CraftingRecipe` ScriptableObject: input items + quantities → output item
- [ ] Crafting stations: hand crafting (basic), workbench (intermediate), forge (advanced)
- [ ] Recipe discovery: some recipes known by default, others found via exploration
- [ ] Crafting UI: recipe list, material requirements, craft button
- [ ] Queue crafting: craft multiple items in sequence
