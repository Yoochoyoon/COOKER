using System;
using UnityEngine;
using UnityEngine.UI;

// 코드로 UI를 만드는 작은 도우미. 좌표는 부모 기준 0~1 비율이라 해상도와 상관없이 배치된다.
public static class UIKit
{
    public static readonly Color Bg = new Color(1f, 0.82f, 0.36f);
    public static readonly Color Ink = new Color(0.23f, 0.12f, 0.06f);
    public static readonly Color Accent = new Color(0.91f, 0.35f, 0.05f);
    public static readonly Color Card = new Color(1f, 0.95f, 0.8f);
    public static readonly Color Muted = new Color(0.62f, 0.45f, 0.3f);
    public static readonly Color Good = new Color(0.12f, 0.55f, 0.2f);
    public static readonly Color Bad = new Color(0.8f, 0.1f, 0.1f);

    static Font font;
    public static Font GetFont()
    {
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "NanumGothic", "Arial" }, 32);
        return font;
    }

    public static RectTransform Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color? color = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        if (color.HasValue)
        {
            var img = go.AddComponent<Image>();
            img.color = color.Value;
        }
        return rt;
    }

    public static Text Label(Transform parent, string text, float x0, float y0, float x1, float y1, int size,
        TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
    {
        var rt = Box(parent, "Label", x0, y0, x1, y1);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetFont();
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color ?? Ink;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        return t;
    }

    public static Button Btn(Transform parent, string text, float x0, float y0, float x1, float y1, Action onClick,
        Color? bg = null, int size = 44, bool enabled = true)
    {
        var rt = Box(parent, "Btn_" + text, x0, y0, x1, y1, bg ?? Accent);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        var cb = b.colors;
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        b.colors = cb;
        Label(rt, text, 0.04f, 0.04f, 0.96f, 0.96f, size, TextAnchor.MiddleCenter, Color.white);
        if (onClick != null) b.onClick.AddListener(() => onClick());
        b.interactable = enabled;
        return b;
    }
}
