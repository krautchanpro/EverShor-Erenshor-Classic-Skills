using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// CONSUMABLE BUFF SYSTEM
    ///
    /// Creates Spell ScriptableObjects for food/drink items so they
    /// apply real stat buffs when right-clicked (consumed). Parses
    /// the recipe description text to extract stats and duration,
    /// then creates a matching Spell with those values.
    ///
    /// The game's native UseConsumable() → StartSpell() handles
    /// the actual buff application, duration tracking, and removal.
    /// </summary>
    public static class ConsumableBuffs
    {
        private static Dictionary<string, Spell> _buffSpells
            = new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Create a buff spell from a consumable's description and attach
        /// it to the item so right-click applies the buff.
        /// Call this after item registration for all consumable items.
        /// </summary>
        public static void AttachBuffToItem(Item item, string description)
        {
            if (item == null || string.IsNullOrEmpty(description)) return;
            // Skip non-consumable items
            if (item.RequiredSlot != Item.SlotType.General) return;
            // Skip items that already have an effect
            if (item.ItemEffectOnClick != null) return;
            // Only process items with stat descriptions
            if (!HasStatInfo(description)) return;

            try
            {
                var spell = CreateBuffSpell(item.ItemName, description);
                if (spell != null)
                {
                    item.ItemEffectOnClick = spell;
                    item.SpellCastTime = 0.5f; // Quick cast
                    item.Disposable = true;    // Consumed on use
                    spell.SpellIcon = item.ItemIcon;
                    _buffSpells[item.ItemName] = spell;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"ConsumableBuffs: Failed for {item.ItemName}: {ex.Message}");
            }
        }

        /// <summary>Check if a description contains stat/buff information.</summary>
        private static bool HasStatInfo(string desc)
        {
            string lower = desc.ToLower();
            return lower.Contains("str") || lower.Contains("end") ||
                   lower.Contains("dex") || lower.Contains("agi") ||
                   lower.Contains("int") || lower.Contains("wis") ||
                   lower.Contains("cha") || lower.Contains("all stats") ||
                   lower.Contains("resist") || lower.Contains("regen") ||
                   lower.Contains("haste") || lower.Contains("hp") ||
                   lower.Contains("mana") || lower.Contains("restore");
        }

        /// <summary>
        /// Parse a consumable description and create a Spell with matching stats.
        /// Examples:
        ///   "+5 Str, +3 Fire Resist for 10 min."
        ///   "+8 all stats for 12 min."
        ///   "+10 Str, +10 End for 15 min."
        /// </summary>
        private static Spell CreateBuffSpell(string itemName, string desc)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.Id = "CS_Buff_" + itemName.Replace(" ", "_");
            spell.SpellName = itemName;
            spell.SpellDesc = desc;
            spell.SelfOnly = true;
            // Set spell type to Beneficial buff
            spell.Type = Spell.SpellType.Beneficial;
            // Set spell line via reflection to avoid enum access issues
            try
            {
                var lf = typeof(Spell).GetField("Line");
                if (lf != null) lf.SetValue(spell, Enum.ToObject(lf.FieldType, 29)); // Global_Buff
            }
            catch { }
            spell.Cooldown = 1f;
            spell.ManaCost = 0;
            spell.SpellChargeTime = 0f;
            spell.InflictOnSelf = true;
            spell.SelfOnly = true;
            spell.InstantEffect = false;
            spell.SpellRange = 0f;
            spell.StatusEffectMessageOnPlayer = "You feel nourished.";

            // Initialize lists to prevent NullReferenceException in StartSpell
            spell.ChargeVariations = new System.Collections.Generic.List<UnityEngine.AudioClip>();
            spell.UsedBy = new System.Collections.Generic.List<Class>();

            // Parse duration — game uses ticks where displayed = ticks * 3 seconds
            int durationMin = ParseDuration(desc);
            float durationSec = durationMin * 60f;
            spell.SpellDurationInTicks = Mathf.Max(1, Mathf.RoundToInt(durationSec / 3f));

            // Zero all stats first
            spell.Str = 0; spell.End = 0; spell.Dex = 0;
            spell.Agi = 0; spell.Int = 0; spell.Wis = 0;
            spell.Cha = 0; spell.HP = 0; spell.Mana = 0;
            spell.MR = 0; spell.ER = 0; spell.PR = 0; spell.VR = 0;
            spell.AC = 0;

            string lower = desc.ToLower();

            // Parse "all stats" first
            var allMatch = Regex.Match(lower, @"\+(\d+)\s*all\s*stats");
            if (allMatch.Success)
            {
                int val = int.Parse(allMatch.Groups[1].Value);
                spell.Str = val; spell.End = val; spell.Dex = val;
                spell.Agi = val; spell.Int = val; spell.Wis = val;
                spell.Cha = val;
            }

            // Parse individual stats: "+5 Str", "+8 End", etc.
            ParseAndSetStat(lower, "str", ref spell.Str);
            ParseAndSetStat(lower, "end", ref spell.End);
            ParseAndSetStat(lower, "dex", ref spell.Dex);
            ParseAndSetStat(lower, "agi", ref spell.Agi);
            ParseAndSetStat(lower, "int", ref spell.Int);
            ParseAndSetStat(lower, "wis", ref spell.Wis);
            ParseAndSetStat(lower, "cha", ref spell.Cha);

            // Parse resists: "+8 Fire Resist" etc.
            ParseResist(lower, "fire", ref spell.ER);
            ParseResist(lower, "cold", ref spell.ER);
            ParseResist(lower, "magic", ref spell.MR);
            ParseResist(lower, "poison", ref spell.PR);
            ParseResist(lower, "void", ref spell.VR);
            ParseResist(lower, "elemental", ref spell.ER);

            // "all resists"
            var allRes = Regex.Match(lower, @"\+(\d+)\s*all\s*resist");
            if (allRes.Success)
            {
                int val = int.Parse(allRes.Groups[1].Value);
                spell.MR += val; spell.ER += val;
                spell.PR += val; spell.VR += val;
            }

            // Parse HP/Mana regen
            var hpRegen = Regex.Match(lower, @"\+(\d+)\s*hp\s*regen");
            if (hpRegen.Success)
                spell.HP = int.Parse(hpRegen.Groups[1].Value);

            var mpRegen = Regex.Match(lower, @"\+(\d+)\s*mp\s*regen");
            if (mpRegen.Success)
                spell.Mana = int.Parse(mpRegen.Groups[1].Value);

            // Check we actually set something
            if (spell.Str == 0 && spell.End == 0 && spell.Dex == 0 &&
                spell.Agi == 0 && spell.Int == 0 && spell.Wis == 0 &&
                spell.Cha == 0 && spell.HP == 0 && spell.Mana == 0 &&
                spell.MR == 0 && spell.ER == 0 && spell.PR == 0 &&
                spell.VR == 0)
                return null;

            return spell;
        }

        /// <summary>Parse "+N stat" from description.</summary>
        private static void ParseAndSetStat(string lower, string statName, ref int field)
        {
            // Match patterns like "+5 str", "+10 Str"
            var match = Regex.Match(lower, @"\+(\d+)\s*" + statName + @"\b");
            if (match.Success)
            {
                int val = int.Parse(match.Groups[1].Value);
                if (val > field) field = val; // Don't override "all stats" if higher
            }
        }

        /// <summary>Parse "+N type Resist" from description.</summary>
        private static void ParseResist(string lower, string type, ref int field)
        {
            var match = Regex.Match(lower, @"\+(\d+)\s*" + type + @"\s*resist");
            if (match.Success)
                field += int.Parse(match.Groups[1].Value);
        }

        /// <summary>Parse duration in minutes from description. Default 5.</summary>
        private static int ParseDuration(string desc)
        {
            var match = Regex.Match(desc.ToLower(), @"(\d+)\s*min");
            if (match.Success)
                return int.Parse(match.Groups[1].Value);
            return 5; // Default 5 minutes
        }
    }
}
