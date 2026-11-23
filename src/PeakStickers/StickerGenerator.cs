using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PeakStickers;

/// <summary>
/// Renders the customization passport dummy with every pose and saves each
/// one as a transparent PNG sticker with a white border around the character.
/// </summary>
internal static class StickerGenerator
{
    /// <summary>
    /// Sample time (0..1 of the animation) for each pose. Emotes that end in a
    /// held pose (collapse, sit, play dead) sample near the end; acrobatic ones
    /// (backflip) sample in the middle of the move.
    /// </summary>
    internal const string DefaultSampleTimes =
        "Wave=0.4;Clap=0.4;ThumbsUp=0.5;Flex=0.45;Salute=0.5;Shrug=0.5;Think=0.5;CrossedArms=0.5;" +
        "ImHere=0.5;Nono=0.5;Panic=0.5;Fist=0.5;PunchFist=0.5;Dance1=0.5;Dance2=0.8;Cinema=0.5;" +
        "Crashout=0.75;Defeat=0.95;Despair=0.9;Sit=0.92;PlayDead=0.95;BackFlip=0.45;" +
        "Idle1=0.3;Idle2=0.3;Idle3=0.3;Idle4=0.3;Idle5=0.3;Idle6=0.3;Idle7=0.3;Idle8=0.3";

    /// <summary>Every emote state present in this build of the game.</summary>
    private static readonly string[] EmoteStates =
    {
        "A_Scout_Emote_Wave",
        "A_Scout_Emote_Clap",
        "A_Scout_Emote_ThumbsUp",
        "A_Scout_Emote_Flex",
        "A_Scout_Emote_Salute",
        "A_Scout_Emote_Shrug",
        "A_Scout_Emote_Think",
        "A_Scout_Emote_CrossedArms",
        "A_Scout_Emote_ImHere",
        "A_Scout_Emote_Nono",
        "A_Scout_Emote_Panic",
        "A_Scout_Emote_Fist",
        "A_Scout_Emote_PunchFist",
        "A_Scout_Emote_Dance1",
        "A_Scout_Emote_Dance2",
        "A_Scout_Emote_Cinema",
        "A_Scout_Emote_Crashout",
        "A_Scout_Emote_Defeat",
        "A_Scout_Emote_Despair",
        "A_Scout_Emote_Sit",
        "A_Scout_Emote_PlayDead",
        "A_Scout_Emote_BackFlip",
    };

    // Full-body framing used by the passport itself (PassportManager.CameraOut).
    private static readonly Vector3 FullBodyLocalPos = new Vector3(0f, 1.05f, 1f);
    private const float FullBodyOrthoSize = 1.3f;

    private const int IdleCount = 8;
    private const float DefaultSampleTime = 0.5f;

    private readonly struct PoseInfo
    {
        public readonly string State;
        public readonly float Time;
        public readonly bool IsIdle;
        public readonly int IdleIndex;

        public PoseInfo(string state, float time, bool isIdle, int idleIndex)
        {
            State = state;
            Time = time;
            IsIdle = isIdle;
            IdleIndex = idleIndex;
        }
    }

    internal static IEnumerator Generate(PassportManager manager, StickerButton button)
    {
        var dummy = manager.dummy;
        var cam = manager.dummyCamera;
        if (dummy == null || dummy.anim == null || dummy.refs == null || cam == null)
        {
            Plugin.Log.LogError("Passport preview not found; cannot generate stickers.");
            button.SetStatus("Error: vista previa no encontrada");
            button.FinishGeneration();
            yield break;
        }

        var poses = BuildPoses(ParseSampleTimes(Plugin.SampleTimesConfig.Value));
        poses = FilterBySelection(poses);
        if (poses.Count == 0)
        {
            button.SetStatus("Error: sin poses configuradas");
            button.FinishGeneration();
            yield break;
        }

        // --- Output directory -------------------------------------------------
        string baseDir = (Plugin.OutputDirectoryConfig.Value ?? string.Empty).Trim();
        if (baseDir.Length == 0)
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PEAK Stickers");
        }

        string dir = Path.Combine(baseDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not create output directory '{dir}': {e}");
            button.SetStatus("Error: no se pudo crear la carpeta");
            button.FinishGeneration();
            yield break;
        }

        int resolution = Mathf.Clamp(Plugin.ResolutionConfig.Value, 256, 4096);
        int border = Mathf.Clamp(Plugin.BorderWidthConfig.Value, 0, 64);
        bool autoCrop = Plugin.AutoCropConfig.Value;
        int cropPad = Mathf.Max(8, resolution / 64);
        const int supersample = 2;
        int rtSize = resolution * supersample;

        var anim = dummy.anim;
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();

        // --- Save state (read-only, nothing modified yet) --------------------
        var origController = anim.runtimeAnimatorController;
        float origSpeed = anim.speed;

