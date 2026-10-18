using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using ILLGAMES.ADV;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class SubtitleSource : IDisposable
    {
        readonly ManualLogSource log;
        TextController controller;
        TextMeshProUGUI sourceText, captionText;
        GameObject captionObject;
        Canvas captionCanvas;
        CanvasGroup suppression;
        float nextProbe;
        bool disposed, prepared, warned, waitingLogged;

        public SubtitleSource(ManualLogSource logger) { log = logger; }

        public Canvas Discover()
        {
            if (disposed) return null;
            try
            {
                TextMeshProUGUI selected = controller ? controller.MessageWindow : null;
                if (Time.realtimeSinceStartup >= nextProbe)
                {
                    nextProbe = Time.realtimeSinceStartup + 0.5f;
                    TextController best = null;
                    foreach (var candidate in UObject.FindObjectsOfType<TextController>())
                    {
                        if (!candidate || !candidate.isActiveAndEnabled) continue;
                        var message = candidate.MessageWindow;
                        if (!message || !message.isActiveAndEnabled) continue;
                        if (!best || (candidate.MessageVisible && !string.IsNullOrWhiteSpace(message.text))) best = candidate;
                        if (candidate.MessageVisible && !string.IsNullOrWhiteSpace(message.text)) break;
                    }
                    controller = best;
                    selected = best ? best.MessageWindow : null;
                }
                if (!selected || !selected.gameObject.activeInHierarchy) selected = null;
                if (selected != sourceText)
                {
                    Restore();
                    sourceText = selected;
                }
                if (!sourceText)
                {
                    if (!waitingLogged)
                    {
                        log?.LogInfo("Separate VR captions waiting for native dialogue. Voice-only scenes without a message window have no text to display.");
                        waitingLogged = true;
                    }
                    return null;
                }
                EnsureCanvas();
                waitingLogged = false;
                return captionCanvas;
            }
            catch (Exception exception)
            {
                Restore();
                Warn("Native dialogue capture unavailable; original dialogue retained: ", exception);
                return null;
            }
        }

        void EnsureCanvas()
        {
            if (captionObject && captionCanvas && captionText) return;
            if (captionObject) UObject.Destroy(captionObject);

            captionObject = new GameObject("AmanatsuLocationVRCaptions", new[] { Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>() });
            UObject.DontDestroyOnLoad(captionObject);
            captionObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
            captionCanvas = captionObject.GetComponent<Canvas>();
            captionCanvas.enabled = false;
            captionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            captionCanvas.sortingOrder = 32001;
            var scaler = captionObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            var message = new GameObject("Dialogue message", new[] { Il2CppType.Of<RectTransform>() });
            message.transform.SetParent(captionObject.transform, false);
            var rect = message.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(24f, 12f);
            rect.offsetMax = new Vector2(-24f, -12f);
            captionText = message.AddComponent<TextMeshProUGUI>();
            captionText.raycastTarget = false;
            captionText.enableAutoSizing = false;
            captionText.alignment = TextAlignmentOptions.Center;
            captionText.textWrappingMode = TextWrappingModes.Normal;
            captionText.overflowMode = TextOverflowModes.Overflow;
        }

        public bool Prepare(Canvas target, int fontSize = 36)
        {
            if (disposed) return false;
            try
            {
                if (!target || target != Discover() || !sourceText || !captionText)
                {
                    Restore();
                    return false;
                }
                var font = sourceText.font;
                if (!font) { Restore(); return false; }
                if (captionText.font != font) captionText.font = font;
                var material = sourceText.fontSharedMaterial;
                if (captionText.fontSharedMaterial != material) captionText.fontSharedMaterial = material;
                var text = sourceText.text;
                if (captionText.text != text) captionText.text = text;
                float size = Math.Max(16, Math.Min(72, fontSize));
                if (captionText.fontSize != size) captionText.fontSize = size;
                if (captionText.richText != sourceText.richText) captionText.richText = sourceText.richText;
                if (captionText.isRightToLeftText != sourceText.isRightToLeftText) captionText.isRightToLeftText = sourceText.isRightToLeftText;
                if (captionText.fontStyle != sourceText.fontStyle) captionText.fontStyle = sourceText.fontStyle;
                if (captionText.maxVisibleCharacters != sourceText.maxVisibleCharacters) captionText.maxVisibleCharacters = sourceText.maxVisibleCharacters;
                if (captionText.maxVisibleWords != sourceText.maxVisibleWords) captionText.maxVisibleWords = sourceText.maxVisibleWords;
                if (captionText.maxVisibleLines != sourceText.maxVisibleLines) captionText.maxVisibleLines = sourceText.maxVisibleLines;
                if (captionText.firstVisibleCharacter != sourceText.firstVisibleCharacter) captionText.firstVisibleCharacter = sourceText.firstVisibleCharacter;
                var color = sourceText.color;
                color.a *= SourceGroupAlpha();
                if (captionText.color != color) captionText.color = color;
                if (!captionCanvas.enabled) captionCanvas.enabled = true;
                if (!suppression)
                {
                    suppression = sourceText.gameObject.AddComponent<CanvasGroup>();

                    suppression.ignoreParentGroups = false;
                    suppression.interactable = true;
                    suppression.blocksRaycasts = true;
                }
                suppression.alpha = 0f;
                prepared = true;
                warned = false;
                return true;
            }
            catch (Exception exception)
            {
                Restore();
                Warn("Could not prepare native dialogue captions; original dialogue restored: ", exception);
                return false;
            }
        }

        float SourceGroupAlpha()
        {
            float alpha = 1f;
            var node = sourceText.transform;
            while (node)
            {
                bool stopAtThisObject = false;
                foreach (var group in node.GetComponents<CanvasGroup>())
                {
                    if (!group || !group.enabled || group == suppression) continue;
                    alpha *= group.alpha;
                    stopAtThisObject |= group.ignoreParentGroups;
                }
                if (stopAtThisObject) break;
                node = node.parent;
            }
            return alpha;
        }

        public bool HasVisibleText
        {
            get
            {
                try
                {
                    return !disposed && prepared && controller && controller.MessageVisible && sourceText && sourceText.isActiveAndEnabled &&
                        captionText && captionText.color.a > 0.001f && sourceText.maxVisibleCharacters != 0 && !string.IsNullOrWhiteSpace(sourceText.text);
                }
                catch { return false; }
            }
        }

        public void Restore()
        {
            prepared = false;
            if (suppression)
            {

                suppression.alpha = 1f;
                UObject.Destroy(suppression);
            }
            suppression = null;
            if (captionCanvas) captionCanvas.enabled = false;
        }

        void Warn(string prefix, Exception exception)
        {
            if (!warned) log?.LogWarning(prefix + exception);
            warned = true;
        }

        public void Dispose()
        {
            Restore();
            disposed = true;
            if (captionObject) UObject.Destroy(captionObject);
            captionObject = null;
            captionCanvas = null;
            captionText = null;
            sourceText = null;
            controller = null;
        }
    }
}
