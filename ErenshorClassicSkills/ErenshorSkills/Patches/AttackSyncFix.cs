using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Patches
{
    // ═══════════════════════════════════════════════════════════════════
    // ATTACK SYNC FIX (merged) + COMBAT WEAPON SKILL XP
    // Originally a separate mod (AttackSyncFix v1.3.0).
    // Merged here so both use the same Harmony instance — no conflicts.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Config entries for attack sync timing.</summary>
    public static class AttackSyncConfig
    {
        public static ConfigEntry<float> PlayerHitFraction;
        public static ConfigEntry<float> NPCHitFraction;
        public static ConfigEntry<float> MinHitDelay;
        public static ConfigEntry<float> MaxHitDelay;
        public static ConfigEntry<bool> SyncPlayerAttacks;
        public static ConfigEntry<bool> SyncNPCAttacks;
        public static ConfigEntry<bool> SyncSimPlayerAttacks;
        public static ConfigEntry<bool> DebugLogging;

        public static void Init(BepInEx.Configuration.ConfigFile cfg)
        {
            PlayerHitFraction = cfg.Bind("AttackSync", "PlayerHitFraction", 0.45f,
                new ConfigDescription("Fraction of swing animation when hit lands.",
                    new AcceptableValueRange<float>(0.1f, 0.9f)));
            NPCHitFraction = cfg.Bind("AttackSync", "NPCHitFraction", 0.40f,
                new ConfigDescription("Fraction of NPC swing when hit sound plays.",
                    new AcceptableValueRange<float>(0.1f, 0.9f)));
            MinHitDelay = cfg.Bind("AttackSync", "MinHitDelay", 0.08f,
                new ConfigDescription("Minimum hit delay in seconds.",
                    new AcceptableValueRange<float>(0.02f, 0.3f)));
            MaxHitDelay = cfg.Bind("AttackSync", "MaxHitDelay", 0.8f,
                new ConfigDescription("Maximum hit delay in seconds.",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));
            SyncPlayerAttacks = cfg.Bind("AttackSync", "SyncPlayerAttacks", true,
                "Enable sync fix for player melee auto-attacks.");
            SyncNPCAttacks = cfg.Bind("AttackSync", "SyncNPCAttacks", true,
                "Enable sync fix for NPC/enemy melee attacks.");
            SyncSimPlayerAttacks = cfg.Bind("AttackSync", "SyncSimPlayerAttacks", true,
                "Enable sync fix for SimPlayer (party member) melee attacks.");
            DebugLogging = cfg.Bind("AttackSync", "DebugLogging", false,
                "Log animation lengths and computed delays.");
        }
    }

    /// <summary>Animation/timing helpers.</summary>
    public static class AttackSyncHelpers
    {
        public static float GetSwingAnimLength(Animator anim)
        {
            if (anim == null) return 0.7f;
            AnimatorClipInfo[] clips = anim.GetNextAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0)
            {
                float len = clips[0].clip.length;
                if (len > 0.05f && len < 1.5f) return len;
            }
            clips = anim.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0)
            {
                float len = clips[0].clip.length;
                if (len > 0.05f && len < 1.5f) return len;
            }
            return 0.7f;
        }

        public static float CalcHitDelay(float animLength, float fraction, float animSpeedMult)
        {
            float effective = animLength / Mathf.Max(animSpeedMult, 0.1f);
            float delay = effective * fraction;
            return Mathf.Clamp(delay, AttackSyncConfig.MinHitDelay.Value, AttackSyncConfig.MaxHitDelay.Value);
        }

        public static float GetAnimSpeedMultiplier(Stats stats)
        {
            float currentDelay = SyncReflection.GetCurrentMHAtkDelay(stats);
            return (currentDelay < 80f) ? 1.25f : 1.0f;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PLAYER MELEE — Prefix replaces PerformAttacks, awards weapon XP
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "PerformAttacks")]
    public static class Patch_PerformAttacks
    {
        public static bool Prefix(PlayerCombat __instance, Character target, int attackCount, bool isMainHand)
        {
            // --- Attack Sync: skip if disabled or ranged ---
            var myStats = SyncReflection.GetPlayerStats(__instance);
            var myAnim = SyncReflection.GetPlayerAnim(__instance);
            var myControl = SyncReflection.GetPlayerControl(__instance);
            if (myStats == null || myAnim == null || myControl == null) return true;

            bool isWand = SyncReflection.CallCheckForWand(__instance, isMainHand);
            bool isBow = SyncReflection.CallCheckForBow(__instance, isMainHand);

            // Wands and bows use their own code path — let vanilla handle them
            if (isWand || isBow) return true;

            if (!AttackSyncConfig.SyncPlayerAttacks.Value) return true;
            if (target == null || !target.Alive) return true;

            bool inRange = SyncReflection.CallCheckForRange(__instance, isWand, isBow, target);

            for (int i = 0; i <= attackCount; i++)
            {
                if (!inRange) continue;
                target.FlagForFactionHit(true);

                // Fire animation triggers immediately
                if (i == 0) myAnim.SetTrigger(isMainHand ? "MeleeSwing" : "DualWield");
                if (i == 1 && isMainHand) myAnim.SetBool("DoubleAttack", true);
                if (i == 1 && !isMainHand) myAnim.SetBool("OHDoubleAttack", true);
                if (i == 2) myAnim.SetTrigger("MeleeSwing");

                int dmg = myStats.CalcMeleeDamage(
                    isMainHand ? myStats.MyInv.MHDmg : myStats.MyInv.OHDmg,
                    target.MyStats.Level, target.MyStats, 0);

                string avoidance = SyncReflection.CallCheckTargetInnateAvoidance(__instance, target);

                if (i == 0 && isMainHand && myStats.CombatStance != null && myStats.CombatStance.SelfDamagePerAttack > 0f)
                {
                    float pct = myStats.CombatStance.SelfDamagePerAttack / 100f;
                    myStats.Myself.SelfDamageMeFlat(Mathf.RoundToInt((float)myStats.CurrentMaxHP * pct));
                }

                myStats.CheckProc(isMainHand ? GameData.PlayerInv.MH : GameData.PlayerInv.OH, target);
                if (dmg > 0) myStats.RecentDmg = 240f;
                if (myStats.CombatStance != null && myStats.CombatStance.SelfDamagePerAttack > 0f)
                    myStats.Myself.SelfDamageMe(myStats.CombatStance.SelfDamagePerAttack);

                // --- MERGED: Award weapon skill XP ---
                try
                {
                    if (SkillsPlugin.CfgEnableCombatSkills.Value)
                    {
                        string weaponName = "";
                        bool isTwoHand = GameData.PlayerInv?.TwoHandPrimary ?? false;
                        int slot = isMainHand ? 12 : 13;
                        var eq = GameData.PlayerInv;
                        if (eq?.EquippedItems != null && eq.EquippedItems.Count > slot && eq.EquippedItems[slot] != null)
                            weaponName = eq.EquippedItems[slot].ItemName ?? "";

                        if (string.IsNullOrEmpty(weaponName) || weaponName == "Empty")
                        {
                            var hth = SkillsSaveManager.Data.HandToHand;
                            hth.TimesUsed++; hth.Successes++;
                            SkillXpEngine.AwardXp(hth, 2f, showChat: false);
                        }
                        else
                        {
                            var wtype = Skills.CombatSkills.ClassifyWeapon(weaponName, isTwoHand);
                            if (wtype != Skills.CombatSkills.WeaponType.Unknown &&
                                wtype != Skills.CombatSkills.WeaponType.Wands)
                                Skills.CombatSkills.OnWeaponHit(weaponName, isTwoHand, 0);
                        }
                    }
                }
                catch { }

                // Start two-phase coroutine for synced hit
                var hitData = new DelayedPlayerHitData
                {
                    Instance = __instance, Target = target, Dmg = dmg,
                    IsMainHand = isMainHand, MyStats = myStats, MyControl = myControl,
                    Avoidance = avoidance, Anim = myAnim
                };
                SkillsPlugin.Instance.StartCoroutine(TwoPhasePlayerHit(hitData));
            }

            // Reset attack delay
            if (isMainHand) myStats.ResetMHAtkDelay();
            else myStats.ResetOHAtkDelay();

            return false; // Skip original
        }

        private static IEnumerator TwoPhasePlayerHit(DelayedPlayerHitData data)
        {
            yield return null; // Wait one frame for animator transition

            if (data.Target == null || !data.Target.Alive || data.MyStats == null)
                yield break;

            float animLength = AttackSyncHelpers.GetSwingAnimLength(data.Anim);
            float animSpeed = AttackSyncHelpers.GetAnimSpeedMultiplier(data.MyStats);
            float fraction = AttackSyncConfig.PlayerHitFraction.Value;
            float hitDelay = AttackSyncHelpers.CalcHitDelay(animLength, fraction, animSpeed);

            if (AttackSyncConfig.DebugLogging.Value)
                SkillsPlugin.Log.LogInfo(
                    $"[Player] animLen={animLength:F3}s speed={animSpeed:F2}x " +
                    $"fraction={fraction:F2} => delay={hitDelay:F3}s");

            float remaining = hitDelay - Time.deltaTime;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            if (data.Target == null || !data.Target.Alive || data.MyStats == null)
                yield break;

            if (data.Avoidance != "")
            {
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(
                    "You try to hit " + data.Target.name + ", but " + data.Target.name + " " + data.Avoidance,
                    ChatLogLine.LogType.PlayerHits));
                yield break;
            }

            if (data.MyStats.Myself?.MyAttackSound != null)
                data.MyStats.Myself.MyAudio.PlayOneShot(data.MyStats.Myself.MyAttackSound,
                    data.MyStats.Myself.MyAudio.volume * GameData.SFXVol * GameData.MasterVol);

            SyncReflection.CallHandleDamageResult(data.Instance, data.Target, ref data.Dmg, data.IsMainHand);
        }
    }

    public class DelayedPlayerHitData
    {
        public PlayerCombat Instance; public Character Target; public int Dmg;
        public bool IsMainHand; public Stats MyStats; public PlayerControl MyControl;
        public string Avoidance; public Animator Anim;
    }

    // ══════════════════════════════════════════════════════════════
    // WAND XP — Postfix on DoWandAttack (vanilla handles wand logic)
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "DoWandAttack")]
    public static class Patch_WandXP
    {
        public static void Postfix(PlayerCombat __instance, Character _target)
        {
            if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;
            try
            {
                var skill = SkillsSaveManager.Data.Wands;
                skill.TimesUsed++; skill.Successes++;
                SkillXpEngine.AwardXp(skill, 2.2f, showChat: false);
            }
            catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ARCHERY XP — Postfix on PerformAttacks for bow attacks only
    // (The Prefix above returns true for bows, so vanilla runs,
    //  and this postfix fires after)
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerCombat), "PerformAttacks")]
    public static class Patch_ArcheryXP
    {
        public static void Postfix(PlayerCombat __instance, Character target, int attackCount, bool isMainHand)
        {
            if (!SkillsPlugin.CfgEnableCombatSkills.Value) return;
            try
            {
                if (target == null) return;
                var mhItem = GameData.PlayerInv?.MH?.MyItem;
                if (mhItem == null || !mhItem.IsBow) return;

                var skill = SkillsSaveManager.Data.Archery;
                skill.TimesUsed++; skill.Successes++;
                SkillXpEngine.AwardXp(skill, 2.5f, showChat: false);
            }
            catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // NPC MELEE SOUND SYNC
    // ══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
    public static class Patch_NPCMeleeSync
    {
        internal static bool SuppressNPCAttackSound = false;

        public static void Prefix(NPC __instance, int baseDamage, bool isOffhand)
        {
            bool isSim = __instance.SimPlayer;
            if (isSim && !AttackSyncConfig.SyncSimPlayerAttacks.Value) return;
            if (!isSim && !AttackSyncConfig.SyncNPCAttacks.Value) return;
            if (SyncReflection.GetNPCMHBow(__instance) || SyncReflection.GetNPCMHWand(__instance)) return;

            Character myself = __instance.GetChar();
            if (myself == null || __instance.CurrentAggroTarget == null) return;

            SuppressNPCAttackSound = true;

            float fraction = isSim
                ? AttackSyncConfig.PlayerHitFraction.Value
                : AttackSyncConfig.NPCHitFraction.Value;
            Animator anim = myself.GetComponent<Animator>();
            float animSpeed = 1.0f;
            if (myself.MyStats != null)
            {
                float currentDelay = SyncReflection.GetCurrentMHAtkDelay(myself.MyStats);
                if (currentDelay < 80f) animSpeed = 1.25f;
            }

            SkillsPlugin.Instance.StartCoroutine(TwoPhaseNPCSound(__instance, anim, fraction, animSpeed));
        }

        public static void Postfix() { SuppressNPCAttackSound = false; }

        private static IEnumerator TwoPhaseNPCSound(NPC npc, Animator anim, float fraction, float animSpeed)
        {
            yield return null;
            if (npc == null) yield break;
            Character myself = npc.GetChar();
            if (myself == null || myself.MyAttackSound == null) yield break;

            float animLength = AttackSyncHelpers.GetSwingAnimLength(anim);
            float hitDelay = AttackSyncHelpers.CalcHitDelay(animLength, fraction, animSpeed);

            if (AttackSyncConfig.DebugLogging.Value)
                SkillsPlugin.Log.LogInfo(
                    $"[NPC:{myself.name}] animLen={animLength:F3}s speed={animSpeed:F2}x " +
                    $"fraction={fraction:F2} => delay={hitDelay:F3}s");

            float remaining = hitDelay - Time.deltaTime;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);

            if (npc == null) yield break;
            myself = npc.GetChar();
            if (myself == null || myself.MyAttackSound == null) yield break;

            float dist = Vector3.Distance(myself.transform.position, GameData.PlayerControl.transform.position);
            bool isTargetPlayer = npc.CurrentAggroTarget == GameData.PlayerControl.Myself;
            if (dist < 8f || isTargetPlayer)
                myself.MyAudio.PlayOneShot(myself.MyAttackSound,
                    myself.MyAudio.volume * GameData.CombatVol * GameData.MasterVol);
        }
    }

    [HarmonyPatch(typeof(NPC), "PerformMeleeHit")]
    [HarmonyPriority(Priority.HigherThanNormal)]
    public static class Patch_NPCMeleeSoundSuppressor
    {
        private static AudioClip _savedClip;
        private static Character _savedChar;

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void MuteSound(NPC __instance)
        {
            _savedClip = null; _savedChar = null;
            if (!Patch_NPCMeleeSync.SuppressNPCAttackSound) return;
            Character myself = __instance.GetChar();
            if (myself?.MyAttackSound != null)
            {
                _savedChar = myself;
                _savedClip = myself.MyAttackSound;
                myself.MyAttackSound = null;
            }
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        public static void RestoreSound()
        {
            if (_savedChar != null && _savedClip != null)
            {
                _savedChar.MyAttackSound = _savedClip;
                _savedChar = null; _savedClip = null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // REFLECTION HELPERS (from AttackSyncFix)
    // ══════════════════════════════════════════════════════════════

    public static class SyncReflection
    {
        private static FieldInfo _pcMyStats, _pcMyAnim, _pcMyControl;
        private static MethodInfo _pcCheckForWand, _pcCheckForBow, _pcCheckForRange;
        private static MethodInfo _pcCheckTargetInnateAvoidance, _pcHandleDamageResult;
        private static FieldInfo _statsCurrentMHAtkDelay;
        private static FieldInfo _npcMHBow, _npcMHWand;
        private static bool _npcFieldsResolved;

        static SyncReflection()
        {
            var pcType = typeof(PlayerCombat);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _pcMyStats = pcType.GetField("myStats", flags);
            _pcMyAnim = pcType.GetField("MyAnim", flags);
            _pcMyControl = pcType.GetField("MyControl", flags);
            _pcCheckForWand = pcType.GetMethod("CheckForWand", flags);
            _pcCheckForBow = pcType.GetMethod("CheckForBow", flags);
            _pcCheckForRange = pcType.GetMethod("CheckForRange", flags);
            _pcCheckTargetInnateAvoidance = pcType.GetMethod("CheckTargetInnateAvoidance", flags);
            _pcHandleDamageResult = pcType.GetMethod("HandleDamageResult", flags);
            _statsCurrentMHAtkDelay = typeof(Stats).GetField("CurrentMHAtkDelay", flags);
        }

        public static Stats GetPlayerStats(PlayerCombat pc) => _pcMyStats?.GetValue(pc) as Stats;
        public static Animator GetPlayerAnim(PlayerCombat pc) => _pcMyAnim?.GetValue(pc) as Animator;
        public static PlayerControl GetPlayerControl(PlayerCombat pc) => _pcMyControl?.GetValue(pc) as PlayerControl;

        public static float GetCurrentMHAtkDelay(Stats stats)
        {
            if (_statsCurrentMHAtkDelay != null) return (float)_statsCurrentMHAtkDelay.GetValue(stats);
            return stats.BaseMHAtkDelay + stats.MyInv.MHDelay * 60f;
        }

        public static bool CallCheckForWand(PlayerCombat pc, bool mh)
            => (bool)(_pcCheckForWand?.Invoke(pc, new object[] { mh }) ?? false);
        public static bool CallCheckForBow(PlayerCombat pc, bool mh)
            => (bool)(_pcCheckForBow?.Invoke(pc, new object[] { mh }) ?? false);
        public static bool CallCheckForRange(PlayerCombat pc, bool w, bool b, Character t)
            => (bool)(_pcCheckForRange?.Invoke(pc, new object[] { w, b, t }) ?? false);
        public static string CallCheckTargetInnateAvoidance(PlayerCombat pc, Character t)
            => (string)(_pcCheckTargetInnateAvoidance?.Invoke(pc, new object[] { t }) ?? "");

        public static void CallHandleDamageResult(PlayerCombat pc, Character target, ref int dmg, bool isMainHand)
        {
            if (_pcHandleDamageResult != null)
            {
                object[] args = new object[] { target, dmg, isMainHand };
                _pcHandleDamageResult.Invoke(pc, args);
                dmg = (int)args[1];
            }
        }

        private static void ResolveNPCFields()
        {
            if (_npcFieldsResolved) return;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _npcMHBow = typeof(NPC).GetField("MHBow", flags);
            _npcMHWand = typeof(NPC).GetField("MHWand", flags);
            _npcFieldsResolved = true;
        }

        public static bool GetNPCMHBow(NPC npc) { ResolveNPCFields(); return (bool)(_npcMHBow?.GetValue(npc) ?? false); }
        public static bool GetNPCMHWand(NPC npc) { ResolveNPCFields(); return (bool)(_npcMHWand?.GetValue(npc) ?? false); }
    }
}
