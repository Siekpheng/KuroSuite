# KuroSuite

A lightweight, native Windows (`#00FFFF` cyberpunk cyan) hardware display and audio visualization suite built in C# (.NET Framework 4.x — zero external runtimes required).

---

## Apps Included

### 1. **KuroHUD** (`KuroHUD/`)
Unified controller for dual case LCD screens + GPU RGB sleep synchronization:
- **Thermalright Trofeo (`1920×462` USB `0416:5408`)** + **Lovingcool AIO (`480×480` USB-CDC serial)** managed from a single tray icon and dark-cyan Control Center (`--ui`).
- **Custom Cyan Matrix HUD (`lovingcool-cyan.layout`)** with circular gauges, synced `15 FPS` idle / `25 FPS` active frame rates, and smooth `550ms` frosted-blur transitions on the Lovingcool display.
- **Accurate Unified Gaming Mode Sync:** Excludes Wallpaper Engine GPU load via PDH and keeps both screens in 1:1 Gaming Mode lockstep without false triggers.
- **Full Unicode / Khmer Music Support:** Automatically detects non-Latin tracks (Khmer, Thai, Japanese, Korean, Chinese, Arabic, etc.) and renders properly shaped track cards via Windows Uniscribe (`TextRenderer`) while keeping live 25 FPS Cava spectrum bars active.
- **Auto Monitor-Sleep Sync (Twinkle Tray / Windows Display Power):** Automatically turns off both case screens **and** your **Sapphire NITRO+ RX 6700 XT** GPU RGB (directly via AMD ADL2 OEM I2C `0x28`, zero SignalRGB conflict) when your main monitor turns off, and restores everything when you wake the monitor.
- **USB Auto-Recovery:** Automatically cycles the stalled USB hub port (`IOCTL_USB_HUB_CYCLE_PORT`) if the Thermalright WinUSB endpoint ever times out.

![KuroHUD Control Center](docs/kurohud_control_center.png)

---

### 2. **KuroBeat** (`KuroBeat/`)
A 60 FPS desktop monitor music visualizer matching the same dark-glass `#00FFFF` cyberpunk aesthetic:
- **Live 96-bar WASAPI Loopback FFT Spectrum** + **Windows Media Session** album art, track info, progress bar, and playback controls.
- **3 Visualizer Styles (`V`):** Cyber Bars (with reflections & peak caps), Mirror Wave, and symmetric Radial Ring around circular album art.
- **4 Color Themes (`M`):** Pure Cyan (`#00FFFF`), Adaptive Album Art, Matrix Green, and Neon Violet.
- **3 Backgrounds (`G`):** Matrix Rain + Cover Glow, Blurred Album Art, or Pure Black (OLED).
- **60 FPS at Native 1440p Fullscreen:** Pre-baked `Format32bppPArgb` background cache, cached album art, and pre-rendered Matrix glyph sprites.
- **Monitor & UX Features:** Multi-monitor snap (`1` / `2`), Fullscreen (`F11` / double-click), Borderless draggable/resizable mode (`B`), Always on Top (`T`), and auto-hiding HUD + mouse cursor after 2.4s idle.

| Cyber Bars | Mirror Wave | Radial Ring |
| :---: | :---: | :---: |
| ![Cyber Bars](docs/kurobeat_bars.png) | ![Mirror Wave](docs/kurobeat_mirror.png) | ![Radial Ring](docs/kurobeat_radial.png) |

---

## Building from Source (Windows 10 / 11)

No Visual Studio or SDK installation is needed — both apps compile directly with Windows' built-in `.NET Framework 4.x` C# compiler (`csc.exe`):

```powershell
# Build KuroBeat (outputs KuroBeat\KuroBeat.exe)
powershell -ExecutionPolicy Bypass -File .\KuroBeat\build.ps1

# Build KuroHUD (outputs KuroHUD\trofeo_lcd.exe)
powershell -ExecutionPolicy Bypass -File .\KuroHUD\build.ps1
```

## KuroBeat Keyboard Shortcuts

| Key | Action |
| :--- | :--- |
| `V` | Cycle Visualizer Style (Bars / Mirror / Radial) |
| `M` | Cycle Color Theme (Cyan / Adaptive / Green / Violet) |
| `G` | Cycle Background (Matrix / Cover Blur / Pure Black) |
| `F11` / Double-Click | Toggle Fullscreen |
| `1` / `2` | Move to Monitor 1 or Monitor 2 |
| `B` | Toggle Borderless Window |
| `T` | Toggle Always on Top |
| `H` | Toggle Track Info Card |
| `Space` / `←` / `→` | Play/Pause, Previous, Next Track |

## License

MIT License — see [LICENSE](LICENSE).
