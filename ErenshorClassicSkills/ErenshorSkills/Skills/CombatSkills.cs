using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// COMBAT WEAPON SKILLS — EverQuest-style weapon proficiencies.
    /// Each weapon type has its own skill that levels as you fight.
    ///
    /// Erenshor weapon → Skill mapping:
    ///   1H Slashing  — Swords, Axes, Blades, Whipsword
    ///   1H Blunt     — Maces, Clubs, Torches, Steins
    ///   Piercing     — Daggers, Knives, Spikes, Picks, Claws
    ///   2H Slashing  — Greatswords, Halberds, Claymores, 2H Axes
    ///   2H Blunt     — Staves, Great Maces, Poles, Totems
    ///   Archery      — Bows (all types)
    ///   Wands        — All wands, sceptres, grimoires, candles, spell focus items
    ///   Hand to Hand — Fists, Fistwraps, unarmed combat
    ///
    /// Whips/Whipsword: Falls under 1H Slashing. The Whipsword is a
    /// segmented blade weapon — functionally a sword. EQ never had a
    /// dedicated whip skill; whip-type weapons were classified as 1H Slash.
    ///
    /// Wands: Separated from 1H Blunt because casters in Erenshor rely
    /// heavily on wand/focus weapons, and lumping them with maces felt
    /// wrong. This gives caster weapon progression its own identity.
    /// </summary>
    public static class CombatSkills
    {
        public enum WeaponType
        {
            OneHandSlash,
            OneHandBlunt,
            Piercing,
            TwoHandSlash,
            TwoHandBlunt,
            Archery,
            Wands,
            HandToHand,
            Unknown
        }

        /// <summary>Classify a weapon by name and hand type.</summary>
        public static WeaponType ClassifyWeapon(string weaponName, bool isTwoHand)
        {
            if (string.IsNullOrEmpty(weaponName))
                return WeaponType.HandToHand;

            string lower = weaponName.ToLower();

            // ── Bows ────────────────────────────────────────────────
            if (lower.Contains("bow") || lower.Contains("seastring"))
                return WeaponType.Archery;

            // ── Wands / Spell Focus (before 2H check, wands are 1H) ─
            if (lower.Contains("wand") || lower.Contains("grimoire") ||
                lower.Contains("candle") || lower.Contains("nighthollow") ||
                lower.Contains("bombastic") || lower.Contains("dreamy") ||
                lower.Contains("eyestalk") || lower.Contains("garg wand") ||
                lower.Contains("sceptre") || lower.Contains("scepter"))
                return WeaponType.Wands;

            // ── 2-Handed weapons ────────────────────────────────────
            if (isTwoHand)
            {
                if (lower.Contains("staff") || lower.Contains("pole") ||
                    lower.Contains("totem") || lower.Contains("heavy bladed mace") ||
                    lower.Contains("humanbone") || lower.Contains("spidersmasher") ||
                    lower.Contains("stirrer"))
                    return WeaponType.TwoHandBlunt;

                return WeaponType.TwoHandSlash;
            }

            // ── Hand to Hand ────────────────────────────────────────
            if (lower.Contains("fist") || lower.Contains("fistwrap") ||
                lower.Contains("claw") || lower.Contains("cryptid"))
                return WeaponType.HandToHand;

            // ── Piercing ────────────────────────────────────────────
            if (lower.Contains("dagger") || lower.Contains("knife") ||
                lower.Contains("spike") || lower.Contains("pick") ||
                lower.Contains("priel") || lower.Contains("eviscerator") ||
                lower.Contains("asp") || lower.Contains("stiletto") ||
                lower.Contains("shiv") || lower.Contains("gourd carver") ||
                lower.Contains("celestial spike"))
                return WeaponType.Piercing;

            // ── 1H Blunt (no longer includes wands) ─────────────────
            if (lower.Contains("mace") || lower.Contains("club") ||
                lower.Contains("torch") ||
                lower.Contains("stein") || lower.Contains("bouquet") ||
                lower.Contains("bundle") || lower.Contains("stone") ||
                lower.Contains("idol") || lower.Contains("jellystick") ||
                lower.Contains("petrified") || lower.Contains("ogre tooth") ||
                lower.Contains("horn of") || lower.Contains("testament"))
                return WeaponType.OneHandBlunt;

            // ── 1H Slash (includes whipsword) ───────────────────────
            if (lower.Contains("sword") || lower.Contains("axe") ||
                lower.Contains("blade") || lower.Contains("hatchet") ||
                lower.Contains("reaper") || lower.Contains("bloodletter") ||
                lower.Contains("ebonshade") || lower.Contains("esen") ||
                lower.Contains("deciding") || lower.Contains("ceto") ||
                lower.Contains("charged") || lower.Contains("carver") ||
                lower.Contains("whip"))
                return WeaponType.OneHandSlash;

            if (lower.Contains("shield") || lower.Contains("buckler"))
                return WeaponType.Unknown;

            return WeaponType.OneHandSlash;
        }

        public static SkillEntry GetSkillForType(WeaponType type)
        {
            var data = SkillsSaveManager.Data;
            switch (type)
            {
                case WeaponType.OneHandSlash:  return data.OneHandSlash;
                case WeaponType.OneHandBlunt:  return data.OneHandBlunt;
                case WeaponType.Piercing:      return data.Piercing;
                case WeaponType.TwoHandSlash:  return data.TwoHandSlash;
                case WeaponType.TwoHandBlunt:  return data.TwoHandBlunt;
                case WeaponType.Archery:       return data.Archery;
                case WeaponType.Wands:         return data.Wands;
                case WeaponType.HandToHand:    return data.HandToHand;
                default:                       return null;
            }
        }

        public static string GetTypeName(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.OneHandSlash:  return "1H Slashing";
                case WeaponType.OneHandBlunt:  return "1H Blunt";
                case WeaponType.Piercing:      return "Piercing";
                case WeaponType.TwoHandSlash:  return "2H Slashing";
                case WeaponType.TwoHandBlunt:  return "2H Blunt";
                case WeaponType.Archery:       return "Archery";
                case WeaponType.Wands:         return "Wands";
                case WeaponType.HandToHand:    return "Hand to Hand";
                default:                       return "Unknown";
            }
        }

        /// <summary>
        /// Damage multiplier: +0.2% per level (configurable).
        /// Level 50 = +10% damage. Conservative but meaningful.
        /// </summary>
        public static float GetDamageMultiplier(WeaponType type)
        {
            if (!SkillsPlugin.CfgEnableCombatSkills.Value) return 1f;
            var skill = GetSkillForType(type);
            if (skill == null) return 1f;
            float bonusPct = skill.Level * SkillsPlugin.CfgCombatSkillDamagePerLevel.Value;
            return 1f + bonusPct / 100f;
        }

        /// <summary>Award XP on weapon hit.</summary>
        public static void OnWeaponHit(string weaponName, bool isTwoHand,
            float damageDealt)
        {
            if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;

            WeaponType type = ClassifyWeapon(weaponName, isTwoHand);
            if (type == WeaponType.Unknown) return;

            var skill = GetSkillForType(type);
            if (skill == null || skill.IsMaxLevel) return;

            skill.TimesUsed++;
            skill.Successes++;

            float xp = 2f;
            if (type == WeaponType.TwoHandSlash || type == WeaponType.TwoHandBlunt)
                xp = 2.5f;
            if (type == WeaponType.Archery)
                xp = 2.5f;
            if (type == WeaponType.Wands)
                xp = 2.2f; // Wands swing fast but are caster weapons

            if (damageDealt > 50f) xp *= 1.1f;
            if (damageDealt > 150f) xp *= 1.15f;

            SkillXpEngine.AwardXp(skill, xp, showChat: false);
        }

        /// <summary>Combat skill info for UI display.</summary>
        public struct CombatSkillInfo
        {
            public string Name;
            public SkillEntry Skill;
            public float Bonus;
            public CombatSkillInfo(string name, SkillEntry skill, float bonus)
            { Name = name; Skill = skill; Bonus = bonus; }
        }

        /// <summary>Get all combat skills with current bonus info.</summary>
        public static List<CombatSkillInfo> GetActiveCombatSkills()
        {
            var result = new List<CombatSkillInfo>();
            var data = SkillsSaveManager.Data;

            var names   = new string[]     { "1H Slashing", "1H Blunt", "Piercing", "2H Slashing", "2H Blunt", "Archery", "Wands", "Hand to Hand" };
            var skills  = new SkillEntry[] { data.OneHandSlash, data.OneHandBlunt, data.Piercing, data.TwoHandSlash, data.TwoHandBlunt, data.Archery, data.Wands, data.HandToHand };
            var types   = new WeaponType[] { WeaponType.OneHandSlash, WeaponType.OneHandBlunt, WeaponType.Piercing, WeaponType.TwoHandSlash, WeaponType.TwoHandBlunt, WeaponType.Archery, WeaponType.Wands, WeaponType.HandToHand };

            for (int i = 0; i < names.Length; i++)
            {
                // Compute bonus directly from skill level × config value
                float bonus = skills[i].Level * SkillsPlugin.CfgCombatSkillDamagePerLevel.Value;
                result.Add(new CombatSkillInfo(names[i], skills[i], bonus));
            }

            return result;
        }
    }
}
