using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ErenshorSkills
{
    /// <summary>
    /// uGUI Canvas-based Classic Skills window (v3).
    /// All positioning is manual — no LayoutGroups or ContentSizeFitter.
    /// Each row is placed at an explicit Y offset inside the scroll content,
    /// and the content height is set to the total of all rows.
    /// </summary>
    public class SkillsUI : MonoBehaviour
    {
        static SkillsUI _instance;
        static bool _showWindow;
        public static bool IsWindowVisible => _showWindow;

        GameObject _canvasGO;
        RectTransform _windowPanel;
        RectTransform _contentParent;
        Text _titleText;

        readonly List<SkillRowUI> _utilRows = new List<SkillRowUI>();
        readonly List<CombatRowUI> _combatRows = new List<CombatRowUI>();
        readonly List<SkillRowUI> _tradeRows = new List<SkillRowUI>();
        readonly List<CombatRowUI> _magicRows = new List<CombatRowUI>();
        readonly List<BonusRowUI> _bonusRows = new List<BonusRowUI>();
        bool _built;

        // ── Colors (matched to game's inventory panel style) ─────────
        static readonly Color C_WinBg    = c(0.08f, 0.12f, 0.15f, 0.96f);  // Dark teal-blue
        static readonly Color C_Panel    = c(0.10f, 0.16f, 0.20f, 0.92f);  // Slightly lighter teal
        static readonly Color C_Title    = c(0.06f, 0.10f, 0.13f, 0.98f);  // Darkest teal for title
        static readonly Color C_Border   = c(0.22f, 0.36f, 0.40f, 0.9f);   // Teal/cyan border
        static readonly Color C_BarBg    = c(0.04f, 0.07f, 0.09f, 0.85f);  // Very dark teal bar bg
        static readonly Color C_Gold     = c(1f, 0.84f, 0f);
        static readonly Color C_Dim      = c(0.55f, 0.55f, 0.55f);
        static readonly Color C_Combat   = c(1f, 0.43f, 0.25f);
        static readonly Color C_Trade    = c(1f, 0.6f, 0f);
        static readonly Color C_Magic    = c(0.6f, 0.4f, 1f);
        static Color c(float r, float g, float b, float a = 1) => new Color(r, g, b, a);

        static readonly Color[] UtilC = {
            c(0.55f,0.76f,0.29f), c(0.31f,0.76f,0.97f), c(1f,0.65f,0.15f),
            c(0.94f,0.33f,0.31f), c(0.81f,0.58f,0.85f), c(1f,0.44f,0.26f),
            c(1f,0.67f,0.25f),    c(0.74f,0.67f,0.64f),
        };

        const float W = 390, H = 720, TITLE = 30, PAD = 6;
        const float ROW = 42, CROW = 44, HDR = 24, GAP = 3;

        // ══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════════════════════

        public static void ToggleWindow()
        {
            _showWindow = !_showWindow;
            if (_instance) _instance.SetVis(_showWindow);
            SkillsPlugin.Log.LogInfo($"SkillsUI: toggled {_showWindow}");
        }
        public static void DoOnGUI() { }

        // ══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════════════════════

        void Awake() => _instance = this;

        void Start()
        {
            try { Build(); _built = true; SetVis(false);
                SkillsPlugin.Log.LogInfo(
                    $"SkillsUI: built. util={_utilRows.Count} combat={_combatRows.Count} trade={_tradeRows.Count}");
            } catch (Exception ex) { SkillsPlugin.Log.LogError($"SkillsUI build: {ex}"); }
        }

        void LateUpdate()
        {
            if (_built && _showWindow && _canvasGO && _canvasGO.activeSelf)
                Refresh();
        }

        void OnDestroy() { if (_canvasGO) Destroy(_canvasGO); }

        // ══════════════════════════════════════════════════════════════
        // BUILD
        // ══════════════════════════════════════════════════════════════

        void Build()
        {
            if (!FindObjectOfType<EventSystem>())
            {
                var es = new GameObject("CS_ES");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(es);
            }

            _canvasGO = new GameObject("CS_Canvas");
            DontDestroyOnLoad(_canvasGO);
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 100;
            var sc = _canvasGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Window
            _windowPanel = Rect(_canvasGO.transform, "Win");
            Img(_windowPanel, C_WinBg);
            _windowPanel.anchorMin = V(0, 1); _windowPanel.anchorMax = V(0, 1);
            _windowPanel.pivot = V(0, 1);
            _windowPanel.anchoredPosition = V(20, -60);
            _windowPanel.sizeDelta = V(W, H);
            _windowPanel.gameObject.AddComponent<Outline>().effectColor = C_Border;
            _windowPanel.gameObject.GetComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);
            _windowPanel.gameObject.AddComponent<InputBlocker>();

            // Title bar (matching game's teal header style)
            var titleRT = Rect(_windowPanel, "Title");
            Img(titleRT, C_Title);
            StretchH(titleRT, 0, TITLE);
            titleRT.gameObject.AddComponent<WindowDragger>().target = _windowPanel;
            // Add border line at bottom of title
            var titleBorder = Rect(titleRT, "TBorder");
            Img(titleBorder, C_Border);
            titleBorder.anchorMin = V(0, 0); titleBorder.anchorMax = V(1, 0);
            titleBorder.pivot = V(0.5f, 0); titleBorder.sizeDelta = V(0, 1);
            titleBorder.anchoredPosition = V(0, 0);

            // Close button FIRST (so it renders on top and gets click priority)
            var clRT = Rect(titleRT, "Close");
            Img(clRT, c(0.6f, 0.15f, 0.15f, 0.9f));
            clRT.anchorMin = V(1, 0.5f); clRT.anchorMax = V(1, 0.5f);
            clRT.pivot = V(1, 0.5f);
            clRT.anchoredPosition = V(-4, 0); clRT.sizeDelta = V(22, 22);
            var btn = clRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = clRT.GetComponent<Image>();
            btn.onClick.AddListener(() => ToggleWindow());
            var ct = Txt(clRT, "\u2715", 12, Color.white, TextAnchor.MiddleCenter);
            ct.fontStyle = FontStyle.Bold; Fill(ct.rectTransform);

            // Title text (after close button; don't cover close button area)
            _titleText = Txt(titleRT, "Skills", 16, Color.white, TextAnchor.MiddleCenter);
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.rectTransform.anchorMin = V(0, 0);
            _titleText.rectTransform.anchorMax = V(1, 1);
            _titleText.rectTransform.offsetMin = V(8, 0);
            _titleText.rectTransform.offsetMax = V(-30, 0);
            _titleText.raycastTarget = false;

            float y = TITLE + 4;

            // Scroll area dimensions
            float bonusH = 128, footerH = 20;
            float scrollH = H - y - bonusH - footerH - 8;

            // Build scroll
            BuildScroll(y, scrollH);

            // Populate content — track Y cursor inside content
            float cy = 2; // top padding
            cy = PlaceHeader(cy, "Utility Skills", C_Gold);
            var utils = SkillsSaveManager.Data.GetUtilitySkills();
            for (int i = 0; i < utils.Count; i++)
            {
                Color col = i < UtilC.Length ? UtilC[i] : Color.white;
                _utilRows.Add(PlaceSkillRow(ref cy, utils[i], col));
            }

            cy = PlaceHeader(cy, "Tradeskills", C_Trade);
            foreach (var s in SkillsSaveManager.Data.GetTradeskills())
                _tradeRows.Add(PlaceSkillRow(ref cy, s, C_Trade));

            cy = PlaceHeader(cy, "Combat Skills", C_Combat, showBonus: true);
            if (SkillsPlugin.CfgEnableCombatSkills.Value)
            {
                foreach (var info in Skills.CombatSkills.GetActiveCombatSkills())
                    _combatRows.Add(PlaceCombatRow(ref cy, info));
            }
            else
            {
                var ph = Txt(_contentParent, "  Combat skills disabled.", 11, C_Dim, TextAnchor.MiddleLeft);
                PlaceAt(ph.rectTransform, cy, 22); cy += 22 + GAP;
            }

            cy = PlaceHeader(cy, "Magic Skills", C_Magic, showBonus: true, bonusLabel: "Bonus Effect");
            foreach (var s in SkillsSaveManager.Data.GetMagicSkills())
                _magicRows.Add(PlaceCombatRow(ref cy, new Skills.CombatSkills.CombatSkillInfo { Name = s.Name, Skill = s, Bonus = 0 }, C_Magic));

            cy += 2; // bottom padding

            // SET CONTENT HEIGHT — this is the key line
            _contentParent.sizeDelta = new Vector2(0, cy);

            SkillsPlugin.Log.LogInfo($"SkillsUI: content height = {cy:F0}px, " +
                $"scroll viewport = {scrollH:F0}px");

            // Bonus panel
            BuildBonusPanel(y + scrollH + 4, bonusH);

            // Footer
            var ft = Txt(_windowPanel,
                $"F8 to close  |  /skills in chat",
                10, c(0.35f, 0.45f, 0.48f), TextAnchor.MiddleCenter);
            StretchH(ft.rectTransform, H - footerH - 2, footerH);
        }

        // ══════════════════════════════════════════════════════════════
        // SCROLL
        // ══════════════════════════════════════════════════════════════

        void BuildScroll(float yTop, float h)
        {
            var sRT = Rect(_windowPanel, "Scroll");
            Img(sRT, Color.clear);
            StretchH(sRT, yTop, h, 4, 4);

            var sr = sRT.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25;

            var vRT = Rect(sRT, "Viewport");
            Img(vRT, Color.clear);
            Fill(vRT);
            vRT.gameObject.AddComponent<RectMask2D>();

            _contentParent = Rect(vRT, "Content");
            _contentParent.anchorMin = V(0, 1);
            _contentParent.anchorMax = V(1, 1);
            _contentParent.pivot = V(0.5f, 1);
            _contentParent.anchoredPosition = Vector2.zero;
            _contentParent.sizeDelta = V(0, 0); // will be set after populating

            sr.viewport = vRT;
            sr.content = _contentParent;

            // Scrollbar
            var sbRT = Rect(sRT, "SB");
            Img(sbRT, c(0.10f, 0.16f, 0.20f, 0.5f));
            sbRT.anchorMin = V(1, 0); sbRT.anchorMax = V(1, 1);
            sbRT.pivot = V(1, 0.5f);
            sbRT.sizeDelta = V(6, 0); sbRT.anchoredPosition = V(3, 0);

            var sb = sbRT.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var area = Rect(sbRT, "Area"); Fill(area);
            var handle = Rect(area, "Handle");
            Img(handle, c(0.30f, 0.45f, 0.50f, 0.7f)); Fill(handle);
            sb.handleRect = handle;
            sb.targetGraphic = handle.GetComponent<Image>();
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        // ══════════════════════════════════════════════════════════════
        // PLACE ROWS (manual Y positioning inside content rect)
        // ══════════════════════════════════════════════════════════════

        float PlaceHeader(float y, string text, Color color, bool showBonus = false, string bonusLabel = "Bonus DMG")
        {
            var t = Txt(_contentParent, text, 14, color, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            PlaceAt(t.rectTransform, y, HDR);
            y += HDR;

            // Column headers
            var hdrRT = Rect(_contentParent, "ColHdr");
            PlaceAt(hdrRT, y, 14);

            var nameH = Txt(hdrRT, "Skill", 9, C_Dim, TextAnchor.MiddleLeft);
            nameH.rectTransform.anchorMin = V(0, 0);
            nameH.rectTransform.anchorMax = V(0.65f, 1);
            nameH.rectTransform.offsetMin = V(PAD, 0);
            nameH.rectTransform.offsetMax = V(0, 0);

            if (showBonus)
            {
                // Skill | Bonus column | Level
                var bonH = Txt(hdrRT, bonusLabel, 9, C_Dim, TextAnchor.MiddleRight);
                bonH.rectTransform.anchorMin = V(0.65f, 0);
                bonH.rectTransform.anchorMax = V(0.82f, 1);
                bonH.rectTransform.offsetMin = V(0, 0);
                bonH.rectTransform.offsetMax = V(0, 0);

                var lvlH = Txt(hdrRT, "Level", 9, C_Dim, TextAnchor.MiddleRight);
                lvlH.rectTransform.anchorMin = V(0.82f, 0);
                lvlH.rectTransform.anchorMax = V(1, 1);
                lvlH.rectTransform.offsetMin = V(0, 0);
                lvlH.rectTransform.offsetMax = V(-PAD, 0);
            }
            else
            {
                // Utility/Trade: Skill | Level (right-aligned)
                var lvlH = Txt(hdrRT, "Level", 9, C_Dim, TextAnchor.MiddleRight);
                lvlH.rectTransform.anchorMin = V(0.65f, 0);
                lvlH.rectTransform.anchorMax = V(1, 1);
                lvlH.rectTransform.offsetMin = V(0, 0);
                lvlH.rectTransform.offsetMax = V(-PAD, 0);
            }

            return y + 14 + GAP;
        }

        SkillRowUI PlaceSkillRow(ref float y, SkillEntry skill, Color accent)
        {
            var row = new SkillRowUI();
            row.skill = skill;

            // Container
            var cRT = Rect(_contentParent, skill.Name);
            Img(cRT, C_Panel);
            PlaceAt(cRT, y, ROW);

            // Name (top-left, top 20px of row)
            row.nameText = Txt(cRT, skill.Name, 12, accent, TextAnchor.MiddleLeft);
            row.nameText.fontStyle = FontStyle.Bold;
            row.nameText.rectTransform.anchorMin = V(0, 1);
            row.nameText.rectTransform.anchorMax = V(0.55f, 1);
            row.nameText.rectTransform.pivot = V(0, 1);
            row.nameText.rectTransform.offsetMin = V(PAD, -20);
            row.nameText.rectTransform.offsetMax = V(0, -2);

            // Train "+" button (top-right, before level)
            var btnRT = Rect(cRT, "Train");
            Img(btnRT, c(0.12f, 0.40f, 0.30f, 0.9f));
            btnRT.anchorMin = V(0.55f, 1); btnRT.anchorMax = V(0.55f, 1);
            btnRT.pivot = V(0, 1);
            btnRT.anchoredPosition = V(0, -3); btnRT.sizeDelta = V(18, 16);
            row.trainBtn = btnRT.gameObject.AddComponent<Button>();
            row.trainBtn.targetGraphic = btnRT.GetComponent<Image>();
            var btnTxt = Txt(btnRT, "+", 11, Color.white, TextAnchor.MiddleCenter);
            btnTxt.fontStyle = FontStyle.Bold; Fill(btnTxt.rectTransform);
            var thisRow = row;
            row.trainBtn.onClick.AddListener(() => OnTrainClick(thisRow.skill));

            // Level (top-right)
            row.levelText = Txt(cRT, "", 12, Color.white, TextAnchor.MiddleRight);
            row.levelText.fontStyle = FontStyle.Bold;
            row.levelText.rectTransform.anchorMin = V(0.65f, 1);
            row.levelText.rectTransform.anchorMax = V(1, 1);
            row.levelText.rectTransform.pivot = V(1, 1);
            row.levelText.rectTransform.offsetMin = V(0, -20);
            row.levelText.rectTransform.offsetMax = V(-PAD, -2);

            // XP bar (middle band: y=22 to y=36 from top)
            var barRT = Rect(cRT, "Bar");
            Img(barRT, C_BarBg);
            barRT.anchorMin = V(0, 1);
            barRT.anchorMax = V(1, 1);
            barRT.pivot = V(0.5f, 1);
            barRT.offsetMin = V(PAD, -36);
            barRT.offsetMax = V(-PAD, -22);

            // Fill
            var fillRT = Rect(barRT, "Fill");
            Img(fillRT, accent * 0.7f).raycastTarget = false;
            fillRT.anchorMin = V(0, 0); fillRT.anchorMax = V(0, 1);
            fillRT.pivot = V(0, 0.5f);
            fillRT.offsetMin = V(1, 1); fillRT.offsetMax = V(0, -1);
            row.barFill = fillRT;

            // XP text overlay on bar
            row.xpText = Txt(barRT, "", 9, Color.white, TextAnchor.MiddleCenter);
            row.xpText.fontStyle = FontStyle.Bold;
            Fill(row.xpText.rectTransform);

            y += ROW + GAP;
            return row;
        }

        CombatRowUI PlaceCombatRow(ref float y, Skills.CombatSkills.CombatSkillInfo info, Color? overrideColor = null)
        {
            var row = new CombatRowUI();
            row.skill = info.Skill;
            Color accent = overrideColor ?? C_Combat;

            var cRT = Rect(_contentParent, info.Name);
            Img(cRT, C_Panel);
            PlaceAt(cRT, y, ROW);

            // Name (top-left)
            row.nameText = Txt(cRT, info.Name, 12, accent, TextAnchor.MiddleLeft);
            row.nameText.fontStyle = FontStyle.Bold;
            row.nameText.rectTransform.anchorMin = V(0, 1);
            row.nameText.rectTransform.anchorMax = V(0.50f, 1);
            row.nameText.rectTransform.pivot = V(0, 1);
            row.nameText.rectTransform.offsetMin = V(PAD, -20);
            row.nameText.rectTransform.offsetMax = V(0, -2);

            // Train "+" button
            var btnRT = Rect(cRT, "Train");
            Img(btnRT, c(0.12f, 0.40f, 0.30f, 0.9f));
            btnRT.anchorMin = V(0.50f, 1); btnRT.anchorMax = V(0.50f, 1);
            btnRT.pivot = V(0, 1);
            btnRT.anchoredPosition = V(0, -3); btnRT.sizeDelta = V(18, 16);
            row.trainBtn = btnRT.gameObject.AddComponent<Button>();
            row.trainBtn.targetGraphic = btnRT.GetComponent<Image>();
            var btnTxt = Txt(btnRT, "+", 11, Color.white, TextAnchor.MiddleCenter);
            btnTxt.fontStyle = FontStyle.Bold; Fill(btnTxt.rectTransform);
            var thisRow = row;
            row.trainBtn.onClick.AddListener(() => OnTrainClick(thisRow.skill));

            // Bonus (before level, matching utility skill alignment)
            row.bonusText = Txt(cRT, "", 12, accent, TextAnchor.MiddleRight);
            row.bonusText.fontStyle = FontStyle.Bold;
            row.bonusText.rectTransform.anchorMin = V(0.65f, 1);
            row.bonusText.rectTransform.anchorMax = V(0.82f, 1);
            row.bonusText.rectTransform.pivot = V(1, 1);
            row.bonusText.rectTransform.offsetMin = V(0, -20);
            row.bonusText.rectTransform.offsetMax = V(0, -2);

            // Level (far right, same position as utility skills)
            row.levelText = Txt(cRT, "", 12, Color.white, TextAnchor.MiddleRight);
            row.levelText.fontStyle = FontStyle.Bold;
            row.levelText.rectTransform.anchorMin = V(0.82f, 1);
            row.levelText.rectTransform.anchorMax = V(1, 1);
            row.levelText.rectTransform.pivot = V(1, 1);
            row.levelText.rectTransform.offsetMin = V(0, -20);
            row.levelText.rectTransform.offsetMax = V(-PAD, -2);

            // XP bar (middle band: y=22 to y=36 from top)
            var barRT = Rect(cRT, "Bar");
            Img(barRT, C_BarBg);
            barRT.anchorMin = V(0, 1);
            barRT.anchorMax = V(1, 1);
            barRT.pivot = V(0.5f, 1);
            barRT.offsetMin = V(PAD, -36);
            barRT.offsetMax = V(-PAD, -22);

            var fillRT = Rect(barRT, "Fill");
            Img(fillRT, accent * 0.6f).raycastTarget = false;
            fillRT.anchorMin = V(0, 0); fillRT.anchorMax = V(0, 1);
            fillRT.pivot = V(0, 0.5f);
            fillRT.offsetMin = V(1, 1); fillRT.offsetMax = V(0, -1);
            row.barFill = fillRT;

            // XP text on bar
            row.hitsText = Txt(barRT, "", 9, Color.white, TextAnchor.MiddleCenter);
            row.hitsText.fontStyle = FontStyle.Bold;
            Fill(row.hitsText.rectTransform);

            y += ROW + GAP;
            return row;
        }

        /// <summary>Place a rect at a Y offset from the top of _contentParent, stretching full width.</summary>
        void PlaceAt(RectTransform rt, float y, float h)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(2, -(y + h));
            rt.offsetMax = V(-2, -y);
        }

        // ══════════════════════════════════════════════════════════════
        // BONUS PANEL
        // ══════════════════════════════════════════════════════════════

        void BuildBonusPanel(float yTop, float h)
        {
            var pRT = Rect(_windowPanel, "Bonus");
            Img(pRT, C_Panel);
            StretchH(pRT, yTop, h, 6, 6);

            float by = 3;
            var hdr = Txt(pRT, "Active Bonuses", 14, C_Gold, TextAnchor.MiddleCenter);
            hdr.fontStyle = FontStyle.Bold;
            BonusPlace(hdr.rectTransform, by, 20); by += 21;

            AddBLine(pRT, ref by, "Fishing: Catch Bonus", "catch_bonus");
            AddBLine(pRT, ref by, "Swim Speed", "swim");
            AddBLine(pRT, ref by, "Bind Wound Heal", "bind");
            AddBLine(pRT, ref by, "Meditate Mana/tick", "med");
            AddBLine(pRT, ref by, "Consumable Bonus", "food");
        }

        void AddBLine(RectTransform parent, ref float y, string label, string key)
        {
            var lbl = Txt(parent, label, 11, C_Dim, TextAnchor.MiddleLeft);
            var lRT = lbl.rectTransform;
            lRT.anchorMin = V(0, 1); lRT.anchorMax = V(0.65f, 1);
            lRT.pivot = V(0, 1);
            lRT.offsetMin = V(8, -(y + 14));
            lRT.offsetMax = V(0, -y);

            var val = Txt(parent, "\u2014", 11, Color.white, TextAnchor.MiddleRight);
            val.fontStyle = FontStyle.Bold;
            var vRT = val.rectTransform;
            vRT.anchorMin = V(0.65f, 1); vRT.anchorMax = V(1, 1);
            vRT.pivot = V(1, 1);
            vRT.offsetMin = V(0, -(y + 14));
            vRT.offsetMax = V(-8, -y);

            _bonusRows.Add(new BonusRowUI { key = key, valueText = val });
            y += 15;
        }

        void BonusPlace(RectTransform rt, float y, float h)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(8, -(y + h));
            rt.offsetMax = V(-8, -y);
        }

        // ══════════════════════════════════════════════════════════════
        // REFRESH
        // ══════════════════════════════════════════════════════════════

        void Refresh()
        {
            var data = SkillsSaveManager.Data;

            // Check for new training points from leveling up
            data.CheckForNewTrainingPoints();

            // Update title with training points
            if (_titleText != null)
            {
                if (data.TrainingPoints > 0)
                    _titleText.text = $"Skills  <color=#00FF00>[{data.TrainingPoints} TP]</color>";
                else
                    _titleText.text = "Skills";
            }

            var utils = data.GetUtilitySkills();
            for (int i = 0; i < _utilRows.Count && i < utils.Count; i++)
                UpdRow(_utilRows[i], utils[i]);

            var tr = data.GetTradeskills();
            for (int i = 0; i < _tradeRows.Count && i < tr.Count; i++)
                UpdRow(_tradeRows[i], tr[i]);

            if (SkillsPlugin.CfgEnableCombatSkills.Value)
            {
                var cb = Skills.CombatSkills.GetActiveCombatSkills();
                for (int i = 0; i < _combatRows.Count && i < cb.Count; i++)
                    UpdCombat(_combatRows[i], cb[i]);
            }

            var mag = data.GetMagicSkills();
            for (int i = 0; i < _magicRows.Count && i < mag.Count; i++)
            {
                var s = mag[i];
                float bonus = 0f;
                switch (s.Name)
                {
                    case "Evocation":   bonus = (Skills.MagicSkills.GetSpellDamageMultiplier() - 1f) * 100f; break;
                    case "Abjuration":  bonus = (Skills.MagicSkills.GetBuffDurationMultiplier() - 1f) * 100f; break;
                    case "Alteration":  bonus = (Skills.MagicSkills.GetHealMultiplier() - 1f) * 100f; break;
                    case "Conjuration": bonus = (Skills.MagicSkills.GetConjurationMultiplier() - 1f) * 100f; break;
                }
                var info = new Skills.CombatSkills.CombatSkillInfo { Name = s.Name, Skill = s, Bonus = bonus };
                UpdCombat(_magicRows[i], info, C_Magic);
            }

            foreach (var b in _bonusRows) UpdBonus(b);
        }

        void UpdRow(SkillRowUI r, SkillEntry s)
        {
            r.skill = s; // Keep reference in sync with loaded data
            UpdateTrainButton(r.trainBtn, s);
            if (!s.IsUnlocked)
            {
                r.levelText.text = $"<color=#666666>0/{s.SkillCap}</color>";
                r.barFill.anchorMax = V(0.002f, 1);
                r.barFill.offsetMax = V(-1, -1);
                r.xpText.text = "";
            }
            else if (s.IsAtCap)
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                r.barFill.anchorMax = V(1f, 1);
                r.barFill.offsetMax = V(-1, -1);
                r.xpText.text = "";
            }
            else
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                float f = Mathf.Clamp01(s.LevelProgress);
                r.barFill.anchorMax = V(Mathf.Max(f, 0.002f), 1);
                r.barFill.offsetMax = V(-1, -1);
                r.xpText.text = "";
            }
        }

        void UpdCombat(CombatRowUI r, Skills.CombatSkills.CombatSkillInfo info, Color? bonusColor = null)
        {
            var s = info.Skill;
            Color accent = bonusColor ?? C_Combat;
            r.skill = s;
            UpdateTrainButton(r.trainBtn, s);
            if (!s.IsUnlocked)
            {
                r.levelText.text = $"<color=#666666>0/{s.SkillCap}</color>";
                r.bonusText.text = "\u2014";
                r.bonusText.color = Color.gray;
                r.barFill.anchorMax = V(0.002f, 1);
                r.barFill.offsetMax = V(-1, -1);
                r.hitsText.text = "";
            }
            else if (s.IsAtCap)
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                r.bonusText.text = info.Bonus > 0.01f ? $"+{info.Bonus:F1}%" : "\u2014";
                r.bonusText.color = info.Bonus > 0.01f ? accent : Color.gray;
                r.barFill.anchorMax = V(1f, 1);
                r.barFill.offsetMax = V(-1, -1);
                r.hitsText.text = "";
            }
            else
            {
                r.levelText.text = $"{s.Level}/{s.SkillCap}";
                r.bonusText.text = info.Bonus > 0.01f ? $"+{info.Bonus:F1}%" : "\u2014";
                r.bonusText.color = info.Bonus > 0.01f ? accent : Color.gray;
                float f = Mathf.Clamp01(s.LevelProgress);
                r.barFill.anchorMax = V(Mathf.Max(f, 0.002f), 1);
                r.barFill.offsetMax = V(-1, -1);
                r.hitsText.text = "";
            }
        }

        void OnTrainClick(SkillEntry skill)
        {
            var data = SkillsSaveManager.Data;
            if (data.TrainingPoints <= 0)
            {
                ChatHelper.Send(
                    "<color=#FF6666>[Skills]</color> No training points available.");
                return;
            }
            if (skill.IsAtCap)
            {
                ChatHelper.Send(
                    $"<color=#FF6666>[Skills]</color> {skill.Name} is already at its skill cap.");
                return;
            }

            if (data.SpendTrainingPoint(skill))
            {
                ChatHelper.Send(
                    $"<color=#FFD700>[Skills]</color> " +
                    $"Trained <color=#FFFFFF>{skill.Name}</color> to level {skill.Level}! " +
                    $"<color=#AAAAAA>({data.TrainingPoints} TP remaining)</color>");
                SkillsSaveManager.Save();
            }
        }

        void UpdateTrainButton(Button btn, SkillEntry skill)
        {
            if (btn == null) return;
            var data = SkillsSaveManager.Data;
            bool canTrain = data.TrainingPoints > 0 && !skill.IsAtCap;
            btn.gameObject.SetActive(canTrain);
        }

        void UpdBonus(BonusRowUI b)
        {
            string v = "\u2014"; bool on = false;
            switch (b.key) {
                case "catch_bonus": on = SkillsPlugin.CfgEnableFishing.Value;
                    if (on) v = $"+{Skills.FishingSkill.CatchBonus:F1}%"; break;
                case "swim": on = SkillsPlugin.CfgEnableSwimming.Value;
                    if (on) v = $"+{(Skills.SwimmingSkill.SpeedMultiplier-1f)*100f:F0}%"; break;
                case "bind": on = SkillsPlugin.CfgEnableBindWound.Value;
                    if (on) v = $"{Skills.BindWoundSkill.HealPercent:F1}% max HP"; break;
                case "med": on = SkillsPlugin.CfgEnableMeditate.Value;
                    if (on) v = $"+{Skills.MeditateSkill.BonusManaPerTick:F1}"; break;
                case "food": on = SkillsPlugin.CfgEnableFoodTolerance.Value;
                    if (on) v = $"+{Skills.FoodToleranceSkill.DurationBonus:F0}%"; break;
            }
            b.valueText.text = on ? v : "\u2014";
            b.valueText.color = on ? Color.white : Color.gray;
        }

        // ══════════════════════════════════════════════════════════════
        // VISIBILITY
        // ══════════════════════════════════════════════════════════════

        void SetVis(bool v)
        {
            if (_canvasGO) _canvasGO.SetActive(v);
            if (!v) { try { GameData.PlayerTyping = false; } catch { } }
        }

        // ══════════════════════════════════════════════════════════════
        // PRIMITIVES
        // ══════════════════════════════════════════════════════════════

        static Texture2D _wt; static Sprite _ws;
        static Sprite WS { get {
            if (!_ws) { if (!_wt) { _wt = new Texture2D(4,4);
                var p = new Color[16]; for (int i=0;i<16;i++) p[i]=Color.white;
                _wt.SetPixels(p); _wt.Apply(); }
                _ws = Sprite.Create(_wt, new UnityEngine.Rect(0,0,4,4), V(0.5f,0.5f)); }
            return _ws; } }

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WS; img.color = color;
            img.type = Image.Type.Simple; img.raycastTarget = true;
            return img;
        }

        static Text Txt(Transform parent, string text, int size,
            Color color, TextAnchor anchor)
        {
            var rt = Rect(parent, "T");
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

        /// <summary>Stretch horizontally across parent at yTop from top.</summary>
        static void StretchH(RectTransform rt, float y, float h,
            float padL = 0, float padR = 0)
        {
            rt.anchorMin = V(0, 1); rt.anchorMax = V(1, 1);
            rt.pivot = V(0.5f, 1);
            rt.offsetMin = V(padL, -(y + h));
            rt.offsetMax = V(-padR, -y);
        }

        /// <summary>Set anchor offsets explicitly: anchors define the relative region, offsets fine-tune.</summary>
        static void Anchor(RectTransform rt,
            float aMinX, float aMinY, float aMaxX, float aMaxY,
            float oMinX, float oMinY, float oMaxX, float oMaxY)
        {
            rt.anchorMin = V(aMinX, aMinY);
            rt.anchorMax = V(aMaxX, aMaxY);
            rt.offsetMin = V(oMinX, oMinY);
            rt.offsetMax = V(oMaxX, oMaxY);
        }

        // ── Data structs ──────────────────────────────────────────────

        class SkillRowUI {
            public Text nameText, levelText, xpText;
            public RectTransform barFill;
            public Button trainBtn;
            public SkillEntry skill;
        }
        class CombatRowUI {
            public Text nameText, levelText, bonusText, hitsText;
            public RectTransform barFill;
            public Button trainBtn;
            public SkillEntry skill;
        }
        class BonusRowUI { public string key; public Text valueText; }

        // ── Helper components ─────────────────────────────────────────

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
    }
}