        CameraClearFlags savedClear = cam.clearFlags;
        Color savedBg = cam.backgroundColor;
        RenderTexture? savedTarget = cam.targetTexture;
        int savedMask = cam.cullingMask;
        float savedOrtho = cam.orthographicSize;
        Vector3 savedLocalPos = cam.transform.localPosition;
        Rect savedRect = cam.rect;
        bool savedCamEnabled = cam.enabled;
        bool savedPost = urp != null && urp.renderPostProcessing;

        var root = dummy.transform;
        var keep = BuildKeepSet(dummy.refs);
        int stickerLayer = FindUnusedLayer();

        // Isolate the character: the dummy subtree PLUS any character renderer
        // that lives outside it. Only objects moved to the sticker layer are
        // visible to the capture camera (which culls everything else), so the
        // model renders no matter where in the scene it is assembled.
        var layered = new HashSet<GameObject>();
        var savedLayers = new List<(GameObject go, int layer)>();
        void AddLayerObject(GameObject? go)
        {
            if (go != null && layered.Add(go))
            {
                savedLayers.Add((go, go.layer));
            }
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            AddLayerObject(t.gameObject);
        }

        foreach (var r in keep)
        {
            AddLayerObject(r.gameObject);
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var savedDisabled = new List<(Renderer r, bool enabled)>();
        foreach (var r in renderers)
        {
            if (!keep.Contains(r))
            {
                savedDisabled.Add((r, r.enabled));
            }
        }

        bool savedAnimEnabled = anim.enabled;
        var savedUwo = new List<(SkinnedMeshRenderer smr, bool value)>();

        var rt = new RenderTexture(rtSize, rtSize, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var tex = new Texture2D(rtSize, rtSize, TextureFormat.RGBA32, false);
        var outTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

        int savedCount = 0;
        Color32[]? lastGoodPx = null;
        bool dualMode = false;
        bool swapped = false;
        Color32[] baseline = Array.Empty<Color32>();
        var clear = new Color(0f, 0f, 0f, 0f);

        // Chosen capture configuration (probed below until something renders).
        bool useForcedFraming = true;
        bool useLayers = true;
        bool useRenderersOff = true;
        bool probeOk = false;

        try
        {
            // Accurate skinned bounds so the auto-framing fits each pose exactly.
            foreach (var r in keep)
            {
                if (r is SkinnedMeshRenderer smr)
                {
                    savedUwo.Add((smr, smr.updateWhenOffscreen));
                    smr.updateWhenOffscreen = true;
                }
            }

            // --- Give the dummy the real character controller so every pose state exists
            RuntimeAnimatorController? charController = null;
            var ch = Character.localCharacter;
            if (ch != null && ch.refs != null)
            {
                // The controller actually in use by the character is the
                // authoritative source of pose states (it can be swapped at
                // runtime, e.g. for head bob settings).
                if (ch.refs.animator != null)
                {
                    charController = ch.refs.animator.runtimeAnimatorController;
                }

                if (charController == null && ch.refs.animations != null)
                {
                    charController = ch.refs.animations.defaultController;
                }
            }

            if (charController != null && charController != origController)
            {
                anim.runtimeAnimatorController = charController;
                swapped = true;
            }

            anim.enabled = true;
            anim.Rebind();
            anim.SetFloat("Idle", 3f);
            anim.speed = 1f;
            anim.Update(0.02f);

            Plugin.Log.LogInfo(
                $"Animator setup: enabled={anim.enabled} (was {savedAnimEnabled}), " +
                $"active={anim.gameObject.activeInHierarchy}, swapped={swapped}, " +
                $"dummyCtrl='{(origController != null ? origController.name : "null")}', " +
                $"useCtrl='{(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null")}', " +
                $"stickerLayer={stickerLayer}, keep={keep.Count}, disabledNonChar={savedDisabled.Count}");

            // --- Camera setup: transparent background, full body framing.
            // Culling/layers/renderers are applied per probe below.
            cam.enabled = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.orthographicSize = FullBodyOrthoSize;
            cam.transform.localPosition = FullBodyLocalPos;
            if (urp != null)
            {
                urp.renderPostProcessing = false;
            }

            int visibleChars = 0;
            foreach (var r in keep)
            {
                if (r != null && r.gameObject.activeInHierarchy && r.enabled && !r.forceRenderingOff)
                {
                    visibleChars++;
                }
            }

            Plugin.Log.LogInfo(
                $"Capture env: camGOActive={cam.gameObject.activeInHierarchy}, camWorld={cam.transform.position}, " +
                $"charWorld={root.position}, visibleCharRenderers={visibleChars}/{keep.Count}, " +
                $"savedOrtho={savedOrtho}, savedPos={savedLocalPos}");

            void ApplyFraming(bool forced)
            {
                cam.orthographicSize = forced ? FullBodyOrthoSize : savedOrtho;
                cam.transform.localPosition = forced ? FullBodyLocalPos : savedLocalPos;
            }

            void ApplyIsolation(bool layers, bool renderersOff)
            {
                cam.cullingMask = layers ? 1 << stickerLayer : savedMask;
                foreach (var (go, layer) in savedLayers)
                {
                    if (go != null)
                    {
                        go.layer = layers ? stickerLayer : layer;
                    }
                }

                foreach (var (r, enabled) in savedDisabled)
                {
                    if (r != null)
                    {
                        r.enabled = renderersOff ? false : enabled;
                    }
                }
            }

            string FrustumInfo()
            {
                var mr = dummy.refs != null ? dummy.refs.mainRenderer : null;
                if (mr == null || cam == null)
                {
                    return "no mainRenderer";
                }

                var c = cam.WorldToViewportPoint(mr.bounds.center);
                return $"mainRig vp=({c.x:F2},{c.y:F2},z={c.z:F1}), boundsSize={mr.bounds.size:F1}";
            }

            var probes = new List<(string label, bool forced, bool layers, bool rOff, bool ok, int covered)>();

            void Probe(string label, bool forced, bool layers, bool renderersOff)
            {
                ApplyFraming(forced);
                ApplyIsolation(layers, renderersOff);
                var px = Capture(cam, rt, tex, savedTarget, clear, ref dualMode);
                int cov = CountCovered(px);
                bool ok = cov > 0;
                probes.Add((label, forced, layers, renderersOff, ok, cov));
                Plugin.Log.LogInfo($"Probe '{label}': covered={cov}/{px.Length}px, {FrustumInfo()}");
            }

            // The static capture first: the character exactly as it stands
            // right now, nothing isolated. This is also the fallback sticker.
            Probe("static(raw+savedFrame)", forced: false, layers: false, renderersOff: false);
            Probe("isolated+forcedFrame", forced: true, layers: true, renderersOff: true);
            Probe("layersOnly+forcedFrame", forced: true, layers: true, renderersOff: false);
            Probe("raw+forcedFrame", forced: true, layers: false, renderersOff: false);
            Probe("isolated+savedFrame", forced: false, layers: true, renderersOff: true);

            // Pick the first working configuration, best output first.
            var preference = new[]
            {
                (forced: true, layers: true, rOff: true),
                (forced: true, layers: true, rOff: false),
                (forced: false, layers: true, rOff: true),
                (forced: true, layers: false, rOff: false),
                (forced: false, layers: false, rOff: false),
            };

            foreach (var cand in preference)
            {
                foreach (var p in probes)
                {
                    if (p.ok && p.forced == cand.forced && p.layers == cand.layers && p.rOff == cand.rOff)
                    {
                        probeOk = true;
                        useForcedFraming = cand.forced;
                        useLayers = cand.layers;
                        useRenderersOff = cand.rOff;
                        break;
                    }
                }

                if (probeOk)
                {
                    break;
                }
            }

            if (probeOk)
            {
                ApplyFraming(useForcedFraming);
                ApplyIsolation(useLayers, useRenderersOff);
                baseline = Capture(cam, rt, tex, savedTarget, clear, ref dualMode);
                Plugin.Log.LogInfo(
                    $"Capture config: forcedFrame={useForcedFraming}, layers={useLayers}, " +
                    $"renderersOff={useRenderersOff}, dualMode={dualMode}, covered={CountCovered(baseline)}");
                if (!useLayers || !useRenderersOff)
                {
                    Plugin.Log.LogWarning("Isolation unavailable — stickers may include the scene background.");
                }
            }
            else
            {
                Plugin.Log.LogError("The character never rendered in the capture camera. See probe lines above.");
            }

            // --- Capture every pose ------------------------------------------
            for (int i = 0; i < poses.Count; i++)
            {
                if (cam == null || anim == null)
                {
                    break;
                }

                var p = poses[i];
                button.SetStatus($"Generando {i + 1}/{poses.Count}");

                ApplyPose(anim, p);
                yield return null;

                if (cam == null || anim == null)
                {
                    break;
                }

                // Re-assert the winning capture configuration in case a tab
                // switch tweened the camera meanwhile.
                ApplyFraming(useForcedFraming);
                ApplyIsolation(useLayers, useRenderersOff);

                // Per-pose auto-framing: fit this pose (extended arms, lying
                // down, ...) inside the view with margin for the outline.
                float edge = ((border + 8f) * 2f) / resolution;
                float margin = Mathf.Max(1.03f, 1f / Mathf.Max(0.25f, 1f - edge));
                FitCameraToBounds(cam, keep, margin);

                var px = Capture(cam, rt, tex, savedTarget, clear, ref dualMode);

                bool sameAsBaseline = SamePixels(baseline, px);
                if (i == 0)
                {
                    Plugin.Log.LogInfo(
                        $"Pose0 '{p.State}': stateMatches={StateMatches(anim, p.State)}, " +
                        $"covered={CountCovered(px)}/{px.Length}px, sameAsBaseline={sameAsBaseline}");
                }

                // A pose that failed to play renders exactly like the default
                // pose (baseline) or like the previously captured one.
                bool duplicate = sameAsBaseline || (lastGoodPx != null && SamePixels(lastGoodPx, px));
                if (duplicate)
                {
                    if (i == 0)
                    {
                        Plugin.Log.LogWarning($"Pose '{p.State}' did not apply, skipped.");
                    }
                    else
                    {
                        Plugin.Log.LogDebug($"Pose '{p.State}' rendered like a previous pose, skipped.");
                    }

                    continue;
                }

                var small = Downsample(px, rtSize, supersample);
                var sticker = AddOutline(small, resolution, border, dualMode);
                if (autoCrop)
                {
                    sticker = CropToFit(sticker, resolution, cropPad);
                }

                outTex.SetPixels32(sticker);
                byte[] png = outTex.EncodeToPNG();

                lastGoodPx = px;
                savedCount++;
                string file = Path.Combine(dir, $"{savedCount:00}_{Suffix(p.State)}.png");
                File.WriteAllBytes(file, png);
            }

            // Guarantee at least one output image: if no pose produced a new
            // frame, save the character exactly as it stands right now.
            if (savedCount == 0 && probeOk && baseline.Length > 0 && CountCovered(baseline) > 0)
            {
                var smallFallback = Downsample(baseline, rtSize, supersample);
                var fallbackSticker = AddOutline(smallFallback, resolution, border, dualMode);
                if (autoCrop)
                {
                    fallbackSticker = CropToFit(fallbackSticker, resolution, cropPad);
                }

                outTex.SetPixels32(fallbackSticker);
                File.WriteAllBytes(Path.Combine(dir, "00_estatico.png"), outTex.EncodeToPNG());
                savedCount = 1;
                Plugin.Log.LogWarning("No pose produced a new image; saved the static character pose as 00_estatico.png.");
            }

            if (savedCount > 0)
            {
                button.SetStatus($"¡Listo! {savedCount} stickers");
                Plugin.Log.LogInfo($"Saved {savedCount} stickers to: {dir}");
                if (Plugin.OpenFolderConfig.Value)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Could not open output folder: {e.Message}");
                    }
                }
            }
            else
            {
                button.SetStatus("No se generó ningún sticker (revisa el log)");
                Plugin.Log.LogError("No stickers were generated. Check the log above.");
            }
        }
        finally
        {
            Restore();

            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            if (tex != null)
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            if (outTex != null)
            {
                UnityEngine.Object.DestroyImmediate(outTex);
            }

            button.FinishGeneration();
        }

        // --- Local helpers ---------------------------------------------------

        Color32[] Capture(Camera c, RenderTexture texture, Texture2D target, RenderTexture? target0, Color clearColor, ref bool dual)
        {
            if (!dual)
            {
                c.backgroundColor = clearColor;
                c.targetTexture = texture;
                c.Render();
                c.targetTexture = target0;
                var px = ReadInto(texture, target);
                if (CornersOpaque(px, texture.width))
                {
                    Plugin.Log.LogWarning("Render texture alpha unavailable, using dual-background capture.");
                    dual = true;
                    return RenderDual(c, texture, target, target0, clearColor);
                }

                return px;
            }

            return RenderDual(c, texture, target, target0, clearColor);
        }

        void Restore()
        {
            var active = RenderTexture.active;
            if (active != null && active == rt)
            {
                RenderTexture.active = null;
            }

            if (cam != null)
            {
                cam.targetTexture = savedTarget;
                cam.clearFlags = savedClear;
                cam.backgroundColor = savedBg;
                cam.cullingMask = savedMask;
                cam.orthographicSize = savedOrtho;
                cam.rect = savedRect;
                cam.transform.localPosition = savedLocalPos;
                cam.enabled = savedCamEnabled;
                if (urp != null)
                {
                    urp.renderPostProcessing = savedPost;
                }
            }

            foreach (var (go, layer) in savedLayers)
            {
                if (go != null)
                {
                    go.layer = layer;
                }
            }

            foreach (var (r, enabled) in savedDisabled)
            {
                if (r != null)
                {
                    r.enabled = enabled;
                }
            }

            foreach (var (smr, uwo) in savedUwo)
            {
                if (smr != null)
                {
                    smr.updateWhenOffscreen = uwo;
                }
            }

            if (anim != null)
            {
                if (swapped)
                {
                    anim.runtimeAnimatorController = origController;
                }

                anim.Rebind();
                anim.SetFloat("Idle", 3f);
                anim.speed = origSpeed;
                anim.enabled = savedAnimEnabled;
                anim.Update(0.02f);
            }
        }
    }

    // --- Pose handling -------------------------------------------------------

    private static void ApplyPose(Animator anim, PoseInfo p)
    {
        anim.speed = 1f;

        if (!p.IsIdle)
        {
            anim.SetBool("Emote", true);
        }

        anim.Play(p.State, 0, 0f);
        anim.Update(0.02f);

        if (p.IsIdle && !StateMatches(anim, p.State))
        {
            // The idle variants live in a blend tree driven by the "Idle"
            // parameter instead of being playable states: rebind to the
            // default (idle) state and select the variant through the param.
            anim.Rebind();
            anim.Update(0.02f);
            anim.SetFloat("Idle", p.IdleIndex);
            anim.speed = 0f;
            anim.Update(0.02f);
            return;
        }

        anim.Play(p.State, 0, p.Time);
        anim.speed = 0f;
        anim.Update(0.02f);
    }

    private static bool StateMatches(Animator anim, string stateName)
    {
        var info = anim.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) || info.IsName("Base Layer." + stateName);
    }

    private static List<PoseInfo> BuildPoses(Dictionary<string, float> times)
    {
        var list = new List<PoseInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string state, bool idle = false, int idleIndex = 0)
        {
            if (seen.Add(state))
            {
                list.Add(new PoseInfo(state, SampleTime(times, state), idle, idleIndex));
            }
        }

        foreach (var s in EmoteStates)
        {
            Add(s);
        }

        // Emotes added by the game (or mods) that are missing from the static list.
        foreach (var s in WheelEmoteStates())
        {
            Add(s);
        }

        if (Plugin.IncludeIdlesConfig.Value)
        {
            for (int i = 1; i <= IdleCount; i++)
            {
                Add($"A_Scout_Idle{i}", idle: true, idleIndex: i);
            }
        }

        return list;
    }

    private static IEnumerable<string> WheelEmoteStates()
    {
        foreach (var wheel in Resources.FindObjectsOfTypeAll<EmoteWheel>())
        {
            if (wheel == null || !wheel.gameObject.scene.IsValid() || wheel.data == null)
            {
                continue;
            }

            foreach (var data in wheel.data)
            {
                if (data != null && !string.IsNullOrEmpty(data.anim))
                {
                    yield return data.anim;
                }
            }

            yield break;
        }
    }

    private static Dictionary<string, float> ParseSampleTimes(string raw)
    {
        var dict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
        {
            return dict;
        }

        foreach (var part in raw.Split(';'))
        {
            int idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            string key = part.Substring(0, idx).Trim();
            if (float.TryParse(part.Substring(idx + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
            {
                dict[key] = t;
            }
        }

        return dict;
    }

    private static float SampleTime(Dictionary<string, float> times, string state)
    {
        string suffix = Suffix(state);
        if (times.TryGetValue(suffix, out float t))
        {
            return t;
        }

        if (times.TryGetValue(state, out t))
        {
            return t;
        }

        return DefaultSampleTime;
    }

    private static string Suffix(string state)
    {
        int idx = state.LastIndexOf('_');
        return idx >= 0 ? state.Substring(idx + 1) : state;
    }

    internal static string StateSuffix(string state)
    {
        return Suffix(state);
    }

    /// <summary>All poses (state name + whether it is an idle variant), in
    /// generation order. Used by the pose dropdown in the UI.</summary>
    internal static List<(string state, bool isIdle)> GetPoseStates()
    {
        var poses = BuildPoses(ParseSampleTimes(Plugin.SampleTimesConfig.Value));
        var list = new List<(string, bool)>(poses.Count);
        foreach (var p in poses)
        {
            list.Add((p.State, p.IsIdle));
        }

        return list;
    }

    /// <summary>Restricts the pose list to the selection saved in the config
    /// (all / emotes / idles / a single pose state).</summary>
    private static List<PoseInfo> FilterBySelection(List<PoseInfo> poses)
    {
        string sel = Plugin.SelectedPosesConfig.Value;
        if (string.IsNullOrWhiteSpace(sel) || sel == "all")
        {
            return poses;
        }

        List<PoseInfo> result;
        if (sel == "emotes")
        {
            result = poses.FindAll(p => !p.IsIdle);
        }
        else if (sel == "idles")
        {
            result = poses.FindAll(p => p.IsIdle);
        }
        else
        {
            result = poses.FindAll(p => string.Equals(p.State, sel, StringComparison.OrdinalIgnoreCase));
        }

        if (result.Count == 0)
        {
            Plugin.Log.LogWarning($"Selection '{sel}' matched no poses — generating all.");
            return poses;
        }

        Plugin.Log.LogInfo($"Pose selection '{sel}': generating {result.Count} pose(s).");
        return result;
    }

    // --- Which renderers belong to the character -----------------------------

    private static HashSet<Renderer> BuildKeepSet(CustomizationRefs refs)
    {
        // Never cached: the passport dummy can be recreated between scenes.
        var keep = new HashSet<Renderer>();

        void AddRenderers(object? value)
        {
            switch (value)
            {
                case Renderer r when r != null:
                    keep.Add(r);
                    break;
                case GameObject go when go != null:
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        keep.Add(r);
                    }

                    break;
                case Array arr:
                    foreach (var item in arr)
                    {
                        AddRenderers(item);
                    }

                    break;
            }
        }

        if (refs.AllRenderers != null)
        {
            foreach (var r in refs.AllRenderers)
            {
                if (r != null)
                {
                    keep.Add(r);
                }
            }
        }

        foreach (var field in typeof(CustomizationRefs).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            // Shadow-catcher duplicates would draw a black blob on transparency.
            if (field.Name.Contains("Shadow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddRenderers(field.GetValue(refs));
        }

        return keep;
    }

    private static int FindUnusedLayer()
    {
        for (int i = 31; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
            {
                return i;
            }
        }

        return 31;
    }

    // --- Rendering helpers ---------------------------------------------------

    private static Color32[] ReadInto(RenderTexture rt, Texture2D tex)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = prev;
        return tex.GetPixels32();
    }

    private static Color32[] RenderDual(Camera cam, RenderTexture rt, Texture2D tex, RenderTexture? savedTarget, Color clear)
    {
        cam.backgroundColor = Color.black;
        cam.targetTexture = rt;
        cam.Render();
        var black = ReadInto(rt, tex);

        cam.backgroundColor = Color.white;
        cam.Render();
        var white = ReadInto(rt, tex);

        cam.targetTexture = savedTarget;
        cam.backgroundColor = clear;

        if (!dualDiagLogged)
        {
            dualDiagLogged = true;
            bool identical = SamePixels(black, white);
            Plugin.Log.LogInfo(
                $"Dual capture diag: blackCovered={CountCovered(black)}/{black.Length}px, " +
                $"whiteCovered={CountCovered(white)}/{white.Length}px, identical={identical}");
            if (identical)
            {
                Plugin.Log.LogError("Black and white renders are identical — RT readback or clear color is not working.");
            }
        }

        return CombineDual(black, white);
    }

    private static bool dualDiagLogged;

    /// <summary>
    /// Recovers coverage and straight color from a black-background and a
    /// white-background render of the same frame.
    /// </summary>
    private static Color32[] CombineDual(Color32[] black, Color32[] white)
    {
        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        var result = new Color32[black.Length];

        for (int i = 0; i < black.Length; i++)
        {
            float br = ToUnit(black[i].r, linear);
            float bg = ToUnit(black[i].g, linear);
            float bb = ToUnit(black[i].b, linear);
            float wr = ToUnit(white[i].r, linear);
            float wg = ToUnit(white[i].g, linear);
            float wb = ToUnit(white[i].b, linear);

            float ar = Mathf.Clamp01(1f - (wr - br));
            float ag = Mathf.Clamp01(1f - (wg - bg));
            float ab = Mathf.Clamp01(1f - (wb - bb));
            float a = (ar + ag + ab) / 3f;

            if (a < 0.004f)
            {
                result[i] = new Color32(255, 255, 255, 0);
                continue;
            }

            byte r = FromUnit(Mathf.Clamp01(br / a), linear);
            byte g = FromUnit(Mathf.Clamp01(bg / a), linear);
            byte b = FromUnit(Mathf.Clamp01(bb / a), linear);
            result[i] = new Color32(r, g, b, (byte)Mathf.RoundToInt(a * 255f));
        }

        return result;
    }

    private static float ToUnit(byte v, bool linear)
    {
        float f = v / 255f;
        if (!linear)
        {
            return f;
        }

        // sRGB -> linear
        return f <= 0.04045f ? f / 12.92f : Mathf.Pow((f + 0.055f) / 1.055f, 2.4f);
    }

    private static byte FromUnit(float f, bool linear)
    {
        if (!linear)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(f) * 255f);
        }

        // linear -> sRGB
        float s = f <= 0.0031308f ? f * 12.92f : 1.055f * Mathf.Pow(f, 1f / 2.4f) - 0.055f;
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(s) * 255f);
    }

    private static bool CornersOpaque(Color32[] px, int size)
    {
        if (px.Length < size * size)
        {
            return false;
        }

        int sum = px[0].a + px[size - 1].a + px[(size - 1) * size].a + px[size * size - 1].a;
        return sum > 0;
    }

    private static int CountCovered(Color32[] px)
    {
        int n = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a > 0)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// Fits the camera view to the live bounds of the visible character
    /// renderers, so every pose (extended arms, lying down, ...) fits inside
    /// the frame with margin for the outline.
    /// </summary>
    private static bool FitCameraToBounds(Camera cam, HashSet<Renderer> keep, float margin)
    {
        bool found = false;
        var b = default(Bounds);
        foreach (var r in keep)
        {
            if (r == null || !r.gameObject.activeInHierarchy || !r.enabled || r.forceRenderingOff)
            {
                continue;
            }

            if (!found)
            {
                b = r.bounds;
                found = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }

        if (!found || b.size.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        var t = cam.transform;
        Vector3 right = t.right;
        Vector3 up = t.up;
        Vector3 forward = t.forward;

        float maxOffX = 0f, maxOffY = 0f;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            var d = corner - b.center;
            maxOffX = Mathf.Max(maxOffX, Mathf.Abs(Vector3.Dot(d, right)));
            maxOffY = Mathf.Max(maxOffY, Mathf.Abs(Vector3.Dot(d, up)));
        }

        cam.orthographicSize = Mathf.Max(0.4f, Mathf.Max(maxOffX, maxOffY) * margin);

        // Move the camera along its view line so the bounds center sits in
        // the middle of the frame (same distance to the character).
        var toCenter = b.center - t.position;
        float depth = Vector3.Dot(toCenter, forward);
        t.position = b.center - (forward * depth);
        return true;
    }

    /// <summary>
    /// Crops to the sticker bounding box (plus padding) and fits it back into
    /// the square canvas, so every pose fills the image without empty space.
    /// </summary>
    private static Color32[] CropToFit(Color32[] px, int size, int pad)
    {
        int minX = size, minY = size, maxX = -1, maxY = -1;
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                if (px[row + x].a > 8)
                {
                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }
        }

        if (maxX < 0)
        {
            return px;
        }

        int x0 = Mathf.Max(0, minX - pad);
        int y0 = Mathf.Max(0, minY - pad);
        int x1 = Mathf.Min(size - 1, maxX + pad);
        int y1 = Mathf.Min(size - 1, maxY + pad);
        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;
        if (w >= size && h >= size)
        {
            return px;
        }

        float scale = Mathf.Min(size / (float)w, size / (float)h);
        int ow = Mathf.Min(size, Mathf.RoundToInt(w * scale));
        int oh = Mathf.Min(size, Mathf.RoundToInt(h * scale));
        if (ow < 1 || oh < 1)
        {
            return px;
        }

        int offX = (size - ow) / 2;
        int offY = (size - oh) / 2;
        var result = new Color32[size * size];

        for (int y = 0; y < oh; y++)
        {
            float sy = y0 + (((y + 0.5f) / scale) - 0.5f);
            int ya = Mathf.Clamp(Mathf.FloorToInt(sy), 0, size - 1);
            int yb = Mathf.Clamp(ya + 1, 0, size - 1);
            float fy = Mathf.Clamp01(sy - ya);

            for (int x = 0; x < ow; x++)
            {
                float sx = x0 + (((x + 0.5f) / scale) - 0.5f);
                int xa = Mathf.Clamp(Mathf.FloorToInt(sx), 0, size - 1);
                int xb = Mathf.Clamp(xa + 1, 0, size - 1);
                float fx = Mathf.Clamp01(sx - xa);

                var c00 = px[(ya * size) + xa];
                var c10 = px[(ya * size) + xb];
                var c01 = px[(yb * size) + xa];
                var c11 = px[(yb * size) + xb];

                // Interpolate premultiplied color to avoid dark/white fringes
                // on semi-transparent edges, then straighten again.
                float a = Lerp4(c00.a, c10.a, c01.a, c11.a, fx, fy) / 255f;
                byte rr = 255, gg = 255, bb = 255;
                if (a > 0.002f)
                {
                    float pr = Lerp4PM(c00.r, c00.a, c10.r, c10.a, c01.r, c01.a, c11.r, c11.a, fx, fy);
                    float pg = Lerp4PM(c00.g, c00.a, c10.g, c10.a, c01.g, c01.a, c11.g, c11.a, fx, fy);
                    float pb = Lerp4PM(c00.b, c00.a, c10.b, c10.a, c01.b, c01.a, c11.b, c11.a, fx, fy);
                    rr = ClampByte(pr / a);
                    gg = ClampByte(pg / a);
                    bb = ClampByte(pb / a);
                }

                result[((offY + y) * size) + offX + x] =
                    new Color32(rr, gg, bb, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }

        return result;

        static float Lerp4(float a, float b, float c, float d, float fx, float fy)
        {
            float top = Mathf.Lerp(a, b, fx);
            float bottom = Mathf.Lerp(c, d, fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        static float Lerp4PM(float ra, byte aa, float rb, byte ab, float rc, byte ac, float rd, byte ad, float fx, float fy)
        {
            float top = Mathf.Lerp((ra * aa) / 65025f, (rb * ab) / 65025f, fx);
            float bottom = Mathf.Lerp((rc * ac) / 65025f, (rd * ad) / 65025f, fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        static byte ClampByte(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        }
    }

    private static bool SamePixels(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
            {
                return false;
            }
        }

        return true;
    }

    // --- Image processing ----------------------------------------------------

    private static Color32[] Downsample(Color32[] src, int srcSize, int factor)
    {
        int dst = srcSize / factor;
        var result = new Color32[dst * dst];
        int n = factor * factor;

        for (int y = 0; y < dst; y++)
        {
            for (int x = 0; x < dst; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < factor; j++)
                {
                    var row = (y * factor + j) * srcSize + x * factor;
                    for (int i = 0; i < factor; i++)
                    {
                        var c = src[row + i];
                        r += c.r;
                        g += c.g;
                        b += c.b;
                        a += c.a;
                    }
                }

                result[y * dst + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the classic sticker look: the silhouette is dilated by
    /// <paramref name="border"/> pixels, smoothed, and filled white behind
    /// the character (which keeps its own anti-aliased transparency).
    /// </summary>
    private static Color32[] AddOutline(Color32[] px, int size, int border, bool straightColors)
    {
        var alpha = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            alpha[i] = px[i].a;
        }

        // Sticker-style silhouette: a morphological closing rounds concavities
        // and fills tiny gaps (between fingers, flower leaves, ...) instead of
        // hugging every small detail.
        int roundness = Mathf.Clamp(Plugin.RoundnessConfig.Value, 0, 64);
        var mask = new byte[alpha.Length];
        for (int i = 0; i < alpha.Length; i++)
        {
            mask[i] = alpha[i] > 6 ? (byte)255 : (byte)0;
        }

        if (roundness > 0)
        {
            mask = MaxFilter(mask, size, roundness, horizontal: true);
            mask = MaxFilter(mask, size, roundness, horizontal: false);
            mask = MinFilter(mask, size, roundness, horizontal: true);
            mask = MinFilter(mask, size, roundness, horizontal: false);
        }

        byte[] band;
        if (border > 0)
        {
            band = MaxFilter(mask, size, border, horizontal: true);
            band = MaxFilter(band, size, border, horizontal: false);
            band = BoxBlur3(band, size, passes: 2);
        }
        else
        {
            band = new byte[alpha.Length];
        }

        var result = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            byte outA = c.a > band[i] ? c.a : band[i];

            if (c.a > 6)
            {
                byte r = c.r, g = c.g, b = c.b;
                if (!straightColors)
                {
                    // The render is premultiplied by coverage; un-premultiply
                    // so semi-transparent edges do not get a dark fringe.
                    r = Unpremultiply(c.r, c.a);
                    g = Unpremultiply(c.g, c.a);
                    b = Unpremultiply(c.b, c.a);
                }

                result[i] = new Color32(r, g, b, outA);
            }
            else
            {
                result[i] = new Color32(255, 255, 255, outA);
            }
        }

        return result;
    }

    private static byte Unpremultiply(byte v, byte a)
    {
        if (a == 0 || a == 255)
        {
            return v;
        }

        int x = (v * 255 + (a / 2)) / a;
        return (byte)(x > 255 ? 255 : x);
    }

    private static byte[] MaxFilter(byte[] src, int size, int radius, bool horizontal)
    {
        var dst = new byte[src.Length];

        for (int line = 0; line < size; line++)
        {
            for (int p = 0; p < size; p++)
            {
                int start = p - radius < 0 ? 0 : p - radius;
                int end = p + radius >= size ? size - 1 : p + radius;
                byte max = 0;
                for (int k = start; k <= end; k++)
                {
                    byte v = horizontal ? src[line * size + k] : src[k * size + line];
                    if (v > max)
                    {
                        max = v;
                    }
                }

                if (horizontal)
                {
                    dst[line * size + p] = max;
                }
                else
                {
                    dst[p * size + line] = max;
                }
            }
        }

        return dst;
    }

    private static byte[] MinFilter(byte[] src, int size, int radius, bool horizontal)
    {
        var dst = new byte[src.Length];

        for (int line = 0; line < size; line++)
        {
            for (int p = 0; p < size; p++)
            {
                int start = p - radius < 0 ? 0 : p - radius;
                int end = p + radius >= size ? size - 1 : p + radius;
                byte min = 255;
                for (int k = start; k <= end; k++)
                {
                    byte v = horizontal ? src[line * size + k] : src[k * size + line];
                    if (v < min)
                    {
                        min = v;
                    }
                }

                if (horizontal)
                {
                    dst[line * size + p] = min;
                }
                else
                {
                    dst[p * size + line] = min;
                }
            }
        }

        return dst;
    }

    private static byte[] BoxBlur3(byte[] src, int size, int passes)
    {
        byte[] cur = src;
        for (int pass = 0; pass < passes; pass++)
        {
            var tmp = new byte[cur.Length];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int x0 = x > 0 ? x - 1 : 0;
                    int x1 = x < size - 1 ? x + 1 : size - 1;
                    int y0 = y > 0 ? y - 1 : 0;
                    int y1 = y < size - 1 ? y + 1 : size - 1;

                    int sum = 0, n = 0;
                    for (int yy = y0; yy <= y1; yy++)
                    {
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            sum += cur[yy * size + xx];
                            n++;
                        }
                    }

                    tmp[y * size + x] = (byte)((sum + (n / 2)) / n);
                }
            }

            cur = tmp;
        }

        return cur;
    }
}
