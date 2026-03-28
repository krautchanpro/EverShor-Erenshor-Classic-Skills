using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// BEGGING — In EverQuest, Begging was a skill everyone had but almost
    /// nobody used seriously. You'd target an NPC, hit the Beg button,
    /// and usually get told off. Occasionally you'd score a copper coin.
    /// Warriors used it as a ghetto aggro tool in emergencies. It was
    /// mechanically pointless but culturally iconic.
    ///
    /// In Erenshor, Begging fits perfectly with the SimPlayer/NPC economy:
    /// - Target a friendly NPC and press the Beg key
    /// - Low skill: usually rejected with snarky messages
    /// - Higher skill: occasionally receive a small gold reward
    /// - At level 25+: chance to receive common vendor items
    /// - At level 40+: NPCs sometimes share zone lore/tips
    ///
    /// This is a pure flavor/nostalgia skill. The gold is trivial.
    /// The real reward is the classic EQ feeling and the fun messages.
    /// </summary>
    public static class BeggingSkill
    {
        private static float _lastBegTime = 0f;

        public static void TryBeg()
        {
            if (!SkillsPlugin.CfgEnableBegging.Value) return;

            float begCD = SkillsPlugin.CfgTestCooldowns.Value ? 1f : 10f;
            if (Time.time - _lastBegTime < begCD)
            {
                ChatHelper.Send(
                    $"<color=#BCAAA4>[Begging]</color> " +
                    $"<color=#AAAAAA>You should wait before begging again...</color>");
                return;
            }
            _lastBegTime = Time.time;

            // Must have a target (NPC, villager, or SimPlayer)
            string targetName = GetTargetName();
            if (string.IsNullOrEmpty(targetName))
            {
                ChatHelper.Send(
                    $"<color=#BCAAA4>[Begging]</color> " +
                    $"<color=#AAAAAA>You need to target someone to beg from.</color>");
                return;
            }

            var skill = SkillsSaveManager.Data.Begging;
            float difficulty = 1.5f; // Begging is hard (as it should be)

            bool success = SkillXpEngine.SkillCheck(skill, difficulty,
                xpOnSuccess: 12f, xpOnFailure: 6f);

            if (success)
            {
                // Determine the reward
                int level = skill.Level;
                float roll = Random.value;

                if (level >= 40 && roll < 0.1f)
                {
                    // Lore tip at high level
                    string tip = GetZoneTip();
                    ChatHelper.Send(
                        $"<color=#BCAAA4>[Begging]</color> " +
                        $"{targetName} leans in and whispers: " +
                        $"<color=#FFD700>\"{tip}\"</color>");
                }
                else if (level >= 25 && roll < 0.15f)
                {
                    // Small item
                    string item = GetBegItem();
                    ChatHelper.Send(
                        $"<color=#BCAAA4>[Begging]</color> " +
                        $"{targetName} sighs and hands you a " +
                        $"<color=#FFFFFF>{item}</color>.");
                }
                else
                {
                    // Gold reward (scaled by level, but always small)
                    int gold = Random.Range(1, Mathf.Max(2, level / 3));
                    TryAddGold(gold);

                    string[] successMsgs = new string[]
                    {
                        $"{targetName} takes pity on you and hands over {gold} gold.",
                        $"{targetName} reluctantly drops {gold} gold into your hand.",
                        $"{targetName} mutters something and gives you {gold} gold.",
                        $"\"Fine, take it,\" says {targetName}, giving you {gold} gold.",
                        $"{targetName} flips you {gold} gold. \"Don't spend it all in one place.\""
                    };
                    ChatHelper.Send(
                        $"<color=#BCAAA4>[Begging]</color> " +
                        $"{successMsgs[Random.Range(0, successMsgs.Length)]}");
                }
            }
            else
            {
                // Failure messages — the real content of Begging
                string[] failMsgs = new string[]
                {
                    $"{targetName} looks at you with contempt.",
                    $"{targetName} shoos you away.",
                    $"\"I've got nothing for the likes of you,\" says {targetName}.",
                    $"{targetName} pretends not to hear you.",
                    $"\"Ask someone who cares,\" {targetName} snaps.",
                    $"{targetName} turns their back on you.",
                    $"\"Do I look like a charity?\" asks {targetName}.",
                    $"{targetName} laughs at your pitiful display.",
                    $"\"You'd have better luck fishing,\" {targetName} suggests.",
                    $"{targetName} says, \"Try killing something instead.\"",
                    $"\"I've seen better begging from a Rottenfoot rat,\" says {targetName}.",
                    $"{targetName} crosses their arms and stares at you.",
                    $"\"My grandmother begs better than you,\" says {targetName}.",
                    $"{targetName} pointedly counts their own gold in front of you.",
                    $"\"Get a job. I hear Treven Pines is hiring,\" says {targetName}."
                };
                ChatHelper.Send(
                    $"<color=#BCAAA4>[Begging]</color> " +
                    $"<color=#AAAAAA>{failMsgs[Random.Range(0, failMsgs.Length)]}</color>");
            }
        }

        private static string GetTargetName()
        {
            try
            {
                var target = GameData.PlayerControl.CurrentTarget;
                if (target == null) return null;

                // Check if it's an NPC via the Character.MyNPC field
                if (target.MyNPC != null)
                {
                    // Accept friendly NPCs and SimPlayers (not hostile mobs)
                    if (target.MyNPC.NeverAggro)
                        return target.MyNPC.NPCName;
                    // SimPlayers also have MyNPC set
                    if (target.MyNPC.ThisSim != null)
                        return target.transform.name;
                }

                // Fallback: check for SimPlayer component
                var sim = target.GetComponent<SimPlayer>();
                if (sim != null)
                    return target.MyStats?.MyName ?? target.transform.name;

                // Last resort: any non-hostile character
                if (target.MyStats != null && !string.IsNullOrEmpty(target.MyStats.MyName))
                    return target.MyStats.MyName;
            }
            catch { }
            return null;
        }

        private static void TryAddGold(int amount)
        {
            try
            {
                var stats = GameData.PlayerStats;
                if (stats != null)
                    GameData.PlayerInv.Gold += amount;
            }
            catch { }
        }

        private static string GetBegItem()
        {
            string[] items = {
                "Stale Bread", "Chipped Mug", "Worn Bandage",
                "Faded Map Fragment", "Bent Copper Ring",
                "Half-Eaten Apple", "Tattered Cloth",
                "Rusty Fishing Hook", "Cracked Gemstone"
            };
            return items[Random.Range(0, items.Length)];
        }

        private static string GetZoneTip()
        {
            string[] tips = {
                "I hear the fish bite best at night near Blacksalt Strand.",
                "The old keeper in Elderstone Mines knows secrets about the forge.",
                "Don't trust everything the vendors in Azure tell you about prices.",
                "The Blight wasn't always corrupted, you know. There's still treasure buried there.",
                "If you can survive Malaroth's Nesting Grounds, the foraging there is unmatched.",
                "Ripper's Keep has a hidden fishing spot that the locals don't advertise.",
                "Azynthi's Garden holds fish that glow with an ethereal light.",
                "The best smithing templates come from the darkest dungeons.",
                "I once saw a SimPlayer pull a treasure map from the waters of Stowaway's Step.",
                "The wind in Windwashed Pass carries seeds from distant lands."
            };
            return tips[Random.Range(0, tips.Length)];
        }
    }
}
