using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace ErenshorSkills
{
    // ═════════════════════════════════════════════════════════════════════
    // Individual skill data
    // ═════════════════════════════════════════════════════════════════════

    [Serializable]
    public class SkillEntry
    {
        public string Name = "";
        public int Level = 0;  // Start at 0 = untrained
        public float CurrentXp = 0f;
        public int TimesUsed = 0;
        public int Successes = 0;
        public int Failures = 0;

        /// <summary>If true, this skill uses the harder combat XP curve.</summary>
        public bool UseCombatXpCurve;

        /// <summary>Whether this skill has been unlocked (level > 0).</summary>
        public bool IsUnlocked => Level > 0;

        /// <summary>
        /// Skill cap based on player level: (PlayerLevel * 5) + 5.
        /// At level 1 cap=10, level 35 cap=180.
        /// </summary>
        public int SkillCap
        {
            get
            {
                int playerLevel = 1;
                try { playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1); }
                catch { }
                return (playerLevel * 5) + 5;
            }
        }

        /// <summary>XP needed for the next level.</summary>
        public float XpToNextLevel
        {
            get
            {
                if (Level <= 0) return 0f;
                if (Level >= SkillCap) return 0f;
                float baseXp = UseCombatXpCurve ? 80f : 40f;
                return baseXp * Mathf.Pow(1.03f, Level);
            }
        }

        public float LevelProgress
        {
            get
            {
                float needed = XpToNextLevel;
                return (needed <= 0f) ? 1f : Mathf.Clamp01(CurrentXp / needed);
            }
        }

        public bool IsMaxLevel => Level >= SkillCap;
        public bool IsAtCap => Level >= SkillCap;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Full save data container for all skills
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Tracks a custom item in the player's inventory for persistence.</summary>
    [Serializable]
    public class SavedCustomItem
    {
        public string Name;
        public int Count;
    }

    [Serializable]
    public class AllSkillsData
    {
        // Custom items in inventory that need to be re-injected on load
        public List<SavedCustomItem> CustomInventory = new List<SavedCustomItem>();

        /// <summary>Recipe names the player has learned. Recipes not in this list are locked.</summary>
        public List<string> KnownRecipes = new List<string>();

        /// <summary>Default starter recipes every character begins with (2 per tradeskill).</summary>
        public static readonly string[] StarterRecipes = new string[]
        {
            // Smithing
            "Copper Rivets", "Crude Iron Dagger", "Hammered Iron Shortsword", "Banded Iron Helm",
            // Baking
            "Trail Rations", "Herb Bread",
            // Brewing
            "Bog Juice", "Faerie Wine",
            // Fletching
            "Crude Shortbow", "Strung Hunting Bow",
            // Jewelcraft
            "Polished Stone Band", "Amber Pendant",
            // Tailoring
            "Patchwork Bandages", "Silken Pouch", "Stitched Leather Gloves", "Padded Cloth Cap",
        };

        /// <summary>Ensure starter recipes are known.</summary>
        public void EnsureStarterRecipes()
        {
            foreach (var name in StarterRecipes)
            {
                if (!KnownRecipes.Contains(name))
                    KnownRecipes.Add(name);
            }
        }

        /// <summary>Check if a recipe is known (learnable system).</summary>
        public bool IsRecipeKnown(string recipeName)
        {
            return KnownRecipes.Contains(recipeName);
        }

        /// <summary>Learn a new recipe. Returns true if it was new.</summary>
        public bool LearnRecipe(string recipeName)
        {
            if (KnownRecipes.Contains(recipeName)) return false;
            KnownRecipes.Add(recipeName);
            return true;
        }

        /// <summary>Unspent training points. Players gain 3 per character level.</summary>
        public int TrainingPoints = 0;

        /// <summary>Last known player level for calculating new training points.</summary>
        public int LastKnownPlayerLevel = 0;

        public SkillEntry Fishing = new SkillEntry { Name = "Fishing" };
        public SkillEntry Foraging = new SkillEntry { Name = "Foraging" };
        public SkillEntry Swimming = new SkillEntry { Name = "Swimming" };
        public SkillEntry SenseHeading = new SkillEntry { Name = "Sense Heading" };
        public SkillEntry BindWound = new SkillEntry { Name = "Bind Wound" };
        public SkillEntry Meditate = new SkillEntry { Name = "Meditate" };
        public SkillEntry SafeFall = new SkillEntry { Name = "Safe Fall" };
        public SkillEntry FoodTolerance = new SkillEntry { Name = "Food Tolerance" };
        public SkillEntry Begging = new SkillEntry { Name = "Begging" };

        // ── Combat weapon skills ────────────────────────────────────
        public SkillEntry OneHandSlash = new SkillEntry { Name = "1H Slashing", UseCombatXpCurve = true };
        public SkillEntry OneHandBlunt = new SkillEntry { Name = "1H Blunt", UseCombatXpCurve = true };
        public SkillEntry Piercing = new SkillEntry { Name = "Piercing", UseCombatXpCurve = true };
        public SkillEntry TwoHandSlash = new SkillEntry { Name = "2H Slashing", UseCombatXpCurve = true };
        public SkillEntry TwoHandBlunt = new SkillEntry { Name = "2H Blunt", UseCombatXpCurve = true };
        public SkillEntry Archery = new SkillEntry { Name = "Archery", UseCombatXpCurve = true };
        public SkillEntry Wands = new SkillEntry { Name = "Wands", UseCombatXpCurve = true };
        public SkillEntry HandToHand = new SkillEntry { Name = "Hand to Hand", UseCombatXpCurve = true };

        // ── Tradeskills ─────────────────────────────────────────────
        public SkillEntry Smithing = new SkillEntry { Name = "Smithing" };
        public SkillEntry Baking = new SkillEntry { Name = "Baking" };
        public SkillEntry Brewing = new SkillEntry { Name = "Brewing" };
        public SkillEntry Fletching = new SkillEntry { Name = "Fletching" };
        public SkillEntry Jewelcraft = new SkillEntry { Name = "Jewelcraft" };
        public SkillEntry Tailoring = new SkillEntry { Name = "Tailoring" };

        // ── Magic skills ────────────────────────────────────────────
        public SkillEntry Evocation = new SkillEntry { Name = "Evocation" };
        public SkillEntry Abjuration = new SkillEntry { Name = "Abjuration" };
        public SkillEntry Alteration = new SkillEntry { Name = "Alteration" };
        public SkillEntry Conjuration = new SkillEntry { Name = "Conjuration" };

        /// <summary>Tracks how many training points have been spent (never decreases).</summary>
        public int TrainingPointsSpent = 0;

        /// <summary>
        /// Check if the player has leveled up and recalculate available training points.
        /// Available = (PlayerLevel * 3) - PointsSpent.
        /// Call this periodically (e.g. on save or UI refresh).
        /// </summary>
        public void CheckForNewTrainingPoints()
        {
            try
            {
                int playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1);
                int totalEarned = playerLevel * 3;
                int newTP = totalEarned - TrainingPointsSpent;
                if (newTP < 0) newTP = 0;

                // Notify if points increased
                if (newTP > TrainingPoints && LastKnownPlayerLevel > 0)
                {
                    int gained = newTP - TrainingPoints;
                    ChatHelper.Send(
                        $"<color=#FFD700>[Skills]</color> " +
                        $"You gained <color=#FFFFFF>{gained}</color> training points! " +
                        $"<color=#AAAAAA>({newTP} available)</color>");
                }

                TrainingPoints = newTP;
                LastKnownPlayerLevel = playerLevel;
            }
            catch { }
        }

        /// <summary>
        /// Spend a training point to raise a skill by 1 level.
        /// Returns true if successful.
        /// </summary>
        public bool SpendTrainingPoint(SkillEntry skill)
        {
            if (TrainingPoints <= 0) return false;
            if (skill.IsAtCap) return false;

            TrainingPointsSpent++;
            TrainingPoints--;
            int oldLevel = skill.Level;
            skill.Level++;
            skill.CurrentXp = 0f;

            SkillsPlugin.Log.LogInfo(
                $"SpendTP: {skill.Name} {oldLevel}->{skill.Level}, " +
                $"TP={TrainingPoints}, Spent={TrainingPointsSpent}, " +
                $"Ref={skill.GetHashCode()}, Fishing.Lv={Fishing.Level}");
            return true;
        }

        /// <summary>
        /// Find a skill by partial name match. Returns the direct field reference.
        /// </summary>
        public SkillEntry FindSkillByName(string partialName)
        {
            string lower = partialName.ToLower();
            var all = GetAll();
            foreach (var s in all)
            {
                if (s.Name.ToLower().Contains(lower))
                    return s;
            }
            return null;
        }

        /// <summary>Return all utility skill entries for the skill window.</summary>
        public List<SkillEntry> GetUtilitySkills()
        {
            return new List<SkillEntry>
            {
                Foraging, Swimming,
                BindWound, Meditate, FoodTolerance, Begging
            };
        }

        /// <summary>Return all combat skill entries.</summary>
        public List<SkillEntry> GetCombatSkills()
        {
            return new List<SkillEntry>
            {
                OneHandSlash, OneHandBlunt, Piercing,
                TwoHandSlash, TwoHandBlunt, Archery, Wands, HandToHand
            };
        }

        /// <summary>Return all tradeskill entries.</summary>
        public List<SkillEntry> GetTradeskills()
        {
            return new List<SkillEntry>
            {
                Fishing, Smithing, Baking, Brewing, Fletching, Jewelcraft, Tailoring
            };
        }

        /// <summary>Return all magic skill entries.</summary>
        public List<SkillEntry> GetMagicSkills()
        {
            return new List<SkillEntry>
            {
                Evocation, Abjuration, Alteration, Conjuration
            };
        }

        /// <summary>Return all skill entries as a list for iteration.</summary>
        public List<SkillEntry> GetAll()
        {
            var list = new List<SkillEntry>();
            list.AddRange(GetUtilitySkills());
            list.AddRange(GetCombatSkills());
            list.AddRange(GetTradeskills());
            list.AddRange(GetMagicSkills());
            return list;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Shared XP / Level-up engine
    // ═════════════════════════════════════════════════════════════════════

    public static class SkillXpEngine
    {
        /// <summary>
        /// Award XP to a skill and handle level-ups.
        /// Returns true if a level-up occurred.
        /// </summary>
        public static bool AwardXp(SkillEntry skill, float xp, bool showChat = true)
        {
            // Skills at level 0 are untrained — no XP gain
            if (!skill.IsUnlocked) return false;
            if (skill.IsAtCap) return false;

            bool leveled = false;

            // Scale XP with skill level so the bar stays relevant at higher levels.
            // At level 1: 1.1x, level 10: 2x, level 20: 3x, level 50: 6x.
            float scaledXp = xp * (1f + skill.Level * 0.1f);

            // ── EQ-style skill-up chance on each use ────────────────
            // Chance = (SkillCap - CurrentLevel) / (SkillCap * 12)
            // Much lower than raw EQ formula to slow progression.
            // Level 1/50: ~8% per use. Level 25/50: ~4%. Level 45/50: ~0.8%.
            int cap = skill.SkillCap;
            if (cap > 0 && skill.Level < cap)
            {
                float skillUpChance = (float)(cap - skill.Level) / (float)(cap * 12);
                if (UnityEngine.Random.value < skillUpChance)
                {
                    skill.Level++;
                    skill.CurrentXp = 0f;
                    leveled = true;
                }
            }

            // ── Normal XP accumulation (still happens even if skill-up occurred) ──
            if (!skill.IsAtCap && skill.XpToNextLevel > 0)
            {
                skill.CurrentXp += scaledXp;

                while (skill.CurrentXp >= skill.XpToNextLevel && !skill.IsAtCap && skill.XpToNextLevel > 0)
                {
                    skill.CurrentXp -= skill.XpToNextLevel;
                    skill.Level++;
                    leveled = true;
                }
            }

            if (skill.IsAtCap)
                skill.CurrentXp = 0f;

            if (leveled && SkillsPlugin.CfgShowChatMessages.Value)
            {
                ChatHelper.Send(
                    $"<color=#FFD700>*** {skill.Name.ToUpper()} SKILL UP! ***</color> " +
                    $"<color=#7EC8E3>Level {skill.Level}/{skill.SkillCap}</color>");
            }

            SkillsSaveManager.Save();
            return leveled;
        }

        /// <summary>
        /// Award tradeskill XP with EQ-style trivial system.
        /// Skill-up chance = (TrivialLevel - CurrentLevel) / TrivialLevel.
        /// No XP or skill-ups once skill >= trivial.
        /// </summary>
        public static bool AwardTradeskillXp(SkillEntry skill, float xp, int trivialLevel)
        {
            if (!skill.IsUnlocked) return false;
            if (skill.IsAtCap) return false;
            if (skill.Level >= trivialLevel) return false; // Trivial — no gains

            bool leveled = false;
            float scaledXp = xp * (1f + skill.Level * 0.1f);

            // ── EQ-style tradeskill skill-up chance ────────────────
            // Chance = (TrivialLevel - CurrentLevel) / TrivialLevel
            // Near trivial = very low chance. Far below = high chance.
            if (trivialLevel > 0)
            {
                float skillUpChance = (float)(trivialLevel - skill.Level) / (float)trivialLevel;
                if (UnityEngine.Random.value < skillUpChance)
                {
                    skill.Level++;
                    skill.CurrentXp = 0f;
                    leveled = true;
                }
            }

            // ── Normal XP accumulation ──
            if (!skill.IsAtCap && skill.Level < trivialLevel && skill.XpToNextLevel > 0)
            {
                skill.CurrentXp += scaledXp;

                while (skill.CurrentXp >= skill.XpToNextLevel && !skill.IsAtCap
                       && skill.Level < trivialLevel && skill.XpToNextLevel > 0)
                {
                    skill.CurrentXp -= skill.XpToNextLevel;
                    skill.Level++;
                    leveled = true;
                }
            }

            if (skill.IsAtCap || skill.Level >= trivialLevel)
                skill.CurrentXp = 0f;

            if (leveled && SkillsPlugin.CfgShowChatMessages.Value)
            {
                ChatHelper.Send(
                    $"<color=#FFD700>*** {skill.Name.ToUpper()} SKILL UP! ***</color> " +
                    $"<color=#7EC8E3>Level {skill.Level}/{skill.SkillCap}</color>");
            }

            SkillsSaveManager.Save();
            return leveled;
        }

        /// <summary>
        /// Attempt a skill check. Higher skill = higher success rate.
        /// Optionally award XP on both success and failure (EQ style).
        /// </summary>
        public static bool SkillCheck(SkillEntry skill, float difficulty = 1f,
            float xpOnSuccess = 10f, float xpOnFailure = 3f)
        {
            skill.TimesUsed++;

            // Success chance: 30% base + 1.2% per level, modified by difficulty
            float chance = (30f + skill.Level * 1.2f) / difficulty;
            chance = Mathf.Clamp(chance, 5f, 95f);

            bool success = UnityEngine.Random.value * 100f < chance;

            if (success)
            {
                skill.Successes++;
                AwardXp(skill, xpOnSuccess);
            }
            else
            {
                skill.Failures++;
                // EQ awarded skillups on failure too - smaller XP
                AwardXp(skill, xpOnFailure, showChat: false);
            }

            return success;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Chat helper
    // ═════════════════════════════════════════════════════════════════════

    public static class ChatHelper
    {
        public static void Send(string message)
        {
            try
            {
                // Use UpdateSocialLog.LogAdd — same approach as ErenshorQoL mod
                UpdateSocialLog.LogAdd(message);
            }
            catch
            {
                SkillsPlugin.Log.LogInfo($"[Chat] {message}");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Save / Load manager
    // ═════════════════════════════════════════════════════════════════════

    public static class SkillsSaveManager
    {
        private static AllSkillsData _data;
        private static string _charName = "";
        public static string CharName => _charName;
        public static string Sanitize(string name) => SanitizeName(name);

        public static AllSkillsData Data
        {
            get
            {
                if (_data == null) _data = new AllSkillsData();
                return _data;
            }
        }

        public static void Load(string characterName)
        {
            _charName = SanitizeName(characterName);
            _data = new AllSkillsData();
            string path = GetPath();

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json) && json.Trim() != "{}")
                    {
                        ParseSkillsJson(json);
                        SkillsPlugin.Log.LogInfo(
                            $"Loaded skill data for '{characterName}' " +
                            $"(Foraging Lv{_data.Foraging.Level})");
                    }
                    else
                    {
                        SkillsPlugin.Log.LogWarning(
                            $"Skill file for '{characterName}' was empty.");
                    }
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogError($"Failed to load skills: {ex.Message}");
                }
            }
            else
            {
                SkillsPlugin.Log.LogInfo(
                    $"New skill profile created for '{characterName}'");
            }

            // Training points are calculated dynamically:
            // Available = (PlayerLevel * 3) - PointsSpent
            // No migration needed — CheckForNewTrainingPoints() handles it.

            // Load custom inventory, recipes, and bags from separate files
            LoadInventoryFile();
            LoadRecipesFile();
            LoadBagsFile();

            HasLoaded = true;
        }

        /// <summary>Parse our manual JSON format into AllSkillsData.</summary>
        private static void ParseSkillsJson(string json)
        {
            // Parse training points metadata
            _data.TrainingPoints = ParseTopLevelInt(json, "__TrainingPoints", 0);
            _data.TrainingPointsSpent = ParseTopLevelInt(json, "__TrainingPointsSpent", 0);
            _data.LastKnownPlayerLevel = ParseTopLevelInt(json, "__LastKnownPlayerLevel", 0);

            var all = _data.GetAll();
            SkillsPlugin.Log.LogInfo($"ParseSkillsJson: parsing {all.Count} skills, TP={_data.TrainingPoints}, Spent={_data.TrainingPointsSpent}");
            foreach (var skill in all)
            {
                string key = $"\"{skill.Name}\"";
                int idx = json.IndexOf(key);
                if (idx < 0) { SkillsPlugin.Log.LogWarning($"ParseSkillsJson: '{skill.Name}' not found in JSON!"); continue; }

                int braceStart = json.IndexOf('{', idx + key.Length);
                int braceEnd = json.IndexOf('}', braceStart);
                if (braceStart < 0 || braceEnd < 0) continue;

                string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                skill.Level = ParseInt(block, "Level", skill.Level);
                skill.CurrentXp = ParseFloat(block, "CurrentXp", skill.CurrentXp);
                skill.TimesUsed = ParseInt(block, "TimesUsed", skill.TimesUsed);
                skill.Successes = ParseInt(block, "Successes", skill.Successes);
                skill.Failures = ParseInt(block, "Failures", skill.Failures);

                if (skill.Name == "Fishing")
                    SkillsPlugin.Log.LogInfo($"ParseSkillsJson: Fishing parsed -> Level={skill.Level}, Ref={skill.GetHashCode()}, _data.Fishing.Lv={_data.Fishing.Level}, SameRef={skill == _data.Fishing}");
            }
        }

        private static void LoadInventoryFile()
        {
            try
            {
                string path = GetInventoryPath();
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json) || json.Trim() == "{}") return;

                _data.CustomInventory.Clear();
                // Parse "ItemName": count pairs
                string content = json.Trim().TrimStart('{').TrimEnd('}');
                string[] pairs = content.Split(',');
                foreach (var pair in pairs)
                {
                    string trimmed = pair.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    int colonIdx = trimmed.LastIndexOf(':');
                    if (colonIdx < 0) continue;
                    string name = trimmed.Substring(0, colonIdx).Trim().Trim('"');
                    string countStr = trimmed.Substring(colonIdx + 1).Trim();
                    if (int.TryParse(countStr, out int count) && count > 0)
                        _data.CustomInventory.Add(new SavedCustomItem { Name = name, Count = count });
                }

                if (_data.CustomInventory.Count > 0)
                    SkillsPlugin.Log.LogInfo(
                        $"Loaded {_data.CustomInventory.Count} custom item types from inventory file.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadInventoryFile error: {ex.Message}");
            }
        }

        private static int ParseTopLevelInt(string json, string key, int fallback)
        {
            string search = $"\"{key}\": ";
            int idx = json.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = json.IndexOfAny(new[] { ',', '}', '\n' }, start);
            if (end < 0) end = json.Length;
            string val = json.Substring(start, end - start).Trim();
            return int.TryParse(val, out int result) ? result : fallback;
        }

        private static int ParseInt(string block, string key, int fallback)
        {
            string search = $"\"{key}\":";
            int idx = block.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = block.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = block.Length;
            string val = block.Substring(start, end - start).Trim();
            return int.TryParse(val, out int result) ? result : fallback;
        }

        private static float ParseFloat(string block, string key, float fallback)
        {
            string search = $"\"{key}\":";
            int idx = block.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = block.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = block.Length;
            string val = block.Substring(start, end - start).Trim();
            return float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result)
                ? result : fallback;
        }

        public static bool HasLoaded { get; set; }

        /// <summary>Clear all in-memory data. Call when logging out.</summary>
        public static void Reset()
        {
            HasLoaded = false;
            _charName = "";
            _data = new AllSkillsData();
            Items.BagSystem.Reset();
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(_charName)) return;
            if (!HasLoaded) return;

            // Verify the character name matches the current player
            // to prevent saving one character's data under another's name
            try
            {
                string currentName = GameData.PlayerStats?.MyName;
                if (!string.IsNullOrEmpty(currentName) &&
                    SanitizeName(currentName) != _charName)
                {
                    SkillsPlugin.Log.LogWarning(
                        $"Save aborted: _charName='{_charName}' but current player is '{currentName}'");
                    return;
                }
            }
            catch { }
            try
            {
                string dir = Path.GetDirectoryName(GetPath());
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Manual JSON — JsonUtility silently fails on our data structure
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");

                // Save training points and player level
                sb.AppendLine($"  \"__TrainingPoints\": {_data.TrainingPoints},");
                sb.AppendLine($"  \"__TrainingPointsSpent\": {_data.TrainingPointsSpent},");
                sb.AppendLine($"  \"__LastKnownPlayerLevel\": {_data.LastKnownPlayerLevel},");

                var skills = _data.GetAll();
                for (int i = 0; i < skills.Count; i++)
                {
                    var s = skills[i];
                    sb.Append($"  \"{s.Name}\": {{" +
                        $"\"Level\":{s.Level}," +
                        $"\"CurrentXp\":{s.CurrentXp:F2}," +
                        $"\"TimesUsed\":{s.TimesUsed}," +
                        $"\"Successes\":{s.Successes}," +
                        $"\"Failures\":{s.Failures}}}");
                    if (i < skills.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine("}");

                string json = sb.ToString();
                File.WriteAllText(GetPath(), json);

                // Save custom inventory to separate file
                SaveInventoryFile();
                SaveRecipesFile();
                SaveBagsFile();

                SkillsPlugin.Log.LogInfo(
                    $"Saved skills for '{_charName}' ({json.Length} chars)");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Failed to save skills: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void SaveInventoryFile()
        {
            try
            {
                var items = _data.CustomInventory;
                if (items == null) return;
                string path = GetInventoryPath();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append($"  \"{items[i].Name}\": {items[i].Count}");
                    if (i < items.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        private static string GetPath()
        {
            return Path.Combine(Paths.ConfigPath, "ClassicSkills",
                $"{_charName}.json");
        }

        private static string GetInventoryPath()
        {
            return Path.Combine(Paths.ConfigPath, "ClassicSkills",
                $"{_charName}_inventory.json");
        }

        private static string GetRecipesPath()
        {
            return Path.Combine(Paths.ConfigPath, "ClassicSkills",
                $"{_charName}_recipes.json");
        }

        private static void LoadRecipesFile()
        {
            try
            {
                string path = GetRecipesPath();
                if (!File.Exists(path))
                {
                    // First time — grant starter recipes
                    _data.EnsureStarterRecipes();
                    return;
                }
                string json = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(json) || json == "[]")
                {
                    _data.EnsureStarterRecipes();
                    return;
                }

                _data.KnownRecipes.Clear();
                // Parse simple JSON array: ["Recipe1", "Recipe2", ...]
                json = json.TrimStart('[').TrimEnd(']');
                string[] parts = json.Split(',');
                foreach (var part in parts)
                {
                    string name = part.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(name))
                        _data.KnownRecipes.Add(name);
                }

                _data.EnsureStarterRecipes();
                SkillsPlugin.Log.LogInfo($"Loaded {_data.KnownRecipes.Count} known recipes.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadRecipesFile error: {ex.Message}");
                _data.EnsureStarterRecipes();
            }
        }

        public static void SaveRecipesFile()
        {
            if (!HasLoaded || string.IsNullOrEmpty(_charName)) return;
            try
            {
                string path = GetRecipesPath();
                var sb = new System.Text.StringBuilder();
                sb.Append("[");
                for (int i = 0; i < _data.KnownRecipes.Count; i++)
                {
                    sb.Append($"\"{_data.KnownRecipes[i]}\"");
                    if (i < _data.KnownRecipes.Count - 1) sb.Append(", ");
                }
                sb.Append("]");
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        private static string GetBagsPath()
        {
            return Path.Combine(Paths.ConfigPath, "ClassicSkills",
                $"{_charName}_bags.json");
        }

        private static void LoadBagsFile()
        {
            try
            {
                string path = GetBagsPath();
                if (!File.Exists(path))
                {
                    Items.BagSystem.Reset();
                    return;
                }
                string json = File.ReadAllText(path).Trim();
                Items.BagSystem.DeserializeBags(json);
                SkillsPlugin.Log.LogInfo($"Loaded bag contents.");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadBagsFile error: {ex.Message}");
            }
        }

        public static void SaveBagsFile()
        {
            if (!HasLoaded || string.IsNullOrEmpty(_charName)) return;
            try
            {
                string path = GetBagsPath();
                string json = Items.BagSystem.SerializeBags();
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
