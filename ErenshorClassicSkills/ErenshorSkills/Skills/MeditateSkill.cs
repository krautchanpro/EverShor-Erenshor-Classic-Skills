using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// MEDITATE — In EverQuest, Meditate was THE caster skill. You'd sit
    /// down after every fight and watch your mana bar crawl back up while
    /// reading a spellbook that covered your entire screen. At higher levels
    /// the spellbook went away and mana regen improved significantly.
    /// "MEDDING" became such a core part of the game that it entered the
    /// MMO lexicon permanently.
    ///
    /// In Erenshor, all classes use mana. This skill:
    /// - Toggle on/off with the hotkey (must be sitting)
    /// - While meditating, mana regen rate increases based on skill level
    /// - XP awarded periodically while meditating
    /// - Moving cancels meditation
    /// - At level 30+: Meditation works at a reduced rate while standing
    /// </summary>
    public static class MeditateSkill
    {
        private static bool _isMeditating = false;
        private static float _lastMedTick = 0f;
        private static float _tickInterval = 3f;
        private static Vector3 _medStartPos;

        public static bool IsMeditating => _isMeditating;

        /// <summary>Extra mana per regen tick while meditating.</summary>
        public static float BonusManaPerTick
        {
            get
            {
                float baseMana = SkillsPlugin.CfgMeditateBaseMana.Value;
                return baseMana + SkillsSaveManager.Data.Meditate.Level * 0.556f;
            }
        }

        public static void ToggleMeditate()
        {
            if (!SkillsPlugin.CfgEnableMeditate.Value) return;

            if (_isMeditating)
            {
                StopMeditating("You stop meditating.");
                return;
            }

            // Meditation works while sitting, even in combat

            // Check if mana is already full
            try
            {
                var stats = GameData.PlayerStats;
                if (stats != null && stats.CurrentMana >= stats.GetCurrentMaxMana())
                {
                    ChatHelper.Send(
                        $"<color=#CE93D8>[Meditate]</color> " +
                        $"Your mana is already full.");
                    return;
                }
            }
            catch { }

            _isMeditating = true;
            _lastMedTick = Time.time;

            try
            {
                _medStartPos = GameData.PlayerControl.transform.position;
            }
            catch
            {
                _medStartPos = Vector3.zero;
            }

            ChatHelper.Send(
                $"<color=#CE93D8>[Meditate]</color> " +
                $"You begin meditating... " +
                $"<color=#AAAAAA>(+{BonusManaPerTick:F1} mana/tick)</color>");
        }

        /// <summary>
        /// Called every frame by Patch_MeditationTick.
        /// Detects the game's native meditation state (RecentCast and RecentDmg <= 0)
        /// and awards bonus mana + XP during it. Also works with manual toggle via M key.
        /// </summary>
        public static void ProcessTick()
        {
            try
            {
                var stats = GameData.PlayerStats;
                if (stats == null) return;

                // Detect sitting — PlayerControl.Sitting is public bool
                bool isSitting = false;
                try
                {
                    isSitting = GameData.PlayerControl.Sitting;
                }
                catch { }

                bool shouldMed = _isMeditating || isSitting;
                if (!shouldMed) return;

                // Only active while mana is recovering — no XP or bonus at full mana
                if (stats.CurrentMana >= stats.GetCurrentMaxMana()) return;

                // Tick interval
                if (Time.time - _lastMedTick < _tickInterval) return;
                _lastMedTick = Time.time;

                var skill = SkillsSaveManager.Data.Meditate;

                // Award bonus mana on top of the game's natural regen
                float mana = BonusManaPerTick;
                stats.CurrentMana = Mathf.Min(
                    stats.CurrentMana + Mathf.RoundToInt(mana),
                    stats.GetCurrentMaxMana());

                // Only gain skill XP while actively recovering mana
                SkillXpEngine.AwardXp(skill, 3f, showChat: false);
                skill.TimesUsed++;
            }
            catch { }
        }

        private static void StopMeditating(string reason)
        {
            _isMeditating = false;
            if (SkillsPlugin.CfgShowChatMessages.Value)
                ChatHelper.Send(
                    $"<color=#CE93D8>[Meditate]</color> " +
                    $"<color=#AAAAAA>{reason}</color>");
        }
    }
}
