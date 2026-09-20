#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace RotationAlertEditor
{
    // UI factory helpers, self-contained (no PocketConveni/PocketOverdrive references).
    // No external sprites: flat rects use sprite=null; buttons get a plain Outline for edge
    // definition instead of the 9-sliced button art used elsewhere in the project.
    public static partial class RotationAlertInstaller
    {
        private static Canvas CreateCanvas(Transform parent, string name, Vector2 size)
        {
            GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.layer = 0;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            canvasObject.AddComponent<VRCUiShape>();
            BoxCollider collider = canvasObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 6f);
            collider.center = Vector3.zero;
            collider.isTrigger = true;
            return canvas;
        }

        private static Image CreateImage(Transform parent, string name, Vector2 size, Vector2 position,
            Color color, bool raycastTarget)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.fillCenter = true;
            image.preserveAspect = false;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static TMP_Text CreateText(Transform parent, string name, string value, float fontSize,
            Vector2 size, Vector2 position, Color color, TextAlignmentOptions alignment,
            TMP_FontAsset font, bool autoSize, bool wordWrapping)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = wordWrapping;
            // Text never intercepts clicks; overlapping a neighboring control (e.g. phaseText's
            // right edge and the mute button) is harmless because of this.
            text.raycastTarget = false;
            if (autoSize)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 12f;
                text.fontSizeMax = fontSize;
            }
            return text;
        }

        // Visuals + interaction wiring only. Persistent listeners are attached separately via
        // AddOpListener/AddClickListener so a button can take one or two listeners as needed
        // (every op button gets both; the mute button gets ToggleMute + PlayUiClick, both on
        // the audio backing).
        private static Button CreateButtonVisual(Transform parent, string name, string label, Vector2 size,
            Vector2 position, Color faceColor, TMP_FontAsset font, float fontSize)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Image image = buttonObject.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = faceColor;
            image.raycastTarget = true;

            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = WithAlpha(TokenText, 0.25f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.58f, 0.62f, 0.9f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.interactable = true;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            CreateText(buttonObject.transform, "Label", label, fontSize, size, Vector2.zero,
                TokenText, TextAlignmentOptions.Center, font, false, false);
            return button;
        }

        // First persistent listener on a button: SendCustomEvent(eventName) on the given backing.
        private static void AddOpListener(Button button, UdonBehaviour target, string eventName)
        {
            UnityEventTools.AddStringPersistentListener(button.onClick, target.SendCustomEvent, eventName);
        }

        // Second persistent listener every button in the panel gets: the UI click sound.
        private static void AddClickListener(Button button, UdonBehaviour audioBacking)
        {
            UnityEventTools.AddStringPersistentListener(button.onClick, audioBacking.SendCustomEvent, "PlayUiClick");
        }
    }
}
#endif
