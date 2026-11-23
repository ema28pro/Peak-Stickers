using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakStickers;

/// <summary>
/// "Generar stickers" control panel injected into the customization passport:
/// rounded generate button, pose dropdown and border thickness slider.
/// It is parented to the passport's canvas with its own sorting order so it
/// always draws on top, and it is only visible while the passport is open.
/// </summary>
public class StickerButton : MonoBehaviour
{
    public const string DefaultLabel = "Generar stickers";
    public const string KeyAll = "all";
    public const string KeyEmotes = "emotes";
    public const string KeyIdles = "idles";

    private const float PanelWidth = 250f;
    private const float PanelHeight = 132f;

    private PassportManager manager = null!;
    private TextMeshProUGUI label = null!;
    private TextMeshProUGUI caption = null!;
    private TextMeshProUGUI borderLabel = null!;
    private GameObject popup = null!;
    private TMP_FontAsset? font;
    private bool popupBuilt;
    private bool generating;

    public static StickerButton Create(PassportManager manager)
    {
        // The PassportManager component lives outside the UI hierarchy, so we
        // must look for the canvas above the objects that are actually part of
        // the visible passport UI (UI elements only render under a Canvas).
        Transform?[] searchPoints =
        {
            manager.uiObject != null ? manager.uiObject.transform : null,
            manager.nameText != null ? manager.nameText.transform : null,
            manager.buttonNext != null ? manager.buttonNext.transform : null,
            manager.transform,
        };

        Transform host = manager.transform;
        Canvas? canvas = null;
        foreach (var start in searchPoints)
        {
            if (start == null)
            {
                continue;
            }

            host = start;
            canvas = null;
            for (var t = start; t != null; t = t.parent)
            {
                var c = t.GetComponent<Canvas>();
                if (c != null)
                {
                    host = t;
                    canvas = c;
                    break;
                }
            }

            if (canvas != null)
            {
                break;
            }
        }

        var font = manager.nameText != null ? manager.nameText.font : null;

        // ---- Panel root ----------------------------------------------------
        var go = new GameObject("PeakStickersPanel", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(host, worldPositionStays: false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var panelImg = UiFactory.NewImage(go.transform, "Background", new Color(0.07f, 0.07f, 0.09f, 0.96f));
        UiFactory.Stretch(panelImg.rectTransform);

        // Own sorting context: always above the passport art.
        var ownCanvas = go.AddComponent<Canvas>();
        ownCanvas.overrideSorting = true;
        ownCanvas.sortingOrder = 30000;
        go.AddComponent<GraphicRaycaster>();

        var comp = go.AddComponent<StickerButton>();
        comp.manager = manager;
        comp.font = font;

        // ---- Generate button (top, rounded) --------------------------------
        var genGo = new GameObject("Generate", typeof(RectTransform));
        ((RectTransform)genGo.transform).SetParent(go.transform, worldPositionStays: false);
        var genRt = (RectTransform)genGo.transform;
        genRt.anchorMin = new Vector2(0f, 1f);
        genRt.anchorMax = new Vector2(1f, 1f);
        genRt.offsetMin = new Vector2(8f, -48f);
        genRt.offsetMax = new Vector2(-8f, -8f);

        var genImg = UiFactory.NewImage(genGo.transform, "Btn", new Color(0.18f, 0.18f, 0.22f, 1f));
        UiFactory.Stretch(genImg.rectTransform);
        var genBtn = genGo.AddComponent<Button>();
        genBtn.targetGraphic = genImg;

        var btnLabel = UiFactory.NewLabel(
            genGo.transform, DefaultLabel, 17f, Color.white, TextAlignmentOptions.Center, font);
        UiFactory.Stretch(btnLabel.rectTransform);
        comp.label = btnLabel;
        genBtn.onClick.AddListener(comp.OnClick);

        // ---- Pose dropdown -------------------------------------------------
        var ddGo = new GameObject("PoseSelect", typeof(RectTransform));
        ((RectTransform)ddGo.transform).SetParent(go.transform, worldPositionStays: false);
        var ddRt = (RectTransform)ddGo.transform;
        ddRt.anchorMin = new Vector2(0f, 1f);
        ddRt.anchorMax = new Vector2(1f, 1f);
        ddRt.offsetMin = new Vector2(8f, -86f);
        ddRt.offsetMax = new Vector2(-8f, -54f);

        var ddImg = UiFactory.NewImage(ddGo.transform, "Bg", new Color(0.13f, 0.13f, 0.16f, 1f));
        UiFactory.Stretch(ddImg.rectTransform);
        var ddBtn = ddGo.AddComponent<Button>();
        ddBtn.targetGraphic = ddImg;

        var cap = UiFactory.NewLabel(
            ddGo.transform, CaptionFor(Plugin.SelectedPosesConfig.Value), 13.5f, Color.white,
            TextAlignmentOptions.MidlineLeft, font);
        UiFactory.Stretch(cap.rectTransform);
        cap.rectTransform.offsetMin = new Vector2(10f, 0f);
        cap.rectTransform.offsetMax = new Vector2(-26f, 0f);
        comp.caption = cap;

        var arrowImg = UiFactory.NewImage(ddGo.transform, "Arrow", new Color(1f, 1f, 1f, 0.85f));
        arrowImg.sprite = UiFactory.TriangleSprite();
        arrowImg.type = Image.Type.Simple;
        arrowImg.raycastTarget = false;
        var arrowRt = arrowImg.rectTransform;
        arrowRt.anchorMin = new Vector2(1f, 0.5f);
        arrowRt.anchorMax = new Vector2(1f, 0.5f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.anchoredPosition = new Vector2(-9f, -1f);
        arrowRt.sizeDelta = new Vector2(14f, 9f);

        ddBtn.onClick.AddListener(comp.TogglePopup);

        var popupImg = UiFactory.NewImage(ddGo.transform, "Options", new Color(0.11f, 0.11f, 0.14f, 0.99f));
        var popupRt = popupImg.rectTransform;
        popupRt.anchorMin = new Vector2(0f, 1f);
        popupRt.anchorMax = new Vector2(1f, 1f);
        popupRt.pivot = new Vector2(0.5f, 1f);
        popupRt.anchoredPosition = new Vector2(0f, -34f);
        popupRt.sizeDelta = new Vector2(0f, 0f);
        popupImg.gameObject.SetActive(false);
        comp.popup = popupImg.gameObject;

        // ---- Border thickness slider ---------------------------------------
        var borderCap = UiFactory.NewLabel(
            go.transform, $"Borde: {Plugin.BorderWidthConfig.Value}", 13f, new Color(1f, 1f, 1f, 0.9f),
            TextAlignmentOptions.MidlineRight, font);
        var borderRt = borderCap.rectTransform;
        borderRt.anchorMin = new Vector2(0f, 1f);
        borderRt.anchorMax = new Vector2(0f, 1f);
        borderRt.pivot = new Vector2(0f, 1f);
        borderRt.anchoredPosition = new Vector2(8f, -90f);
        borderRt.sizeDelta = new Vector2(64f, 24f);
        comp.borderLabel = borderCap;

        var sliderGo = new GameObject("BorderSlider", typeof(RectTransform));
        ((RectTransform)sliderGo.transform).SetParent(go.transform, worldPositionStays: false);
        var sliderRt = (RectTransform)sliderGo.transform;
        sliderRt.anchorMin = new Vector2(0f, 1f);
        sliderRt.anchorMax = new Vector2(1f, 1f);
        sliderRt.offsetMin = new Vector2(78f, -114f);
        sliderRt.offsetMax = new Vector2(-8f, -90f);

        var slider = sliderGo.AddComponent<Slider>();

        var trackImg = UiFactory.NewImage(sliderGo.transform, "Track", new Color(0.30f, 0.30f, 0.35f, 1f));
        var trackRt = trackImg.rectTransform;
        trackRt.anchorMin = new Vector2(0f, 0.5f);
        trackRt.anchorMax = new Vector2(1f, 0.5f);
        trackRt.pivot = new Vector2(0.5f, 0.5f);
        trackRt.sizeDelta = new Vector2(-14f, 8f);

        var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        ((RectTransform)fillAreaGo.transform).SetParent(sliderGo.transform, worldPositionStays: false);
        var fillAreaRt = (RectTransform)fillAreaGo.transform;
        fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRt.sizeDelta = new Vector2(-14f, 8f);

        var fillImg = UiFactory.NewImage(fillAreaGo.transform, "Fill", new Color(0.45f, 0.75f, 0.90f, 1f));
        UiFactory.Stretch(fillImg.rectTransform);

        var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
        ((RectTransform)handleAreaGo.transform).SetParent(sliderGo.transform, worldPositionStays: false);
        var handleAreaRt = (RectTransform)handleAreaGo.transform;
        handleAreaRt.anchorMin = new Vector2(0f, 0.5f);
        handleAreaRt.anchorMax = new Vector2(1f, 0.5f);
        handleAreaRt.pivot = new Vector2(0.5f, 0.5f);
        handleAreaRt.sizeDelta = new Vector2(-14f, 0f);

        var handleImg = UiFactory.NewImage(handleAreaGo.transform, "Handle", Color.white);
        var handleRt = handleImg.rectTransform;
        handleRt.sizeDelta = new Vector2(14f, 14f);

        slider.fillRect = fillImg.rectTransform;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.minValue = 0f;
        slider.maxValue = 40f;
        slider.wholeNumbers = true;
        slider.value = Mathf.Clamp(Plugin.BorderWidthConfig.Value, 0, 40);
        slider.onValueChanged.AddListener(v =>
        {
            Plugin.BorderWidthConfig.Value = (int)v;
            if (comp.borderLabel != null)
            {
                comp.borderLabel.text = $"Borde: {(int)v}";
            }

            Plugin.Instance?.SaveConfig();
        });

        go.SetActive(manager.isOpen);

        Plugin.Log.LogInfo(
            $"Sticker panel created: host='{HierarchyPath(host)}', " +
            $"canvas={(canvas != null ? canvas.name : "NONE")} " +
            $"mode={(canvas != null ? canvas.renderMode.ToString() : "-")}, " +
            $"size={rt.rect.size}, open={manager.isOpen}");
        return comp;
    }

    // --- Callbacks -----------------------------------------------------------

    private void OnClick()
    {
        if (generating || manager == null || Plugin.Instance == null)
        {
            return;
        }

        generating = true;
        label.text = "Generando…";
        ClosePopup();

        // The coroutine runs on the plugin (always active), so closing the
        // passport mid-generation can never interrupt the restore step.
        Plugin.Instance.RunGeneration(manager, this);
    }

    private void TogglePopup()
    {
        if (popup == null)
        {
            return;
        }

        if (!popup.activeSelf)
        {
            EnsureOptions();
            popup.SetActive(true);

            // Draw above the rest of the panel (options overlap the slider).
            popup.transform.SetAsLastSibling();
        }
        else
        {
            ClosePopup();
        }
    }

    private void ClosePopup()
    {
        if (popup != null && popup.activeSelf)
        {
            popup.SetActive(false);
        }
    }

    private void SelectOption(string key, string text)
    {
        Plugin.SelectedPosesConfig.Value = key;
        if (caption != null)
        {
            caption.text = text;
        }

        Plugin.Instance?.SaveConfig();
        ClosePopup();
    }

    // --- Dropdown content ----------------------------------------------------

    private void EnsureOptions()
    {
        if (popupBuilt)
        {
            return;
        }

        popupBuilt = true;

        var states = StickerGenerator.GetPoseStates();
        int emotes = 0, idles = 0;
        foreach (var (_, isIdle) in states)
        {
            if (isIdle)
            {
                idles++;
            }
            else
            {
                emotes++;
            }
        }

        var options = new List<(string key, string text)>
        {
            (KeyAll, $"Todas ({states.Count})"),
            (KeyEmotes, $"Solo emotes ({emotes})"),
            (KeyIdles, $"Solo idles ({idles})"),
        };
        foreach (var (state, _) in states)
        {
            options.Add((state, StickerGenerator.StateSuffix(state)));
        }

        const float itemHeight = 26f;
        const float spacing = 2f;
        const float padding = 3f;
        float contentHeight = (options.Count * (itemHeight + spacing)) + (padding * 2f);
        const float maxHeight = 260f;

        ((RectTransform)popup.transform).sizeDelta = new Vector2(0f, Mathf.Min(contentHeight, maxHeight));

        // Viewport + content with a vertical list.
        var scroll = popup.AddComponent<ScrollRect>();

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        ((RectTransform)viewportGo.transform).SetParent(popup.transform, worldPositionStays: false);
        UiFactory.Stretch((RectTransform)viewportGo.transform);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        ((RectTransform)contentGo.transform).SetParent(viewportGo.transform, worldPositionStays: false);
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, contentHeight);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scroll.viewport = (RectTransform)viewportGo.transform;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;

        for (int i = 0; i < options.Count; i++)
        {
            string key = options[i].key;
            string text = options[i].text;

            var itemImg = UiFactory.NewImage(contentRt, $"Option{i}", new Color(0.16f, 0.16f, 0.20f, 1f));
            var itemGo = itemImg.gameObject;
            var le = itemGo.AddComponent<LayoutElement>();
            le.preferredHeight = itemHeight;
            le.flexibleWidth = 1f;

            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemImg;

            var itemLabel = UiFactory.NewLabel(
                itemRtOf(itemImg), text, 13f, Color.white, TextAlignmentOptions.MidlineLeft, font);
            UiFactory.Stretch(itemLabel.rectTransform);
            itemLabel.rectTransform.offsetMin = new Vector2(8f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-8f, 0f);

            string capturedKey = key;
            string capturedText = text;
            itemBtn.onClick.AddListener(() => SelectOption(capturedKey, capturedText));
        }

        static RectTransform itemRtOf(Image img) => img.rectTransform;
    }

    private static string CaptionFor(string key)
    {
        var states = StickerGenerator.GetPoseStates();
        if (string.IsNullOrWhiteSpace(key) || key == KeyAll)
        {
            return $"Todas ({states.Count})";
        }

        if (key == KeyEmotes || key == KeyIdles)
        {
            int n = 0;
            bool wantIdle = key == KeyIdles;
            foreach (var (_, isIdle) in states)
            {
                if (isIdle == wantIdle)
                {
                    n++;
                }
            }

            return wantIdle ? $"Solo idles ({n})" : $"Solo emotes ({n})";
        }

        foreach (var (state, _) in states)
        {
            if (string.Equals(state, key, System.StringComparison.OrdinalIgnoreCase))
            {
                return StickerGenerator.StateSuffix(state);
            }
        }

        return $"Todas ({states.Count})";
    }

    // --- Visibility / status -------------------------------------------------

    /// <summary>Shows the panel only while the passport is open.</summary>
    public void SetPassportOpen(bool open)
    {
        if (gameObject.activeSelf != open)
        {
            gameObject.SetActive(open);
            if (!open)
            {
                ClosePopup();
            }
            else
            {
                // Position log: for a Screen Space overlay canvas this is in
                // screen pixels, so an off-screen value is easy to spot.
                Plugin.Log.LogInfo($"Sticker panel shown at {((RectTransform)transform).position}");
            }
        }
    }

    public void SetStatus(string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    /// <summary>
    /// Called by the generator (from its finally block) on every exit path,
    /// so the button can never get stuck in the "generating" state.
    /// </summary>
    public void FinishGeneration()
    {
        generating = false;
        if (Plugin.Instance != null)
        {
            Plugin.Instance.ScheduleLabelReset(this);
        }
        else
        {
            ResetLabel();
        }
    }

    public IEnumerator ResetLabelSoon()
    {
        yield return new WaitForSeconds(4f);
        ResetLabel();
    }

    public void ResetLabel()
    {
        if (label != null)
        {
            label.text = DefaultLabel;
        }
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        var cur = t.parent;
        while (cur != null)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }

        return path;
    }
}
