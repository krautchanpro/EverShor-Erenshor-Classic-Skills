using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SkillsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.erenshor.classicskills";
        public const string PluginName = "Erenshor Classic Skills";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static SkillsPlugin Instance;
        private Harmony _harmony;

        // ── Global Config ───────────────────────────────────────────────
        internal static ConfigEntry<KeyCode> CfgSkillWindowKey;
        internal static ConfigEntry<bool> CfgShowChatMessages;
        internal static ConfigEntry<int> CfgGlobalMaxLevel;

        // ── Per-skill toggles ───────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableFishing;
        internal static ConfigEntry<bool> CfgEnableForaging;
        internal static ConfigEntry<bool> CfgEnableSwimming;
        // Sense Heading removed
        internal static ConfigEntry<bool> CfgEnableBindWound;
        internal static ConfigEntry<bool> CfgEnableMeditate;
        // Safe Fall removed
        internal static ConfigEntry<bool> CfgEnableFoodTolerance;
        internal static ConfigEntry<bool> CfgEnableBegging;

        // ── Combat skills ───────────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableCombatSkills;
        internal static ConfigEntry<float> CfgCombatSkillDamagePerLevel;

        // ── Magic skills ────────────────────────────────────────────
        internal static ConfigEntry<bool> CfgEnableMagicSkills;
        internal static ConfigEntry<float> CfgEvocationDamagePerLevel;
        internal static ConfigEntry<float> CfgAbjurationDurationPerLevel;
        internal static ConfigEntry<float> CfgAlterationHealPerLevel;
        internal static ConfigEntry<float> CfgConjurationDamagePerLevel;

        // ── Skill-specific config ───────────────────────────────────────
        internal static ConfigEntry<KeyCode> CfgForageKey;
        internal static ConfigEntry<KeyCode> CfgBindWoundKey;
        // Sense Heading removed
        internal static ConfigEntry<KeyCode> CfgBegKey;
        internal static ConfigEntry<KeyCode> CfgMeditateKey;
        internal static ConfigEntry<float> CfgBindWoundBaseHealPercent;
        internal static ConfigEntry<float> CfgMeditateBaseMana;
        // Safe Fall removed
        internal static ConfigEntry<bool> CfgTestCooldowns;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindConfig();

            _harmony = new Harmony(PluginGuid);
            PatchAllSafe();

            // Close skills window on scene change (logout, zone change)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
            Log.LogInfo($"  Press {CfgSkillWindowKey.Value} to open the Skills window.");
            Log.LogInfo($"  Type /skills in chat for a summary.");
        }

        private void Start()
        {
            DebugLog($"Start() called. GO active={gameObject.activeSelf}, enabled={enabled}");
            Log.LogInfo($"Classic Skills: Start() called. GO={gameObject.name}, active={gameObject.activeSelf}");
            StartCoroutine(InputPollingCoroutine());

            // Initialize uGUI-based skills window
            if (gameObject.GetComponent<SkillsUI>() == null)
                gameObject.AddComponent<SkillsUI>();
            if (gameObject.GetComponent<MenuBarButton>() == null)
                gameObject.AddComponent<MenuBarButton>();

            // Pre-build the tradeskill window so first station click is instant
            UI.TradeskillWindow.Open("Smithing");
            UI.TradeskillWindow.Close();
        }

        private void OnEnable()
        {
            DebugLog($"OnEnable() called.");
        }

        /// <summary>
        /// Patch each class individually so one failure doesn't kill the rest.
        /// </summary>
        private void PatchAllSafe()
        {
            var patchTypes = new System.Type[]
            {
                typeof(Patches.Patch_LoadCharacter),
                typeof(Patches.Patch_SaveOnInventory),
                typeof(Patches.Patch_DoFishing),
                // Fishing cast time reduction removed
                typeof(Patches.Patch_FishingCatchBonus),
                typeof(Patches.Patch_Swimming),
                // Patch_UseConsumable removed — ambiguous match on Stats.AddStatusEffect
                // Our Patch_UseCustomConsumable handles custom food items instead
                typeof(Patches.Patch_MeditationTick),
                typeof(Patches.Patch_UseCustomConsumable),
                typeof(Patches.Patch_SwimSpeed),
                typeof(Patches.Patch_BuffDuration),
                // Combat XP + Attack Sync Fix (merged)
                typeof(Patches.Patch_PerformAttacks),
                typeof(Patches.Patch_WandXP),
                typeof(Patches.Patch_ArcheryXP),
                typeof(Patches.Patch_NPCMeleeSync),
                typeof(Patches.Patch_NPCMeleeSoundSuppressor),
                typeof(Patches.Patch_ChatCommands),
                typeof(Stations.Patch_OpenForge),
                typeof(Stations.Patch_PlayerInteract),
                typeof(Stations.Patch_ZoneLoad_Stations),
                typeof(Stations.Patch_VendorLoadWindow),
                typeof(Stations.Patch_VendorPurchase),
                typeof(Patches.Patch_PreventBagSplit),
                typeof(Patches.Patch_BagStoreOnRightClick),
                typeof(Patches.Patch_FoodToleranceXP),
                typeof(Patches.Patch_FoodToleranceDuration),
                typeof(Patches.Patch_FoodToleranceDuration4),
                typeof(Items.Patch_ItemDatabase_Init),
                typeof(Items.Patch_ItemDatabase_Awake),
                typeof(Items.Patch_GetItemByID),
                typeof(Patches.Patch_TooltipInject),
                typeof(Patches.Patch_SpellTooltip),
                typeof(Patches.Patch_SpellBookTooltip),
                typeof(Patches.Patch_HotbarSpellTooltip),
                typeof(Patches.Patch_CombatDamageBonus_Melee),
                typeof(Patches.Patch_CombatDamageBonus_Bow),
                typeof(Patches.Patch_RecipeDrop),
                typeof(Patches.Patch_MeatDrop),
                typeof(Patches.Patch_MiningNodeDrop),
                typeof(Patches.Patch_EndgameMaterialDrop),
                typeof(Patches.Patch_MiningBonus),
                typeof(Patches.Patch_MagicSkillXP),
                typeof(Patches.Patch_MagicSkillXP2),
                typeof(Patches.Patch_EvocationDamage),
                typeof(Patches.Patch_AlterationHeal),
            };

            int ok = 0, fail = 0;
            foreach (var t in patchTypes)
            {
                try
                {
                    _harmony.PatchAll(t);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    Log.LogWarning($"Patch {t.Name} failed (non-fatal): {ex.Message}");
                }
            }
            Log.LogInfo($"Harmony: {ok} patches applied, {fail} skipped.");
        }

        private void BindConfig()
        {
            // ── Global ──────────────────────────────────────────────────
            CfgSkillWindowKey = Config.Bind("Global", "SkillWindowKey", KeyCode.F8,
                "Key to toggle the Classic Skills window.");
            CfgShowChatMessages = Config.Bind("Global", "ShowChatMessages", true,
                "Show skill-up and usage messages in chat.");
            CfgGlobalMaxLevel = Config.Bind("Global", "MaxSkillLevel", 180,
                "Maximum level for all skills (1-200).");

            // ── Skill Toggles ───────────────────────────────────────────
            CfgEnableFishing = Config.Bind("Skill Toggles", "EnableFishing", true,
                "Enable the Fishing skill.");
            CfgEnableForaging = Config.Bind("Skill Toggles", "EnableForaging", true,
                "Enable the Foraging skill (find items while exploring).");
            CfgEnableSwimming = Config.Bind("Skill Toggles", "EnableSwimming", true,
                "Enable the Swimming skill (move faster in water).");
            // Sense Heading removed
            CfgEnableBindWound = Config.Bind("Skill Toggles", "EnableBindWound", true,
                "Enable the Bind Wound skill (bandage yourself out of combat).");
            CfgEnableMeditate = Config.Bind("Skill Toggles", "EnableMeditate", true,
                "Enable the Meditate skill (faster mana regen while sitting).");
            // Safe Fall removed
            CfgEnableFoodTolerance = Config.Bind("Skill Toggles", "EnableFoodTolerance", true,
                "Enable the Food Tolerance skill (food/drink buff duration).");
            CfgEnableBegging = Config.Bind("Skill Toggles", "EnableBegging", true,
                "Enable the Begging skill (beg NPCs for copper).");
            CfgEnableCombatSkills = Config.Bind("Skill Toggles", "EnableCombatSkills", true,
                "Enable combat weapon skills (1H Slash, 1H Blunt, Piercing, etc.).");
            CfgEnableMagicSkills = Config.Bind("Skill Toggles", "EnableMagicSkills", true,
                "Enable magic skills (Evocation, Abjuration, Alteration, Conjuration).");

            // ── Attack Sync Fix (merged) ────────────────────────────────
            Patches.AttackSyncConfig.Init(Config);

            // ── Keybinds ────────────────────────────────────────────────
            CfgForageKey = Config.Bind("Keybinds", "ForageKey", KeyCode.F9,
                "Key to attempt foraging.");
            CfgBindWoundKey = Config.Bind("Keybinds", "BindWoundKey", KeyCode.F10,
                "Key to bind wounds (bandage self).");
            // Sense Heading removed
            CfgBegKey = Config.Bind("Keybinds", "BegKey", KeyCode.Semicolon,
                "Key to beg from targeted NPC.");
            CfgMeditateKey = Config.Bind("Keybinds", "MeditateKey", KeyCode.M,
                "Key to toggle meditation (must be sitting).");

            // ── Skill Tuning ────────────────────────────────────────────
            CfgBindWoundBaseHealPercent = Config.Bind("Skill Tuning", "BindWoundBaseHealPercent", 3f,
                "Base percentage of max HP healed per Bind Wound use. Scales +0.15% per level (30% at skill 180).");
            CfgMeditateBaseMana = Config.Bind("Skill Tuning", "MeditateBaseMana", 2f,
                "Base extra mana per tick while meditating. Scales with skill level.");
            // Safe Fall removed
            // Safe Fall removed
            CfgCombatSkillDamagePerLevel = Config.Bind("Skill Tuning", "CombatSkillDamagePerLevel", 0.085f,
                "Percent bonus damage per combat skill level. At 0.085, level 180 = +15.3% damage.");

            // ── Magic Skill Tuning ──────────────────────────────────
            CfgEvocationDamagePerLevel = Config.Bind("Skill Tuning", "EvocationDamagePerLevel", 0.0008f,
                "Bonus spell damage per Evocation level. At 0.0008, level 180 = +14.4%.");
            CfgAbjurationDurationPerLevel = Config.Bind("Skill Tuning", "AbjurationDurationPerLevel", 0.003f,
                "Bonus buff duration per Abjuration level. At 0.003, level 180 = +54%.");
            CfgAlterationHealPerLevel = Config.Bind("Skill Tuning", "AlterationHealPerLevel", 0.0008f,
                "Bonus heal amount per Alteration level. At 0.0008, level 180 = +14.4%.");
            CfgConjurationDamagePerLevel = Config.Bind("Skill Tuning", "ConjurationDamagePerLevel", 0.0008f,
                "Bonus DoT/pet damage per Conjuration level. At 0.0008, level 180 = +14.4%.");

            // ── Testing ─────────────────────────────────────────────────
            CfgTestCooldowns = Config.Bind("Testing", "TestCooldowns", false,
                "Set all utility skill cooldowns to 1 second for testing.");
        }

        private static bool _loggedFirstUpdate = false;
        private static int _updateCount = 0;
        private static string _debugLogPath;

        private static void DebugLog(string msg)
        {
            try
            {
                if (_debugLogPath == null)
                    _debugLogPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(
                            System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "ClassicSkills_debug.log");
                System.IO.File.AppendAllText(_debugLogPath, 
                    $"[{System.DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        private void Update()
        {
            try
            {
                _updateCount++;
                if (!_loggedFirstUpdate && _updateCount > 10)
                {
                    _loggedFirstUpdate = true;
                    string msg = $"Update() running at frame {_updateCount}. F8 keybind = {CfgSkillWindowKey.Value}";
                    Log.LogInfo(msg);
                    DebugLog(msg);
                }

                if (Input.GetKeyDown(CfgSkillWindowKey.Value))
                {
                    DebugLog("F8 pressed! Toggling skills window.");
                    Log.LogInfo("Classic Skills: F8 pressed!");
                    SkillsUI.ToggleWindow();
                }

                // ── Active skill keybinds ───────────────────────────────────
                if (CfgEnableForaging.Value && Input.GetKeyDown(CfgForageKey.Value))
                    Skills.ForagingSkill.TryForage();

                if (CfgEnableBindWound.Value && Input.GetKeyDown(CfgBindWoundKey.Value))
                    Skills.BindWoundSkill.TryBindWound();

                // Sense Heading removed — game has built-in map/compass

                if (CfgEnableBegging.Value && Input.GetKeyDown(CfgBegKey.Value))
                    Skills.BeggingSkill.TryBeg();

                if (CfgEnableMeditate.Value && Input.GetKeyDown(CfgMeditateKey.Value))
                    Skills.MeditateSkill.ToggleMeditate();
            }
            catch (Exception ex)
            {
                if (_updateCount <= 2)
                    DebugLog($"Update error at frame {_updateCount}: {ex}");
            }
        }

        private void OnGUI()
        {
            SkillsUI.DoOnGUI();
        }

        private void OnDestroy()
        {
            // Save on plugin destroy (game quit)
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
            }
            catch { }
            _harmony?.UnpatchSelf();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            string name = scene.name;
            if (name == "Menu" || name == "LoadScene")
            {
                // Close skills window when returning to menu or loading screen
                if (SkillsUI.IsWindowVisible)
                    SkillsUI.ToggleWindow();

                // Save ONLY if the character name still matches the current player.
                // This prevents saving stale data during character switches.
                if (SkillsSaveManager.HasLoaded)
                {
                    try
                    {
                        Items.ItemFactory.SaveCustomInventory();
                        SkillsSaveManager.Save();
                    }
                    catch { }
                }

                // Clear all stale data IMMEDIATELY so it can't leak
                SkillsSaveManager.Reset();
            }
            else
            {
                // Any non-menu scene: check if we need to load
                if (!SkillsSaveManager.HasLoaded)
                {
                    Log.LogInfo($"Scene '{name}' loaded, HasLoaded=false — starting DelayedCharacterLoad");
                    StartCoroutine(DelayedCharacterLoad());
                }
                else
                {
                    // Verify the loaded character still matches (zone change within same character)
                    try
                    {
                        string currentPlayer = GameData.PlayerStats?.MyName;
                        if (!string.IsNullOrEmpty(currentPlayer) &&
                            !string.IsNullOrEmpty(SkillsSaveManager.CharName) &&
                            SkillsSaveManager.Sanitize(currentPlayer) != SkillsSaveManager.CharName)
                        {
                            Log.LogWarning($"Character mismatch on zone change: loaded='{SkillsSaveManager.CharName}' but player='{currentPlayer}'. Reloading.");
                            SkillsSaveManager.Reset();
                            StartCoroutine(DelayedCharacterLoad());
                        }
                    }
                    catch { }
                }
            }
        }

        private System.Collections.IEnumerator DelayedCharacterLoad()
        {
            // Wait for player stats to be available
            string charName = "";
            for (int i = 0; i < 300; i++)
            {
                yield return null;
                try
                {
                    var stats = GameData.PlayerStats;
                    if (stats != null)
                    {
                        charName = stats.MyName;
                        if (!string.IsNullOrEmpty(charName)) break;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(charName) && !SkillsSaveManager.HasLoaded)
            {
                SkillsSaveManager.Load(charName);
                Log.LogInfo($"Skills loaded for '{charName}' (character switch)");
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                Log.LogInfo("Saved skills on application quit.");
            }
            catch { }
        }

        /// <summary>
        /// Coroutine that polls input every frame as a backup in case Update() doesn't fire.
        /// </summary>
        private IEnumerator InputPollingCoroutine()
        {
            DebugLog("InputPollingCoroutine started");
            Log.LogInfo("Classic Skills: Input coroutine started.");
            int frame = 0;
            while (true)
            {
                try
                {
                    frame++;
                    if (frame == 30)
                    {
                        DebugLog($"Coroutine running at frame {frame}");
                        Log.LogInfo($"Classic Skills: Coroutine confirmed running (frame {frame}).");
                    }
                    // Input handling is done in Update() — coroutine is just a heartbeat check
                }
                catch { }

                yield return null;
            }
        }
    }
}
