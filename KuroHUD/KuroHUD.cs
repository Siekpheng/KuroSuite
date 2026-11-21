using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace LovingcoolLcd
{
    enum ScreenMode { Dashboard, Music, Gaming }

    static class AppPaths
    {
        public static readonly string TrofeoDir = ResolveTrofeoDir();
        public static readonly string LovingcoolDir = ResolveLovingcoolDir();
        public static string TrofeoCoreExe { get { return Path.Combine(TrofeoDir, "trofeo_lcd_core.exe"); } }
        public static string TrofeoConfigExe { get { return Path.Combine(TrofeoDir, "trofeo_config.exe"); } }
        public static string TrofeoConf { get { return Path.Combine(TrofeoDir, "trofeo.conf"); } }
        public static string ProfilesDir { get { return Path.Combine(TrofeoDir, "profiles"); } }
        public static string ActiveProfileFile { get { return Path.Combine(ProfilesDir, "active"); } }
        public static string LauncherLog { get { return Path.Combine(TrofeoDir, "launcher.log"); } }
        public static string LovingcoolConf { get { return Path.Combine(LovingcoolDir, "lovingcool.conf"); } }
        public static string LovingcoolStatus { get { return Path.Combine(LovingcoolDir, "lovingcool_status.txt"); } }
        public static string AmdBinPath { get { return Path.Combine(LovingcoolDir, "AMDFamily17.bin"); } }

        static string ResolveTrofeoDir()
        {
            string bd = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(bd, "trofeo_lcd_core.exe"))) return bd.TrimEnd('\\');
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ThermalRight Screen");
        }

        static string ResolveLovingcoolDir()
        {
            string bd = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(bd, "lovingcool.conf")) && File.Exists(Path.Combine(bd, "AMDFamily17.bin")))
                return bd.TrimEnd('\\');
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lovingcool Screen");
        }
    }

    static class TrofeoConfSync
    {
        static readonly object SyncLock = new object();

        public static string GetActiveValue(string key, string fallback)
        {
            lock (SyncLock)
            {
                try
                {
                    if (!File.Exists(AppPaths.TrofeoConf)) return fallback;
                    var rx = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=\s*([^\r\n#]+)", RegexOptions.IgnoreCase);
                    string found = fallback;
                    foreach (string line in File.ReadAllLines(AppPaths.TrofeoConf, Encoding.UTF8))
                    {
                        var m = rx.Match(line);
                        if (m.Success) found = m.Groups[1].Value.Trim();
                    }
                    return found;
                }
                catch { return fallback; }
            }
        }

        public static void SetActiveValue(string key, string value)
        {
            lock (SyncLock)
            {
                try
                {
                    if (!File.Exists(AppPaths.TrofeoConf)) return;
                    string text = File.ReadAllText(AppPaths.TrofeoConf, new UTF8Encoding(false));
                    var rx = new Regex(@"(?m)^([ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)([^\r\n#]*)([ \t]*#[^\r\n]*)?", RegexOptions.IgnoreCase);
                    string updated;
                    if (rx.IsMatch(text))
                    {
                        updated = rx.Replace(text, m =>
                        {
                            string comment = m.Groups[3].Success ? m.Groups[3].Value : "";
                            return m.Groups[1].Value + value + (string.IsNullOrEmpty(comment) ? "" : " " + comment.TrimStart());
                        });
                    }
                    else
                    {
                        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                        updated = text.TrimEnd() + nl + key + " = " + value + nl;
                    }
                    File.WriteAllText(AppPaths.TrofeoConf, updated, new UTF8Encoding(false));
                }
                catch { }
            }
        }

        public static List<string> GetAvailableLayouts()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] builtins = { "lovingcool-cyan", "orbit", "tiles", "deck", "cards", "console", "default" };
            foreach (string b in builtins)
            {
                if (seen.Add(b)) result.Add(b);
            }
            try
            {
                string layoutsDir = Path.Combine(AppPaths.TrofeoDir, "layouts");
                if (Directory.Exists(layoutsDir))
                {
                    foreach (string f in Directory.GetFiles(layoutsDir, "*.layout"))
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrEmpty(name) || name.Equals("unicode-music", StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(name)) result.Add(name);
                    }
                }
            }
            catch { }
            return result;
        }

        public static void EnsureProfiles()
        {
            lock (SyncLock)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.ProfilesDir);
                    WriteIfMissing("monitor-off.profile",
                        "brightness = 0\r\n" +
                        "background_effect = none\r\n" +
                        "background_color = #000000\r\n" +
                        "music_screen = false\r\n" +
                        "gaming_screen = false\r\n" +
                        "gaming_layout = classic\r\n" +
                        "gaming_bg = color\r\n" +
                        "gaming_bg_color = #000000\r\n" +
                        "gaming_colors = #000000, #000000\r\n" +
                        "gaming_text_color = #000000\r\n" +
                        "text_color = #000000\r\n" +
                        "clock_color = #000000\r\n" +
                        "color = #000000\r\n" +
                        "layout = default\r\n" +
                        "hide = spectrum, clock, clock_date, cpu, gpu, mem, uptime, time, date, net, disk, volume, nowplaying, weather, ticker, dashboard\r\n");
                    WriteIfMissing("force-dashboard.profile", "music_screen = false\r\ngaming_screen = false\r\n");
                    WriteIfMissing("force-music.profile", "music_screen = true\r\ngaming_screen = false\r\nlayout = music\r\nlayout_spectrum = true\r\n");
                    WriteProfileExact("force-gaming.profile", "gaming_screen = true\r\nmusic_screen = false\r\ngaming_gpu_percent = 1\r\ngaming_delay = 0\r\ngaming_hold = 60\r\n");
                    WriteProfileExact("unicode-music.profile", "music_screen = false\r\ngaming_screen = false\r\nlayout = unicode-music\r\nlayout_spectrum = true\r\n");
                }
                catch { }
            }
        }

        static void WriteIfMissing(string fileName, string body)
        {
            string p = Path.Combine(AppPaths.ProfilesDir, fileName);
            if (!File.Exists(p)) File.WriteAllText(p, body, Encoding.ASCII);
        }

        static void WriteProfileExact(string fileName, string body)
        {
            string p = Path.Combine(AppPaths.ProfilesDir, fileName);
            if (File.Exists(p))
            {
                try
                {
                    if (File.ReadAllText(p, Encoding.ASCII) == body) return;
                }
                catch { }
            }
            File.WriteAllText(p, body, Encoding.ASCII);
        }

        public static string GetActiveProfile()
        {
            lock (SyncLock)
            {
                try
                {
                    if (!File.Exists(AppPaths.ActiveProfileFile)) return "none";
                    string s = File.ReadAllText(AppPaths.ActiveProfileFile, Encoding.UTF8).Trim();
                    return string.IsNullOrEmpty(s) ? "none" : s;
                }
                catch { return "none"; }
            }
        }

        public static void SetActiveProfile(string profileName)
        {
            lock (SyncLock)
            {
                try
                {
                    EnsureProfiles();
                    if (string.IsNullOrEmpty(profileName) || profileName.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(AppPaths.ActiveProfileFile)) File.Delete(AppPaths.ActiveProfileFile);
                    }
                    else
                    {
                        File.WriteAllText(AppPaths.ActiveProfileFile, profileName, Encoding.ASCII);
                    }
                }
                catch { }
            }
        }

        public static void ClearManagedProfiles()
        {
            string cur = GetActiveProfile();
            if (cur == "monitor-off" || cur == "force-dashboard" || cur == "force-music" || cur == "force-gaming" || cur == "unicode-music")
                SetActiveProfile(null);
        }
    }

    static class TrofeoUnicodeMusicBridge
    {
        static string lastRenderKey = "";
        static bool toggleSlot;

        public static bool NeedsUnicodeFallback(MediaReader media)
        {
            if (media == null) return false;
            return HasNonLatinScript(media.Title) || HasNonLatinScript(media.Artist) || HasNonLatinScript(media.Album);
        }

        public static bool HasNonLatinScript(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch <= 0x024F) continue;
                if (ch >= 0x2000 && ch <= 0x206F) continue; // dashes, smart quotes, bullets
                if (ch == 0x2122 || ch == 0x00B7) continue;
                return true;
            }
            return false;
        }

        static bool ContainsKhmer(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if ((ch >= 0x1780 && ch <= 0x17FF) || (ch >= 0x19E0 && ch <= 0x19FF)) return true;
            }
            return false;
        }

        public static void EnsureRendered(MediaReader media, Config cfg)
        {
            try
            {
                string title = media.Title ?? "";
                string artist = media.Artist ?? "";
                string album = media.Album ?? "";
                int posBucket = (int)(media.Position.TotalSeconds / 3.0);
                int durSec = (int)media.Duration.TotalSeconds;
                bool hasCover;
                lock (media.CoverLock) hasCover = (media.CoverArt != null);

                string key = string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                    title, artist, album, media.IsPlaying ? 1 : 0, posBucket, durSec, hasCover ? 1 : 0);
                if (key == lastRenderKey) return;
                lastRenderKey = key;

                string layoutsDir = Path.Combine(AppPaths.TrofeoDir, "layouts");
                Directory.CreateDirectory(layoutsDir);
                toggleSlot = !toggleSlot;
                string imgName = toggleSlot ? "unicode_music_a.png" : "unicode_music_b.png";
                string imgPath = Path.Combine(layoutsDir, imgName);
                string layoutPath = Path.Combine(layoutsDir, "unicode-music.layout");

                Color cyan = cfg != null ? cfg.ThemeColor : Color.FromArgb(0, 255, 255);
                Color ice = Color.FromArgb(230, 255, 255);
                Color dim = Color.FromArgb(158, 216, 230);

                // Opaque 24bpp bitmap so Win32 TextRenderer (Uniscribe complex-script shaping) renders cleanly
                using (var bmp = new Bitmap(1920, 462, PixelFormat.Format24bppRgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.FromArgb(4, 12, 18));

                    // Soft dark blurred cover backdrop
                    lock (media.CoverLock)
                    {
                        if (media.BlurredBg != null)
                        {
                            g.DrawImage(media.BlurredBg, new Rectangle(0, 0, 1920, 462));
                            using (var ov = new SolidBrush(Color.FromArgb(195, 3, 10, 16)))
                                g.FillRectangle(ov, 0, 0, 1920, 462);
                        }
                    }

                    // Outer subtle cyan frame
                    using (var framePen = new Pen(Color.FromArgb(90, cyan), 2f))
                        g.DrawRectangle(framePen, 10, 10, 1899, 441);

                    // Left Album Cover with 5px #00FFFF rounded border (matching Trofeo Music mode)
                    var artRect = new Rectangle(46, 51, 360, 360);
                    using (var clip = MakeRoundedRect(artRect, 14))
                    {
                        var oldClip = g.Clip;
                        g.SetClip(clip);
                        lock (media.CoverLock)
                        {
                            if (media.CoverArt != null)
                                g.DrawImage(media.CoverArt, artRect);
                            else
                            {
                                using (var br = new SolidBrush(Color.FromArgb(10, 28, 38)))
                                    g.FillRectangle(br, artRect);
                            }
                        }
                        g.Clip = oldClip;
                        using (var borderPen = new Pen(cyan, 5f))
                            g.DrawPath(borderPen, clip);
                    }

                    // Right Track Info using Win32 Uniscribe (TextRenderer) for full Khmer / Unicode shaping
                    int tx = 446;
                    int tw = 1420;
                    string primaryFont = (ContainsKhmer(title) || ContainsKhmer(artist) || ContainsKhmer(album))
                        ? "Khmer UI" : "Segoe UI";
                    TextFormatFlags tff = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

                    using (var fTag = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var fTitle = new Font(primaryFont, 50f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var fArtist = new Font(primaryFont, 30f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var fAlbum = new Font(primaryFont, 21f, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        TextRenderer.DrawText(g, media.IsPlaying ? "NOW PLAYING" : "PAUSED", fTag, new Rectangle(tx, 46, tw, 28), cyan, tff);
                        TextRenderer.DrawText(g, string.IsNullOrEmpty(title) ? "NOW PLAYING" : title, fTitle, new Rectangle(tx, 78, tw, 84), ice, tff);
                        TextRenderer.DrawText(g, artist, fArtist, new Rectangle(tx, 162, tw, 52), cyan, tff);
                        if (!string.IsNullOrEmpty(album) && !string.Equals(album, title, StringComparison.OrdinalIgnoreCase))
                            TextRenderer.DrawText(g, album, fAlbum, new Rectangle(tx, 216, tw, 36), dim, tff);
                    }

                    // Cyan Progress Bar + Timestamps (leaving bottom 155px open for trofeo_lcd_core's 25 FPS Cava spectrum)
                    int barY = 272;
                    int barH = 8;
                    double frac = media.Duration.TotalSeconds > 1.0
                        ? Math.Max(0.0, Math.Min(1.0, media.Position.TotalSeconds / media.Duration.TotalSeconds))
                        : 0.0;
                    using (var trackBr = new SolidBrush(Color.FromArgb(45, cyan)))
                    using (var fillBr = new SolidBrush(cyan))
                    using (var knobBr = new SolidBrush(ice))
                    using (var fTime = new Font("Consolas", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        g.FillRectangle(trackBr, tx, barY, tw, barH);
                        int fw = (int)(tw * frac);
                        if (fw > 0)
                        {
                            g.FillRectangle(fillBr, tx, barY, fw, barH);
                            g.FillEllipse(knobBr, tx + fw - 7, barY - 4, 15, 15);
                        }
                        if (media.Duration.TotalSeconds > 1.0)
                        {
                            TextRenderer.DrawText(g, FormatMmSs(media.Position), fTime, new Rectangle(tx, barY + 14, 180, 26), ice,
                                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                            TextRenderer.DrawText(g, FormatMmSs(media.Duration), fTime, new Rectangle(tx + tw - 180, barY + 14, 180, 26), ice,
                                TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        }
                    }

                    bmp.Save(imgPath, ImageFormat.Png);
                }

                string layoutBody =
                    "# Auto-generated Unicode/Khmer Music Layout (1920x462)\r\n" +
                    "image x=0 y=0 w=100 h=100 file=\"layouts/" + imgName + "\"\r\n" +
                    "text x=98 y=94 anchor=br size=2.1 color=#9ED8E6 text=\"CPU {cpu} {cpu_temp}   GPU {gpu} {gpu_temp}   RAM {ram}\"\r\n";
                File.WriteAllText(layoutPath, layoutBody, Encoding.ASCII);
            }
            catch { }
        }

        static string FormatMmSs(TimeSpan t)
        {
            if (t.TotalHours >= 1.0)
                return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        static GraphicsPath MakeRoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var gp = new GraphicsPath();
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    static class SapphireGpuRgbController
    {
        const int Addr7 = 0x28;
        const int I2cLine = 1;
        const int ActionWrite = 2;
        const int ActionReadRepStart = 3;

        const int RegExtCtrl = 0x0F;
        const int RegMode = 0x10;
        const int RegRed = 0x1A;
        const int RegGreen = 0x1B;
        const int RegBlue = 0x1C;
        const int RegBrightness = 0x3E;

        const byte ModeCustomStatic = 0x06;
        const byte ModeOff = 0x07;

        public delegate IntPtr AdlAlloc(int size);
        static readonly AdlAlloc AllocDel = AllocMem;
        static IntPtr AllocMem(int size) { return Marshal.AllocHGlobal(size); }

        [StructLayout(LayoutKind.Sequential)]
        struct ADLI2C
        {
            public int iSize;
            public int iLine;
            public int iAddress;
            public int iOffset;
            public int iAction;
            public int iSpeed;
            public int iDataSize;
            public IntPtr pcData;
        }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Create(AdlAlloc cb, int enumConnected, out IntPtr ctx);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int num);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_Active_Get(IntPtr ctx, int adapterIndex, out int status);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Display_WriteAndReadI2C(IntPtr ctx, int adapterIndex, ref ADLI2C i2c);

        static readonly object RgbLock = new object();
        static bool lastAppliedOff;
        static bool hasSavedState;
        static byte savedMode = ModeCustomStatic;
        static byte savedExt = 0;
        static byte savedR = 0;
        static byte savedG = 31;
        static byte savedB = 255;
        static byte savedBri = 10;

        static bool ReadReg(IntPtr ctx, int adapter, int reg, out byte val)
        {
            val = 0;
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteByte(buf, 0, 0);
                var i2c = new ADLI2C
                {
                    iSize = Marshal.SizeOf(typeof(ADLI2C)),
                    iLine = I2cLine,
                    iAddress = Addr7 << 1,
                    iOffset = reg,
                    iAction = ActionReadRepStart,
                    iSpeed = 100,
                    iDataSize = 1,
                    pcData = buf
                };
                if (ADL2_Display_WriteAndReadI2C(ctx, adapter, ref i2c) == 0)
                {
                    val = Marshal.ReadByte(buf, 0);
                    return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static bool WriteReg(IntPtr ctx, int adapter, int reg, byte val)
        {
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteByte(buf, 0, val);
                var i2c = new ADLI2C
                {
                    iSize = Marshal.SizeOf(typeof(ADLI2C)),
                    iLine = I2cLine,
                    iAddress = Addr7 << 1,
                    iOffset = reg,
                    iAction = ActionWrite,
                    iSpeed = 100,
                    iDataSize = 1,
                    pcData = buf
                };
                return ADL2_Display_WriteAndReadI2C(ctx, adapter, ref i2c) == 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static int FindSapphireAdapter(IntPtr ctx)
        {
            int n;
            if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out n) != 0) return -1;
            for (int a = 0; a < n; a++)
            {
                int act = 0;
                if (ADL2_Adapter_Active_Get(ctx, a, out act) != 0 || act == 0) continue;
                byte probe;
                if (ReadReg(ctx, a, RegMode, out probe)) return a;
            }
            return -1;
        }

        public static void SetOff(bool off, Action<string> logFn)
        {
            lock (RgbLock)
            {
                if (off == lastAppliedOff) return;
                IntPtr ctx = IntPtr.Zero;
                try
                {
                    if (ADL2_Main_Control_Create(AllocDel, 1, out ctx) != 0) return;
                    int adapter = FindSapphireAdapter(ctx);
                    if (adapter < 0) return;

                    if (off)
                    {
                        byte curMode, curExt, curR, curG, curB, curBri;
                        if (ReadReg(ctx, adapter, RegMode, out curMode))
                        {
                            ReadReg(ctx, adapter, RegExtCtrl, out curExt);
                            ReadReg(ctx, adapter, RegRed, out curR);
                            ReadReg(ctx, adapter, RegGreen, out curG);
                            ReadReg(ctx, adapter, RegBlue, out curB);
                            ReadReg(ctx, adapter, RegBrightness, out curBri);
                            if (curMode != ModeOff)
                            {
                                savedMode = curMode;
                                savedExt = curExt;
                                savedR = curR;
                                savedG = curG;
                                savedB = curB;
                                savedBri = curBri;
                                hasSavedState = true;
                            }
                            if (curExt != 0) WriteReg(ctx, adapter, RegExtCtrl, 0);
                            if (WriteReg(ctx, adapter, RegMode, ModeOff))
                            {
                                lastAppliedOff = true;
                                if (logFn != null)
                                    logFn(string.Format("Sapphire GPU RGB turned OFF (saved mode=0x{0:X2} rgb={1},{2},{3} bri={4})",
                                        savedMode, savedR, savedG, savedB, savedBri));
                            }
                        }
                    }
                    else
                    {
                        byte targetMode = (hasSavedState && savedMode != ModeOff) ? savedMode : ModeCustomStatic;
                        WriteReg(ctx, adapter, RegExtCtrl, savedExt);
                        WriteReg(ctx, adapter, RegRed, savedR);
                        WriteReg(ctx, adapter, RegGreen, savedG);
                        WriteReg(ctx, adapter, RegBlue, savedB);
                        WriteReg(ctx, adapter, RegBrightness, savedBri);
                        if (WriteReg(ctx, adapter, RegMode, targetMode))
                        {
                            lastAppliedOff = false;
                            if (logFn != null)
                                logFn(string.Format("Sapphire GPU RGB restored static (mode=0x{0:X2} rgb={1},{2},{3} bri={4})",
                                    targetMode, savedR, savedG, savedB, savedBri));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logFn != null) logFn("Sapphire GPU RGB error: " + ex.Message);
                }
                finally
                {
                    if (ctx != IntPtr.Zero) { try { ADL2_Main_Control_Destroy(ctx); } catch { } }
                }
            }
        }
    }

    sealed class TrofeoCoreManager : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);
        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref USB_CYCLE_PORT_PARAMS lpInBuffer, uint nInBufferSize, ref USB_CYCLE_PORT_PARAMS lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);
        [DllImport("cfgmgr32.dll")]
        static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern uint CM_Get_Device_IDW(uint dnDevInst, StringBuilder Buffer, int BufferLen, uint ulFlags);

        [StructLayout(LayoutKind.Sequential)]
        struct USB_CYCLE_PORT_PARAMS
        {
            public uint ConnectionIndex;
            public uint StatusReturned;
        }

        readonly string[] extraArgs;
        readonly Func<int> getIdleFps;
        readonly Func<int> getActiveFps;
        readonly AutoResetEvent restartSignal = new AutoResetEvent(false);
        readonly object procLock = new object();
        readonly object logLock = new object();
        readonly IntPtr job;
        Thread supThread;
        Process currentProc;
        volatile bool stopping;
        volatile bool manualRestartRequested;
        public volatile int CorePid;
        public volatile bool CoreRunning;
        public DateTime CoreStartedUtc = DateTime.MinValue;
        public string LastNote = "Starting...";

        public TrofeoCoreManager(string[] passthroughArgs, Func<int> idleFpsFn, Func<int> activeFpsFn)
        {
            extraArgs = passthroughArgs ?? new string[0];
            getIdleFps = idleFpsFn;
            getActiveFps = activeFpsFn;
            job = CreateKillOnCloseJob();
        }

        static IntPtr CreateKillOnCloseJob()
        {
            IntPtr h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr info = Marshal.AllocHGlobal(144);
            for (int i = 0; i < 144; i++) Marshal.WriteByte(info, i, 0);
            Marshal.WriteInt32(info, 16, 0x00002000);
            SetInformationJobObject(h, 9, info, 144);
            Marshal.FreeHGlobal(info);
            return h;
        }

        public void Log(string msg)
        {
            try
            {
                lock (logLock)
                {
                    string lp = AppPaths.LauncherLog;
                    if (File.Exists(lp) && new FileInfo(lp).Length > 65536)
                    {
                        byte[] all = File.ReadAllBytes(lp);
                        int start = Math.Max(0, all.Length - 32768);
                        using (var fs = new FileStream(lp, FileMode.Create, FileAccess.Write, FileShare.Read))
                            fs.Write(all, start, all.Length - start);
                    }
                    File.AppendAllText(lp, string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, msg, Environment.NewLine), Encoding.UTF8);
                }
            }
            catch { }
        }

        static double UptimeSeconds()
        {
            try
            {
                if (Stopwatch.IsHighResolution && Stopwatch.Frequency > 0)
                    return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            }
            catch { }
            return (Environment.TickCount & 0x7FFFFFFF) / 1000.0;
        }

        public void Start()
        {
            if (supThread != null) return;
            supThread = new Thread(Run) { IsBackground = true, Name = "TrofeoCoreSupervisor" };
            supThread.Start();
        }

        public void RestartNow()
        {
            manualRestartRequested = true;
            lock (procLock)
            {
                if (currentProc != null)
                {
                    try { if (!currentProc.HasExited) currentProc.Kill(); } catch { }
                }
            }
            restartSignal.Set();
        }

        void Run()
        {
            string coreExe = AppPaths.TrofeoCoreExe;
            if (!File.Exists(coreExe)) { LastNote = "trofeo_lcd_core.exe not found"; return; }

            double up = UptimeSeconds();
            if (up < 25.0)
            {
                int waitMs = Math.Min(12000, (int)((25.0 - up) * 1000.0));
                if (waitMs > 500)
                {
                    Log("Early boot detected (" + up.ToString("0.0") + "s); delaying trofeo_lcd_core by " + (waitMs / 1000.0).ToString("0.0") + "s");
                    LastNote = "Early boot settle...";
                    restartSignal.WaitOne(waitMs);
                }
            }

            int[] backoffs = { 3000, 5000, 8000, 12000, 15000 };
            int failCount = 0;

            while (!stopping)
            {
                manualRestartRequested = false;
                var tail = new List<string>();
                DateTime started = DateTime.UtcNow;
                int exitCode = -1;
                string argsStr = BuildArgs();

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = coreExe,
                        Arguments = argsStr,
                        WorkingDirectory = AppPaths.TrofeoDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var p = new Process { StartInfo = psi })
                    {
                        p.OutputDataReceived += (s, e) => AppendTail(tail, "[out] ", e.Data);
                        p.ErrorDataReceived += (s, e) => AppendTail(tail, "[err] ", e.Data);
                        if (p.Start())
                        {
                            lock (procLock) currentProc = p;
                            CorePid = p.Id;
                            CoreStartedUtc = DateTime.Now;
                            CoreRunning = true;
                            LastNote = "Running (PID " + p.Id + ")";
                            if (job != IntPtr.Zero) { try { AssignProcessToJobObject(job, p.Handle); } catch { } }
                            p.BeginOutputReadLine();
                            p.BeginErrorReadLine();
                            p.WaitForExit();
                            exitCode = p.ExitCode;
                            lock (procLock) currentProc = null;
                            CoreRunning = false;
                            CorePid = 0;
                        }
                    }
                }
                catch (Exception ex) { Log("trofeo_lcd_core start error: " + ex.Message); }

                if (stopping) break;
                if (manualRestartRequested) { failCount = 0; continue; }

                double lived = (DateTime.UtcNow - started).TotalSeconds;
                if (lived >= 30.0) failCount = 0;
                int delayMs = backoffs[Math.Min(failCount, backoffs.Length - 1)];
                failCount++;
                string msgTail;
                lock (tail) msgTail = string.Join(" | ", tail.ToArray());
                LastNote = "Retry #" + failCount + " (code " + exitCode + ")";
                Log("trofeo_lcd_core exited code=" + exitCode + " after " + lived.ToString("0.0") + "s. Retry #" + failCount + ". " + msgTail);
                if (!stopping && (msgTail.IndexOf("Operation timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (failCount >= 2 && msgTail.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    TryResetTrofeoUsbDevice();
                }
                restartSignal.WaitOne(delayMs);
            }
        }

        DateTime lastUsbResetUtc = DateTime.MinValue;

        void TryResetTrofeoUsbDevice()
        {
            if ((DateTime.UtcNow - lastUsbResetUtc).TotalSeconds < 12.0) return;
            lastUsbResetUtc = DateTime.UtcNow;
            bool cycled = false;
            try
            {
                using (var rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_0416&PID_5408"))
                {
                    if (rk == null) return;
                    foreach (string sub in rk.GetSubKeyNames())
                    {
                        string instId = @"USB\VID_0416&PID_5408\" + sub;
                        int port = 0;
                        using (var subKey = rk.OpenSubKey(sub))
                        {
                            if (subKey != null)
                            {
                                object addrObj = subKey.GetValue("Address");
                                if (addrObj is int) port = (int)addrObj;
                                else if (addrObj != null) int.TryParse(addrObj.ToString(), out port);
                            }
                        }

                        uint devNode, parentNode;
                        if (port > 0 && CM_Locate_DevNodeW(out devNode, instId, 0) == 0 && CM_Get_Parent(out parentNode, devNode, 0) == 0)
                        {
                            var sbParent = new StringBuilder(512);
                            if (CM_Get_Device_IDW(parentNode, sbParent, sbParent.Capacity, 0) == 0)
                            {
                                string hubSym = @"\\?\" + sbParent.ToString().Replace('\\', '#') + "#{f18a0e88-c30c-11d0-8815-00a0c906bed8}";
                                IntPtr hHub = CreateFileW(hubSym, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
                                if (hHub != IntPtr.Zero && hHub != new IntPtr(-1))
                                {
                                    try
                                    {
                                        var cp = new USB_CYCLE_PORT_PARAMS { ConnectionIndex = (uint)port, StatusReturned = 0 };
                                        uint br;
                                        if (DeviceIoControl(hHub, 0x00220444u, ref cp, 8u, ref cp, 8u, out br, IntPtr.Zero))
                                        {
                                            cycled = true;
                                            Log("Cycled Thermalright USB hub port " + port + " (" + instId + ")");
                                        }
                                        else
                                        {
                                            Log("USB port cycle DeviceIoControl err=" + Marshal.GetLastWin32Error());
                                        }
                                    }
                                    finally { CloseHandle(hHub); }
                                }
                            }
                        }

                        if (!cycled)
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe"),
                                Arguments = "/restart-device \"" + instId + "\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                            using (var p = Process.Start(psi))
                            {
                                if (p != null)
                                {
                                    p.WaitForExit(5000);
                                    if (p.ExitCode == 0)
                                        Log("Reset stalled Thermalright USB endpoint via pnputil (" + instId + ")");
                                }
                            }
                        }
                    }
                }
                Thread.Sleep(cycled ? 2200 : 1500);
            }
            catch (Exception ex) { Log("USB auto-reset error: " + ex.Message); }
        }

        string BuildArgs()
        {
            var sb = new StringBuilder();
            bool hasIdle = false, hasActive = false;
            foreach (string a in extraArgs)
            {
                string low = a.ToLowerInvariant();
                if (low == "--hide-console") continue;
                if (low == "--idle-fps" || low.StartsWith("--idle-fps=")) hasIdle = true;
                if (low == "--active-fps" || low.StartsWith("--active-fps=")) hasActive = true;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append((a.Contains(" ") || a.Contains("\t")) ? "\"" + a.Replace("\"", "\\\"") + "\"" : a);
            }
            if (!hasIdle)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("--idle-fps ").Append(Math.Max(1, getIdleFps()));
            }
            if (!hasActive)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("--active-fps ").Append(Math.Max(5, getActiveFps()));
            }
            return sb.ToString();
        }

        static void AppendTail(List<string> tail, string prefix, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (tail)
            {
                tail.Add(prefix + line);
                if (tail.Count > 25) tail.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            stopping = true;
            restartSignal.Set();
            lock (procLock)
            {
                if (currentProc != null)
                {
                    try { if (!currentProc.HasExited) currentProc.Kill(); } catch { }
                }
            }
        }
    }

    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();

        static void KillProcessByName(string name)
        {
            int selfPid = Process.GetCurrentProcess().Id;
            try
            {
                foreach (Process old in Process.GetProcessesByName(name))
                {
                    using (old)
                    {
                        if (old.Id == selfPid) continue;
                        try { old.Kill(); old.WaitForExit(1500); } catch { }
                    }
                }
            }
            catch { }
        }

        static int ForwardToTrofeoCore(string[] args)
        {
            if (!File.Exists(AppPaths.TrofeoCoreExe)) return 1;
            var sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append((a.Contains(" ") || a.Contains("\t")) ? "\"" + a.Replace("\"", "\\\"") + "\"" : a);
            }
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.TrofeoCoreExe,
                Arguments = sb.ToString(),
                WorkingDirectory = AppPaths.TrofeoDir,
                UseShellExecute = false
            };
            using (Process p = Process.Start(psi))
            {
                if (p == null) return 1;
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            string previewPng = null;
            string previewState = "idle";
            bool diag = false;
            bool trofeoPassthrough = false;
            bool openUi = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--lovingcool-preview-png" && i + 1 < args.Length) previewPng = args[++i];
                else if (a == "--preview-state" && i + 1 < args.Length) previewState = args[++i].ToLowerInvariant();
                else if (a == "--lovingcool-diag" || a == "--dual-diag") diag = true;
                else if (a == "--ui") openUi = true;
                else if (a == "--preview-png" || a == "--diag" || a == "--fps-diag" ||
                         a == "--lhm-list" || a == "--lcd-test" || a == "--layout" ||
                         a == "-h" || a == "--help" || a == "-v" || a == "--version")
                {
                    trofeoPassthrough = true;
                }
            }

            if (trofeoPassthrough) return ForwardToTrofeoCore(args);

            Config cfg = Config.Load(AppPaths.LovingcoolConf);

            if (diag)
            {
                var diagThread = new Thread(() => RunDiag(cfg));
                diagThread.SetApartmentState(ApartmentState.MTA);
                diagThread.Start();
                diagThread.Join();
                return 0;
            }

            if (previewPng != null)
            {
                int exitCode = 0;
                var mta = new Thread(() =>
                {
                    try
                    {
                        using (var sensors = new HardwareSensors())
                        using (var media = new MediaReader())
                        using (var audio = new WasapiCava(64))
                        {
                            sensors.Update();
                            media.Update();
                            Thread.Sleep(150);
                            sensors.Update();
                            audio.Update();

                            ScreenMode mode = ScreenMode.Dashboard;
                            if (previewState == "music") mode = ScreenMode.Music;
                            else if (previewState == "gaming") mode = ScreenMode.Gaming;

                            // If preview runs non-elevated and PawnIO is restricted, read latest live readings from running elevated service
                            if (sensors.CpuTemp <= 0f)
                            {
                                try
                                {
                                    string stPath = AppPaths.LovingcoolStatus;
                                    if (!File.Exists(stPath)) stPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lovingcool_status.txt");
                                    if (File.Exists(stPath))
                                    {
                                        foreach (string ln in File.ReadAllLines(stPath))
                                        {
                                            string[] kv = ln.Split('=');
                                            if (kv.Length == 2)
                                            {
                                                float f;
                                                if (kv[0] == "cpu_temp" && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) sensors.CpuTemp = f;
                                                else if (kv[0] == "cpu_power" && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) sensors.CpuPower = f;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }

                            if (mode == ScreenMode.Music)
                            {
                                if (string.IsNullOrEmpty(media.Title))
                                {
                                    media.Title = "NEON HORIZON";
                                    media.Artist = "THE MIDNIGHT DRIVERS";
                                    media.Album = "ENDLESS SUMMER";
                                    media.Position = TimeSpan.FromSeconds(83);
                                    media.Duration = TimeSpan.FromSeconds(240);
                                    media.IsPlaying = true;
                                }
                                if (audio.PeakLevel < 0.02f)
                                {
                                    audio.InjectDemoSpectrum();
                                }
                            }
                            else if (mode == ScreenMode.Gaming)
                            {
                                sensors.InjectDemoGaming();
                            }

                            using (Bitmap bmp = Renderer.RenderFrame(480, 480, cfg, sensors, media, audio, mode))
                            {
                                bmp.Save(previewPng, ImageFormat.Png);
                            }
                        }
                    }
                    catch { exitCode = 1; }
                });
                mta.SetApartmentState(ApartmentState.MTA);
                mta.Start();
                mta.Join();
                return exitCode;
            }

            bool createdNew = false;
            Mutex mutex = null;
            try
            {
                var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                var mSec = new MutexSecurity();
                mSec.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
                mutex = new Mutex(true, "Local\\CyanDualDisplay_Mutex", out createdNew, mSec);
            }
            catch (UnauthorizedAccessException)
            {
                createdNew = false;
            }
            try
            {
                if (!createdNew)
                {
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting("Local\\CyanDualDisplay_ShowUi", EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                            ev.Set();
                    }
                    catch { }
                    return 0;
                }
                FreeConsole();
                KillProcessByName("lovingcool_lcd");
                KillProcessByName("trofeo_lcd_core");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, exArgs) =>
                {
                    try { File.AppendAllText(AppPaths.LauncherLog, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] UI ThreadException: " + exArgs.Exception + Environment.NewLine); } catch { }
                };
                using (var app = new TrayApp(cfg, args, openUi))
                {
                    Application.Run();
                }
            }
            finally
            {
                if (mutex != null) mutex.Dispose();
            }
            return 0;
        }

        static void RunDiag(Config cfg)
        {
            Console.WriteLine("=== KuroHUD Unified Diagnostics ===");
            Console.WriteLine("Thermalright Dir: " + AppPaths.TrofeoDir);
            Console.WriteLine("Thermalright Config: layout=" + TrofeoConfSync.GetActiveValue("layout", "default") +
                " | brightness=" + TrofeoConfSync.GetActiveValue("brightness", "100") +
                " | profile=" + TrofeoConfSync.GetActiveProfile());
            Console.WriteLine("Lovingcool Config: " + cfg.ConfPath + " | mode=" + cfg.Mode + " | idle_fps=" + cfg.IdleFps + " | active_fps=" + cfg.ActiveFps + " | brightness=" + cfg.Brightness + " | sleep_sync=" + cfg.SyncMonitorSleep);
            string port = SerialDriver.FindPort(cfg.Port);
            Console.WriteLine("Detected Lovingcool Serial Port: " + (port ?? "NONE"));
            using (var s = new HardwareSensors())
            {
                s.Update();
                Thread.Sleep(250);
                s.Update();
                Console.WriteLine(string.Format("CPU: {0} | Load={1:0}% | Temp={2:0}C | Freq={3:0.00}GHz | PawnIO={4}", s.CpuName, s.CpuLoad, s.CpuTemp, s.CpuGhz, s.PawnStatus));
                Console.WriteLine(string.Format("GPU: {0} | Load={1:0}% | Wallpaper={2:0}% | Effective={3:0}% | Temp={4:0}C | Hotspot={5:0}C | Power={6:0}W | FPS={7:0}",
                    s.GpuName, s.GpuLoad, s.WallpaperGpuLoad, s.EffectiveGameGpuLoad, s.GpuTemp, s.GpuHotspot, s.GpuPower, s.GameFps));
                Console.WriteLine(string.Format("RAM: {0:0.0} / {1:0.0} GB ({2:0}%)", s.RamUsedGb, s.RamTotalGb, s.RamPercent));
                Console.WriteLine(string.Format("Foreground Game: IsGame={0} | Name='{1}'", s.IsForegroundGame, s.GameTitle));
            }
            using (var m = new MediaReader())
            {
                m.Update();
                Console.WriteLine(string.Format("Media: Playing={0} | '{1}' - '{2}' ({3}) | Cover={4} | Err={5}",
                    m.IsPlaying, m.Artist, m.Title, m.Album,
                    m.CoverArt != null ? (m.CoverArt.Width + "x" + m.CoverArt.Height) : "null",
                    m.LastThumbError ?? "none"));
            }
        }
    }

    class Config
    {
        public string Port = "auto";
        public string Mode = "auto"; // auto | dashboard | music | gaming | off
        public int Rotate = -1;      // -1 = auto from device angle, or 0, 90, 180, 270
        public int ActiveFps = 25;
        public int IdleFps = 15;
        public Color ThemeColor = Color.FromArgb(0, 255, 255); // #00FFFF
        public int Brightness = 100;
        public bool GamingScreen = true;
        public int GamingGpuPercent = 50;
        public int GamingDelaySec = 1;
        public int GamingHoldSec = 2;
        public string ClockFormat = "12h";
        public bool SyncMonitorSleep = true;
        public string ConfPath;

        public static Config Load(string path)
        {
            var c = new Config { ConfPath = path };
            if (!File.Exists(path))
            {
                c.Save();
                return c;
            }
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    int hash = v.IndexOf(" #");
                    if (hash >= 0) v = v.Substring(0, hash).Trim();

                    if (k == "port") c.Port = v;
                    else if (k == "mode") c.Mode = v.ToLowerInvariant();
                    else if (k == "rotate")
                    {
                        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) c.Rotate = -1;
                        else { int r; if (int.TryParse(v, out r)) c.Rotate = r; }
                    }
                    else if (k == "active_fps") { int f; if (int.TryParse(v, out f)) c.ActiveFps = Math.Max(5, Math.Min(30, f)); }
                    else if (k == "idle_fps") { int f; if (int.TryParse(v, out f)) c.IdleFps = Math.Max(1, Math.Min(30, f)); }
                    else if (k == "brightness") { int b; if (int.TryParse(v, out b)) c.Brightness = Math.Max(10, Math.Min(100, b)); }
                    else if (k == "gaming_screen") c.GamingScreen = !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0";
                    else if (k == "gaming_gpu_percent") { int g; if (int.TryParse(v, out g)) c.GamingGpuPercent = Math.Max(5, Math.Min(95, g)); }
                    else if (k == "gaming_delay") { int d; if (int.TryParse(v, out d)) c.GamingDelaySec = Math.Max(0, Math.Min(60, d)); }
                    else if (k == "gaming_hold") { int h; if (int.TryParse(v, out h)) c.GamingHoldSec = Math.Max(0, Math.Min(120, h)); }
                    else if (k == "clock_format") c.ClockFormat = v.ToLowerInvariant();
                    else if (k == "sync_monitor_sleep") c.SyncMonitorSleep = !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0";
                    else if (k == "color")
                    {
                        string hex = v.TrimStart('#');
                        if (hex.Length == 6)
                        {
                            int rgb;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                                c.ThemeColor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                        }
                    }
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Lovingcool AIO 480x480 Cyan Display Configuration");
                sb.AppendLine("# Changes to this file are reloaded automatically!");
                sb.AppendLine();
                sb.AppendLine("port = " + Port + "               # auto, or COM5");
                sb.AppendLine("mode = " + Mode + "               # auto | dashboard | music | gaming | off");
                sb.AppendLine("rotate = " + (Rotate < 0 ? "auto" : Rotate.ToString()) + "             # auto | 0 | 90 | 180 | 270");
                sb.AppendLine(string.Format("color = #{0:X2}{1:X2}{2:X2}          # Pure Cyan #00FFFF", ThemeColor.R, ThemeColor.G, ThemeColor.B));
                sb.AppendLine("active_fps = " + ActiveFps + "           # FPS during music / gaming");
                sb.AppendLine("idle_fps = " + IdleFps + "              # FPS on system dashboard");
                sb.AppendLine("brightness = " + Brightness + "          # 10 - 100");
                sb.AppendLine("clock_format = " + ClockFormat + "        # 12h | 24h");
                sb.AppendLine("sync_monitor_sleep = " + (SyncMonitorSleep ? "true" : "false") + " # Turn off both screens when main monitor sleeps");
                sb.AppendLine();
                sb.AppendLine("# --- Automatic Gaming Screen ---");
                sb.AppendLine("gaming_screen = " + (GamingScreen ? "true" : "false") + "      # Auto-switch to Gaming Telemetry when playing a game");
                sb.AppendLine("gaming_gpu_percent = " + GamingGpuPercent + "   # GPU load (%) threshold to trigger gaming mode");
                sb.AppendLine("gaming_delay = " + GamingDelaySec + "          # Seconds GPU stays above threshold before switching");
                sb.AppendLine("gaming_hold = " + GamingHoldSec + "           # Seconds gaming screen stays after GPU load drops");
                File.WriteAllText(ConfPath, sb.ToString());
            }
            catch { }
        }
    }

    sealed class DisplayPowerWatcher : NativeWindow, IDisposable
    {
        static readonly Guid GuidConsoleDisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        static readonly Guid GuidSessionDisplayStatus = new Guid("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
        static readonly Guid GuidMonitorPowerOn = new Guid("02731015-4510-4526-99e6-e5a17ebd1aea");
        const int WmPowerBroadcast = 0x0218;
        const int PbtApmSuspend = 0x0004;
        const int PbtApmResumeAutomatic = 0x0012;
        const int PbtPowerSettingChange = 0x8013;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

        readonly Action<bool> onStateChanged;
        IntPtr hConsole, hSession, hMonitor;
        bool? lastOff;

        public DisplayPowerWatcher(Action<bool> callback)
        {
            onStateChanged = callback;
            CreateHandle(new CreateParams());
            Guid g1 = GuidConsoleDisplayState, g2 = GuidSessionDisplayStatus, g3 = GuidMonitorPowerOn;
            hConsole = RegisterPowerSettingNotification(Handle, ref g1, 0);
            hSession = RegisterPowerSettingNotification(Handle, ref g2, 0);
            hMonitor = RegisterPowerSettingNotification(Handle, ref g3, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmPowerBroadcast)
            {
                int wp = m.WParam.ToInt32();
                if (wp == PbtApmSuspend)
                {
                    ReportState(true);
                }
                else if (wp == PbtApmResumeAutomatic)
                {
                    ReportState(false);
                }
                else if (wp == PbtPowerSettingChange && m.LParam != IntPtr.Zero)
                {
                    byte[] gb = new byte[16];
                    Marshal.Copy(m.LParam, gb, 0, 16);
                    var g = new Guid(gb);
                    int len = Marshal.ReadInt32(m.LParam, 16);
                    if (len >= 4 && (g == GuidConsoleDisplayState || g == GuidSessionDisplayStatus || g == GuidMonitorPowerOn))
                    {
                        int val = Marshal.ReadInt32(m.LParam, 20);
                        ReportState(val == 0);
                    }
                }
            }
            base.WndProc(ref m);
        }

        void ReportState(bool off)
        {
            if (lastOff.HasValue && lastOff.Value == off) return;
            lastOff = off;
            if (onStateChanged != null) onStateChanged(off);
        }

        public void Dispose()
        {
            try
            {
                if (hConsole != IntPtr.Zero) { UnregisterPowerSettingNotification(hConsole); hConsole = IntPtr.Zero; }
                if (hSession != IntPtr.Zero) { UnregisterPowerSettingNotification(hSession); hSession = IntPtr.Zero; }
                if (hMonitor != IntPtr.Zero) { UnregisterPowerSettingNotification(hMonitor); hMonitor = IntPtr.Zero; }
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
            catch { }
        }
    }

    sealed class TelemetrySnapshot
    {
        public string MasterStatus = "Active";
        public string LovingcoolStatus = "Starting...";
        public string CpuName = "AMD RYZEN 5 5600";
        public float CpuLoad;
        public float CpuTemp;
        public float CpuPower;
        public float CpuGhz;
        public string GpuName = "RADEON RX 6700 XT";
        public float GpuLoad;
        public float WallpaperGpuLoad;
        public float EffectiveGpuLoad;
        public float GpuTemp;
        public float GpuHotspot;
        public float GpuPower;
        public float RamUsedGb;
        public float RamTotalGb;
        public float RamPercent;
        public float GameFps;
        public string GameTitle = "ACTIVE GAME";
        public bool IsForegroundGame;
        public string MediaSummary = "None";
        public string PawnStatus = "";
        public DateTime UpdatedAt = DateTime.MinValue;
    }

    class TrayApp : IDisposable
    {
        public readonly Config cfg;
        readonly NotifyIcon tray;
        readonly Icon trayIcon;
        readonly DisplayPowerWatcher powerWatcher;
        readonly TrofeoCoreManager trofeoCore;
        readonly Thread worker;
        readonly AutoResetEvent wakeEvent = new AutoResetEvent(false);
        readonly EventWaitHandle showUiEvent;
        readonly System.Windows.Forms.Timer uiTimer;
        readonly object snapLock = new object();
        TelemetrySnapshot latestSnap = new TelemetrySnapshot();
        ControlCenterForm controlForm;

        volatile bool physicalMonitorOff;
        volatile bool manualScreenOff;
        volatile bool autoGamingActive;
        volatile bool unicodeMusicActive;
        volatile bool stopping;
        volatile string statusText = "Starting...";
        DateTime lastConfCheck = DateTime.UtcNow;
        DateTime lastConfWrite;

        public bool IsManualScreenOff { get { return manualScreenOff; } }
        public bool IsEffectiveOff { get { return manualScreenOff || (physicalMonitorOff && cfg.SyncMonitorSleep); } }
        public bool IsPhysicalMonitorOff { get { return physicalMonitorOff; } }
        public TrofeoCoreManager Core { get { return trofeoCore; } }

        public TrayApp(Config cfg, string[] passthroughArgs, bool openUiOnStart)
        {
            this.cfg = cfg;
            if (this.cfg.Mode == "off") this.cfg.Mode = "auto";
            this.cfg.Save();
            if (File.Exists(cfg.ConfPath)) lastConfWrite = File.GetLastWriteTimeUtc(cfg.ConfPath);
            TrofeoConfSync.EnsureProfiles();
            // Let unified CyanDualDisplay control Gaming Mode for both screens (prevents trofeo_lcd_core false triggers)
            TrofeoConfSync.SetActiveValue("gaming_screen", "false");

            bool createdEv;
            var evSec = new EventWaitHandleSecurity();
            evSec.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                AccessControlType.Allow));
            showUiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CyanDualDisplay_ShowUi", out createdEv, evSec);

            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new CyanDarkColorTable());
            menu.BackColor = Color.FromArgb(6, 18, 26);
            menu.ForeColor = Color.FromArgb(220, 250, 255);

            var openUiItem = new ToolStripMenuItem("Open KuroHUD Control Center...") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            openUiItem.Click += (s, e) => ShowControlCenter();
            var masterItem = new ToolStripMenuItem("Displays: Initializing...") { Enabled = false };
            var trofeoItem = new ToolStripMenuItem("Thermalright: Starting...") { Enabled = false };
            var lcItem = new ToolStripMenuItem("Lovingcool: Starting...") { Enabled = false };
            menu.Items.Add(openUiItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(masterItem);
            menu.Items.Add(trofeoItem);
            menu.Items.Add(lcItem);
            menu.Items.Add(new ToolStripSeparator());

            var modeMenu = new ToolStripMenuItem("Master Mode (Both Screens)");
            var mAuto = new ToolStripMenuItem("Auto-Switch (Dashboard / Music / Gaming)", null, (s, e) => SetMasterMode("auto"));
            var mDash = new ToolStripMenuItem("System Dashboard Only", null, (s, e) => SetMasterMode("dashboard"));
            var mMusic = new ToolStripMenuItem("Music + Visualizer Only", null, (s, e) => SetMasterMode("music"));
            var mGame = new ToolStripMenuItem("Gaming Telemetry Only", null, (s, e) => SetMasterMode("gaming"));
            var mOff = new ToolStripMenuItem("Turn Off Both Screens", null, (s, e) => SetManualScreenOff(!manualScreenOff));
            modeMenu.DropDownItems.AddRange(new ToolStripItem[] { mAuto, mDash, mMusic, mGame, new ToolStripSeparator(), mOff });
            menu.Items.Add(modeMenu);

            var layoutMenu = new ToolStripMenuItem("Thermalright Layout");
            menu.Items.Add(layoutMenu);

            var rotMenu = new ToolStripMenuItem("Lovingcool Rotation");
            var rAuto = new ToolStripMenuItem("Auto (Device Default)", null, (s, e) => SetRotate(-1));
            var r0 = new ToolStripMenuItem("0\u00b0", null, (s, e) => SetRotate(0));
            var r90 = new ToolStripMenuItem("90\u00b0", null, (s, e) => SetRotate(90));
            var r180 = new ToolStripMenuItem("180\u00b0", null, (s, e) => SetRotate(180));
            var r270 = new ToolStripMenuItem("270\u00b0", null, (s, e) => SetRotate(270));
            rotMenu.DropDownItems.AddRange(new ToolStripItem[] { rAuto, r0, r90, r180, r270 });
            menu.Items.Add(rotMenu);

            var mSleepSync = new ToolStripMenuItem("Auto-Sleep With Main Monitor", null, (s, e) => SetSyncMonitorSleep(!cfg.SyncMonitorSleep));
            menu.Items.Add(mSleepSync);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Layout Studio (trofeo_config)", null, (s, e) => OpenTrofeoConfig());
            menu.Items.Add("Edit trofeo.conf (Thermalright)", null, (s, e) => OpenFileInNotepad(AppPaths.TrofeoConf));
            menu.Items.Add("Edit lovingcool.conf (Lovingcool)", null, (s, e) => OpenFileInNotepad(cfg.ConfPath));
            menu.Items.Add("Restart Thermalright Core", null, (s, e) => RestartTrofeoCore());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit All", null, (s, e) => ExitAll());

            menu.Opening += (s, e) =>
            {
                bool effOff = IsEffectiveOff;
                masterItem.Text = effOff
                    ? (manualScreenOff ? "State: Manual Screen Off (Both)" : "State: Monitor Sleep (Both Off)")
                    : string.Format("State: Active | Synced FPS {0}/{1} | Brightness {2}%", cfg.IdleFps, cfg.ActiveFps, cfg.Brightness);
                trofeoItem.Text = "Thermalright: " + (trofeoCore != null ? trofeoCore.LastNote : "N/A") +
                    " · " + TrofeoConfSync.GetActiveValue("layout", "default");
                lcItem.Text = "Lovingcool: " + statusText;

                mAuto.Checked = !manualScreenOff && cfg.Mode == "auto";
                mDash.Checked = !manualScreenOff && cfg.Mode == "dashboard";
                mMusic.Checked = !manualScreenOff && cfg.Mode == "music";
                mGame.Checked = !manualScreenOff && cfg.Mode == "gaming";
                mOff.Checked = manualScreenOff;
                mSleepSync.Checked = cfg.SyncMonitorSleep;

                rAuto.Checked = cfg.Rotate < 0;
                r0.Checked = cfg.Rotate == 0;
                r90.Checked = cfg.Rotate == 90;
                r180.Checked = cfg.Rotate == 180;
                r270.Checked = cfg.Rotate == 270;

                layoutMenu.DropDownItems.Clear();
                string curLayout = TrofeoConfSync.GetActiveValue("layout", "default");
                foreach (string lay in TrofeoConfSync.GetAvailableLayouts())
                {
                    string copy = lay;
                    var item = new ToolStripMenuItem(copy, null, (s2, e2) => SetThermalrightLayout(copy));
                    item.Checked = string.Equals(curLayout, copy, StringComparison.OrdinalIgnoreCase);
                    layoutMenu.DropDownItems.Add(item);
                }
            };

            trayIcon = CreateCyanIcon();
            tray = new NotifyIcon
            {
                Icon = trayIcon,
                Text = "KuroHUD",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.DoubleClick += (s, e) => ShowControlCenter();

            // Register monitor power notifications BEFORE starting trofeo_lcd_core so boot sleep never flashes
            powerWatcher = new DisplayPowerWatcher(off =>
            {
                physicalMonitorOff = off;
                UpdateSleepState();
                wakeEvent.Set();
            });

            DateTime settleEnd = DateTime.UtcNow.AddMilliseconds(220);
            while (DateTime.UtcNow < settleEnd)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            UpdateSleepState();

            trofeoCore = new TrofeoCoreManager(passthroughArgs, () => cfg.IdleFps, () => cfg.ActiveFps);
            trofeoCore.Log("KuroHUD controller started (mode=" + cfg.Mode + ", idle=" + cfg.IdleFps + ", active=" + cfg.ActiveFps + ", openUi=" + openUiOnStart + ")");
            trofeoCore.Start();

            worker = new Thread(RunLoop) { IsBackground = true, Name = "LovingcoolWorker" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            uiTimer = new System.Windows.Forms.Timer { Interval = 250 };
            uiTimer.Tick += (s, e) =>
            {
                if (stopping) return;
                try
                {
                    if (showUiEvent.WaitOne(0))
                    {
                        trofeoCore.Log("Received ShowUi wake signal");
                        ShowControlCenter();
                    }
                    string tip = IsEffectiveOff ? "KuroHUD (Off)" : "KuroHUD (" + cfg.Mode + ")";
                    if (tip.Length >= 63) tip = tip.Substring(0, 62);
                    if (tray.Text != tip) tray.Text = tip;
                }
                catch (Exception ex)
                {
                    if (trofeoCore != null) trofeoCore.Log("UI timer error: " + ex.Message);
                }
            };
            uiTimer.Start();

            if (openUiOnStart) ShowControlCenter();
        }

        public TelemetrySnapshot GetSnapshot()
        {
            lock (snapLock) return latestSnap;
        }

        public void ShowControlCenter()
        {
            if (stopping) return;
            try
            {
                if (controlForm == null || controlForm.IsDisposed)
                {
                    controlForm = new ControlCenterForm(this);
                }
                if (!controlForm.Visible) controlForm.Show();
                if (controlForm.WindowState == FormWindowState.Minimized) controlForm.WindowState = FormWindowState.Normal;
                controlForm.BringToFront();
                controlForm.Activate();
                controlForm.RefreshUi();
                if (trofeoCore != null) trofeoCore.Log("ControlCenterForm shown hwnd=0x" + controlForm.Handle.ToInt64().ToString("X") + " vis=" + controlForm.Visible);
            }
            catch (Exception ex)
            {
                if (trofeoCore != null) trofeoCore.Log("ShowControlCenter error: " + ex.ToString());
            }
        }

        public void UpdateSleepState()
        {
            bool effOff = IsEffectiveOff;
            string desired;
            if (effOff)
                desired = "monitor-off";
            else if (cfg.Mode == "dashboard")
                desired = "force-dashboard";
            else if (cfg.Mode == "gaming" || (cfg.Mode == "auto" && autoGamingActive))
                desired = "force-gaming";
            else if (unicodeMusicActive && (cfg.Mode == "auto" || cfg.Mode == "music"))
                desired = "unicode-music";
            else if (cfg.Mode == "music")
                desired = "force-music";
            else
                desired = "none";

            string cur = TrofeoConfSync.GetActiveProfile();
            if (!string.Equals(cur, desired, StringComparison.OrdinalIgnoreCase))
            {
                TrofeoConfSync.SetActiveProfile(desired == "none" ? null : desired);
            }

            SapphireGpuRgbController.SetOff(effOff, msg => { if (trofeoCore != null) trofeoCore.Log(msg); });
        }

        public void SetMasterMode(string m)
        {
            manualScreenOff = false;
            cfg.Mode = (m ?? "auto").ToLowerInvariant();
            if (cfg.Mode == "off") { SetManualScreenOff(true); return; }
            cfg.Save();
            lastConfWrite = File.Exists(cfg.ConfPath) ? File.GetLastWriteTimeUtc(cfg.ConfPath) : DateTime.MinValue;
            UpdateSleepState();
            wakeEvent.Set();
        }

        public void SetManualScreenOff(bool off)
        {
            manualScreenOff = off;
            UpdateSleepState();
            wakeEvent.Set();
        }

        public void SetThermalrightLayout(string layoutName)
        {
            if (!string.IsNullOrEmpty(layoutName))
                TrofeoConfSync.SetActiveValue("layout", layoutName);
        }

        public void SetMasterBrightness(int brightness)
        {
            int b = Math.Max(10, Math.Min(100, brightness));
            cfg.Brightness = b;
            cfg.Save();
            lastConfWrite = File.Exists(cfg.ConfPath) ? File.GetLastWriteTimeUtc(cfg.ConfPath) : DateTime.MinValue;
            TrofeoConfSync.SetActiveValue("brightness", b.ToString(CultureInfo.InvariantCulture));
            wakeEvent.Set();
        }

        public void SetSyncedFps(int idleFps, int activeFps)
        {
            cfg.IdleFps = Math.Max(1, Math.Min(30, idleFps));
            cfg.ActiveFps = Math.Max(5, Math.Min(30, activeFps));
            cfg.Save();
            lastConfWrite = File.Exists(cfg.ConfPath) ? File.GetLastWriteTimeUtc(cfg.ConfPath) : DateTime.MinValue;
            TrofeoConfSync.SetActiveValue("effect_fps", Math.Max(1, Math.Min(15, cfg.IdleFps)).ToString(CultureInfo.InvariantCulture));
            if (trofeoCore != null) trofeoCore.RestartNow();
            wakeEvent.Set();
        }

        public void SetRotate(int r)
        {
            cfg.Rotate = r;
            cfg.Save();
            lastConfWrite = File.Exists(cfg.ConfPath) ? File.GetLastWriteTimeUtc(cfg.ConfPath) : DateTime.MinValue;
            wakeEvent.Set();
        }

        public void SetSyncMonitorSleep(bool enabled)
        {
            cfg.SyncMonitorSleep = enabled;
            cfg.Save();
            lastConfWrite = File.Exists(cfg.ConfPath) ? File.GetLastWriteTimeUtc(cfg.ConfPath) : DateTime.MinValue;
            UpdateSleepState();
            wakeEvent.Set();
        }

        public void RestartTrofeoCore()
        {
            if (trofeoCore != null) trofeoCore.RestartNow();
        }

        public void OpenTrofeoConfig()
        {
            try
            {
                if (File.Exists(AppPaths.TrofeoConfigExe))
                    Process.Start(new ProcessStartInfo { FileName = AppPaths.TrofeoConfigExe, WorkingDirectory = AppPaths.TrofeoDir });
            }
            catch { }
        }

        static void OpenFileInNotepad(string path)
        {
            try { if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
        }

        public void ExitAll()
        {
            stopping = true;
            manualScreenOff = false;
            SapphireGpuRgbController.SetOff(false, msg => { if (trofeoCore != null) trofeoCore.Log(msg); });
            TrofeoConfSync.ClearManagedProfiles();
            wakeEvent.Set();
            if (uiTimer != null) uiTimer.Stop();
            if (controlForm != null && !controlForm.IsDisposed) controlForm.ForceClose();
            if (trofeoCore != null) trofeoCore.Dispose();
            if (tray != null) tray.Visible = false;
            Application.Exit();
        }

        static Icon CreateCyanIcon()
        {
            try
            {
                Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embedded != null) return embedded;
            }
            catch { }
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(4, 18, 24));
                using (var pen = new Pen(Color.FromArgb(0, 255, 255), 2f))
                    g.DrawRectangle(pen, 1, 1, 13, 13);
                using (var br = new SolidBrush(Color.FromArgb(0, 255, 255)))
                {
                    g.FillRectangle(br, 4, 8, 2, 5);
                    g.FillRectangle(br, 7, 4, 2, 9);
                    g.FillRectangle(br, 10, 6, 2, 7);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        void RunLoop()
        {
            using (var sensors = new HardwareSensors())
            using (var media = new MediaReader())
            using (var audio = new WasapiCava(64))
            using (var driver = new SerialDriver())
            {
                DateTime lastSensorUpdate = DateTime.MinValue;
                DateTime lastStatusFile = DateTime.MinValue;
                DateTime lastAudioActive = DateTime.MinValue;
                DateTime gpuBusySince = DateTime.MinValue;
                DateTime lastGamingActive = DateTime.MinValue;
                ScreenMode? lastRenderedMode = null;
                bool? lastOffState = null;
                Bitmap blackFrame = null;
                Bitmap lastSentFrame = null;
                Bitmap fadeFromFrame = null;
                DateTime fadeStartUtc = DateTime.MinValue;
                const double FadeDurationMs = 550.0;

                var mediaThread = new Thread(() =>
                {
                    while (!stopping)
                    {
                        bool prevPlaying = media.IsPlaying;
                        media.Update();
                        if (media.IsPlaying != prevPlaying || media.MediaChangedWake)
                        {
                            media.MediaChangedWake = false;
                            wakeEvent.Set();
                        }
                        Thread.Sleep(250);
                    }
                }) { IsBackground = true, Name = "MediaPoller" };
                mediaThread.SetApartmentState(ApartmentState.MTA);
                mediaThread.Start();

                try
                {
                    while (!stopping)
                    {
                        try
                        {
                            if ((DateTime.UtcNow - lastConfCheck).TotalSeconds >= 0.5)
                            {
                                lastConfCheck = DateTime.UtcNow;
                                if (File.Exists(cfg.ConfPath))
                                {
                                    DateTime wt = File.GetLastWriteTimeUtc(cfg.ConfPath);
                                    if (wt != lastConfWrite)
                                    {
                                        lastConfWrite = wt;
                                        Config reloaded = Config.Load(cfg.ConfPath);
                                        bool fpsChanged = reloaded.IdleFps != cfg.IdleFps || reloaded.ActiveFps != cfg.ActiveFps;
                                        bool sleepOrModeChanged = reloaded.Mode != cfg.Mode || reloaded.SyncMonitorSleep != cfg.SyncMonitorSleep;
                                        cfg.Mode = reloaded.Mode == "off" ? "auto" : reloaded.Mode;
                                        cfg.Rotate = reloaded.Rotate;
                                        cfg.ActiveFps = reloaded.ActiveFps;
                                        cfg.IdleFps = reloaded.IdleFps;
                                        cfg.ThemeColor = reloaded.ThemeColor;
                                        cfg.Brightness = reloaded.Brightness;
                                        cfg.GamingScreen = reloaded.GamingScreen;
                                        cfg.GamingGpuPercent = reloaded.GamingGpuPercent;
                                        cfg.GamingDelaySec = reloaded.GamingDelaySec;
                                        cfg.GamingHoldSec = reloaded.GamingHoldSec;
                                        cfg.ClockFormat = reloaded.ClockFormat;
                                        cfg.SyncMonitorSleep = reloaded.SyncMonitorSleep;
                                        if (sleepOrModeChanged) UpdateSleepState();
                                        if (fpsChanged && trofeoCore != null) trofeoCore.RestartNow();
                                    }
                                }
                            }

                            var sw = Stopwatch.StartNew();
                            DateTime now = DateTime.UtcNow;
                            bool isOff = IsEffectiveOff;

                            // Keep telemetry fresh even while panel is sleeping or reconnecting so Control Center stays live
                            if ((now - lastSensorUpdate).TotalMilliseconds >= 250)
                            {
                                float prevEffGpu = sensors.EffectiveGameGpuLoad;
                                sensors.Update();
                                lastSensorUpdate = now;
                                float curEffGpu = sensors.EffectiveGameGpuLoad;
                                if ((prevEffGpu < cfg.GamingGpuPercent && curEffGpu >= cfg.GamingGpuPercent) ||
                                    (prevEffGpu >= cfg.GamingGpuPercent && curEffGpu < cfg.GamingGpuPercent))
                                {
                                    wakeEvent.Set();
                                }
                            }

                            bool isGaming = false;
                            if (!isOff)
                            {
                                bool gpuAbove = sensors.EffectiveGameGpuLoad >= cfg.GamingGpuPercent;
                                if (gpuAbove)
                                {
                                    if (gpuBusySince == DateTime.MinValue) gpuBusySince = now;
                                    if ((now - gpuBusySince).TotalSeconds >= cfg.GamingDelaySec)
                                        lastGamingActive = now;
                                }
                                else
                                {
                                    gpuBusySince = DateTime.MinValue;
                                }

                                isGaming = cfg.GamingScreen && (lastGamingActive != DateTime.MinValue) &&
                                           ((now - lastGamingActive).TotalSeconds <= cfg.GamingHoldSec);
                            }
                            else
                            {
                                gpuBusySince = DateTime.MinValue;
                            }

                            // Match trofeo_lcd_core silence_timeout_ms = 800ms
                            bool isMusic = false;
                            if (!isOff)
                            {
                                audio.Update();
                                if (audio.PeakLevel > 0.001f) lastAudioActive = now;
                                isMusic = media.IsPlaying && (audio.PeakLevel > 0.001f || (now - lastAudioActive).TotalSeconds < 0.8);
                            }

                            // Keep Thermalright Gaming Mode + Unicode/Khmer Music overlay in 1:1 lockstep
                            bool wantUnicodeMusic = !isOff && !isGaming && (cfg.Mode == "music" || (cfg.Mode == "auto" && isMusic)) &&
                                                    TrofeoUnicodeMusicBridge.NeedsUnicodeFallback(media);
                            if (wantUnicodeMusic)
                            {
                                TrofeoUnicodeMusicBridge.EnsureRendered(media, cfg);
                            }
                            if (isGaming != autoGamingActive || wantUnicodeMusic != unicodeMusicActive)
                            {
                                autoGamingActive = isGaming;
                                unicodeMusicActive = wantUnicodeMusic;
                                UpdateSleepState();
                            }

                            if (!driver.IsConnected)
                            {
                                statusText = "Connecting...";
                                if (!driver.Connect(cfg.Port))
                                {
                                    statusText = driver.LastError ?? "Not connected";
                                    UpdateSnapshot(sensors, media, isOff, statusText);
                                    Thread.Sleep(2000);
                                    continue;
                                }
                            }

                            ScreenMode activeMode = lastRenderedMode ?? ScreenMode.Dashboard;
                            if (!isOff)
                            {
                                if (cfg.Mode == "gaming") activeMode = ScreenMode.Gaming;
                                else if (cfg.Mode == "music") activeMode = ScreenMode.Music;
                                else if (cfg.Mode == "dashboard") activeMode = ScreenMode.Dashboard;
                                else
                                {
                                    if (isGaming) activeMode = ScreenMode.Gaming;
                                    else if (isMusic) activeMode = ScreenMode.Music;
                                    else activeMode = ScreenMode.Dashboard;
                                }
                            }

                            statusText = isOff
                                ? string.Format("{0} ({1}x{2}) · {3}", driver.PortName, driver.Width, driver.Height,
                                    manualScreenOff ? "Screen Off (Manual)" : "Screen Off (Monitor Sleep)")
                                : string.Format("{0} ({1}x{2}) · {3}",
                                    driver.PortName, driver.Width, driver.Height,
                                    activeMode == ScreenMode.Gaming ? "Gaming Telemetry" :
                                    (activeMode == ScreenMode.Music ? "Music + Cava" : "System Dashboard"));

                            UpdateSnapshot(sensors, media, isOff, statusText);

                            int rot = cfg.Rotate >= 0 ? cfg.Rotate : driver.DefaultRotate;
                            bool stateChanged = (lastOffState.HasValue && isOff != lastOffState.Value) ||
                                                (!isOff && lastRenderedMode.HasValue && activeMode != lastRenderedMode.Value);
                            if (stateChanged && lastSentFrame != null)
                            {
                                if (fadeFromFrame != null) fadeFromFrame.Dispose();
                                fadeFromFrame = lastSentFrame;
                                lastSentFrame = null;
                                fadeStartUtc = DateTime.UtcNow;
                            }

                            if (blackFrame == null || blackFrame.Width != driver.Width || blackFrame.Height != driver.Height)
                            {
                                if (blackFrame != null) blackFrame.Dispose();
                                blackFrame = new Bitmap(driver.Width, driver.Height, PixelFormat.Format24bppRgb);
                                using (var bg = Graphics.FromImage(blackFrame)) bg.Clear(Color.Black);
                            }

                            Bitmap finalFrame = isOff
                                ? new Bitmap(blackFrame)
                                : Renderer.RenderFrame(driver.Width, driver.Height, cfg, sensors, media, audio, activeMode);
                            try
                            {
                                if (fadeFromFrame != null)
                                {
                                    double elapsedMs = (DateTime.UtcNow - fadeStartUtc).TotalMilliseconds;
                                    if (elapsedMs >= FadeDurationMs)
                                    {
                                        fadeFromFrame.Dispose();
                                        fadeFromFrame = null;
                                    }
                                    else
                                    {
                                        float t = (float)Math.Max(0.0, Math.Min(1.0, elapsedMs / FadeDurationMs));
                                        Bitmap blended = Renderer.BlendFrames(fadeFromFrame, finalFrame, t);
                                        finalFrame.Dispose();
                                        finalFrame = blended;
                                    }
                                }

                                driver.SendFrame(finalFrame, rot);
                                if (lastSentFrame != null) lastSentFrame.Dispose();
                                lastSentFrame = finalFrame;
                                finalFrame = null;
                                lastOffState = isOff;
                                if (!isOff) lastRenderedMode = activeMode;
                            }
                            finally
                            {
                                if (finalFrame != null) finalFrame.Dispose();
                            }

                            if ((now - lastStatusFile).TotalSeconds >= 3.0)
                            {
                                lastStatusFile = now;
                                try
                                {
                                    string stText = string.Format(CultureInfo.InvariantCulture,
                                        "updated={0}\nmode={1}\nport={2}\npawnio={3}\ncpu_load={4:0.0}\ncpu_temp={5:0.0}\ncpu_power={6:0.0}\ncpu_ghz={7:0.00}\ngpu_load={8:0.0}\neffective_gpu={9:0.0}\ngpu_temp={10:0.0}\nfps={11:0}\ntrofeo_pid={12}\n",
                                        DateTime.Now.ToString("s"), isOff ? "ScreenOff" : activeMode.ToString(), driver.PortName, sensors.PawnStatus,
                                        sensors.CpuLoad, sensors.CpuTemp, sensors.CpuPower, sensors.CpuGhz, sensors.GpuLoad, sensors.EffectiveGameGpuLoad,
                                        sensors.GpuTemp, sensors.GameFps, trofeoCore != null ? trofeoCore.CorePid : 0);
                                    File.WriteAllText(AppPaths.LovingcoolStatus, stText);
                                }
                                catch { }
                            }

                            int targetFps = fadeFromFrame != null
                                ? Math.Max(28, cfg.ActiveFps)
                                : (isOff ? 1 : (activeMode != ScreenMode.Dashboard ? Math.Max(20, cfg.ActiveFps) : Math.Max(8, cfg.IdleFps)));
                            int frameMs = 1000 / Math.Max(1, targetFps);
                            int sleep = frameMs - (int)sw.ElapsedMilliseconds;
                            if (sleep > 5) wakeEvent.WaitOne(sleep);
                        }
                        catch (Exception ex)
                        {
                            statusText = "Error: " + ex.Message;
                            if (fadeFromFrame != null) { fadeFromFrame.Dispose(); fadeFromFrame = null; }
                            if (lastSentFrame != null) { lastSentFrame.Dispose(); lastSentFrame = null; }
                            lastRenderedMode = null;
                            driver.Disconnect(false);
                            Thread.Sleep(2000);
                        }
                    }
                }
                finally
                {
                    if (blackFrame != null) blackFrame.Dispose();
                    if (lastSentFrame != null) lastSentFrame.Dispose();
                    if (fadeFromFrame != null) fadeFromFrame.Dispose();
                }

                driver.Disconnect(true);
            }
        }

        void UpdateSnapshot(HardwareSensors s, MediaReader m, bool isOff, string lcStatus)
        {
            string mediaLine = "Idle (No active media)";
            if (!string.IsNullOrEmpty(m.Title))
            {
                mediaLine = string.Format("{0}: {1}{2}", m.IsPlaying ? "Playing" : "Paused", m.Title,
                    string.IsNullOrEmpty(m.Artist) ? "" : " - " + m.Artist);
            }
            var next = new TelemetrySnapshot
            {
                MasterStatus = isOff ? (manualScreenOff ? "Manual Screen Off" : "Monitor Sleep Synced") : "Active (" + cfg.Mode.ToUpperInvariant() + ")",
                LovingcoolStatus = lcStatus ?? "N/A",
                CpuName = s.CpuName,
                CpuLoad = s.CpuLoad,
                CpuTemp = s.CpuTemp,
                CpuPower = s.CpuPower,
                CpuGhz = s.CpuGhz,
                GpuName = s.GpuName,
                GpuLoad = s.GpuLoad,
                WallpaperGpuLoad = s.WallpaperGpuLoad,
                EffectiveGpuLoad = s.EffectiveGameGpuLoad,
                GpuTemp = s.GpuTemp,
                GpuHotspot = s.GpuHotspot,
                GpuPower = s.GpuPower,
                RamUsedGb = s.RamUsedGb,
                RamTotalGb = s.RamTotalGb,
                RamPercent = s.RamPercent,
                GameFps = s.GameFps,
                GameTitle = s.GameTitle,
                IsForegroundGame = s.IsForegroundGame,
                MediaSummary = mediaLine,
                PawnStatus = s.PawnStatus,
                UpdatedAt = DateTime.Now
            };
            lock (snapLock) latestSnap = next;
        }

        public void Dispose()
        {
            stopping = true;
            manualScreenOff = false;
            TrofeoConfSync.ClearManagedProfiles();
            wakeEvent.Set();
            if (uiTimer != null) uiTimer.Dispose();
            if (controlForm != null && !controlForm.IsDisposed) controlForm.Dispose();
            if (trofeoCore != null) trofeoCore.Dispose();
            if (powerWatcher != null) powerWatcher.Dispose();
            if (showUiEvent != null) showUiEvent.Dispose();
            if (tray != null) tray.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
        }
    }

    sealed class CyanDarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Color.FromArgb(0, 68, 84); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(0, 220, 220); } }
        public override Color MenuBorder { get { return Color.FromArgb(0, 140, 160); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(6, 18, 26); } }
        public override Color SeparatorDark { get { return Color.FromArgb(14, 70, 86); } }
    }

    sealed class ControlCenterForm : Form
    {
        static readonly Color BgDark = Color.FromArgb(4, 12, 18);
        static readonly Color CardBg = Color.FromArgb(9, 24, 34);
        static readonly Color CardBorder = Color.FromArgb(16, 88, 108);
        static readonly Color Cyan = Color.FromArgb(0, 255, 255);
        static readonly Color Ice = Color.FromArgb(228, 252, 255);
        static readonly Color DimText = Color.FromArgb(132, 188, 200);

        readonly TrayApp app;
        readonly System.Windows.Forms.Timer refreshTimer;
        readonly Label lblHeaderBadge;
        readonly Label lblTrofeoInfo;
        readonly Label lblLovingInfo;
        readonly Label lblTelemetry;
        readonly Label lblBrightnessVal;
        readonly TrackBar trkBrightness;
        readonly ComboBox cmbLayout;
        readonly ComboBox cmbRotate;
        readonly NumericUpDown numIdleFps;
        readonly NumericUpDown numActiveFps;
        readonly CheckBox chkSleepSync;
        readonly Button[] modeButtons = new Button[5];
        readonly string[] modeKeys = { "auto", "dashboard", "music", "gaming", "off" };
        bool allowRealClose;
        bool suppressEvents;
        bool draggingBrightness;

        public ControlCenterForm(TrayApp app)
        {
            this.app = app;
            Text = "KuroHUD — Control Center";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Size = new Size(740, 625);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = BgDark;
            ForeColor = Ice;
            Font = new Font("Segoe UI", 9.5f);

            var lblTitle = AddLabel(this, "KUROHUD CONTROL CENTER", 14f, FontStyle.Bold, Cyan, 18, 14, 460, 28);
            var lblSub = AddLabel(this, "Thermalright 1920\u00d7462 · Lovingcool 480\u00d7480 · Sapphire GPU RGB", 8.5f, FontStyle.Regular, DimText, 19, 42, 460, 18);
            lblHeaderBadge = AddLabel(this, "ACTIVE", 9f, FontStyle.Bold, BgDark, 520, 18, 196, 28);
            lblHeaderBadge.BackColor = Cyan;
            lblHeaderBadge.TextAlign = ContentAlignment.MiddleCenter;

            // Row 1: Two screen status cards
            var cardTrofeo = AddCard(18, 70, 344, 118, "THERMALRIGHT TROFEO · 1920\u00d7462 USB");
            lblTrofeoInfo = AddLabel(cardTrofeo, "Starting...", 9f, FontStyle.Regular, Ice, 12, 30, 320, 80);

            var cardLoving = AddCard(376, 70, 344, 118, "LOVINGCOOL AIO · 480\u00d7480 COM");
            lblLovingInfo = AddLabel(cardLoving, "Starting...", 9f, FontStyle.Regular, Ice, 12, 30, 320, 80);

            // Row 2: Live shared telemetry card
            var cardTel = AddCard(18, 198, 702, 108, "SHARED LIVE TELEMETRY");
            lblTelemetry = AddLabel(cardTel, "Sampling sensors...", 9f, FontStyle.Regular, Ice, 12, 30, 678, 70);

            // Row 3: Master Mode selector
            var cardMode = AddCard(18, 316, 702, 74, "MASTER MODE (UPDATES BOTH SCREENS TOGETHER)");
            string[] labels = { "Auto Switch", "Dashboard", "Music + Vis", "Gaming Tele", "Screen Off" };
            for (int i = 0; i < 5; i++)
            {
                string key = modeKeys[i];
                var b = MakeButton(labels[i], 12 + i * 136, 30, 128, 32, (s, e) =>
                {
                    if (key == "off") app.SetManualScreenOff(true);
                    else app.SetMasterMode(key);
                    RefreshUi();
                });
                cardMode.Controls.Add(b);
                modeButtons[i] = b;
            }

            // Row 4: Synced Settings card
            var cardCfg = AddCard(18, 400, 702, 134, "SYNCHRONIZED DISPLAY SETTINGS");
            AddLabel(cardCfg, "Thermalright Layout", 9f, FontStyle.Regular, DimText, 12, 32, 136, 22);
            cmbLayout = new ComboBox
            {
                Location = new Point(150, 30),
                Size = new Size(185, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BgDark,
                ForeColor = Ice,
                FlatStyle = FlatStyle.Flat
            };
            foreach (string lay in TrofeoConfSync.GetAvailableLayouts()) cmbLayout.Items.Add(lay);
            cmbLayout.SelectedIndexChanged += (s, e) =>
            {
                if (!suppressEvents && cmbLayout.SelectedItem != null)
                    app.SetThermalrightLayout(cmbLayout.SelectedItem.ToString());
            };
            cardCfg.Controls.Add(cmbLayout);

            AddLabel(cardCfg, "Lovingcool Rotate", 9f, FontStyle.Regular, DimText, 360, 32, 130, 22);
            cmbRotate = new ComboBox
            {
                Location = new Point(492, 30),
                Size = new Size(192, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BgDark,
                ForeColor = Ice,
                FlatStyle = FlatStyle.Flat
            };
            cmbRotate.Items.AddRange(new object[] { "Auto (Device Default)", "0\u00b0", "90\u00b0", "180\u00b0", "270\u00b0" });
            cmbRotate.SelectedIndexChanged += (s, e) =>
            {
                if (suppressEvents) return;
                int[] rots = { -1, 0, 90, 180, 270 };
                if (cmbRotate.SelectedIndex >= 0 && cmbRotate.SelectedIndex < rots.Length)
                    app.SetRotate(rots[cmbRotate.SelectedIndex]);
            };
            cardCfg.Controls.Add(cmbRotate);

            AddLabel(cardCfg, "Master Brightness", 9f, FontStyle.Regular, DimText, 12, 66, 136, 22);
            trkBrightness = new TrackBar
            {
                Location = new Point(146, 62),
                Size = new Size(146, 30),
                Minimum = 10,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                BackColor = CardBg
            };
            lblBrightnessVal = AddLabel(cardCfg, "100%", 9.5f, FontStyle.Bold, Cyan, 294, 65, 48, 22);
            trkBrightness.MouseDown += (s, e) => { draggingBrightness = true; };
            trkBrightness.MouseUp += (s, e) => { draggingBrightness = false; app.SetMasterBrightness(trkBrightness.Value); };
            trkBrightness.Scroll += (s, e) =>
            {
                lblBrightnessVal.Text = trkBrightness.Value + "%";
                if (!draggingBrightness) app.SetMasterBrightness(trkBrightness.Value);
            };
            cardCfg.Controls.Add(trkBrightness);

            AddLabel(cardCfg, "Synced FPS", 9f, FontStyle.Regular, DimText, 360, 66, 80, 22);
            AddLabel(cardCfg, "Idle", 8.5f, FontStyle.Regular, DimText, 442, 67, 28, 20);
            numIdleFps = MakeNumeric(470, 64, 52, 1, 30);
            AddLabel(cardCfg, "Active", 8.5f, FontStyle.Regular, DimText, 528, 67, 42, 20);
            numActiveFps = MakeNumeric(572, 64, 52, 5, 30);
            var btnApplyFps = MakeButton("Apply", 630, 63, 56, 25, (s, e) =>
            {
                app.SetSyncedFps((int)numIdleFps.Value, (int)numActiveFps.Value);
                RefreshUi();
            });
            cardCfg.Controls.Add(numIdleFps);
            cardCfg.Controls.Add(numActiveFps);
            cardCfg.Controls.Add(btnApplyFps);

            chkSleepSync = new CheckBox
            {
                Text = "Auto turn off both screens & GPU RGB when main monitor turns off (Twinkle Tray / sleep)",
                Location = new Point(14, 100),
                Size = new Size(660, 24),
                ForeColor = Ice,
                BackColor = CardBg,
                FlatStyle = FlatStyle.Flat
            };
            chkSleepSync.CheckedChanged += (s, e) =>
            {
                if (!suppressEvents) app.SetSyncMonitorSleep(chkSleepSync.Checked);
            };
            cardCfg.Controls.Add(chkSleepSync);

            // Bottom action strip
            cardCfg.TabIndex = 4;
            Controls.Add(MakeButton("Layout Studio", 18, 546, 134, 32, (s, e) => app.OpenTrofeoConfig()));
            Controls.Add(MakeButton("Restart Core", 160, 546, 120, 32, (s, e) => { app.RestartTrofeoCore(); RefreshUi(); }));
            Controls.Add(MakeButton("Open Configs", 288, 546, 124, 32, (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + AppPaths.TrofeoDir + "\""); } catch { }
            }));
            Controls.Add(MakeButton("Hide to Tray", 482, 546, 120, 32, (s, e) => Hide()));
            var btnExit = MakeButton("Exit All", 610, 546, 110, 32, (s, e) => app.ExitAll());
            btnExit.ForeColor = Color.FromArgb(255, 130, 140);
            Controls.Add(btnExit);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += (s, e) => { if (Visible) RefreshUi(); };
            VisibleChanged += (s, e) =>
            {
                if (Visible) { refreshTimer.Start(); RefreshUi(); }
                else refreshTimer.Stop();
            };
            FormClosing += (s, e) =>
            {
                if (!allowRealClose && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        public void ForceClose()
        {
            allowRealClose = true;
            try { refreshTimer.Stop(); Close(); } catch { }
        }

        public void RefreshUi()
        {
            TelemetrySnapshot snap = app.GetSnapshot();
            bool effOff = app.IsEffectiveOff;
            lblHeaderBadge.Text = snap.MasterStatus.ToUpperInvariant();
            lblHeaderBadge.BackColor = effOff ? Color.FromArgb(120, 48, 60) : Cyan;
            lblHeaderBadge.ForeColor = effOff ? Ice : BgDark;

            string curLayout = TrofeoConfSync.GetActiveValue("layout", "default");
            string curBri = TrofeoConfSync.GetActiveValue("brightness", app.cfg.Brightness.ToString(CultureInfo.InvariantCulture));
            string curProfile = TrofeoConfSync.GetActiveProfile();
            string coreNote = app.Core != null ? app.Core.LastNote : "Stopped";

            lblTrofeoInfo.Text = string.Format(
                "Status: {0}\nActive Layout: {1}   |   Brightness: {2}%\nFPS Target: {3} idle / {4} active\nProfile Override: {5}",
                coreNote, curLayout, curBri, app.cfg.IdleFps, app.cfg.ActiveFps, curProfile);

            lblLovingInfo.Text = string.Format(
                "Status: {0}\nRotation: {1}   |   Brightness: {2}%\nFPS Target: {3} idle / {4} active\nPawnIO Driver: {5}",
                snap.LovingcoolStatus,
                app.cfg.Rotate < 0 ? "Auto" : (app.cfg.Rotate + "\u00b0"),
                app.cfg.Brightness, app.cfg.IdleFps, app.cfg.ActiveFps,
                string.IsNullOrEmpty(snap.PawnStatus) ? "N/A" : snap.PawnStatus);

            lblTelemetry.Text = string.Format(
                "CPU ({0}): {1:0}%  ·  {2:0.0}\u00b0C  ·  {3:0.0}W  ·  {4:0.00} GHz\n" +
                "GPU ({5}): {6:0}% raw (WPE {7:0}% -> Game {8:0}%)  ·  {9:0}\u00b0C / HS {10:0}\u00b0C  ·  {11:0}W\n" +
                "RAM: {12:0.0}/{13:0.0} GB ({14:0}%)  ·  FPS: {15}  ·  Media: {16}",
                snap.CpuName, snap.CpuLoad, snap.CpuTemp, snap.CpuPower, snap.CpuGhz,
                snap.GpuName, snap.GpuLoad, snap.WallpaperGpuLoad, snap.EffectiveGpuLoad, snap.GpuTemp, snap.GpuHotspot, snap.GpuPower,
                snap.RamUsedGb, snap.RamTotalGb, snap.RamPercent,
                snap.GameFps > 0f ? string.Format("{0:0} ({1})", snap.GameFps, snap.GameTitle) : "0 (No active game)",
                snap.MediaSummary);

            string activeKey = app.IsManualScreenOff ? "off" : app.cfg.Mode;
            for (int i = 0; i < modeButtons.Length; i++)
            {
                bool on = string.Equals(modeKeys[i], activeKey, StringComparison.OrdinalIgnoreCase);
                modeButtons[i].BackColor = on ? Cyan : Color.FromArgb(11, 34, 48);
                modeButtons[i].ForeColor = on ? BgDark : Ice;
            }

            suppressEvents = true;
            try
            {
                if (!cmbLayout.Focused)
                {
                    int idx = cmbLayout.FindStringExact(curLayout);
                    if (idx >= 0 && cmbLayout.SelectedIndex != idx) cmbLayout.SelectedIndex = idx;
                }
                if (!cmbRotate.Focused)
                {
                    int[] rots = { -1, 0, 90, 180, 270 };
                    int rIdx = Array.IndexOf(rots, app.cfg.Rotate);
                    if (rIdx >= 0 && cmbRotate.SelectedIndex != rIdx) cmbRotate.SelectedIndex = rIdx;
                }
                if (!draggingBrightness && trkBrightness.Value != app.cfg.Brightness)
                {
                    trkBrightness.Value = Math.Max(10, Math.Min(100, app.cfg.Brightness));
                    lblBrightnessVal.Text = trkBrightness.Value + "%";
                }
                if (!numIdleFps.Focused) numIdleFps.Value = Math.Max(1, Math.Min(30, app.cfg.IdleFps));
                if (!numActiveFps.Focused) numActiveFps.Value = Math.Max(5, Math.Min(30, app.cfg.ActiveFps));
                chkSleepSync.Checked = app.cfg.SyncMonitorSleep;
            }
            finally
            {
                suppressEvents = false;
            }
        }

        Panel AddCard(int x, int y, int w, int h, string heading)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = CardBg };
            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(CardBorder, 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            AddLabel(p, heading, 8.5f, FontStyle.Bold, Cyan, 12, 8, w - 24, 18);
            Controls.Add(p);
            return p;
        }

        static Label AddLabel(Control parent, string text, float size, FontStyle style, Color color, int x, int y, int w, int h)
        {
            var l = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", size, style),
                ForeColor = color,
                Location = new Point(x, y),
                Size = new Size(w, h)
            };
            parent.Controls.Add(l);
            return l;
        }

        static Button MakeButton(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(11, 34, 48),
                ForeColor = Ice,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = CardBorder;
            b.FlatAppearance.BorderSize = 1;
            b.Click += onClick;
            return b;
        }

        static NumericUpDown MakeNumeric(int x, int y, int w, int min, int max)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 24),
                Minimum = min,
                Maximum = max,
                BackColor = BgDark,
                ForeColor = Ice,
                BorderStyle = BorderStyle.FixedSingle
            };
        }
    }

    // =========================================================================
    // LOVINGCOOL SERIAL DRIVER (USB-CDC VID_33C3, 2,000,000 baud, 55 AA protocol)
    // =========================================================================
    class SerialDriver : IDisposable
    {
        SerialPort port;
        public string PortName { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Angle { get; private set; }
        public double Version { get; private set; }
        public string Model { get; private set; }
        public string LastError { get; private set; }
        DateTime lastKeepAlive;
        static ImageCodecInfo jpegCodec;
        int jpegQuality = 88;

        public bool IsConnected { get { return port != null && port.IsOpen; } }
        public int DefaultRotate { get { return (Angle == 90 || Angle == 270) ? Angle : 0; } }

        public SerialDriver()
        {
            Width = 480;
            Height = 480;
            Angle = 270;
            Version = 4.5;
        }

        public bool Connect(string configPort)
        {
            Disconnect(false);
            string com = (string.IsNullOrEmpty(configPort) || configPort.Equals("auto", StringComparison.OrdinalIgnoreCase))
                ? FindPort(null) : configPort;
            if (string.IsNullOrEmpty(com))
            {
                LastError = "Lovingcool screen not found";
                return false;
            }

            PortName = com;
            port = new SerialPort(com, 2000000)
            {
                DtrEnable = true,
                ReadTimeout = 150,
                WriteTimeout = 4000
            };

            try
            {
                port.Open();
            }
            catch (UnauthorizedAccessException)
            {
                LastError = com + " busy (close official Lovingcool app)";
                port.Dispose();
                port = null;
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                port.Dispose();
                port = null;
                return false;
            }

            Thread.Sleep(250);
            port.Write(new byte[] { 0xFF, 0xD9, 0xFF, 0xD9 }, 0, 4);
            port.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);
            Thread.Sleep(150);
            port.DiscardInBuffer();

            string json = Query(0x06);
            if (json != null)
            {
                Width = JsonInt(json, "width", 480);
                Height = JsonInt(json, "height", 480);
                Angle = JsonInt(json, "angle", 270);
                Model = JsonStr(json, "model") ?? "";
                string verStr = JsonStr(json, "version") ?? "4.5";
                double v;
                if (double.TryParse(verStr, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Version = v;
            }

            SendCommand(0x11); // Enter live streaming mode
            lastKeepAlive = DateTime.UtcNow;
            return true;
        }

        public void SendFrame(Bitmap bmp, int rotate)
        {
            if (!IsConnected) return;

            int r = ((rotate % 360) + 360) % 360;
            int maxBytes = Version > 2.8 ? 80 * 1024 : 50 * 1024;
            byte[] jpeg;
            if (r != 0)
            {
                // Rotate a copy so caller's frame stays unrotated for transitions
                using (var rotated = new Bitmap(bmp))
                {
                    if (r == 90) rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (r == 180) rotated.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    else if (r == 270) rotated.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    jpeg = EncodeJpeg(rotated, maxBytes, ref jpegQuality);
                }
            }
            else
            {
                jpeg = EncodeJpeg(bmp, maxBytes, ref jpegQuality);
            }
            byte[] packet = WrapFrame(jpeg);

            if ((DateTime.UtcNow - lastKeepAlive).TotalMilliseconds >= 1500)
            {
                SendCommand(0x11);
                lastKeepAlive = DateTime.UtcNow;
            }

            port.Write(packet, 0, packet.Length);
            if (port.BytesToRead > 0) port.DiscardInBuffer();
        }

        byte[] WrapFrame(byte[] jpeg)
        {
            if (Version <= 2.8) return jpeg;
            byte[] f = new byte[jpeg.Length + 6];
            BitConverter.GetBytes(jpeg.Length).CopyTo(f, 0);
            System.Buffer.BlockCopy(jpeg, 0, f, 4, jpeg.Length);
            int sum = 0;
            for (int i = 0; i < f.Length - 2; i++) sum += f[i];
            f[f.Length - 2] = (byte)sum;
            f[f.Length - 1] = (byte)(sum >> 8);
            return f;
        }

        static byte[] EncodeJpeg(Bitmap bmp, int maxBytes, ref int quality)
        {
            if (jpegCodec == null)
            {
                foreach (var c in ImageCodecInfo.GetImageEncoders())
                    if (c.MimeType == "image/jpeg") { jpegCodec = c; break; }
            }
            for (int q = quality; q >= 35; q -= 8)
            {
                using (var ms = new MemoryStream())
                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);
                    bmp.Save(ms, jpegCodec, ep);
                    if (ms.Length <= maxBytes || q <= 40)
                    {
                        quality = Math.Min(92, q + 4);
                        return ms.ToArray();
                    }
                }
            }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Jpeg);
                return ms.ToArray();
            }
        }

        void SendCommand(byte cmd)
        {
            byte[] b = new byte[7];
            b[0] = 0x55; b[1] = 0xAA; b[2] = 7; b[3] = 0; b[4] = cmd;
            int sum = 0;
            for (int i = 0; i < 5; i++) sum += b[i];
            b[5] = (byte)sum; b[6] = (byte)(sum >> 8);
            port.Write(b, 0, b.Length);
        }

        string Query(byte cmd)
        {
            SendCommand(cmd);
            var buf = new List<byte>();
            var deadline = DateTime.UtcNow.AddMilliseconds(1200);
            byte[] chunk = new byte[1024];
            while (DateTime.UtcNow < deadline)
            {
                int n = 0;
                try { n = port.Read(chunk, 0, chunk.Length); } catch (TimeoutException) { }
                for (int i = 0; i < n; i++) buf.Add(chunk[i]);
                string text = Encoding.UTF8.GetString(buf.ToArray());
                int a = text.IndexOf('{'), z = text.LastIndexOf('}');
                if (a >= 0 && z > a) return text.Substring(a, z - a + 1);
            }
            return null;
        }

        static string JsonStr(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(?:\"([^\"]*)\"|([-0-9.]+))");
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        static int JsonInt(string json, string key, int def)
        {
            double d;
            string s = JsonStr(json, key);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (int)d : def;
        }

        public static string FindPort(string preferred)
        {
            var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(preferred) && !preferred.Equals("auto", StringComparison.OrdinalIgnoreCase) && present.Contains(preferred))
                return preferred;

            try
            {
                using (RegistryKey usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
                {
                    if (usb != null)
                    {
                        foreach (string dev in usb.GetSubKeyNames())
                        {
                            if (!dev.StartsWith("VID_33C3", StringComparison.OrdinalIgnoreCase)) continue;
                            using (RegistryKey dk = usb.OpenSubKey(dev))
                            {
                                if (dk == null) continue;
                                foreach (string inst in dk.GetSubKeyNames())
                                {
                                    using (RegistryKey p = dk.OpenSubKey(inst + @"\Device Parameters"))
                                    {
                                        if (p == null) continue;
                                        string portName = p.GetValue("PortName") as string;
                                        if (!string.IsNullOrEmpty(portName) && present.Contains(portName))
                                            return portName;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return present.Contains("COM5") ? "COM5" : null;
        }

        public void Disconnect(bool appExiting)
        {
            if (port == null) return;
            try
            {
                if (appExiting && port.IsOpen)
                {
                    if (Version >= 2.8) port.Write(new byte[] { 0xFF, 0xD9, 0xFF, 0xD9 }, 0, 4);
                    if (Version >= 3.1) SendCommand(0x21);
                    Thread.Sleep(80);
                }
            }
            catch { }
            finally
            {
                try { port.Dispose(); } catch { }
                port = null;
            }
        }

        public void Dispose() { Disconnect(true); }
    }

    // =========================================================================
    // HARDWARE SENSORS + FOREGROUND GAME DETECTOR
    // =========================================================================
    class HardwareSensors : IDisposable
    {
        public string CpuName = "AMD RYZEN 5 5600";
        public string GpuName = "RADEON RX 6700 XT";
        public float CpuLoad;
        public float CpuTemp;
        public float CpuPower;
        public float CpuGhz = 4.4f;
        public string PawnStatus = "Not initialized";
        public float GpuLoad;
        public float GpuTemp;
        public float GpuHotspot;
        public float GpuPower;
        public float GpuClockMHz;
        public float RamUsedGb;
        public float RamTotalGb;
        public float RamPercent;

        // Gaming telemetry
        public bool IsForegroundGame;
        public string GameTitle = "ACTIVE GAME";
        public DateTime GameStartTime = DateTime.UtcNow;
        public float GameFps;
        public readonly List<float> FpsHistory = new List<float>();
        public readonly List<float> CpuHistory = new List<float>();
        public readonly List<float> GpuHistory = new List<float>();

        string lastGameProc = "";
        int baseCpuMaxMhz;
        IntPtr pdhCpuQuery = IntPtr.Zero;
        IntPtr pdhCpuPerfCounter = IntPtr.Zero;
        bool pdhCpuPrimed;
        ulong prevIdle, prevKernel, prevUser;
        IntPtr adlCtx = IntPtr.Zero;
        int adlAdapter = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        public delegate IntPtr ADL_Main_Memory_Alloc(int size);
        static readonly ADL_Main_Memory_Alloc AdlAllocDelegate = AllocAdl;
        static IntPtr AllocAdl(int size) { return Marshal.AllocHGlobal(size); }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, out IntPtr context);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Destroy(IntPtr context);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

        [StructLayout(LayoutKind.Sequential)]
        struct ADLPMLogDataOutput
        {
            public int size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public int[] sensors;
        }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapterIndex, out ADLPMLogDataOutput output);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_FrameMetrics_Caps(IntPtr context, int adapterIndex, out int supported);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_FrameMetrics_Start(IntPtr context, int adapterIndex, int displayIndex);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_FrameMetrics_Get(IntPtr context, int adapterIndex, int displayIndex, out float fps);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Adapter_FrameMetrics_Stop(IntPtr context, int adapterIndex, int displayIndex);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateMutexW(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);
        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll")]
        static extern bool ReleaseMutex(IntPtr hMutex);

        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int pawnio_open(out IntPtr handle);
        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int pawnio_load(IntPtr handle, byte[] blob, IntPtr size);
        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern int pawnio_execute(IntPtr handle, string name, ulong[] inBuf, IntPtr inSize, ulong[] outBuf, IntPtr outSize, out IntPtr returnSize);
        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int pawnio_close(IntPtr handle);

        [DllImport("powrprof.dll")]
        static extern uint CallNtPowerInformation(int InformationLevel, IntPtr InputBuffer, uint InputBufferLength, IntPtr OutputBuffer, uint OutputBufferLength);

        [DllImport("pdh.dll")]
        static extern int PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern int PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);
        [DllImport("pdh.dll")]
        static extern int PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        struct PDH_FMT_COUNTERVALUE
        {
            public uint CStatus;
            public int pad;
            public double doubleValue;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PDH_FMT_COUNTERVALUE_ITEM
        {
            public IntPtr szName;
            public uint CStatus;
            public int pad;
            public double doubleValue;
        }

        public float WallpaperGpuLoad;
        public float EffectiveGameGpuLoad
        {
            get
            {
                if (IsForegroundGame) return GpuLoad;
                return Math.Max(0f, GpuLoad - WallpaperGpuLoad);
            }
        }

        IntPtr pawnHandle = IntPtr.Zero;
        double energyUnitJoules;
        uint lastCpuEnergy;
        DateTime lastCpuEnergyTime = DateTime.MinValue;
        bool adlFrameMetricsStarted;
        readonly List<int> adlFpsWindow = new List<int>();
        IntPtr pdhQuery = IntPtr.Zero;
        IntPtr pdhGpuCounter = IntPtr.Zero;
        DateTime lastWallpaperPidScan = DateTime.MinValue;
        DateTime lastWallpaperSample = DateTime.MinValue;
        string lastWallpaperPidSig = "";
        readonly HashSet<int> wallpaperPids = new HashSet<int>();

        static readonly string[] WallpaperProcNames =
        {
            "wallpaper64", "wallpaper32", "webwallpaper64", "webwallpaper32", "edgewallpaper64"
        };

        static readonly HashSet<string> NonGameProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "searchhost", "searchapp", "shellexperiencehost", "startmenuexperiencehost",
            "applicationframehost", "taskmgr", "cmd", "powershell", "pwsh", "windowsterminal",
            "chrome", "brave", "msedge", "msedgewebview2", "firefox", "opera", "vivaldi",
            "spotify", "discord", "foobar2000", "musicbee", "vlc", "mpv", "potplayermini64",
            "steamwebhelper", "steam", "epicgameslauncher", "riotclientservices", "riotclientux",
            "antigravity", "code", "devenv", "notepad", "lovingcool_lcd", "trofeo_lcd", "trofeo_lcd_core", "trofeo_config",
            "lcdwall", "signalrgb", "signalrgblauncher", "fluentflyout", "twinkle tray",
            "radeonsoftware", "amdrsserv", "nvcontainer", "obs64", "obs32", "sharex", "phoneexperiencehost",
            "wallpaper64", "wallpaper32", "webwallpaper64", "webwallpaper32",
            "edgewallpaper64", "wallpaperservice64", "wallpaperui"
        };

        static readonly HashSet<string> KnownGameProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "robloxplayerbeta", "valorant-win64-shipping", "valorant", "league of legends",
            "cs2", "dota2", "r5apex", "overwatch", "fortniteclient-win64-shipping", "genshinimpact",
            "starrail", "zenlesszonezero", "wuthering waves", "eldenring", "cyberpunk2077",
            "baldur's gate 3", "bg3", "bg3_dx11", "helldivers2", "palworld-win64-shipping",
            "minecraft", "javaw", "gta5", "rdr2", "RocketLeague", "DeadByDaylight-Win64-Shipping",
            "pubg", "TslGame", "Warframe.x64", "PathOfExileSteam", "destiny2"
        };

        public HardwareSensors()
        {
            try
            {
                if (ADL2_Main_Control_Create(AdlAllocDelegate, 1, out adlCtx) == 0)
                {
                    int n;
                    if (ADL2_Adapter_NumberOfAdapters_Get(adlCtx, out n) == 0)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            ADLPMLogDataOutput pm;
                            if (ADL2_New_QueryPMLogData_Get(adlCtx, i, out pm) == 0 && pm.sensors[8 * 2] != 0)
                            {
                                adlAdapter = i;
                                int sup;
                                if (ADL2_Adapter_FrameMetrics_Caps(adlCtx, adlAdapter, out sup) == 0 && sup != 0)
                                {
                                    if (ADL2_Adapter_FrameMetrics_Start(adlCtx, adlAdapter, 0) == 0)
                                        adlFrameMetricsStarted = true;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch { adlCtx = IntPtr.Zero; }

            // Initialize real CPU base clock + boost frequency PDH counter
            baseCpuMaxMhz = ReadPowerInfoMaxMhz();
            if (baseCpuMaxMhz > 0) CpuGhz = baseCpuMaxMhz / 1000f;
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out pdhCpuQuery) == 0 &&
                    PdhAddEnglishCounterW(pdhCpuQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out pdhCpuPerfCounter) == 0)
                {
                    PdhCollectQueryData(pdhCpuQuery);
                    pdhCpuPrimed = true;
                }
                else
                {
                    CloseCpuFreqPdh();
                }
            }
            catch { CloseCpuFreqPdh(); }

            // Initialize PawnIO for AMD Ryzen CPU Temp & Power
            try
            {
                string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AMDFamily17.bin");
                if (!File.Exists(binPath)) binPath = AppPaths.AmdBinPath;
                if (File.Exists(binPath))
                {
                    byte[] blob = File.ReadAllBytes(binPath);
                    IntPtr h;
                    int hrOpen = pawnio_open(out h);
                    if (hrOpen >= 0 && h != IntPtr.Zero)
                    {
                        int hrLoad = pawnio_load(h, blob, (IntPtr)blob.Length);
                        if (hrLoad >= 0)
                        {
                            pawnHandle = h;
                            ulong[] inMsr = { 0xC0010299UL };
                            ulong[] outMsr = new ulong[1];
                            IntPtr retSz;
                            int hrUnit = pawnio_execute(pawnHandle, "ioctl_read_msr", inMsr, (IntPtr)1, outMsr, (IntPtr)1, out retSz);
                            if (hrUnit >= 0)
                            {
                                int esu = (int)((outMsr[0] >> 8) & 0x1FUL);
                                energyUnitJoules = Math.Pow(0.5, esu);
                            }
                            PawnStatus = string.Format("OK (unitHr=0x{0:X8})", hrUnit);
                        }
                        else
                        {
                            PawnStatus = string.Format("pawnio_load failed 0x{0:X8}", hrLoad);
                            pawnio_close(h);
                        }
                    }
                    else
                    {
                        PawnStatus = string.Format("pawnio_open failed 0x{0:X8}", hrOpen);
                    }
                }
                else
                {
                    PawnStatus = "AMDFamily17.bin not found";
                }
            }
            catch (Exception ex) { pawnHandle = IntPtr.Zero; PawnStatus = "ERR: " + ex.Message; }

            for (int i = 0; i < 60; i++)
            {
                CpuHistory.Add(0);
                GpuHistory.Add(0);
                FpsHistory.Add(0);
            }
        }

        public void Update()
        {
            // 1. CPU Load via GetSystemTimes
            ulong idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong idleDiff = idle - prevIdle;
                ulong totalDiff = (kernel - prevKernel) + (user - prevUser);
                prevIdle = idle; prevKernel = kernel; prevUser = user;
                if (totalDiff > 0)
                {
                    float busy = (float)(totalDiff - idleDiff) * 100f / totalDiff;
                    CpuLoad = Math.Max(0f, Math.Min(100f, busy));
                }
            }

            CpuGhz = ReadRealCpuGhz();

            // 1b. AMD Ryzen CPU Temp & Power via PawnIO
            if (pawnHandle != IntPtr.Zero)
            {
                try
                {
                    IntPtr mtx = CreateMutexW(IntPtr.Zero, false, "Global\\Access_PCI");
                    bool ownsMutex = false;
                    if (mtx != IntPtr.Zero)
                    {
                        uint wr = WaitForSingleObject(mtx, 200);
                        ownsMutex = (wr == 0u || wr == 0x80u);
                    }
                    try
                    {
                        ulong[] inSmn = { 0x59800UL };
                        ulong[] outSmn = new ulong[1];
                        IntPtr retSz;
                        int hrSmn = pawnio_execute(pawnHandle, "ioctl_read_smn", inSmn, (IntPtr)1, outSmn, (IntPtr)1, out retSz);
                        if (hrSmn >= 0)
                        {
                            uint raw = (uint)outSmn[0];
                            bool rangeSel = (raw & 0x00080000u) != 0;
                            bool tjSel = (raw & 0x00030000u) == 0x00030000u;
                            int milliC = (int)(raw >> 21) * 125;
                            if (rangeSel || tjSel) milliC -= 49000;
                            float tc = milliC / 1000f;
                            if (tc > 0f && tc < 130f) CpuTemp = tc;
                            PawnStatus = string.Format("OK (Temp={0:0.0}C)", CpuTemp);
                        }
                        else
                        {
                            PawnStatus = string.Format("smn err 0x{0:X8}", hrSmn);
                        }
                    }
                    finally
                    {
                        if (ownsMutex) ReleaseMutex(mtx);
                        if (mtx != IntPtr.Zero) CloseHandle(mtx);
                    }

                    if (energyUnitJoules > 0)
                    {
                        ulong[] inMsr = { 0xC001029BUL };
                        ulong[] outMsr = new ulong[1];
                        IntPtr retSz;
                        if (pawnio_execute(pawnHandle, "ioctl_read_msr", inMsr, (IntPtr)1, outMsr, (IntPtr)1, out retSz) >= 0)
                        {
                            uint curEnergy = (uint)(outMsr[0] & 0xFFFFFFFFUL);
                            DateTime now = DateTime.UtcNow;
                            if (lastCpuEnergyTime != DateTime.MinValue)
                            {
                                double dt = (now - lastCpuEnergyTime).TotalSeconds;
                                if (dt >= 0.15)
                                {
                                    uint delta = unchecked(curEnergy - lastCpuEnergy);
                                    double watts = (delta * energyUnitJoules) / dt;
                                    if (watts >= 0 && watts < 300) CpuPower = (float)watts;
                                    lastCpuEnergy = curEnergy;
                                    lastCpuEnergyTime = now;
                                }
                            }
                            else
                            {
                                lastCpuEnergy = curEnergy;
                                lastCpuEnergyTime = now;
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. RAM via GlobalMemoryStatusEx
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref mem))
            {
                RamTotalGb = mem.ullTotalPhys / (1024f * 1024f * 1024f);
                RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024f * 1024f * 1024f);
                RamPercent = mem.dwMemoryLoad;
            }

            // 3. AMD GPU via ADL2 PMLog
            float driverFps = 0f;
            if (adlCtx != IntPtr.Zero && adlAdapter >= 0)
            {
                try
                {
                    ADLPMLogDataOutput pm;
                    if (ADL2_New_QueryPMLogData_Get(adlCtx, adlAdapter, out pm) == 0)
                    {
                        if (pm.sensors[1 * 2] != 0) GpuClockMHz = pm.sensors[1 * 2 + 1];
                        if (pm.sensors[8 * 2] != 0) GpuTemp = pm.sensors[8 * 2 + 1];
                        if (pm.sensors[19 * 2] != 0) GpuLoad = pm.sensors[19 * 2 + 1];
                        if (pm.sensors[23 * 2] != 0) GpuPower = pm.sensors[23 * 2 + 1];
                        if (pm.sensors[27 * 2] != 0) GpuHotspot = pm.sensors[27 * 2 + 1];
                    }
                    if (adlFrameMetricsStarted)
                    {
                        float fmFps;
                        if (ADL2_Adapter_FrameMetrics_Get(adlCtx, adlAdapter, 0, out fmFps) == 0 && fmFps > 0f && fmFps <= 1500f)
                        {
                            adlFpsWindow.Add((int)Math.Round(fmFps));
                            if (adlFpsWindow.Count > 5) adlFpsWindow.RemoveAt(0);
                            var sorted = new List<int>(adlFpsWindow);
                            sorted.Sort();
                            driverFps = sorted[sorted.Count / 2];
                        }
                        else
                        {
                            adlFpsWindow.Clear();
                        }
                    }
                }
                catch { }
            }

            // 4. Exclude Wallpaper Engine GPU load from desktop gaming-mode triggers
            UpdateWallpaperGpuLoad();

            // 5. Detect Foreground Game & Window Title
            DetectForegroundGame();

            // 6. Read exact FPS_NOW (RVA 0x7AEF64) from trofeo_lcd_core only while a real game/3D load is active
            bool allowFps = IsForegroundGame || EffectiveGameGpuLoad >= 20f;
            int trofeoFps = allowFps ? ReadTrofeoFpsNow() : 0;
            if (allowFps && trofeoFps > 0)
            {
                GameFps = trofeoFps;
            }
            else if (allowFps && driverFps > 0)
            {
                GameFps = driverFps;
            }
            else
            {
                GameFps = 0f;
            }
            if (!IsForegroundGame && EffectiveGameGpuLoad < 20f && GameFps <= 0f)
            {
                GameTitle = "ACTIVE GAME";
                lastGameProc = "";
            }

            if (CpuHistory.Count >= 60) CpuHistory.RemoveAt(0);
            CpuHistory.Add(CpuLoad);

            if (GpuHistory.Count >= 60) GpuHistory.RemoveAt(0);
            GpuHistory.Add(GpuLoad);

            if (FpsHistory.Count >= 60) FpsHistory.RemoveAt(0);
            FpsHistory.Add(GameFps);
        }

        void DetectForegroundGame()
        {
            IsForegroundGame = false;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != 0)
                    {
                        using (Process p = Process.GetProcessById((int)pid))
                        {
                            CheckIfGameProcess(p, hwnd);
                        }
                    }
                }
            }
            catch { }
        }

        bool CheckIfGameProcess(Process p, IntPtr hwnd)
        {
            string procName = p.ProcessName ?? "";
            if (NonGameProcesses.Contains(procName)) return false;

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string winTitle = sb.ToString().Trim();

            bool isKnownGame = KnownGameProcesses.Contains(procName);
            bool isGameFolder = false;
            try
            {
                string path = p.MainModule != null ? p.MainModule.FileName : "";
                if (!string.IsNullOrEmpty(path))
                {
                    string low = path.ToLowerInvariant();
                    if (low.Contains(@"\steamapps\common\") ||
                        low.Contains(@"\riot games\") ||
                        low.Contains(@"\epic games\") ||
                        low.Contains(@"\xboxgames\") ||
                        low.Contains(@"\gog galaxy\games\") ||
                        low.Contains(@"\roblox\versions\"))
                    {
                        isGameFolder = true;
                    }
                }
            }
            catch { }

            bool isFullscreen3D = false;
            RECT r;
            if (GetWindowRect(hwnd, out r))
            {
                int ww = r.Right - r.Left;
                int wh = r.Bottom - r.Top;
                var scr = Screen.PrimaryScreen.Bounds;
                if (ww >= scr.Width && wh >= scr.Height && GpuLoad >= 15f)
                    isFullscreen3D = true;
            }

            if (isKnownGame || isGameFolder || isFullscreen3D || EffectiveGameGpuLoad >= 25f)
            {
                IsForegroundGame = isKnownGame || isGameFolder || isFullscreen3D;
                SetGameInfo(procName, winTitle);
                return true;
            }
            return false;
        }

        void SetGameInfo(string procName, string winTitle)
        {
            string cleanTitle = !string.IsNullOrEmpty(winTitle) ? winTitle : procName;
            if (cleanTitle.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)) cleanTitle = "ROBLOX";
            else if (cleanTitle.IndexOf("VALORANT", StringComparison.OrdinalIgnoreCase) >= 0) cleanTitle = "VALORANT";
            if (cleanTitle.Length > 24) cleanTitle = cleanTitle.Substring(0, 24).Trim();
            if (!string.IsNullOrEmpty(cleanTitle)) GameTitle = cleanTitle.ToUpperInvariant();

            if (procName != lastGameProc)
            {
                lastGameProc = procName;
                GameStartTime = DateTime.UtcNow;
            }
        }

        IntPtr cachedTrofeoHandle = IntPtr.Zero;
        IntPtr cachedTrofeoFpsAddr = IntPtr.Zero;
        int cachedTrofeoPid = -1;
        DateTime lastTrofeoLookup = DateTime.MinValue;

        int ReadTrofeoFpsNow()
        {
            // FPS_NOW AtomicU32 in trofeo_lcd v2.22.0 is at RVA 0x7AEF64
            const int FpsNowRva = 0x7AEF64;

            if (cachedTrofeoHandle != IntPtr.Zero)
            {
                byte[] buf = new byte[4];
                IntPtr bytesRead;
                if (ReadProcessMemory(cachedTrofeoHandle, cachedTrofeoFpsAddr, buf, 4, out bytesRead) && (int)bytesRead == 4)
                {
                    int val = BitConverter.ToInt32(buf, 0);
                    if (val >= 0 && val <= 2000) return val;
                }
                CloseHandle(cachedTrofeoHandle);
                cachedTrofeoHandle = IntPtr.Zero;
                cachedTrofeoPid = -1;
            }

            if ((DateTime.UtcNow - lastTrofeoLookup).TotalSeconds < 2.0) return 0;
            lastTrofeoLookup = DateTime.UtcNow;

            string[] names = { "trofeo_lcd_core" };
            foreach (string name in names)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        using (p)
                        {
                            try
                            {
                                if (p.MainModule == null) continue;
                                if (p.MainModule.ModuleMemorySize < 2 * 1024 * 1024) continue;
                                IntPtr baseAddr = p.MainModule.BaseAddress;
                                IntPtr hProc = OpenProcess(0x0010, false, p.Id); // PROCESS_VM_READ
                                if (hProc == IntPtr.Zero) continue;
                                cachedTrofeoHandle = hProc;
                                cachedTrofeoPid = p.Id;
                                cachedTrofeoFpsAddr = new IntPtr(baseAddr.ToInt64() + FpsNowRva);
                                byte[] buf = new byte[4];
                                IntPtr bytesRead;
                                if (ReadProcessMemory(cachedTrofeoHandle, cachedTrofeoFpsAddr, buf, 4, out bytesRead) && (int)bytesRead == 4)
                                {
                                    int val = BitConverter.ToInt32(buf, 0);
                                    if (val >= 0 && val <= 2000) return val;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return 0;
        }

        static int ReadPowerInfoMaxMhz()
        {
            int count = Math.Max(1, Environment.ProcessorCount);
            IntPtr buf = Marshal.AllocHGlobal(count * 24);
            try
            {
                uint rc = CallNtPowerInformation(11, IntPtr.Zero, 0, buf, (uint)(count * 24));
                if (rc == 0)
                {
                    int maxMhz = Marshal.ReadInt32(buf, 4);
                    int curMhz = Marshal.ReadInt32(buf, 8);
                    return maxMhz > 0 ? maxMhz : curMhz;
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return 0;
        }

        float ReadRealCpuGhz()
        {
            if (baseCpuMaxMhz <= 0) baseCpuMaxMhz = ReadPowerInfoMaxMhz();
            if (pdhCpuQuery != IntPtr.Zero && pdhCpuPrimed)
            {
                try
                {
                    if (PdhCollectQueryData(pdhCpuQuery) == 0)
                    {
                        uint type;
                        PDH_FMT_COUNTERVALUE val;
                        if (PdhGetFormattedCounterValue(pdhCpuPerfCounter, 0x00000200, out type, out val) == 0 &&
                            val.CStatus <= 1 && val.doubleValue > 5.0 && val.doubleValue <= 300.0 && baseCpuMaxMhz > 0)
                        {
                            double ghz = (baseCpuMaxMhz * (val.doubleValue / 100.0)) / 1000.0;
                            if (ghz >= 0.4 && ghz <= 6.5) return (float)ghz;
                        }
                    }
                }
                catch { }
            }
            return baseCpuMaxMhz > 0 ? (baseCpuMaxMhz / 1000f) : 3.5f;
        }

        void CloseCpuFreqPdh()
        {
            if (pdhCpuQuery != IntPtr.Zero)
            {
                try { PdhCloseQuery(pdhCpuQuery); } catch { }
                pdhCpuQuery = IntPtr.Zero;
                pdhCpuPerfCounter = IntPtr.Zero;
                pdhCpuPrimed = false;
            }
        }

        void ClosePdh()
        {
            if (pdhQuery != IntPtr.Zero)
            {
                try { PdhCloseQuery(pdhQuery); } catch { }
                pdhQuery = IntPtr.Zero;
                pdhGpuCounter = IntPtr.Zero;
            }
        }

        void UpdateWallpaperGpuLoad()
        {
            if (GpuLoad < 12f)
            {
                WallpaperGpuLoad = 0f;
                return;
            }

            DateTime now = DateTime.UtcNow;
            if ((now - lastWallpaperPidScan).TotalSeconds >= 3.0)
            {
                lastWallpaperPidScan = now;
                wallpaperPids.Clear();
                var pidList = new List<int>();
                foreach (string name in WallpaperProcNames)
                {
                    try
                    {
                        foreach (Process p in Process.GetProcessesByName(name))
                        {
                            using (p)
                            {
                                if (wallpaperPids.Add(p.Id)) pidList.Add(p.Id);
                            }
                        }
                    }
                    catch { }
                }
                if (pidList.Count == 0)
                {
                    lastWallpaperPidSig = "";
                    WallpaperGpuLoad = 0f;
                    ClosePdh();
                    return;
                }
                pidList.Sort();
                string sig = string.Join(",", pidList);
                if (sig != lastWallpaperPidSig || pdhQuery == IntPtr.Zero)
                {
                    lastWallpaperPidSig = sig;
                    ClosePdh();
                    if (PdhOpenQuery(null, IntPtr.Zero, out pdhQuery) == 0 &&
                        PdhAddEnglishCounterW(pdhQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out pdhGpuCounter) == 0)
                    {
                        PdhCollectQueryData(pdhQuery); // baseline sample
                    }
                    else
                    {
                        ClosePdh();
                    }
                    lastWallpaperSample = now;
                    return;
                }
            }

            if (pdhQuery == IntPtr.Zero || wallpaperPids.Count == 0)
            {
                WallpaperGpuLoad = 0f;
                return;
            }
            if ((now - lastWallpaperSample).TotalMilliseconds < 200) return;
            lastWallpaperSample = now;

            if (PdhCollectQueryData(pdhQuery) != 0) return;
            uint sz = 0, cnt = 0;
            int rc = PdhGetFormattedCounterArrayW(pdhGpuCounter, 0x00000200, ref sz, ref cnt, IntPtr.Zero);
            if (sz == 0 || (rc != 0 && unchecked((uint)rc) != 0x800007D2U)) return;

            IntPtr mem = Marshal.AllocHGlobal((int)sz);
            try
            {
                if (PdhGetFormattedCounterArrayW(pdhGpuCounter, 0x00000200, ref sz, ref cnt, mem) != 0) return;
                int stride = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM));
                double sum = 0.0;
                for (uint i = 0; i < cnt; i++)
                {
                    var it = (PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(IntPtr.Add(mem, (int)(i * stride)), typeof(PDH_FMT_COUNTERVALUE_ITEM));
                    if (it.CStatus > 1 || it.doubleValue <= 0.0) continue;
                    string inst = Marshal.PtrToStringUni(it.szName);
                    if (string.IsNullOrEmpty(inst) || !inst.StartsWith("pid_", StringComparison.Ordinal)) continue;
                    int u = inst.IndexOf('_', 4);
                    if (u <= 4) continue;
                    int pid;
                    if (!int.TryParse(inst.Substring(4, u - 4), out pid) || !wallpaperPids.Contains(pid)) continue;
                    if (inst.IndexOf("engtype_Copy", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    sum += it.doubleValue;
                }
                WallpaperGpuLoad = (float)Math.Min(GpuLoad, Math.Min(100.0, sum));
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        public void InjectDemoGaming()
        {
            IsForegroundGame = true;
            GameTitle = "STARFALL ODYSSEY";
            GameStartTime = DateTime.UtcNow.AddMinutes(-62);
            GpuLoad = 46f;
            GpuTemp = 58f;
            GpuHotspot = 64f;
            GpuPower = 142f;
            CpuLoad = 37f;
            CpuTemp = 61f;
            CpuPower = 48f;
            CpuGhz = 4.45f;
            GameFps = 144f;
            FpsHistory.Clear();
            for (int i = 0; i < 60; i++)
            {
                double t = i / 59.0;
                float f = (float)(135.0 + 14.0 * Math.Sin(t * Math.PI * 3.2) + 5.0 * Math.Cos(t * 9.0));
                FpsHistory.Add(f);
            }
        }

        public void Dispose()
        {
            CloseCpuFreqPdh();
            ClosePdh();
            if (cachedTrofeoHandle != IntPtr.Zero)
            {
                try { CloseHandle(cachedTrofeoHandle); } catch { }
                cachedTrofeoHandle = IntPtr.Zero;
            }
            if (pawnHandle != IntPtr.Zero)
            {
                try { pawnio_close(pawnHandle); } catch { }
                pawnHandle = IntPtr.Zero;
            }
            if (adlCtx != IntPtr.Zero)
            {
                try { ADL2_Main_Control_Destroy(adlCtx); } catch { }
                adlCtx = IntPtr.Zero;
            }
        }
    }

    // =========================================================================
    // WINDOWS MEDIA SESSION READER (GSMTC: Title, Artist, Album, Timeline, Cover)
    // =========================================================================
    class MediaReader : IDisposable
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public bool IsPlaying;
        public TimeSpan Position = TimeSpan.Zero;
        public TimeSpan Duration = TimeSpan.Zero;
        public Bitmap CoverArt;
        public Bitmap BlurredBg;
        public string LastThumbError;
        public readonly object CoverLock = new object();
        public volatile bool MediaChangedWake;

        string lastTrackKey = "";
        string lastSourceApp = "";
        ulong loadedThumbHash = 0;
        bool hasLoadedThumb = false;
        int pendingThumbChecks = 0;
        DateTime nextThumbCheckUtc = DateTime.MinValue;
        volatile bool propsChangedSignal = false;
        GlobalSystemMediaTransportControlsSessionManager mgr;
        GlobalSystemMediaTransportControlsSession subscribedSession;

        static T WaitWinRT<T>(IAsyncOperation<T> op, int timeoutMs = 600)
        {
            int waited = 0;
            while (op.Status == AsyncStatus.Started && waited < timeoutMs)
            {
                Thread.Sleep(10);
                waited += 10;
            }
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        static T WaitWinRTProg<T, P>(IAsyncOperationWithProgress<T, P> op, int timeoutMs = 600)
        {
            int waited = 0;
            while (op.Status == AsyncStatus.Started && waited < timeoutMs)
            {
                Thread.Sleep(10);
                waited += 10;
            }
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            propsChangedSignal = true;
        }

        void EnsureSessionSubscription(GlobalSystemMediaTransportControlsSession session)
        {
            if (object.ReferenceEquals(session, subscribedSession)) return;
            if (subscribedSession != null)
            {
                try { subscribedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch { }
                subscribedSession = null;
            }
            if (session != null)
            {
                try
                {
                    session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    subscribedSession = session;
                }
                catch { }
            }
        }

        public void Update()
        {
            try
            {
                if (mgr == null)
                    mgr = WaitWinRT(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                if (mgr == null) return;

                var session = mgr.GetCurrentSession();
                EnsureSessionSubscription(session);
                if (session == null)
                {
                    IsPlaying = false;
                    return;
                }

                var info = session.GetPlaybackInfo();
                IsPlaying = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var timeline = session.GetTimelineProperties();
                if (timeline != null)
                {
                    Duration = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : TimeSpan.Zero;
                    TimeSpan pos = timeline.Position;
                    if (IsPlaying && timeline.LastUpdatedTime != DateTimeOffset.MinValue)
                    {
                        TimeSpan elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                        if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(10))
                            pos += elapsed;
                    }
                    if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
                    if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                    Position = pos;
                }

                var props = WaitWinRT(session.TryGetMediaPropertiesAsync());
                if (props != null)
                {
                    Title = props.Title ?? "";
                    Artist = props.Artist ?? "";
                    Album = props.AlbumTitle ?? "";
                    string appId = "";
                    try { appId = session.SourceAppUserModelId ?? ""; } catch { }

                    if (string.IsNullOrEmpty(Title))
                    {
                        if (!string.IsNullOrEmpty(lastTrackKey))
                        {
                            lastTrackKey = "";
                            ClearCover();
                        }
                        return;
                    }

                    string key = Title + "|" + Artist + "|" + Album + "|" + appId;
                    if (key != lastTrackKey || appId != lastSourceApp)
                    {
                        lastTrackKey = key;
                        lastSourceApp = appId;
                        pendingThumbChecks = 10; // re-check over ~5s in case player updates art after title
                        nextThumbCheckUtc = DateTime.UtcNow;
                        propsChangedSignal = false;
                        MediaChangedWake = true;
                    }
                    else if (propsChangedSignal)
                    {
                        propsChangedSignal = false;
                        if (pendingThumbChecks < 4) pendingThumbChecks = 4;
                        nextThumbCheckUtc = DateTime.UtcNow;
                    }

                    if (pendingThumbChecks > 0 && DateTime.UtcNow >= nextThumbCheckUtc)
                    {
                        pendingThumbChecks--;
                        nextThumbCheckUtc = DateTime.UtcNow.AddMilliseconds(500);
                        RefreshThumbnailIfChanged(props.Thumbnail);
                    }
                }
            }
            catch
            {
                EnsureSessionSubscription(null);
                mgr = null;
            }
        }

        void ClearCover()
        {
            lock (CoverLock)
            {
                if (CoverArt != null) { CoverArt.Dispose(); CoverArt = null; }
                if (BlurredBg != null) { BlurredBg.Dispose(); BlurredBg = null; }
                loadedThumbHash = 0;
                hasLoadedThumb = false;
            }
        }

        static ulong ComputeFnv1a64(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++)
            {
                h ^= data[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        void RefreshThumbnailIfChanged(IRandomAccessStreamReference thumbRef)
        {
            if (thumbRef == null)
            {
                ClearCover();
                LastThumbError = "thumbRef is null";
                return;
            }

            try
            {
                using (var stream = WaitWinRT(thumbRef.OpenReadAsync(), 1500))
                {
                    if (stream == null || stream.Size == 0)
                    {
                        ClearCover();
                        LastThumbError = "stream null or 0";
                        return;
                    }
                    uint sz = (uint)stream.Size;
                    var buf = new Windows.Storage.Streams.Buffer(sz);
                    var readBuf = WaitWinRTProg(stream.ReadAsync(buf, sz, InputStreamOptions.None), 1500);
                    if (readBuf == null) readBuf = buf;
                    using (var dr = DataReader.FromBuffer(readBuf))
                    {
                        byte[] bytes = new byte[readBuf.Length];
                        dr.ReadBytes(bytes);
                        if (bytes.Length == 0)
                        {
                            ClearCover();
                            return;
                        }
                        ulong hash = ComputeFnv1a64(bytes);
                        if (hasLoadedThumb && hash == loadedThumbHash) return;

                        using (var ms = new MemoryStream(bytes))
                        using (var raw = new Bitmap(ms))
                        {
                            var newCover = new Bitmap(raw, new Size(180, 180));
                            var newBg = CreateBlurredCyanBackdrop(raw, 480, 480);
                            lock (CoverLock)
                            {
                                if (CoverArt != null) CoverArt.Dispose();
                                if (BlurredBg != null) BlurredBg.Dispose();
                                CoverArt = newCover;
                                BlurredBg = newBg;
                                loadedThumbHash = hash;
                                hasLoadedThumb = true;
                            }
                            MediaChangedWake = true;
                            LastThumbError = "OK (" + bytes.Length + " bytes)";
                        }
                    }
                }
            }
            catch (Exception ex) { LastThumbError = ex.Message; }
        }

        static Bitmap CreateBlurredCyanBackdrop(Bitmap src, int w, int h)
        {
            using (var tiny = new Bitmap(24, 24))
            {
                using (var g = Graphics.FromImage(tiny))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, 24, 24));
                }
                var bg = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bg))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(tiny, new Rectangle(0, 0, w, h));
                    using (var dark = new SolidBrush(Color.FromArgb(238, 2, 10, 14)))
                        g.FillRectangle(dark, 0, 0, w, h);
                }
                return bg;
            }
        }

        public void Dispose()
        {
            EnsureSessionSubscription(null);
            ClearCover();
        }
    }

    // =========================================================================
    // WASAPI LOOPBACK AUDIO CAPTURE + CAVA SPECTRUM VISUALIZER
    // =========================================================================
    class WasapiCava : IDisposable
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            int Initialize(int ShareMode, int StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
            int GetBufferSize(out uint pNumBufferFrames);
            int GetStreamLatency(out long phnsLatency);
            int GetCurrentPadding(out uint pNumPaddingFrames);
            int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            int GetMixFormat(out IntPtr ppDeviceFormat);
            int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr eventHandle);
            int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
            int ReleaseBuffer(uint NumFramesRead);
            int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }

        public readonly int NumBars;
        public readonly float[] Bars;
        public readonly float[] Peaks;
        readonly float[] fallVelocity;
        readonly float[] ringBuffer = new float[2048];
        int ringPos;
        float smoothMax = 0.15f;
        public float PeakLevel { get; private set; }

        IAudioClient client;
        IAudioCaptureClient capture;
        int channels = 2;
        int sampleRate = 48000;

        public WasapiCava(int numBars)
        {
            NumBars = numBars;
            Bars = new float[numBars];
            Peaks = new float[numBars];
            fallVelocity = new float[numBars];
            InitWasapi();
        }

        void InitWasapi()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                IMMDevice device;
                enumerator.GetDefaultAudioEndpoint(0, 0, out device);
                Guid iidAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                object obj;
                device.Activate(ref iidAudioClient, 23, IntPtr.Zero, out obj);
                client = (IAudioClient)obj;
                IntPtr fmtPtr;
                client.GetMixFormat(out fmtPtr);
                channels = Marshal.ReadInt16(fmtPtr, 2);
                sampleRate = Marshal.ReadInt32(fmtPtr, 4);
                client.Initialize(0, 0x00020000, 1000000, 0, fmtPtr, IntPtr.Zero);
                Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
                object capObj;
                client.GetService(ref iidCapture, out capObj);
                capture = (IAudioCaptureClient)capObj;
                client.Start();
            }
            catch
            {
                client = null;
                capture = null;
            }
        }

        public void Update()
        {
            if (capture == null)
            {
                InitWasapi();
                if (capture == null) return;
            }

            float maxAmp = 0f;
            try
            {
                uint packetSize;
                capture.GetNextPacketSize(out packetSize);
                while (packetSize > 0)
                {
                    IntPtr dataPtr;
                    uint numFrames, flags;
                    ulong devPos, qpcPos;
                    capture.GetBuffer(out dataPtr, out numFrames, out flags, out devPos, out qpcPos);
                    if ((flags & 2) == 0 && numFrames > 0)
                    {
                        int count = (int)numFrames * channels;
                        float[] samples = new float[count];
                        Marshal.Copy(dataPtr, samples, 0, count);
                        for (int i = 0; i < (int)numFrames; i++)
                        {
                            float mono = samples[i * channels];
                            if (channels > 1) mono = 0.5f * (mono + samples[i * channels + 1]);
                            float a = Math.Abs(mono);
                            if (a > maxAmp) maxAmp = a;
                            ringBuffer[ringPos] = mono;
                            ringPos = (ringPos + 1) & 2047;
                        }
                    }
                    else if (numFrames > 0)
                    {
                        for (int i = 0; i < (int)numFrames; i++)
                        {
                            ringBuffer[ringPos] = 0f;
                            ringPos = (ringPos + 1) & 2047;
                        }
                    }
                    capture.ReleaseBuffer(numFrames);
                    capture.GetNextPacketSize(out packetSize);
                }
            }
            catch
            {
                capture = null;
                return;
            }

            PeakLevel = maxAmp * 0.6f + PeakLevel * 0.4f;
            ComputeCavaSpectrum();
        }

        void ComputeCavaSpectrum()
        {
            const int N = 1024;
            double[] re = new double[N];
            double[] im = new double[N];
            for (int i = 0; i < N; i++)
            {
                float s = ringBuffer[(ringPos - N + i) & 2047];
                double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (N - 1)));
                re[i] = s * hann;
            }

            Fft(re, im, N);

            double fMin = 42.0;
            double fMax = 14000.0;
            float framePeak = 0.01f;
            float[] rawTarget = new float[NumBars];

            for (int b = 0; b < NumBars; b++)
            {
                double t0 = (double)b / NumBars;
                double t1 = (double)(b + 1) / NumBars;
                double freq0 = fMin * Math.Pow(fMax / fMin, t0);
                double freq1 = fMin * Math.Pow(fMax / fMin, t1);
                int bin0 = Math.Max(1, (int)Math.Floor(freq0 * N / sampleRate));
                int bin1 = Math.Min(N / 2 - 1, Math.Max(bin0, (int)Math.Ceiling(freq1 * N / sampleRate)));

                double sum = 0;
                for (int k = bin0; k <= bin1; k++)
                {
                    double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    if (mag > sum) sum = mag;
                }

                double eq = Math.Pow(freq0 / 100.0, 0.32) * (b < 8 ? 1.25 : 1.0);
                float val = (float)(sum * eq);
                rawTarget[b] = val;
                if (val > framePeak) framePeak = val;
            }

            float[] smoothed = new float[NumBars];
            for (int b = 0; b < NumBars; b++)
            {
                float v = rawTarget[b] * 0.65f;
                if (b > 0) v += rawTarget[b - 1] * 0.175f;
                if (b + 1 < NumBars) v += rawTarget[b + 1] * 0.175f;
                smoothed[b] = v;
            }

            if (framePeak > smoothMax) smoothMax = smoothMax * 0.7f + framePeak * 0.3f;
            else smoothMax = Math.Max(0.08f, smoothMax * 0.985f + framePeak * 0.015f);

            for (int b = 0; b < NumBars; b++)
            {
                float norm = Math.Min(1.0f, smoothed[b] / (smoothMax * 0.92f));
                if (PeakLevel < 0.003f) norm = 0f;

                if (norm >= Bars[b])
                {
                    Bars[b] = Bars[b] * 0.30f + norm * 0.70f;
                    fallVelocity[b] = 0.018f;
                }
                else
                {
                    Bars[b] = Math.Max(0f, Bars[b] - fallVelocity[b]);
                    fallVelocity[b] = Math.Min(0.09f, fallVelocity[b] + 0.0085f);
                }

                if (Bars[b] >= Peaks[b]) Peaks[b] = Bars[b];
                else Peaks[b] = Math.Max(0f, Peaks[b] - 0.018f);
            }
        }

        public void InjectDemoSpectrum()
        {
            for (int i = 0; i < NumBars; i++)
            {
                double x = (double)i / NumBars;
                double wave = Math.Exp(-x * 1.45) * (0.62 + 0.38 * Math.Sin(x * 19.0));
                Bars[i] = (float)Math.Max(0.06, Math.Min(0.98, wave));
                Peaks[i] = Math.Min(1.0f, Bars[i] + 0.05f);
            }
        }

        static void Fft(double[] re, double[] im, int n)
        {
            int j = 0;
            for (int i = 0; i < n; i++)
            {
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
                int m = n >> 1;
                while (m >= 1 && j >= m) { j -= m; m >>= 1; }
                j += m;
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wlenRe = Math.Cos(ang), wlenIm = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double wRe = 1.0, wIm = 0.0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int u = i + k, v = i + k + half;
                        double tRe = re[v] * wRe - im[v] * wIm;
                        double tIm = re[v] * wIm + im[v] * wRe;
                        re[v] = re[u] - tRe; im[v] = im[u] - tIm;
                        re[u] += tRe; im[u] += tIm;
                        double nextWRe = wRe * wlenRe - wIm * wlenIm;
                        wIm = wRe * wlenIm + wIm * wlenRe;
                        wRe = nextWRe;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (client != null)
            {
                try { client.Stop(); } catch { }
                client = null;
            }
        }
    }

    // =========================================================================
    // ANIMATED CYAN MATRIX RAIN + SMOOTH TYPOGRAPHY RENDERER FOR 480x480 PANEL
    // =========================================================================
    sealed class MatrixRain
    {
        sealed class Drop
        {
            public int X;
            public float HeadY;
            public float Speed;
            public int TrailLen;
            public float Intensity;
            public char[] Chars;
        }

        const string Charset = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ#$%*+=<>";
        readonly int width;
        readonly int height;
        readonly int cellStep;
        readonly Drop[] drops;
        readonly Random rng = new Random(2026);
        readonly object sync = new object();
        long lastTicks;
        double shimmerAccum;

        public MatrixRain(int w, int h, int colSpacing)
        {
            width = w;
            height = h;
            cellStep = 21;
            int cols = Math.Max(1, (w - 24) / colSpacing);
            drops = new Drop[cols];

            for (int c = 0; c < cols; c++)
            {
                int trail = rng.Next(7, 14);
                char[] chars = new char[trail + 1];
                for (int k = 0; k < chars.Length; k++)
                    chars[k] = Charset[rng.Next(Charset.Length)];

                // Spread streams evenly across the full height so frame 0 / previews look rich
                float startY = (float)((c * 67 + rng.Next(0, 120)) % (h + 150)) + 40f;
                drops[c] = new Drop
                {
                    X = 12 + c * colSpacing + (rng.Next(3) - 1),
                    HeadY = startY,
                    Speed = 95f + (float)rng.NextDouble() * 110f,
                    TrailLen = trail,
                    Intensity = 0.68f + (float)rng.NextDouble() * 0.32f,
                    Chars = chars
                };
            }
        }

        public void Render(Graphics g, Color primary, Color iceWhite, float brightnessScale)
        {
            long nowTicks = Stopwatch.GetTimestamp();
            Font glyphFont = Renderer.GetFont("Cascadia Mono", 20f, FontStyle.Bold);

            lock (sync)
            {
                float dt = 0f;
                if (lastTicks != 0)
                {
                    double sec = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
                    dt = (float)Math.Max(0.0, Math.Min(0.12, sec));
                }
                lastTicks = nowTicks;

                if (dt > 0f)
                {
                    shimmerAccum += dt;
                    bool doShimmer = shimmerAccum >= 0.085;
                    if (doShimmer) shimmerAccum = 0.0;

                    for (int i = 0; i < drops.Length; i++)
                    {
                        Drop d = drops[i];
                        d.HeadY += d.Speed * dt;
                        float tailBottom = d.HeadY - (d.TrailLen + 1) * cellStep;
                        if (tailBottom > height + 16)
                        {
                            d.HeadY = -(float)rng.Next(10, 110);
                            d.Speed = 95f + (float)rng.NextDouble() * 110f;
                            d.Intensity = 0.68f + (float)rng.NextDouble() * 0.32f;
                        }
                        if (doShimmer && rng.NextDouble() < 0.45)
                        {
                            d.Chars[0] = Charset[rng.Next(Charset.Length)];
                            d.Chars[rng.Next(d.Chars.Length)] = Charset[rng.Next(Charset.Length)];
                        }
                    }
                }

                GraphicsState mState = g.Save();
                g.SetClip(new Rectangle(8, 8, width - 16, height - 16));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var br = new SolidBrush(primary))
                {
                    for (int i = 0; i < drops.Length; i++)
                    {
                        Drop d = drops[i];
                        int maxK = d.TrailLen;
                        for (int k = 0; k <= maxK; k++)
                        {
                            float cy = d.HeadY - k * cellStep;
                            if (cy < -cellStep || cy > height + 4) continue;

                            float norm = 1.0f - (k / (float)(maxK + 1));
                            float fade = (float)Math.Pow(norm, 1.2) * d.Intensity * brightnessScale;
                            int alpha = Math.Max(0, Math.Min(255, (int)(245f * fade)));
                            if (alpha < 12) continue;

                            if (k == 0)
                            {
                                br.Color = Color.FromArgb(Math.Max(0, Math.Min(255, (int)(110f * d.Intensity * brightnessScale))), primary);
                                g.DrawString(d.Chars[0].ToString(), glyphFont, br, d.X + 1.2f, cy + 1.2f, Renderer.TypoFormat);
                                br.Color = Color.FromArgb(Math.Max(0, Math.Min(255, (int)(248f * d.Intensity * brightnessScale))), iceWhite);
                                g.DrawString(d.Chars[0].ToString(), glyphFont, br, d.X, cy, Renderer.TypoFormat);
                            }
                            else
                            {
                                int r = (int)(primary.R * 0.45f);
                                int gr = (int)(primary.G * (0.72f + 0.28f * norm));
                                int b = (int)(primary.B * (0.78f + 0.22f * norm));
                                br.Color = Color.FromArgb(alpha, r, gr, b);
                                g.DrawString(d.Chars[k % d.Chars.Length].ToString(), glyphFont, br, d.X, cy, Renderer.TypoFormat);
                            }
                        }
                    }
                }
                g.Restore(mState);
            }
        }
    }

    static class Renderer
    {
        static readonly Color IceWhite = Color.FromArgb(230, 255, 255);
        static readonly Color IceDim = Color.FromArgb(158, 216, 230);
        static readonly Color DarkBase = Color.FromArgb(3, 9, 13);
        static readonly MatrixRain Matrix = new MatrixRain(480, 480, 23);
        static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        static readonly object FontLock = new object();
        public static readonly StringFormat TypoFormat = CreateTypoFormat();

        static StringFormat CreateTypoFormat()
        {
            var sf = new StringFormat(StringFormat.GenericTypographic);
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            sf.Trimming = StringTrimming.None;
            return sf;
        }

        public static Font GetFont(string family, float pixelSize, FontStyle style = FontStyle.Regular)
        {
            string key = family + "|" + pixelSize.ToString("0.0", CultureInfo.InvariantCulture) + "|" + (int)style;
            lock (FontLock)
            {
                Font f;
                if (FontCache.TryGetValue(key, out f)) return f;
                try { f = new Font(family, pixelSize, style, GraphicsUnit.Pixel); }
                catch
                {
                    try { f = new Font("Segoe UI", pixelSize, style, GraphicsUnit.Pixel); }
                    catch { f = new Font(FontFamily.GenericSansSerif, pixelSize, style, GraphicsUnit.Pixel); }
                }
                FontCache[key] = f;
                return f;
            }
        }

        static SizeF MeasureSmoothText(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return g.MeasureString(text, font, new PointF(0f, 0f), TypoFormat);
        }

        static void DrawSmoothText(Graphics g, string text, Font font, Color color, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var br = new SolidBrush(color))
                g.DrawString(text, font, br, x, y, TypoFormat);
        }

        static void DrawSmoothTextCentered(Graphics g, string text, Font font, Color color, float centerX, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            SizeF sz = MeasureSmoothText(g, text, font);
            DrawSmoothText(g, text, font, color, centerX - sz.Width * 0.5f, y);
        }

        static void DrawSmoothTextRight(Graphics g, string text, Font font, Color color, float rightX, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            SizeF sz = MeasureSmoothText(g, text, font);
            DrawSmoothText(g, text, font, color, rightX - sz.Width, y);
        }

        static string lastMarqueeTitle = "";
        static DateTime marqueeStart = DateTime.UtcNow;

        static void DrawSmoothMarquee(Graphics g, string text, Font font, Color color, int x, int y, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return;
            SizeF sz = MeasureSmoothText(g, text, font);
            int lineH = Math.Max((int)Math.Ceiling(font.GetHeight(g)), (int)Math.Ceiling(sz.Height)) + 4;
            if (sz.Width <= maxWidth)
            {
                GraphicsState stFit = g.Save();
                g.SetClip(new Rectangle(x, y - 2, maxWidth, lineH + 2));
                DrawSmoothText(g, text, font, color, x, y);
                g.Restore(stFit);
                return;
            }

            string loopStr = text + "     ";
            float loopW = Math.Max(1f, MeasureSmoothText(g, loopStr, font).Width);
            double elapsed = (DateTime.UtcNow - marqueeStart).TotalSeconds;
            float offsetPx = (float)((elapsed * 42.0) % loopW);

            GraphicsState st = g.Save();
            g.SetClip(new Rectangle(x, y - 2, maxWidth, lineH + 2));
            DrawSmoothText(g, loopStr, font, color, x - offsetPx, y);
            DrawSmoothText(g, loopStr, font, color, x - offsetPx + loopW, y);
            g.Restore(st);
        }

        static void DrawHudCard(Graphics g, Rectangle rect, int radius, Color accent, int fillAlpha = 158)
        {
            using (GraphicsPath gp = RoundedRect(rect, radius))
            {
                using (var br = new SolidBrush(Color.FromArgb(fillAlpha, 4, 15, 20))) g.FillPath(br, gp);
                using (var glowPen = new Pen(Color.FromArgb(30, accent), 3f)) g.DrawPath(glowPen, gp);
                using (var pen = new Pen(Color.FromArgb(168, accent), 1.4f)) g.DrawPath(pen, gp);
            }
            using (var sheenPen = new Pen(Color.FromArgb(40, IceWhite), 1f))
                g.DrawLine(sheenPen, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
        }

        static void DrawProgressBar(Graphics g, Rectangle rect, float percent, Color trackColor, Color fillStart, Color fillEnd, int radius)
        {
            using (GraphicsPath trackGp = RoundedRect(rect, radius))
            using (var trkBr = new SolidBrush(trackColor))
                g.FillPath(trkBr, trackGp);

            float clamped = Math.Max(0f, Math.Min(100f, percent));
            int fillW = (int)Math.Round(rect.Width * clamped / 100.0);
            if (fillW <= 0) return;

            GraphicsState st = g.Save();
            using (GraphicsPath clipGp = RoundedRect(rect, radius))
            {
                g.SetClip(clipGp);
                g.SetClip(new Rectangle(rect.X, rect.Y, fillW, rect.Height), CombineMode.Intersect);
                using (var fillBr = new LinearGradientBrush(rect, fillStart, fillEnd, 0f))
                    g.FillRectangle(fillBr, rect.X, rect.Y, fillW, rect.Height);
            }
            g.Restore(st);
        }

        static void DrawWithBlur(Graphics g, Bitmap src, int w, int h, float blurAmount, float alpha)
        {
            float clampedAlpha = Math.Max(0f, Math.Min(1f, alpha));
            if (clampedAlpha <= 0.01f) return;

            float blur = Math.Max(0f, Math.Min(1f, blurAmount));
            Bitmap drawSrc = src;
            Bitmap downsampled = null;
            try
            {
                if (blur > 0.03f)
                {
                    // Progressive bilinear downsample produces a smooth frosted glass blur in <0.4ms
                    float scale = Math.Max(0.10f, 1.0f - blur * 0.86f);
                    int bw = Math.Max(28, (int)Math.Round(w * scale));
                    int bh = Math.Max(28, (int)Math.Round(h * scale));
                    downsampled = new Bitmap(bw, bh, PixelFormat.Format24bppRgb);
                    using (var bg = Graphics.FromImage(downsampled))
                    {
                        bg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        bg.DrawImage(src, 0, 0, bw, bh);
                    }
                    drawSrc = downsampled;
                }

                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (clampedAlpha >= 0.995f)
                {
                    g.DrawImage(drawSrc, 0, 0, w, h);
                }
                else
                {
                    var cm = new ColorMatrix { Matrix33 = clampedAlpha };
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        g.DrawImage(drawSrc, new Rectangle(0, 0, w, h), 0, 0, drawSrc.Width, drawSrc.Height, GraphicsUnit.Pixel, attr);
                    }
                }
            }
            finally
            {
                if (downsampled != null) downsampled.Dispose();
            }
        }

        public static Bitmap BlendFrames(Bitmap from, Bitmap to, float progress)
        {
            float t = Math.Max(0f, Math.Min(1f, progress));
            float eased = t * t * (3f - 2f * t);
            int w = to.Width, h = to.Height;

            // Outgoing UI softens into frosted blur as incoming UI sharpens out of blur
            float outBlur = Math.Min(1f, eased * 1.15f);
            float inBlur = Math.Max(0f, (1f - eased) * 0.95f);

            var blended = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(blended))
            {
                g.Clear(DarkBase);
                g.CompositingMode = CompositingMode.SourceOver;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawWithBlur(g, from, w, h, outBlur, 1.0f - eased * 0.85f);
                DrawWithBlur(g, to, w, h, inBlur, eased);
            }
            return blended;
        }

        public static Bitmap RenderFrame(int w, int h, Config cfg, HardwareSensors sensors, MediaReader media, WasapiCava audio, ScreenMode mode)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                if (mode == ScreenMode.Gaming)
                    RenderGamingScreen(g, w, h, cfg.ThemeColor, cfg, sensors);
                else if (mode == ScreenMode.Music)
                    RenderMusicScreen(g, w, h, cfg.ThemeColor, sensors, media, audio);
                else
                    RenderDashboardScreen(g, w, h, cfg.ThemeColor, cfg, sensors);

                if (cfg.Brightness < 100)
                {
                    int alpha = (int)((100 - cfg.Brightness) * 2.55f);
                    using (var dim = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                        g.FillRectangle(dim, 0, 0, w, h);
                }
            }
            return bmp;
        }

        static void DrawOuterFrame(Graphics g, int w, int h, Color cyan)
        {
            using (var glowPen = new Pen(Color.FromArgb(35, cyan), 4f))
                g.DrawRectangle(glowPen, 4, 4, w - 8, h - 8);
            using (var borderPen = new Pen(Color.FromArgb(168, cyan), 1.5f))
                g.DrawRectangle(borderPen, 6, 6, w - 12, h - 12);
        }

        // ---------------------------------------------------------------------
        // MODE 3: #00FFFF + #E6FFFF CYAN GAMING TELEMETRY SCREEN
        // ---------------------------------------------------------------------
        static void RenderGamingScreen(Graphics g, int w, int h, Color cyan, Config cfg, HardwareSensors sensors)
        {
            Color cyanDim = Color.FromArgb(0, 175, 195);
            Color barTrack = Color.FromArgb(14, 52, 62);

            g.Clear(DarkBase);
            Matrix.Render(g, cyan, IceWhite, 0.85f);
            DrawOuterFrame(g, w, h, cyan);

            // 1. Top Header Glass Strip (Game Name, Clock)
            DrawHudCard(g, new Rectangle(16, 10, 448, 32), 8, cyan, 158);
            string gameName = string.IsNullOrEmpty(sensors.GameTitle) ? "Active Game" : sensors.GameTitle;
            string clockStr = cfg.ClockFormat == "24h"
                ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                : DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant();

            Font clkFont = GetFont("Bahnschrift SemiBold", 15f);
            Font titleFont = GetFont("Bahnschrift SemiBold", 20f);
            int cw = (int)Math.Ceiling(MeasureSmoothText(g, clockStr, clkFont).Width);
            DrawSmoothTextRight(g, clockStr, clkFont, cyan, w - 26, 17f);
            DrawSmoothMarquee(g, gameName, titleFont, IceWhite, 26, 14, w - 62 - cw);

            // 2. Hero FPS & 60s Telemetry Curve Card (y=48..242)
            Rectangle heroCard = new Rectangle(16, 48, 448, 194);
            DrawHudCard(g, heroCard, 12, cyan, 158);

            int gx = 26, gy = 122, gw = 320, gh = 110;
            using (var guidePen = new Pen(Color.FromArgb(26, cyan), 1f))
            {
                g.DrawLine(guidePen, gx, gy + gh / 3, gx + gw, gy + gh / 3);
                g.DrawLine(guidePen, gx, gy + (gh * 2) / 3, gx + gw, gy + (gh * 2) / 3);
            }
            DrawSparklineRange(g, gx, gy, gw, gh, sensors.FpsHistory, 0f, 210f, cyan, 65);

            Font axisFont = GetFont("Bahnschrift", 12f);
            DrawSmoothText(g, "-60s", axisFont, cyanDim, gx, gy + gh - 15);
            DrawSmoothTextRight(g, "NOW", axisFont, cyanDim, gx + gw, gy + gh - 15);

            // Hero FPS counter (#E6FFFF with subtle cyan aura)
            int fpsVal = (int)Math.Round(sensors.GameFps);
            string fpsStr = fpsVal > 0 ? fpsVal.ToString(CultureInfo.InvariantCulture) : "--";
            Font fpsFont = GetFont("Bahnschrift SemiBold", 54f);
            DrawSmoothText(g, fpsStr, fpsFont, Color.FromArgb(45, cyan), 28f, 55f);
            DrawSmoothText(g, fpsStr, fpsFont, IceWhite, 26f, 53f);

            int fpsW = (int)Math.Ceiling(MeasureSmoothText(g, fpsStr, fpsFont).Width);
            DrawSmoothText(g, "FPS", GetFont("Bahnschrift SemiBold", 20f), cyan, 34f + fpsW, 61f);
            string msStr = fpsVal > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} ms", 1000f / fpsVal) : "-- ms";
            DrawSmoothText(g, msStr, GetFont("Bahnschrift", 14f), IceDim, 34f + fpsW, 86f);

            using (var sepPen = new Pen(Color.FromArgb(55, cyan), 1f))
                g.DrawLine(sepPen, 358, 58, 358, 232);

            int minFps = int.MaxValue, maxFps = 0, sumFps = 0, cntFps = 0;
            foreach (float f in sensors.FpsHistory)
            {
                if (f > 1f)
                {
                    int iv = (int)Math.Round(f);
                    if (iv < minFps) minFps = iv;
                    if (iv > maxFps) maxFps = iv;
                    sumFps += iv;
                    cntFps++;
                }
            }
            if (cntFps == 0 && fpsVal > 0) { minFps = maxFps = sumFps = fpsVal; cntFps = 1; }
            int avgFps = cntFps > 0 ? (sumFps / cntFps) : 0;

            Font statValFont = GetFont("Bahnschrift SemiBold", 22f);
            int sx = 372;
            DrawSmoothText(g, "AVG", axisFont, cyanDim, sx, 56f);
            DrawSmoothText(g, avgFps > 0 ? avgFps.ToString(CultureInfo.InvariantCulture) : "--", statValFont, IceWhite, sx, 71f);

            DrawSmoothText(g, "MIN", axisFont, cyanDim, sx, 114f);
            DrawSmoothText(g, cntFps > 0 ? minFps.ToString(CultureInfo.InvariantCulture) : "--", statValFont, IceWhite, sx, 129f);

            DrawSmoothText(g, "MAX", axisFont, cyanDim, sx, 172f);
            DrawSmoothText(g, cntFps > 0 ? maxFps.ToString(CultureInfo.InvariantCulture) : "--", statValFont, IceWhite, sx, 187f);

            // 3. Bottom Hardware Telemetry Rows (GPU, CPU, MEM)
            DrawTelemetryRow(g, new Rectangle(16, 250, 448, 66),
                "GPU", string.Format(CultureInfo.InvariantCulture, "{0:0}%", sensors.GpuLoad), sensors.GpuLoad,
                string.Format(CultureInfo.InvariantCulture, "{0:0}°C   HOT {1:0}°C   {2:0} W", sensors.GpuTemp, sensors.GpuHotspot, sensors.GpuPower),
                cyan, barTrack);

            string cpuDetails = sensors.CpuTemp > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0}°C   {1:0} W   {2:0.00} GHz", sensors.CpuTemp, sensors.CpuPower, sensors.CpuGhz)
                : string.Format(CultureInfo.InvariantCulture, "Ryzen 5 5600   {0:0.00} GHz", sensors.CpuGhz);

            DrawTelemetryRow(g, new Rectangle(16, 324, 448, 66),
                "CPU", string.Format(CultureInfo.InvariantCulture, "{0:0}%", sensors.CpuLoad), sensors.CpuLoad,
                cpuDetails, cyan, barTrack);

            float freeGb = Math.Max(0f, sensors.RamTotalGb - sensors.RamUsedGb);
            DrawTelemetryRow(g, new Rectangle(16, 398, 448, 66),
                "MEM", string.Format(CultureInfo.InvariantCulture, "{0:0}%", sensors.RamPercent), sensors.RamPercent,
                string.Format(CultureInfo.InvariantCulture, "USED {0:0.0} GB   FREE {1:0.0} GB", sensors.RamUsedGb, freeGb),
                cyan, barTrack);
        }

        static void DrawTelemetryRow(Graphics g, Rectangle row, string label, string bigRight, float percent, string details,
            Color cyan, Color barTrack)
        {
            DrawHudCard(g, row, 10, cyan, 158);
            DrawSmoothText(g, label, GetFont("Bahnschrift SemiBold", 20f), cyan, row.X + 14, row.Y + 6);
            DrawSmoothTextRight(g, bigRight, GetFont("Bahnschrift SemiBold", 22f), IceWhite, row.Right - 14, row.Y + 5);

            DrawProgressBar(g, new Rectangle(row.X + 14, row.Y + 32, row.Width - 28, 7), percent,
                barTrack, Color.FromArgb(0, 175, 200), cyan, 3);

            DrawSmoothText(g, details, GetFont("Bahnschrift", 13f), IceDim, row.X + 14, row.Y + 43);
        }

        // ---------------------------------------------------------------------
        // MODE 1: NOW-PLAYING MUSIC + MATRIX RAIN + CAVA PEAKS VISUALIZER
        // ---------------------------------------------------------------------
        static void RenderMusicScreen(Graphics g, int w, int h, Color cyan, HardwareSensors sensors, MediaReader media, WasapiCava audio)
        {
            Color trackBg = Color.FromArgb(14, 52, 60);

            g.Clear(DarkBase);

            // Softly tint blurred album art beneath the bright Matrix rain
            lock (media.CoverLock)
            {
                if (media.BlurredBg != null)
                {
                    var cm = new ColorMatrix { Matrix33 = 0.34f };
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        g.DrawImage(media.BlurredBg, new Rectangle(0, 0, w, h),
                            0, 0, media.BlurredBg.Width, media.BlurredBg.Height, GraphicsUnit.Pixel, attr);
                    }
                }
            }

            Matrix.Render(g, cyan, IceWhite, 0.94f);
            DrawOuterFrame(g, w, h, cyan);

            // Top-left cover art card
            Rectangle artRect = new Rectangle(16, 16, 148, 148);
            for (int glow = 8; glow >= 2; glow -= 2)
            {
                Rectangle gr = Rectangle.Inflate(artRect, glow, glow);
                using (GraphicsPath gp = RoundedRect(gr, 14 + glow / 2))
                using (var pen = new Pen(Color.FromArgb(26 - glow * 2, cyan), 3f))
                    g.DrawPath(pen, gp);
            }

            using (GraphicsPath clipPath = RoundedRect(artRect, 14))
            {
                GraphicsState state = g.Save();
                g.SetClip(clipPath);
                bool drewCover = false;
                lock (media.CoverLock)
                {
                    if (media.CoverArt != null)
                    {
                        g.DrawImage(media.CoverArt, artRect);
                        drewCover = true;
                    }
                }
                if (!drewCover)
                {
                    using (var cardBg = new LinearGradientBrush(artRect, Color.FromArgb(8, 95, 102), Color.FromArgb(4, 38, 42), 135f))
                        g.FillRectangle(cardBg, artRect);
                    int cx = artRect.X + artRect.Width / 2;
                    int cy = artRect.Y + artRect.Height / 2;
                    using (var vinylBr = new SolidBrush(Color.FromArgb(20, 24, 26)))
                        g.FillEllipse(vinylBr, cx - 56, cy - 56, 112, 112);
                    using (var groovePen = new Pen(Color.FromArgb(34, 42, 46), 1f))
                    {
                        for (int r = 24; r <= 50; r += 5)
                            g.DrawEllipse(groovePen, cx - r, cy - r, r * 2, r * 2);
                    }
                    using (var lblBr = new SolidBrush(cyan))
                        g.FillEllipse(lblBr, cx - 18, cy - 18, 36, 36);
                    using (var holeBr = new SolidBrush(Color.Black))
                        g.FillEllipse(holeBr, cx - 4, cy - 4, 8, 8);
                }
                g.Restore(state);

                using (var borderPen = new Pen(cyan, 2.2f))
                    g.DrawPath(borderPen, clipPath);
            }

            // Track metadata glass card
            DrawHudCard(g, new Rectangle(174, 16, 290, 148), 12, cyan, 152);
            int tx = 186;
            int maxTw = 452 - tx;

            DrawSmoothText(g, "NOW PLAYING", GetFont("Bahnschrift SemiBold", 12f), cyan, tx, 26f);

            string title = string.IsNullOrEmpty(media.Title) ? "No Track Playing" : media.Title;
            string artist = string.IsNullOrEmpty(media.Artist) ? "Unknown Artist" : media.Artist;
            string album = media.Album ?? "";

            if (title != lastMarqueeTitle)
            {
                lastMarqueeTitle = title;
                marqueeStart = DateTime.UtcNow;
            }

            DrawSmoothMarquee(g, title, GetFont("Bahnschrift SemiBold", 26f), IceWhite, tx, 48, maxTw);
            DrawSmoothMarquee(g, artist, GetFont("Bahnschrift SemiBold", 18f), cyan, tx, 86, maxTw);
            if (!string.IsNullOrEmpty(album))
                DrawSmoothMarquee(g, album, GetFont("Bahnschrift", 14f), IceDim, tx, 118, maxTw);

            // Playback progress bar & timestamps
            int barX = 16, barY = 178, barW = 448, barH = 6;
            double prog = 0.0;
            if (media.Duration.TotalSeconds > 0.5)
                prog = Math.Max(0.0, Math.Min(1.0, media.Position.TotalSeconds / media.Duration.TotalSeconds));

            DrawProgressBar(g, new Rectangle(barX, barY, barW, barH), (float)(prog * 100.0),
                trackBg, Color.FromArgb(0, 180, 210), IceWhite, 3);

            int fillW = (int)Math.Round(barW * prog);
            int knobX = barX + fillW;
            int knobY = barY + barH / 2;
            using (var glowBr = new SolidBrush(Color.FromArgb(75, cyan)))
                g.FillEllipse(glowBr, knobX - 10, knobY - 10, 20, 20);
            using (var knobBr = new SolidBrush(IceWhite))
                g.FillEllipse(knobBr, knobX - 5, knobY - 5, 10, 10);

            string posStr = FormatTime(media.Position);
            string durStr = FormatTime(media.Duration);
            Font timeFont = GetFont("Bahnschrift SemiBold", 16f);
            Color textShadow = Color.FromArgb(200, 2, 8, 12);
            DrawSmoothText(g, posStr, timeFont, textShadow, barX + 1.5f, barY + 13.5f);
            DrawSmoothText(g, posStr, timeFont, IceWhite, barX, barY + 12f);
            DrawSmoothTextRight(g, durStr, timeFont, textShadow, barX + barW + 1.5f, barY + 13.5f);
            DrawSmoothTextRight(g, durStr, timeFont, IceWhite, barX + barW, barY + 12f);

            // CAVA Visualizer glass container with floating #E6FFFF peak caps
            Rectangle specCard = new Rectangle(14, 216, 452, 224);
            DrawHudCard(g, specCard, 12, cyan, 144);

            float specX = 22f, specY = 224f, specW = 436f, specH = 208f;
            int nBars = audio.NumBars;
            float pitch = specW / Math.Max(1, nBars);
            float bw = Math.Max(3f, pitch - 1.8f);

            using (var barGrad = new LinearGradientBrush(
                new RectangleF(specX, specY, specW, specH),
                IceWhite, Color.FromArgb(0, 145, 165), 90f))
            using (var peakBr = new SolidBrush(IceWhite))
            {
                var blend = new ColorBlend(3)
                {
                    Colors = new[] { Color.FromArgb(145, 255, 255), cyan, Color.FromArgb(0, 115, 132) },
                    Positions = new[] { 0.0f, 0.25f, 1.0f }
                };
                barGrad.InterpolationColors = blend;

                for (int i = 0; i < nBars; i++)
                {
                    float bx = specX + i * pitch;
                    float bh = Math.Max(3f, audio.Bars[i] * (specH - 8f));
                    float by = specY + specH - bh;
                    g.FillRectangle(barGrad, bx, by, bw, bh);

                    float peakVal = Math.Max(audio.Bars[i], audio.Peaks[i]);
                    if (peakVal > 0.02f)
                    {
                        float peakH = Math.Max(3f, peakVal * (specH - 8f));
                        float peakY = Math.Max(specY, specY + specH - peakH - 3f);
                        g.FillRectangle(peakBr, bx, peakY, bw, 3f);
                    }
                }
            }

            string footer = sensors.CpuTemp > 0
                ? string.Format(CultureInfo.InvariantCulture, "CPU {0:0}% {1:0}°C   ·   GPU {2:0}% {3:0}°C   ·   {4:0.0} GB",
                    sensors.CpuLoad, sensors.CpuTemp, sensors.GpuLoad, sensors.GpuTemp, sensors.RamUsedGb)
                : string.Format(CultureInfo.InvariantCulture, "CPU {0:0}%   ·   GPU {1:0}% {2:0}°C   ·   {3:0.0}/{4:0} GB",
                    sensors.CpuLoad, sensors.GpuLoad, sensors.GpuTemp, sensors.RamUsedGb, sensors.RamTotalGb);
            Font footFont = GetFont("Bahnschrift SemiBold", 14f);
            using (GraphicsPath fGp = RoundedRect(new Rectangle(24, 444, 432, 24), 7))
            using (var fBr = new SolidBrush(Color.FromArgb(165, 3, 12, 16)))
                g.FillPath(fBr, fGp);
            DrawSmoothTextCentered(g, footer, footFont, IceDim, w * 0.5f, 448f);
        }

        // ---------------------------------------------------------------------
        // MODE 2: #00FFFF + #E6FFFF CYAN SYSTEM DASHBOARD (480x480 Square)
        // ---------------------------------------------------------------------
        static void RenderDashboardScreen(Graphics g, int w, int h, Color cyan, Config cfg, HardwareSensors sensors)
        {
            Color ringTrack = Color.FromArgb(14, 52, 62);

            g.Clear(DarkBase);
            Matrix.Render(g, cyan, IceWhite, 0.92f);
            DrawOuterFrame(g, w, h, cyan);

            DateTime now = DateTime.Now;
            bool is12h = cfg.ClockFormat != "24h";
            string timeStr = is12h
                ? now.ToString("h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant()
                : now.ToString("HH:mm", CultureInfo.InvariantCulture);
            string dateStr = now.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

            DrawHudCard(g, new Rectangle(16, 12, 448, 76), 12, cyan, 158);
            Font clockFont = GetFont("Bahnschrift SemiBold", 44f);
            DrawSmoothTextCentered(g, timeStr, clockFont, Color.FromArgb(45, cyan), w * 0.5f + 1.5f, 15.5f);
            DrawSmoothTextCentered(g, timeStr, clockFont, IceWhite, w * 0.5f, 14f);
            DrawSmoothTextCentered(g, dateStr, GetFont("Bahnschrift SemiBold", 15f), IceDim, w * 0.5f, 62f);

            Rectangle cpuCard = new Rectangle(16, 96, 216, 214);
            Rectangle gpuCard = new Rectangle(248, 96, 216, 214);

            string cpuCenter = sensors.CpuTemp > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0}°C", sensors.CpuTemp)
                : string.Format(CultureInfo.InvariantCulture, "{0:0}%", sensors.CpuLoad);
            string cpuLine1 = sensors.CpuTemp > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0}%   {1:0} W", sensors.CpuLoad, sensors.CpuPower)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.00} GHz", sensors.CpuGhz);
            string cpuLine2 = sensors.CpuTemp > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} GHz", sensors.CpuGhz)
                : "6 CORES";

            DrawHardwareCard(g, cpuCard, "CPU", "5600",
                sensors.CpuLoad, cpuCenter, cpuLine1, cpuLine2, cyan, ringTrack);

            DrawHardwareCard(g, gpuCard, "GPU", "6700XT",
                sensors.GpuLoad, string.Format(CultureInfo.InvariantCulture, "{0:0}°C", sensors.GpuTemp),
                string.Format(CultureInfo.InvariantCulture, "{0:0}%   {1:0} W", sensors.GpuLoad, sensors.GpuPower),
                string.Format(CultureInfo.InvariantCulture, "HOT {0:0}°C", sensors.GpuHotspot),
                cyan, ringTrack);

            Rectangle ramCard = new Rectangle(16, 318, 448, 60);
            DrawHudCard(g, ramCard, 10, cyan, 158);
            DrawSmoothText(g, "MEM", GetFont("Bahnschrift SemiBold", 20f), cyan, 28f, 325f);
            string ramText = string.Format(CultureInfo.InvariantCulture, "{0:0.0} / {1:0.0} GB  ({2:0}%)",
                sensors.RamUsedGb, sensors.RamTotalGb, sensors.RamPercent);
            DrawSmoothTextRight(g, ramText, GetFont("Bahnschrift SemiBold", 15f), IceWhite, 452f, 328f);

            DrawProgressBar(g, new Rectangle(28, 353, 424, 11), sensors.RamPercent,
                ringTrack, Color.FromArgb(0, 170, 200), cyan, 5);

            Rectangle graphCard = new Rectangle(16, 386, 448, 82);
            DrawHudCard(g, graphCard, 10, cyan, 158);
            DrawSmoothText(g, "LOAD", GetFont("Bahnschrift SemiBold", 15f), cyan, 28f, 392f);
            DrawSmoothTextRight(g, "CPU  /  GPU", GetFont("Bahnschrift", 14f), IceDim, 452f, 393f);

            int gx = 28, gy = 414, gw = 424, gh = 46;
            DrawSparklineRange(g, gx, gy, gw, gh, sensors.GpuHistory, 0f, 100f, Color.FromArgb(0, 150, 175), 42);
            DrawSparklineRange(g, gx, gy, gw, gh, sensors.CpuHistory, 0f, 100f, cyan, 72);
        }

        static void DrawHardwareCard(Graphics g, Rectangle card, string tag, string model,
            float ringPercent, string centerText, string line1, string line2,
            Color cyan, Color ringTrack)
        {
            DrawHudCard(g, card, 12, cyan, 158);

            DrawSmoothText(g, tag, GetFont("Bahnschrift SemiBold", 20f), cyan, card.X + 12, card.Y + 8);
            DrawSmoothTextRight(g, model, GetFont("Bahnschrift", 14f), IceDim, card.Right - 12, card.Y + 11);

            int rSize = 102;
            int rx = card.X + (card.Width - rSize) / 2;
            int ry = card.Y + 36;
            Rectangle arcRect = new Rectangle(rx, ry, rSize, rSize);

            using (var trkPen = new Pen(ringTrack, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(trkPen, arcRect, 135f, 270f);

            float sweep = Math.Max(4f, Math.Min(270f, (ringPercent / 100f) * 270f));
            using (var glowPen = new Pen(Color.FromArgb(55, cyan), 13f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(glowPen, arcRect, 135f, sweep);
            using (var valPen = new Pen(cyan, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(valPen, arcRect, 135f, sweep);

            Font centerFont = GetFont("Bahnschrift SemiBold", centerText.Length <= 4 ? 27f : 22f);
            SizeF cSz = MeasureSmoothText(g, centerText, centerFont);
            DrawSmoothText(g, centerText, centerFont, IceWhite,
                card.X + card.Width * 0.5f - cSz.Width * 0.5f,
                ry + rSize * 0.5f - cSz.Height * 0.5f + 1f);

            DrawSmoothTextCentered(g, line1, GetFont("Bahnschrift SemiBold", 18f), cyan, card.X + card.Width * 0.5f, card.Y + 148f);
            DrawSmoothTextCentered(g, line2, GetFont("Bahnschrift", 14f), IceDim, card.X + card.Width * 0.5f, card.Y + 178f);
        }

        static void DrawSparklineRange(Graphics g, int x, int y, int w, int h, List<float> values, float minVal, float maxVal, Color color, int fillAlpha)
        {
            if (values == null || values.Count < 2) return;
            int n = values.Count;
            PointF[] pts = new PointF[n];
            float span = Math.Max(1f, maxVal - minVal);
            for (int i = 0; i < n; i++)
            {
                float px = x + (float)i * w / (n - 1);
                float v = Math.Max(minVal, Math.Min(maxVal, values[i]));
                float py = y + h - ((v - minVal) / span) * (h - 4);
                pts[i] = new PointF(px, py);
            }

            PointF[] poly = new PointF[n + 2];
            Array.Copy(pts, 0, poly, 0, n);
            poly[n] = new PointF(x + w, y + h);
            poly[n + 1] = new PointF(x, y + h);

            using (var fillBr = new SolidBrush(Color.FromArgb(fillAlpha, color)))
                g.FillPolygon(fillBr, poly);
            using (var glowPen = new Pen(Color.FromArgb(60, color), 4f))
                g.DrawLines(glowPen, pts);
            using (var linePen = new Pen(color, 2f))
                g.DrawLines(linePen, pts);
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static List<string> WrapText(string text, int maxCharsPerLine, int maxLines)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cur = "";
            foreach (string w in words)
            {
                string next = cur.Length == 0 ? w : cur + " " + w;
                if (next.Length <= maxCharsPerLine)
                {
                    cur = next;
                }
                else
                {
                    if (cur.Length > 0)
                    {
                        lines.Add(cur);
                        if (lines.Count >= maxLines) return lines;
                        cur = w.Length > maxCharsPerLine ? w.Substring(0, maxCharsPerLine) : w;
                    }
                    else
                    {
                        lines.Add(w.Substring(0, maxCharsPerLine));
                        if (lines.Count >= maxLines) return lines;
                        cur = "";
                    }
                }
            }
            if (cur.Length > 0 && lines.Count < maxLines) lines.Add(cur);
            return lines;
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        static string FormatTime(TimeSpan ts)
        {
            int totalSec = Math.Max(0, (int)ts.TotalSeconds);
            return string.Format("{0}:{1:00}", totalSec / 60, totalSec % 60);
        }
    }
}
