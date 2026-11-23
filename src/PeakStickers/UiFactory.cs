using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakStickers;

/// <summary>
/// Small helpers to build rounded, good-looking runtime UI without needing
/// sprite assets from the game.
/// </summary>
internal static class UiFactory
{
    private static Sprite? rounded;
    private static Sprite? triangle;

    /// <summary>White 9-slice rounded rectangle, usable at any size.</summary>
    internal static Sprite RoundedSprite()
    {
        if (rounded != null)
        {
            return rounded;
        }

        const int size = 128;
        const int radius = 30;
        const int border = 36;
        float half = size / 2f;
        float r = radius;

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f - half;
                float fy = y + 0.5f - half;
                float qx = Mathf.Abs(fx) - (half - r);
                float qy = Mathf.Abs(fy) - (half - r);
                float d = Mathf.Sqrt((Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)) + (Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)))
                    + Mathf.Min(Mathf.Max(qx, qy), 0f)
                    - r;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[(y * size) + x] = new Color32(255, 255, 255, a);
            }
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();

        rounded = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        return rounded;
    }

    /// <summary>Small white triangle pointing down (dropdown arrow).</summary>
    internal static Sprite TriangleSprite()
    {
        if (triangle != null)
        {
            return triangle;
        }

        const int w = 16;
        const int h = 10;
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            float hw = ((1f - t) * (w / 2f - 1f)) + 0.5f;
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Abs((x + 0.5f) - (w / 2f)) - hw;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[(y * w) + x] = new Color32(255, 255, 255, a);
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();

        triangle = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return triangle;
    }

    internal static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        ((RectTransform)go.transform).SetParent(parent, worldPositionStays: false);
        var img = go.AddComponent<Image>();
        img.sprite = RoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    internal static TextMeshProUGUI NewLabel(
        Transform parent,
        string text,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        TMP_FontAsset? font)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        ((RectTransform)go.transform).SetParent(parent, worldPositionStays: false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            tmp.font = font;
        }

        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    internal static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
