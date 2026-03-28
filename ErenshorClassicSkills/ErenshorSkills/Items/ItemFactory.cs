using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// ITEM FACTORY — Creates real, functional game items that appear in
    /// inventory with icons, can be equipped, consumed, or sold to vendors.
    ///
    /// Three-tier icon system:
    ///   1. REUSE GAME ICONS — Many custom items match existing game items
    ///      closely enough to share their icon (berries, mushrooms, ores,
    ///      rings, etc.). We search the ItemDatabase for visual matches.
    ///   2. EXTERNAL PNG PACK — Users can drop a folder of PNGs into
    ///      BepInEx/plugins/ClassicSkills/Icons/ named after items.
    ///      Supports community icon packs (e.g. the "Pixel Fantasy RPG
    ///      Icons" free pack from itch.io, or REXARD's Unity Asset Store
    ///      pack which has fishing, herbs, jewelry, armor, food icons).
    ///   3. PROCEDURAL FALLBACK — For items without a match or external
    ///      icon, generates a simple colored icon with a category symbol.
    ///
    /// Recommended free icon packs (user downloads separately):
    ///   - "Pixel Fantasy RPG Icons" on itch.io (free, 800+ icons)
    ///   - "8000+ Raven Fantasy Icons" on itch.io (free tier)
    ///   - "RPG inventory icons" on Unity Asset Store (free, 151 rated)
    ///   - "2D RPG Inventory Item Sprites" (99 sprites, fishing/herbs/food)
    ///
    /// All custom items are injected into Erenshor's ItemDatabase on game
    /// start via a Harmony postfix on ItemDatabase.Start().
    /// </summary>
    public static class ItemFactory
    {
        // All registered custom items, keyed by item name
        private static Dictionary<string, Item> _registeredItems
            = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

        // Icon cache
        private static Dictionary<string, Sprite> _iconCache
            = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // Consumable buff data: item name → (statType, statBonus, durationSec, category)
        // Category is "Food" or "Drink" — only one buff per category at a time
        public static Dictionary<string, (string stat, int bonus, float dur, string category)> ConsumableBuffs
            = new Dictionary<string, (string, int, float, string)>(StringComparer.OrdinalIgnoreCase);

        // Reference to the game's item database
        private static ItemDatabase _itemDb;

        // Path to external icon folder
        private static string IconFolder =>
            Path.Combine(Paths.PluginPath, "ClassicSkills", "Icons");

        /// <summary>Whether the factory has been initialized.</summary>
        public static bool Initialized { get; private set; }

        /// <summary>
        /// Scan the player's inventory and save all custom item names/counts
        /// into the skill save data. Called before save.
        /// </summary>
        public static void SaveCustomInventory()
        {
            if (!SkillsSaveManager.HasLoaded) return;
            var data = SkillsSaveManager.Data;
            data.CustomInventory.Clear();
            try
            {
                var inv = GameData.PlayerInv;
                if (inv == null) { SkillsPlugin.Log.LogWarning("SaveCustomInv: inv is null"); return; }

                // Count items from StoredSlots (UI slots with quantity)
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (inv.StoredSlots != null)
                {
                    foreach (var slot in inv.StoredSlots)
                    {
                        if (slot == null || slot.MyItem == null) continue;
                        string name = slot.MyItem.ItemName;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!_registeredItems.ContainsKey(name)) continue;
                        int qty = Mathf.Max(1, slot.Quantity);
                        if (!counts.ContainsKey(name)) counts[name] = 0;
                        counts[name] += qty;
                    }
                }

                // Fallback: also check StoredItems list
                if (counts.Count == 0 && inv.StoredItems != null)
                {
                    foreach (var item in inv.StoredItems)
                    {
                        if (item == null || string.IsNullOrEmpty(item.ItemName)) continue;
                        if (!_registeredItems.ContainsKey(item.ItemName)) continue;
                        if (!counts.ContainsKey(item.ItemName)) counts[item.ItemName] = 0;
                        counts[item.ItemName]++;
                    }
                }

                foreach (var kvp in counts)
                    data.CustomInventory.Add(new SavedCustomItem { Name = kvp.Key, Count = kvp.Value });

                SkillsPlugin.Log.LogInfo(
                    $"SaveCustomInv: found {counts.Count} custom item types, " +
                    $"{data.CustomInventory.Count} saved.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SaveCustomInventory error: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-inject saved custom items into the player's inventory.
        /// Called after character load, with a delay to ensure inventory is ready.
        /// </summary>
        public static void RestoreCustomInventory()
        {
            // Custom items are now restored by the game's native save/load system
            // via Patch_GetItemByID which resolves CS_ prefixed IDs.
            // We only need to ensure item instances exist in our _itemsById dictionary,
            // which is handled by Initialize(). No need to re-add to inventory.
            var data = SkillsSaveManager.Data;
            if (data.CustomInventory == null || data.CustomInventory.Count == 0) return;
            SkillsPlugin.Log.LogInfo(
                $"Custom inventory: {data.CustomInventory.Count} item types tracked (restored by game natively).");
        }

        // ═════════════════════════════════════════════════════════════
        // Initialization — called from Harmony patch on ItemDatabase
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Called after ItemDatabase.Start(). Registers all custom items
        /// into the game's item system.
        /// </summary>
        public static void Initialize(ItemDatabase itemDb)
        {
            _itemDb = itemDb;

            // Skip initialization if game DB isn't loaded yet (Awake runs before Start)
            if (_itemDb?.ItemDB == null || _itemDb.ItemDB.Length == 0)
            {
                SkillsPlugin.Log.LogInfo("ItemFactory: Skipping init — game DB not loaded yet.");
                return;
            }

            _registeredItems.Clear();
            _iconCache.Clear();
            _gameIconMap.Clear();
            _gameVisualsBySlot.Clear();

            // Ensure icon folder exists
            try
            {
                if (!Directory.Exists(IconFolder))
                    Directory.CreateDirectory(IconFolder);
            }
            catch { }

            // Load any external icon PNGs
            LoadExternalIcons();

            // Build icon map from existing game items
            BuildGameIconMap();

            // Register all custom items
            RegisterForagingItems();
            RegisterTradeskillItems();
            RegisterBeggingItems();

            Initialized = true;

            int extCount = 0;
            try { extCount = Directory.GetFiles(IconFolder, "*.png").Length; }
            catch { }

            SkillsPlugin.Log.LogInfo(
                $"ItemFactory: Registered {_registeredItems.Count} custom items. " +
                $"({_iconCache.Count} icons loaded" +
                (extCount > 0 ? $", {extCount} external PNGs" : "") + ")");        }

        // ═════════════════════════════════════════════════════════════
        // Public API — used by Foraging, Tradeskills, Begging
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Get a registered custom item by name. Returns null if not found.
        /// </summary>
        public static Item GetItem(string name)
        {
            _registeredItems.TryGetValue(name, out var item);
            return item;
        }

        /// <summary>
        /// Add a custom item to the player's inventory. Returns true if
        /// the item was found and added successfully.
        /// </summary>
        public static bool GiveItemToPlayer(string itemName)
        {
            try
            {
                // First check our custom items
                var item = GetItem(itemName);

                // If not custom, check the game's native database
                if (item == null && _itemDb != null)
                {
                    foreach (var gameItem in _itemDb.ItemDB)
                    {
                        if (gameItem != null && gameItem.ItemName == itemName)
                        {
                            item = gameItem;
                            break;
                        }
                    }
                }

                if (item == null) return false;

                var inventory = GameData.PlayerInv;
                if (inventory == null) return false;

                // Pass the SAME item instance (not a clone) for stackable items.
                // The game's AddItemToInv uses reference equality (==) for stacking.
                // It also calls UpdatePlayerInventory() internally.
                return inventory.AddItemToInv(item);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"ItemFactory.GiveItemToPlayer failed for '{itemName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create an equipment item with stats scaled to the player's level.
        /// Returns the created item, or null on failure.
        /// </summary>
        public static Item CreateScaledEquipment(string baseName, string slot,
            string statType, float statPerLevel, string description)
        {
            try
            {
                int playerLevel = GameData.PlayerStats?.Level ?? 1;
                int statValue = Mathf.Max(1,
                    Mathf.RoundToInt(playerLevel * statPerLevel));

                // Create the item
                var item = ScriptableObject.CreateInstance<Item>();
                item.ItemName = baseName;
                item.Lore = $"{description}\n+{statValue} {statType} (Level {playerLevel})";
                item.ItemValue = statValue * 3;
                item.ItemLevel = playerLevel;
                item.Stackable = false;
                item.Classes = new System.Collections.Generic.List<Class>();
                try
                {
                    int slotIdx = GetSlotIndex(slot);
                    var slotField = typeof(Item).GetField("RequiredSlot");
                    if (slotField != null)
                        slotField.SetValue(item, Enum.ToObject(slotField.FieldType, slotIdx));
                }
                catch { }
                item.ItemIcon = FindIcon(baseName, "equipment");

                // Set the stat bonus
                ApplyStatToItem(item, statType, statValue);

                return item;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"Failed to create scaled equipment '{baseName}': {ex.Message}");
                return null;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Item registration — builds all custom items
        // ═════════════════════════════════════════════════════════════

        private static void RegisterForagingItems()
        {
            // ── Junk ────────────────────────────────────────────────
            RegisterSimpleItem("Handful of Dirt", "Just... dirt.", 0, "junk");
            RegisterSimpleItem("Smooth Pebble", "A round, polished stone.", 0, "junk");
            RegisterSimpleItem("Broken Twig", "Snapped clean.", 0, "junk");
            RegisterSimpleItem("Rusty Nail", "Tetanus not included.", 1, "junk");
            RegisterSimpleItem("Cracked Button", "From someone's coat, long ago.", 0, "junk");
            RegisterSimpleItem("Tangled String", "Too short to be useful.", 0, "junk");
            RegisterSimpleItem("Worn Leather Scrap", "A fragment of something.", 1, "junk");
            RegisterSimpleItem("Crumpled Note", "The writing has faded.", 1, "junk");
            RegisterSimpleItem("Soggy Cloth Shoe", "Someone's loss.", 2, "junk");
            RegisterSimpleItem("Barnacle-Encrusted Coin", "Too corroded to spend.", 3, "junk");
            RegisterSimpleItem("Muck-Covered Boot", "The smell alone is a weapon.", 1, "junk");
            RegisterSimpleItem("Suspicious Bone", "Best not to think about it.", 2, "junk");
            RegisterSimpleItem("Bleached Skull Fragment", "The desert claims all.", 1, "junk");
            RegisterSimpleItem("Sand-Scored Buckle", "Pitted by endless winds.", 3, "junk");
            RegisterSimpleItem("Corrupted Shard", "Pulses with wrongness.", 2, "junk");
            RegisterSimpleItem("Withered Hand", "Rather not think about this.", 3, "junk");

            // ── Bait ────────────────────────────────────────────────
            RegisterSimpleItem("Fishing Grub", "Fish love these.", 1, "bait");
            RegisterSimpleItem("Earthworm", "The classic bait.", 1, "bait");
            RegisterSimpleItem("Cricket", "Still chirping.", 2, "bait");
            RegisterSimpleItem("Beetle Larva", "Irresistible to bottom-feeders.", 2, "bait");
            RegisterSimpleItem("Glowworm", "Attracts deep-water fish.", 4, "bait");
            RegisterSimpleItem("Nightcrawler", "Premium bait.", 5, "bait");
            RegisterSimpleItem("Enchanted Lure Bug", "Rare fish can't resist.", 10, "bait");
            RegisterSimpleItem("Golden Grub", "Attracts legendary catches.", 20, "bait");
            RegisterSimpleItem("Twilight Moth", "Coastal specialty bait.", 4, "bait");
            RegisterSimpleItem("Brine Shrimp Cluster", "Saltwater bait.", 5, "bait");
            RegisterSimpleItem("Bog Leech", "Swamp fish aren't picky.", 6, "bait");

            // ── Food (consumable with stat buffs) ───────────────────
            RegisterSimpleItem("Pod of Water", "Clean water.", 0, "food");
            RegisterConsumable("Wild Berries", "Tart and refreshing.", 2,
                "Endurance", 1, 120f);
            RegisterConsumable("Edible Root", "Bland but nutritious.", 1,
                "Endurance", 1, 120f);
            RegisterConsumable("Handful of Nuts", "Crunchy and filling.", 2,
                "Endurance", 2, 120f);
            RegisterConsumable("Fresh Mushroom", "Earthy flavor.", 3,
                "Wisdom", 1, 120f);
            RegisterConsumable("Stowaway's Rations", "Simple island fare.", 2,
                "Endurance", 1, 120f);
            RegisterConsumable("Faerie Apple", "Faintly glowing fruit.", 5,
                "Intelligence", 3, 300f);
            RegisterConsumable("Enchanted Honeycomb", "Fae-touched honey.", 12,
                "Wisdom", 4, 300f);
            RegisterConsumable("Dockside Oyster", "Briny and fresh.", 4,
                "Endurance", 2, 180f);
            RegisterConsumable("Sailor's Hardtack", "Rock-hard biscuit.", 3,
                "Endurance", 3, 300f);
            RegisterConsumable("Plains Wheat Cake", "Simple flatbread.", 5,
                "Strength", 3, 300f);
            RegisterConsumable("Revival Berry Mash", "Sweet and restorative.", 10,
                "Endurance", 4, 300f);
            RegisterConsumable("Desert Fig", "Dried by the sun.", 6,
                "Agility", 3, 300f);
            RegisterConsumable("Cactus Water Pouch", "Refreshing in the heat.", 10,
                "Endurance", 3, 300f, "Drink");
            RegisterConsumable("Meadow Honey Drops", "Crystallized honey.", 8,
                "Charisma", 3, 300f);
            RegisterConsumable("Silkengrass Tea Bundle", "Sharpens the mind.", 12,
                "Intelligence", 4, 300f, "Drink");
            RegisterConsumable("Firebloom Pepper", "Searingly hot.", 15,
                "Strength", 5, 600f);
            RegisterConsumable("Chargrilled Wyrm Egg", "Substantial boost.", 25,
                "Strength", 6, 600f);
            RegisterConsumable("Solunarian Fruit", "Tastes of moonlight.", 18,
                "Wisdom", 5, 600f);
            RegisterConsumable("Dawn Nectar", "Liquid gold.", 30,
                "Intelligence", 6, 600f, "Drink");
            RegisterConsumable("Garden Ambrosia", "Food of the Azynthi.", 25,
                "Wisdom", 6, 600f);
            RegisterConsumable("Essence of Eternity", "Shimmering droplet.", 50,
                "Intelligence", 8, 900f, "Drink");
            RegisterConsumable("Blightcap Mushroom", "Toxic but edible.", 12,
                "Endurance", 4, 300f);
            RegisterConsumable("Iron Ration Block", "Military-grade sustenance.", 10,
                "Endurance", 5, 300f);

            // ── Herbs (crafting materials with vendor value) ────────
            RegisterSimpleItem("Common Weed", "Basic herb.", 1, "herb");
            RegisterSimpleItem("Healing Moss", "Speeds wound recovery.", 3, "herb");
            RegisterSimpleItem("Aromatic Herb Bundle", "Fragrant herbs.", 5, "herb");
            RegisterSimpleItem("Faerie Dust", "Alchemists pay well.", 6, "herb");
            RegisterSimpleItem("Dewdrop Herb", "Collected at dawn.", 8, "herb");
            RegisterSimpleItem("Fae-Touched Blossom", "Rare fae flower.", 15, "herb");
            RegisterSimpleItem("Rockwort Leaves", "Mountain herb.", 5, "herb");
            RegisterSimpleItem("Vitheo's Blessing Herb", "Sacred herb.", 12, "herb");
            RegisterSimpleItem("Duskbloom Petal", "Blooms at twilight.", 7, "herb");
            RegisterSimpleItem("Tidecaller's Root", "Cultivated by tidecallers.", 14, "herb");
            RegisterSimpleItem("Ashbloom Herb", "Volcanic soil herb.", 18, "herb");
            RegisterSimpleItem("Firesage Bundle", "Dragon territory sage.", 30, "herb");
            RegisterSimpleItem("Moonvine Root", "Pulses with divine energy.", 20, "herb");
            RegisterSimpleItem("Solunarian Flower", "Touched by sun and moon.", 35, "herb");
            RegisterSimpleItem("Blightvein Moss", "Corrupted but usable.", 16, "herb");
            RegisterSimpleItem("Nightshade Berry", "Tainted by abyssal energy.", 28, "herb");
            RegisterSimpleItem("Shimmer Herb", "Shifts color constantly.", 30, "herb");
            RegisterSimpleItem("Essence of the Garden", "Distilled Azynthi magic.", 60, "herb");
            RegisterSimpleItem("Wild Herb", "Minor medicinal value.", 2, "herb");

            // ── Base Crafting Materials (used in early-mid recipes) ───
            // NOTE: Chunk of Copper Ore, Chunk of Iron Ore, and Coal are GAME items
            // (from the game's smithing system). Do NOT re-register them here.
            // They are already in the game's ItemDB and our recipes reference them by name.
            RegisterSimpleItem("Dark Bark Strip", "Tough bark stripped from darkwood trees.", 2, "wood");
            RegisterSimpleItem("Spider Silk Strand", "Strong, flexible silk from giant spiders.", 3, "crafting");
            RegisterSimpleItem("Bramble Thorn", "A sharp, curved thorn. Useful in fletching.", 1, "crafting");
            RegisterSimpleItem("Charred Root", "A root blackened by fire. Still sturdy.", 1, "wood");
            RegisterSimpleItem("Nesting Feather", "A large, stiff feather. Good for fletching.", 3, "crafting");
            RegisterSimpleItem("Enchanted Root", "A root pulsing with faint magic.", 5, "wood");
            RegisterSimpleItem("Swamp Root", "Pungent root from the bogs. Used in brewing.", 2, "herb");
            RegisterSimpleItem("Runestone Shard", "A fragment of carved runestone.", 6, "ore");
            RegisterSimpleItem("Windwashed Crystal", "Clear crystal polished by wind.", 5, "gem");
            RegisterSimpleItem("Obsidian Shard", "Volcanic glass, razor sharp.", 4, "ore");
            RegisterSimpleItem("Malaroth Scale Fragment", "A scale from the dragons of Malaroth.", 8, "exotic");
            RegisterSimpleItem("Moonstone Pebble", "A small, luminous stone.", 4, "gem");
            RegisterSimpleItem("Rough Geode", "Uncracked geode. Crystals inside.", 3, "gem");
            RegisterSimpleItem("Honey Comb Fragment", "Waxy comb dripping with honey.", 2, "food");
            RegisterSimpleItem("Desert Cactus Fruit", "Prickly but sweet.", 3, "food");
            RegisterSimpleItem("Silken Grass Blade", "A blade of silken grass, smooth as cloth.", 3, "herb");
            RegisterSimpleItem("Dawn Blossom", "A flower that blooms at first light.", 5, "herb");
            RegisterSimpleItem("Bloodstone Chip", "Deep red stone chip.", 6, "gem");
            RegisterSimpleItem("Brine-Crusted Pearl", "Pearl encrusted with sea salt.", 8, "gem");
            RegisterSimpleItem("Golden Pollen", "Shimmering pollen from rare flowers.", 10, "herb");
            RegisterSimpleItem("Corruption Crystal", "Crystal tainted by dark energy.", 8, "gem");
            RegisterSimpleItem("Azynthi Petal", "A petal from the Azynthi gardens.", 15, "herb");
            RegisterSimpleItem("Glimmering Geode Fragment", "Half a geode, crystals sparkling.", 10, "gem");

            // ── Tier 3-7 Crafting Materials (ores, gems, hides, wood, exotic) ──
            // Ores and metals
            RegisterSimpleItem("Chunk of Silver Ore", "Gleaming silver ore.", 8, "ore");
            RegisterSimpleItem("Chunk of Gold Ore", "Precious gold ore.", 15, "ore");
            RegisterSimpleItem("Chunk of Platinum Ore", "Extremely rare platinum.", 25, "ore");
            RegisterSimpleItem("Chunk of Adamantite Ore", "Nearly indestructible metal.", 40, "ore");
            RegisterSimpleItem("Starmetal Fragment", "Fallen from the heavens.", 60, "ore");
            RegisterSimpleItem("Void Iron Chunk", "Metal touched by the void.", 80, "ore");
            RegisterSimpleItem("Sunforged Ingot", "Forged in solar fire.", 70, "ore");
            RegisterSimpleItem("Moonsilver Bar", "Liquid silver solidified by moonlight.", 65, "ore");
            RegisterSimpleItem("Soulsteel Billet", "Resonates with spiritual energy.", 90, "ore");
            // Gems
            RegisterSimpleItem("Rough Ruby", "Uncut ruby.", 12, "gem");
            RegisterSimpleItem("Rough Sapphire", "Uncut sapphire.", 12, "gem");
            RegisterSimpleItem("Rough Emerald", "Uncut emerald.", 12, "gem");
            RegisterSimpleItem("Flawless Diamond", "A perfect stone.", 50, "gem");
            RegisterSimpleItem("Abyssal Opal", "Swirls with darkness.", 35, "gem");
            RegisterSimpleItem("Starshard Crystal", "Pulses with starlight.", 60, "gem");
            RegisterSimpleItem("Prismatic Jewel", "Refracts all light.", 80, "gem");
            // Hides and cloth
            RegisterSimpleItem("Cured Leather Hide", "Properly treated leather.", 10, "hide");
            RegisterSimpleItem("Tough Hide Strip", "Tough dragon leather.", 25, "hide");
            RegisterSimpleItem("Abyssal Silk Thread", "Spun from void essence.", 40, "hide");
            RegisterSimpleItem("Celestial Fabric Bolt", "Woven from starlight.", 60, "hide");
            RegisterSimpleItem("Ethereal Gossamer", "Nearly invisible thread.", 80, "hide");
            // Wood and bone
            RegisterSimpleItem("Ironbark Plank", "Dense as iron.", 15, "wood");
            RegisterSimpleItem("Petrified Heartwood", "Ancient and unyielding.", 30, "wood");
            RegisterSimpleItem("Voidtouched Timber", "Warped by dark energy.", 45, "wood");
            RegisterSimpleItem("Worldtree Splinter", "From the tree of ages.", 70, "wood");
            RegisterSimpleItem("Beast Bone Shard", "From a lesser drake.", 20, "bone");
            RegisterSimpleItem("Elder Wyrm Bone", "Ancient dragon bone.", 50, "bone");
            RegisterSimpleItem("Titan's Knucklebone", "Massive and dense.", 75, "bone");
            // Exotic materials
            RegisterSimpleItem("Phoenix Feather", "Burns but never consumed.", 50, "exotic");
            RegisterSimpleItem("Kraken Ink Vial", "Deep-sea ink.", 35, "exotic");
            RegisterSimpleItem("Frostweave Strand", "Cold to the touch.", 40, "exotic");
            RegisterSimpleItem("Essence of Chaos", "Unstable elemental force.", 55, "exotic");
            RegisterSimpleItem("Crystallized Time", "A moment frozen forever.", 100, "exotic");
            RegisterSimpleItem("Breath of the Abyss", "Condensed void energy.", 75, "exotic");
            RegisterSimpleItem("Thread of Fate", "Spun by the cosmos.", 90, "exotic");
            RegisterSimpleItem("Golden Wheat", "Cultivated grain.", 3, "food");
            // Fish and meat for cooking
            RegisterSimpleItem("Raw Fish", "A fresh catch. Cook it for better results.", 4, "food");
            RegisterSimpleItem("Moongill Trout", "A rare, luminescent fish. Prized by cooks.", 20, "food");
            RegisterSimpleItem("Golden Carp", "A shimmering golden fish. Extremely rare.", 40, "food");
            RegisterSimpleItem("Beast Meat", "Raw meat from a slain beast. Best cooked.", 5, "food");
            RegisterSimpleItem("Eagle Feather", "Sharp fletching feather.", 4, "crafting");

            SkillsPlugin.Log.LogInfo(
                $"ItemFactory: Registered {_registeredItems.Count} foraging items.");
        }

        private static void RegisterTradeskillItems()
        {
            int countBefore = _registeredItems.Count;

            // ── Smithing results ────────────────────────────────────
            RegisterSimpleItem("Copper Rivets", "Basic fasteners for crafting.", 3, "crafting");
            RegisterSimpleItem("Iron Arrowheads", "Used in Fletching.", 5, "crafting");
            RegisterSimpleItem("Steel Chain Links", "Used in Tailoring.", 10, "crafting");
            RegisterSimpleItem("Reinforced Buckle", "Sturdy buckle.", 12, "crafting");
            RegisterSimpleItem("Tempered Blade Blank", "Unfinished blade.", 25, "crafting");
            RegisterSimpleItem("Mithril Wire", "Fine wire for Jewelcraft.", 30, "crafting");
            RegisterEquipment("Forged Shield Boss", "Charm",
                "Endurance", 7, 45, "+8 AC, +18 HP, +7 End.");
            if (_registeredItems.TryGetValue("Forged Shield Boss", out var forged)) { forged.AC = 8; forged.HP = 18; }
            RegisterSimpleItem("Hardened Alloy Ingot", "Premium smithing material.", 80, "crafting");
            // New tier 3-7 smithing products
            RegisterSimpleItem("Silver Wire", "Fine silver wire for jewelry.", 15, "crafting");
            RegisterSimpleItem("Adamantite Rivets", "Unbreakable fasteners.", 35, "crafting");
            RegisterSimpleItem("Gold Setting", "Ornate gold framework.", 30, "crafting");
            RegisterSimpleItem("Adamantite Plate", "Heavy armor plate.", 50, "crafting");
            RegisterSimpleItem("Platinum Filigree", "Delicate platinum work.", 45, "crafting");
            RegisterSimpleItem("Soulsteel Chain Links", "Spirit-infused links.", 80, "crafting");
            RegisterSimpleItem("Starmetal Blade Blank", "Celestial metal blank.", 70, "crafting");
            RegisterEquipment("Moonforged Hatchet", "Primary", "Strength", 8, 120, "Forged under moonlight, the blade holds a faint silver gleam.");
            RegisterEquipment("Sunforged Greatsword", "Primary", "Strength", 10, 200, "The blade radiates warmth. Enemies flinch before the first swing.");
            RegisterEquipment("Voidsteel Maul", "Primary", "Strength", 12, 280, "Forged from ore pulled from the void. The weight feels alive.");
            RegisterEquipment("Titan's Bulwark", "Charm",
                "Endurance", 24, 350, "An immovable wall of metal. Those behind it sleep soundly.");
            if (_registeredItems.TryGetValue("Titan's Bulwark", out var titans)) { titans.AC = 36; titans.HP = 112; }
            RegisterEquipment("Eternity's Edge", "Primary", "Strength", 14, 500, "The edge never dulls. Some say it cuts the moments between heartbeats.");

            // ── Baking results ──────────────────────────────────────
            RegisterConsumable("Trail Rations", "Simple food.", 2,
                "Endurance", 2, 180f);
            RegisterConsumable("Herb Bread", "Fragrant bread.", 6,
                "Wisdom", 2, 300f);
            RegisterConsumable("Mushroom Stew", "Hearty stew.", 10,
                "Endurance", 3, 300f);
            RegisterConsumable("Honeycake", "Sweet and energizing.", 14,
                "Charisma", 4, 300f);
            RegisterConsumable("Peppered Trail Jerky", "Fiery meat.", 22,
                "Strength", 5, 600f);
            RegisterConsumable("Solunarian Feast", "Celestial banquet.", 50,
                "Wisdom", 5, 600f);
            RegisterConsumable("Grand Ambrosia", "Divine food.", 100,
                "Intelligence", 8, 900f);
            // New tier 3-7 baking products
            RegisterConsumable("Ironbark Smoked Meat", "Hearty smoked meat.", 25, "Endurance", 6, 600f);
            RegisterConsumable("Frostberry Tart", "Chilled dessert.", 35, "Intelligence", 7, 600f);
            RegisterConsumable("Drake Bone Broth", "Fortifying broth.", 50, "Endurance", 8, 900f);
            RegisterConsumable("Phoenix-Spiced Kebabs", "Fiery meat.", 75, "Strength", 10, 900f);
            RegisterConsumable("Moonlight Risotto", "Silvery rice dish.", 100, "Wisdom", 8, 900f);
            RegisterConsumable("Titan's Feast Platter", "Enormous meal.", 150, "Endurance", 10, 900f);
            RegisterConsumable("Void-Touched Confection", "Unsettling but powerful.", 220, "Intelligence", 12, 1200f);
            RegisterConsumable("Eternal Banquet", "Time-frozen feast.", 350, "Wisdom", 14, 1200f);
            RegisterConsumable("Cosmos Cake", "Tastes of infinity.", 500, "Intelligence", 15, 1500f);

            // ── Brewing results ─────────────────────────────────────
            RegisterConsumable("Bog Juice", "Murky but drinkable.", 2,
                "Intelligence", 1, 180f, "Drink");
            RegisterConsumable("Faerie Wine", "Shimmering drink.", 8,
                "Intelligence", 3, 300f, "Drink");
            RegisterConsumable("Duskbloom Tea", "Calming tea.", 12,
                "Wisdom", 4, 300f, "Drink");
            RegisterConsumable("Braxonian Cactus Ale", "Potent desert brew.", 18,
                "Endurance", 4, 300f, "Drink");
            RegisterConsumable("Blightvein Elixir", "Dark potion.", 35,
                "Intelligence", 6, 600f, "Drink");
            RegisterConsumable("Firesage Draught", "Volcanic brew.", 60,
                "Strength", 8, 600f, "Drink");
            RegisterConsumable("Elixir of Vitality", "Legendary elixir.", 120,
                "Intelligence", 10, 900f, "Drink");
            // New tier 3-7 brewing products
            RegisterConsumable("Silver Moon Tonic", "Shimmering tonic.", 30, "Wisdom", 7, 600f, "Drink");
            RegisterConsumable("Drake Blood Ale", "Crimson ale.", 50, "Strength", 8, 900f, "Drink");
            RegisterConsumable("Frostfire Flask", "Burns cold.", 75, "Endurance", 10, 900f, "Drink");
            RegisterConsumable("Voidwalker's Draught", "Dark potion.", 100, "Intelligence", 10, 900f, "Drink");
            RegisterConsumable("Phoenix Tears", "Liquid fire.", 150, "Endurance", 12, 1200f, "Drink");
            RegisterConsumable("Essence of Starmetal", "Cosmic elixir.", 200, "Wisdom", 12, 900f, "Drink");
            RegisterConsumable("Titan's Blood Mead", "Enormous power.", 280, "Strength", 15, 1200f, "Drink");
            RegisterConsumable("Draught of Eternity", "Time stands still.", 400, "Intelligence", 14, 1500f, "Drink");
            RegisterConsumable("Elixir of the Cosmos", "Universe in a bottle.", 500, "Wisdom", 16, 1500f, "Drink");

            // ── Fletching results ───────────────────────────────────
            RegisterSimpleItem("Rough Arrows (20)", "Basic arrows.", 4, "ammo");
            RegisterSimpleItem("Iron-Tipped Arrows (20)", "+5% ranged damage.", 10, "ammo");
            RegisterSimpleItem("Windwashed Arrows (20)", "+10% ranged damage.", 25, "ammo");
            RegisterSimpleItem("Bonesteel Arrows (20)", "+20% ranged damage.", 70, "ammo");
            RegisterEquipment("Crude Shortbow", "Primary",
                "Dexterity", 1, 8, "Bent wood and rough string. It fires. Mostly.");
            RegisterEquipment("Strung Hunting Bow", "Primary",
                "Dexterity", 2, 15, "Properly strung with decent pull. A real step up.");
            RegisterEquipment("Hunting Shortbow", "Primary",
                "Dexterity", 3, 20, "A short hunting bow, compact enough for the brush.");
            RegisterEquipment("Composite Longbow", "Primary",
                "Strength", 5, 50, "Layers of sinew and horn give it power beyond its size.");
            RegisterEquipment("Azynthi Recurve", "Primary",
                "Dexterity", 7, 130, "Carved from a living Azynthi branch. It hums when drawn.");
            // New tier 3-7 fletching products
            RegisterSimpleItem("Ironbark Shaft", "Strong arrow shaft.", 20, "crafting");
            RegisterSimpleItem("Silver-Tipped Arrows (20)", "+15% ranged damage.", 30, "ammo");
            RegisterSimpleItem("Petrified Bow Stave", "Ancient bow stave.", 40, "crafting");
            RegisterEquipment("Wyrm Bone Bow", "Primary", "Strength", 8, 80, "Built from the ribcage of a wyrm. Arrows fly straight and true.");
            RegisterSimpleItem("Voidtipped Arrows (20)", "+25% ranged damage.", 60, "ammo");
            RegisterSimpleItem("Worldtree Bow Limb", "Legendary bow wood.", 80, "crafting");
            RegisterSimpleItem("Starmetal Arrowheads", "Celestial arrowheads.", 70, "crafting");
            RegisterSimpleItem("Starfire Arrows (20)", "+35% ranged damage.", 120, "ammo");
            RegisterEquipment("Titan's Longbow", "Primary", "Dexterity", 12, 250, "It takes two hands and considerable will to draw this string.");
            RegisterEquipment("Bow of the Cosmos", "Primary", "Dexterity", 14, 450, "Arrows loosed from this bow arrive before the string finishes vibrating.");
            RegisterSimpleItem("Eternity's Volley (20)", "+45% ranged damage.", 500, "ammo");

            // ── Jewelcraft results ──────────────────────────────────
            RegisterEquipment("Polished Stone Band", "Ring",
                "Wisdom", 2, 4, "A river stone, polished smooth and set in copper wire.");
            if (_registeredItems.TryGetValue("Polished Stone Band", out var polish)) { polish.HP = 3; }
            RegisterEquipment("Amber Pendant", "Neck",
                "Intelligence", 3, 12, "Ancient sap hardened to gold. Something small is trapped within.");
            if (_registeredItems.TryGetValue("Amber Pendant", out var amberp)) { amberp.HP = 6; }
            RegisterEquipment("Moonstone Ring", "Ring",
                "Wisdom", 5, 25, "The stone glows faintly when the wearer speaks truth.");
            if (_registeredItems.TryGetValue("Moonstone Ring", out var moonst)) { moonst.HP = 9; }
            RegisterEquipment("Brine Pearl Earring", "Ring",
                "Wisdom", 6, 35, "Harvested from the salt marshes. Smells faintly of the sea.");
            if (_registeredItems.TryGetValue("Brine Pearl Earring", out var brinep)) { brinep.HP = 12; }
            RegisterEquipment("Bloodstone Signet", "Ring",
                "Wisdom", 7, 55, "The dark red stone pulses warmly against the skin.");
            if (_registeredItems.TryGetValue("Bloodstone Signet", out var bloods)) { bloods.AC = 1; bloods.HP = 13; }
            RegisterEquipment("Moonvine Choker", "Neck",
                "Intelligence", 8, 80, "Woven from moonvine tendrils that tighten gently when magic is near.");
            if (_registeredItems.TryGetValue("Moonvine Choker", out var moonvi)) { moonvi.AC = 2; moonvi.HP = 17; }
            RegisterEquipment("Shimmerweave Crown", "Head",
                "Endurance", 9, 150, "Light as air but strong as wire. The gems shift color with the wearer's mood.");
            if (_registeredItems.TryGetValue("Shimmerweave Crown", out var shimme)) { shimme.AC = 9; shimme.HP = 34; }
            // New tier 3-7 jewelcraft products
            RegisterEquipment("Ruby Studded Band", "Ring",
                "Wisdom", 10, 40, "Three rubies set in silver. Each one warm to the touch.");
            if (_registeredItems.TryGetValue("Ruby Studded Band", out var rubyst)) { rubyst.AC = 1; rubyst.HP = 19; }
            RegisterEquipment("Sapphire Pendant", "Neck",
                "Intelligence", 13, 55, "Blue as deep water. The chain is cold no matter how long it's worn.");
            if (_registeredItems.TryGetValue("Sapphire Pendant", out var sapphi)) { sapphi.AC = 3; sapphi.HP = 37; }
            RegisterEquipment("Emerald Focus Stone", "Charm",
                "Endurance", 14, 70, "Carved from a single emerald. Thoughts sharpen when held.");
            if (_registeredItems.TryGetValue("Emerald Focus Stone", out var emeral)) { emeral.AC = 17; emeral.HP = 45; }
            RegisterEquipment("Diamond Encrusted Circlet", "Head",
                "Endurance", 15, 100, "Every diamond is flawless. The goldwork between them is not.");
            if (_registeredItems.TryGetValue("Diamond Encrusted Circlet", out var diamon)) { diamon.AC = 16; diamon.HP = 81; }
            RegisterEquipment("Abyssal Opal Talisman", "Charm",
                "Endurance", 18, 130, "The opal swirls with colors that don't exist in nature.");
            if (_registeredItems.TryGetValue("Abyssal Opal Talisman", out var abyssa)) { abyssa.AC = 22; abyssa.HP = 64; }
            RegisterEquipment("Starshard Earring", "Ring",
                "Wisdom", 20, 170, "A sliver of fallen star, still faintly warm after all these years.");
            if (_registeredItems.TryGetValue("Starshard Earring", out var starsh)) { starsh.AC = 3; starsh.HP = 64; }
            RegisterEquipment("Phoenix Heart Amulet", "Neck",
                "Intelligence", 22, 220, "The ember at its center has never gone out. It never will.");
            if (_registeredItems.TryGetValue("Phoenix Heart Amulet", out var phoeni)) { phoeni.AC = 6; phoeni.HP = 84; }
            RegisterEquipment("Titan's Signet Ring", "Ring",
                "Wisdom", 24, 300, "Sized for a finger as wide as a man's wrist. Resized, it kept the weight.");
            if (_registeredItems.TryGetValue("Titan's Signet Ring", out var titansr)) { titansr.AC = 4; titansr.HP = 84; }
            RegisterEquipment("Crown of the Void", "Head",
                "Endurance", 25, 400, "Stare into the gems long enough and they stare back.");
            if (_registeredItems.TryGetValue("Crown of the Void", out var crowno)) { crowno.AC = 36; crowno.HP = 201; }
            RegisterEquipment("Amulet of Eternity", "Neck",
                "Intelligence", 25, 500, "Time passes around it, never through it. The wearer feels ageless.");
            if (_registeredItems.TryGetValue("Amulet of Eternity", out var amulet)) { amulet.AC = 8; amulet.HP = 114; }

            // ── Tailoring results ───────────────────────────────────
            RegisterSimpleItem("Patchwork Bandages", "Improves Bind Wound.", 3, "crafting");
            // Smithing starter gear — slightly better than game's starter weapons
            // Smithing starter gear — better than game starters
            // Game: Rusty Dagger=4dmg/1.0dly, Rusty Shortsword=5dmg/1.25dly
            RegisterWeapon("Crude Iron Dagger", "Primary", 3, // OneHandDagger
                5, 1.0f, "Strength", 2, 8, "A crude but effective blade, hammered from raw iron.");
            RegisterWeapon("Hammered Iron Shortsword", "Primary", 1, // OneHandMelee
                5, 1.3f, "Strength", 2, 10, "Forged flat on an anvil and given a rough edge.");
            RegisterEquipment("Banded Iron Helm", "Head",
                "Endurance", 3, 12, "Iron bands riveted over a leather frame. Keeps the skull intact.");
            if (_registeredItems.TryGetValue("Banded Iron Helm", out var bih)) { bih.AC = 2; bih.HP = 9; }
            // Tailoring starter gear — better than game starters
            // Game: Cloth Gloves=1AC, Leather Gloves=2AC, Cloth Hood=2AC
            RegisterEquipment("Stitched Leather Gloves", "Hand",
                "Dexterity", 2, 8, "Double-stitched leather protects the knuckles without sacrificing grip.");
            if (_registeredItems.TryGetValue("Stitched Leather Gloves", out var slg)) { slg.AC = 2; slg.HP = 3; }
            RegisterEquipment("Padded Cloth Cap", "Head",
                "Endurance", 3, 10, "Padded layers of cloth offer modest protection from blows to the head.");
            if (_registeredItems.TryGetValue("Padded Cloth Cap", out var pcc)) { pcc.AC = 2; pcc.HP = 9; }
            RegisterSimpleItem("Silken Pouch",
                "Right-click to open. 4 item slots.", 8, "crafting");
            RegisterSimpleItem("Herbalist's Satchel",
                "Right-click to open. 6 item slots.", 18, "crafting");
            RegisterSimpleItem("Simple Backpack",
                "Right-click to open. 8 item slots.", 35, "crafting");
            RegisterSimpleItem("Adventurer's Backpack",
                "Right-click to open. 10 item slots. Sturdy and spacious.", 80, "crafting");
            // Make bags right-clickable via dummy spell
            SetupBagItem("Silken Pouch");
            SetupBagItem("Herbalist's Satchel");
            SetupBagItem("Simple Backpack");
            SetupBagItem("Adventurer's Backpack");
            RegisterEquipment("Padded Leather Vest", "Chest",
                "Endurance", 5, 25, "A vest of padded leather, worn and serviceable.");
            if (_registeredItems.TryGetValue("Padded Leather Vest", out var padded)) { padded.AC = 12; padded.HP = 30; }
            RegisterEquipment("Meadow Silk Robe", "Chest",
                "Endurance", 6, 40, "Woven from the silk of meadow spiders, faintly luminous in moonlight.");
            if (_registeredItems.TryGetValue("Meadow Silk Robe", out var meadow)) { meadow.AC = 18; meadow.HP = 43; }
            RegisterEquipment("Blighthide Cloak", "Back",
                "Agility", 7, 60, "Cured in blightvein oils. The leather repels toxins and shadow alike.");
            if (_registeredItems.TryGetValue("Blighthide Cloak", out var blight)) { blight.AC = 6; blight.HP = 23; }
            RegisterEquipment("Azynthi Woven Mantle", "Back",
                "Agility", 9, 130, "Living fibers from the Azynthi groves, woven by careful hands.");
            if (_registeredItems.TryGetValue("Azynthi Woven Mantle", out var azynth)) { azynth.AC = 8; azynth.HP = 28; }
            // New tier 3-7 tailoring products
            RegisterSimpleItem("Cured Tough Leather", "Processed dragon hide.", 35, "crafting");
            RegisterEquipment("Ironbark Reinforced Vest", "Chest",
                "Endurance", 12, 50, "Reinforced with strips of ironbark over hardened leather.");
            if (_registeredItems.TryGetValue("Ironbark Reinforced Vest", out var ironba)) { ironba.AC = 40; ironba.HP = 100; }
            RegisterSimpleItem("Abyssal Weave Cloth", "Dark magic fabric.", 50, "crafting");
            RegisterEquipment("Frostweave Cloak", "Back",
                "Agility", 15, 80, "Threads of frostweave shimmer with trapped winter cold.");
            if (_registeredItems.TryGetValue("Frostweave Cloak", out var frostw)) { frostw.AC = 14; frostw.HP = 67; }
            RegisterSimpleItem("Celestial Silk Bolt", "Heavenly fabric.", 70, "crafting");
            RegisterEquipment("Dragonscale Brigandine", "Chest",
                "Endurance", 20, 180, "Overlapping scales from a fallen drake, each one a shield in miniature.");
            if (_registeredItems.TryGetValue("Dragonscale Brigandine", out var dragon)) { dragon.AC = 73; dragon.HP = 213; }
            RegisterEquipment("Voidweave Robe", "Chest",
                "Endurance", 22, 220, "Woven from threads that drink the light. Whispers echo in its folds.");
            if (_registeredItems.TryGetValue("Voidweave Robe", out var voidwe)) { voidwe.AC = 80; voidwe.HP = 240; }
            RegisterEquipment("Celestial Battle Mantle", "Back",
                "Agility", 24, 300, "A mantle of celestial silk that shimmers like a clear night sky.");
            if (_registeredItems.TryGetValue("Celestial Battle Mantle", out var celest)) { celest.AC = 27; celest.HP = 140; }
            RegisterEquipment("Titan's War Harness", "Chest",
                "Endurance", 25, 400, "A titan's battle gear, sized for mortal shoulders. The weight is immense.");
            if (_registeredItems.TryGetValue("Titan's War Harness", out var titanwh)) { titanwh.AC = 100; titanwh.HP = 320; }
            RegisterEquipment("Mantle of Eternity", "Back",
                "Agility", 25, 500, "Neither thread nor fiber has aged since the day it was woven. Perhaps it never will.");
            if (_registeredItems.TryGetValue("Mantle of Eternity", out var mantle)) { mantle.AC = 33; mantle.HP = 175; }

            // ── Drop-only recipe products ──────────────────────
            RegisterEquipment("Rusted Heirloom Blade", "Primary", "Strength", 2, 8, "An ancient family weapon. The rust is mostly cosmetic.");
            RegisterEquipment("Goblin-Forged Shiv", "Primary", "Dexterity", 2, 15, "Crude but wickedly sharp. Goblins know where to stick things.");
            RegisterEquipment("Burnished War Pick", "Primary", "Strength", 2, 22, "Equally suited for mining and splitting skulls.");
            RegisterEquipment("Blighted Iron Mace", "Primary", "Strength", 3, 30, "The iron weeps a dark residue. Best not to lick it.");
            RegisterEquipment("Fernallan War Axe", "Primary", "Strength", 3, 38, "Blessed by druids. The handle sprouts buds in spring.");
            RegisterEquipment("Tempered Partisan", "Primary", "Strength", 4, 45, "Long reach and a wicked point. Keeps enemies at arm's length.");
            RegisterEquipment("Braxonian Flamberge", "Primary", "Strength", 4, 55, "The wavy blade parts armor like water.");
            RegisterEquipment("Boneweave Shield", "Secondary", "Endurance", 5, 65, "Woven from the bones of fallen beasts. Lighter than it looks.");
            RegisterEquipment("Ghostmetal Rapier", "Primary", "Strength", 5, 75, "The blade seems to flicker in and out of existence.");
            RegisterEquipment("Warden's Maul", "Primary", "Strength", 5, 85, "Built to settle arguments. Usually does.");
            RegisterEquipment("Emberthorn Scimitar", "Primary", "Strength", 6, 95, "Thorns of ember line the guard. Handle with caution.");
            RegisterEquipment("Nightfall Greatsword", "Primary", "Strength", 6, 110, "Daylight dims where this blade swings.");
            RegisterEquipment("Runeforged Hammer", "Primary", "Strength", 7, 120, "Runes crawl along the head. They rearrange during battle.");
            RegisterEquipment("Frostbite Battleaxe", "Primary", "Strength", 7, 135, "The edge is rimmed with permanent frost. Cuts burn cold.");
            RegisterEquipment("Wyrmbone Defender", "Secondary", "Endurance", 8, 155, "A shield carved from a single wyrm vertebra.");
            RegisterEquipment("Searing Iron Dirk", "Primary", "Dexterity", 6, 145, "Always warm to the touch. Wounds cauterize on contact.");
            RegisterEquipment("Stonecrusher Flail", "Primary", "Strength", 8, 170, "The head was a boulder once. Now it's a warning.");
            RegisterEquipment("Eclipse Longsword", "Primary", "Strength", 9, 190, "Forged during an eclipse. The shadow never left the steel.");
            RegisterEquipment("Thunderforge Warhammer", "Primary", "Strength", 9, 210, "Lightning crackles between the runes when swung.");
            RegisterEquipment("Blightfang Dagger", "Primary", "Dexterity", 8, 225, "The venom glands are built into the hilt. Squeeze gently.");
            RegisterEquipment("Worldsplitter Axe", "Primary", "Strength", 10, 250, "Said to have cracked a mountainside. The mountain disagrees.");
            RegisterEquipment("Dawnshard Falchion", "Primary", "Strength", 10, 270, "Catches the first light of morning and holds it until dusk.");
            RegisterEquipment("Abyssal Cleaver", "Primary", "Strength", 10, 295, "Pulled from a rift. Whatever held it before had larger hands.");
            RegisterEquipment("Starforged Bastard Sword", "Primary", "Strength", 11, 320, "Metal that fell from the sky, shaped by mortal ambition.");
            RegisterEquipment("Dragonlord's Lance", "Primary", "Strength", 11, 350, "The shaft is scorched but unburnt. It remembers its rider.");
            RegisterEquipment("Void Reaver's Khopesh", "Primary", "Strength", 11, 380, "The curve follows no geometry found in nature.");
            RegisterEquipment("Phoenix-Forged Claymore", "Primary", "Strength", 11, 410, "Reforged in phoenix fire seven times. It won't break.");
            RegisterEquipment("Moonbane Kukri", "Primary", "Dexterity", 9, 430, "Silver that drinks moonlight. Werewolves avoid its wielder.");
            RegisterEquipment("Warforged Sentinel Shield", "Secondary", "Endurance", 11, 460, "Dented from a hundred battles. Each dent saved a life.");
            RegisterEquipment("Ironheart Champion's Blade", "Primary", "Strength", 11, 480, "Won in a duel. The loser's name is etched near the pommel.");
            RegisterSimpleItem("Campfire Ash Cake", "Simple flatbread. +2 End for 5 min.", 5, "consumable");
            RegisterSimpleItem("Honeyed Fig Rolls", "Sweet rolls. +3 Cha, +2 Wis for 5 min.", 12, "consumable");
            RegisterSimpleItem("Goblin Pepper Steak", "Spicy meat. +4 Str, +3 Fire Resist for 8 min.", 20, "consumable");
            RegisterSimpleItem("Fernallan Herb Pie", "Savory pie. +5 Wis, +3 Int for 8 min.", 28, "consumable");
            RegisterSimpleItem("Braxonian Cactus Bread", "Dense bread. +5 End, +4 Str for 10 min.", 35, "consumable");
            RegisterSimpleItem("Firebloom Dumpling", "Fiery food. +6 Str, +6 Fire Resist for 10 min.", 42, "consumable");
            RegisterSimpleItem("Moonpetal Scones", "Delicate pastry. +7 Wis, +5 Int for 10 min.", 50, "consumable");
            RegisterSimpleItem("Blightvein Mushroom Soup", "Dark soup. +7 Int, +5 Poison Resist for 12 min.", 60, "consumable");
            RegisterSimpleItem("Smoked Beast Jerky", "Tough meat. +8 End, +6 Str for 12 min.", 68, "consumable");
            RegisterSimpleItem("Solunarian Fruit Tart", "Sweet tart. +7 all stats for 10 min.", 80, "consumable");
            RegisterSimpleItem("Phoenix Pepper Stew", "Burning stew. +9 Str, +10 Fire Resist for 12 min.", 90, "consumable");
            RegisterSimpleItem("Abyssal Mushroom Risotto", "Dark rice dish. +10 Int, +8 all Resists for 12 min.", 105, "consumable");
            RegisterSimpleItem("Titan Root Casserole", "Heavy meal. +10 End, +8 Str for 15 min.", 115, "consumable");
            RegisterSimpleItem("Celestial Honey Cakes", "Heavenly cakes. +8 all stats for 12 min.", 130, "consumable");
            RegisterSimpleItem("Frostfire Goulash", "Hot-cold stew. +10 Fire/Cold Resist, +6 End for 15 min.", 145, "consumable");
            RegisterSimpleItem("Starmetal-Seared Steak", "Cosmic meat. +10 Str, +8 End for 15 min.", 155, "consumable");
            RegisterSimpleItem("Moonlit Garden Salad", "Fresh salad. +9 Wis, +9 Int, +5 MP regen for 15 min.", 170, "consumable");
            RegisterSimpleItem("Voidberry Pudding", "Dark dessert. +10 Int, +8 Wis, +8 Magic Resist for 15 min.", 185, "consumable");
            RegisterSimpleItem("Dragon's Feast", "Legendary meal. +11 Str, +11 End for 18 min.", 200, "consumable");
            RegisterSimpleItem("Essence-Infused Bread", "Glowing bread. +10 all stats for 15 min.", 220, "consumable");
            RegisterSimpleItem("Sunfire Souffle", "Solar food. +12 Str, +10 Fire Resist for 18 min.", 240, "consumable");
            RegisterSimpleItem("Prismatic Fruit Bowl", "Rainbow fruit. +10 all stats, +3 HP regen for 18 min.", 260, "consumable");
            RegisterSimpleItem("Titan's Roast", "Massive roast. +14 End, +12 Str for 20 min.", 285, "consumable");
            RegisterSimpleItem("Worldtree Acorn Bread", "Ancient bread. +12 Wis, +10 Int, +5 MP regen for 20 min.", 310, "consumable");
            RegisterSimpleItem("Voidfire Curry", "Unstable food. +13 Int, +12 all Resists for 18 min.", 335, "consumable");
            RegisterSimpleItem("Celestial Banquet Spread", "Grand feast. +12 all stats, +4 HP regen for 20 min.", 360, "consumable");
            RegisterSimpleItem("Phoenix Feather Souffle", "Reborn food. +14 End, +12 all stats for 22 min.", 390, "consumable");
            RegisterSimpleItem("Moonsilver-Glazed Ham", "Silver meat. +13 Str, +13 End for 22 min.", 410, "consumable");
            RegisterSimpleItem("Starcrust Pie", "Cosmic pie. +12 all stats, +3% haste for 20 min.", 440, "consumable");
            RegisterSimpleItem("Eternity's Harvest Feast", "Timeless meal. +14 all stats, +5 HP regen for 25 min.", 470, "consumable");
            RegisterSimpleItem("Swamp Rot Grog", "Foul grog. +2 End, +1 Poison Resist for 5 min.", 5, "consumable");
            RegisterSimpleItem("Goblin Firewater", "Burns going down. +3 Str, +3 Fire Resist for 5 min.", 12, "consumable");
            RegisterSimpleItem("Fernallan Moonshine", "Glowing drink. +4 Int, +3 Wis for 8 min.", 20, "consumable");
            RegisterSimpleItem("Braxonian Cactus Tequila", "Desert spirit. +5 Str, +4 End for 8 min.", 28, "consumable");
            RegisterSimpleItem("Duskbloom Brandy", "Fragrant brandy. +5 Wis, +4 MP regen for 10 min.", 35, "consumable");
            RegisterSimpleItem("Firesage Stout", "Volcanic beer. +6 Str, +6 Fire Resist for 10 min.", 42, "consumable");
            RegisterSimpleItem("Blight Whiskey", "Dark liquor. +7 Int, +5 Poison Resist for 10 min.", 50, "consumable");
            RegisterSimpleItem("Solunarian Cordial", "Golden drink. +6 all stats for 10 min.", 60, "consumable");
            RegisterSimpleItem("Beastblood Wine", "Crimson wine. +8 Str, +6 End for 12 min.", 68, "consumable");
            RegisterSimpleItem("Moonpetal Mead", "Sweet mead. +8 Wis, +6 Int for 12 min.", 80, "consumable");
            RegisterSimpleItem("Ashbloom Sake", "Volcanic sake. +9 Str, +8 Fire Resist for 12 min.", 90, "consumable");
            RegisterSimpleItem("Essence of Shadows", "Dark potion. +10 Int, +8 all Resists for 12 min.", 105, "consumable");
            RegisterSimpleItem("Starlight Lager", "Sparkling beer. +8 all stats for 12 min.", 115, "consumable");
            RegisterSimpleItem("Phoenix Down Tonic", "Revival tonic. +10 End, +8 HP regen for 15 min.", 130, "consumable");
            RegisterSimpleItem("Titan's Grog", "Enormous drink. +12 Str, +10 End for 15 min.", 145, "consumable");
            RegisterSimpleItem("Voidtouched Absinthe", "Reality-bending. +10 Int, +10 Wis for 15 min.", 155, "consumable");
            RegisterSimpleItem("Sunfire Rum", "Solar rum. +11 Str, +10 Fire Resist for 15 min.", 170, "consumable");
            RegisterSimpleItem("Moonsilver Elixir", "Silver potion. +10 all stats for 15 min.", 185, "consumable");
            RegisterSimpleItem("Worldtree Sap Wine", "Ancient wine. +12 Wis, +10 Int, +5 MP regen for 18 min.", 200, "consumable");
            RegisterSimpleItem("Dragonfire Bourbon", "Fiery bourbon. +12 Str, +12 Fire Resist for 18 min.", 220, "consumable");
            RegisterSimpleItem("Soulsteel Infusion", "Spirit potion. +12 all stats for 15 min.", 240, "consumable");
            RegisterSimpleItem("Celestial Champagne", "Heavenly bubbly. +11 all stats, +3 HP regen for 18 min.", 260, "consumable");
            RegisterSimpleItem("Voidwalker's Reserve", "Aged darkness. +14 Int, +12 all Resists for 18 min.", 285, "consumable");
            RegisterSimpleItem("Starmetal Spirits", "Cosmic drink. +12 all stats, +3% haste for 18 min.", 310, "consumable");
            RegisterSimpleItem("Phoenix Ember Mead", "Burning mead. +14 Str, +14 End for 20 min.", 335, "consumable");
            RegisterSimpleItem("Titan's Reserve Vintage", "Aged giant wine. +14 all stats for 20 min.", 360, "consumable");
            RegisterSimpleItem("Eternity's Vintage", "Ageless wine. +14 all stats, +5 HP regen for 22 min.", 390, "consumable");
            RegisterSimpleItem("Voidheart Cordial", "Void elixir. +13 all stats, +10 all Resists for 22 min.", 410, "consumable");
            RegisterSimpleItem("Sunfire Phoenix Lager", "Solar-fire beer. +15 Str, +15 End, +10 Fire Resist for 22 min.", 440, "consumable");
            RegisterSimpleItem("Worldtree Ambrosia", "World drink. +14 all stats, +4% haste for 22 min.", 470, "consumable");
            RegisterSimpleItem("Goblin Shortbow", "Crude bow. +2 Dex.", 8, "ammo");
            RegisterSimpleItem("Poisoned Arrows (20)", "Toxic arrows. +8% ranged damage, +poison proc.", 15, "ammo");
            RegisterEquipment("Fernallan Longbow", "Primary", "Dexterity", 2, 22, "Grown, not carved. The string hums with druidic blessing.");
            RegisterSimpleItem("Sharpbone Bolts (20)", "Bone bolts. +12% ranged damage.", 28, "ammo");
            RegisterEquipment("Windrunner Bow", "Primary", "Dexterity", 3, 38, "Light enough to fire while sprinting. Try not to miss.");
            RegisterSimpleItem("Blighted Arrows (20)", "Cursed arrows. +14% ranged damage, +poison proc.", 45, "ammo");
            RegisterEquipment("Boneframe Recurve", "Primary", "Dexterity", 3, 55, "The limbs flex with unnatural strength. Bone remembers.");
            RegisterSimpleItem("Razorbone Arrows (20)", "Wyrm arrows. +18% ranged damage.", 65, "ammo");
            RegisterEquipment("Runewood Longbow", "Primary", "Dexterity", 4, 75, "Runes burned into the stave guide arrows to their mark.");
            RegisterEquipment("Ironbark War Bow", "Primary", "Dexterity", 4, 85, "Draw weight that breaks lesser arms. Worth the effort.");
            RegisterSimpleItem("Adamantite Arrowheads (20)", "Hard arrows. +22% ranged damage.", 95, "ammo");
            RegisterEquipment("Nightstalker Bow", "Primary", "Dexterity", 5, 110, "Silent and deadly. Targets never hear it.");
            RegisterSimpleItem("Moonsilver Arrows (20)", "Silver arrows. +26% ranged damage.", 120, "ammo");
            RegisterEquipment("Sunfire Longbow", "Primary", "Dexterity", 6, 135, "Arrows leave trails of light. Beautiful and lethal.");
            RegisterEquipment("Wyrm Sinew Composite", "Primary", "Dexterity", 6, 155, "Strung with wyrm sinew. The draw feels almost eager.");
            RegisterSimpleItem("Voidtipped Bolts (20)", "Dark bolts. +28% ranged damage.", 145, "ammo");
            RegisterEquipment("Phoenix Recurve", "Primary", "Dexterity", 7, 170, "Arrows ignite mid-flight. A spectacular way to die.");
            RegisterEquipment("Starmetal Bow", "Primary", "Dexterity", 7, 190, "The metal fell from the sky. Arrows seem to fall upward.");
            RegisterSimpleItem("Abyssal Arrows (20)", "Void arrows. +32% ranged damage.", 210, "ammo");
            RegisterEquipment("Titan's Warbow", "Primary", "Dexterity", 8, 225, "Meant for siege warfare. Effective at any range.");
            RegisterEquipment("Dragonlord's Greatbow", "Primary", "Dexterity", 8, 250, "Scorched by dragonfire during its making. The heat lingers.");
            RegisterSimpleItem("Celestial Arrows (20)", "Holy arrows. +36% ranged damage.", 270, "ammo");
            RegisterEquipment("Voidweave Longbow", "Primary", "Dexterity", 9, 295, "Arrows vanish after leaving the string. They reappear inside the target.");
            RegisterEquipment("Soulfire Bow", "Primary", "Dexterity", 9, 320, "Arrows carry a piece of the wielder's fury. It always comes back.");
            RegisterSimpleItem("Prismatic Arrows (20)", "Elemental arrows. +40% ranged damage.", 350, "ammo");
            RegisterEquipment("Worldtree Warbow", "Primary", "Dexterity", 9, 380, "Carved from the oldest living wood. It still grows leaves.");
            RegisterEquipment("Phoenix Flight Bow", "Primary", "Dexterity", 9, 410, "Every arrow is a tiny sun. Shadows flee before them.");
            RegisterEquipment("Void Hunter's Bow", "Primary", "Dexterity", 9, 430, "Tracks its quarry through dimensions. There is no hiding.");
            RegisterSimpleItem("Starfall Arrows (20)", "Meteor arrows. +42% ranged damage.", 460, "ammo");
            RegisterEquipment("Titan Slayer's Longbow", "Primary", "Dexterity", 9, 480, "Built to bring down gods. The string hums with restrained power.");
            RegisterEquipment("Goblin Tooth Necklace", "Neck", "Strength", 1, 5, "Crude necklace. +2 Str.");
            RegisterEquipment("Fernallan Leaf Brooch", "Charm", "Intelligence", 1, 12, "Nature brooch. +3 Wis, +2 Int.");
            RegisterEquipment("Braxonian Sand Ring", "Ring", "Strength", 2, 20, "Desert ring. +4 End, +3 Str.");
            RegisterEquipment("Blightstone Pendant", "Neck", "Intelligence", 2, 28, "Dark pendant. +5 Int, +4 Poison Resist.");
            RegisterEquipment("Moonpetal Circlet", "Head", "Intelligence", 2, 35, "Lunar headpiece. +5 Wis, +4 Int.");
            RegisterEquipment("Scale-Etched Amulet", "Neck", "Endurance", 3, 42, "Dragon necklace. +6 End, +5 Fire Resist.");
            RegisterEquipment("Starlight Band", "Ring", "Charisma", 3, 50, "Sparkling ring. +7 Cha, +4 all Resists.");
            RegisterEquipment("Deep Pearl Ring", "Ring", "Intelligence", 3, 60, "Dark pearl ring. +7 Int, +6 Wis.");
            RegisterEquipment("Emberheart Pendant", "Neck", "Endurance", 4, 68, "Fire necklace. +8 End, +8 Fire Resist.");
            RegisterEquipment("Titan's Bone Ring", "Ring", "Strength", 4, 80, "Heavy ring. +8 Str, +7 End.");
            RegisterEquipment("Celestial Moonband", "Charm", "Intelligence", 4, 90, "Lunar ring. +8 Wis, +7 Int.");
            RegisterEquipment("Voidstone Choker", "Neck", "Intelligence", 5, 105, "Dark choker. +10 Int, +8 Magic Resist.");
            RegisterEquipment("Sunfire Signet", "Ring", "Strength", 5, 115, "Solar ring. +9 Str, +9 Fire Resist.");
            RegisterEquipment("Starweave Tiara", "Head", "Wisdom", 5, 130, "Star headpiece. +8 all stats.");
            RegisterEquipment("Dragonheart Pendant", "Neck", "Strength", 6, 145, "Dragon necklace. +10 End, +10 Str.");
            RegisterEquipment("Soulbound Ring", "Ring", "Wisdom", 6, 155, "Spirit ring. +10 Wis, +8 MP regen.");
            RegisterEquipment("Eclipse Earring", "Ring", "Intelligence", 8, 170, "Dark earring. +10 Int, +8 Agi.");
            RegisterEquipment("Phoenix Heart Ring", "Ring", "Endurance", 8, 185, "Fire ring. +11 End, +10 Fire Resist.");
            RegisterEquipment("Worldtree Amulet", "Neck", "Intelligence", 7, 200, "Ancient necklace. +12 Wis, +10 Int.");
            RegisterEquipment("Titan's Eye Circlet", "Head", "Strength", 7, 220, "Giant headpiece. +12 End, +10 Str.");
            RegisterEquipment("Voidstar Pendant", "Neck", "Intelligence", 7, 240, "Dark necklace. +13 Int, +12 all Resists.");
            RegisterEquipment("Sunmetal Band", "Ring", "Strength", 9, 260, "Solar ring. +11 Str, +11 Dex.");
            RegisterEquipment("Dragonlord's Signet", "Ring", "Strength", 8, 285, "Dragon ring. +12 Str, +12 End.");
            RegisterEquipment("Starweave Crown", "Head", "Wisdom", 8, 310, "Star crown. +12 all stats.");
            RegisterEquipment("Soulfire Earring", "Ring", "Intelligence", 8, 335, "Spirit earring. +13 Wis, +12 Int.");
            RegisterEquipment("Void Eclipse Ring", "Ring", "Intelligence", 8, 360, "Dark ring. +13 Int, +12 Wis.");
            RegisterEquipment("Phoenix Crown", "Head", "Endurance", 10, 390, "Fire crown. +14 End, +12 all stats.");
            RegisterEquipment("Titan's Sigil Choker", "Neck", "Strength", 10, 410, "Giant necklace. +14 Str, +14 End.");
            RegisterEquipment("Starfire Band", "Ring", "Wisdom", 10, 440, "Cosmic ring. +12 all stats, +3% haste.");
            RegisterEquipment("Eternity's Diadem", "Head", "Wisdom", 10, 470, "Timeless crown. +13 all stats, +4% haste.");
            RegisterEquipment("Goblin Patchwork Vest", "Chest", "Endurance", 2, 5, "Crude vest. +2 AC, +1 End.");
            RegisterEquipment("Fernallan Leaf Cloak", "Back", "Agility", 2, 12, "Woven from living fern fronds. Still green after years.");
            RegisterEquipment("Braxonian Sandcloth Robe", "Chest", "Intelligence", 2, 20, "Desert-woven cloth that breathes in heat and holds warmth in cold.");
            RegisterEquipment("Blighthide Bracers", "Bracer", "Endurance", 2, 28, "The leather darkens further each season. Still supple.");
            RegisterEquipment("Moonweave Sash", "Waist", "Wisdom", 3, 35, "Glows softly at night. Useful and unsettling.");
            RegisterEquipment("Beasthide Boots", "Foot", "Agility", 3, 42, "Thick hide grips any terrain. Beasts avoid the wearer.");
            RegisterEquipment("Duskweave Cowl", "Head", "Intelligence", 3, 50, "Shadows pool in the hood even at midday.");
            RegisterEquipment("Ironhide Gauntlets", "Hand", "Endurance", 4, 60, "Scaled knuckles over leather palms. Built for war.");
            RegisterEquipment("Runewoven Silk Robe", "Chest", "Intelligence", 4, 68, "Runes shift when read. The wearer learns to stop trying.");
            RegisterEquipment("Ironbark Reinforced Leggings", "Leg", "Endurance", 4, 80, "Ironbark splints sewn into leather. Stiff but protective.");
            RegisterEquipment("Wyrmscale Hauberk", "Chest", "Endurance", 5, 90, "Each ring is a wyrm scale. The mail is surprisingly light.");
            RegisterEquipment("Abyssal Silk Robe", "Chest", "Intelligence", 5, 105, "The silk absorbs spells aimed at the wearer. Mostly.");
            RegisterEquipment("Sunweave Cloak", "Back", "Endurance", 6, 115, "Warm as a summer noon, even in winter darkness.");
            RegisterEquipment("Moonsilver Chainmail", "Chest", "Agility", 6, 130, "Silver links that move like water. Silent even at full sprint.");
            RegisterEquipment("Phoenix Feather Mantle", "Back", "Endurance", 6, 145, "The feathers are still warm. They may never cool.");
            RegisterEquipment("Voidweave Bracers", "Bracer", "Intelligence", 6, 155, "Space bends slightly around the wrists. Enemies misjudge distance.");
            RegisterEquipment("Titan's Hide Belt", "Waist", "Endurance", 7, 170, "Cut from a titan's loincloth. Don't think about it.");
            RegisterEquipment("Starmetal Threaded Robe", "Chest", "Intelligence", 7, 185, "Tiny points of light wink in the fabric like distant stars.");
            RegisterEquipment("Worldtree Bark Vest", "Chest", "Wisdom", 7, 200, "Peeled from the Worldtree itself. It still grows.");
            RegisterEquipment("Dragonlord's Greaves", "Leg", "Endurance", 8, 220, "Fit for a rider. The knee guards bear claw marks.");
            RegisterEquipment("Soulweave Mantle", "Back", "Endurance", 8, 240, "Faint whispers trail behind the wearer. Friendly ones.");
            RegisterEquipment("Phoenix-Down Vest", "Chest", "Endurance", 8, 260, "Twice burned, twice remade. The vest remembers its deaths.");
            RegisterEquipment("Voidweave Leggings", "Leg", "Intelligence", 9, 285, "The fabric ripples without wind. Something moves beneath.");
            RegisterEquipment("Starweave Battle Robe", "Chest", "Endurance", 9, 310, "Constellations map across the surface. None match the sky.");
            RegisterEquipment("Titan's War Greaves", "Leg", "Endurance", 9, 335, "Heavy as sin. The ground trembles with each step.");
            RegisterEquipment("Moonweave Battle Cloak", "Back", "Endurance", 9, 360, "Billows dramatically even indoors. Impossible to look bad in.");
            RegisterEquipment("Phoenix-Wing Pauldrons", "Chest", "Endurance", 9, 390, "Spread like wings in battle. They aren't just decorative.");
            RegisterEquipment("Voidsilk Hood", "Head", "Intelligence", 9, 410, "The wearer sees in darkness. Also sees things that aren't there.");
            RegisterEquipment("Starmetal Woven Girdle", "Waist", "Endurance", 9, 440, "The buckle points north. Always north, even underground.");
            RegisterEquipment("Eternity's Vestments", "Chest", "Endurance", 9, 470, "The stitching has no beginning and no end. Neither does the garment.");

            SkillsPlugin.Log.LogInfo(
                $"ItemFactory: Registered {_registeredItems.Count - countBefore} tradeskill items.");
        }

        private static void RegisterBeggingItems()
        {
            RegisterSimpleItem("Stale Bread", "Hard as a rock.", 1, "food");
            RegisterSimpleItem("Chipped Mug", "Missing a handle.", 1, "junk");
            RegisterSimpleItem("Worn Bandage", "Used but functional.", 1, "crafting");
            RegisterSimpleItem("Faded Map Fragment", "Illegible.", 1, "junk");
            RegisterSimpleItem("Bent Copper Ring", "Barely a ring.", 2, "junk");
            RegisterSimpleItem("Half-Eaten Apple", "The good half.", 1, "food");
            RegisterSimpleItem("Tattered Cloth", "Barely holding together.", 1, "crafting");
            RegisterSimpleItem("Rusty Fishing Hook", "Still pointy.", 1, "bait");
            RegisterSimpleItem("Cracked Gemstone", "Worthless to jewelers.", 2, "junk");
        }

        // ═════════════════════════════════════════════════════════════
        // Registration helpers
        // ═════════════════════════════════════════════════════════════

        private static void RegisterSimpleItem(string name, string desc,
            int goldValue, string iconCategory)
        {
            if (_registeredItems.ContainsKey(name)) return;

            var item = ScriptableObject.CreateInstance<Item>();
            item.ItemName = name;
            item.Lore = desc;
            item.ItemValue = goldValue;
            item.Stackable = true;
            item.Classes = new System.Collections.Generic.List<Class>();
            item.ItemIcon = FindIcon(name, iconCategory);

            _registeredItems[name] = item;
            InjectIntoDatabase(item);

            // Attach buff spells to consumable items (food/drinks)
            if (iconCategory == "consumable" || iconCategory == "food" || iconCategory == "drink")
                ErenshorSkills.Items.ConsumableBuffs.AttachBuffToItem(item, desc);
        }

        private static void RegisterConsumable(string name, string desc,
            int goldValue, string statType, int statBonus, float duration,
            string category = "Food")
        {
            if (_registeredItems.ContainsKey(name)) return;

            var item = ScriptableObject.CreateInstance<Item>();
            item.ItemName = name;
            item.Lore = $"{desc}\nUse: +{statBonus * 5} HP, +{statBonus} {statType} for {duration / 60f:F0} min.\n<color={(category == "Drink" ? "#4FC3F7" : "#8BC34A")}>[{category}]</color>";
            item.ItemValue = goldValue;
            item.Disposable = true;  // Right-click to consume
            item.Stackable = true;   // Food stacks in inventory
            item.Classes = new System.Collections.Generic.List<Class>();
            item.ItemIcon = FindIcon(name, "food");

            // Track buff data for our custom consumption handler
            ConsumableBuffs[name] = (statType, statBonus, duration, category);

            // Create a minimal spell so the game shows "Activatable" on the item.
            // Our Patch_UseCustomConsumable prefix intercepts before CastSpell runs.
            try
            {
                var spell = ScriptableObject.CreateInstance<Spell>();
                spell.SpellName = $"{name} Buff";
                spell.SpellDesc = $"+{statBonus} {statType} for {duration / 60f:F0} min.";
                spell.StatusEffectMessageOnPlayer = $"You feel nourished.";
                // Game displays ticks * 3 as seconds, so divide by 3 for correct duration
                spell.SpellDurationInTicks = Mathf.Max(1, Mathf.RoundToInt(duration / 3f));
                spell.ManaCost = 0;
                spell.SpellChargeTime = 0f;
                spell.InflictOnSelf = true;
                spell.SelfOnly = true;
                spell.InstantEffect = false;
                spell.SpellRange = 0f;
                // Initialize lists to prevent NullReferenceException in StartSpell
                spell.ChargeVariations = new System.Collections.Generic.List<AudioClip>();
                spell.UsedBy = new System.Collections.Generic.List<Class>();
                try
                {
                    var tf = typeof(Spell).GetField("Type");
                    if (tf != null) tf.SetValue(spell, Enum.ToObject(tf.FieldType, 2));
                    var lf = typeof(Spell).GetField("Line");
                    if (lf != null) lf.SetValue(spell, Enum.ToObject(lf.FieldType, 29));
                }
                catch { }
                ApplyStatToSpell(spell, statType, statBonus);
                spell.HP = statBonus * 5; // HP bonus for regen
                spell.SpellIcon = item.ItemIcon;
                item.ItemEffectOnClick = spell;
            }
            catch { }

            _registeredItems[name] = item;
            InjectIntoDatabase(item);
        }

        /// <summary>
        /// Create a Spell ScriptableObject that applies a stat buff.
        /// This is assigned to ItemEffectOnClick so right-clicking
        /// the consumable applies the buff via the game's spell system.
        /// </summary>
        private static Spell CreateBuffSpell(string itemName, string statType,
            int statBonus, float duration)
        {
            try
            {
                var spell = ScriptableObject.CreateInstance<Spell>();
                spell.SpellName = $"{itemName} Buff";
                spell.SpellDesc = $"+{statBonus} {statType}";
                spell.StatusEffectMessageOnPlayer = $"You feel nourished. (+{statBonus} {statType})";
                spell.InstantEffect = false;
                spell.InflictOnSelf = true;
                spell.ManaCost = 0;
                spell.SpellChargeTime = 0f;
                spell.RequiredLevel = 0;

                // Set Type to Beneficial (2) and Line to Global_Buff (29)
                // so the game's CastSpell doesn't treat it as a damage spell.
                // Using reflection to set enum fields by integer value since
                // the enum types aren't directly accessible.
                try
                {
                    var typeField = typeof(Spell).GetField("Type");
                    var lineField = typeof(Spell).GetField("Line");
                    if (typeField != null)
                        typeField.SetValue(spell, Enum.ToObject(typeField.FieldType, 2));
                    if (lineField != null)
                        lineField.SetValue(spell, Enum.ToObject(lineField.FieldType, 29));
                }
                catch (Exception enumEx)
                {
                    SkillsPlugin.Log.LogWarning($"Spell enum set failed: {enumEx.Message}");
                }

                // Duration in ticks (game ticks are ~6 seconds each)
                // duration param is in seconds, so 120s = 20 ticks
                spell.SpellDurationInTicks = Mathf.Max(1,
                    Mathf.RoundToInt(duration / 6f));

                // Apply the stat
                ApplyStatToSpell(spell, statType, statBonus);

                return spell;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Failed to create buff spell for '{itemName}': {ex.Message}");
                return null;
            }
        }

        private static void ApplyStatToSpell(Spell spell, string statType, int value)
        {
            switch (statType)
            {
                case "Strength":     spell.Str = value; break;
                case "Agility":      spell.Agi = value; break;
                case "Endurance":    spell.End = value; break;
                case "Intelligence": spell.Int = value; break;
                case "Wisdom":       spell.Wis = value; break;
                case "Charisma":     spell.Cha = value; break;
                case "Dexterity":    spell.Dex = value; break;
            }
        }

        private static void RegisterEquipment(string name, string slot,
            string statType, int statBonus, int goldValue, string desc)
        {
            if (_registeredItems.ContainsKey(name)) return;

            var item = ScriptableObject.CreateInstance<Item>();
            item.ItemName = name;
            item.Lore = desc;
            item.ItemValue = goldValue;
            item.ItemLevel = Mathf.Clamp(goldValue / 15, 1, 35); // Approximate item level from gold value
            item.Stackable = false;
            item.Classes = new System.Collections.Generic.List<Class>();
            // Set RequiredSlot using the enum values from Item.SlotType
            // General=0, Head=1, Neck=2, Chest=3, Shoulder=4, Arm=5,
            // Bracer=6, Ring=7, Hand=8, Foot=9, Leg=10, Back=11,
            // Waist=12, Primary=13, Secondary=14, PrimaryOrSecondary=15
            try
            {
                int slotIdx = GetSlotIndex(slot);
                var slotField = typeof(Item).GetField("RequiredSlot");
                if (slotField != null)
                    slotField.SetValue(item, Enum.ToObject(slotField.FieldType, slotIdx));
            }
            catch { }
            item.ItemIcon = FindIcon(name, "equipment");

            ApplyStatToItem(item, statType, statBonus);

            // Auto-detect weapon type from item name for Primary slot items
            if (slot == "Primary" || slot == "PrimaryOrSecondary")
            {
                string lower = name.ToLower();
                int wtype = 0; // None
                if (lower.Contains("dagger") || lower.Contains("shiv") || lower.Contains("knife"))
                    wtype = 3; // OneHandDagger
                else if (lower.Contains("greatsword") || lower.Contains("maul") || lower.Contains("halberd") || lower.Contains("flamberge"))
                    wtype = 2; // TwoHandMelee
                else if (lower.Contains("staff") || lower.Contains("stave"))
                    wtype = 4; // TwoHandStaff
                else if (lower.Contains("bow") || lower.Contains("crossbow") || lower.Contains("longbow") || lower.Contains("shortbow"))
                    wtype = 5; // TwoHandBow
                else if (lower.Contains("sword") || lower.Contains("axe") || lower.Contains("mace") || lower.Contains("rapier") ||
                         lower.Contains("scimitar") || lower.Contains("hatchet") || lower.Contains("pick") || lower.Contains("blade") ||
                         lower.Contains("club") || lower.Contains("edge"))
                    wtype = 1; // OneHandMelee
                if (wtype > 0)
                {
                    try
                    {
                        var wtField = typeof(Item).GetField("ThisWeaponType");
                        if (wtField != null)
                            wtField.SetValue(item, Enum.ToObject(wtField.FieldType, wtype));
                    }
                    catch { }
                }
            }

            // Borrow 3D model and icon from matching game item
            string tradeskillGuess = GuessItemTradeskill(name, slot);
            ApplyVisualsToItem(item, tradeskillGuess);

            _registeredItems[name] = item;
            InjectIntoDatabase(item);
        }

        /// <summary>
        /// Register a weapon with damage, delay, weapon type, and optional stat bonus.
        /// WeaponType: 1=OneHandMelee, 2=TwoHandMelee, 3=OneHandDagger, 4=TwoHandStaff, 5=TwoHandBow
        /// </summary>
        private static void RegisterWeapon(string name, string slot, int weaponType,
            int damage, float delay, string statType, int statBonus, int goldValue, string desc)
        {
            if (_registeredItems.ContainsKey(name)) return;

            var item = ScriptableObject.CreateInstance<Item>();
            item.ItemName = name;
            item.Lore = desc;
            item.ItemValue = goldValue;
            item.ItemLevel = Mathf.Clamp(goldValue / 15, 1, 35);
            item.Stackable = false;
            item.WeaponDmg = damage;
            item.WeaponDly = delay;
            item.Classes = new System.Collections.Generic.List<Class>();
            // Set weapon type
            try
            {
                var wtField = typeof(Item).GetField("ThisWeaponType");
                if (wtField != null)
                    wtField.SetValue(item, Enum.ToObject(wtField.FieldType, weaponType));
            }
            catch { }
            // Set slot
            try
            {
                int slotIdx = GetSlotIndex(slot);
                var slotField = typeof(Item).GetField("RequiredSlot");
                if (slotField != null)
                    slotField.SetValue(item, Enum.ToObject(slotField.FieldType, slotIdx));
            }
            catch { }
            item.ItemIcon = FindIcon(name, "equipment");
            if (statBonus > 0) ApplyStatToItem(item, statType, statBonus);

            string tradeskillGuess = GuessItemTradeskill(name, slot);
            ApplyVisualsToItem(item, tradeskillGuess);

            _registeredItems[name] = item;
            InjectIntoDatabase(item);
        }

        /// <summary>
        /// Set up a bag item with a dummy spell so right-click triggers
        /// UseConsumable, which our prefix intercepts to open the bag.
        /// </summary>
        private static void SetupBagItem(string name)
        {
            if (!_registeredItems.TryGetValue(name, out var item)) return;
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.SpellName = "Open " + name;
            spell.SpellDesc = "Open this container.";
            spell.SelfOnly = true;
            spell.ManaCost = 0;
            spell.SpellChargeTime = 0f;
            spell.Cooldown = 0.5f;
            spell.ChargeVariations = new System.Collections.Generic.List<AudioClip>();
            spell.UsedBy = new System.Collections.Generic.List<Class>();
            item.ItemEffectOnClick = spell;
            item.SpellCastTime = 0f;
            item.Stackable = false; // Bags don't stack
            item.Disposable = false; // Don't consume the bag!
        }

        /// <summary>Guess which tradeskill made this item based on name/slot.</summary>
        private static string GuessItemTradeskill(string name, string slot)
        {
            string lower = name.ToLower();
            // Bows/arrows = fletching
            if (lower.Contains("bow") || lower.Contains("arrow") || lower.Contains("bolt") ||
                lower.Contains("crossbow") || lower.Contains("quiver"))
                return "Fletching";
            // Rings/necklaces/crowns = jewelcraft
            if (slot == "Ring" || slot == "Neck" || slot == "Charm" ||
                lower.Contains("ring") || lower.Contains("pendant") || lower.Contains("amulet") ||
                lower.Contains("crown") || lower.Contains("circlet") || lower.Contains("earring") ||
                lower.Contains("choker") || lower.Contains("brooch") || lower.Contains("diadem") ||
                lower.Contains("tiara") || lower.Contains("band") || lower.Contains("signet"))
                return "Jewelcraft";
            // Armor pieces = tailoring
            if (slot == "Chest" || slot == "Back" || slot == "Leg" || slot == "Waist" ||
                slot == "Foot" || slot == "Bracer" || slot == "Head" || slot == "Hand" ||
                lower.Contains("robe") || lower.Contains("cloak") || lower.Contains("vest") ||
                lower.Contains("mantle") || lower.Contains("boots") || lower.Contains("hood") ||
                lower.Contains("leggings") || lower.Contains("greaves"))
                return "Tailoring";
            // Weapons/shields = smithing
            return "Smithing";
        }

        /// <summary>
        /// Assign a unique Id to the item but do NOT modify the game's
        /// ItemDB array or itemDict. Our Harmony patch on GetItemByID
        /// handles lookups at runtime. This keeps our items separate
        /// from the game's database (per Recks' advice).
        /// </summary>
        private static void InjectIntoDatabase(Item item)
        {
            // Assign a unique Id so the game's save system can reference it
            string id = "CS_" + item.ItemName.Replace(" ", "_");
            item.Id = id;

            // Store in our own lookup by Id for the GetItemByID patch
            _itemsById[id] = item;
        }

        // Lookup by Id for our Harmony patch on GetItemByID
        private static Dictionary<string, Item> _itemsById
            = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Get a custom item by its CS_ Id.</summary>
        public static Item GetItemById(string id)
        {
            _itemsById.TryGetValue(id, out var item);
            return item;
        }

        // ═════════════════════════════════════════════════════════════
        // Icon system
        // ═════════════════════════════════════════════════════════════

        // Map of game item names whose icons we can borrow
        private static Dictionary<string, Sprite> _gameIconMap
            = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Visual data borrowed from a game item: icon, 3D model reference, and colors.
        /// </summary>
        private struct ItemVisuals
        {
            public Sprite Icon;
            public string EquipmentToActivate;
            public int WeaponType; // 0=None, 1=1HMelee, 2=2HMelee, 3=Dagger, 4=Staff, 5=Bow
            public int ItemLevel;
            public Color PrimaryColor, SecondaryColor;
            public Color MetalPrimary, MetalSecondary;
            public Color LeatherPrimary, LeatherSecondary;
        }

        // Game items indexed by slot type for 3D model borrowing
        private static Dictionary<int, List<ItemVisuals>> _gameVisualsBySlot
            = new Dictionary<int, List<ItemVisuals>>();

        private static void BuildGameIconMap()
        {
            if (_itemDb?.ItemDB == null) return;

            // Build a lookup of existing game item icons and visuals for reuse
            foreach (var item in _itemDb.ItemDB)
            {
                if (item == null || string.IsNullOrEmpty(item.ItemName)) continue;

                if (item.ItemIcon != null)
                    _gameIconMap[item.ItemName] = item.ItemIcon;

                // Index items with 3D models by slot for visual borrowing
                if (!string.IsNullOrEmpty(item.EquipmentToActivate))
                {
                    int slotKey = (int)item.RequiredSlot;
                    if (!_gameVisualsBySlot.ContainsKey(slotKey))
                        _gameVisualsBySlot[slotKey] = new List<ItemVisuals>();

                    _gameVisualsBySlot[slotKey].Add(new ItemVisuals
                    {
                        Icon = item.ItemIcon,
                        EquipmentToActivate = item.EquipmentToActivate,
                        WeaponType = (int)item.ThisWeaponType,
                        ItemLevel = item.ItemLevel,
                        PrimaryColor = item.ItemPrimaryColor,
                        SecondaryColor = item.ItemSecondaryColor,
                        MetalPrimary = item.ItemMetalPrimary,
                        MetalSecondary = item.ItemMetalSecondary,
                        LeatherPrimary = item.ItemLeatherPrimary,
                        LeatherSecondary = item.ItemLeatherSecondary,
                    });
                }
            }

            SkillsPlugin.Log.LogInfo(
                $"ItemFactory: Indexed {_gameIconMap.Count} game icons, " +
                $"{_gameVisualsBySlot.Values.Sum(l => l.Count)} 3D models for reuse.");
        }

        /// <summary>
        /// Apply icon and 3D visuals from a matching game item to a custom item.
        /// Borrows the 3D mesh and applies a custom color tint to distinguish it.
        /// </summary>
        public static void ApplyVisualsToItem(Item item, string tradeskill)
        {
            if (item == null) return;
            int slotKey = (int)item.RequiredSlot;

            // For weapons (Primary=13), also check PrimaryOrSecondary=15 since
            // most game weapons use that slot type
            List<ItemVisuals> visualsList = null;
            if (_gameVisualsBySlot.TryGetValue(slotKey, out var list1) && list1.Count > 0)
                visualsList = list1;
            else if (slotKey == 13 && _gameVisualsBySlot.TryGetValue(15, out var list2) && list2.Count > 0)
                visualsList = list2;
            else if (slotKey == 15 && _gameVisualsBySlot.TryGetValue(13, out var list3) && list3.Count > 0)
                visualsList = list3;

            if (visualsList != null && visualsList.Count > 0)
            {
                // For weapons, try to match by weapon type first
                int myWeaponType = (int)item.ThisWeaponType;
                List<ItemVisuals> filtered = null;
                if (myWeaponType > 0)
                {
                    filtered = new List<ItemVisuals>();
                    foreach (var v in visualsList)
                        if (v.WeaponType == myWeaponType) filtered.Add(v);
                    // Also check the other slot list
                    List<ItemVisuals> otherSlot = null;
                    if (slotKey == 13) _gameVisualsBySlot.TryGetValue(15, out otherSlot);
                    else if (slotKey == 15) _gameVisualsBySlot.TryGetValue(13, out otherSlot);
                    if (otherSlot != null)
                        foreach (var v in otherSlot)
                            if (v.WeaponType == myWeaponType) filtered.Add(v);
                    if (filtered.Count == 0) filtered = null; // Fall back to all
                }
                var pickFrom = filtered ?? visualsList;

                // Pick the model closest to our item's level for natural progression
                // Low-level crafted items get low-level looking models
                int targetLevel = item.ItemLevel > 0 ? item.ItemLevel : 1;
                ItemVisuals vis = pickFrom[0];
                int bestDist = int.MaxValue;
                foreach (var v in pickFrom)
                {
                    int dist = Mathf.Abs(v.ItemLevel - targetLevel);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        vis = v;
                    }
                }

                SkillsPlugin.Log.LogInfo(
                    $"Visuals: {item.ItemName} slot={slotKey} wtype={myWeaponType} " +
                    $"filtered={filtered?.Count ?? -1} pickFrom={pickFrom.Count} " +
                    $"chose={vis.EquipmentToActivate} visWtype={vis.WeaponType} visLvl={vis.ItemLevel} myLvl={targetLevel}");

                item.EquipmentToActivate = vis.EquipmentToActivate;

                // Only borrow icon if we have NO icon at all (not even procedural)
                if (item.ItemIcon == null && vis.Icon != null)
                    item.ItemIcon = vis.Icon;

                // Apply a tradeskill-tinted color to distinguish from the original
                Color tint = GetTradeskillTint(tradeskill);
                item.ItemPrimaryColor = tint;
                item.ItemSecondaryColor = Color.Lerp(tint, Color.black, 0.3f);
                item.ItemMetalPrimary = Color.Lerp(tint, Color.white, 0.2f);
                item.ItemMetalSecondary = Color.Lerp(tint, Color.gray, 0.3f);
                item.ItemLeatherPrimary = Color.Lerp(tint, new Color(0.4f, 0.25f, 0.1f), 0.4f);
                item.ItemLeatherSecondary = Color.Lerp(tint, new Color(0.3f, 0.2f, 0.1f), 0.5f);
            }
        }

        /// <summary>Get a unique color tint per tradeskill.</summary>
        private static Color GetTradeskillTint(string tradeskill)
        {
            switch (tradeskill)
            {
                case "Smithing":   return new Color(0.75f, 0.45f, 0.15f); // Copper/bronze
                case "Baking":     return new Color(0.85f, 0.75f, 0.35f); // Golden wheat
                case "Brewing":    return new Color(0.50f, 0.30f, 0.60f); // Deep purple
                case "Fletching":  return new Color(0.35f, 0.60f, 0.25f); // Forest green
                case "Jewelcraft": return new Color(0.30f, 0.50f, 0.85f); // Sapphire blue
                case "Tailoring":  return new Color(0.70f, 0.25f, 0.45f); // Crimson
                default:           return Color.gray;
            }
        }

        /// <summary>
        /// Find the best icon for an item, using the three-tier system:
        /// 1. Check for external PNG in Icons/ folder
        /// 2. Check for matching game icon
        /// 3. Generate procedural fallback
        /// </summary>
        private static Sprite FindIcon(string itemName, string category)
        {
            // ── Tier 1: External PNG ────────────────────────────────
            if (_iconCache.TryGetValue(itemName, out var cached))
                return cached;

            // ── Tier 2: Reuse matching game icon ────────────────────
            string matchName = FindGameIconMatch(itemName, category);
            if (matchName != null && _gameIconMap.TryGetValue(matchName, out var gameIcon))
            {
                _iconCache[itemName] = gameIcon;
                return gameIcon;
            }

            // ── Tier 3: Procedural fallback ─────────────────────────
            var procedural = GenerateProceduralIcon(itemName, category);
            _iconCache[itemName] = procedural;
            return procedural;
        }

        /// <summary>
        /// Find a game item whose icon would visually match our custom item.
        /// Returns the game item's name, or null if no good match.
        /// </summary>
        private static string FindGameIconMatch(string itemName, string category)
        {
            string lower = itemName.ToLower();

            // ── Direct name matches (our item name = game item name) ─
            if (_gameIconMap.ContainsKey(itemName))
                return itemName;

            // ── Specific item overrides (best visual match) ──────────
            // Bags/containers → game has actual bag icons
            if (lower.Contains("pouch") || lower.Contains("satchel") ||
                lower.Contains("backpack") || lower.Contains("pack"))
                return FindFirst("Tamer's Pack", "Bag of Offering Stones", "A Magical Bag", "Bag of Faerie Dust");

            // Bandages → cloth arm wraps
            if (lower.Contains("bandage"))
                return FindFirst("Braxonian Wrap", "Islander Bandit Armwrap", "Cloth Sleeves");

            // Rivets/nails/pins/fasteners → small metal items
            if (lower.Contains("rivet") || lower.Contains("nail") || lower.Contains("pin") ||
                lower.Contains("fastener") || lower.Contains("tack"))
                return FindFirst("Pupil's Pin", "Treasure Hunter's Pin", "Chunk of Copper Ore");

            // Arrowheads/arrow tips → arrow skill books have arrow icons
            if (lower.Contains("arrowhead") || lower.Contains("arrow tip"))
                return FindFirst("Skill Book: Blunted Arrow", "Chunk of Iron Ore");

            // Chain links/chainmail → actual chain armor icons
            if (lower.Contains("chain link") || lower.Contains("chain"))
                return FindFirst("Linked Chain Shirt", "Molorai Battle Chain", "Rotten Chain Tunic");

            // Blade blank/unfinished blade → sword icons
            if (lower.Contains("blade blank") || lower.Contains("blank"))
                return FindFirst("Copper Sword", "Rusty Shortsword");

            // Shield boss → shield icons
            if (lower.Contains("boss") || lower.Contains("shield"))
                return FindFirst("Old Buckler", "Crested Shield", "Braxonian Shield", "Magus Shield");

            // Wire/filigree/thread → string/thread items
            if (lower.Contains("wire") || lower.Contains("filigree") || lower.Contains("thread") ||
                lower.Contains("gossamer"))
                return FindFirst("Seastring", "Centering Cord", "Dreamthread Wings");

            // Buckle/clasp → belt/waist items
            if (lower.Contains("buckle") || lower.Contains("clasp"))
                return FindFirst("Braided Belt", "Metal Girdle", "Shadowclasp");

            // Setting/framework → jewelry items
            if (lower.Contains("setting"))
                return FindFirst("Copper Ring", "Golden Luminstone Ring");

            // Ingot/bar/billet/plate → ore items
            if (lower.Contains("ingot") || lower.Contains("bar") || lower.Contains("billet") ||
                lower.Contains("plate") || lower.Contains("alloy"))
                return FindFirst("Chunk of Iron Ore", "Chunk of Copper Ore", "Coal");

            // Shard/crystal → gem/crystal items
            if (lower.Contains("shard") || lower.Contains("crystal") || lower.Contains("gem") ||
                lower.Contains("jewel") || lower.Contains("opal") || lower.Contains("ruby") ||
                lower.Contains("sapphire") || lower.Contains("emerald") || lower.Contains("diamond"))
                return FindFirst("Faerie Dust", "Smooth Pebble");

            // Silk/fabric/cloth/bolt/weave → cloth items
            if (lower.Contains("silk") || lower.Contains("fabric") || lower.Contains("bolt") ||
                lower.Contains("weave") || lower.Contains("cloth") || lower.Contains("tattered"))
                return FindFirst("Cloth Shirt", "Cloth Sleeves");

            // Leather/hide → leather items
            if (lower.Contains("leather") || lower.Contains("hide"))
                return FindFirst("Tattered Leather Tunic", "Tattered Leather Gloves");

            // Wood/plank/shaft/stave/limb → wooden items
            if (lower.Contains("plank") || lower.Contains("timber") || lower.Contains("shaft") ||
                lower.Contains("limb") || lower.Contains("splinter"))
                return FindFirst("Carved Bow", "Weak Wand");

            // Bone/knuckle → bone items
            if (lower.Contains("bone") || lower.Contains("knuckle") || lower.Contains("skull"))
                return FindFirst("Bear Bone Club");

            // Feather → feather items
            if (lower.Contains("feather"))
                return FindFirst("Pink Pteriaped Feather", "Eagle Feather");

            // ── Broad category matches ───────────────────────────────

            // Berries/fruit
            if (lower.Contains("berr") || lower.Contains("fruit") ||
                lower.Contains("apple") || lower.Contains("fig"))
                return FindFirst("Bread", "Wild Berries");

            // Mushroom
            if (lower.Contains("mushroom"))
                return FindFirst("Fresh Mushroom");

            // Water/drink
            if (lower.Contains("water") || lower.Contains("juice") ||
                lower.Contains("tea") || lower.Contains("ale") ||
                lower.Contains("wine") || lower.Contains("elixir") ||
                lower.Contains("draught") || lower.Contains("nectar") ||
                lower.Contains("tonic") || lower.Contains("mead") ||
                lower.Contains("grog") || lower.Contains("stout") ||
                lower.Contains("brandy") || lower.Contains("rum") ||
                lower.Contains("lager") || lower.Contains("sake") ||
                lower.Contains("cordial") || lower.Contains("spirits") ||
                lower.Contains("ambrosia") || lower.Contains("vintage"))
                return FindFirst("Pod of Water");

            // Food/cooked items
            if (lower.Contains("bread") || lower.Contains("cake") || lower.Contains("stew") ||
                lower.Contains("meat") || lower.Contains("jerky") || lower.Contains("pie") ||
                lower.Contains("tart") || lower.Contains("dumpling") || lower.Contains("risotto") ||
                lower.Contains("salad") || lower.Contains("pudding") || lower.Contains("feast") ||
                lower.Contains("roast") || lower.Contains("souffle") || lower.Contains("goulash") ||
                lower.Contains("steak") || lower.Contains("curry") || lower.Contains("kebab") ||
                lower.Contains("soup") || lower.Contains("casserole") || lower.Contains("ham") ||
                lower.Contains("roll") || lower.Contains("scone") || lower.Contains("pepper") ||
                lower.Contains("honey") || lower.Contains("ration") || lower.Contains("hardtack") ||
                lower.Contains("biscuit"))
                return FindFirst("Bread");

            // Herbs/plants
            if (lower.Contains("herb") || lower.Contains("root") ||
                lower.Contains("moss") || lower.Contains("bloom") ||
                lower.Contains("petal") || lower.Contains("blossom") ||
                lower.Contains("flower") || lower.Contains("weed") ||
                lower.Contains("sage") || lower.Contains("dust") ||
                lower.Contains("pollen") || lower.Contains("grass") ||
                lower.Contains("wheat"))
                return FindFirst("Healing Moss", "Ogrespice Bundle");

            // Ore/metal (catch-all for anything not caught above)
            if (lower.Contains("ore") || lower.Contains("metal") || lower.Contains("iron") ||
                lower.Contains("copper") || lower.Contains("steel") || lower.Contains("gold") ||
                lower.Contains("silver") || lower.Contains("platinum") || lower.Contains("adamant") ||
                lower.Contains("mithril") || lower.Contains("starmetal") || lower.Contains("cobalt"))
                return FindFirst("Chunk of Iron Ore", "Chunk of Copper Ore", "Coal");

            // Rings
            if (lower.Contains("ring") || lower.Contains("band") ||
                lower.Contains("loop") || lower.Contains("signet") || lower.Contains("earring"))
                return FindFirst("Copper Ring", "Unsocketed Ring", "Golden Luminstone Ring");

            // Necklaces/amulets
            if (lower.Contains("pendant") || lower.Contains("choker") ||
                lower.Contains("necklace") || lower.Contains("amulet") || lower.Contains("talisman"))
                return FindFirst("Plain Necklace", "Neophyte Necklace");

            // Bows
            if (lower.Contains("bow") || lower.Contains("recurve") || lower.Contains("crossbow"))
                return FindFirst("Carved Bow", "Rotwood Bow", "Whitewood", "Traveler's Bow");

            // Arrows/bolts (ammo)
            if (lower.Contains("arrow") || lower.Contains("bolt"))
                return FindFirst("Carved Bow");

            // Swords
            if (lower.Contains("sword") || lower.Contains("blade") || lower.Contains("edge") ||
                lower.Contains("rapier") || lower.Contains("scimitar") || lower.Contains("flamberge") ||
                lower.Contains("falchion") || lower.Contains("khopesh") || lower.Contains("claymore"))
                return FindFirst("Copper Sword", "Steel Long Sword", "Polished Longsword");

            // Daggers
            if (lower.Contains("dagger") || lower.Contains("shiv") || lower.Contains("knife") ||
                lower.Contains("dirk") || lower.Contains("kukri"))
                return FindFirst("Rusty Dagger", "Priel Knife");

            // Axes
            if (lower.Contains("axe") || lower.Contains("hatchet") || lower.Contains("cleaver"))
                return FindFirst("Rusty Hatchet", "Molorai Axe");

            // Maces/blunt
            if (lower.Contains("mace") || lower.Contains("maul") || lower.Contains("club") ||
                lower.Contains("hammer") || lower.Contains("flail"))
                return FindFirst("Brackwood Mace", "Bear Bone Club");

            // Polearms
            if (lower.Contains("lance") || lower.Contains("partisan") || lower.Contains("halberd") ||
                lower.Contains("spear") || lower.Contains("pike"))
                return FindFirst("Rusty Hatchet");

            // Staves/wands
            if (lower.Contains("staff") || lower.Contains("stave") || lower.Contains("wand"))
                return FindFirst("Weak Wand");

            // Chest armor
            if (lower.Contains("vest") || lower.Contains("robe") || lower.Contains("tunic") ||
                lower.Contains("harness") || lower.Contains("brigandine") || lower.Contains("breastplate") ||
                lower.Contains("hauberk") || lower.Contains("chainmail") || lower.Contains("coat"))
                return FindFirst("Tattered Leather Tunic", "Cloth Shirt");

            // Cloaks
            if (lower.Contains("cloak") || lower.Contains("mantle") || lower.Contains("cape") ||
                lower.Contains("pauldron"))
                return FindFirst("Old Torn Cape");

            // Gloves
            if (lower.Contains("glove") || lower.Contains("gauntlet"))
                return FindFirst("Tattered Leather Gloves", "Cloth Gloves");

            // Boots
            if (lower.Contains("boot") || lower.Contains("shoe") || lower.Contains("greave"))
                return FindFirst("Tattered Leather Boots", "Cloth Shoes");

            // Helms
            if (lower.Contains("helm") || lower.Contains("cap") || lower.Contains("hood") ||
                lower.Contains("crown") || lower.Contains("circlet") || lower.Contains("cowl") ||
                lower.Contains("tiara") || lower.Contains("diadem"))
                return FindFirst("Tattered Leather Skullcap", "Cloth Hood");

            // Belts
            if (lower.Contains("belt") || lower.Contains("girdle") || lower.Contains("sash"))
                return FindFirst("Braided Belt", "Metal Girdle");

            // Bracers
            if (lower.Contains("bracer") || lower.Contains("vambrace"))
                return FindFirst("Cloth Sleeves", "Islander Bandit Armwrap");

            // Leggings
            if (lower.Contains("legging") || lower.Contains("pant") || lower.Contains("greave"))
                return FindFirst("Cloth Pants", "Tattered Leather Leggings");

            // Charms/focus
            if (lower.Contains("charm") || lower.Contains("focus") || lower.Contains("totem") ||
                lower.Contains("brooch"))
                return FindFirst("Charm of the Shield", "Unsocketed Ring");

            // Bait/grubs/worms
            if (lower.Contains("grub") || lower.Contains("worm") || lower.Contains("larva") ||
                lower.Contains("leech") || lower.Contains("cricket") || lower.Contains("moth") ||
                lower.Contains("bait") || lower.Contains("hook") || lower.Contains("shrimp") ||
                lower.Contains("crawler") || lower.Contains("bug") || lower.Contains("lure"))
                return FindFirst("Fishing Grub");

            // Ink/essence/exotic
            if (lower.Contains("ink") || lower.Contains("essence") || lower.Contains("breath"))
                return FindFirst("Faerie Dust");

            // Egg
            if (lower.Contains("egg"))
                return FindFirst("Fresh Mushroom");

            // Mug/cup
            if (lower.Contains("mug") || lower.Contains("cup") || lower.Contains("goblet"))
                return FindFirst("Pod of Water");

            // Notes/maps
            if (lower.Contains("map") || lower.Contains("note") || lower.Contains("fragment") ||
                lower.Contains("scroll") || lower.Contains("letter"))
                return FindFirst("Crumpled Note");

            // Junk catch-alls
            if (lower.Contains("coin")) return FindFirst("Chunk of Copper Ore");
            if (lower.Contains("hand") || lower.Contains("finger")) return FindFirst("Bear Bone Club");
            if (lower.Contains("button")) return FindFirst("Smooth Pebble");
            if (lower.Contains("string")) return FindFirst("Seastring", "Centering Cord");
            if (lower.Contains("scrap")) return FindFirst("Cloth Sleeves");

            return null;
        }

        /// <summary>Find the first matching game icon from a list of names.</summary>
        private static string FindFirst(params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (_gameIconMap.ContainsKey(name))
                    return name;
            }
            return null;
        }

        /// <summary>Report all custom items that don't have a borrowed game icon.</summary>
        public static void ReportMissingIcons()
        {
            var missing = new List<string>();
            var procedural = new List<string>();
            int total = 0, borrowed = 0;
            foreach (var kvp in _registeredItems)
            {
                total++;
                if (kvp.Value.ItemIcon == null)
                {
                    missing.Add(kvp.Key);
                }
                else
                {
                    // Check if it's our procedural icon (solid color square)
                    // vs a real game icon. Procedural icons have name "CS_Proc_*"
                    bool isGameIcon = _gameIconMap.ContainsValue(kvp.Value.ItemIcon);
                    bool isExternal = _iconCache.TryGetValue(kvp.Key, out var ci) && ci == kvp.Value.ItemIcon;
                    if (!isGameIcon && !isExternal)
                        procedural.Add(kvp.Key);
                    else
                        borrowed++;
                }
            }
            ChatHelper.Send($"<color=#00FF00>[Icons]</color> {total} custom items: {borrowed} with game icons, {procedural.Count} procedural, {missing.Count} missing.");
            if (procedural.Count > 0)
            {
                ChatHelper.Send("<color=#FFFF00>[Icons]</color> Items using procedural (colored square) icons:");
                foreach (var name in procedural)
                    ChatHelper.Send($"  <color=#AAAAAA>{name}</color>");
            }
            if (missing.Count > 0)
            {
                ChatHelper.Send("<color=#FF6666>[Icons]</color> Items with NO icon:");
                foreach (var name in missing)
                    ChatHelper.Send($"  <color=#FF6666>{name}</color>");
            }
            // Also write to log file
            string path = Path.Combine(BepInEx.Paths.ConfigPath, "ClassicSkills", "dumps", "missing_icons.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            lines.Add($"=== ICON REPORT: {total} items, {borrowed} borrowed, {procedural.Count} procedural, {missing.Count} missing ===\n");
            lines.Add("PROCEDURAL (need game icon match or custom PNG):");
            lines.AddRange(procedural);
            lines.Add("\nMISSING (no icon at all):");
            lines.AddRange(missing);
            File.WriteAllLines(path, lines);
            ChatHelper.Send($"<color=#AAAAAA>Full list saved to: {path}</color>");
        }

        /// <summary>Load PNG files from the external icons folder.</summary>
        private static void LoadExternalIcons()
        {
            if (!Directory.Exists(IconFolder)) return;

            string[] pngFiles = Directory.GetFiles(IconFolder, "*.png");
            foreach (var path in pngFiles)
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    byte[] data = File.ReadAllBytes(path);

                    Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    if (ImageConversion.LoadImage(tex, data))
                    {
                        Sprite sprite = Sprite.Create(tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        _iconCache[name] = sprite;
                    }
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning(
                        $"Failed to load icon '{path}': {ex.Message}");
                }
            }

            if (pngFiles.Length > 0)
                SkillsPlugin.Log.LogInfo(
                    $"ItemFactory: Loaded {pngFiles.Length} external icon PNGs.");
        }

        /// <summary>
        /// Generate a simple procedural icon — colored square with a
        /// category symbol drawn on it.
        /// </summary>
        private static Sprite GenerateProceduralIcon(string itemName, string category)
        {
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            // Background color by category
            Color bg, fg, border;
            switch (category)
            {
                case "junk":
                    bg = new Color(0.25f, 0.22f, 0.2f);
                    fg = new Color(0.6f, 0.55f, 0.5f);
                    border = new Color(0.4f, 0.35f, 0.3f);
                    break;
                case "bait":
                    bg = new Color(0.3f, 0.2f, 0.15f);
                    fg = new Color(0.7f, 0.5f, 0.35f);
                    border = new Color(0.5f, 0.35f, 0.25f);
                    break;
                case "food":
                    bg = new Color(0.2f, 0.25f, 0.12f);
                    fg = new Color(0.6f, 0.75f, 0.3f);
                    border = new Color(0.4f, 0.5f, 0.2f);
                    break;
                case "herb":
                    bg = new Color(0.12f, 0.25f, 0.15f);
                    fg = new Color(0.3f, 0.7f, 0.35f);
                    border = new Color(0.2f, 0.5f, 0.25f);
                    break;
                case "equipment":
                    bg = new Color(0.15f, 0.15f, 0.25f);
                    fg = new Color(0.7f, 0.65f, 0.3f);
                    border = new Color(0.5f, 0.45f, 0.2f);
                    break;
                case "crafting":
                    bg = new Color(0.2f, 0.18f, 0.22f);
                    fg = new Color(0.6f, 0.55f, 0.65f);
                    border = new Color(0.4f, 0.35f, 0.45f);
                    break;
                case "ammo":
                    bg = new Color(0.2f, 0.2f, 0.18f);
                    fg = new Color(0.65f, 0.6f, 0.5f);
                    border = new Color(0.45f, 0.4f, 0.35f);
                    break;
                default:
                    bg = new Color(0.2f, 0.2f, 0.2f);
                    fg = new Color(0.6f, 0.6f, 0.6f);
                    border = new Color(0.4f, 0.4f, 0.4f);
                    break;
            }

            // Fill background
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = bg;

            // Draw border (2px)
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (x < 2 || x >= size - 2 || y < 2 || y >= size - 2)
                        pixels[y * size + x] = border;
                }
            }

            // Draw a simple shape in the center based on category
            int cx = size / 2, cy = size / 2;
            switch (category)
            {
                case "food":
                    // Circle (apple/berry shape)
                    DrawCircle(pixels, size, cx, cy, 14, fg);
                    break;
                case "herb":
                    // Leaf shape (diamond)
                    DrawDiamond(pixels, size, cx, cy, 16, fg);
                    break;
                case "equipment":
                    // Ring shape (circle outline)
                    DrawCircle(pixels, size, cx, cy, 16, fg);
                    DrawCircle(pixels, size, cx, cy, 10, bg);
                    break;
                case "bait":
                    // Squiggle (S-curve approximation)
                    DrawDiamond(pixels, size, cx, cy - 4, 8, fg);
                    DrawDiamond(pixels, size, cx, cy + 4, 8, fg);
                    break;
                case "crafting":
                    // Gear/cog shape (small square)
                    DrawRect(pixels, size, cx - 10, cy - 10, 20, 20, fg);
                    DrawRect(pixels, size, cx - 6, cy - 6, 12, 12, bg);
                    break;
                case "ammo":
                    // Arrow pointing up
                    DrawDiamond(pixels, size, cx, cy - 6, 10, fg);
                    DrawRect(pixels, size, cx - 2, cy, 4, 16, fg);
                    break;
                default:
                    // Question mark shape
                    DrawCircle(pixels, size, cx, cy - 4, 10, fg);
                    DrawRect(pixels, size, cx - 2, cy + 10, 4, 6, fg);
                    break;
            }

            tex.SetPixels(pixels);
            tex.Apply();

            return Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }

        // ── Simple pixel drawing helpers ────────────────────────────

        private static void DrawCircle(Color[] pixels, int size,
            int cx, int cy, int radius, Color color)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                for (int y = cy - radius; y <= cy + radius; y++)
                {
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist <= radius)
                        pixels[y * size + x] = color;
                }
            }
        }

        private static void DrawDiamond(Color[] pixels, int size,
            int cx, int cy, int radius, Color color)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                for (int y = cy - radius; y <= cy + radius; y++)
                {
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    int dist = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                    if (dist <= radius)
                        pixels[y * size + x] = color;
                }
            }
        }

        private static void DrawRect(Color[] pixels, int size,
            int x0, int y0, int w, int h, Color color)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                for (int y = y0; y < y0 + h; y++)
                {
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    pixels[y * size + x] = color;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Stat / Slot helpers
        // ═════════════════════════════════════════════════════════════

        private static void ApplyStatToItem(Item item, string statType, int value)
        {
            // Erenshor's Item class has stat fields for each attribute
            // Map our stat name to the appropriate field
            switch (statType)
            {
                case "Strength":    item.Str = value; break;
                case "Agility":     item.Agi = value; break;
                case "Endurance":   item.End = value; break;
                case "Intelligence":item.Int = value; break;
                case "Wisdom":      item.Wis = value; break;
                case "Charisma":    item.Cha = value; break;
                case "Dexterity":   item.Dex = value; break;
            }
        }

        private static int GetSlotIndex(string slot)
        {
            switch (slot)
            {
                case "Head":    return 1;
                case "Neck":    return 2;
                case "Chest":   return 3;
                case "Shoulder":return 4;
                case "Arm":     return 5;
                case "Bracer":  return 6;
                case "Ring":    return 7;
                case "Hand":    return 8;
                case "Foot":    return 9;
                case "Leg":     return 10;
                case "Back":    return 11;
                case "Waist":   return 12;
                case "Primary": return 13;
                case "Secondary":return 14;
                case "Charm":   return 17;
                default:        return 0; // General
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // Harmony patch — inject items on game start
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// After the game's ItemDatabase initializes, inject all our custom
    /// items into it so they're recognized by the inventory, vendors,
    /// and loot systems.
    /// </summary>
    [HarmonyPatch(typeof(ItemDatabase), "Start")]
    public static class Patch_ItemDatabase_Init
    {
        public static void Postfix(ItemDatabase __instance)
        {
            try
            {
                ItemFactory.Initialize(__instance);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    $"ItemFactory initialization failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Also try to initialize in Awake (runs before Start) as a belt-and-suspenders
    /// approach. If the DB isn't ready yet, this will silently do nothing.
    /// </summary>
    [HarmonyPatch(typeof(ItemDatabase), "Awake")]
    public static class Patch_ItemDatabase_Awake
    {
        public static void Postfix(ItemDatabase __instance)
        {
            try
            {
                if (!ItemFactory.Initialized)
                    ItemFactory.Initialize(__instance);
            }
            catch { /* DB might not be ready in Awake — that's fine, Start will handle it */ }
        }
    }

    /// <summary>
    /// Patch GetItemByID so that when the game loads saved inventory
    /// and looks up items by their Id string, our custom items with
    /// CS_ prefixed IDs are found without modifying the game's itemDict.
    /// </summary>
    [HarmonyPatch(typeof(ItemDatabase), "GetItemByID")]
    public static class Patch_GetItemByID
    {
        public static void Postfix(ref Item __result, string id)
        {
            // If the game found the item (not Empty), don't override
            if (__result != null && __result != GameData.PlayerInv?.Empty) return;
            if (string.IsNullOrEmpty(id)) return;

            // Check our custom items by Id
            try
            {
                var custom = ItemFactory.GetItemById(id);
                if (custom != null)
                {
                    __result = custom;
                    SkillsPlugin.Log.LogInfo(
                        $"GetItemByID: Resolved custom item '{id}'");
                }
            }
            catch { }
        }
    }
}
