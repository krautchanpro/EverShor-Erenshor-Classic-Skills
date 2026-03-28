using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ErenshorSkills.UI
{
    /// <summary>
    /// uGUI Canvas-based Tradeskill Crafting Window (v2).
    /// Matches the SkillsUI style — dark panels, gold accents,
    /// proper scroll/drag via uGUI event system.
    /// </summary>
    public class TradeskillWindow : MonoBehaviour
    {
        static TradeskillWindow _instance;
        static bool _showWindow;
        static string _activeTradeskill = "";
        static int _selectedRecipeIndex = -1;
        static string _searchText = "";
        static bool _showOnlyAvailable = false;
        static bool _isCombining;
        static float _combineStartTime;
        const float COMBINE_DUR = 2.5f;

        // Cached filtered recipes
        static List<Skills.Tradeskills.Recipe> _filtered
            = new List<Skills.Tradeskills.Recipe>();

        // uGUI references
        GameObject _canvasGO;
        RectTransform _windowPanel;
        ScrollRect _recipeScroll;
        RectTransform _recipeContent;
        RectTransform _detailPanel;
        Text _titleText, _skillLevelText, _combineStatusText, _footerText;
        Text _detailName, _detailDesc, _detailReqSkill, _detailTrivial;
        Text _detailChance, _detailValue;
        Image _detailIcon;
        RectTransform _detailIconRT;
        RectTransform _xpBarFill, _combineBarFill;
        Text _xpBarText, _combineBarText;
        Button _combineBtn;
        Text _combineBtnText;
        Button _deconBtn;
        Text _deconBtnText;
        InputField _searchField;

        RectTransform _matsContainer;
        bool _built;

        // Colors matching game's native UI palette
        static readonly Color C_WinBg  = c(0.08f, 0.12f, 0.15f, 0.96f);  // Dark teal-blue
        static readonly Color C_Panel  = c(0.10f, 0.16f, 0.20f, 0.92f);  // Slightly lighter teal
        static readonly Color C_Title  = c(0.06f, 0.10f, 0.13f, 0.98f);  // Darkest teal for title
        static readonly Color C_Border = c(0.22f, 0.36f, 0.40f, 0.9f);   // Teal/cyan border
        static readonly Color C_BarBg  = c(0.04f, 0.07f, 0.09f, 0.85f);  // Very dark teal bar bg
        static readonly Color C_Gold   = c(1f, 0.84f, 0f);
        static readonly Color C_Accent = c(1f, 0.6f, 0f);
        static readonly Color C_Dim    = c(0.55f, 0.55f, 0.55f);
        static readonly Color C_Green  = c(0.3f, 0.69f, 0.31f);
        static readonly Color C_Red    = c(0.94f, 0.33f, 0.31f);
        static Color c(float r, float g, float b, float a = 1) => new Color(r, g, b, a);

        const float W = 500, H = 560, TITLE_H = 30, PAD = 6;

        // ══════════════════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════════════════

        public static void Open(string tradeskillName)
        {
            if (_instance == null)
            {
                var go = new GameObject("TradeskillWindowUI");
                _instance = go.AddComponent<TradeskillWindow>();
                DontDestroyOnLoad(go);
            }

            if (_showWindow && _activeTradeskill == tradeskillName)
            {
                _showWindow = false;
                _instance.SetVis(false);
                return;
            }

            _activeTradeskill = tradeskillName;
            _showWindow = true;
            _selectedRecipeIndex = -1;
            _searchText = "";
            _isCombining = false;
            RefreshRecipeList();
            _instance.SetVis(true);
            if (_instance._built)
            {
                _instance.ClearRecipeRows();   // Force full rebuild
                _instance.ClearMaterials();
                if (_instance._searchField) _instance._searchField.text = "";
                _instance.Refresh();
            }
        }

        public static void Close()
        {
            _showWindow = false;
            _isCombining = false;
            if (_instance) _instance.SetVis(false);
        }

        public static bool IsOpen => _showWindow;

        // ══════════════════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════════════════

        void Start()
        {
            try { Build(); _built = true; SetVis(false); }
            catch (Exception ex) { SkillsPlugin.Log.LogError($"TradeskillWindow build: {ex}"); }
        }

        void LateUpdate()
        {
            if (!_built || !_showWindow || !_canvasGO || !_canvasGO.activeSelf) return;

            // Escape key closes the window
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (_isCombining && Time.time - _combineStartTime >= COMBINE_DUR)
            {
                _isCombining = false;
                ExecuteCombine();
            }

            Refresh();
        }

        void OnDestroy() { if (_canvasGO) Destroy(_canvasGO); }

        // ══════════════════════════════════════════════════════════
        // BUILD — construct the entire uGUI hierarchy
        // ══════════════════════════════════════════════════════════

        void Build()
        {
            // Reuse existing EventSystem if present
            if (!FindObjectOfType<EventSystem>())
            {
                var es = new GameObject("CS_TS_ES");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(es);
            }

            _canvasGO = new GameObject("CS_TS_Canvas");
            DontDestroyOnLoad(_canvasGO);
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 101;
            var sc = _canvasGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Window panel
            _windowPanel = MkRect(_canvasGO.transform, "Win");
            MkImg(_windowPanel, C_WinBg);
            _windowPanel.anchorMin = V(0, 1); _windowPanel.anchorMax = V(0, 1);
            _windowPanel.pivot = V(0, 1);
            _windowPanel.anchoredPosition = V(420, -60);
            _windowPanel.sizeDelta = V(W, H);
            _windowPanel.gameObject.AddComponent<Outline>().effectColor = C_Border;
            _windowPanel.gameObject.GetComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);
            _windowPanel.gameObject.AddComponent<InputBlocker>();

            // Title bar
            var titleRT = MkRect(_windowPanel, "Title");
            MkImg(titleRT, C_Title);
            StretchH(titleRT, 0, TITLE_H);
            titleRT.gameObject.AddComponent<WindowDragger>().target = _windowPanel;
            // Border line at bottom of title
            var titleBorder = MkRect(titleRT, "TBorder");
            MkImg(titleBorder, C_Border);
            titleBorder.anchorMin = V(0, 0); titleBorder.anchorMax = V(1, 0);
            titleBorder.pivot = V(0.5f, 0); titleBorder.sizeDelta = V(0, 1);
            titleBorder.anchoredPosition = V(0, 0);

            // Close button FIRST
            var clRT = MkRect(titleRT, "Close");
            var clImg = MkImg(clRT, c(0.6f, 0.15f, 0.15f, 0.9f));
            clRT.anchorMin = V(1, 0.5f); clRT.anchorMax = V(1, 0.5f);
            clRT.pivot = V(1, 0.5f);
            clRT.anchoredPosition = V(-4, 0); clRT.sizeDelta = V(28, 28);
            var btn = clRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = clImg;
            var colors = btn.colors;
            colors.normalColor = c(0.6f, 0.15f, 0.15f, 0.9f);
            colors.highlightedColor = c(0.8f, 0.2f, 0.2f, 1f);
            colors.pressedColor = c(0.4f, 0.1f, 0.1f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => {
                SkillsPlugin.Log.LogInfo("TradeskillWindow: Close button clicked");
                Close();
            });
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var ct = MkTxt(clRT, "\u2715", 12, Color.white, TextAnchor.MiddleCenter);
            ct.fontStyle = FontStyle.Bold; Fill(ct.rectTransform);

            // Title text AFTER close button
            _titleText = MkTxt(titleRT, "Tradeskill", 16, Color.white, TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.rectTransform.anchorMin = V(0, 0);
            _titleText.rectTransform.anchorMax = V(1, 1);
            _titleText.rectTransform.offsetMin = V(8, 0);
            _titleText.rectTransform.offsetMax = V(-30, 0);
            _titleText.raycastTarget = false;

            // Skill level label (right side of title)
            _skillLevelText = MkTxt(titleRT, "", 11, C_Accent, TextAnchor.MiddleRight);
            _skillLevelText.rectTransform.anchorMin = V(0.6f, 0);
            _skillLevelText.rectTransform.anchorMax = V(0.92f, 1);
            _skillLevelText.rectTransform.offsetMin = Vector2.zero;
            _skillLevelText.rectTransform.offsetMax = Vector2.zero;

            float y = TITLE_H + 2;

            // XP bar
            var xpBg = MkRect(_windowPanel, "XPBg");
            MkImg(xpBg, C_BarBg); StretchH(xpBg, y, 14, 6, 6);
            _xpBarFill = MkRect(xpBg, "XPFill");
            MkImg(_xpBarFill, C_Accent * 0.7f).raycastTarget = false;
            _xpBarFill.anchorMin = V(0, 0); _xpBarFill.anchorMax = V(0, 1);
            _xpBarFill.pivot = V(0, 0.5f);
            _xpBarFill.offsetMin = V(1, 1); _xpBarFill.offsetMax = V(0, -1);
            _xpBarText = MkTxt(xpBg, "", 9, Color.white, TextAnchor.MiddleCenter);
            _xpBarText.fontStyle = FontStyle.Bold; Fill(_xpBarText.rectTransform);
            y += 18;

            // Search bar row
            var searchRow = MkRect(_windowPanel, "Search");
            StretchH(searchRow, y, 22, 6, 6);

            // Search input field
            var sfRT = MkRect(searchRow, "SF");
            sfRT.anchorMin = V(0, 0); sfRT.anchorMax = V(0.75f, 1);
            sfRT.offsetMin = V(0, 0); sfRT.offsetMax = V(-4, 0);
            MkImg(sfRT, c(0.10f, 0.16f, 0.22f, 0.8f));
            _searchField = sfRT.gameObject.AddComponent<InputField>();
            var sfText = MkTxt(sfRT, "", 11, Color.white, TextAnchor.MiddleLeft);
            sfText.rectTransform.offsetMin = V(4, 0); sfText.rectTransform.offsetMax = V(-4, 0);
            Fill(sfText.rectTransform);
            sfText.rectTransform.offsetMin = V(4, 0);
            _searchField.textComponent = sfText;
            _searchField.onValueChanged.AddListener(s => { _searchText = s; RefreshRecipeList(); if (_built) Refresh(); });

            // Placeholder
            var phText = MkTxt(sfRT, "Search recipes...", 11, C_Dim, TextAnchor.MiddleLeft);
            Fill(phText.rectTransform); phText.rectTransform.offsetMin = V(4, 0);
            _searchField.placeholder = phText;

            // Craftable toggle
            var togRT = MkRect(searchRow, "Tog");
            togRT.anchorMin = V(0.76f, 0); togRT.anchorMax = V(1, 1);
            togRT.offsetMin = Vector2.zero; togRT.offsetMax = Vector2.zero;
            var togLabel = MkTxt(togRT, "Craftable", 10, C_Dim, TextAnchor.MiddleCenter);
            Fill(togLabel.rectTransform);
            y += 26;

            // Two-panel area: recipes (left) + details (right)
            float panelH = H - y - 70; // leave room for combine + footer

            // LEFT: Recipe list scroll
            var leftPanel = MkRect(_windowPanel, "Left");
            MkImg(leftPanel, C_Panel);
            leftPanel.anchorMin = V(0, 1); leftPanel.anchorMax = V(0.42f, 1);
            leftPanel.pivot = V(0, 1);
            leftPanel.offsetMin = V(6, -(y + panelH));
            leftPanel.offsetMax = V(0, -y);

            // Recipe header
            var rHdr = MkTxt(leftPanel, "Recipes", 12, C_Accent, TextAnchor.MiddleCenter);
            rHdr.fontStyle = FontStyle.Bold;
            rHdr.rectTransform.anchorMin = V(0, 1); rHdr.rectTransform.anchorMax = V(1, 1);
            rHdr.rectTransform.pivot = V(0.5f, 1);
            rHdr.rectTransform.offsetMin = V(0, -20);
            rHdr.rectTransform.offsetMax = Vector2.zero;

            // Recipe scroll
            var rScrollRT = MkRect(leftPanel, "RScroll");
            rScrollRT.anchorMin = V(0, 0); rScrollRT.anchorMax = V(1, 1);
            rScrollRT.offsetMin = V(2, 2); rScrollRT.offsetMax = V(-2, -22);
            var sr = rScrollRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20;

            var vpRT = MkRect(rScrollRT, "VP");
            MkImg(vpRT, Color.clear); Fill(vpRT);
            vpRT.gameObject.AddComponent<RectMask2D>();

            _recipeContent = MkRect(vpRT, "Content");
            _recipeContent.anchorMin = V(0, 1); _recipeContent.anchorMax = V(1, 1);
            _recipeContent.pivot = V(0.5f, 1);
            _recipeContent.anchoredPosition = Vector2.zero;
            _recipeContent.sizeDelta = V(0, 0);
            sr.viewport = vpRT; sr.content = _recipeContent;
            _recipeScroll = sr;

            // RIGHT: Detail panel
            _detailPanel = MkRect(_windowPanel, "Right");
            MkImg(_detailPanel, C_Panel);
            _detailPanel.anchorMin = V(0.43f, 1); _detailPanel.anchorMax = V(1, 1);
            _detailPanel.pivot = V(0, 1);
            _detailPanel.offsetMin = V(0, -(y + panelH));
            _detailPanel.offsetMax = V(-6, -y);

            float dy = 4;

            // Item icon (top-left of detail panel)
            _detailIconRT = MkRect(_detailPanel, "DetailIcon");
            _detailIcon = MkImg(_detailIconRT, Color.clear);
            _detailIcon.raycastTarget = true;
            _detailIconRT.anchorMin = V(0, 1); _detailIconRT.anchorMax = V(0, 1);
            _detailIconRT.pivot = V(0, 1);
            _detailIconRT.anchoredPosition = V(6, -(dy));
            _detailIconRT.sizeDelta = V(36, 36);
            // Tooltip will be added dynamically in Refresh

            // Name (to the right of icon)
            _detailName = MkTxt(_detailPanel, "", 13, C_Gold, TextAnchor.UpperLeft);
            _detailName.fontStyle = FontStyle.Bold;
            _detailName.rectTransform.anchorMin = V(0, 1);
            _detailName.rectTransform.anchorMax = V(1, 1);
            _detailName.rectTransform.pivot = V(0.5f, 1);
            _detailName.rectTransform.offsetMin = V(46, -(dy + 20));
            _detailName.rectTransform.offsetMax = V(-6, -dy);
            dy += 40;

            _detailDesc = MkTxt(_detailPanel, "", 10, C_Dim, TextAnchor.UpperLeft);
            _detailDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceIn(_detailDesc.rectTransform, dy, 0, 6, 6); // zero height — hidden

            _detailReqSkill = MkTxt(_detailPanel, "", 11, Color.white, TextAnchor.MiddleLeft);
            _detailReqSkill.supportRichText = true;
            PlaceIn(_detailReqSkill.rectTransform, dy, 16, 6, 6); dy += 18;

            _detailTrivial = MkTxt(_detailPanel, "", 11, C_Dim, TextAnchor.MiddleLeft);
            _detailTrivial.supportRichText = true;
            PlaceIn(_detailTrivial.rectTransform, dy, 16, 6, 6); dy += 18;

            _detailChance = MkTxt(_detailPanel, "", 11, C_Green, TextAnchor.MiddleLeft);
            PlaceIn(_detailChance.rectTransform, dy, 16, 6, 6); dy += 20;

            // Materials header
            var matHdr = MkTxt(_detailPanel, "── Materials ──", 11, C_Accent, TextAnchor.MiddleCenter);
            matHdr.fontStyle = FontStyle.Bold;
            PlaceIn(matHdr.rectTransform, dy, 16, 6, 6); dy += 18;

            // Materials container (populated dynamically)
            _matsContainer = MkRect(_detailPanel, "Mats");
            _matsContainer.anchorMin = V(0, 1); _matsContainer.anchorMax = V(1, 1);
            _matsContainer.pivot = V(0.5f, 1);
            _matsContainer.offsetMin = V(6, -(dy + 120));
            _matsContainer.offsetMax = V(-6, -dy);
            dy += 122;

            _detailValue = MkTxt(_detailPanel, "", 11, C_Gold, TextAnchor.MiddleLeft);
            PlaceIn(_detailValue.rectTransform, dy, 16, 6, 6);

            // Combine progress bar
            float combineY = y + panelH + 4;
            var cbBg = MkRect(_windowPanel, "CombBg");
            MkImg(cbBg, C_BarBg); StretchH(cbBg, combineY, 16, 6, 6);
            _combineBarFill = MkRect(cbBg, "CombFill");
            MkImg(_combineBarFill, c(1f, 0.8f, 0.2f, 0.8f)).raycastTarget = false;
            _combineBarFill.anchorMin = V(0, 0); _combineBarFill.anchorMax = V(0, 1);
            _combineBarFill.pivot = V(0, 0.5f);
            _combineBarFill.offsetMin = V(1, 1); _combineBarFill.offsetMax = V(0, -1);
            _combineBarText = MkTxt(cbBg, "", 9, Color.white, TextAnchor.MiddleCenter);
            _combineBarText.fontStyle = FontStyle.Bold; Fill(_combineBarText.rectTransform);

            // Combine button (left half)
            float btnY = combineY + 20;
            var btnRT = MkRect(_windowPanel, "CombBtn");
            MkImg(btnRT, c(0.10f, 0.18f, 0.20f, 0.9f));
            btnRT.anchorMin = V(0, 1); btnRT.anchorMax = V(0.52f, 1);
            btnRT.pivot = V(0.5f, 1);
            btnRT.offsetMin = V(6, -(btnY + 30));
            btnRT.offsetMax = V(0, -btnY);
            _combineBtn = btnRT.gameObject.AddComponent<Button>();
            _combineBtn.targetGraphic = btnRT.GetComponent<Image>();
            _combineBtn.onClick.AddListener(OnCombineClick);
            _combineBtnText = MkTxt(btnRT, "Select a recipe", 12, C_Dim, TextAnchor.MiddleCenter);
            _combineBtnText.fontStyle = FontStyle.Bold;
            Fill(_combineBtnText.rectTransform);

            // Deconstruct button (right half)
            var dRT = MkRect(_windowPanel, "DeconBtn");
            MkImg(dRT, c(0.18f, 0.12f, 0.10f, 0.9f));
            dRT.anchorMin = V(0.53f, 1); dRT.anchorMax = V(1, 1);
            dRT.pivot = V(0.5f, 1);
            dRT.offsetMin = V(0, -(btnY + 30));
            dRT.offsetMax = V(-6, -btnY);
            _deconBtn = dRT.gameObject.AddComponent<Button>();
            _deconBtn.targetGraphic = dRT.GetComponent<Image>();
            _deconBtn.onClick.AddListener(OnDeconstructClick);
            _deconBtnText = MkTxt(dRT, "Deconstruct", 12, C_Dim, TextAnchor.MiddleCenter);
            _deconBtnText.fontStyle = FontStyle.Bold;
            Fill(_deconBtnText.rectTransform);

            // Status text (between button and footer)
            _combineStatusText = MkTxt(_windowPanel, "", 10, C_Dim, TextAnchor.MiddleCenter);
            StretchH(_combineStatusText.rectTransform, btnY + 32, 14, 6, 6);
            _combineStatusText.supportRichText = true;

            // Footer (at the very bottom, no overlap)
            _footerText = MkTxt(_windowPanel, "", 9, c(0.35f, 0.45f, 0.48f), TextAnchor.MiddleCenter);
            StretchH(_footerText.rectTransform, H - 16, 14, 6, 6);
        } // end Build()

        // ══════════════════════════════════════════════════════════
        // REFRESH — update all dynamic text/bars every frame
        // ══════════════════════════════════════════════════════════

        void Refresh()
        {
            var skill = GetSkillEntry();
            if (skill == null) return;
            int max = SkillsPlugin.CfgGlobalMaxLevel.Value;

            _titleText.text = _activeTradeskill;
            _skillLevelText.text = $"Skill: {skill.Level}/{max}";

            // XP bar
            if (skill.IsMaxLevel)
            {
                _xpBarFill.anchorMax = V(1, 1); _xpBarFill.offsetMax = V(-1, -1);
                _xpBarText.text = "";
            }
            else
            {
                float f = Mathf.Clamp01(skill.LevelProgress);
                _xpBarFill.anchorMax = V(Mathf.Max(f, 0.002f), 1);
                _xpBarFill.offsetMax = V(-1, -1);
                _xpBarText.text = "";
            }

            // Combine bar
            if (_isCombining)
            {
                float p = Mathf.Clamp01((Time.time - _combineStartTime) / COMBINE_DUR);
                _combineBarFill.anchorMax = V(Mathf.Max(p, 0.002f), 1);
                _combineBarFill.offsetMax = V(-1, -1);
                _combineBarText.text = "Combining...";
            }
            else
            {
                _combineBarFill.anchorMax = V(0, 1);
                _combineBarText.text = "";
            }

            // Populate recipe list
            PopulateRecipeList(skill);

            // Populate detail panel
            if (_selectedRecipeIndex >= 0 && _selectedRecipeIndex < _filtered.Count)
            {
                var r = _filtered[_selectedRecipeIndex];
                bool canAttempt = skill.Level >= r.MinSkillLevel;
                bool trivial = skill.Level >= r.TrivialLevel;

                _detailName.text = r.Name;
                _detailDesc.text = "";

                // Set icon
                var iconSprite = GetItemIcon(r.Name);
                if (iconSprite != null)
                {
                    _detailIcon.sprite = iconSprite;
                    _detailIcon.color = Color.white;
                }
                else
                {
                    _detailIcon.sprite = null;
                    _detailIcon.color = Color.clear;
                }
                // Attach/update tooltip on icon
                var tt = _detailIconRT.GetComponent<RecipeTooltip>();
                if (tt == null) tt = _detailIconRT.gameObject.AddComponent<RecipeTooltip>();
                tt.ItemName = r.Name;
                _detailReqSkill.text = canAttempt
                    ? $"Required: <color=#4CAF50>{r.MinSkillLevel}</color>"
                    : $"Required: <color=#EF5350>{r.MinSkillLevel}</color> (need {r.MinSkillLevel - skill.Level} more)";
                _detailTrivial.text = trivial
                    ? $"Trivial: <color=#666666>{r.TrivialLevel} (no XP)</color>"
                    : $"Trivial at: {r.TrivialLevel}";

                if (canAttempt)
                {
                    float ch = Mathf.Clamp(40f + (skill.Level - r.MinSkillLevel) * 1.5f, 15f, 95f);
                    Color cc = ch >= 70f ? C_Green : (ch >= 40f ? C_Accent : C_Red);
                    _detailChance.text = $"Success: {ch:F0}%";
                    _detailChance.color = cc;
                }
                else { _detailChance.text = ""; }

                _detailValue.text = $"Value: {r.GoldValue}g";
                PopulateMaterials(r);

                // Combine button state
                bool hasAll = HasAllMaterials(r);
                bool canCombine = canAttempt && hasAll && !_isCombining;
                _combineBtnText.text = canCombine ? "Combine" : "Cannot Combine";
                _combineBtnText.color = canCombine ? C_Gold : C_Dim;

                // Deconstruct button: need the item in inventory + know the recipe
                bool hasItem = CountPlayerItem(r.Name) > 0;
                _deconBtnText.text = hasItem ? "Deconstruct" : "No Item";
                _deconBtnText.color = hasItem ? C_Accent : C_Dim;
            }
            else
            {
                _detailName.text = ""; _detailDesc.text = "";
                _detailReqSkill.text = ""; _detailTrivial.text = "";
                _detailChance.text = ""; _detailValue.text = "";
                ClearMaterials();
                _combineBtnText.text = "Select a recipe"; _combineBtnText.color = C_Dim;
                _deconBtnText.text = "Deconstruct"; _deconBtnText.color = C_Dim;
                // Clear icon
                if (_detailIcon != null) { _detailIcon.sprite = null; _detailIcon.color = Color.clear; }
            }

            _footerText.text = "";
        }

        // ══════════════════════════════════════════════════════════
        // RECIPE LIST POPULATION
        // ══════════════════════════════════════════════════════════

        List<GameObject> _recipeRows = new List<GameObject>();

        // Icon cache for recipe result items
        static Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();
        static string _iconDir;

        static Sprite GetItemIcon(string itemName)
        {
            if (_iconCache.TryGetValue(itemName, out var cached)) return cached;
            
            if (_iconDir == null)
                _iconDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(SkillsPlugin.Instance.Info.Location),
                    "ClassicSkills", "Icons");

            string path = System.IO.Path.Combine(_iconDir, itemName + ".png");
            if (!System.IO.File.Exists(path))
            {
                _iconCache[itemName] = null;
                return null;
            }

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.LoadImage(data);
                var sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
                _iconCache[itemName] = sprite;
                return sprite;
            }
            catch
            {
                _iconCache[itemName] = null;
                return null;
            }
        }

        void PopulateRecipeList(SkillEntry skill)
        {
            // Rebuild if count changed
            if (_recipeRows.Count == _filtered.Count) return;

            ClearRecipeRows();
            float ry = 2;
            for (int i = 0; i < _filtered.Count; i++)
            {
                var recipe = _filtered[i];
                bool canAttempt = skill.Level >= recipe.MinSkillLevel;
                bool trivial = skill.Level >= recipe.TrivialLevel;
                int idx = i; // capture for closure

                var rowRT = MkRect(_recipeContent, recipe.Name);
                MkImg(rowRT, idx == _selectedRecipeIndex
                    ? c(0.15f, 0.25f, 0.28f, 0.6f) : Color.clear);
                rowRT.anchorMin = V(0, 1); rowRT.anchorMax = V(1, 1);
                rowRT.pivot = V(0.5f, 1);
                rowRT.offsetMin = V(2, -(ry + 22));
                rowRT.offsetMax = V(-2, -ry);

                // Level tag (right side)
                string lvl = trivial ? "<color=#666>T</color>"
                    : canAttempt ? $"<color=#4CAF50>{recipe.MinSkillLevel}</color>"
                    : $"<color=#EF5350>{recipe.MinSkillLevel}</color>";
                var lvlTxt = MkTxt(rowRT, lvl, 10, Color.white, TextAnchor.MiddleRight);
                lvlTxt.supportRichText = true;
                lvlTxt.rectTransform.anchorMin = V(0.85f, 0);
                lvlTxt.rectTransform.anchorMax = V(1, 1);
                lvlTxt.rectTransform.offsetMin = V(0, 0);
                lvlTxt.rectTransform.offsetMax = V(-2, 0);

                // Recipe name
                Color nameCol = idx == _selectedRecipeIndex ? C_Gold
                    : canAttempt ? Color.white : C_Dim;
                var nameTxt = MkTxt(rowRT, recipe.Name, 11, nameCol, TextAnchor.MiddleLeft);
                if (idx == _selectedRecipeIndex) nameTxt.fontStyle = FontStyle.Bold;
                nameTxt.rectTransform.anchorMin = V(0, 0);
                nameTxt.rectTransform.anchorMax = V(0.85f, 1);
                nameTxt.rectTransform.offsetMin = V(4, 0);
                nameTxt.rectTransform.offsetMax = V(0, 0);

                // Click handler
                var clickBtn = rowRT.gameObject.AddComponent<Button>();
                clickBtn.targetGraphic = rowRT.GetComponent<Image>();
                clickBtn.onClick.AddListener(() =>
                {
                    _selectedRecipeIndex = idx;
                    _combineStatusText.text = "";
                    ClearRecipeRows();
                });

                _recipeRows.Add(rowRT.gameObject);
                ry += 22;
            }

            _recipeContent.sizeDelta = V(0, ry + 2);
        }

        void ClearRecipeRows()
        {
            foreach (var go in _recipeRows) if (go) Destroy(go);
            _recipeRows.Clear();
        }

        // ══════════════════════════════════════════════════════════
        // MATERIALS DISPLAY
        // ══════════════════════════════════════════════════════════

        List<GameObject> _matRows = new List<GameObject>();

        void PopulateMaterials(Skills.Tradeskills.Recipe recipe)
        {
            ClearMaterials();
            float my = 0;
            for (int i = 0; i < recipe.Materials.Length; i++)
            {
                string matName = recipe.Materials[i];
                int required = recipe.MaterialCounts[i];
                int have = CountPlayerItem(matName);
                bool enough = have >= required;

                // Indicator
                var indTxt = MkTxt(_matsContainer, enough ? "\u2713" : "\u2717", 11,
                    enough ? C_Green : C_Red, TextAnchor.MiddleLeft);
                indTxt.fontStyle = FontStyle.Bold;
                indTxt.rectTransform.anchorMin = V(0, 1);
                indTxt.rectTransform.anchorMax = V(0.08f, 1);
                indTxt.rectTransform.pivot = V(0, 1);
                indTxt.rectTransform.offsetMin = V(0, -(my + 16));
                indTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(indTxt.gameObject);

                // Name
                var nTxt = MkTxt(_matsContainer, matName, 11,
                    enough ? Color.white : C_Red, TextAnchor.MiddleLeft);
                nTxt.rectTransform.anchorMin = V(0.08f, 1);
                nTxt.rectTransform.anchorMax = V(0.78f, 1);
                nTxt.rectTransform.pivot = V(0, 1);
                nTxt.rectTransform.offsetMin = V(0, -(my + 16));
                nTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(nTxt.gameObject);

                // Count
                var cTxt = MkTxt(_matsContainer, $"{have}/{required}", 11,
                    enough ? C_Green : C_Red, TextAnchor.MiddleRight);
                cTxt.fontStyle = FontStyle.Bold;
                cTxt.rectTransform.anchorMin = V(0.78f, 1);
                cTxt.rectTransform.anchorMax = V(1, 1);
                cTxt.rectTransform.pivot = V(1, 1);
                cTxt.rectTransform.offsetMin = V(0, -(my + 16));
                cTxt.rectTransform.offsetMax = V(0, -my);
                _matRows.Add(cTxt.gameObject);

                my += 18;
            }
        }

        void ClearMaterials()
        {
            foreach (var go in _matRows) if (go) Destroy(go);
            _matRows.Clear();
        }

        // ══════════════════════════════════════════════════════════
        // COMBINE LOGIC
        // ══════════════════════════════════════════════════════════

        void OnCombineClick()
        {
            if (_isCombining) return;
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;

            var recipe = _filtered[_selectedRecipeIndex];
            var skill = GetSkillEntry();
            if (skill == null) return;

            bool canAttempt = skill.Level >= recipe.MinSkillLevel;
            bool hasAll = HasAllMaterials(recipe);

            if (!canAttempt)
            {
                _combineStatusText.text = $"<color=#EF5350>Need {_activeTradeskill} level {recipe.MinSkillLevel}.</color>";
                return;
            }
            if (!hasAll)
            {
                _combineStatusText.text = "<color=#EF5350>Missing required materials.</color>";
                return;
            }

            _isCombining = true;
            _combineStartTime = Time.time;
            _combineStatusText.text = "";
        }

        void ExecuteCombine()
        {
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;
            var recipe = _filtered[_selectedRecipeIndex];
            Skills.Tradeskills.AttemptCombine(_activeTradeskill, recipe.Name);
            RefreshRecipeList();
            ClearRecipeRows();
            ClearMaterials();
        }

        void OnDeconstructClick()
        {
            if (_isCombining) return;
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _filtered.Count) return;

            var recipe = _filtered[_selectedRecipeIndex];

            // Check if player has the item
            int have = CountPlayerItem(recipe.Name);
            if (have <= 0)
            {
                _combineStatusText.text = $"<color=#EF5350>You don't have a {recipe.Name} to deconstruct.</color>";
                return;
            }

            Skills.Tradeskills.AttemptDeconstruct(_activeTradeskill, recipe.Name);
            RefreshRecipeList();
            ClearRecipeRows();
            ClearMaterials();
        }

        // ══════════════════════════════════════════════════════════
        // RECIPE FILTERING
        // ══════════════════════════════════════════════════════════

        static void RefreshRecipeList()
        {
            _filtered.Clear();
            var all = Skills.Tradeskills.GetRecipesForSkillPublic(_activeTradeskill);
            if (all == null) return;

            var skill = GetSkillEntry();
            string search = _searchText.ToLower();

            foreach (var r in all)
            {
                // Only show recipes the player has learned
                if (!SkillsSaveManager.Data.IsRecipeKnown(r.Name)) continue;
                if (search.Length > 0 && !r.Name.ToLower().Contains(search)) continue;
                if (_showOnlyAvailable)
                {
                    if (skill.Level < r.MinSkillLevel) continue;
                    if (!HasAllMaterials(r)) continue;
                }
                _filtered.Add(r);
            }
            if (_selectedRecipeIndex >= _filtered.Count)
                _selectedRecipeIndex = _filtered.Count - 1;
        }

        // ══════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════

        static SkillEntry GetSkillEntry()
        {
            var d = SkillsSaveManager.Data;
            switch (_activeTradeskill)
            {
                case "Smithing":   return d.Smithing;
                case "Baking":     return d.Baking;
                case "Brewing":    return d.Brewing;
                case "Fletching":  return d.Fletching;
                case "Jewelcraft": return d.Jewelcraft;
                case "Tailoring":  return d.Tailoring;
                default: return null;
            }
        }

        static string GetIcon()
        {
            switch (_activeTradeskill)
            {
                case "Smithing":   return "\u2692";
                case "Baking":     return "\u25CB";
                case "Brewing":    return "\u2615";
                case "Fletching":  return "\u2191";
                case "Jewelcraft": return "\u25C6";
                case "Tailoring":  return "\u2702";
                default: return "\u2699";
            }
        }

        static int GetRecipeCostUI(int trivialLevel)
        {
            if (trivialLevel <= 20) return 10;
            if (trivialLevel <= 40) return 50;
            if (trivialLevel <= 60) return 150;
            if (trivialLevel <= 80) return 400;
            if (trivialLevel <= 100) return 800;
            if (trivialLevel <= 120) return 1500;
            if (trivialLevel <= 140) return 3000;
            if (trivialLevel <= 160) return 6000;
            return 10000;
        }

        static int CountPlayerItem(string itemName)
        {
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots == null) return 0;
                int c = 0;
                foreach (var slot in inv.StoredSlots)
                {
                    if (slot != null && slot.MyItem != null &&
                        slot.MyItem.ItemName == itemName)
                        c += Mathf.Max(1, slot.Quantity);
                }
                return c;
            }
            catch { return 0; }
        }

        static bool HasAllMaterials(Skills.Tradeskills.Recipe recipe)
        {
            for (int i = 0; i < recipe.Materials.Length; i++)
                if (CountPlayerItem(recipe.Materials[i]) < recipe.MaterialCounts[i])
                    return false;
            return true;
        }

        void SetVis(bool v)
        {
            if (_canvasGO) _canvasGO.SetActive(v);
            if (!v) { try { GameData.PlayerTyping = false; } catch { } }
        }

        // ══════════════════════════════════════════════════════════
        // UI PRIMITIVES (same as SkillsUI)
        // ══════════════════════════════════════════════════════════

        static Texture2D _wt; static Sprite _ws;
        static Sprite WS { get {
            if (!_ws) { if (!_wt) { _wt = new Texture2D(4,4);
                var p = new Color[16]; for (int i=0;i<16;i++) p[i]=Color.white;
                _wt.SetPixels(p); _wt.Apply(); }
                _ws = Sprite.Create(_wt, new UnityEngine.Rect(0,0,4,4), V(0.5f,0.5f)); }
            return _ws; } }

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static RectTransform MkRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image MkImg(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WS; img.color = color;
            img.type = Image.Type.Simple; img.raycastTarget = true;
            return img;
        }

        static Text MkTxt(Transform parent, string text, int size,
            Color color, TextAnchor anchor)
        {
            var rt = MkRect(parent, "T");
            var t = rt.gameObject.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size; t.color = color; t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void StretchH(RectTransform rt, float y, float h,
            float padL = 0, float padR = 0)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        /// <summary>Place rect inside parent at Y from top, stretching width with padding.</summary>
        static void PlaceIn(RectTransform rt, float y, float h, float padL, float padR)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        // ── Helper components (reused from SkillsUI pattern) ──────

        class InputBlocker : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerEnter(PointerEventData e)
            { try { GameData.PlayerTyping = true; } catch { } }
            public void OnPointerExit(PointerEventData e)
            { try { GameData.PlayerTyping = false; } catch { } }
        }

        class WindowDragger : MonoBehaviour,
            IDragHandler, IBeginDragHandler
        {
            public RectTransform target;
            Vector2 _off;
            public void OnBeginDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                _off = target.anchoredPosition - lp;
            }
            public void OnDrag(PointerEventData e)
            {
                if (!target) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                target.anchoredPosition = lp + _off;
            }
        }

        /// <summary>Uses the game's native ItemInfoWindow to show item tooltips on hover.</summary>
        class RecipeTooltip : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            public string ItemName;

            public void OnPointerEnter(PointerEventData e)
            {
                try
                {
                    if (GameData.ItemInfoWindow == null) return;
                    if (string.IsNullOrEmpty(ItemName)) return;

                    // Find the Item object — check our custom items first, then game DB
                    Item item = Items.ItemFactory.GetItem(ItemName);
                    if (item == null)
                    {
                        // Search game's ItemDB by name
                        var db = GameData.ItemDB;
                        if (db?.ItemDB != null)
                        {
                            foreach (var gi in db.ItemDB)
                            {
                                if (gi != null && gi.ItemName == ItemName)
                                {
                                    item = gi;
                                    break;
                                }
                            }
                        }
                    }

                    if (item == null) return;

                    // Position tooltip near the icon
                    Vector2 pos = (Vector2)Input.mousePosition + new Vector2(20f, 0f);
                    GameData.ItemInfoWindow.DisplayItem(item, pos, 1);

                    // Bring tooltip canvas to front so it's above our window
                    var tooltipCanvas = GameData.ItemInfoWindow.GetComponentInParent<Canvas>();
                    if (tooltipCanvas != null && tooltipCanvas.sortingOrder <= 101)
                        tooltipCanvas.sortingOrder = 200;
                }
                catch { }
            }

            public void OnPointerExit(PointerEventData e)
            {
                try
                {
                    if (GameData.ItemInfoWindow != null)
                        GameData.ItemInfoWindow.CloseItemWindow();
                }
                catch { }
            }
        }
    }
}
