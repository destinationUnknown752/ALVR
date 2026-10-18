using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{
    internal sealed class SettingSpec
    {
        public readonly ConfigEntryBase Entry;
        public readonly string Label, Group, Unit, Description;
        public readonly float Minimum, Maximum, Step, DisplayScale;
        public bool IsBoolean => Entry.SettingType == typeof(bool);

        public SettingSpec(ConfigEntryBase entry, string label, string group,
            float minimum = 0f, float maximum = 1f, float step = 0.01f,
            string unit = "", float displayScale = 1f, string description = null)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            if (entry.SettingType != typeof(bool) && entry.SettingType != typeof(float) && entry.SettingType != typeof(int))
                throw new ArgumentException("Settings controls support bool, float and int entries.", nameof(entry));
            if (!IsBoolean && (!Finite(minimum) || !Finite(maximum) || maximum <= minimum ||
                !Finite(step) || step < 0f || !Finite(displayScale) || displayScale <= 0f))
                throw new ArgumentException("Numeric settings require finite bounds, a nonnegative step and positive display scale.");
            Label = string.IsNullOrEmpty(label) ? entry.Definition.Key : label;
            Group = string.IsNullOrEmpty(group) ? "Settings" : group;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            Unit = unit ?? "";
            DisplayScale = displayScale;
            Description = description ?? "";
        }

        internal object NumericValue(float value)
        {
            if (!Finite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            double number = Math.Max(Minimum, Math.Min(Maximum, value));
            if (Step > 0f) number = Math.Round(number / Step, MidpointRounding.AwayFromZero) * Step;
            number = Math.Max(Minimum, Math.Min(Maximum, number));
            if (Entry.SettingType == typeof(int)) return (int)Math.Round(number, MidpointRounding.AwayFromZero);
            return (float)number;
        }

        internal string Format(object value)
        {
            if (IsBoolean) return (bool)value ? "On" : "Off";
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture) * DisplayScale;
            double step = (Step > 0f ? Step : (Maximum - Minimum) / 100f) * DisplayScale;

            step *= 1.00001;
            int decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : step >= 0.001 ? 3 : 4;
            string suffix = Unit.Length == 0 ? "" : " " + Unit;
            return number.ToString("F" + decimals, CultureInfo.InvariantCulture) + suffix;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class VrSettingsMenu : IDisposable
    {
        sealed class Row
        {
            public SettingSpec Spec;
            public Slider Slider;
            public Toggle Toggle;
            public Text Value;
            public object LastValue;
        }

        sealed class Page
        {
            public string Name;
            public GameObject Root;
            public RawImage Tab;
            public ScrollRect Scroll;
        }

        const float PanelWidth = 1120f, PanelHeight = 800f, PageHeight = 448f, RowHeight = 90f;
        static readonly Color PanelColor = new Color(0.055f, 0.075f, 0.105f, 1f);
        static readonly Color RowColor = new Color(0.085f, 0.115f, 0.15f, 1f);
        static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.29f, 1f);
        static readonly Color Accent = new Color(0.2f, 0.8f, 0.87f, 1f);
        static readonly Color Muted = new Color(0.67f, 0.75f, 0.82f, 1f);
        readonly ManualLogSource log;
        readonly ConfigFile config;
        readonly Action recenter, retryVr, captureDiagnostics;
        readonly List<SettingSpec> specs;
        readonly List<Row> rows = new List<Row>();
        readonly List<Page> pages = new List<Page>();
        readonly List<object> listeners = new List<object>();
        readonly Dictionary<ConfigEntryBase, object> savedValues = new Dictionary<ConfigEntryBase, object>();
        GameObject root, inputObject;
        EventSystem fallbackInput;
        Font font;
        Text status;
        bool visible, disposed, refreshing, cursorOwned, oldCursorVisible;
        CursorLockMode oldCursorLock;
        float nextRefresh, nextInputCheck;
        string lastFailure;

        public VrSettingsMenu(ManualLogSource logger, ConfigFile configuration, IReadOnlyList<SettingSpec> settings,
            Action recenter = null, Action retryVr = null, Action captureDiagnostics = null)
        {
            log = logger;
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.recenter = recenter;
            this.retryVr = retryVr;
            this.captureDiagnostics = captureDiagnostics;
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            specs = new List<SettingSpec>(settings.Count);
            foreach (var spec in settings)
            {
                if (spec == null) throw new ArgumentException("A settings specification cannot be null.", nameof(settings));
                if (spec.Entry.ConfigFile != config) throw new ArgumentException("All settings must belong to the supplied config file.");
                if (savedValues.ContainsKey(spec.Entry)) throw new ArgumentException("A setting may only appear once in the menu.");
                specs.Add(spec);
                savedValues.Add(spec.Entry, spec.Entry.BoxedValue);
            }
        }

        public bool Visible => visible && root && root.activeInHierarchy;

        public bool IsTextInputFocused
        {
            get
            {
                try
                {
                    var system = EventSystem.current;
                    var selected = system ? system.currentSelectedGameObject : null;
                    var input = selected ? selected.GetComponent<InputField>() : null;
                    if (input && input.isFocused) return true;
                    var tmpInput = selected ? selected.GetComponent<TMP_InputField>() : null;
                    return tmpInput && tmpInput.isFocused;
                }
                catch { return false; }
            }
        }

        public void Toggle()
        {
            if (disposed) return;
            if (Visible) { Close(); return; }
            try
            {
                if (visible) Close();
                if (!root) { DestroyPanel(); Build(); }
                EnsureInput();
                oldCursorVisible = Cursor.visible;
                oldCursorLock = Cursor.lockState;
                cursorOwned = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                visible = true;
                root.SetActive(true);
                Refresh(true);
                lastFailure = null;
            }
            catch (Exception exception)
            {
                ReportFailure("Could not open VR settings", exception);
                Close();
                DestroyPanel();
            }
        }

        public void Update()
        {
            if (disposed || !visible) return;
            if (!Visible) { Close(); return; }
            try
            {

                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
                float now = Time.realtimeSinceStartup;
                if (now >= nextInputCheck) { EnsureInput(); nextInputCheck = now + 1f; }
                if (now >= nextRefresh) { Refresh(false); nextRefresh = now + 0.15f; }
            }
            catch (Exception exception)
            {
                ReportFailure("VR settings closed after a UI error", exception);
                Close();
            }
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!font) throw new InvalidOperationException("Unity's built-in UI font is unavailable.");
            root = new GameObject("AmanatsuLocationVRSettings", new[] { Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>() });
            root.layer = 5;
            root.SetActive(false);
            UObject.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();
            var backdrop = Rect("Backdrop", root.transform);
            Stretch(backdrop);
            Image(backdrop, new Color(0.015f, 0.025f, 0.04f, 0.87f), true);
            var panel = Rect("Panel", backdrop);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Image(panel, PanelColor, true);
            Label(panel, "Amanatsu Location VR settings", 26, 22, 780, 46, 32, Color.white);
            Label(panel, "Changes apply live. Save keeps them after restart.", 28, 70, 940, 30, 20, Muted);
            Button(panel, "Close", PanelWidth - 116, 24, 90, 44, Close);
            Button(panel, "Recenter", 24, 112, 154, 44, recenter);
            Button(panel, "Restart VR", 192, 112, 154, 44, retryVr);
            Button(panel, "Diagnostics", 360, 112, 154, 44, captureDiagnostics);
            Label(panel, "Recenter shortcut: Ctrl+Shift+R", 536, 119, 558, 30, 20, Muted);

            var groups = new List<string>();
            foreach (var spec in specs) if (!groups.Contains(spec.Group)) groups.Add(spec.Group);
            if (groups.Count == 0) groups.Add("Settings");
            float tabWidth = (PanelWidth - 48f) / groups.Count;
            for (int index = 0; index < groups.Count; ++index)
            {
                int pageIndex = index;
                var page = new Page { Name = groups[index] };
                page.Tab = Button(panel, page.Name, 24f + index * tabWidth, 174f, tabWidth - 6f, 44f,
                    () => ShowPage(pageIndex)).GetComponent<RawImage>();
                var area = Rect("Page " + page.Name, panel);
                TopLeft(area, 24, 236, PanelWidth - 48, PageHeight);
                page.Root = area.gameObject;
                Image(area, PanelColor, true);
                var scroll = area.gameObject.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.inertia = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40f;
                var viewport = Rect("Viewport", area);
                Stretch(viewport, 0, 0, 26, 0);
                Image(viewport, PanelColor, true);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect("Content", viewport);
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 1f);
                int rowIndex = 0;
                foreach (var spec in specs)
                    if (spec.Group == page.Name) AddRow(content, spec, rowIndex++);
                content.sizeDelta = new Vector2(0, Math.Max(PageHeight, rowIndex * RowHeight));
                scroll.viewport = viewport;
                scroll.content = content;
                page.Scroll = scroll;
                AddScrollbar(area, scroll);
                pages.Add(page);
            }
            Label(panel, "Scroll to see every setting in the selected tab.", 26, 694, 960, 25, 17, Muted);
            status = Label(panel, "", 26, 742, 610, 34, 20, Muted);
            Button(panel, "Reset all", 676, 736, 132, 44, ResetAll);
            Button(panel, "Save", 822, 736, 120, 44, Save, Accent);
            Button(panel, "Close", 958, 736, 136, 44, Close);
            ShowPage(0);
        }

        void AddRow(RectTransform content, SettingSpec spec, int index)
        {
            var area = Rect(spec.Entry.Definition.Key, content);
            TopLeft(area, 0, index * RowHeight, PanelWidth - 74, RowHeight - 6);
            Image(area, RowColor, false);
            Label(area, spec.Label, 16, 8, 320, 35, 23, Color.white);
            if (spec.Description.Length != 0)
                Label(area, spec.Description, 16, 45, 330, 36, 16, Muted);
            var row = new Row { Spec = spec };
            if (spec.IsBoolean)
            {
                var box = Rect("Toggle", area);
                TopLeft(box, 364, 20, 514, 44);
                var background = Image(box, ButtonColor, true);
                var toggle = box.gameObject.AddComponent<Toggle>();
                toggle.transition = Selectable.Transition.None;
                toggle.targetGraphic = background;
                var check = Rect("Check", box);
                TopLeft(check, 12, 10, 24, 24);
                toggle.graphic = Image(check, Accent, false);
                row.Toggle = toggle;
                row.Value = Label(box, "", 52, 5, 320, 34, 23, Color.white);
                Action<bool> managed = value => GuardAction(() => Write(row, value));
                UnityAction<bool> native = managed;
                listeners.Add(managed);
                listeners.Add(native);
                toggle.onValueChanged.AddListener(native);
            }
            else
            {
                Button(area, "-", 354, 23, 34, 38, () => Nudge(row, -1));
                var sliderRect = Rect("Slider", area);
                TopLeft(sliderRect, 400, 18, 332, 46);
                Image(sliderRect, new Color(0, 0, 0, 0), true);
                var slider = sliderRect.gameObject.AddComponent<Slider>();
                slider.transition = Selectable.Transition.None;
                slider.minValue = spec.Minimum;
                slider.maxValue = spec.Maximum;
                slider.wholeNumbers = spec.Entry.SettingType == typeof(int);
                var track = Rect("Track", sliderRect);
                TopLeft(track, 0, 19, 332, 8);
                Image(track, ButtonColor, false);
                var fillArea = Rect("Fill area", sliderRect);
                Stretch(fillArea, 8, 19, 8, 19);
                var fill = Rect("Fill", fillArea);
                Stretch(fill);
                Image(fill, Accent, false);
                slider.fillRect = fill;
                var handleArea = Rect("Handle area", sliderRect);
                Stretch(handleArea, 10, 0, 10, 0);
                var handle = Rect("Handle", handleArea);
                handle.anchorMin = handle.anchorMax = new Vector2(0, 0.5f);
                handle.sizeDelta = new Vector2(22, 38);
                slider.handleRect = handle;
                slider.targetGraphic = Image(handle, Color.white, true);
                row.Slider = slider;
                row.Value = Label(area, "", 744, 24, 124, 35, 21, Color.white, TextAnchor.MiddleCenter);
                Button(area, "+", 876, 23, 34, 38, () => Nudge(row, 1));
                Action<float> managed = value => GuardAction(() => Write(row, spec.NumericValue(value)));
                UnityAction<float> native = managed;
                listeners.Add(managed);
                listeners.Add(native);
                slider.onValueChanged.AddListener(native);
            }
            Button(area, "Default", 928, 23, 96, 38, () => Write(row, spec.Entry.DefaultValue));
            rows.Add(row);
        }

        void AddScrollbar(RectTransform area, ScrollRect scroll)
        {
            var rect = Rect("Scrollbar", area);
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(18, 0);
            Image(rect, ButtonColor, true);
            var slidingArea = Rect("Sliding area", rect);
            Stretch(slidingArea, 2, 2, 2, 2);
            var handle = Rect("Handle", slidingArea);
            Stretch(handle);
            var graphic = Image(handle, Accent, true);
            var bar = rect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.transition = Selectable.Transition.None;
            bar.handleRect = handle;
            bar.targetGraphic = graphic;
            scroll.verticalScrollbar = bar;
        }

        void ShowPage(int selected)
        {
            for (int index = 0; index < pages.Count; ++index)
            {
                bool active = index == selected;
                pages[index].Root.SetActive(active);
                pages[index].Tab.color = active ? new Color(0.14f, 0.42f, 0.48f, 1) : ButtonColor;
            }
        }

        void Nudge(Row row, int direction)
        {
            float current = Convert.ToSingle(row.Spec.Entry.BoxedValue, CultureInfo.InvariantCulture);
            float step = row.Spec.Step > 0 ? row.Spec.Step : (row.Spec.Maximum - row.Spec.Minimum) / 100f;
            Write(row, row.Spec.NumericValue(current + step * direction));
        }

        void Write(Row row, object value)
        {
            if (refreshing || disposed) return;
            row.Spec.Entry.BoxedValue = value;
            Refresh(false);
        }

        void ResetAll()
        {
            foreach (var spec in specs) spec.Entry.BoxedValue = spec.Entry.DefaultValue;
            Refresh(true);
        }

        void Save()
        {
            config.Save();
            foreach (var spec in specs) savedValues[spec.Entry] = spec.Entry.BoxedValue;
            Refresh(true);
            log?.LogInfo("VR settings saved to " + config.ConfigFilePath);
        }

        void Refresh(bool force)
        {
            refreshing = true;
            try
            {
                bool dirty = false;
                foreach (var row in rows)
                {
                    var value = row.Spec.Entry.BoxedValue;
                    if (!Equals(value, savedValues[row.Spec.Entry])) dirty = true;
                    if (!force && Equals(value, row.LastValue)) continue;
                    if (row.Toggle) row.Toggle.SetIsOnWithoutNotify((bool)value);
                    if (row.Slider) row.Slider.SetValueWithoutNotify(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    row.Value.text = row.Spec.Format(value);
                    row.LastValue = value;
                }
                if (status)
                {
                    string text = dirty ? "Unsaved changes. Close keeps this preview." : "Saved settings. Toggle menu: Ctrl+Shift+V.";
                    if (status.text != text) status.text = text;
                }
            }
            finally { refreshing = false; }
        }

        void EnsureInput()
        {
            var current = EventSystem.current;
            if (fallbackInput && current == fallbackInput)
            {
                foreach (var other in UObject.FindObjectsOfType<EventSystem>())
                    if (other && other != fallbackInput && other.isActiveAndEnabled)
                    {
                        EventSystem.current = other;
                        inputObject.SetActive(false);
                        return;
                    }
            }
            if (current && current.isActiveAndEnabled)
            {
                if (inputObject && current != fallbackInput) inputObject.SetActive(false);
                return;
            }
            if (!inputObject)
            {
                inputObject = new GameObject("AmanatsuLocationVRSettingsInput");
                UObject.DontDestroyOnLoad(inputObject);
                fallbackInput = inputObject.AddComponent<EventSystem>();
                inputObject.AddComponent<StandaloneInputModule>();
            }
            if (!inputObject.activeSelf) inputObject.SetActive(true);
        }

        void Close()
        {
            visible = false;
            try { if (root) root.SetActive(false); }
            catch (Exception exception) { ReportFailure("Could not hide VR settings", exception); }
            try
            {
                if (cursorOwned)
                {
                    Cursor.lockState = oldCursorLock;
                    Cursor.visible = oldCursorVisible;
                    cursorOwned = false;
                }
            }
            catch (Exception exception) { ReportFailure("Could not restore the game cursor", exception); }
            try { if (inputObject) inputObject.SetActive(false); }
            catch (Exception exception) { ReportFailure("Could not stop settings input", exception); }
        }

        void GuardAction(Action action)
        {
            if (disposed || action == null) return;
            try { action(); }
            catch (Exception exception)
            {
                ReportFailure("VR settings action failed", exception);
                if (status) status.text = "Could not apply or save the setting. See the game log.";
            }
        }

        void ReportFailure(string message, Exception exception)
        {
            string text = message + ": " + exception.Message;
            if (lastFailure != text) log?.LogWarning(text);
            lastFailure = text;
        }

        RectTransform Rect(string name, Transform parent)
        {
            var value = new GameObject(name, new[] { Il2CppType.Of<RectTransform>() });
            value.layer = 5;
            value.transform.SetParent(parent, false);
            var rect = value.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            return rect;
        }

        static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static RawImage Image(RectTransform rect, Color color, bool raycast)
        {
            var graphic = rect.gameObject.AddComponent<RawImage>();
            graphic.texture = Texture2D.whiteTexture;
            graphic.color = color;
            graphic.raycastTarget = raycast;
            return graphic;
        }

        Text Label(Transform parent, string value, float x, float y, float width, float height,
            int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var rect = Rect("Label", parent);
            TopLeft(rect, x, y, width, height);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        GameObject Button(Transform parent, string title, float x, float y, float width, float height,
            Action action, Color? color = null)
        {
            var rect = Rect(title, parent);
            TopLeft(rect, x, y, width, height);
            var graphic = Image(rect, color ?? ButtonColor, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = graphic;
            button.interactable = action != null;
            Label(rect, title, 3, 2, width - 6, height - 4, 20,
                action == null ? Muted : color.HasValue ? PanelColor : Color.white, TextAnchor.MiddleCenter);
            Action managed = () => GuardAction(action);
            UnityAction native = managed;
            listeners.Add(managed);
            listeners.Add(native);
            button.onClick.AddListener(native);
            return rect.gameObject;
        }

        void DestroyPanel()
        {
            if (root) UObject.Destroy(root);
            root = null;
            rows.Clear();
            pages.Clear();
            listeners.Clear();
            status = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Close();
            disposed = true;
            DestroyPanel();
            if (inputObject) UObject.Destroy(inputObject);
            inputObject = null;
            fallbackInput = null;
        }
    }
}
