using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ErenshorSkills
{
    /// <summary>
    /// Adds a moveable Skills button to the screen.
    /// Loads the SkillsMenu.png icon and creates a circular toggle button.
    /// Drag to move, click to toggle Skills window. Position is saved.
    /// </summary>
    public class MenuBarButton : MonoBehaviour
    {
        static MenuBarButton _instance;
        GameObject _btnGO;
        GameObject _canvasGO;
        Image _bgImage;
        RectTransform _btnRT;
        bool _built;

        // Saved position config file path
        static string _posFile;

        void Awake() => _instance = this;

        void Start()
        {
            try
            {
                _posFile = Path.Combine(
                    Path.GetDirectoryName(SkillsPlugin.Instance.Info.Location),
                    "ClassicSkills", "button_pos.txt");
                Build();
                _built = true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"MenuBarButton build failed: {ex}");
            }
        }

        void Update()
        {
            if (!_built || _bgImage == null || _canvasGO == null) return;

            // Only show when actually playing — not on menu or character select
            bool inGame = false;
            try
            {
                inGame = !GameData.InCharSelect &&
                         UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Menu" &&
                         GameData.PlayerStats != null &&
                         !string.IsNullOrEmpty(GameData.PlayerStats.MyName);
            }
            catch { }

            if (_canvasGO.activeSelf != inGame)
                _canvasGO.SetActive(inGame);

            if (!inGame) return;

            _bgImage.color = SkillsUI.IsWindowVisible
                ? new Color(0.8f, 1f, 0.8f)
                : Color.white;
        }


        void Build()
        {
            // Always use our own canvas so we control positioning
            _canvasGO = new GameObject("CS_BarCanvas");
            DontDestroyOnLoad(_canvasGO);
            var cv = _canvasGO.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 99;
            var sc = _canvasGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Start hidden — Update() will show it when a character is in-game
            _canvasGO.SetActive(false);

            // Create the button
            _btnGO = new GameObject("CS_SkillsBarBtn");
            _btnGO.transform.SetParent(_canvasGO.transform, false);
            _btnRT = _btnGO.AddComponent<RectTransform>();

            // Default position: bottom-center, right of the hotkey bar
            _btnRT.anchorMin = new Vector2(0.5f, 0);
            _btnRT.anchorMax = new Vector2(0.5f, 0);
            _btnRT.pivot = new Vector2(0.5f, 0.5f);
            _btnRT.anchoredPosition = new Vector2(330, 29);
            _btnRT.sizeDelta = new Vector2(42, 42);

            // Load saved position if it exists
            LoadPosition();

            // The icon IS the button
            _bgImage = _btnGO.AddComponent<Image>();
            _bgImage.color = Color.white;
            _bgImage.type = Image.Type.Simple;
            LoadIcon(_bgImage);

            // Drag-to-move handler (distinguishes click vs drag)
            var dragger = _btnGO.AddComponent<ButtonDragger>();
            dragger.target = _btnRT;
            dragger.onClicked = () => SkillsUI.ToggleWindow();
            dragger.onDragEnd = () => SavePosition();

            SkillsPlugin.Log.LogInfo("MenuBarButton: built successfully");
        }

        void LoadIcon(Image targetImage)
        {
            try
            {
                string iconPath = Path.Combine(
                    Path.GetDirectoryName(SkillsPlugin.Instance.Info.Location),
                    "ClassicSkills", "Icons", "SkillsMenu.png");

                if (!File.Exists(iconPath))
                {
                    SkillsPlugin.Log.LogWarning($"MenuBarButton: icon not found at {iconPath}");
                    targetImage.color = new Color(0.8f, 0.7f, 0.4f);
                    return;
                }

                byte[] data = File.ReadAllBytes(iconPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.LoadImage(data);
                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
                targetImage.sprite = sprite;
                targetImage.color = Color.white;

                SkillsPlugin.Log.LogInfo($"MenuBarButton: loaded icon ({tex.width}x{tex.height})");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"MenuBarButton icon load: {ex.Message}");
                targetImage.color = new Color(0.8f, 0.7f, 0.4f);
            }
        }

        void OnDestroy()
        {
            if (_btnGO) Destroy(_btnGO);
        }

        // ── Position persistence ──────────────────────────────────

        void SavePosition()
        {
            if (_btnRT == null) return;
            try
            {
                var p = _btnRT.anchoredPosition;
                File.WriteAllText(_posFile, $"{p.x},{p.y}");
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"MenuBarButton save pos: {ex.Message}");
            }
        }

        void LoadPosition()
        {
            if (_btnRT == null) return;
            try
            {
                if (!File.Exists(_posFile)) return;
                string text = File.ReadAllText(_posFile).Trim();
                string[] parts = text.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y))
                {
                    _btnRT.anchoredPosition = new Vector2(x, y);
                    SkillsPlugin.Log.LogInfo($"MenuBarButton: loaded position ({x}, {y})");
                }
            }
            catch { }
        }

        /// <summary>
        /// Handles drag-to-move and click-to-toggle.
        /// If the pointer moves more than a few pixels, it's a drag.
        /// Otherwise it's a click.
        /// </summary>
        class ButtonDragger : MonoBehaviour,
            IPointerDownHandler, IPointerUpHandler,
            IDragHandler, IBeginDragHandler
        {
            public RectTransform target;
            public Action onClicked;
            public Action onDragEnd;

            bool _dragging;
            Vector2 _dragOffset;

            public void OnPointerDown(PointerEventData e)
            {
                _dragging = false;
            }

            public void OnBeginDrag(PointerEventData e)
            {
                _dragging = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                _dragOffset = target.anchoredPosition - lp;
            }

            public void OnDrag(PointerEventData e)
            {
                if (!_dragging || target == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target.parent as RectTransform, e.position,
                    e.pressEventCamera, out Vector2 lp);
                target.anchoredPosition = lp + _dragOffset;
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (_dragging)
                {
                    _dragging = false;
                    onDragEnd?.Invoke();
                }
                else
                {
                    onClicked?.Invoke();
                }
            }
        }
    }
}
