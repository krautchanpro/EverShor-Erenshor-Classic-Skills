using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// EQ-STYLE BAG SYSTEM
    /// Each bag instance gets a permanent unique ID (counter-based).
    /// Moving bags between inventory slots does not lose contents.
    /// Multiple bag windows can be open simultaneously.
    /// Compatible with LootRarity mod (Quantity 4-8 = rarity tiers).
    /// </summary>
    public static class BagSystem
    {
        private static Dictionary<string, List<BagSlotData>> _bags
            = new Dictionary<string, List<BagSlotData>>();

        private static int _nextBagCounter = 100; // Start at 100 to avoid rarity tier collision

        /// <summary>Get next unique bag number and increment counter.</summary>
        public static int NextBagNumber() => _nextBagCounter++;

        private static readonly Dictionary<string, int> BagDefs
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Silken Pouch", 4 },
            { "Herbalist's Satchel", 6 },
            { "Simple Backpack", 8 },
            { "Adventurer's Backpack", 10 },
        };

        [Serializable]
        public class BagSlotData
        {
            public string ItemId = "";
            public string ItemName = "";
            public int Quantity = 0;
            public bool IsStackable = false;
        }

        public static bool IsBag(string n) => BagDefs.ContainsKey(n);
        public static bool IsBag(Item i) => i != null && BagDefs.ContainsKey(i.ItemName);
        public static int GetBagSlots(string n) => BagDefs.TryGetValue(n, out int s) ? s : 0;

        public static List<BagSlotData> GetBagContents(string bagId)
        {
            if (!_bags.TryGetValue(bagId, out var c))
            { c = new List<BagSlotData>(); _bags[bagId] = c; }
            return c;
        }

        public static bool TryAddItemToBag(string bagId, string bagName, Item item, int qty, bool stackable)
        {
            if (item == null) return false;
            if (IsBag(item))
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> You can't put a bag inside a bag!"); return false; }
            int max = GetBagSlots(bagName);
            if (max <= 0) return false;
            var contents = GetBagContents(bagId);
            string itemId = item.Id ?? item.ItemName;
            if (stackable)
            {
                foreach (var s in contents)
                    if (s.IsStackable && s.ItemName == item.ItemName)
                    { s.Quantity += qty; return true; }
            }
            if (contents.Count >= max)
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> This bag is full!"); return false; }
            contents.Add(new BagSlotData { ItemId = itemId, ItemName = item.ItemName,
                Quantity = qty, IsStackable = stackable });
            return true;
        }

        public static bool TryStoreFromInventorySlot(ItemIcon slot)
        {
            if (slot?.MyItem == null) return false;
            if (!BagWindow.AnyOpen) return false;
            string bagId = BagWindow.LastFocusedBagId;
            string bagName = BagWindow.LastFocusedBagName;
            if (string.IsNullOrEmpty(bagId)) return false;
            var item = slot.MyItem;
            if (item == GameData.PlayerInv.Empty) return false;
            if (IsBag(item))
            { ChatHelper.Send("<color=#EF5350>[Bag]</color> You can't put a bag inside a bag!"); return true; }

            int qty = slot.Quantity; if (qty <= 0) qty = 1;
            if (TryAddItemToBag(bagId, bagName, item, qty, item.Stackable))
            {
                slot.MyItem = GameData.PlayerInv.Empty;
                slot.Quantity = 0; slot.UpdateSlotImage();
                ChatHelper.Send($"<color=#4CAF50>[Bag]</color> Stored {item.ItemName} in {bagName}.");
                BagWindow.RefreshAll(); SkillsSaveManager.Save();
                return true;
            }
            return false;
        }

        public static BagSlotData RemoveFromBag(string bagId, int idx)
        {
            var c = GetBagContents(bagId);
            if (idx < 0 || idx >= c.Count) return null;
            var r = c[idx]; c.RemoveAt(idx); return r;
        }

        public static string GetRarityTag(BagSlotData s)
        {
            if (s.IsStackable) return "";
            switch (s.Quantity)
            {
                case 2: return " <color=#00FFFF>(Blessed)</color>";
                case 3: return " <color=#FF00FF>(Epic)</color>";
                case 4: return " <color=#00FF00>(Uncommon)</color>";
                case 5: return " <color=#FFFF00>(Rare)</color>";
                case 6: return " <color=#FF3333>(Epic)</color>";
                case 7: return " <color=#FF8800>(Legendary)</color>";
                case 8: return " <color=#666666>(Mythic)</color>";
                default: return "";
            }
        }

        public static string SerializeBags()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"_ctr\":");
            sb.Append(_nextBagCounter);
            foreach (var kvp in _bags)
            {
                if (kvp.Value.Count == 0) continue;
                sb.Append($",\"{kvp.Key}\":[");
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var s = kvp.Value[i];
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"i\":\"{s.ItemId}\",\"n\":\"{s.ItemName}\",\"q\":{s.Quantity},\"s\":{(s.IsStackable ? "1" : "0")}}}");
                }
                sb.Append("]");
            }
            sb.Append("}");
            return sb.ToString();
        }

        public static void DeserializeBags(string json)
        {
            _bags.Clear(); _nextBagCounter = 100;
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            try
            {
                // Parse _ctr
                var ctrM = System.Text.RegularExpressions.Regex.Match(json, "\"_ctr\":(\\d+)");
                if (ctrM.Success) _nextBagCounter = int.Parse(ctrM.Groups[1].Value);

                // Parse bag contents (keys starting with "bag")
                var bagM = System.Text.RegularExpressions.Regex.Matches(json, "\"(bag\\d+)\":\\[([^\\]]*)\\]");
                foreach (System.Text.RegularExpressions.Match m in bagM)
                {
                    string bagId = m.Groups[1].Value;
                    string arrStr = m.Groups[2].Value;
                    var contents = new List<BagSlotData>();
                    if (!string.IsNullOrEmpty(arrStr.Trim()))
                    {
                        string[] items = arrStr.Split(new[] { "},{" }, StringSplitOptions.None);
                        foreach (var itemStr in items)
                        {
                            string clean = itemStr.Trim().Trim('{', '}');
                            string id = "", name = ""; int qty = 1; bool stack = false;
                            string[] kv = clean.Split(',');
                            foreach (var pair in kv)
                            {
                                var parts = pair.Split(':');
                                if (parts.Length < 2) continue;
                                string key = parts[0].Trim().Trim('"');
                                string val = parts[1].Trim().Trim('"');
                                if (key == "i") id = val;
                                else if (key == "n") name = val;
                                else if (key == "q") int.TryParse(val, out qty);
                                else if (key == "s") stack = val == "1";
                            }
                            if (!string.IsNullOrEmpty(name))
                                contents.Add(new BagSlotData { ItemId = id, ItemName = name,
                                    Quantity = qty, IsStackable = stack });
                        }
                    }
                    _bags[bagId] = contents;
                }

                // Also try parsing old-format bags (keys like "Silken Pouch_28")
                var oldM = System.Text.RegularExpressions.Regex.Matches(json,
                    "\"([A-Z][^\"]+_\\d+)\":\\[([^\\]]*)\\]");
                foreach (System.Text.RegularExpressions.Match m in oldM)
                {
                    string oldId = m.Groups[1].Value;
                    if (oldId.StartsWith("bag")) continue; // Already handled
                    string arrStr = m.Groups[2].Value;
                    // Migrate to new format
                    string newId = $"bag{_nextBagCounter++}";
                    var contents = new List<BagSlotData>();
                    if (!string.IsNullOrEmpty(arrStr.Trim()))
                    {
                        string[] items = arrStr.Split(new[] { "},{" }, StringSplitOptions.None);
                        foreach (var itemStr in items)
                        {
                            string clean = itemStr.Trim().Trim('{', '}');
                            string id = "", name = ""; int qty = 1; bool stack = false;
                            string[] kv = clean.Split(',');
                            foreach (var pair in kv)
                            {
                                var parts = pair.Split(':');
                                if (parts.Length < 2) continue;
                                string key = parts[0].Trim().Trim('"');
                                string val = parts[1].Trim().Trim('"');
                                if (key == "i") id = val; else if (key == "n") name = val;
                                else if (key == "q") int.TryParse(val, out qty);
                                else if (key == "s") stack = val == "1";
                            }
                            if (!string.IsNullOrEmpty(name))
                                contents.Add(new BagSlotData { ItemId = id, ItemName = name,
                                    Quantity = qty, IsStackable = stack });
                        }
                    }
                    if (contents.Count > 0)
                    {
                        _bags[newId] = contents;
                        SkillsPlugin.Log.LogInfo($"Migrated old bag '{oldId}' -> '{newId}' ({contents.Count} items)");
                    }
                }
            }
            catch (Exception ex)
            { SkillsPlugin.Log.LogWarning($"BagSystem deserialize error: {ex.Message}"); }
        }

        public static void Reset()
        {
            _bags.Clear(); _nextBagCounter = 100;
            BagWindow.CloseAll();
        }
    }

    public static class BagWindow
    {
        static Canvas _canvas;
        static Dictionary<string, SingleBagWindow> _wins = new Dictionary<string, SingleBagWindow>();
        static string _lastBagId, _lastBagName;
        public static bool AnyOpen => _wins.Count > 0;
        public static bool IsOpen => _wins.Count > 0;
        public static string ActiveBagId => _lastBagId;
        public static string ActiveBagName => _lastBagName;
        public static string LastFocusedBagId => _lastBagId;
        public static string LastFocusedBagName => _lastBagName;

        static Canvas GetCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("BagCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 115;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();
            return _canvas;
        }

        public static void Open(string bagId, string bagName)
        {
            _lastBagId = bagId; _lastBagName = bagName;
            if (_wins.TryGetValue(bagId, out var w)) { w.BringToFront(); return; }
            int slots = BagSystem.GetBagSlots(bagName);
            int cols = Mathf.Min(slots, 4);
            int rows = Mathf.CeilToInt((float)slots / cols);
            float ox = 200 + _wins.Count * 30;
            float oy = _wins.Count * -25;
            _wins[bagId] = new SingleBagWindow(GetCanvas().transform,
                bagId, bagName, cols, rows, ox, oy);
        }
        public static void CloseOne(string id)
        { if (_wins.TryGetValue(id, out var w)) { w.Destroy(); _wins.Remove(id); } }
        public static void Close() => CloseAll();
        public static void CloseAll()
        { foreach (var k in _wins) k.Value.Destroy(); _wins.Clear(); }
        public static void RefreshAll()
        { foreach (var k in _wins) k.Value.Refresh(); }
        public static void RefreshIfOpen() => RefreshAll();
        public static void SetFocus(string id, string n) { _lastBagId = id; _lastBagName = n; }
    }

    public class SingleBagWindow
    {
        string _bagId, _bagName; int _cols, _rows;
        GameObject _root; RectTransform _panel, _slotsParent;
        List<GameObject> _slotGOs = new List<GameObject>();
        static readonly Color C_Bg = new Color(0.08f, 0.12f, 0.15f, 0.96f);    // Dark teal-blue
        static readonly Color C_Title = new Color(0.06f, 0.10f, 0.13f, 0.98f); // Darkest teal
        static readonly Color C_Slot = new Color(0.12f, 0.18f, 0.22f, 0.9f);   // Teal slot with item
        static readonly Color C_Empty = new Color(0.08f, 0.13f, 0.16f, 0.7f);  // Darker empty slot
        static readonly Color C_Border = new Color(0.22f, 0.36f, 0.40f, 0.9f); // Teal border
        const float SLOT = 58f; // pixels per slot
        const float PAD = 3f;

        public SingleBagWindow(Transform parent, string bagId, string bagName,
            int cols, int rows, float ox, float oy)
        {
            _bagId = bagId; _bagName = bagName; _cols = cols; _rows = rows;
            float w = cols * SLOT + (cols + 1) * PAD;
            float h = rows * SLOT + (rows + 1) * PAD + 28; // +28 for title

            _root = new GameObject($"Bag_{bagId}");
            _root.transform.SetParent(parent, false);
            _panel = _root.AddComponent<RectTransform>();
            _root.AddComponent<Image>().color = C_Bg;
            var outline = _root.AddComponent<Outline>();
            outline.effectColor = C_Border;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(w, h);
            _panel.anchoredPosition = new Vector2(ox, oy);

            // Title
            var tGO = new GameObject("Title"); tGO.transform.SetParent(_panel, false);
            var tRT = tGO.AddComponent<RectTransform>();
            tGO.AddComponent<Image>().color = C_Title;
            tRT.anchorMin = new Vector2(0, 1); tRT.anchorMax = Vector2.one;
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.offsetMin = new Vector2(0, -28); tRT.offsetMax = Vector2.zero;
            var tt = MkTxt(tRT, bagName, 13, Color.white, TextAnchor.MiddleCenter);
            tt.fontStyle = FontStyle.Bold;
            tGO.AddComponent<BagWindowDragger>().Target = _panel;
            // Title border line
            var tbGO = new GameObject("TBorder"); tbGO.transform.SetParent(tRT, false);
            var tbRT = tbGO.AddComponent<RectTransform>();
            tbGO.AddComponent<Image>().color = C_Border;
            tbRT.anchorMin = new Vector2(0, 0); tbRT.anchorMax = new Vector2(1, 0);
            tbRT.pivot = new Vector2(0.5f, 0); tbRT.sizeDelta = new Vector2(0, 1);
            tbRT.anchoredPosition = Vector2.zero;
            // Focus on click
            string bid = bagId, bn = bagName;
            var fb = tGO.AddComponent<Button>(); fb.targetGraphic = tGO.GetComponent<Image>();
            fb.onClick.AddListener(() => BagWindow.SetFocus(bid, bn));
            // Close button
            var cGO = new GameObject("X"); cGO.transform.SetParent(tRT, false);
            var cRT = cGO.AddComponent<RectTransform>();
            cGO.AddComponent<Image>().color = new Color(0.6f, 0.15f, 0.15f, 0.9f);
            cRT.anchorMin = new Vector2(1, 0); cRT.anchorMax = Vector2.one;
            cRT.pivot = new Vector2(1, 0.5f);
            cRT.offsetMin = new Vector2(-26, 2); cRT.offsetMax = new Vector2(-2, -2);
            var cb = cGO.AddComponent<Button>(); cb.targetGraphic = cGO.GetComponent<Image>();
            string cid = bagId; cb.onClick.AddListener(() => BagWindow.CloseOne(cid));
            MkTxt(cRT, "\u2715", 11, Color.white, TextAnchor.MiddleCenter);

            // Slots container - absolute positioned below title
            var sGO = new GameObject("Slots"); sGO.transform.SetParent(_panel, false);
            _slotsParent = sGO.AddComponent<RectTransform>();
            _slotsParent.anchorMin = new Vector2(0, 0); _slotsParent.anchorMax = new Vector2(1, 1);
            _slotsParent.offsetMin = Vector2.zero;
            _slotsParent.offsetMax = new Vector2(0, -28);
            Refresh();
        }

        public void BringToFront()
        { if (_root) _root.transform.SetAsLastSibling(); BagWindow.SetFocus(_bagId, _bagName); }
        public void Destroy() { if (_root) UnityEngine.Object.Destroy(_root); }

        public void Refresh()
        {
            foreach (var go in _slotGOs) if (go) UnityEngine.Object.Destroy(go);
            _slotGOs.Clear();
            int max = BagSystem.GetBagSlots(_bagName);
            var contents = BagSystem.GetBagContents(_bagId);

            for (int i = 0; i < max; i++)
            {
                int col = i % _cols, row = i / _cols;
                bool has = i < contents.Count && !string.IsNullOrEmpty(contents[i].ItemName);

                // Build slot from scratch — no cloning, guaranteed sizing
                var go = new GameObject($"S{i}");
                go.transform.SetParent(_slotsParent, false);
                var rt = go.AddComponent<RectTransform>();
                var bg = go.AddComponent<Image>();
                bg.color = has ? C_Slot : C_Empty;
                var slotOutline = go.AddComponent<Outline>();
                slotOutline.effectColor = C_Border;
                slotOutline.effectDistance = new Vector2(1, -1);

                // Absolute pixel positioning
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(SLOT, SLOT);
                rt.anchoredPosition = new Vector2(
                    PAD + col * (SLOT + PAD),
                    -(PAD + row * (SLOT + PAD)));

                if (has)
                {
                    var s = contents[i];
                    Item gi = null;
                    if (!string.IsNullOrEmpty(s.ItemId))
                        gi = GameData.ItemDB?.GetItemByID(s.ItemId);
                    if (gi == null) gi = ItemFactory.GetItem(s.ItemName);

                    // Item icon image
                    if (gi != null && gi.ItemIcon != null)
                    {
                        var iconGO = new GameObject("Icon");
                        iconGO.transform.SetParent(rt, false);
                        var iconRT = iconGO.AddComponent<RectTransform>();
                        iconRT.anchorMin = Vector2.zero; iconRT.anchorMax = Vector2.one;
                        iconRT.offsetMin = new Vector2(2, 2); iconRT.offsetMax = new Vector2(-2, -2);
                        var iconImg = iconGO.AddComponent<Image>();
                        iconImg.sprite = gi.ItemIcon;
                        iconImg.preserveAspect = true;
                    }

                    // Quantity text (bottom-left)
                    if (s.Quantity > 1 && s.IsStackable)
                    {
                        var qGO = new GameObject("Qty");
                        qGO.transform.SetParent(rt, false);
                        var qRT = qGO.AddComponent<RectTransform>();
                        qRT.anchorMin = new Vector2(0, 0);
                        qRT.anchorMax = new Vector2(0.5f, 0.3f);
                        qRT.offsetMin = Vector2.zero; qRT.offsetMax = Vector2.zero;
                        var qBg = qGO.AddComponent<Image>();
                        qBg.color = new Color(0, 0, 0, 0.7f);
                        var qTxt = new GameObject("QT").AddComponent<Text>();
                        qTxt.transform.SetParent(qRT, false);
                        qTxt.rectTransform.anchorMin = Vector2.zero;
                        qTxt.rectTransform.anchorMax = Vector2.one;
                        qTxt.rectTransform.offsetMin = Vector2.zero;
                        qTxt.rectTransform.offsetMax = Vector2.zero;
                        qTxt.text = s.Quantity.ToString();
                        qTxt.fontSize = 10; qTxt.color = Color.white;
                        qTxt.alignment = TextAnchor.MiddleCenter;
                        qTxt.font = Font.CreateDynamicFontFromOSFont("Arial", 10);
                    }

                    // Click overlay + tooltip
                    int idx = i; string mid = _bagId;
                    Item tooltipItem = gi;
                    int tooltipQty = s.Quantity;
                    var ov = new GameObject("Clk"); ov.transform.SetParent(rt, false);
                    var ovr = ov.AddComponent<RectTransform>();
                    ovr.anchorMin = Vector2.zero; ovr.anchorMax = Vector2.one;
                    ovr.offsetMin = ovr.offsetMax = Vector2.zero;
                    var oi = ov.AddComponent<Image>(); oi.color = new Color(0, 0, 0, 0);
                    var ob = ov.AddComponent<Button>(); ob.targetGraphic = oi;

                    // Tooltip on hover — use game's native ItemInfoWindow
                    var trigger = ov.AddComponent<EventTrigger>();
                    var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    Item hItem = tooltipItem; int hQty = tooltipQty;
                    enterEntry.callback.AddListener((data) => {
                        try {
                            if (hItem != null)
                            {
                                var pos = ov.transform.position + new Vector3(-200f, 0f, 0f);
                                GameData.ItemInfoWindow.DisplayItem(hItem, pos, hQty);
                            }
                        } catch { }
                    });
                    trigger.triggers.Add(enterEntry);
                    var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exitEntry.callback.AddListener((data) => {
                        try { GameData.ItemInfoWindow.CloseItemWindow(); } catch { }
                    });
                    trigger.triggers.Add(exitEntry);
                    ob.onClick.AddListener(() =>
                    {
                        var rem = BagSystem.RemoveFromBag(mid, idx);
                        if (rem != null)
                        {
                            try {
                                Item rgi = null;
                                if (!string.IsNullOrEmpty(rem.ItemId))
                                    rgi = GameData.ItemDB?.GetItemByID(rem.ItemId);
                                if (rgi == null) rgi = ItemFactory.GetItem(rem.ItemName);
                                if (rgi != null)
                                {
                                    GameData.PlayerInv.AddItemToInv(rgi, rem.Quantity);
                                    ChatHelper.Send($"<color=#4CAF50>[Bag]</color> Removed {rem.ItemName}{BagSystem.GetRarityTag(rem)}.");
                                }
                            } catch { }
                        }
                        BagWindow.RefreshAll(); SkillsSaveManager.Save();
                    });
                }
                _slotGOs.Add(go);
            }
        }

        static Text MkTxt(RectTransform p, string t, int sz, Color c, TextAnchor a)
        {
            var go = new GameObject("T"); go.transform.SetParent(p, false);
            var tx = go.AddComponent<Text>();
            tx.text = t; tx.fontSize = sz; tx.color = c; tx.alignment = a;
            tx.font = Font.CreateDynamicFontFromOSFont("Arial", sz);
            tx.supportRichText = true;
            var r = tx.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return tx;
        }
    }

    public class BagWindowDragger : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public RectTransform Target;
        Vector2 _off;
        public void OnBeginDrag(PointerEventData e)
        { if (Target) RectTransformUtility.ScreenPointToLocalPointInRectangle(Target, e.position, e.pressEventCamera, out _off); }
        public void OnDrag(PointerEventData e)
        { if (Target) Target.position = e.position - _off; }
    }
}
