using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace PeakStickers;

/// <summary>
/// Adds a button to the customization passport that generates
/// transparent-background PNG stickers of your character in every pose.
/// </summary>
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static Plugin? Instance { get; private set; }

    internal static ConfigEntry<string> OutputDirectoryConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> OpenFolderConfig { get; private set; } = null!;
    internal static ConfigEntry<int> ResolutionConfig { get; private set; } = null!;
    internal static ConfigEntry<int> BorderWidthConfig { get; private set; } = null!;
    internal static ConfigEntry<int> RoundnessConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> AutoCropConfig { get; private set; } = null!;
    internal static ConfigEntry<bool> IncludeIdlesConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SampleTimesConfig { get; private set; } = null!;
    internal static ConfigEntry<string> SelectedPosesConfig { get; private set; } = null!;

    private StickerButton? stickerButton;
    private bool buttonErrorLogged;

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        OutputDirectoryConfig = Config.Bind(
            "1. Salida",
            "OutputDirectory",
            string.Empty,
            "Carpeta donde se guardan los stickers PNG. Vacío = Mis Imágenes/PEAK Stickers");

        OpenFolderConfig = Config.Bind(
            "1. Salida",
            "OpenFolderWhenDone",
            false,
            "Abrir la carpeta de salida cuando termine de generar");

        ResolutionConfig = Config.Bind(
            "2. Stickers",
            "Resolution",
            1024,
            "Tamaño de cada sticker en píxeles (512, 1024 o 2048)");

        BorderWidthConfig = Config.Bind(
            "2. Stickers",
            "BorderWidth",
            12,
            "Grosor del borde blanco en píxeles (0 = sin línea). Se puede cambiar con el slider de la interfaz");

        RoundnessConfig = Config.Bind(
            "2. Stickers",
            "Roundness",
            14,
            "Redondeo del borde estilo pegatina (0-64): rellena huequitos y suaviza detalles pequeños de la silueta");

        AutoCropConfig = Config.Bind(
            "2. Stickers",
            "AutoCrop",
            true,
            "Recortar cada imagen a la caja del personaje y ajustarla al tamaño del sticker");

        SelectedPosesConfig = Config.Bind(
            "2. Stickers",
            "SelectedPoses",
            "all",
            "Poses a generar: all, emotes, idles o el nombre de una pose (ej. A_Scout_Emote_Wave). Se puede cambiar con el desplegable");

        IncludeIdlesConfig = Config.Bind(
            "2. Stickers",
            "IncludeIdlePoses",
            true,
            "Incluir también las 8 variantes de pose idle");

        SampleTimesConfig = Config.Bind(
            "3. Poses",
            "SampleTimes",
            StickerGenerator.DefaultSampleTimes,
            "Frame de muestra (0 a 1) de cada pose. Ejemplo: BackFlip=0.45;PlayDead=0.95");

        Log.LogInfo($"{Name} loaded!");
    }

    private void Update()
    {
        var manager = PassportManager.instance;
        if (manager == null)
        {
            return;
        }

        // The button lives on the passport canvas, so it is created as soon as
        // the customization passport exists in the scene.
        if (stickerButton == null)
        {
            try
            {
                stickerButton = StickerButton.Create(manager);
            }
            catch (Exception e)
            {
                if (!buttonErrorLogged)
                {
                    Log.LogError($"Could not create the sticker button: {e}");
                    buttonErrorLogged = true;
                }

                return;
            }
        }

        stickerButton.SetPassportOpen(manager.isOpen);
    }

    /// <summary>
    /// Runs the generation coroutine on the plugin itself (always active),
    /// so it survives closing or hiding the passport UI mid-generation.
    /// </summary>
    internal void RunGeneration(PassportManager manager, StickerButton button)
    {
        StartCoroutine(StickerGenerator.Generate(manager, button));
    }

    internal void ScheduleLabelReset(StickerButton button)
    {
        StartCoroutine(button.ResetLabelSoon());
    }

    internal void SaveConfig()
    {
        Config.Save();
    }
}
