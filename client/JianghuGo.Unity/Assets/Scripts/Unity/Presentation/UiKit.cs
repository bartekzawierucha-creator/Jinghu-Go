using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JianghuGo.Unity.Presentation
{
    /// <summary>
    /// Builds the prototype UI from code so the flow runs in an empty scene without hand-made prefabs.
    /// Final pixel-art UI will replace this (Presentation layer only; Core is untouched).
    /// </summary>
    public static class UiKit
    {
        private static Font _font;
        private static Font Font => _font ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static readonly Color Paper = new Color(0.93f, 0.89f, 0.80f);
        public static readonly Color Ink = new Color(0.12f, 0.10f, 0.09f);
        public static readonly Color Cinnabar = new Color(0.70f, 0.16f, 0.12f);

        public static Canvas CreateCanvas()
        {
            var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Paper;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(60, 60, 120, 60);
            layout.spacing = 24;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return rt;
        }

        /// <summary>A plain vertical list inside a screen (rebuilt by destroying its children).</summary>
        public static RectTransform Column(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return (RectTransform)go.transform;
        }

        public static Text Label(Transform parent, string text, int size = 40, Color? color = null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = color ?? Ink;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            go.GetComponent<LayoutElement>().minHeight = size * 1.4f;
            return t;
        }

        public static InputField TextField(Transform parent, string placeholder, bool password = false)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            go.GetComponent<LayoutElement>().minHeight = 100;
            var text = Label(go.transform, string.Empty);
            Stretch(text.rectTransform, 20);
            var ph = Label(go.transform, placeholder, 40, new Color(0.5f, 0.5f, 0.5f));
            Stretch(ph.rectTransform, 20);
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            return field;
        }

        public static Button ActionButton(Transform parent, string caption, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Cinnabar;
            go.GetComponent<LayoutElement>().minHeight = 110;
            var label = Label(go.transform, caption, 42, Paper);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform, 0);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, 0);
            rt.offsetMax = new Vector2(-inset, 0);
        }
    }
}
