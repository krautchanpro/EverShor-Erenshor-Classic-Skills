using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// BIND WOUND — In EverQuest, Bind Wound was how warriors and other
    /// non-casters recovered HP between fights. You'd sit down, bandage
    /// yourself, and watch "You have been bandaged for X hit points."
    /// scroll by. It was essential for downtime management and gave melee
    /// classes a way to reduce their dependency on healers.
    ///
    /// In Erenshor, this adds an active out-of-combat self-heal:
    /// - Press the hotkey while not in combat
    /// - Heals a percentage of your max HP based on skill level
    /// - Base: 3% max HP at level 1, scaling to ~28% at level 50
    /// - Has a cooldown (reduced by skill)
    /// - Must be below 70% HP to use (EQ's old restriction)
    /// - Higher skill = more HP healed and shorter cooldown
    ///
    /// At level 25+: Can be used on targeted SimPlayers too.
    /// At level 40+: HP threshold increases to 90%.
    /// </summary>
    public static class BindWoundSkill
    {
        private static float _lastUseTime = 0f;

        /// <summary>Cooldown in seconds.</summary>
        public static float Cooldown =>
            SkillsPlugin.CfgTestCooldowns.Value
                ? 1f
                : Mathf.Max(45f - SkillsSaveManager.Data.BindWound.Level * 0.5f, 15f);

        /// <summary>Percentage of max HP healed per use (0-100).</summary>
        public static float HealPercent
        {
            get
            {
                float basePct = SkillsPlugin.CfgBindWoundBaseHealPercent.Value;
                int level = SkillsSaveManager.Data.BindWound.Level;
                // Scales to 30% at skill 180: (30 - 3) / 180 = 0.15
                return basePct + level * 0.15f;
            }
        }

        /// <summary>HP threshold — must be below this % to bind wound.</summary>
        public static float HpThreshold =>
            SkillsSaveManager.Data.BindWound.Level >= 40 ? 0.9f : 0.7f;

        /// <summary>Calculate the actual HP healed based on a given max HP.</summary>
        public static int CalculateHeal(int maxHP)
        {
            return Mathf.Max(1, Mathf.RoundToInt(maxHP * HealPercent / 100f));
        }

        /// <summary>
        /// Find and consume one Patchwork Bandage from inventory.
        /// Returns true if a bandage was found and consumed.
        /// </summary>
        private static bool ConsumeBandage()
        {
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots == null) return false;

                foreach (var slot in inv.StoredSlots)
                {
                    if (slot?.MyItem == null) continue;
                    if (slot.MyItem.ItemName != "Patchwork Bandages") continue;

                    // Found bandages — consume one
                    if (slot.Quantity <= 1)
                    {
                        slot.MyItem = GameData.PlayerInv.Empty;
                        slot.Quantity = 0;
                    }
                    else
                    {
                        slot.Quantity--;
                    }
                    slot.UpdateSlotImage();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void TryBindWound()
        {
            if (!SkillsPlugin.CfgEnableBindWound.Value) return;

            // Cooldown
            if (Time.time - _lastUseTime < Cooldown)
            {
                float remaining = Cooldown - (Time.time - _lastUseTime);
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You must wait {remaining:F0}s before binding wounds again.");
                return;
            }

            // Must be out of combat
            try
            {
                var player = GameData.PlayerControl;
                if (GameData.InCombat)
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[Bind Wound]</color> " +
                        $"You cannot bind wounds while in combat!");
                    return;
                }
            }
            catch { }

            // Check HP threshold
            int maxHP = 100;
            try
            {
                var stats = GameData.PlayerStats;
                if (stats != null)
                {
                    maxHP = stats.CurrentMaxHP;
                    float hpRatio = (float)stats.CurrentHP / (float)stats.CurrentMaxHP;
                    if (hpRatio >= HpThreshold)
                    {
                        int pct = Mathf.RoundToInt(HpThreshold * 100f);
                        ChatHelper.Send(
                            $"<color=#EF5350>[Bind Wound]</color> " +
                            $"You must be below {pct}% HP to bind wounds.");
                        return;
                    }
                }
            }
            catch { }

            // Check for bandages in inventory
            if (!ConsumeBandage())
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You need Patchwork Bandages to bind wounds. " +
                    $"Craft them with Tailoring.");
                return;
            }

            _lastUseTime = Time.time;

            var skill = SkillsSaveManager.Data.BindWound;

            // Skill check determines if the bandage is effective
            bool success = SkillXpEngine.SkillCheck(skill, 1.0f,
                xpOnSuccess: 15f, xpOnFailure: 5f);

            if (success)
            {
                int heal = CalculateHeal(maxHP);

                // Actually heal the player
                try
                {
                    var stats = GameData.PlayerStats;
                    if (stats != null)
                    {
                        int before = stats.CurrentHP;
                        stats.CurrentHP = Mathf.Min(
                            stats.CurrentHP + heal, stats.CurrentMaxHP);
                        heal = stats.CurrentHP - before; // Actual amount healed
                    }
                }
                catch { }

                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You have been bandaged for " +
                    $"<color=#FFFFFF>{heal}</color> hit points " +
                    $"<color=#AAAAAA>({HealPercent:F1}% of max HP)</color>.");
            }
            else
            {
                string[] failMsgs = new string[]
                {
                    "The bandage slips. You fail to bind your wounds.",
                    "You fumble with the bandage materials.",
                    "Your wound dressing doesn't take hold.",
                    "The bandage is too loose to be effective."
                };
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"<color=#AAAAAA>{failMsgs[Random.Range(0, failMsgs.Length)]}</color>");
            }
        }
    }
}
