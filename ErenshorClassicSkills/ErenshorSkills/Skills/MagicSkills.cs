using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// MAGIC SKILLS — EverQuest's magic system tracked proficiency in
    /// four schools of magic: Evocation, Abjuration, Alteration, and
    /// Conjuration. Each spell cast raised the corresponding skill,
    /// and higher skill meant more powerful spells.
    ///
    /// In Erenshor, we classify spells by their SpellType:
    ///   Evocation   — Direct damage (Damage, AE, PBAE)
    ///   Abjuration  — Buffs and wards (Beneficial)
    ///   Alteration  — Healing (Heal)
    ///   Conjuration — Pets and DoTs (Pet, StatusEffect with damage)
    ///
    /// Bonuses per skill level (at cap 180):
    ///   Evocation   — +0.08% spell damage per level = +14.4% at 180
    ///   Abjuration  — +0.3% buff duration per level = +54% at 180
    ///   Alteration  — +0.08% heal amount per level  = +14.4% at 180
    ///   Conjuration — +0.08% DoT/pet damage per level = +14.4% at 180
    /// </summary>
    public static class MagicSkills
    {
        public enum MagicSchool
        {
            None,
            Evocation,    // Direct damage
            Abjuration,   // Buffs/wards
            Alteration,   // Heals
            Conjuration   // Pets/DoTs
        }

        /// <summary>Classify a spell into a magic school based on its SpellType.</summary>
        public static MagicSchool ClassifySpell(Spell spell)
        {
            if (spell == null) return MagicSchool.None;

            switch (spell.Type)
            {
                case Spell.SpellType.Damage:
                case Spell.SpellType.AE:
                case Spell.SpellType.PBAE:
                    return MagicSchool.Evocation;

                case Spell.SpellType.Beneficial:
                    return MagicSchool.Abjuration;

                case Spell.SpellType.Heal:
                    return MagicSchool.Alteration;

                case Spell.SpellType.Pet:
                case Spell.SpellType.StatusEffect:
                    return MagicSchool.Conjuration;

                case Spell.SpellType.Misc:
                    // Misc spells: if they do damage, Evocation; otherwise Abjuration
                    if (spell.TargetDamage > 0)
                        return MagicSchool.Evocation;
                    return MagicSchool.Abjuration;

                default:
                    return MagicSchool.None;
            }
        }

        /// <summary>Get the SkillEntry for a magic school.</summary>
        public static SkillEntry GetSkillEntry(MagicSchool school)
        {
            if (!SkillsSaveManager.HasLoaded) return null;
            var data = SkillsSaveManager.Data;
            switch (school)
            {
                case MagicSchool.Evocation:   return data.Evocation;
                case MagicSchool.Abjuration:  return data.Abjuration;
                case MagicSchool.Alteration:  return data.Alteration;
                case MagicSchool.Conjuration:  return data.Conjuration;
                default: return null;
            }
        }

        /// <summary>Award XP when a spell is cast. Called from Harmony patch.</summary>
        public static void OnSpellCast(Spell spell)
        {
            if (spell == null) return;
            if (!SkillsSaveManager.HasLoaded) return;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;

            var school = ClassifySpell(spell);
            if (school == MagicSchool.None) return;

            var skill = GetSkillEntry(school);
            if (skill == null) return;

            // XP scales with spell mana cost (harder spells = more XP)
            float xp = 8f + spell.ManaCost * 0.15f;
            float difficulty = 1.0f;

            SkillXpEngine.SkillCheck(skill, difficulty,
                xpOnSuccess: xp, xpOnFailure: xp * 0.5f);
        }

        /// <summary>Get spell damage multiplier from Evocation skill.</summary>
        public static float GetSpellDamageMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Evocation;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * SkillsPlugin.CfgEvocationDamagePerLevel.Value;
        }

        /// <summary>Get heal amount multiplier from Alteration skill.</summary>
        public static float GetHealMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Alteration;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * SkillsPlugin.CfgAlterationHealPerLevel.Value;
        }

        /// <summary>Get buff duration multiplier from Abjuration skill.</summary>
        public static float GetBuffDurationMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Abjuration;
            // +0.3% per level = +54% at 180
            return 1f + skill.Level * SkillsPlugin.CfgAbjurationDurationPerLevel.Value;
        }

        /// <summary>Get DoT/pet damage multiplier from Conjuration skill.</summary>
        public static float GetConjurationMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Conjuration;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * SkillsPlugin.CfgConjurationDamagePerLevel.Value;
        }
    }
}
