using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Patches
{
    // ═══════════════════════════════════════════════════════════════════
    // CHARACTER LOAD / SAVE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load skill data when the game world starts.
    /// We no longer load here because GameManager.Start fires during the character
    /// selection screen before the player has entered the world. Loading is now
    /// handled by DelayedCharacterLoad in Plugin.OnSceneLoaded when an actual
    /// game zone loads.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "Start")]
    public static class Patch_LoadCharacter
    {
        public static void Postfix()
        {
            // Intentionally empty — loading is handled by OnSceneLoaded
            SkillsPlugin.Log.LogInfo("GameManager.Start fired (character select screen). Skipping skill load.");
        }

        // Keep this coroutine for reference but it's no longer called from here
        private static System.Collections.IEnumerator DelayedLoad()
        {
            string name = "";
            for (int i = 0; i < 300; i++)
            {
                yield return null;
                try
                {
                    var stats = GameData.PlayerStats;
                    if (stats != null)
                    {
                        name = stats.MyName;
                        if (!string.IsNullOrEmpty(name)) break;
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(name))
                name = "DefaultChar";

            try
            {
                SkillsSaveManager.Load(name);
                SkillsPlugin.Log.LogInfo($"Skills loaded for '{name}'");

                if (SkillsPlugin.CfgShowChatMessages.Value)
                {
                    var data = SkillsSaveManager.Data;
                    int total = 0;
                    foreach (var s in data.GetAll()) total += s.Level;

                    ChatHelper.Send(
                        $"<color=#FFD700>[Classic Skills]</color> " +
                        $"Welcome back, {name}! " +
                        $"<color=#AAAAAA>({total} combined skill levels | " +
                        $"Press {SkillsPlugin.CfgSkillWindowKey.Value} for details)</color>");
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Error loading skills: {ex.Message}");
            }

            // Wait for ItemFactory and inventory to be ready, then restore custom items
            yield return new UnityEngine.WaitForSeconds(3f);
            for (int i = 0; i < 30; i++)
            {
                if (Items.ItemFactory.Initialized) break;
                yield return new UnityEngine.WaitForSeconds(0.5f);
            }

            try
            {
                Items.ItemFactory.RestoreCustomInventory();
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"RestoreCustomInventory error: {ex.Message}");
            }
        }
    }

    /// <summary>Save skill data when inventory toggles, and sync skills window visibility.</summary>
    [HarmonyPatch(typeof(Inventory), "ToggleInv")]
    public static class Patch_SaveOnInventory
    {
        public static void Postfix(Inventory __instance)
        {
            try
            {
                // Save custom items before saving skills
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                SkillsPlugin.Log.LogInfo("ToggleInv: saved skills + custom inventory.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Error saving skills: {ex.Message}");
            }

            // Sync skills window with inventory visibility
            try
            {
                bool invOpen = __instance.InvWindow != null &&
                               __instance.InvWindow.activeSelf;
                if (invOpen && !SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();
                else if (!invOpen && SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FISHING PATCHES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award fishing XP when a catch completes.
    /// Hooks resetFishing instead of StartFishing to avoid interfering
    /// with the fishing coroutine loop.
    /// </summary>
    [HarmonyPatch(typeof(Fishing), "resetFishing")]
    public static class Patch_DoFishing
    {
        public static void Prefix(Fishing __instance, bool ___fishCaught, bool ___fishing)
        {
            if (!SkillsPlugin.CfgEnableFishing.Value) return;
            // Prefix runs BEFORE resetFishing clears fishCaught
            if (!___fishCaught) return;
            // Only award XP if still in the fishing state
            // (not if the player moved/jumped and the coroutine just cleaned up)
            if (!___fishing) return;
            try
            {
                // fishCaught is true — a fish was caught this cycle.
                // Try to get the fish name from inventory, but award XP
                // regardless since we know a catch happened.
                string item = "fish";
                bool isJunk = false;
                bool isRare = false;

                var inv = GameData.PlayerInv;
                if (inv?.StoredItems != null && inv.StoredItems.Count > 0)
                {
                    var last = inv.StoredItems[inv.StoredItems.Count - 1];
                    if (last != null && !string.IsNullOrEmpty(last.ItemName))
                    {
                        item = last.ItemName;
                        string lower = item.ToLower();
                        isJunk = lower.Contains("seaweed") || lower.Contains("boot") ||
                                 lower.Contains("cloth shoe") || lower.Contains("driftwood");
                        isRare = lower.Contains("moongill") || lower.Contains("stonemouth") ||
                                 lower.Contains("treasure map") || lower.Contains("mold") ||
                                 lower.Contains("worm eel") || lower.Contains("fanged") ||
                                 lower.Contains("golden") || lower.Contains("caveskimmer");
                    }
                }

                Skills.FishingSkill.OnCatch(item, isRare, isJunk);

                // Give cooking materials based on catch type
                if (!isJunk)
                {
                    // Common catch = Raw Fish
                    Items.ItemFactory.GiveItemToPlayer("Raw Fish");

                    // Rare catches also give special fish
                    if (isRare)
                    {
                        string lower = item.ToLower();
                        if (lower.Contains("moongill"))
                            Items.ItemFactory.GiveItemToPlayer("Moongill Trout");
                        else if (lower.Contains("golden"))
                            Items.ItemFactory.GiveItemToPlayer("Golden Carp");
                        else
                        {
                            // Other rare fish give a random special fish
                            if (UnityEngine.Random.value < 0.5f)
                                Items.ItemFactory.GiveItemToPlayer("Moongill Trout");
                            else
                                Items.ItemFactory.GiveItemToPlayer("Golden Carp");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Fishing patch error: {ex.Message}");
            }
        }
    }

    // Fishing cast-time reduction removed — not implemented.
    // Safe Fall removed entirely.

    /// <summary>
    /// Fishing Catch Bonus — adds extra nibbles based on fishing skill.
    /// The game's fishing uses a 'nibble' counter: more nibbles = higher catch chance.
    /// This patch gives a skill-based chance to add bonus nibbles each fishing tick,
    /// making higher-skilled fishers catch fish more reliably.
    /// </summary>
    [HarmonyPatch(typeof(Fishing), "Update")]
    public static class Patch_FishingCatchBonus
    {
        public static void Prefix(Fishing __instance, ref int ___nibble, bool ___fishing,
            float ___waitForFish, Water ___fishWater)
        {
            if (!SkillsPlugin.CfgEnableFishing.Value) return;
            if (!___fishing || ___fishWater == null) return;
            // Only boost when the timer is about to expire (catch check imminent)
            if (___waitForFish > 60f * Time.deltaTime * 2f) return;
            try
            {
                float bonus = Skills.FishingSkill.CatchBonus;
                if (bonus <= 0f) return;
                // RareCatchBonus is skill * 0.5, so at skill 50 = 25%.
                // Roll for a bonus nibble: bonus% chance to add +1 nibble
                if (UnityEngine.Random.value * 100f < bonus)
                {
                    ___nibble++;
                }
            }
            catch { }
        }
    }

    // Rare catch bonus is now handled by Patch_FishingRareCatch above

    // ═══════════════════════════════════════════════════════════════════
    // SAFE FALL PATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept fall damage to apply Safe Fall reduction.
    /// Erenshor applies fall damage through the character's damage system.
    /// </summary>
    // Safe Fall removed

    // ═══════════════════════════════════════════════════════════════════
    // SWIMMING PATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect when the player is in water and award swimming XP.
    /// PlayerControl has a "Swimming" bool field and WaterMovement method.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "WaterMovement")]
    public static class Patch_Swimming
    {
        private static float _lastCheck = 0f;

        public static void Postfix(PlayerControl __instance, bool ___Swimming)
        {
            if (!SkillsPlugin.CfgEnableSwimming.Value) return;
            if (Time.time - _lastCheck < 1f) return;
            _lastCheck = Time.time;

            try
            {
                bool inWater = ___Swimming;
                var cc = __instance.GetComponent<CharacterController>();
                bool moving = cc != null && cc.velocity.magnitude > 0.1f;

                Skills.SwimmingSkill.OnSwimTick(inWater, moving);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ALCOHOL TOLERANCE PATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept consumable item use to award Food Tolerance XP
    /// and extend buff duration.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffect")]
    public static class Patch_UseConsumable
    {
        public static void Postfix(Stats __instance)
        {
            if (!SkillsPlugin.CfgEnableFoodTolerance.Value) return;
            try
            {
                // Award Food Tolerance XP when a status effect is applied (includes food buffs)
                Skills.FoodToleranceSkill.OnConsumeItem("Consumable");
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MEDITATION TICK (via PlayerMovement Update)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Process meditation ticks. Piggybacks on the movement update
    /// to check meditation state every frame.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "LateUpdate")]
    public static class Patch_MeditationTick
    {
        public static void Postfix()
        {
            if (!SkillsPlugin.CfgEnableMeditate.Value) return;
            try
            {
                Skills.MeditateSkill.ProcessTick();
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SWIM SPEED BONUS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply swimming speed bonus based on Swimming skill level.
    /// Reads the game's base swim speed field and caps actualSpeed
    /// to prevent exponential compounding.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), "WaterMovement")]
    public static class Patch_SwimSpeed
    {
        public static void Postfix(PlayerControl __instance, bool ___Swimming, float ___speed)
        {
            if (!SkillsPlugin.CfgEnableSwimming.Value) return;
            if (!___Swimming) return;
            try
            {
                float mult = Skills.SwimmingSkill.SpeedMultiplier;
                if (mult <= 1f) return;

                // ___speed is the base movement speed (private field, typically 3f)
                // Cap actualSpeed to base * multiplier to prevent compounding
                float maxSpeed = ___speed * mult;
                if (__instance.actualSpeed > 0.1f && __instance.actualSpeed < maxSpeed)
                    __instance.actualSpeed = Mathf.Min(__instance.actualSpeed * mult, maxSpeed);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BUFF DURATION BONUS (Food Tolerance)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extend buff durations based on Food Tolerance skill.
    /// Hooks AddStatusEffectNoChecks to multiply Duration by the bonus.
    /// Also awards Food Tolerance XP when any beneficial effect is applied.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffectNoChecks")]
    public static class Patch_BuffDuration
    {
        public static void Postfix(Stats __instance, Spell spell, bool _fromPlayer)
        {
            if (!_fromPlayer) return;
            try
            {
                // Only extend beneficial effects
                if (spell.Type != Spell.SpellType.Beneficial) return;

                // Determine if this buff came from a spell cast or from a consumable item.
                // If the player is actively casting a spell = Abjuration (spell buff).
                // Otherwise = Food Tolerance (food/drink consumable).
                bool isSpellCast = false;
                try
                {
                    var playerSpells = GameData.PlayerControl?.Myself?.MySpells;
                    if (playerSpells != null)
                        isSpellCast = playerSpells.isCasting();
                }
                catch { }

                float totalMult = 1f;

                // Food Tolerance: extends food/drink consumable buffs (not actively casting)
                if (!isSpellCast && SkillsPlugin.CfgEnableFoodTolerance.Value)
                {
                    float bonus = Skills.FoodToleranceSkill.DurationBonus;
                    if (bonus > 0f)
                        totalMult += bonus / 100f;
                }

                // Abjuration: extends spell buffs (player actively casting)
                if (isSpellCast && SkillsPlugin.CfgEnableMagicSkills.Value)
                {
                    float abjMult = Skills.MagicSkills.GetBuffDurationMultiplier();
                    if (abjMult > 1f)
                        totalMult += (abjMult - 1f);
                }

                if (totalMult <= 1f) return;

                // Find the status effect that was just applied and extend it
                for (int i = 0; i < 30; i++)
                {
                    if (__instance.StatusEffects[i] != null &&
                        __instance.StatusEffects[i].Effect == spell)
                    {
                        __instance.StatusEffects[i].Duration =
                            Mathf.RoundToInt(__instance.StatusEffects[i].Duration * totalMult);
                        break;
                    }
                }

                // Award Food Tolerance XP for food/drink consumables (not spell casts)
                if (!isSpellCast && SkillsPlugin.CfgEnableFoodTolerance.Value)
                    Skills.FoodToleranceSkill.OnConsumeItem(spell.SpellName);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CUSTOM CONSUMABLE HANDLING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept UseConsumable to handle our custom food items.
    /// The game's native path crashes because our runtime Spell objects
    /// lack fields that CastSpell.StartSpell expects. We apply the buff
    /// directly via Stats.AddStatusEffect instead.
    /// </summary>
    /// <summary>
    /// Intercept UseConsumable to handle our custom food items.
    /// We apply the buff directly and handle item removal using
    /// the game's own pattern (Quantity--, set to Empty, UpdateSlotImage).
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "UseConsumable")]
    public static class Patch_UseCustomConsumable
    {
        public static bool Prefix(ItemIcon __instance)
        {
            try
            {
                var item = __instance.MyItem;
                if (item == null) return true;

                string name = item.ItemName;
                if (string.IsNullOrEmpty(name)) return true;

                // Recipe scrolls — right-click to learn the recipe
                if (Stations.RecipeVendors.IsRecipeScroll(item))
                {
                    if (Stations.RecipeVendors.OnItemPurchased(item))
                    {
                        // Remove the scroll from inventory
                        __instance.MyItem = GameData.PlayerInv.Empty;
                        __instance.Quantity = 0;
                        __instance.UpdateSlotImage();
                    }
                    return false; // Don't pass to game's UseConsumable
                }

                // If a bag window is open, right-clicking an item stores it in the bag
                if (Items.BagWindow.AnyOpen && !Items.BagSystem.IsBag(name))
                {
                    if (Items.BagSystem.TryStoreFromInventorySlot(__instance))
                        return false; // Consumed the click
                }

                // Check if this is a bag — open bag window instead of consuming
                if (Items.BagSystem.IsBag(name))
                {
                    // Use Quantity field to store persistent bag instance ID
                    // Bags are non-stackable so Quantity is normally 1
                    // We use values 100+ as our bag instance IDs
                    int bagNum = __instance.Quantity;
                    if (bagNum < 100)
                    {
                        // First time opening — assign a new permanent ID
                        bagNum = Items.BagSystem.NextBagNumber();
                        __instance.Quantity = bagNum;
                    }
                    string bagId = $"bag{bagNum}";
                    Items.BagWindow.Open(bagId, name);
                    return false; // Don't consume the bag!
                }

                if (!Items.ItemFactory.ConsumableBuffs.TryGetValue(name, out var buff))
                    return true; // not ours, let game handle it

                // Check if player is alive and not casting
                if (!GameData.PlayerControl.Myself.Alive) return false;
                if (GameData.PlayerControl.Myself.MySpells.isCasting()) return false;

                // Apply the buff via AddStatusEffectNoChecks
                var stats = GameData.PlayerStats;
                if (stats == null) return false;

                // CATEGORY CHECK: Remove any existing buff from the same category
                // (Food overwrites Food, Drink overwrites Drink)
                string category = buff.category; // "Food" or "Drink"
                string catTag = $"[CS_{category}]";
                try
                {
                    // Game uses StatusEffects[0..29] fixed array
                    for (int i = 0; i < 30; i++)
                    {
                        var eff = stats.StatusEffects[i];
                        if (eff != null && eff.Effect != null &&
                            eff.Effect.SpellName != null &&
                            eff.Effect.SpellName.Contains(catTag))
                        {
                            // Remove this effect by nulling the slot
                            stats.StatusEffects[i] = null;
                            SkillsPlugin.Log.LogInfo(
                                $"Removed previous {category} buff: {eff.Effect.SpellName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogWarning($"Category override error: {ex.Message}");
                }

                var spell = ScriptableObject.CreateInstance<Spell>();
                // Embed category tag in spell name for later removal
                spell.SpellName = $"{name} {catTag}";
                spell.StatusEffectMessageOnPlayer =
                    $"You feel nourished. (+{buff.bonus} {buff.stat})";
                // Game displays ticks * 3 as seconds, so divide by 3
                spell.SpellDurationInTicks = Mathf.Max(1,
                    Mathf.RoundToInt(buff.dur / 3f));
                spell.SelfOnly = true;
                spell.InflictOnSelf = true;
                spell.InstantEffect = false;
                spell.ManaCost = 0;
                spell.SpellChargeTime = 0f;

                // Set Type = Beneficial, Line = Global_Buff
                try
                {
                    var tf = typeof(Spell).GetField("Type");
                    if (tf != null)
                        tf.SetValue(spell, System.Enum.ToObject(tf.FieldType, 2));
                    var lf = typeof(Spell).GetField("Line");
                    if (lf != null)
                        lf.SetValue(spell, System.Enum.ToObject(lf.FieldType, 29));
                }
                catch { }

                // HP for regen + named stat
                spell.HP = buff.bonus * 5;
                switch (buff.stat)
                {
                    case "Strength":     spell.Str = buff.bonus; break;
                    case "Agility":      spell.Agi = buff.bonus; break;
                    case "Endurance":    spell.End = buff.bonus; break;
                    case "Intelligence": spell.Int = buff.bonus; break;
                    case "Wisdom":       spell.Wis = buff.bonus; break;
                    case "Charisma":     spell.Cha = buff.bonus; break;
                    case "Dexterity":    spell.Dex = buff.bonus; break;
                }

                // Use the item's icon for the buff icon
                spell.SpellIcon = item.ItemIcon;

                stats.AddStatusEffectNoChecks(spell, true, 0, null);

                ChatHelper.Send(
                    $"<color=#CDDC39>[{category}]</color> " +
                    $"You consume the {name}. +{buff.bonus} {buff.stat} " +
                    $"for {buff.dur / 60f:F0} min.");

                // Remove item using the game's own pattern:
                // Quantity--, then set to Empty if 0, then UpdateSlotImage
                if (item.Disposable)
                {
                    __instance.Quantity--;
                }
                if (__instance.Quantity <= 0)
                {
                    __instance.MyItem = GameData.PlayerInv.Empty;
                }
                __instance.UpdateSlotImage();

                return false; // skip game's UseConsumable
            }
            catch (System.Exception ex)
            {
                SkillsPlugin.Log.LogError($"Custom consumable error: {ex}");
                return false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMBAT WEAPON SKILL PATCHES
    // ═══════════════════════════════════════════════════════════════════

    // Combat weapon XP patches are now in AttackSyncFix.cs (merged with Attack Sync Fix)

    // ═══════════════════════════════════════════════════════════════════
    // RECIPE DROPS FROM ENEMIES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Roll for recipe scroll drops when the player earns XP (enemy kill).
    /// Drop chance scales with XP earned (proxy for enemy level).
    /// Higher tier recipes drop from higher level enemies.
    /// Boss-only recipes require 200+ XP kills (named/boss mobs).
    /// Non-boss-only drops cap at trivial 178 to avoid redundancy with endgame.
    /// </summary>
    /// <summary>
    /// Inject recipe scrolls into mob loot tables before the loot window opens.
    /// Hooks LootTable.LoadLootTable to add recipe scrolls to ActualDrops.
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_RecipeDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;

            try
            {
                // Get mob level and boss status from the LootTable's Character
                var character = __instance.GetComponent<Character>();
                if (character == null) return;

                int mobLevel = 0;
                bool isBoss = false;
                try
                {
                    mobLevel = character.MyStats?.Level ?? 0;
                    isBoss = character.BossXp > 1f;
                }
                catch { }
                if (mobLevel <= 0) return;

                // Drop chance: 5% normal, 30% boss
                float dropChance = isBoss ? 0.30f : 0.05f;
                if (UnityEngine.Random.value > dropChance) return;

                // Mob level 1-35 maps to recipe MinSkillLevel 1-180
                int centerSkill = mobLevel * 5;
                int minRecipeSkill = Mathf.Max(1, centerSkill - 15);
                int maxRecipeSkill = Mathf.Min(180, centerSkill + 10);

                if (isBoss)
                {
                    minRecipeSkill = Mathf.Max(1, centerSkill - 5);
                    maxRecipeSkill = Mathf.Min(180, centerSkill + 20);
                }

                // Gather eligible recipes
                var candidates = new System.Collections.Generic.List<string>();
                string[] skills = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                foreach (var ts in skills)
                {
                    var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                    foreach (var r in recipes)
                    {
                        if (r.MinSkillLevel < minRecipeSkill) continue;
                        if (r.MinSkillLevel > maxRecipeSkill) continue;
                        if (r.BossOnly && !isBoss) continue;
                        candidates.Add(r.Name);
                    }
                }

                if (candidates.Count == 0) return;

                string recipeName = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                var scroll = Stations.RecipeVendors.GetScrollForRecipe(recipeName);
                if (scroll == null) return;

                // Inject into ActualDrops (max 8 loot slots)
                if (__instance.ActualDrops.Count < 8)
                {
                    __instance.ActualDrops.Add(scroll);
                }
                else
                {
                    // Replace an empty slot if all 8 are taken
                    for (int i = 0; i < __instance.ActualDrops.Count; i++)
                    {
                        if (__instance.ActualDrops[i] == null ||
                            __instance.ActualDrops[i] == GameData.PlayerInv.Empty)
                        {
                            __instance.ActualDrops[i] = scroll;
                            break;
                        }
                    }
                }
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MEAT DROPS FROM BEAST MOBS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drop Beast Meat from beast-type mobs and Spider Silk from spiders.
    /// Hooks LootTable.LoadLootTable to inject items into mob loot.
    /// Spider silk tier scales with mob level:
    ///   Low level spiders: Spider Silk Strand
    ///   Mid level spiders: Spider Silk Strand + chance of Abyssal Silk Thread
    ///   High level spiders: Abyssal Silk Thread + chance of Celestial Silk Bolt
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_MeatDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            try
            {
                var character = __instance.GetComponent<Character>();
                if (character == null) return;

                string mobName = "";
                int mobLevel = 0;
                try
                {
                    mobName = character.MyStats?.MyName ?? "";
                    mobLevel = character.MyStats?.Level ?? 0;
                }
                catch { }
                if (string.IsNullOrEmpty(mobName)) return;
                string lower = mobName.ToLower();

                // ── Spider silk drops ──
                bool isSpider = lower.Contains("spider") || lower.Contains("arachn");
                if (isSpider)
                {
                    // 35% chance to drop silk
                    if (UnityEngine.Random.value <= 0.35f)
                    {
                        string silkName;
                        if (mobLevel >= 25)
                            silkName = UnityEngine.Random.value < 0.20f
                                ? "Celestial Silk Bolt" : "Abyssal Silk Thread";
                        else if (mobLevel >= 15)
                            silkName = UnityEngine.Random.value < 0.25f
                                ? "Abyssal Silk Thread" : "Spider Silk Strand";
                        else
                            silkName = "Spider Silk Strand";

                        var silk = Items.ItemFactory.GetItem(silkName);
                        if (silk != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(silk);
                    }
                }

                // ── Wyrm/Drake/Dragon drops: Beast Bone Shard + Elder Wyrm Bone ──
                bool isWyrm = lower.Contains("wyrm") || lower.Contains("drake") ||
                    lower.Contains("dragon") || lower.Contains("firebone") ||
                    lower.Contains("icebone");
                if (isWyrm)
                {
                    // 30% Beast Bone Shard
                    if (UnityEngine.Random.value <= 0.30f)
                    {
                        var bone = Items.ItemFactory.GetItem("Beast Bone Shard");
                        if (bone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(bone);
                    }
                    // Elder Wyrm Bone: 15% from low level, 30% from high level
                    float wyrmBoneChance = mobLevel >= 20 ? 0.30f : 0.15f;
                    if (UnityEngine.Random.value <= wyrmBoneChance)
                    {
                        var elderBone = Items.ItemFactory.GetItem("Elder Wyrm Bone");
                        if (elderBone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(elderBone);
                    }
                }

                // ── Fire creature drops: Phoenix Feather ──
                bool isFire = lower.Contains("firebone") || lower.Contains("fire") &&
                    (lower.Contains("guardian") || lower.Contains("elemental") ||
                     lower.Contains("phoenix"));
                if (isFire && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.20f)
                    {
                        var feather = Items.ItemFactory.GetItem("Phoenix Feather");
                        if (feather != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(feather);
                    }
                }

                // ── Void/Undead/Corrupted drops: Breath of the Abyss ──
                bool isVoid = lower.Contains("void") || lower.Contains("abyssal") ||
                    lower.Contains("blight") || lower.Contains("corrupted") ||
                    lower.Contains("sivakayan") || lower.Contains("ghost") ||
                    lower.Contains("wraith") || lower.Contains("shade") ||
                    lower.Contains("lich") || lower.Contains("undead");
                if (isVoid && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.20f)
                    {
                        var breath = Items.ItemFactory.GetItem("Breath of the Abyss");
                        if (breath != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(breath);
                    }
                }

                // ── Malaroth drops: Malaroth Scale Fragment ──
                bool isMalaroth = lower.Contains("malaroth");
                if (isMalaroth)
                {
                    if (UnityEngine.Random.value <= 0.35f)
                    {
                        var scale = Items.ItemFactory.GetItem("Malaroth Scale Fragment");
                        if (scale != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(scale);
                    }
                }

                // ── Ice/Frost creature drops: Frostweave Strand ──
                bool isFrost = lower.Contains("frost") || lower.Contains("ice") ||
                    lower.Contains("icebone") ||
                    (lower.Contains("guardian") && lower.Contains("ice"));
                if (isFrost && mobLevel >= 15)
                {
                    if (UnityEngine.Random.value <= 0.25f)
                    {
                        var frost = Items.ItemFactory.GetItem("Frostweave Strand");
                        if (frost != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(frost);
                    }
                }

                // ── Giant/Titan drops: Titan's Knucklebone ──
                bool isGiant = lower.Contains("giant") || lower.Contains("titan") ||
                    lower.Contains("golem") || lower.Contains("volcanic");
                if (isGiant && mobLevel >= 20)
                {
                    if (UnityEngine.Random.value <= 0.15f)
                    {
                        var titan = Items.ItemFactory.GetItem("Titan's Knucklebone");
                        if (titan != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(titan);
                    }
                }

                // ── Skeleton drops: Beast Bone Shard ──
                bool isSkeleton = lower.Contains("skeleton") || lower.Contains("boneraiser") ||
                    lower.Contains("boneweaver") || lower.Contains("bonesaw");
                if (isSkeleton)
                {
                    if (UnityEngine.Random.value <= 0.25f)
                    {
                        var bone = Items.ItemFactory.GetItem("Beast Bone Shard");
                        if (bone != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(bone);
                    }
                }

                // ── Beast meat drops ──
                bool isBeast = lower.Contains("wolf") || lower.Contains("bear") ||
                    lower.Contains("boar") || lower.Contains("beast") ||
                    lower.Contains("lion") || lower.Contains("stag") ||
                    lower.Contains("deer") || isSpider ||
                    lower.Contains("wyrm") || lower.Contains("drake") ||
                    lower.Contains("dragon") || lower.Contains("malaroth") ||
                    lower.Contains("bat") || lower.Contains("rat") ||
                    lower.Contains("hound") || lower.Contains("raptor") ||
                    lower.Contains("serpent") || lower.Contains("snake") ||
                    lower.Contains("crab") || lower.Contains("scorpion") ||
                    lower.Contains("beetle") || lower.Contains("cat") ||
                    lower.Contains("tiger") || lower.Contains("panther") ||
                    lower.Contains("gorilla") || lower.Contains("ape") ||
                    lower.Contains("lizard") || lower.Contains("croc");

                if (!isBeast) return;

                // 25% chance to drop meat
                if (UnityEngine.Random.value <= 0.25f)
                {
                    var meat = Items.ItemFactory.GetItem("Beast Meat");
                    if (meat != null && __instance.ActualDrops.Count < 8)
                        __instance.ActualDrops.Add(meat);
                }

                // 20% chance to drop hide/leather (tier based on mob level)
                if (UnityEngine.Random.value <= 0.20f)
                {
                    string hideName;
                    if (mobLevel >= 18)
                        hideName = "Tough Hide Strip";
                    else
                        hideName = "Worn Leather Scrap";

                    var hide = Items.ItemFactory.GetItem(hideName);
                    if (hide != null && __instance.ActualDrops.Count < 8)
                        __instance.ActualDrops.Add(hide);
                }

                // ── Endgame boss drops: Thread of Fate, Essence of Eternity ──
                bool isBoss = false;
                try { isBoss = character.BossXp > 1f; } catch { }
                if (isBoss && mobLevel >= 25)
                {
                    // 10% Thread of Fate from endgame bosses
                    if (UnityEngine.Random.value <= 0.10f)
                    {
                        var fate = Items.ItemFactory.GetItem("Thread of Fate");
                        if (fate != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(fate);
                    }
                    // 8% Essence of Eternity from endgame bosses
                    if (UnityEngine.Random.value <= 0.08f)
                    {
                        var ess = Items.ItemFactory.GetItem("Essence of Eternity");
                        if (ess != null && __instance.ActualDrops.Count < 8)
                            __instance.ActualDrops.Add(ess);
                    }
                }
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MINING NODE BONUS DROPS
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "TryMine")]
    public static class Patch_MiningBonus
    {
        private static int GetZoneTier(string scene)
        {
            switch (scene)
            {
                case "Stowaway": case "Tutorial": case "ShiveringStep": return 1;
                case "Azure": case "Windwashed": case "FernallaField": case "Willowwatch": return 2;
                case "Silkengrass": case "Braxonian": case "Brake": case "Loomingwood": return 3;
                case "Soluna": case "Ripper": case "Rottenfoot": return 4;
                case "Hidden": case "Azynthi": case "AzynthiClear":
                case "Underspine": case "PrielPlateau": case "Krakengard": return 5;
                default: return 2;
            }
        }

        private static readonly string[][] TierOres = {
            new string[0],
            new[] { "Chunk of Copper Ore", "Chunk of Iron Ore", "Coal" },
            new[] { "Chunk of Iron Ore", "Coal", "Chunk of Silver Ore" },
            new[] { "Chunk of Silver Ore", "Chunk of Gold Ore", "Chunk of Adamantite Ore" },
            new[] { "Chunk of Gold Ore", "Chunk of Platinum Ore", "Chunk of Adamantite Ore" },
            new[] { "Chunk of Platinum Ore", "Chunk of Adamantite Ore" },
        };
        private static readonly string[][] TierGems = {
            new string[0],
            new[] { "Smooth Pebble", "Rough Geode" },
            new[] { "Rough Geode", "Moonstone Pebble", "Glimmering Geode Fragment" },
            new[] { "Rough Ruby", "Rough Emerald", "Rough Sapphire", "Bloodstone Chip" },
            new[] { "Rough Ruby", "Rough Sapphire", "Flawless Diamond", "Abyssal Opal" },
            new[] { "Flawless Diamond", "Abyssal Opal", "Starshard Crystal", "Prismatic Jewel" },
        };
        private static readonly string[][] TierMetals = {
            new string[0],
            new string[0],
            new[] { "Moonsilver Bar" },
            new[] { "Moonsilver Bar", "Sunforged Ingot", "Void Iron Chunk" },
            new[] { "Sunforged Ingot", "Void Iron Chunk", "Soulsteel Billet" },
            new[] { "Soulsteel Billet", "Starmetal Fragment" },
        };
        private static readonly string[] EndgameMining = {
            "Crystallized Time", "Worldtree Splinter", "Essence of Eternity"
        };

        public static void Postfix(Character target)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            try
            {
                if (target == null || !target.isNPC) return;
                if (target.GetComponent<MiningNode>() == null) return;

                string scene = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name;
                int tier = GetZoneTier(scene);
                if (tier <= 0) return;

                // Check player level — must be appropriate level for zone tier
                int playerLevel = 1;
                try { playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1); }
                catch { }
                int[] requiredLevels = { 0, 1, 5, 12, 18, 25, 30 };
                int reqLvl = tier < requiredLevels.Length ? requiredLevels[tier] : 30;
                if (playerLevel < reqLvl)
                    tier = 1; // Downgrade to T1 if underleveled

                var inv = GameData.PlayerInv;
                if (inv == null) return;

                // 40% bonus ore
                if (tier < TierOres.Length && TierOres[tier].Length > 0 &&
                    UnityEngine.Random.value <= 0.40f)
                    GiveBonus(inv, TierOres[tier][UnityEngine.Random.Range(0, TierOres[tier].Length)]);

                // 25% gem
                if (tier < TierGems.Length && TierGems[tier].Length > 0 &&
                    UnityEngine.Random.value <= 0.25f)
                    GiveBonus(inv, TierGems[tier][UnityEngine.Random.Range(0, TierGems[tier].Length)]);

                // 15% refined metal (tier 2+)
                if (tier >= 2 && tier < TierMetals.Length && TierMetals[tier].Length > 0 &&
                    UnityEngine.Random.value <= 0.15f)
                    GiveBonus(inv, TierMetals[tier][UnityEngine.Random.Range(0, TierMetals[tier].Length)]);

                // 5% endgame (tier 5)
                if (tier >= 5 && UnityEngine.Random.value <= 0.05f)
                    GiveBonus(inv, EndgameMining[UnityEngine.Random.Range(0, EndgameMining.Length)]);
            }
            catch { }
        }

        private static void GiveBonus(Inventory inv, string name)
        {
            var item = Items.ItemFactory.GetItem(name);
            if (item == null) return;
            if (!inv.AddItemToInv(item)) inv.ForceItemToInv(item);
            ChatHelper.Send($"<color=#FFD700>[Mining]</color> Bonus find: <color=#FFFFFF>{name}</color>!");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MINING NODE DROPS — Custom ores, gems, and metals from mining
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(MiningNode), "Mine")]
    public static class Patch_MiningNodeDrop
    {
        public static void Postfix(MiningNode __instance, Item __result)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                int tier = GetZoneTier(scene);
                if (tier <= 0) return;

                // Check player level — must be appropriate level for the zone tier
                int playerLevel = 1;
                try { playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1); }
                catch { }
                int requiredLevel = GetRequiredLevel(tier);
                if (playerLevel < requiredLevel)
                {
                    // Player too low level — only drop T1 materials regardless of zone
                    tier = 1;
                }

                if (UnityEngine.Random.value > 0.60f) return;
                string drop = RollMiningDrop(tier);
                if (string.IsNullOrEmpty(drop)) return;
                var item = Items.ItemFactory.GetItem(drop);
                if (item == null) return;
                var inv = GameData.PlayerInv;
                if (inv != null)
                {
                    if (!inv.AddItemToInv(item))
                        inv.ForceItemToInv(item);
                }
                ChatHelper.Send($"<color=#FFD700>[Mining]</color> You also find: <color=#FFFFFF>{drop}</color>");
            }
            catch { }
        }

        /// <summary>
        /// Minimum player level required for each mining tier.
        /// Prevents low-level players from farming high-tier materials.
        /// </summary>
        private static int GetRequiredLevel(int tier)
        {
            switch (tier)
            {
                case 1: return 1;   // Stowaway — anyone
                case 2: return 5;   // Low-mid zones
                case 3: return 12;  // Mid zones (Azure, etc.)
                case 4: return 18;  // Mid-high zones
                case 5: return 25;  // High zones
                case 6: return 30;  // Endgame zones
                default: return 1;
            }
        }

        private static int GetZoneTier(string scene)
        {
            switch (scene)
            {
                case "Stowaway": case "Tutorial": return 1;
                case "FernallaField": case "Hidden": case "Brake": return 2;
                case "Azure": case "Windwashed": case "Silkengrass": return 3;
                case "Braxonian": case "Elderstone": case "Malaroth": return 4;
                case "Soluna": case "Ripper": case "Bonepits":
                case "Vitheo": case "VitheosEnd": case "SaltedStrand": return 5;
                case "Abyssal": case "PrielPlateau": case "Azynthi":
                case "AzynthiClear": case "ShiveringStep":
                case "Loomingwood": case "Rottenfoot": case "Krakengard": return 6;
                default: return 2;
            }
        }

        private static string RollMiningDrop(int tier)
        {
            float roll = UnityEngine.Random.value;
            if (roll < 0.50f) return RollCommonOre(tier);
            if (roll < 0.75f) return RollRareOreOrGem(tier);
            if (roll < 0.90f) return RollRefinedMetal(tier);
            return RollEndgame(tier);
        }

        private static string RollCommonOre(int tier)
        {
            switch (tier)
            {
                case 1: return "Chunk of Copper Ore";
                case 2: return UnityEngine.Random.value < 0.5f ? "Chunk of Copper Ore" : "Chunk of Iron Ore";
                case 3: return UnityEngine.Random.value < 0.5f ? "Chunk of Iron Ore" : "Coal";
                case 4: return UnityEngine.Random.value < 0.5f ? "Chunk of Silver Ore" : "Chunk of Iron Ore";
                case 5: return UnityEngine.Random.value < 0.5f ? "Chunk of Adamantite Ore" : "Chunk of Platinum Ore";
                case 6: return UnityEngine.Random.value < 0.5f ? "Chunk of Adamantite Ore" : "Chunk of Gold Ore";
                default: return "Chunk of Copper Ore";
            }
        }

        private static string RollRareOreOrGem(int tier)
        {
            string[][] pools = {
                new[] { "Coal", "Smooth Pebble" },
                new[] { "Coal", "Rough Geode", "Chunk of Silver Ore" },
                new[] { "Chunk of Silver Ore", "Rough Ruby", "Rough Emerald", "Moonstone Pebble" },
                new[] { "Chunk of Gold Ore", "Rough Sapphire", "Bloodstone Chip", "Glimmering Geode Fragment" },
                new[] { "Chunk of Platinum Ore", "Flawless Diamond", "Abyssal Opal", "Starshard Crystal" },
                new[] { "Prismatic Jewel", "Flawless Diamond", "Starshard Crystal", "Abyssal Opal" },
            };
            var pool = pools[Mathf.Clamp(tier - 1, 0, pools.Length - 1)];
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private static string RollRefinedMetal(int tier)
        {
            if (tier <= 2) return RollCommonOre(tier);
            string[][] pools = {
                new[] { "Void Iron Chunk" },
                new[] { "Void Iron Chunk", "Soulsteel Billet" },
                new[] { "Soulsteel Billet", "Moonsilver Bar", "Sunforged Ingot" },
                new[] { "Starmetal Fragment", "Moonsilver Bar", "Sunforged Ingot", "Soulsteel Billet" },
            };
            var pool = pools[Mathf.Clamp(tier - 3, 0, pools.Length - 1)];
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private static string RollEndgame(int tier)
        {
            if (tier <= 4) return RollRareOreOrGem(tier);
            if (tier == 5) { string[] p = { "Worldtree Splinter", "Petrified Heartwood" }; return p[UnityEngine.Random.Range(0, p.Length)]; }
            string[] p6 = { "Crystallized Time", "Essence of Eternity", "Worldtree Splinter", "Starmetal Fragment" };
            return p6[UnityEngine.Random.Range(0, p6.Length)];
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENDGAME MATERIAL DROPS FROM POWERFUL MOBS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drop endgame crafting materials (Thread of Fate, Essence of Eternity,
    /// Crystallized Time) from boss/named mobs in high-level zones.
    /// Hooks LootTable.LoadLootTable alongside existing recipe/meat drops.
    /// </summary>
    [HarmonyPatch(typeof(LootTable), "LoadLootTable")]
    public static class Patch_EndgameMaterialDrop
    {
        public static void Prefix(LootTable __instance)
        {
            if (!SkillsSaveManager.HasLoaded) return;
            try
            {
                var character = __instance.GetComponent<Character>();
                if (character == null) return;
                int mobLevel = 0;
                bool isBoss = false;
                try
                {
                    mobLevel = character.MyStats?.Level ?? 0;
                    isBoss = character.BossXp > 1f;
                }
                catch { }

                // Only high-level mobs (25+) and bosses drop endgame materials
                if (mobLevel < 25) return;

                // Boss: 20% chance, Named/high: 5% chance
                float chance = isBoss ? 0.20f : 0.05f;
                if (UnityEngine.Random.value > chance) return;

                string[] endgameDrops;
                if (mobLevel >= 30 && isBoss)
                    endgameDrops = new[] { "Thread of Fate", "Essence of Eternity", "Crystallized Time" };
                else if (mobLevel >= 30)
                    endgameDrops = new[] { "Crystallized Time", "Worldtree Splinter", "Essence of Eternity" };
                else
                    endgameDrops = new[] { "Worldtree Splinter", "Petrified Heartwood" };

                string drop = endgameDrops[UnityEngine.Random.Range(0, endgameDrops.Length)];
                var item = Items.ItemFactory.GetItem(drop);
                if (item == null) return;

                if (__instance.ActualDrops.Count < 8)
                    __instance.ActualDrops.Add(item);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BAG SPLIT PROTECTION — Prevent stack split from corrupting bag IDs
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prevent the game's stack-split (Ctrl+click, Shift+click) from modifying
    /// bag item Quantity, which stores our permanent bag instance ID.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "InteractItemSlot")]
    public static class Patch_PreventBagSplit
    {
        public static bool Prefix(ItemIcon __instance)
        {
            try
            {
                if (__instance?.MyItem == null) return true;
                if (!Items.BagSystem.IsBag(__instance.MyItem.ItemName)) return true;
                // If a split key is held, block the interaction entirely
                if (Input.GetKey(InputManager.SplitOne) || Input.GetKey(InputManager.SplitTen))
                    return false;
            }
            catch { }
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BAG ITEM STORAGE — Right-click items to store in open bag
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercept right-clicks on inventory items when a bag window is open.
    /// If a bag is open and the player right-clicks a non-bag item,
    /// store it in the bag instead of the normal action.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "OnPointerUp")]
    public static class Patch_BagStoreOnRightClick
    {
        public static bool Prefix(ItemIcon __instance, UnityEngine.EventSystems.PointerEventData eventData)
        {
            try
            {
                // Only intercept right-clicks
                if (eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                    return true;
                // Only when bag window is open
                if (!Items.BagWindow.AnyOpen) return true;
                // Only for inventory slots (not vendor, loot, equipment)
                if (__instance.VendorSlot || __instance.LootSlot) return true;
                // Skip empty slots
                if (__instance.MyItem == null || __instance.MyItem == GameData.PlayerInv.Empty)
                    return true;
                // Skip bag items (those open the bag instead)
                if (Items.BagSystem.IsBag(__instance.MyItem.ItemName)) return true;
                // Skip equipment slots
                if (GameData.PlayerInv.EquipmentSlots != null &&
                    GameData.PlayerInv.EquipmentSlots.Contains(__instance))
                    return true;

                // Store the item in the open bag
                if (Items.BagSystem.TryStoreFromInventorySlot(__instance))
                    return false; // Block normal right-click behavior
            }
            catch { }
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FOOD TOLERANCE — XP on consume + duration bonus
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award Food Tolerance XP whenever any consumable is used.
    /// Hooks the game's UseConsumable on ItemIcon.
    /// </summary>
    [HarmonyPatch(typeof(ItemIcon), "UseConsumable")]
    public static class Patch_FoodToleranceXP
    {
        public static void Postfix(ItemIcon __instance)
        {
            try
            {
                if (!SkillsSaveManager.HasLoaded) return;
                var item = __instance.MyItem;
                if (item == null || item.ItemEffectOnClick == null) return;
                // Skip bags
                if (Items.BagSystem.IsBag(item.ItemName)) return;

                Skills.FoodToleranceSkill.OnConsumeItem(item.ItemName);
            }
            catch { }
        }
    }

    /// <summary>
    /// Extend buff duration based on Food Tolerance skill.
    /// Only applies to beneficial self-buffs (food/drink effects).
    /// Hooks AddStatusEffect with the duration parameter.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffect",
        new System.Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character), typeof(float) })]
    public static class Patch_FoodToleranceDuration
    {
        public static void Prefix(Stats __instance, Spell spell, bool _fromPlayer,
            ref float _duration)
        {
            try
            {
                if (!SkillsSaveManager.HasLoaded) return;
                // Only modify player's own buffs
                if (__instance.Myself == null || __instance.Myself.isNPC) return;
                if (spell == null) return;
                // Only extend self-targeted beneficial buffs (food/drink effects)
                // Don't filter on SpellType — game food may use Misc, StatusEffect, etc.
                if (!spell.SelfOnly && !spell.InflictOnSelf) return;
                // Skip damage spells
                if (spell.Type == Spell.SpellType.Damage || spell.Type == Spell.SpellType.AE
                    || spell.Type == Spell.SpellType.PBAE) return;

                float bonus = Skills.FoodToleranceSkill.DurationBonus;
                if (bonus > 0f)
                {
                    float mult = 1f + bonus / 100f;
                    _duration = _duration * mult;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Hook the 4-parameter AddStatusEffect which is what the spell system
    /// actually calls for food/drink buffs. Temporarily increase
    /// spell.SpellDurationInTicks, then restore in Postfix.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "AddStatusEffect",
        new System.Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    public static class Patch_FoodToleranceDuration4
    {
        private static int _savedDuration = -1;
        private static Spell _savedSpell = null;

        public static void Prefix(Stats __instance, Spell spell)
        {
            try
            {
                if (!SkillsSaveManager.HasLoaded) return;
                if (__instance.Myself == null || __instance.Myself.isNPC) return;
                if (spell == null) return;
                if (!spell.SelfOnly && !spell.InflictOnSelf) return;
                if (spell.Type == Spell.SpellType.Damage || spell.Type == Spell.SpellType.AE
                    || spell.Type == Spell.SpellType.PBAE) return;

                float bonus = Skills.FoodToleranceSkill.DurationBonus;
                if (bonus > 0f)
                {
                    float mult = 1f + bonus / 100f;
                    _savedSpell = spell;
                    _savedDuration = spell.SpellDurationInTicks;
                    spell.SpellDurationInTicks = Mathf.RoundToInt(
                        spell.SpellDurationInTicks * mult);
                }
            }
            catch { }
        }

        public static void Postfix()
        {
            // Restore original duration so the spell ScriptableObject isn't permanently modified
            if (_savedSpell != null && _savedDuration >= 0)
            {
                _savedSpell.SpellDurationInTicks = _savedDuration;
                _savedSpell = null;
                _savedDuration = -1;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════════
    // TOOLTIP INJECTION — Add category + weapon skill type to item tooltips
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Postfix on ItemInfoWindow.DisplayItem to inject extra info:
    /// - Food/Drink category for our consumables
    /// - Weapon skill type (1H Slashing, Piercing, etc.) for ALL weapons
    /// </summary>
    [HarmonyPatch(typeof(ItemInfoWindow), "DisplayItem")]
    public static class Patch_TooltipInject
    {
        public static void Postfix(ItemInfoWindow __instance, Item item, int _quantity)
        {
            try
            {
                if (item == null) return;
                string extra = "";

                // 1. Food/Drink category — already in Lore at registration, skip duplicate

                // 2. Weapon skill type for ANY weapon (game or custom)
                // Check WeaponDmg OR WeaponDly OR RequiredSlot is Primary/Secondary
                bool isWeapon = item.WeaponDmg > 0 || item.WeaponDly > 0 ||
                    item.RequiredSlot == Item.SlotType.Primary ||
                    item.RequiredSlot == Item.SlotType.Secondary;
                if (isWeapon)
                {
                    // Determine if 2-handed by checking the inventory state or item name
                    bool isTwoHand = false;
                    try { isTwoHand = GameData.PlayerInv != null && GameData.PlayerInv.TwoHandPrimary; }
                    catch { }
                    // Also check common 2H indicators in the name
                    string lower = item.ItemName.ToLower();
                    if (lower.Contains("great") || lower.Contains("staff") ||
                        lower.Contains("halberd") || lower.Contains("claymore") ||
                        lower.Contains("maul") || lower.Contains("pole") ||
                        lower.Contains("totem") || lower.Contains("warbow") ||
                        lower.Contains("longbow"))
                        isTwoHand = true;

                    var wtype = Skills.CombatSkills.ClassifyWeapon(
                        item.ItemName, isTwoHand);
                    if (wtype != Skills.CombatSkills.WeaponType.Unknown)
                    {
                        string typeName = Skills.CombatSkills.GetTypeName(wtype);
                        extra += $"\n<color=#FF9800>Skill: {typeName}</color>";
                    }
                }

                // Append to Lore text if we have extra info
                if (!string.IsNullOrEmpty(extra))
                {
                    __instance.Lore.text += extra;
                }
            }
            catch { }
        }
    }

    // CHAT COMMANDS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Add chat commands for all skills:
    /// /skills - Overview of all skill levels
    /// /fishskill, /foraging, /swimming, etc. - Individual skill info
    /// /forage - Attempt to forage (alias for keybind)
    /// /bindwound - Attempt to bind wounds
    /// /beg - Attempt to beg
    /// /meditate - Toggle meditation
    /// /senseheading - Sense heading
    /// </summary>
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    public static class Patch_ChatCommands
    {
        public static bool Prefix(TypeText __instance)
        {
            try
            {
                // typed is a UI input field component, not a string
                // Access it the same way ErenshorQoL does: __instance.typed.text
                string raw = __instance.typed.text;

                if (string.IsNullOrEmpty(raw)) return true;
                string cmd = raw.Trim().ToLower();
                if (string.IsNullOrEmpty(cmd)) return true;

                // ── /skills — Master overview ────────────────────────
                if (cmd == "/skills" || cmd == "/skilllist")
                {
                    ShowSkillOverview();
                    return false;
                }

                // ── /scanobjects — Debug: list game objects matching keywords ──
                if (cmd.StartsWith("/scanobjects"))
                {
                    string filter = cmd.Length > 13 ? cmd.Substring(13).Trim().ToLower() : "";
                    string[] keywords = string.IsNullOrEmpty(filter)
                        ? new[] { "oven", "brew", "brewing", "barrel", "bench", "fire", "loom", "forge" }
                        : new[] { filter };
                    var allObjs = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>();
                    int count = 0;
                    ChatHelper.Send("<color=#00FF00>[Scan]</color> Searching game objects...");
                    foreach (var obj in allObjs)
                    {
                        if (obj == null) continue;
                        string lower = obj.name.ToLower();
                        foreach (var kw in keywords)
                        {
                            if (lower.Contains(kw))
                            {
                                var pos = obj.transform.position;
                                bool hasCol = obj.GetComponent<UnityEngine.Collider>() != null;
                                ChatHelper.Send($"<color=#AAAAAA>[{(hasCol ? "COL" : "---")}]</color> " +
                                    $"<color=#FFFFFF>{obj.name}</color> " +
                                    $"<color=#888888>({pos.x:F1}, {pos.y:F1}, {pos.z:F1})</color>");
                                count++;
                                break;
                            }
                        }
                    }
                    ChatHelper.Send($"<color=#00FF00>[Scan]</color> Found {count} objects.");
                    return false;
                }

                // ── /givebag — Debug: give a bag item ───────────────
                if (cmd.StartsWith("/givebag"))
                {
                    string bagName = cmd.Length > 9 ? cmd.Substring(9).Trim() : "";
                    if (string.IsNullOrEmpty(bagName))
                    {
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Bags: Silken Pouch, Herbalist's Satchel, Simple Backpack, Adventurer's Backpack");
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Usage: /givebag <name>");
                        return false;
                    }
                    // Match partial name
                    string[] bags = { "Silken Pouch", "Herbalist's Satchel", "Simple Backpack", "Adventurer's Backpack" };
                    string matched = null;
                    foreach (var b in bags)
                        if (b.ToLower().Contains(bagName.ToLower())) { matched = b; break; }
                    if (matched == null)
                    {
                        ChatHelper.Send($"<color=#FF6666>[Debug]</color> Unknown bag: {bagName}");
                        return false;
                    }
                    if (Items.ItemFactory.GiveItemToPlayer(matched))
                        ChatHelper.Send($"<color=#00FF00>[Debug]</color> Gave you: {matched}");
                    else
                        ChatHelper.Send($"<color=#FF6666>[Debug]</color> Failed to give: {matched}");
                    return false;
                }

                // ── /givetp — Debug: grant training points ────────────
                if (cmd.StartsWith("/givetp"))
                {
                    string numStr = cmd.Length > 8 ? cmd.Substring(8).Trim() : "10";
                    int amount = 10;
                    int.TryParse(numStr, out amount);
                    // Decrease spent so the recalculation gives more TP
                    SkillsSaveManager.Data.TrainingPointsSpent =
                        Mathf.Max(0, SkillsSaveManager.Data.TrainingPointsSpent - amount);
                    SkillsSaveManager.Data.CheckForNewTrainingPoints();
                    ChatHelper.Send(
                        $"<color=#00FF00>[Debug]</color> Granted {amount} training points. " +
                        $"Total: {SkillsSaveManager.Data.TrainingPoints}");
                    SkillsSaveManager.Save();
                    return false;
                }

                // ── /learnrecipe — Debug: learn a recipe by name ─────
                if (cmd.StartsWith("/learnrecipe"))
                {
                    string rname = cmd.Length > 13 ? cmd.Substring(13).Trim() : "";
                    if (string.IsNullOrEmpty(rname))
                    {
                        ChatHelper.Send("<color=#00FF00>[Debug]</color> Usage: /learnrecipe <recipe name>");
                        return false;
                    }
                    if (rname.ToLower() == "all")
                    {
                        int learned = 0;
                        string[] tsList = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                        foreach (var ts in tsList)
                        {
                            var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                            foreach (var r in recipes)
                                if (SkillsSaveManager.Data.LearnRecipe(r.Name)) learned++;
                        }
                        SkillsSaveManager.SaveRecipesFile();
                        ChatHelper.Send($"<color=#00FF00>[Debug]</color> Learned {learned} new recipes.");
                        return false;
                    }
                    // Find recipe by partial name
                    string[] allTS = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                    foreach (var ts in allTS)
                    {
                        var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                        foreach (var r in recipes)
                        {
                            if (r.Name.ToLower().Contains(rname.ToLower()))
                            {
                                if (SkillsSaveManager.Data.LearnRecipe(r.Name))
                                {
                                    SkillsSaveManager.SaveRecipesFile();
                                    ChatHelper.Send($"<color=#FFD700>[Skills]</color> Learned recipe: <color=#FFFFFF>{r.Name}</color> ({ts})");
                                }
                                else
                                    ChatHelper.Send($"<color=#AAAAAA>[Skills]</color> You already know {r.Name}.");
                                return false;
                            }
                        }
                    }
                    ChatHelper.Send($"<color=#FF6666>[Skills]</color> No recipe found matching: {rname}");
                    return false;
                }

                // /buyrecipe removed — use recipe trainer NPCs instead
                if (cmd.StartsWith("/buyrecipe"))
                {
                    ChatHelper.Send("<color=#FF9800>[Skills]</color> Visit a Recipe Trainer NPC to buy recipes.");
                    return false;
                }
                // ── /recipes — Show known recipe count ────────────────
                if (cmd == "/recipes")
                {
                    var data = SkillsSaveManager.Data;
                    int total = 0;
                    string[] tsList = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
                    foreach (var ts in tsList)
                        total += Skills.Tradeskills.GetRecipesForSkillPublic(ts).Count;
                    ChatHelper.Send(
                        $"<color=#FFD700>[Skills]</color> Known recipes: " +
                        $"<color=#FFFFFF>{data.KnownRecipes.Count}</color> / {total}");
                    return false;
                }

                // ── /train <skillname> ──────────────────────────────
                if (cmd.StartsWith("/train"))
                {
                    string skillName = cmd.Length > 7 ? cmd.Substring(7).Trim() : "";
                    if (string.IsNullOrEmpty(skillName))
                    {
                        var data = SkillsSaveManager.Data;
                        ChatHelper.Send(
                            $"<color=#FFD700>[Skills]</color> " +
                            $"Training points: <color=#00FF00>{data.TrainingPoints}</color>. " +
                            $"Usage: /train <skill name>");
                    }
                    else
                    {
                        var data = SkillsSaveManager.Data;
                        // Use FindSkillByName to get the ACTUAL field reference
                        SkillEntry match = data.FindSkillByName(skillName);
                        if (match == null)
                            ChatHelper.Send($"<color=#FF6666>[Skills]</color> Unknown skill: {skillName}");
                        else if (data.TrainingPoints <= 0)
                            ChatHelper.Send("<color=#FF6666>[Skills]</color> No training points available.");
                        else if (match.IsAtCap)
                            ChatHelper.Send($"<color=#FF6666>[Skills]</color> {match.Name} is at its skill cap.");
                        else if (data.SpendTrainingPoint(match))
                        {
                            ChatHelper.Send(
                                $"<color=#FFD700>[Skills]</color> " +
                                $"Trained <color=#FFFFFF>{match.Name}</color> to level {match.Level}! " +
                                $"<color=#AAAAAA>({data.TrainingPoints} TP remaining)</color>");
                            SkillsSaveManager.Save();
                        }
                    }
                    return false;
                }

                // ── Individual skill info ────────────────────────────
                if (cmd == "/fishskill" || cmd == "/fishing")
                { ShowSkillDetail(SkillsSaveManager.Data.Fishing, "#7EC8E3"); return false; }
                if (cmd == "/foraging" || cmd == "/forageskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Foraging, "#8BC34A"); return false; }
                if (cmd == "/swimming" || cmd == "/swimskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Swimming, "#4FC3F7"); return false; }

                if (cmd == "/bindwoundskill")
                { ShowSkillDetail(SkillsSaveManager.Data.BindWound, "#EF5350"); return false; }
                if (cmd == "/meditateskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Meditate, "#CE93D8"); return false; }

                if (cmd == "/toleranceskill" || cmd == "/foodtolerance")
                { ShowSkillDetail(SkillsSaveManager.Data.FoodTolerance, "#FFAB40"); return false; }
                if (cmd == "/beggingskill")
                { ShowSkillDetail(SkillsSaveManager.Data.Begging, "#BCAAA4"); return false; }

                // ── Combat skill commands ────────────────────────────
                if (cmd == "/weaponskills" || cmd == "/combat" || cmd == "/combatskills")
                {
                    ShowCombatSkillOverview();
                    return false;
                }

                // ── Action commands (aliases for keybinds) ───────────
                if (cmd == "/forage")
                { Skills.ForagingSkill.TryForage(); return false; }
                if (cmd == "/bindwound" || cmd == "/bandage")
                { Skills.BindWoundSkill.TryBindWound(); return false; }
                if (cmd == "/beg")
                { Skills.BeggingSkill.TryBeg(); return false; }
                if (cmd == "/meditate" || cmd == "/med")
                { Skills.MeditateSkill.ToggleMeditate(); return false; }


                // ── /dumpdb — Export game databases to CSV ────────
                if (cmd == "/dumpdb")
                { DatabaseDumper.DumpAll(); return false; }

                // ── /iconcheck — List items missing game icons ───────
                if (cmd == "/iconcheck")
                { Items.ItemFactory.ReportMissingIcons(); return false; }

                // ── Tradeskill commands — open crafting windows ─────
                string[] tradeskillNames = { "Smithing", "Baking", "Brewing",
                    "Fletching", "Jewelcraft", "Tailoring" };

                foreach (var ts in tradeskillNames)
                {
                    string prefix = "/" + ts.ToLower();
                    // /smithing or /smith opens the window
                    if (cmd == prefix || cmd == prefix.Substring(0, Math.Min(prefix.Length, 6)))
                    {
                        UI.TradeskillWindow.Open(ts);
                        return false;
                    }
                    // /smithing list still works for chat-only users
                    if (cmd == prefix + " list")
                    {
                        Skills.Tradeskills.ListRecipes(ts);
                        return false;
                    }
                    // /smithing make <name> still works as a text shortcut
                    if (cmd.StartsWith(prefix + " make "))
                    {
                        string recipeName = __instance.typed.text.Trim()
                            .Substring((prefix + " make ").Length).Trim();
                        Skills.Tradeskills.AttemptCombine(ts, recipeName);
                        return false;
                    }
                }

                // Short aliases: /smith, /bake, /brew, /fletch, /jewel, /tailor
                if (cmd == "/smith" || cmd == "/forge")
                { UI.TradeskillWindow.Open("Smithing"); return false; }
                if (cmd == "/bake" || cmd == "/cook")
                { UI.TradeskillWindow.Open("Baking"); return false; }
                if (cmd == "/brew")
                { UI.TradeskillWindow.Open("Brewing"); return false; }
                if (cmd == "/fletch")
                { UI.TradeskillWindow.Open("Fletching"); return false; }
                if (cmd == "/jewel" || cmd == "/jewelry")
                { UI.TradeskillWindow.Open("Jewelcraft"); return false; }
                if (cmd == "/tailor" || cmd == "/sew")
                { UI.TradeskillWindow.Open("Tailoring"); return false; }

                if (cmd == "/tradeskills" || cmd == "/trades")
                {
                    ShowTradeskillOverview();
                    return false;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Chat command error: {ex}");
            }
            return true;
        }

        /// <summary>Gold cost for a recipe based on its trivial level.</summary>
        private static int GetRecipeCost(int trivialLevel)
        {
            if (trivialLevel <= 20) return 10;
            if (trivialLevel <= 40) return 50;
            if (trivialLevel <= 60) return 150;
            if (trivialLevel <= 80) return 400;
            if (trivialLevel <= 100) return 800;
            if (trivialLevel <= 120) return 1500;
            if (trivialLevel <= 140) return 3000;
            if (trivialLevel <= 160) return 6000;
            return 10000; // 161-180 endgame
        }

        private static void ShowSkillOverview()
        {
            var data = SkillsSaveManager.Data;
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            ChatHelper.Send(
                $"<color=#FFD700>══════ Utility Skills ══════</color>");

            foreach (var s in data.GetUtilitySkills())
            {
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{s.Name,-20}</color> " +
                    $"Lv {lvl}/{max} {bar} " +
                    $"<color=#AAAAAA>(Used {s.TimesUsed}x)</color>");
            }

            ChatHelper.Send(
                $"<color=#FF6E40>══════ Combat Skills ═══════</color>");

            foreach (var entry in Skills.CombatSkills.GetActiveCombatSkills())
            {
                var s = entry.Skill;
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";
                string bonus = entry.Bonus > 0.01f
                    ? $" <color=#FF6E40>(+{entry.Bonus:F1}% dmg)</color>"
                    : "";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{entry.Name,-20}</color> " +
                    $"Lv {lvl}/{max} {bar}{bonus}");
            }

            ChatHelper.Send(
                $"<color=#FFD700>════════════════════════════</color>");
            ChatHelper.Send(
                $"<color=#AAAAAA>Press {SkillsPlugin.CfgSkillWindowKey.Value} " +
                $"for the full skill window. /weaponskills for combat details.</color>");
        }

        private static void ShowCombatSkillOverview()
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            ChatHelper.Send(
                $"<color=#FF6E40>═══ Combat Weapon Skills ═══</color>");

            foreach (var entry in Skills.CombatSkills.GetActiveCombatSkills())
            {
                var s = entry.Skill;
                string bar = MiniBar(s.LevelProgress, 12);
                string bonus = entry.Bonus > 0.01f
                    ? $"<color=#FF6E40>+{entry.Bonus:F1}% damage</color>"
                    : "<color=#AAAAAA>no bonus yet</color>";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{entry.Name,-16}</color> " +
                    $"Lv {s.Level}/{max} {bar} {bonus} " +
                    $"<color=#666666>({s.TimesUsed} hits)</color>");
            }

            ChatHelper.Send(
                $"<color=#AAAAAA>Damage bonus: " +
                $"+{SkillsPlugin.CfgCombatSkillDamagePerLevel.Value:F1}% per level " +
                $"(max +{SkillsPlugin.CfgCombatSkillDamagePerLevel.Value * max:F0}% at level {max})</color>");
            ChatHelper.Send(
                $"<color=#FF6E40>════════════════════════════</color>");
        }

        private static void ShowTradeskillOverview()
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;
            var data = SkillsSaveManager.Data;

            ChatHelper.Send(
                $"<color=#FF9800>═══ Tradeskills ═══</color>");

            foreach (var s in data.GetTradeskills())
            {
                string bar = MiniBar(s.LevelProgress, 10);
                string lvl = s.IsMaxLevel
                    ? $"<color=#FFD700>{s.Level}</color>"
                    : $"{s.Level}";

                ChatHelper.Send(
                    $"  <color=#FFFFFF>{s.Name,-16}</color> " +
                    $"Lv {lvl}/{max} {bar} " +
                    $"<color=#AAAAAA>({s.Successes} crafted | {s.Failures} failed)</color>");
            }

            ChatHelper.Send(
                $"<color=#AAAAAA>Use /<tradeskill> list to see recipes. " +
                $"/<tradeskill> make <name> to craft.</color>");
            ChatHelper.Send(
                $"<color=#FF9800>═══════════════════</color>");
        }

        private static void ShowSkillDetail(SkillEntry skill, string color)
        {
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;
            ChatHelper.Send(
                $"<color={color}>═══ {skill.Name} ═══</color>");
            ChatHelper.Send(
                $"<color={color}>Level:</color> {skill.Level}/{max}");
            if (!skill.IsMaxLevel)
                ChatHelper.Send(
                    $"<color={color}>XP:</color> " +
                    $"{skill.CurrentXp:F0}/{skill.XpToNextLevel:F0} " +
                    $"({skill.LevelProgress * 100f:F1}%)");
            ChatHelper.Send(
                $"<color={color}>Used:</color> {skill.TimesUsed} times " +
                $"| {skill.Successes} successes " +
                $"| {skill.Failures} failures");
            ChatHelper.Send(
                $"<color={color}>═══════════════════</color>");
        }

        private static string MiniBar(float progress, int width)
        {
            int filled = Mathf.RoundToInt(progress * width);
            string result = "[";
            for (int i = 0; i < width; i++)
                result += (i < filled) ? "█" : "░";
            result += "]";
            return result;
        }
    }

    /// <summary>
    /// Apply combat skill damage bonus to melee damage calculations.
    /// Multiplies the damage result by the player's weapon skill bonus.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "CalcMeleeDamage")]
    public static class Patch_CombatDamageBonus_Melee
    {
        public static void Postfix(Stats __instance, ref int __result)
        {
            try
            {
                if (__result <= 0) return; // Don't modify misses
                if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;

                // Only apply to the player, not NPCs
                if (__instance.Myself == null) return;
                var pc = GameData.PlayerControl;
                if (pc == null || __instance.Myself != pc) return;

                // Get equipped weapon name
                string weaponName = __instance.MyInv?.MH?.MyItem?.ItemName;
                if (string.IsNullOrEmpty(weaponName)) return;

                bool isTwoHand = __instance.MyInv != null && __instance.MyInv.TwoHandPrimary;
                var wtype = Skills.CombatSkills.ClassifyWeapon(weaponName, isTwoHand);
                if (wtype == Skills.CombatSkills.WeaponType.Unknown) return;

                float mult = Skills.CombatSkills.GetDamageMultiplier(wtype);
                if (mult > 1f)
                    __result = Mathf.RoundToInt(__result * mult);
            }
            catch { }
        }
    }

    /// <summary>
    /// Apply combat skill damage bonus to bow/archery damage calculations.
    /// </summary>
    [HarmonyPatch(typeof(Stats), "CalcBowDamage")]
    public static class Patch_CombatDamageBonus_Bow
    {
        public static void Postfix(Stats __instance, ref int __result)
        {
            try
            {
                if (__result <= 0) return;
                if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;

                if (__instance.Myself == null) return;
                var pc = GameData.PlayerControl;
                if (pc == null || __instance.Myself != pc) return;

                float mult = Skills.CombatSkills.GetDamageMultiplier(
                    Skills.CombatSkills.WeaponType.Archery);
                if (mult > 1f)
                    __result = Mathf.RoundToInt(__result * mult);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — XP on spell cast
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Award magic skill XP when the player casts a spell.
    /// Hooks CastSpell.StartSpell — fires when a spell begins casting.
    /// Classifies the spell by school and awards XP to the matching skill.
    /// </summary>
    [HarmonyPatch(typeof(CastSpell), "StartSpell",
        new System.Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    public static class Patch_MagicSkillXP
    {
        public static void Postfix(CastSpell __instance, Spell _spell, bool __result)
        {
            if (!__result) return;
            if (!__instance.isPlayer) return;
            try { Skills.MagicSkills.OnSpellCast(_spell); }
            catch { }
        }
    }

    /// <summary>
    /// Also hook the 2-param StartSpell overload — this is what the hotbar uses.
    /// </summary>
    [HarmonyPatch(typeof(CastSpell), "StartSpell",
        new System.Type[] { typeof(Spell), typeof(Stats) })]
    public static class Patch_MagicSkillXP2
    {
        public static void Postfix(CastSpell __instance, Spell _spell, bool __result)
        {
            if (!__result) return;
            if (!__instance.isPlayer) return;
            try { Skills.MagicSkills.OnSpellCast(_spell); }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — Evocation bonus (spell damage)
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(Character), "MagicDamageMe")]
    public static class Patch_EvocationDamage
    {
        public static void Prefix(ref int _dmg, Character _source)
        {
            try
            {
                if (_source == null || _source.isNPC) return;
                if (_source != GameData.PlayerControl?.Myself) return;
                if (_dmg <= 0) return;

                // Determine which magic school applies based on the current/last spell
                float mult = 1f;
                var spells = _source.MySpells;
                Spell current = null;
                try { current = spells?.GetCurrentCast(); } catch { }

                if (current != null)
                {
                    var school = Skills.MagicSkills.ClassifySpell(current);
                    if (school == Skills.MagicSkills.MagicSchool.Evocation)
                        mult = Skills.MagicSkills.GetSpellDamageMultiplier();
                    else if (school == Skills.MagicSkills.MagicSchool.Conjuration)
                        mult = Skills.MagicSkills.GetConjurationMultiplier();
                }
                else
                {
                    // No active cast = DoT tick or proc, use Conjuration
                    mult = Skills.MagicSkills.GetConjurationMultiplier();
                }

                if (mult > 1f)
                    _dmg = Mathf.RoundToInt(_dmg * mult);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MAGIC SKILLS — Alteration bonus (heal amount)
    // ═══════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(Stats), "HealMe")]
    public static class Patch_AlterationHeal
    {
        public static void Prefix(ref int _incomingHeal)
        {
            try
            {
                if (_incomingHeal <= 0) return;
                // Boost heals when the player is casting a heal spell
                var playerSpells = GameData.PlayerControl?.Myself?.MySpells;
                if (playerSpells != null && playerSpells.isCasting())
                {
                    var current = playerSpells.GetCurrentCast();
                    if (current != null && current.Type == Spell.SpellType.Heal)
                    {
                        float mult = Skills.MagicSkills.GetHealMultiplier();
                        if (mult > 1f)
                            _incomingHeal = Mathf.RoundToInt(_incomingHeal * mult);
                    }
                }
            }
            catch { }
        }
    }
}
