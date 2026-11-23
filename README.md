<div align="center">
<h1>Peak Stickers</h1>

![AI-Guided Development](https://img.shields.io/badge/AI--Guided-Development-8A2BE2?style=flat)  
[![Thunderstore](https://img.shields.io/badge/Thunderstore-PeakStickers-7B4DF0?style=flat)](https://thunderstore.io/c/peak/p/CoolShitREN/PeakStickers)  
**English** · [Español](https://github.com/ema28pro/Peak-Stickers/blob/main/README_ES.md)
</div>


[![Multiple poses generated in the output folder](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/Captura%20de%20pantalla%202026-11-22%20190435.png)](https://github.com/ema28pro/Peak-Stickers/tree/main/img/examples)

| I'm Here | Shrug | Idle 2 | BackFlip |
| :---: | :---: | :---: | :---: |
| ![I'm Here](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/01_ImHere.png) | ![Shrug](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/01_Shrug.png) | ![Idle 2](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/02_Idle2.png) | ![BackFlip](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/22_BackFlip.png) |


Generate **PNG stickers of your PEAK character with their outfit**, in **every pose**, with a **transparent background** and the classic **white sticker border**.

You can see all available poses in the [`img/examples`](https://github.com/ema28pro/Peak-Stickers/tree/main/img/examples) folder.

## How does it work?

1. Open your **customization notebook** (the passport) in-game.
2. You'll see a new **"Generar stickers"** button in the top-right corner.
3. Click it: the mod captures your character's 3D model in every pose (all 22 emote wheel emotes + 8 idle pose variants) and saves one PNG per pose.
4. The stickers are saved to **`Pictures\PEAK Stickers\<date>`**.

Each sticker is exported in high quality (rendered at 2× and downscaled), with no background and a smooth white border around the character.

![Generate stickers button in the customization notebook](https://raw.githubusercontent.com/ema28pro/Peak-Stickers/main/img/vista_previa.png)

## Requirements

- [BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/) (automatic dependency on Thunderstore).

## Configuration

Optional: edit `BepInEx/config/ema28pro.PeakStickers.cfg`

| Option | Default | Description |
| --- | --- | --- |
| `OutputDirectory` | *(empty)* | Output folder. Empty = `Pictures/PEAK Stickers`. |
| `OpenFolderWhenDone` | `false` | Open the output folder when finished. |
| `Resolution` | `1024` | Size of each sticker in pixels (512/1024/2048). |
| `BorderWidth` | `12` | Width of the white border in pixels. |
| `IncludeIdlePoses` | `true` | Include the 8 idle pose variants. |
| `SampleTimes` | *(table)* | Sample frame (0..1) per pose. E.g.: `BackFlip=0.45;PlayDead=0.95`. |

### Fine-tuning poses

Each animation has a "sample frame": poses that end in a held position (collapsing, sitting, etc.) are captured **at the end** of the animation, while acrobatic ones (backflip) are captured **at an intermediate frame**. If a pose doesn't look the way you want, adjust its value in `SampleTimes` and generate again.

## Building (optional)

```sh
dotnet build -c Release
```

The Thunderstore package is output to `./artifacts/thunderstore/`. Requires [.NET SDK 10](https://dotnet.microsoft.com/download) and the game installed (to reference its assemblies).

## Credits

- Built with the [official PEAK mod template](https://github.com/PEAKModding/BepInExTemplate).
