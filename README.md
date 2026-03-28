# Evershor - Classic Skills

An EverQuest-inspired skills and tradeskills mod for [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/). Adds 25 trackable skills, 6 tradeskills with 280 recipes, 4 magic schools, a full material economy driven by mining, fishing, hunting, and boss kills.

## Installation

1. **Install BepInEx 5** for Erenshor if you haven't already:
   - Download [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (Unity Mono version)
   - Extract into your Erenshor game folder (`Steam/steamapps/common/Erenshor/`)
   - Run the game once to generate BepInEx folders, then close it

2. **Install ErenshorClassicSkills:**
   - Extract the zip into your Erenshor game folder
   - Your folder structure should look like:
     ```
     Erenshor/
     └── BepInEx/
         └── plugins/
             ├── ErenshorClassicSkills.dll
             └── ClassicSkills/
                 ├── Icons/    (438 item icons)
                 └── Models/   (5 station models)
     ```

3. **Launch the game.** The mod generates its config on first run.

---

## Features

### Skills System (25 Skills)

Press **F8** (configurable) to open the Skills window. Skills level up through use and via an EQ-style skill-up chance system with a trivial cap. Training Points (3 per player level) can be spent to manually raise skills. Skills will not start leveling up till you put a training point in it. Skill cap formula: `(PlayerLevel × 5) + 5` — max 180 at level 35.

**Utility Skills (7):**
- **Fishing** — XP on every catch; higher skill = more nibbles = better catch rate
- **Foraging** — Gather herbs and materials from the wild (F9 keybind)
- **Swimming** — Levels from swimming; increases swim speed (+0.3% per level)
- **Bind Wound** — Heal yourself out of combat (+0.15% max HP per level, F10 keybind)
- **Meditate** — Regenerate mana while sitting; works in combat (M keybind)
- **Food Tolerance** — Extends food/drink buff duration (+0.5% per level)
- **Begging** — Ask NPCs for gold, items, or lore (; keybind)

**Combat Skills (8):**
- 1H Slashing, 1H Blunt, Piercing, 2H Slashing, 2H Blunt, Archery, Wands, Hand to Hand
- Each weapon skill adds bonus damage: +0.085% per level (up to +15.3% at 180)
- Weapon skill type shown on all weapon tooltips

**Magic Skills (4):**
- **Evocation** — Levels on damage spells. +0.08% spell damage per level
- **Abjuration** — Levels on buff spells. +0.3% buff duration per level
- **Alteration** — Levels on heals. +0.08% heal amount per level
- **Conjuration** — Levels on DoTs. +0.08% DoT damage per level

**Tradeskills (6):**
- Smithing (52 recipes), Baking (55), Brewing (46), Fletching (29), Jewelcraft (47), Tailoring (51)
- 280 total recipes crafted at stations placed in Port Azure and Stowaway's Step

---

### Tradeskill Crafting

Craft at appropriate stations. There are custom stations(loom, brewing barrel, oven, jewlers kit, fletching table) for each tradeskill at Port Azure and the castle/Warehouse(in the room to the right) on stowaway island. Campfires and ovens also work for baking. Brew barrels also work for brewing. Workbenches across the game also work for Fletching. Forges work for Smithing. Crafting has a success/fail chance based on skill level vs recipe difficulty. Failed combines consume materials (just like EQ).

**Recipe Sources:**
- **Starter recipes** (2 per tradeskill) — auto-learned on first load
- **Vendor recipes** (skill 1–90) — bought as scrolls from merchants
- **Drop-only recipes** (skill 90+) — found in mob loot windows (5% normal, 30% boss)
- **Boss-only recipes** — drop exclusively from named/boss mobs
- Right-click recipe scrolls in inventory to learn them

**Crafting Stations:**
- Custom 3D stations spawned in Port Azure and Stowaway's Step
- Game objects (campfires, stoves, ovens, barrels, workbenches) also work as stations
- The game's existing Forge works for Smithing

**Craftable Equipment (153 pieces):**
- Smithing: Weapons and shields (ilvl 2–24)
- Fletching: Bows (ilvl 1–14)
- Jewelcraft: Rings, necklaces, headpieces, charms (ilvl 1–25)
- Tailoring: Cloth/leather armor, cloaks (ilvl 2–25)

**Craftable Consumables (55 items):**
- Baking: Food buffs scaling from +2 stats to +15 all stats
- Brewing: Drink buffs on the same curve
- Food/Drink use a category system — one Food buff and one Drink buff active at a time

**Bags:**
- Silken Pouch (4 slots, Tailoring 5)
- Herbalist's Satchel (6 slots, Tailoring 10)
- Simple Backpack (8 slots, Tailoring 25)
- Adventurer's Backpack (10 slots, Tailoring 50)

---

### Material Economy

Materials come from gameplay — no vendor shortcuts for valuable resources.

**Mining Nodes** drop bonus materials based on zone tier (60% chance per mine). Player level gating prevents low-level players from farming high-tier zones:

| Zone Tier | Zones | Req. Level | Common Ore | Rare Ore/Gem | Refined Metal | Endgame |
|---|---|---|---|---|---|---|
| T1 | Stowaway, Tutorial | 1 | Copper Ore | Coal, Smooth Pebble | — | — |
| T2 | Fernalla, Hidden, Brake | 5 | Copper/Iron Ore | Silver Ore, Rough Geode | — | — |
| T3 | Azure, Windwashed, Silkengrass | 12 | Iron Ore, Coal | Silver, Ruby, Emerald | Void Iron | — |
| T4 | Braxonian, Elderstone, Malaroth | 18 | Silver/Iron Ore | Gold, Sapphire, Bloodstone | Void Iron, Soulsteel | — |
| T5 | Soluna, Ripper, Bonepits, Vitheo | 25 | Adamantite, Platinum | Diamond, Opal, Starshard | Soulsteel, Moonsilver, Sunforged | Worldtree Splinter |
| T6 | Abyssal, Azynthi, PrielPlateau | 30 | Adamantite, Gold | Prismatic Jewel, Diamond | Starmetal, Moonsilver, Sunforged | Crystallized Time, Essence of Eternity |

**Monster Drops** (independent rolls, appear in loot window):

| Material | Monster Type | Min Level | Drop % |
|---|---|---|---|
| Beast Meat | Beasts (wolf, bear, boar, etc.) | Any | 25% |
| Worn Leather Scrap | Beasts | 1–17 | 20% |
| Tough Hide Strip | Beasts | 18+ | 20% |
| Spider Silk Strand | Spiders | 1–14 | 35% |
| Abyssal Silk Thread | Spiders | 15+ | 35% |
| Celestial Silk Bolt | Spiders | 25+ | ~7% |
| Beast Bone Shard | Wyrms, Skeletons | Any | 25–30% |
| Elder Wyrm Bone | Wyrms, Dragons | Any | 15–30% |
| Phoenix Feather | Fire creatures | 15+ | 20% |
| Breath of the Abyss | Void/Blight/Undead | 15+ | 20% |
| Malaroth Scale Fragment | Malaroths | Any | 35% |
| Frostweave Strand | Frost/Ice creatures | 15+ | 25% |
| Titan's Knucklebone | Giants, Golems | 20+ | 15% |

**Endgame Boss Drops** (level 25+ mobs):

| Material | Boss (20%) | Non-Boss (5%) |
|---|---|---|
| Thread of Fate | Bosses 30+ | — |
| Essence of Eternity | Bosses 30+ | Level 30+ mobs |
| Crystallized Time | Bosses 25+ | Level 25+ mobs |
| Worldtree Splinter | — | Level 25+ mobs |

**Fishing** (non-junk catches):
- Raw Fish — every catch (used in 9 Baking recipes)
- Moongill Trout — rare moongill catches
- Golden Carp — rare golden fish catches

**Vendor and Foraging** (basic crafting supplies):
- Herbs, wheat, water, berries, mushrooms, peppers
- Basic components (tangled string, enchanted root, faerie dust, etc.)
- No ores, gems, metals, monster parts, or endgame materials on vendors

---

### Chat Commands

| Command | Description |
|---|---|
| `/skills` | Overview of all skill levels |
| `/smith list`, `/bake list`, etc. | List known recipes |
| `/meditate` | Toggle meditation |

### Default Keybinds

| Key | Action |
|---|---|
| F8 | Open/Close Skills window |
| F9 | Forage |
| F10 | Bind Wound |
| M | Toggle Meditate |
| ; | Beg |

---

## Configuration

Config file: `BepInEx/config/com.erenshor.classicskills.cfg` (generated on first run)

Key settings:
- `SkillWindowKey` — Keybind to open Skills window (default: F8)
- `EnableCombatSkills` — Toggle combat weapon skills on/off
- `EnableMagicSkills` — Toggle magic school skills on/off
- `CombatSkillDamagePerLevel` — Damage bonus per combat skill level (default: 0.085)
- `EvocationDamagePerLevel` — Spell damage bonus per Evocation level (default: 0.08)
- `AbjurationDurationPerLevel` — Buff duration bonus per Abjuration level (default: 0.3)
- Individual toggles for Fishing, Swimming, Meditate, Foraging, etc.

---

## Credits

- **Mod by**: Sovietpremier(Thurderan)
- **Built with**: BepInEx 5, HarmonyX
- **For**: [Erenshor](https://store.steampowered.com/app/2382520/Erenshor/) by Burgee Media
