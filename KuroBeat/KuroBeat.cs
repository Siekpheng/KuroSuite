using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace KuroBeat
{
    enum VisStyle { CyberBars = 0, MirrorWave = 1, RadialRing = 2 }
    enum BgStyle { MatrixAndBlur = 0, BlurredCover = 1, PureBlack = 2 }
    enum ThemePreset { PureCyan = 0, AdaptiveCover = 1, MatrixGreen = 2, NeonViolet = 3 }

    sealed class VisConfig
    {
        public VisStyle Style = VisStyle.CyberBars;
        public BgStyle Background = BgStyle.MatrixAndBlur;
        public ThemePreset Theme = ThemePreset.PureCyan;
        public bool Borderless = false;
        public bool AlwaysOnTop = false;
        public bool Fullscreen = false;
        public bool ShowTrackInfo = true;
        public bool RemoteServer = true;
        public int RemotePort = 8765;
        public int WinX = -1;
        public int WinY = -1;
        public int WinW = 1280;
        public int WinH = 720;
        public string ConfPath;

        public static VisConfig Load(string path)
        {
            var c = new VisConfig { ConfPath = path };
            if (!File.Exists(path)) { c.Save(); return c; }
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    int n;
                    if (k == "style")
                    {
                        if (v == "mirror") c.Style = VisStyle.MirrorWave;
                        else if (v == "radial") c.Style = VisStyle.RadialRing;
                        else c.Style = VisStyle.CyberBars;
                    }
                    else if (k == "background")
                    {
                        if (v == "blur") c.Background = BgStyle.BlurredCover;
                        else if (v == "black") c.Background = BgStyle.PureBlack;
                        else c.Background = BgStyle.MatrixAndBlur;
                    }
                    else if (k == "theme")
                    {
                        if (v == "adaptive") c.Theme = ThemePreset.AdaptiveCover;
                        else if (v == "green") c.Theme = ThemePreset.MatrixGreen;
                        else if (v == "violet") c.Theme = ThemePreset.NeonViolet;
                        else c.Theme = ThemePreset.PureCyan;
                    }
                    else if (k == "borderless") c.Borderless = (v == "true" || v == "1");
                    else if (k == "always_on_top") c.AlwaysOnTop = (v == "true" || v == "1");
                    else if (k == "fullscreen") c.Fullscreen = (v == "true" || v == "1");
                    else if (k == "show_track_info") c.ShowTrackInfo = (v != "false" && v != "0");
                    else if (k == "remote_server") c.RemoteServer = (v != "false" && v != "0");
                    else if (k == "remote_port" && int.TryParse(v, out n)) c.RemotePort = Math.Max(1024, Math.Min(65535, n));
                    else if (k == "win_x" && int.TryParse(v, out n)) c.WinX = n;
                    else if (k == "win_y" && int.TryParse(v, out n)) c.WinY = n;
                    else if (k == "win_w" && int.TryParse(v, out n)) c.WinW = Math.Max(480, n);
                    else if (k == "win_h" && int.TryParse(v, out n)) c.WinH = Math.Max(300, n);
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                string st = Style == VisStyle.MirrorWave ? "mirror" : (Style == VisStyle.RadialRing ? "radial" : "bars");
                string bg = Background == BgStyle.BlurredCover ? "blur" : (Background == BgStyle.PureBlack ? "black" : "matrix");
                string th = Theme == ThemePreset.AdaptiveCover ? "adaptive" : (Theme == ThemePreset.MatrixGreen ? "green" : (Theme == ThemePreset.NeonViolet ? "violet" : "cyan"));
                var sb = new StringBuilder();
                sb.AppendLine("# KuroBeat Configuration");
                sb.AppendLine("style = " + st);
                sb.AppendLine("background = " + bg);
                sb.AppendLine("theme = " + th);
                sb.AppendLine("borderless = " + (Borderless ? "true" : "false"));
                sb.AppendLine("always_on_top = " + (AlwaysOnTop ? "true" : "false"));
                sb.AppendLine("fullscreen = " + (Fullscreen ? "true" : "false"));
                sb.AppendLine("show_track_info = " + (ShowTrackInfo ? "true" : "false"));
                sb.AppendLine("remote_server = " + (RemoteServer ? "true" : "false"));
                sb.AppendLine("remote_port = " + RemotePort);
                sb.AppendLine("win_x = " + WinX);
                sb.AppendLine("win_y = " + WinY);
                sb.AppendLine("win_w = " + WinW);
                sb.AppendLine("win_h = " + WinH);
                File.WriteAllText(ConfPath, sb.ToString());
            }
            catch { }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            string previewPng = null;
            string previewStyle = "bars";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--preview-png" && i + 1 < args.Length) previewPng = args[++i];
                else if (a == "--style" && i + 1 < args.Length) previewStyle = args[++i].ToLowerInvariant();
            }

            string confPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kurobeat.conf");
            VisConfig cfg = VisConfig.Load(confPath);

            if (previewPng != null)
            {
                int exitCode = 0;
                var mta = new Thread(() =>
                {
                    try
                    {
                        if (previewStyle == "mirror") cfg.Style = VisStyle.MirrorWave;
                        else if (previewStyle == "radial") cfg.Style = VisStyle.RadialRing;
                        else cfg.Style = VisStyle.CyberBars;

                        using (var media = new MediaReader())
                        using (var audio = new WasapiCava(96))
                        {
                            media.Update();
                            Thread.Sleep(150);
                            media.Update();
                            audio.Update();
                            if (string.IsNullOrEmpty(media.Title))
                            {
                                media.Title = "NEON HORIZON";
                                media.Artist = "THE MIDNIGHT DRIVERS";
                                media.Album = "ENDLESS SUMMER";
                                media.Position = TimeSpan.FromSeconds(83);
                                media.Duration = TimeSpan.FromSeconds(240);
                                media.IsPlaying = true;
                            }
                            if (audio.PeakLevel < 0.02f) audio.InjectDemoSpectrum();

                            var renderer = new VisualizerRenderer();
                            using (var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppPArgb))
                            using (var g = Graphics.FromImage(bmp))
                            {
                                renderer.Render(g, 1920, 1080, cfg, media, audio, 0f);
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new VisualizerForm(cfg))
            {
                Application.Run(form);
            }
            return 0;
        }
    }

    // =========================================================================
    // BUILT-IN YOUTUBE MUSIC PHONE REMOTE SERVER (HTTP + WS + VOL + MDNS)
    // =========================================================================
    static class SystemVolume
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorComObj { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnum
        {
            int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDev ppEndpoint);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDev
        {
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        static readonly object AudioLock = new object();
        static Guid IidEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static Guid EmptyGuid = Guid.Empty;

        static IAudioEndpointVolume GetVolumeInterface()
        {
            IMMDeviceEnum enumerator = (IMMDeviceEnum)(new MMDeviceEnumeratorComObj());
            IMMDev device = null;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null)
                    return null;
                object obj;
                if (device.Activate(ref IidEndpointVolume, 23, IntPtr.Zero, out obj) != 0 || obj == null)
                    return null;
                return (IAudioEndpointVolume)obj;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool TryGetState(out int volumePercent, out bool muted)
        {
            volumePercent = 50;
            muted = false;
            lock (AudioLock)
            {
                IAudioEndpointVolume ep = null;
                try
                {
                    ep = GetVolumeInterface();
                    if (ep == null) return false;
                    float level;
                    bool isMuted;
                    ep.GetMasterVolumeLevelScalar(out level);
                    ep.GetMute(out isMuted);
                    volumePercent = (int)Math.Round(Math.Max(0f, Math.Min(1f, level)) * 100f);
                    muted = isMuted;
                    return true;
                }
                catch { return false; }
                finally { if (ep != null) Marshal.ReleaseComObject(ep); }
            }
        }

        public static bool SetVolume(int percent)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            lock (AudioLock)
            {
                IAudioEndpointVolume ep = null;
                try
                {
                    ep = GetVolumeInterface();
                    if (ep == null) return false;
                    Guid ctx = EmptyGuid;
                    ep.SetMasterVolumeLevelScalar(clamped / 100.0f, ref ctx);
                    if (clamped > 0)
                    {
                        bool isMuted;
                        ep.GetMute(out isMuted);
                        if (isMuted) ep.SetMute(false, ref ctx);
                    }
                    return true;
                }
                catch { return false; }
                finally { if (ep != null) Marshal.ReleaseComObject(ep); }
            }
        }

        public static bool SetMute(bool mute)
        {
            lock (AudioLock)
            {
                IAudioEndpointVolume ep = null;
                try
                {
                    ep = GetVolumeInterface();
                    if (ep == null) return false;
                    Guid ctx = EmptyGuid;
                    ep.SetMute(mute, ref ctx);
                    return true;
                }
                catch { return false; }
                finally { if (ep != null) Marshal.ReleaseComObject(ep); }
            }
        }

        public static bool ToggleMute()
        {
            int vol;
            bool muted;
            if (!TryGetState(out vol, out muted)) return false;
            return SetMute(!muted);
        }

        public static bool StepVolume(int delta)
        {
            int vol;
            bool muted;
            if (!TryGetState(out vol, out muted)) return false;
            return SetVolume(vol + delta);
        }
    }

    sealed class WsClient
    {
        public string Id { get; private set; }
        public string Role { get; private set; }
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly object sendLock = new object();
        public volatile bool Closed;

        public WsClient(TcpClient tcp, NetworkStream stream, string role)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Role = role;
            this.tcp = tcp;
            this.stream = stream;
        }

        public void SendText(string text)
        {
            if (Closed) return;
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                byte[] frame = EncodeFrame(0x81, payload);
                lock (sendLock)
                {
                    if (!Closed)
                    {
                        stream.Write(frame, 0, frame.Length);
                        stream.Flush();
                    }
                }
            }
            catch { Close(); }
        }

        public void SendPong(byte[] payload)
        {
            if (Closed) return;
            try
            {
                byte[] frame = EncodeFrame(0x8A, payload ?? new byte[0]);
                lock (sendLock)
                {
                    if (!Closed)
                    {
                        stream.Write(frame, 0, frame.Length);
                        stream.Flush();
                    }
                }
            }
            catch { Close(); }
        }

        static byte[] EncodeFrame(byte firstByte, byte[] payload)
        {
            int len = payload.Length;
            int headerLen = len < 126 ? 2 : (len <= 65535 ? 4 : 10);
            byte[] frame = new byte[headerLen + len];
            frame[0] = firstByte;
            if (len < 126)
            {
                frame[1] = (byte)len;
            }
            else if (len <= 65535)
            {
                frame[1] = 126;
                frame[2] = (byte)((len >> 8) & 0xFF);
                frame[3] = (byte)(len & 0xFF);
            }
            else
            {
                frame[1] = 127;
                ulong ulen = (ulong)len;
                for (int i = 0; i < 8; i++)
                    frame[2 + i] = (byte)((ulen >> (56 - i * 8)) & 0xFF);
            }
            System.Buffer.BlockCopy(payload, 0, frame, headerLen, len);
            return frame;
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            try { stream.Close(); } catch { }
            try { tcp.Close(); } catch { }
        }
    }

    sealed class MdnsResponder : IDisposable
    {
        static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");
        const int MdnsPort = 5353;
        readonly string targetHost;
        readonly Func<List<string>> getLanIps;
        Socket sock;
        Thread thread;
        volatile bool stopping;

        public MdnsResponder(string hostName, Func<List<string>> getLanIps)
        {
            targetHost = hostName.Trim().TrimEnd('.').ToLowerInvariant();
            this.getLanIps = getLanIps;
        }

        public void Start()
        {
            if (thread != null) return;
            stopping = false;
            thread = new Thread(Run) { IsBackground = true, Name = "KuroMdnsResponder" };
            thread.Start();
        }

        void Run()
        {
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                sock.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

                List<string> ips = getLanIps();
                IPAddress bindIp = ips.Count > 0 ? IPAddress.Parse(ips[0]) : IPAddress.Any;
                try { sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MdnsGroup, bindIp)); }
                catch { sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MdnsGroup)); }
                try { sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, bindIp.GetAddressBytes()); } catch { }
                try { sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255); } catch { }

                var mcastEp = new IPEndPoint(MdnsGroup, MdnsPort);
                byte[] announce = BuildAResponse(targetHost, bindIp);
                if (announce != null)
                {
                    try { sock.SendTo(announce, mcastEp); } catch { }
                    Thread.Sleep(200);
                    if (!stopping) { try { sock.SendTo(announce, mcastEp); } catch { } }
                }

                byte[] buf = new byte[1500];
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                while (!stopping)
                {
                    int len = sock.ReceiveFrom(buf, ref remote);
                    if (len < 12) continue;
                    ushort flags = (ushort)((buf[2] << 8) | buf[3]);
                    if ((flags & 0x8000) != 0) continue;
                    int qdCount = (buf[4] << 8) | buf[5];
                    if (qdCount <= 0) continue;

                    int offset = 12;
                    bool matched = false;
                    bool wantUnicast = false;
                    for (int q = 0; q < qdCount && offset < len; q++)
                    {
                        string qname = ReadDnsName(buf, len, ref offset);
                        if (offset + 4 > len) break;
                        ushort qtype = (ushort)((buf[offset] << 8) | buf[offset + 1]);
                        ushort qclass = (ushort)((buf[offset + 2] << 8) | buf[offset + 3]);
                        offset += 4;

                        if (string.Equals(qname, targetHost, StringComparison.OrdinalIgnoreCase) &&
                            (qtype == 1 || qtype == 255))
                        {
                            matched = true;
                            if ((qclass & 0x8000) != 0) wantUnicast = true;
                        }
                    }

                    if (matched)
                    {
                        List<string> curIps = getLanIps();
                        IPAddress ip = curIps.Count > 0 ? IPAddress.Parse(curIps[0]) : bindIp;
                        byte[] resp = BuildAResponse(targetHost, ip);
                        if (resp != null)
                        {
                            try { sock.SendTo(resp, mcastEp); } catch { }
                            if (wantUnicast) { try { sock.SendTo(resp, remote); } catch { } }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (sock != null) { try { sock.Close(); } catch { } sock = null; }
            }
        }

        static string ReadDnsName(byte[] buf, int len, ref int offset)
        {
            var sb = new StringBuilder();
            int pos = offset;
            bool jumped = false;
            int guard = 0;
            while (pos < len && guard++ < 64)
            {
                byte b = buf[pos];
                if (b == 0)
                {
                    if (!jumped) offset = pos + 1;
                    break;
                }
                if ((b & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= len) break;
                    int ptr = ((b & 0x3F) << 8) | buf[pos + 1];
                    if (!jumped) offset = pos + 2;
                    jumped = true;
                    pos = ptr;
                    continue;
                }
                pos++;
                if (pos + b > len) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(buf, pos, b));
                pos += b;
            }
            return sb.ToString();
        }

        static byte[] BuildAResponse(string hostName, IPAddress ip)
        {
            byte[] ipBytes = ip.GetAddressBytes();
            if (ipBytes.Length != 4) return null;
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x00, 0x00, 0x84, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 }, 0, 12);
                foreach (string label in hostName.Split('.'))
                {
                    byte[] lb = Encoding.ASCII.GetBytes(label);
                    ms.WriteByte((byte)lb.Length);
                    ms.Write(lb, 0, lb.Length);
                }
                ms.WriteByte(0x00);
                ms.Write(new byte[] { 0x00, 0x01, 0x80, 0x01, 0x00, 0x00, 0x00, 0x78, 0x00, 0x04 }, 0, 10);
                ms.Write(ipBytes, 0, 4);
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            stopping = true;
            if (sock != null) { try { sock.Close(); } catch { } sock = null; }
            thread = null;
        }
    }

    sealed class YtmRemoteBridge : IDisposable
    {
        readonly int port;
        readonly object lifecycleLock = new object();
        readonly object clientsLock = new object();
        readonly object stateLock = new object();
        readonly List<WsClient> clients = new List<WsClient>();
        readonly Dictionary<string, object> playbackState = new Dictionary<string, object>
        {
            { "title", "" },
            { "artist", "" },
            { "artwork", "" },
            { "isPlaying", false },
            { "currentTime", 0.0 },
            { "duration", 0.0 }
        };

        int lastVolume = -1;
        bool lastMuted = false;
        TcpListener listener;
        Thread acceptThread;
        Thread volThread;
        MdnsResponder mdns;
        volatile bool stopping;
        volatile bool hostingInProcess;
        bool runningExternal;
        string webRoot;
        string lastError = "";

        public string ConnectUrl { get { return "http://127.0.0.1:" + port + "/connect"; } }
        public string PhoneUrl { get { return "http://kurobeat.local:" + port; } }
        public bool IsRunning
        {
            get
            {
                if (hostingInProcess) return true;
                return runningExternal && IsPortOpen(port);
            }
        }
        public string StatusLabel
        {
            get
            {
                if (IsRunning) return PhoneUrl;
                if (string.IsNullOrEmpty(webRoot)) return "web/ folder missing";
                if (!string.IsNullOrEmpty(lastError)) return lastError;
                return "Stopped";
            }
        }

        public YtmRemoteBridge(int port)
        {
            this.port = port;
            webRoot = ResolveWebRoot();
        }

        static string ResolveWebRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string parentDir = Path.GetDirectoryName(baseDir) ?? "";
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string[] candidates =
            {
                Path.Combine(baseDir, "web"),
                Path.Combine(parentDir, "web"),
                Path.Combine(docs, @"KuroBeat\web"),
                Path.Combine(docs, @"ytm-remote\web")
            };
            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c) && File.Exists(Path.Combine(c, "index.html")))
                    return Path.GetFullPath(c);
            }
            return null;
        }

        static bool IsPortOpen(int p)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    IAsyncResult ar = tcp.BeginConnect("127.0.0.1", p, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(120, false);
                    if (!ok) return false;
                    tcp.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        static List<string> GetLanIps()
        {
            var preferred = new List<string>();
            var others = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    if (name.Contains("virtual") || name.Contains("vmware") || name.Contains("hyper-v") ||
                        name.Contains("vbox") || name.Contains("warp") || name.Contains("tailscale") ||
                        name.Contains("zerotier") || name.Contains("wsl"))
                        continue;

                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip == "127.0.0.1") continue;
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            preferred.Add(ip);
                        else
                            others.Add(ip);
                    }
                }
            }
            catch { }
            preferred.AddRange(others);
            return preferred;
        }

        public void Start()
        {
            lock (lifecycleLock)
            {
                if (hostingInProcess) return;
                webRoot = ResolveWebRoot();
                if (string.IsNullOrEmpty(webRoot))
                {
                    lastError = "web/ folder missing";
                    return;
                }
                if (IsPortOpen(port))
                {
                    runningExternal = true;
                    lastError = "";
                    return;
                }

                int initVol;
                bool initMuted;
                if (SystemVolume.TryGetState(out initVol, out initMuted))
                {
                    lastVolume = initVol;
                    lastMuted = initMuted;
                }
                else
                {
                    lastVolume = 50;
                    lastMuted = false;
                }

                try
                {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    listener = null;
                    return;
                }

                stopping = false;
                hostingInProcess = true;
                runningExternal = false;
                lastError = "";

                mdns = new MdnsResponder("kurobeat.local", GetLanIps);
                mdns.Start();

                volThread = new Thread(VolumeMonitorLoop) { IsBackground = true, Name = "KuroRemoteVolMon" };
                volThread.Start();

                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "KuroRemoteHttp" };
                acceptThread.Start();
            }
        }

        public void Stop()
        {
            lock (lifecycleLock)
            {
                stopping = true;
                hostingInProcess = false;
                if (mdns != null) { mdns.Dispose(); mdns = null; }
                if (listener != null) { try { listener.Stop(); } catch { } listener = null; }

                List<WsClient> snapshot;
                lock (clientsLock)
                {
                    snapshot = new List<WsClient>(clients);
                    clients.Clear();
                }
                foreach (var c in snapshot) c.Close();
            }
        }

        void AcceptLoop()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient tcp = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleConnection, tcp);
                }
                catch
                {
                    if (stopping) break;
                    Thread.Sleep(200);
                }
            }
        }

        void VolumeMonitorLoop()
        {
            while (!stopping)
            {
                try
                {
                    int vol;
                    bool muted;
                    if (SystemVolume.TryGetState(out vol, out muted))
                    {
                        bool changed = false;
                        lock (stateLock)
                        {
                            if (vol != lastVolume || muted != lastMuted)
                            {
                                lastVolume = vol;
                                lastMuted = muted;
                                changed = true;
                            }
                        }
                        if (changed) BroadcastSystemVolume();
                    }
                }
                catch { }
                Thread.Sleep(250);
            }
        }

        void HandleConnection(object state)
        {
            TcpClient tcp = (TcpClient)state;
            tcp.NoDelay = true;
            NetworkStream stream = null;
            try
            {
                stream = tcp.GetStream();
                string rawHeaders = ReadHttpHeaders(stream);
                if (string.IsNullOrEmpty(rawHeaders)) { tcp.Close(); return; }

                string[] lines = rawHeaders.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) { tcp.Close(); return; }
                string[] reqParts = lines[0].Split(' ');
                if (reqParts.Length < 2) { tcp.Close(); return; }
                string method = reqParts[0].ToUpperInvariant();
                string rawTarget = reqParts[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon > 0)
                        headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                }

                string path = rawTarget;
                string query = "";
                int qIdx = rawTarget.IndexOf('?');
                if (qIdx >= 0)
                {
                    path = rawTarget.Substring(0, qIdx);
                    query = rawTarget.Substring(qIdx + 1);
                }

                string upgrade;
                if (headers.TryGetValue("Upgrade", out upgrade) &&
                    upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    HandleWebSocket(tcp, stream, headers, query);
                    return;
                }

                HandleHttpRequest(stream, method, path);
                tcp.Close();
            }
            catch
            {
                try { if (stream != null) stream.Close(); } catch { }
                try { tcp.Close(); } catch { }
            }
        }

        static string ReadHttpHeaders(NetworkStream stream)
        {
            stream.ReadTimeout = 10000;
            var ms = new MemoryStream();
            byte[] buf = new byte[1];
            int match = 0;
            while (ms.Length < 16384)
            {
                int read = stream.Read(buf, 0, 1);
                if (read <= 0) return null;
                byte b = buf[0];
                ms.WriteByte(b);
                if ((match == 0 && b == '\r') ||
                    (match == 1 && b == '\n') ||
                    (match == 2 && b == '\r') ||
                    (match == 3 && b == '\n'))
                {
                    match++;
                    if (match == 4) break;
                }
                else
                {
                    match = (b == '\r') ? 1 : 0;
                }
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        void HandleHttpRequest(NetworkStream stream, string method, string path)
        {
            if (method == "OPTIONS")
            {
                SendHttpResponse(stream, "204 No Content", "text/plain", new byte[0]);
                return;
            }

            if (path == "/api/info")
            {
                int extCount = 0;
                int phoneCount = 0;
                lock (clientsLock)
                {
                    foreach (var c in clients)
                    {
                        if (!c.Closed)
                        {
                            if (c.Role == "extension") extCount++;
                            else if (c.Role == "phone") phoneCount++;
                        }
                    }
                }
                List<string> ips = GetLanIps();
                string primary = ips.Count > 0 ? ips[0] : "127.0.0.1";
                var info = new Dictionary<string, object>
                {
                    { "port", port },
                    { "primaryIp", primary },
                    { "lanIps", ips },
                    { "phoneUrl", PhoneUrl },
                    { "ipUrl", "http://" + primary + ":" + port },
                    { "extensionConnected", extCount > 0 },
                    { "extensionCount", extCount },
                    { "phoneCount", phoneCount },
                    { "volume", lastVolume },
                    { "muted", lastMuted }
                };
                string json = new JavaScriptSerializer().Serialize(info);
                SendHttpResponse(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                return;
            }

            if (path == "/" || path == "") path = "/index.html";
            else if (path == "/connect") path = "/connect.html";

            string relPath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (relPath.Contains("..") || string.IsNullOrEmpty(webRoot))
            {
                SendHttpResponse(stream, "403 Forbidden", "text/plain", Encoding.UTF8.GetBytes("Forbidden"));
                return;
            }

            string fullPath = Path.GetFullPath(Path.Combine(webRoot, relPath));
            if (!fullPath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                SendHttpResponse(stream, "404 Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"));
                return;
            }

            byte[] content = File.ReadAllBytes(fullPath);
            SendHttpResponse(stream, "200 OK", GetMimeType(Path.GetExtension(fullPath)), content);
        }

        static void SendHttpResponse(NetworkStream stream, string status, string contentType, byte[] body)
        {
            string header =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0) stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        static string GetMimeType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".ico": return "image/x-icon";
                default: return "application/octet-stream";
            }
        }

        void HandleWebSocket(TcpClient tcp, NetworkStream stream, Dictionary<string, string> headers, string query)
        {
            string wsKey;
            if (!headers.TryGetValue("Sec-WebSocket-Key", out wsKey)) { tcp.Close(); return; }

            string acceptKey = ComputeWebSocketAcceptKey(wsKey);
            string handshake =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + acceptKey + "\r\n\r\n";
            byte[] handshakeBytes = Encoding.UTF8.GetBytes(handshake);
            stream.Write(handshakeBytes, 0, handshakeBytes.Length);
            stream.Flush();

            string role = query.IndexOf("role=extension", StringComparison.OrdinalIgnoreCase) >= 0 ? "extension" : "phone";
            var client = new WsClient(tcp, stream, role);
            lock (clientsLock) clients.Add(client);

            if (role == "phone") client.SendText(BuildFullStateMessage());
            else if (role == "extension") BroadcastFullStateToPhones();

            stream.ReadTimeout = Timeout.Infinite;
            try
            {
                while (!client.Closed && !stopping)
                {
                    int opcode;
                    byte[] payload = ReadWebSocketFrame(stream, out opcode);
                    if (payload == null || opcode == 0x8) break;
                    if (opcode == 0x9) client.SendPong(payload);
                    else if (opcode == 0x1) ProcessWsMessage(client, Encoding.UTF8.GetString(payload));
                }
            }
            catch { }
            finally
            {
                client.Close();
                lock (clientsLock) clients.Remove(client);
                if (!stopping && role == "extension") BroadcastFullStateToPhones();
            }
        }

        static string ComputeWebSocketAcceptKey(string key)
        {
            string combined = key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using (SHA1 sha1 = SHA1.Create())
                return Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(combined)));
        }

        static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        static byte[] ReadWebSocketFrame(NetworkStream stream, out int opcode)
        {
            opcode = 0;
            byte[] header = new byte[2];
            if (!ReadExact(stream, header, 0, 2)) return null;
            opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (payloadLen == 126)
            {
                byte[] ext = new byte[2];
                if (!ReadExact(stream, ext, 0, 2)) return null;
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = new byte[8];
                if (!ReadExact(stream, ext, 0, 8)) return null;
                payloadLen = 0;
                for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
            }
            if (payloadLen < 0 || payloadLen > 1024 * 1024) return null;

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                if (!ReadExact(stream, maskKey, 0, 4)) return null;
            }

            byte[] payload = new byte[(int)payloadLen];
            if (payloadLen > 0 && !ReadExact(stream, payload, 0, (int)payloadLen)) return null;

            if (masked && maskKey != null)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] = (byte)(payload[i] ^ maskKey[i % 4]);
            }
            return payload;
        }

        void ProcessWsMessage(WsClient sender, string rawJson)
        {
            Dictionary<string, object> data;
            try { data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(rawJson); }
            catch { return; }
            if (data == null || !data.ContainsKey("type")) return;
            string type = Convert.ToString(data["type"]);

            if (sender.Role == "extension")
            {
                if (type == "playbackState")
                {
                    lock (stateLock)
                    {
                        UpdateField(data, "title");
                        UpdateField(data, "artist");
                        UpdateField(data, "artwork");
                        UpdateField(data, "isPlaying");
                        UpdateField(data, "currentTime");
                        UpdateField(data, "duration");
                        UpdateField(data, "nextTitle");
                        UpdateField(data, "nextArtist");
                    }
                    BroadcastFullStateToPhones();
                }
                else if (type == "searchResults" || type == "homeFeed" || type == "queueState")
                {
                    ForwardToPhones(rawJson);
                }
            }
            else if (sender.Role == "phone" && type == "command")
            {
                string action = data.ContainsKey("action") ? Convert.ToString(data["action"]) : "";
                if (action == "setVolume")
                {
                    int val = Convert.ToInt32(Math.Round(Convert.ToDouble(data["value"])));
                    if (SystemVolume.SetVolume(val)) SyncAndBroadcastVolume();
                }
                else if (action == "toggleMute")
                {
                    if (SystemVolume.ToggleMute()) SyncAndBroadcastVolume();
                }
                else if (action == "volumeStep")
                {
                    int delta = Convert.ToInt32(data["delta"]);
                    if (SystemVolume.StepVolume(delta)) SyncAndBroadcastVolume();
                }
                else if (action == "search" || action == "getHome" || action == "getQueue")
                {
                    if (!HasExtensionConnected())
                    {
                        string respType = action == "search" ? "searchResults" : (action == "getHome" ? "homeFeed" : "queueState");
                        var errResp = new Dictionary<string, object>
                        {
                            { "type", respType },
                            { "requestId", data.ContainsKey("requestId") ? data["requestId"] : "" },
                            { "error", "YouTube Music tab is not connected on your PC." }
                        };
                        sender.SendText(new JavaScriptSerializer().Serialize(errResp));
                    }
                    else
                    {
                        ForwardToFirstExtension(rawJson);
                    }
                }
                else if (action == "playPause" || action == "next" || action == "prev" || action == "seek" || action == "playTrack" || action == "playQueueIndex")
                {
                    ForwardToExtensions(rawJson);
                }
            }
        }

        void UpdateField(Dictionary<string, object> src, string key)
        {
            if (src.ContainsKey(key) && src[key] != null)
                playbackState[key] = src[key];
        }

        void SyncAndBroadcastVolume()
        {
            int vol;
            bool muted;
            if (SystemVolume.TryGetState(out vol, out muted))
            {
                lock (stateLock) { lastVolume = vol; lastMuted = muted; }
                BroadcastSystemVolume();
            }
        }

        bool HasExtensionConnected()
        {
            lock (clientsLock)
            {
                foreach (var c in clients)
                    if (!c.Closed && c.Role == "extension") return true;
            }
            return false;
        }

        string BuildFullStateMessage()
        {
            bool extConnected = HasExtensionConnected();
            Dictionary<string, object> payload;
            lock (stateLock)
            {
                payload = new Dictionary<string, object>
                {
                    { "type", "state" },
                    { "extensionConnected", extConnected },
                    { "playback", new Dictionary<string, object>(playbackState) },
                    { "systemVolume", new Dictionary<string, object> { { "volume", lastVolume }, { "muted", lastMuted } } }
                };
            }
            return new JavaScriptSerializer().Serialize(payload);
        }

        void BroadcastFullStateToPhones()
        {
            string msg = BuildFullStateMessage();
            ForwardToPhones(msg);
        }

        void BroadcastSystemVolume()
        {
            Dictionary<string, object> payload;
            lock (stateLock)
            {
                payload = new Dictionary<string, object> { { "type", "systemVolume" }, { "volume", lastVolume }, { "muted", lastMuted } };
            }
            ForwardToPhones(new JavaScriptSerializer().Serialize(payload));
        }

        void ForwardToExtensions(string rawJson)
        {
            List<WsClient> targets = new List<WsClient>();
            lock (clientsLock)
            {
                foreach (var c in clients)
                    if (!c.Closed && c.Role == "extension") targets.Add(c);
            }
            foreach (var c in targets) c.SendText(rawJson);
        }

        void ForwardToFirstExtension(string rawJson)
        {
            WsClient target = null;
            lock (clientsLock)
            {
                foreach (var c in clients)
                {
                    if (!c.Closed && c.Role == "extension") { target = c; break; }
                }
            }
            if (target != null) target.SendText(rawJson);
        }

        void ForwardToPhones(string rawJson)
        {
            List<WsClient> targets = new List<WsClient>();
            lock (clientsLock)
            {
                foreach (var c in clients)
                    if (!c.Closed && c.Role == "phone") targets.Add(c);
            }
            foreach (var c in targets) c.SendText(rawJson);
        }

        public void OpenConnectPage()
        {
            if (!IsRunning) Start();
            try { Process.Start(ConnectUrl); } catch { }
        }

        public void CopyPhoneUrl()
        {
            try { Clipboard.SetText(PhoneUrl); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    // =========================================================================
    // MAIN MONITOR VISUALIZER WINDOW
    // =========================================================================
    sealed class VisualizerForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint uPeriod);

        readonly VisConfig cfg;
        readonly WasapiCava audio;
        readonly MediaReader media;
        readonly VisualizerRenderer renderer;
        readonly YtmRemoteBridge remoteBridge;
        readonly System.Windows.Forms.Timer frameTimer;
        readonly Thread mediaThread;
        readonly NotifyIcon tray;
        readonly Icon appIcon;
        readonly ContextMenuStrip ctxMenu;

        volatile bool stopping;
        DateTime lastMouseMoveUtc = DateTime.UtcNow;
        Point lastMouseClientPos = new Point(-9999, -9999);
        bool cursorHidden;
        float hudAlpha = 1f;
        Rectangle savedNormalBounds;
        bool savedBorderless;
        readonly List<HudButton> hudButtons = new List<HudButton>();

        sealed class HudButton
        {
            public Rectangle Rect;
            public string Label;
            public Action OnClick;
        }

        public VisualizerForm(VisConfig cfg)
        {
            this.cfg = cfg;
            Text = "KuroBeat";
            BackColor = Color.Black;
            MinimumSize = new Size(480, 300);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            appIcon = CreateAppIcon();
            Icon = appIcon;

            audio = new WasapiCava(96);
            media = new MediaReader();
            renderer = new VisualizerRenderer();
            remoteBridge = new YtmRemoteBridge(cfg.RemotePort);
            if (cfg.RemoteServer) remoteBridge.Start();

            Size = new Size(cfg.WinW, cfg.WinH);
            if (cfg.WinX >= -32000 && cfg.WinY >= -32000 && IsLocationOnAnyScreen(cfg.WinX, cfg.WinY, cfg.WinW, cfg.WinH))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(cfg.WinX, cfg.WinY);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
            savedNormalBounds = Bounds;

            ctxMenu = BuildContextMenu();
            ContextMenuStrip = ctxMenu;

            tray = new NotifyIcon
            {
                Icon = appIcon,
                Text = "KuroBeat",
                ContextMenuStrip = ctxMenu,
                Visible = true
            };
            tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

            ApplyWindowStyle();

            mediaThread = new Thread(MediaPollLoop) { IsBackground = true, Name = "VisMediaPoller" };
            mediaThread.SetApartmentState(ApartmentState.MTA);
            mediaThread.Start();

            try { timeBeginPeriod(1); } catch { }
            frameTimer = new System.Windows.Forms.Timer { Interval = 15 }; // 60 FPS with 1ms timer resolution
            frameTimer.Tick += (s, e) =>
            {
                if (stopping || !Visible || WindowState == FormWindowState.Minimized) return;
                audio.Update();
                double idleSec = (DateTime.UtcNow - lastMouseMoveUtc).TotalSeconds;
                float targetAlpha = idleSec < 2.4 ? 1f : 0f;
                hudAlpha += (targetAlpha - hudAlpha) * 0.16f;
                if (hudAlpha < 0.01f) hudAlpha = 0f;

                bool mouseInside = Bounds.Contains(Cursor.Position);
                bool shouldHideCursor = mouseInside && !ctxMenu.Visible && idleSec >= 2.4;
                SetCursorHidden(shouldHideCursor);

                Invalidate();
            };
            frameTimer.Start();
        }

        void SetCursorHidden(bool hide)
        {
            if (hide && !cursorHidden)
            {
                cursorHidden = true;
                Cursor.Hide();
            }
            else if (!hide && cursorHidden)
            {
                cursorHidden = false;
                Cursor.Show();
            }
        }

        static bool IsLocationOnAnyScreen(int x, int y, int w, int h)
        {
            var r = new Rectangle(x, y, Math.Max(100, w), Math.Max(100, h));
            foreach (Screen sc in Screen.AllScreens)
            {
                if (sc.WorkingArea.IntersectsWith(r)) return true;
            }
            return false;
        }

        void MediaPollLoop()
        {
            while (!stopping)
            {
                media.Update();
                Thread.Sleep(250);
            }
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip();
            m.BackColor = Color.FromArgb(6, 18, 26);
            m.ForeColor = Color.FromArgb(228, 252, 255);
            m.Renderer = new ToolStripProfessionalRenderer(new DarkCyanMenuColors());

            var styleMenu = new ToolStripMenuItem("Visualizer Style (V)");
            styleMenu.DropDownItems.Add("Cyber Bars", null, (s, e) => { cfg.Style = VisStyle.CyberBars; cfg.Save(); });
            styleMenu.DropDownItems.Add("Mirror Wave", null, (s, e) => { cfg.Style = VisStyle.MirrorWave; cfg.Save(); });
            styleMenu.DropDownItems.Add("Radial Ring", null, (s, e) => { cfg.Style = VisStyle.RadialRing; cfg.Save(); });

            var themeMenu = new ToolStripMenuItem("Color Theme (M)");
            themeMenu.DropDownItems.Add("Pure Cyan (#00FFFF)", null, (s, e) => { cfg.Theme = ThemePreset.PureCyan; cfg.Save(); });
            themeMenu.DropDownItems.Add("Adaptive Album Art", null, (s, e) => { cfg.Theme = ThemePreset.AdaptiveCover; cfg.Save(); });
            themeMenu.DropDownItems.Add("Matrix Green (#00FF66)", null, (s, e) => { cfg.Theme = ThemePreset.MatrixGreen; cfg.Save(); });
            themeMenu.DropDownItems.Add("Neon Violet (#C04DFF)", null, (s, e) => { cfg.Theme = ThemePreset.NeonViolet; cfg.Save(); });

            var bgMenu = new ToolStripMenuItem("Background (G)");
            bgMenu.DropDownItems.Add("Matrix Rain + Glow", null, (s, e) => { cfg.Background = BgStyle.MatrixAndBlur; cfg.Save(); });
            bgMenu.DropDownItems.Add("Blurred Album Cover", null, (s, e) => { cfg.Background = BgStyle.BlurredCover; cfg.Save(); });
            bgMenu.DropDownItems.Add("Pure Black (OLED)", null, (s, e) => { cfg.Background = BgStyle.PureBlack; cfg.Save(); });

            var monMenu = new ToolStripMenuItem("Move to Monitor");
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                int idx = i;
                string label = string.Format("Monitor {0} ({1}x{2}){3}", i + 1, screens[i].Bounds.Width, screens[i].Bounds.Height, screens[i].Primary ? " - Primary" : "");
                monMenu.DropDownItems.Add(label, null, (s, e) => MoveToMonitor(idx));
            }

            var itemFs = new ToolStripMenuItem("Fullscreen (F11 / Double-Click)", null, (s, e) => ToggleFullscreen());
            var itemBorder = new ToolStripMenuItem("Borderless Window (B)", null, (s, e) => { cfg.Borderless = !cfg.Borderless; ApplyWindowStyle(); cfg.Save(); });
            var itemTop = new ToolStripMenuItem("Always on Top (T)", null, (s, e) => { cfg.AlwaysOnTop = !cfg.AlwaysOnTop; TopMost = cfg.AlwaysOnTop; cfg.Save(); });
            var itemHud = new ToolStripMenuItem("Show Track Info Card (H)", null, (s, e) => { cfg.ShowTrackInfo = !cfg.ShowTrackInfo; cfg.Save(); });

            var remoteMenu = new ToolStripMenuItem("YouTube Music Phone Remote");
            var remoteStatus = new ToolStripMenuItem("Status: Starting...") { Enabled = false };
            var remoteQr = new ToolStripMenuItem("Open Phone QR / Connect Page (R)", null, (s, e) => remoteBridge.OpenConnectPage());
            var remoteCopy = new ToolStripMenuItem("Copy Phone URL", null, (s, e) => remoteBridge.CopyPhoneUrl());
            var remoteAuto = new ToolStripMenuItem("Auto-Start Phone Remote With KuroBeat", null, (s, e) =>
            {
                cfg.RemoteServer = !cfg.RemoteServer;
                cfg.Save();
                if (cfg.RemoteServer) remoteBridge.Start();
                else remoteBridge.Stop();
            });
            remoteMenu.DropDownItems.AddRange(new ToolStripItem[] { remoteStatus, new ToolStripSeparator(), remoteQr, remoteCopy, new ToolStripSeparator(), remoteAuto });

            m.Items.Add(styleMenu);
            m.Items.Add(themeMenu);
            m.Items.Add(bgMenu);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(remoteMenu);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(itemFs);
            m.Items.Add(itemBorder);
            m.Items.Add(itemTop);
            m.Items.Add(itemHud);
            m.Items.Add(monMenu);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Play / Pause (Space)", null, (s, e) => media.SendCommand(MediaCommand.TogglePlayPause));
            m.Items.Add("Previous Track (Left)", null, (s, e) => media.SendCommand(MediaCommand.Previous));
            m.Items.Add("Next Track (Right)", null, (s, e) => media.SendCommand(MediaCommand.Next));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, (s, e) => Close());

            m.Opening += (s, e) =>
            {
                SetCursorHidden(false);
                ((ToolStripMenuItem)styleMenu.DropDownItems[0]).Checked = cfg.Style == VisStyle.CyberBars;
                ((ToolStripMenuItem)styleMenu.DropDownItems[1]).Checked = cfg.Style == VisStyle.MirrorWave;
                ((ToolStripMenuItem)styleMenu.DropDownItems[2]).Checked = cfg.Style == VisStyle.RadialRing;
                ((ToolStripMenuItem)themeMenu.DropDownItems[0]).Checked = cfg.Theme == ThemePreset.PureCyan;
                ((ToolStripMenuItem)themeMenu.DropDownItems[1]).Checked = cfg.Theme == ThemePreset.AdaptiveCover;
                ((ToolStripMenuItem)themeMenu.DropDownItems[2]).Checked = cfg.Theme == ThemePreset.MatrixGreen;
                ((ToolStripMenuItem)themeMenu.DropDownItems[3]).Checked = cfg.Theme == ThemePreset.NeonViolet;
                ((ToolStripMenuItem)bgMenu.DropDownItems[0]).Checked = cfg.Background == BgStyle.MatrixAndBlur;
                ((ToolStripMenuItem)bgMenu.DropDownItems[1]).Checked = cfg.Background == BgStyle.BlurredCover;
                ((ToolStripMenuItem)bgMenu.DropDownItems[2]).Checked = cfg.Background == BgStyle.PureBlack;
                remoteStatus.Text = "Remote: " + remoteBridge.StatusLabel;
                remoteAuto.Checked = cfg.RemoteServer;
                itemFs.Checked = cfg.Fullscreen;
                itemBorder.Checked = cfg.Borderless;
                itemTop.Checked = cfg.AlwaysOnTop;
                itemHud.Checked = cfg.ShowTrackInfo;
            };
            return m;
        }

        void ApplyWindowStyle()
        {
            TopMost = cfg.AlwaysOnTop;
            if (cfg.Fullscreen)
            {
                var sc = Screen.FromControl(this);
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = sc.Bounds;
            }
            else
            {
                FormBorderStyle = cfg.Borderless ? FormBorderStyle.None : FormBorderStyle.Sizable;
                if (savedNormalBounds.Width >= 480 && savedNormalBounds.Height >= 300)
                    Bounds = savedNormalBounds;
            }
        }

        void ToggleFullscreen()
        {
            if (!cfg.Fullscreen)
            {
                if (WindowState == FormWindowState.Normal) savedNormalBounds = Bounds;
                savedBorderless = cfg.Borderless;
                cfg.Fullscreen = true;
            }
            else
            {
                cfg.Fullscreen = false;
                cfg.Borderless = savedBorderless;
            }
            ApplyWindowStyle();
            cfg.Save();
        }

        void MoveToMonitor(int idx)
        {
            Screen[] screens = Screen.AllScreens;
            if (idx < 0 || idx >= screens.Length) return;
            var sc = screens[idx];
            if (cfg.Fullscreen)
            {
                Bounds = sc.Bounds;
            }
            else
            {
                int w = Math.Min(Width, sc.WorkingArea.Width - 40);
                int h = Math.Min(Height, sc.WorkingArea.Height - 40);
                int x = sc.WorkingArea.Left + (sc.WorkingArea.Width - w) / 2;
                int y = sc.WorkingArea.Top + (sc.WorkingArea.Height - h) / 2;
                Bounds = new Rectangle(x, y, w, h);
                savedNormalBounds = Bounds;
            }
            SaveWindowBounds();
        }

        void SaveWindowBounds()
        {
            if (!cfg.Fullscreen && WindowState == FormWindowState.Normal)
            {
                savedNormalBounds = Bounds;
                cfg.WinX = Left;
                cfg.WinY = Top;
                cfg.WinW = Width;
                cfg.WinH = Height;
                cfg.Save();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            renderer.Render(e.Graphics, ClientSize.Width, ClientSize.Height, cfg, media, audio, hudAlpha);
            if (hudAlpha > 0.05f)
            {
                DrawHoverHud(e.Graphics, ClientSize.Width, ClientSize.Height, hudAlpha);
            }
        }

        void DrawHoverHud(Graphics g, int w, int h, float alpha)
        {
            hudButtons.Clear();
            int a = (int)(alpha * 220f);
            int pad = 16;
            int btnH = 30;
            int y = pad;

            string styleName = cfg.Style == VisStyle.CyberBars ? "Bars" : (cfg.Style == VisStyle.MirrorWave ? "Mirror" : "Radial");
            string themeName = cfg.Theme == ThemePreset.PureCyan ? "Cyan" : (cfg.Theme == ThemePreset.AdaptiveCover ? "Adaptive" : (cfg.Theme == ThemePreset.MatrixGreen ? "Matrix" : "Violet"));
            string bgName = cfg.Background == BgStyle.MatrixAndBlur ? "Matrix BG" : (cfg.Background == BgStyle.BlurredCover ? "Cover BG" : "Black BG");

            var defs = new List<Tuple<string, Action>>
            {
                Tuple.Create(remoteBridge.IsRunning ? "Phone QR" : "Start Remote", (Action)(() => remoteBridge.OpenConnectPage())),
                Tuple.Create("Style: " + styleName, (Action)(() => { cfg.Style = (VisStyle)(((int)cfg.Style + 1) % 3); cfg.Save(); })),
                Tuple.Create("Theme: " + themeName, (Action)(() => { cfg.Theme = (ThemePreset)(((int)cfg.Theme + 1) % 4); cfg.Save(); })),
                Tuple.Create(bgName, (Action)(() => { cfg.Background = (BgStyle)(((int)cfg.Background + 1) % 3); cfg.Save(); })),
                Tuple.Create(cfg.Fullscreen ? "Windowed" : "Fullscreen", (Action)ToggleFullscreen),
                Tuple.Create(cfg.AlwaysOnTop ? "Pinned" : "Pin Top", (Action)(() => { cfg.AlwaysOnTop = !cfg.AlwaysOnTop; TopMost = cfg.AlwaysOnTop; cfg.Save(); })),
                Tuple.Create("<<", (Action)(() => media.SendCommand(MediaCommand.Previous))),
                Tuple.Create(media.IsPlaying ? "Pause" : "Play", (Action)(() => media.SendCommand(MediaCommand.TogglePlayPause))),
                Tuple.Create(">>", (Action)(() => media.SendCommand(MediaCommand.Next)))
            };

            Color accent = renderer.GetAccentColor(cfg, media);
            using (var font = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bgBr = new SolidBrush(Color.FromArgb((int)(alpha * 185f), 5, 16, 24)))
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 160f), accent), 1f))
            using (var txtBr = new SolidBrush(Color.FromArgb(a, 230, 252, 255)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                int right = w - pad;
                for (int i = defs.Count - 1; i >= 0; i--)
                {
                    SizeF sz = g.MeasureString(defs[i].Item1, font);
                    int bw = (int)Math.Ceiling(sz.Width) + 20;
                    int bx = right - bw;
                    if (bx < pad) break;
                    var rect = new Rectangle(bx, y, bw, btnH);
                    hudButtons.Add(new HudButton { Rect = rect, Label = defs[i].Item1, OnClick = defs[i].Item2 });
                    using (var gp = VisualizerRenderer.RoundedRect(rect, 7))
                    {
                        g.FillPath(bgBr, gp);
                        g.DrawPath(pen, gp);
                    }
                    g.DrawString(defs[i].Item1, font, txtBr, rect, sf);
                    right = bx - 8;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Location == lastMouseClientPos) return;
            lastMouseClientPos = e.Location;
            lastMouseMoveUtc = DateTime.UtcNow;
            SetCursorHidden(false);
            bool overBtn = false;
            if (hudAlpha > 0.1f)
            {
                foreach (var b in hudButtons)
                {
                    if (b.Rect.Contains(e.Location)) { overBtn = true; break; }
                }
            }
            Cursor = overBtn ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetCursorHidden(false);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            SetCursorHidden(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            lastMouseMoveUtc = DateTime.UtcNow;
            SetCursorHidden(false);
            if (e.Button == MouseButtons.Left)
            {
                if (hudAlpha > 0.1f)
                {
                    foreach (var b in hudButtons)
                    {
                        if (b.Rect.Contains(e.Location))
                        {
                            b.OnClick();
                            Invalidate();
                            return;
                        }
                    }
                }
                if (e.Clicks == 2)
                {
                    ToggleFullscreen();
                    return;
                }
                if (cfg.Borderless && !cfg.Fullscreen)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0x00A1, 2, 0); // WM_NCLBUTTONDOWN HTCAPTION
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Allow resizing borderless window from 8px edges
            if (m.Msg == 0x0084 && cfg.Borderless && !cfg.Fullscreen) // WM_NCHITTEST
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));
                const int grip = 10;
                bool left = p.X < grip, right = p.X >= ClientSize.Width - grip;
                bool top = p.Y < grip, bottom = p.Y >= ClientSize.Height - grip;
                if (top && left) { m.Result = (IntPtr)13; return; }
                if (top && right) { m.Result = (IntPtr)14; return; }
                if (bottom && left) { m.Result = (IntPtr)16; return; }
                if (bottom && right) { m.Result = (IntPtr)17; return; }
                if (left) { m.Result = (IntPtr)10; return; }
                if (right) { m.Result = (IntPtr)11; return; }
                if (top) { m.Result = (IntPtr)12; return; }
                if (bottom) { m.Result = (IntPtr)15; return; }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            lastMouseMoveUtc = DateTime.UtcNow;
            if (e.KeyCode == Keys.F11) ToggleFullscreen();
            else if (e.KeyCode == Keys.Escape && cfg.Fullscreen) ToggleFullscreen();
            else if (e.KeyCode == Keys.V) { cfg.Style = (VisStyle)(((int)cfg.Style + 1) % 3); cfg.Save(); }
            else if (e.KeyCode == Keys.M) { cfg.Theme = (ThemePreset)(((int)cfg.Theme + 1) % 4); cfg.Save(); }
            else if (e.KeyCode == Keys.G) { cfg.Background = (BgStyle)(((int)cfg.Background + 1) % 3); cfg.Save(); }
            else if (e.KeyCode == Keys.B) { cfg.Borderless = !cfg.Borderless; ApplyWindowStyle(); cfg.Save(); }
            else if (e.KeyCode == Keys.T) { cfg.AlwaysOnTop = !cfg.AlwaysOnTop; TopMost = cfg.AlwaysOnTop; cfg.Save(); }
            else if (e.KeyCode == Keys.H) { cfg.ShowTrackInfo = !cfg.ShowTrackInfo; cfg.Save(); }
            else if (e.KeyCode == Keys.R) remoteBridge.OpenConnectPage();
            else if (e.KeyCode == Keys.Space) media.SendCommand(MediaCommand.TogglePlayPause);
            else if (e.KeyCode == Keys.Left) media.SendCommand(MediaCommand.Previous);
            else if (e.KeyCode == Keys.Right) media.SendCommand(MediaCommand.Next);
            else if (e.KeyCode == Keys.D1) MoveToMonitor(0);
            else if (e.KeyCode == Keys.D2) MoveToMonitor(1);
            Invalidate();
        }

        protected override void OnMove(EventArgs e) { base.OnMove(e); if (Visible) SaveWindowBounds(); }
        protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); if (Visible) SaveWindowBounds(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            stopping = true;
            SetCursorHidden(false);
            SaveWindowBounds();
            if (frameTimer != null) frameTimer.Stop();
            if (tray != null) tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopping = true;
                try { timeEndPeriod(1); } catch { }
                if (frameTimer != null) frameTimer.Dispose();
                if (remoteBridge != null) remoteBridge.Dispose();
                if (renderer != null) renderer.Dispose();
                if (audio != null) audio.Dispose();
                if (media != null) media.Dispose();
                if (tray != null) tray.Dispose();
                if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        static Icon CreateAppIcon()
        {
            try
            {
                Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embedded != null) return embedded;
            }
            catch { }
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(4, 14, 22));
                using (var pen = new Pen(Color.FromArgb(0, 255, 255), 2.5f))
                    g.DrawRectangle(pen, 2, 2, 27, 27);
                using (var br = new SolidBrush(Color.FromArgb(0, 255, 255)))
                {
                    g.FillRectangle(br, 7, 16, 4, 10);
                    g.FillRectangle(br, 14, 7, 4, 19);
                    g.FillRectangle(br, 21, 11, 4, 15);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    sealed class DarkCyanMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Color.FromArgb(0, 72, 92); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(0, 220, 220); } }
        public override Color MenuBorder { get { return Color.FromArgb(0, 140, 165); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(6, 18, 26); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(6, 18, 26); } }
        public override Color SeparatorDark { get { return Color.FromArgb(14, 70, 88); } }
    }

    // =========================================================================
    // CYBERPUNK MONITOR RENDERER (60 FPS CACHED BG + SPRITE MATRIX RAIN)
    // =========================================================================
    sealed class VisualizerRenderer : IDisposable
    {
        sealed class RainDrop
        {
            public float X, Y, Speed;
            public int Length;
            public float Intensity;
            public int[] GlyphIdx;
        }

        readonly List<RainDrop> drops = new List<RainDrop>();
        readonly Random rng = new Random();
        int lastW, lastH;
        const string GlyphPool = "0123456789ABCDEF+-=<>*#$";

        // Cached full-screen background (avoids per-frame 2560x1440 bicubic + PathGradientBrush + huge FillPath!)
        Bitmap cachedBg;
        int bgW, bgH, bgCoverVer = -1, bgAccentArgb;
        BgStyle bgStyle;
        VisStyle bgVisStyle;
        bool bgShowTrack;

        // Cached scaled/clipped album covers (avoids per-frame bicubic + SetClip)
        Bitmap cachedRectCover;
        int rectArtSize, rectCoverVer = -1, rectAccentArgb;
        Bitmap cachedCircleCover;
        int circleArtSize, circleCoverVer = -1, circleAccentArgb;

        // Cached Matrix rain glyph sprites [glyphIndex, tier 0..3] (avoids 600x DrawString/frame)
        Bitmap[,] glyphSprites;
        int spriteFontPx, spriteAccentArgb;

        void EnsureRain(int w, int h)
        {
            if (w == lastW && h == lastH && drops.Count > 0) return;
            lastW = w; lastH = h;
            drops.Clear();
            int cols = Math.Min(34, Math.Max(16, w / 56));
            for (int i = 0; i < cols; i++)
            {
                drops.Add(MakeDrop(i * (w / (float)cols) + 12f, (float)(rng.NextDouble() * h)));
            }
        }

        RainDrop MakeDrop(float x, float y)
        {
            int len = rng.Next(7, 14);
            int[] idx = new int[len];
            for (int i = 0; i < len; i++) idx[i] = rng.Next(GlyphPool.Length);
            return new RainDrop
            {
                X = x,
                Y = y,
                Speed = 1.2f + (float)rng.NextDouble() * 2.5f,
                Length = len,
                Intensity = 0.40f + (float)rng.NextDouble() * 0.60f,
                GlyphIdx = idx
            };
        }

        void EnsureBackgroundCache(int w, int h, VisConfig cfg, Color accent, MediaReader media)
        {
            int ver = media.CoverVersion;
            int accArgb = accent.ToArgb();
            if (cachedBg != null && bgW == w && bgH == h && bgStyle == cfg.Background &&
                bgVisStyle == cfg.Style && bgShowTrack == cfg.ShowTrackInfo &&
                bgCoverVer == ver && bgAccentArgb == accArgb)
                return;

            if (cachedBg != null) { cachedBg.Dispose(); cachedBg = null; }
            bgW = w; bgH = h; bgStyle = cfg.Background; bgVisStyle = cfg.Style; bgShowTrack = cfg.ShowTrackInfo;
            bgCoverVer = ver; bgAccentArgb = accArgb;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                if (cfg.Background == BgStyle.PureBlack)
                {
                    g.Clear(Color.Black);
                }
                else
                {
                    g.Clear(Color.FromArgb(3, 8, 13));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    lock (media.CoverLock)
                    {
                        if (media.BlurredBg != null)
                        {
                            g.DrawImage(media.BlurredBg, new Rectangle(0, 0, w, h));
                            int overlayAlpha = cfg.Background == BgStyle.BlurredCover ? 155 : 205;
                            using (var ov = new SolidBrush(Color.FromArgb(overlayAlpha, 3, 10, 16)))
                                g.FillRectangle(ov, 0, 0, w, h);
                        }
                    }

                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(-w * 0.1f, h * 0.45f, w * 1.2f, h * 0.9f);
                        using (var pgb = new PathGradientBrush(gp))
                        {
                            pgb.CenterColor = Color.FromArgb(46, accent);
                            pgb.SurroundColors = new[] { Color.Transparent };
                            g.FillPath(pgb, gp);
                        }
                    }
                }

                // Bake static glass card panels into cachedBg (eliminates 2.2M-pixel FillPath per frame!)
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int pad = Math.Max(24, Math.Min(w, h) / 22);
                if (cfg.Style == VisStyle.CyberBars || cfg.Style == VisStyle.MirrorWave)
                {
                    int topEnd = pad + 44;
                    if (cfg.ShowTrackInfo)
                    {
                        int cardH = cfg.Style == VisStyle.CyberBars
                            ? Math.Max(110, Math.Min(230, (int)(h * 0.27f)))
                            : Math.Max(104, Math.Min(200, (int)(h * 0.24f)));
                        var cardRect = new Rectangle(pad, pad + 34, w - pad * 2, cardH);
                        using (var gp = RoundedRect(cardRect, 16))
                        using (var bg = new SolidBrush(Color.FromArgb(155, 5, 16, 25)))
                        using (var border = new Pen(Color.FromArgb(145, accent), 1.5f))
                        {
                            g.FillPath(bg, gp);
                            g.DrawPath(border, gp);
                        }
                        topEnd = cardRect.Bottom + pad;
                    }
                    if (cfg.Style == VisStyle.CyberBars)
                    {
                        var specRect = new Rectangle(pad, topEnd, w - pad * 2, Math.Max(120, h - topEnd - pad));
                        using (var gp = RoundedRect(specRect, 16))
                        using (var bg = new SolidBrush(Color.FromArgb(145, 4, 14, 22)))
                        using (var border = new Pen(Color.FromArgb(130, accent), 1.5f))
                        {
                            g.FillPath(bg, gp);
                            g.DrawPath(border, gp);
                        }
                    }
                }
            }
            cachedBg = bmp;
        }

        void EnsureGlyphSprites(int fontPx, Color accent)
        {
            int accArgb = accent.ToArgb();
            if (glyphSprites != null && spriteFontPx == fontPx && spriteAccentArgb == accArgb) return;
            DisposeGlyphSprites();
            spriteFontPx = fontPx;
            spriteAccentArgb = accArgb;

            int cellW = Math.Max(12, (int)(fontPx * 0.85f) + 2);
            int cellH = Math.Max(14, (int)(fontPx * 1.15f) + 2);
            Color[] tierColors =
            {
                Color.FromArgb(115, 220, 255, 255), // 0 = bright head
                Color.FromArgb(62, accent),         // 1 = upper tail
                Color.FromArgb(34, accent),         // 2 = mid tail
                Color.FromArgb(16, accent)          // 3 = dim tail
            };

            glyphSprites = new Bitmap[GlyphPool.Length, 4];
            using (var font = new Font("Consolas", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                for (int t = 0; t < 4; t++)
                {
                    using (var br = new SolidBrush(tierColors[t]))
                    {
                        for (int c = 0; c < GlyphPool.Length; c++)
                        {
                            var b = new Bitmap(cellW, cellH, PixelFormat.Format32bppPArgb);
                            using (var gg = Graphics.FromImage(b))
                            {
                                gg.TextRenderingHint = TextRenderingHint.AntiAlias;
                                gg.Clear(Color.Transparent);
                                gg.DrawString(GlyphPool[c].ToString(), font, br, 0, 0);
                            }
                            glyphSprites[c, t] = b;
                        }
                    }
                }
            }
        }

        Bitmap GetCachedRectCover(int artSize, Color accent, MediaReader media)
        {
            int ver = media.CoverVersion;
            int accArgb = accent.ToArgb();
            if (cachedRectCover != null && rectArtSize == artSize && rectCoverVer == ver && rectAccentArgb == accArgb)
                return cachedRectCover;

            if (cachedRectCover != null) { cachedRectCover.Dispose(); cachedRectCover = null; }
            rectArtSize = artSize; rectCoverVer = ver; rectAccentArgb = accArgb;

            var bmp = new Bitmap(artSize + 8, artSize + 8, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                var r = new Rectangle(4, 4, artSize, artSize);
                using (var clip = RoundedRect(r, 12))
                {
                    g.SetClip(clip);
                    lock (media.CoverLock)
                    {
                        if (media.CoverArt != null) g.DrawImage(media.CoverArt, r);
                        else
                        {
                            using (var br = new SolidBrush(Color.FromArgb(10, 30, 42)))
                                g.FillRectangle(br, r);
                        }
                    }
                    g.ResetClip();
                    using (var pen = new Pen(accent, 4f)) g.DrawPath(pen, clip);
                }
            }
            cachedRectCover = bmp;
            return cachedRectCover;
        }

        Bitmap GetCachedCircleCover(int dia, Color accent, MediaReader media)
        {
            // Quantize diameter to 4px steps so bass pulse doesn't rebuild every frame
            int qDia = Math.Max(64, (dia / 4) * 4);
            int ver = media.CoverVersion;
            int accArgb = accent.ToArgb();
            if (cachedCircleCover != null && circleArtSize == qDia && circleCoverVer == ver && circleAccentArgb == accArgb)
                return cachedCircleCover;

            if (cachedCircleCover != null) { cachedCircleCover.Dispose(); cachedCircleCover = null; }
            circleArtSize = qDia; circleCoverVer = ver; circleAccentArgb = accArgb;

            var bmp = new Bitmap(qDia + 10, qDia + 10, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                var r = new RectangleF(5, 5, qDia, qDia);
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(r);
                    g.SetClip(clip);
                    lock (media.CoverLock)
                    {
                        if (media.CoverArt != null) g.DrawImage(media.CoverArt, r);
                        else
                        {
                            using (var br = new SolidBrush(Color.FromArgb(10, 28, 38)))
                                g.FillEllipse(br, r);
                        }
                    }
                    g.ResetClip();
                }
                using (var pen = new Pen(accent, 4f)) g.DrawEllipse(pen, r);
            }
            cachedCircleCover = bmp;
            return cachedCircleCover;
        }

        public Color GetAccentColor(VisConfig cfg, MediaReader media)
        {
            if (cfg.Theme == ThemePreset.MatrixGreen) return Color.FromArgb(0, 255, 110);
            if (cfg.Theme == ThemePreset.NeonViolet) return Color.FromArgb(196, 84, 255);
            if (cfg.Theme == ThemePreset.AdaptiveCover && media.HasAdaptiveAccent) return media.AdaptiveAccent;
            return Color.FromArgb(0, 255, 255);
        }

        public void Render(Graphics g, int w, int h, VisConfig cfg, MediaReader media, WasapiCava audio, float hudAlpha)
        {
            if (w < 64 || h < 64) return;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Low;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color accent = GetAccentColor(cfg, media);
            Color ice = Color.FromArgb(230, 252, 255);
            Color deepAccent = Color.FromArgb(accent.R / 4, accent.G / 4, accent.B / 4);

            // 1. Ultra-fast cached background blit (< 0.5 ms at 1440p)
            EnsureBackgroundCache(w, h, cfg, accent, media);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(cachedBg, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;

            if (cfg.Background == BgStyle.MatrixAndBlur)
            {
                DrawMatrixRain(g, w, h, accent, audio.BassEnergy);
            }

            // 2. Visualizer Mode
            if (cfg.Style == VisStyle.CyberBars)
            {
                DrawCyberBarsLayout(g, w, h, cfg, media, audio, accent, deepAccent, ice);
            }
            else if (cfg.Style == VisStyle.MirrorWave)
            {
                DrawMirrorWaveLayout(g, w, h, cfg, media, audio, accent, deepAccent, ice);
            }
            else
            {
                DrawRadialRingLayout(g, w, h, cfg, media, audio, accent, deepAccent, ice);
            }
        }

        void DrawMatrixRain(Graphics g, int w, int h, Color accent, float bass)
        {
            EnsureRain(w, h);
            float step = Math.Max(18f, h / 40f);
            int fontPx = Math.Max(12, (int)(step * 0.70f));
            EnsureGlyphSprites(fontPx, accent);

            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                d.Y += d.Speed * (1f + bass * 0.75f);
                if (d.Y - d.Length * step > h + 24)
                {
                    drops[i] = MakeDrop(d.X, -rng.Next(20, 180));
                    d = drops[i];
                }
                int dx = (int)d.X;
                for (int k = 0; k < d.Length; k++)
                {
                    float cy = d.Y - k * step;
                    if (cy < -step || cy > h) continue;
                    int tier = k == 0 ? 0 : (k < d.Length / 3 ? 1 : (k < (d.Length * 2) / 3 ? 2 : 3));
                    g.DrawImageUnscaled(glyphSprites[d.GlyphIdx[k], tier], dx, (int)cy);
                }
            }
        }

        void DrawCyberBarsLayout(Graphics g, int w, int h, VisConfig cfg, MediaReader media, WasapiCava audio, Color accent, Color deepAccent, Color ice)
        {
            int pad = Math.Max(24, Math.Min(w, h) / 22);
            int topEnd = pad;

            if (cfg.ShowTrackInfo)
            {
                int cardH = Math.Max(110, Math.Min(230, (int)(h * 0.27f)));
                var cardRect = new Rectangle(pad, pad + 34, w - pad * 2, cardH);
                DrawTrackCard(g, cardRect, media, accent, ice);
                topEnd = cardRect.Bottom + pad;
            }
            else
            {
                topEnd = pad + 44;
            }

            var specRect = new Rectangle(pad, topEnd, w - pad * 2, Math.Max(120, h - topEnd - pad));

            int innerPad = Math.Max(16, specRect.Width / 48);
            int areaX = specRect.X + innerPad;
            int areaW = specRect.Width - innerPad * 2;
            int baseY = specRect.Bottom - innerPad - (int)(specRect.Height * 0.10f);
            int maxBarH = Math.Max(40, baseY - (specRect.Y + innerPad));
            int reflH = Math.Max(12, specRect.Bottom - innerPad - baseY);

            int bars = Math.Min(audio.NumBars, Math.Max(32, areaW / 14));
            float step = (float)areaW / bars;
            float barW = Math.Max(4f, step * 0.68f);

            // Axis-aligned rectangles don't need AA -> huge GDI+ speed win
            g.SmoothingMode = SmoothingMode.HighSpeed;
            using (var grad = new LinearGradientBrush(new Point(0, baseY - maxBarH), new Point(0, baseY), ice, accent))
            using (var reflBr = new SolidBrush(Color.FromArgb(42, accent)))
            using (var capBr = new SolidBrush(ice))
            using (var glowBr = new SolidBrush(Color.FromArgb(32, accent)))
            using (var linePen = new Pen(Color.FromArgb(95, accent), 1f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { ice, accent, Color.FromArgb(200, Math.Max(0, accent.R / 3), Math.Max(40, accent.G / 2), Math.Max(60, accent.B / 2)) };
                blend.Positions = new[] { 0f, 0.35f, 1f };
                grad.InterpolationColors = blend;

                g.DrawLine(linePen, areaX, baseY + 1, areaX + areaW, baseY + 1);

                for (int i = 0; i < bars; i++)
                {
                    int srcIdx = (int)((float)i / bars * audio.NumBars);
                    float v = audio.Bars[srcIdx];
                    float pk = audio.Peaks[srcIdx];
                    int bh = Math.Max(3, (int)(v * maxBarH));
                    int bx = (int)(areaX + i * step + (step - barW) * 0.5f);
                    int bw = Math.Max(4, (int)barW);
                    int by = baseY - bh;

                    if (v > 0.25f) g.FillRectangle(glowBr, bx - 2, by - 3, bw + 4, bh + 3);
                    g.FillRectangle(grad, bx, by, bw, bh);

                    int rh = Math.Min(reflH, (int)(bh * 0.26f));
                    if (rh > 2) g.FillRectangle(reflBr, bx, baseY + 3, bw, rh);

                    int py = baseY - Math.Max(3, (int)(pk * maxBarH)) - 4;
                    g.FillRectangle(capBr, bx, py, bw, 3);
                }
            }
        }

        void DrawMirrorWaveLayout(Graphics g, int w, int h, VisConfig cfg, MediaReader media, WasapiCava audio, Color accent, Color deepAccent, Color ice)
        {
            int pad = Math.Max(24, Math.Min(w, h) / 22);
            int topEnd = pad + 40;
            if (cfg.ShowTrackInfo)
            {
                int cardH = Math.Max(104, Math.Min(200, (int)(h * 0.24f)));
                var cardRect = new Rectangle(pad, pad + 34, w - pad * 2, cardH);
                DrawTrackCard(g, cardRect, media, accent, ice);
                topEnd = cardRect.Bottom + pad;
            }

            int areaY = topEnd;
            int areaH = Math.Max(140, h - areaY - pad);
            int midY = areaY + areaH / 2;
            int halfH = Math.Max(40, (areaH / 2) - 18);
            int areaX = pad + 16;
            int areaW = w - (pad + 16) * 2;

            int halfBars = Math.Min(48, Math.Max(20, areaW / 24));
            int totalBars = halfBars * 2;
            float step = (float)areaW / totalBars;
            float barW = Math.Max(4f, step * 0.66f);

            g.SmoothingMode = SmoothingMode.HighSpeed;
            using (var glowPen = new Pen(Color.FromArgb(140, accent), 2f))
            using (var topGrad = new LinearGradientBrush(new Point(0, midY - halfH), new Point(0, midY), ice, accent))
            using (var botGrad = new LinearGradientBrush(new Point(0, midY), new Point(0, midY + halfH), accent, Color.FromArgb(90, deepAccent)))
            using (var capBr = new SolidBrush(ice))
            {
                g.DrawLine(glowPen, areaX, midY, areaX + areaW, midY);
                for (int i = 0; i < totalBars; i++)
                {
                    int distFromCenter = Math.Abs(i - halfBars);
                    int srcIdx = Math.Min(audio.NumBars - 1, (int)((float)distFromCenter / halfBars * (audio.NumBars * 0.85f)));
                    float v = audio.Bars[srcIdx];
                    float pk = audio.Peaks[srcIdx];
                    int bh = Math.Max(3, (int)(v * halfH));
                    int bx = (int)(areaX + i * step + (step - barW) * 0.5f);
                    int bw = Math.Max(4, (int)barW);

                    g.FillRectangle(topGrad, bx, midY - bh - 2, bw, bh);
                    g.FillRectangle(botGrad, bx, midY + 2, bw, (int)(bh * 0.82f));

                    int py = midY - Math.Max(4, (int)(pk * halfH)) - 6;
                    g.FillRectangle(capBr, bx, py, bw, 3);
                }
            }
        }

        void DrawRadialRingLayout(Graphics g, int w, int h, VisConfig cfg, MediaReader media, WasapiCava audio, Color accent, Color deepAccent, Color ice)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = w / 2;
            int cy = cfg.ShowTrackInfo ? (int)(h * 0.44f) : (h / 2);
            int minDim = Math.Min(w, h);
            float baseR = Math.Max(70f, minDim * (cfg.ShowTrackInfo ? 0.16f : 0.21f));
            float pulse = baseR * (1f + audio.BassEnergy * 0.06f);
            float maxRay = Math.Max(55f, minDim * 0.18f);

            using (var ringPen = new Pen(Color.FromArgb(65, accent), 2f))
                g.DrawEllipse(ringPen, cx - pulse - 6, cy - pulse - 6, (pulse + 6) * 2, (pulse + 6) * 2);

            int count = audio.NumBars;
            int half = count / 2;
            using (var rayPen = new Pen(accent, Math.Max(3f, minDim / 250f)))
            using (var peakBr = new SolidBrush(ice))
            {
                rayPen.StartCap = LineCap.Round;
                rayPen.EndCap = LineCap.Round;
                for (int i = 0; i < count; i++)
                {
                    double ang = (2.0 * Math.PI * i / count) - Math.PI / 2.0;
                    float cos = (float)Math.Cos(ang);
                    float sin = (float)Math.Sin(ang);
                    int sym = i < half ? i : (count - 1 - i);
                    int srcIdx = Math.Min(audio.NumBars - 1, (int)((float)sym / half * (audio.NumBars * 0.90f)));
                    float v = audio.Bars[srcIdx];
                    float pk = audio.Peaks[srcIdx];
                    float len = Math.Max(6f, v * maxRay);

                    float x0 = cx + cos * (pulse + 6f);
                    float y0 = cy + sin * (pulse + 6f);
                    float x1 = cx + cos * (pulse + 6f + len);
                    float y1 = cy + sin * (pulse + 6f + len);

                    rayPen.Color = Color.FromArgb(Math.Min(255, 145 + (int)(v * 110f)), accent);
                    g.DrawLine(rayPen, x0, y0, x1, y1);

                    float px = cx + cos * (pulse + 12f + pk * maxRay);
                    float py = cy + sin * (pulse + 12f + pk * maxRay);
                    g.FillEllipse(peakBr, px - 2.2f, py - 2.2f, 4.4f, 4.4f);
                }
            }

            // Pre-cached circular album art blit (zero per-frame scaling / SetClip!)
            Bitmap circ = GetCachedCircleCover((int)(baseR * 2f), accent, media);
            g.DrawImageUnscaled(circ, cx - circ.Width / 2, cy - circ.Height / 2);

            if (cfg.ShowTrackInfo)
            {
                int infoY = (int)(cy + pulse + maxRay * 0.68f);
                int infoW = Math.Min(820, w - 64);
                int infoX = (w - infoW) / 2;
                float titlePx = Math.Max(22f, minDim * 0.036f);
                float subPx = Math.Max(14f, minDim * 0.021f);
                int titleH = (int)(titlePx * 1.38f);
                int subH = (int)(subPx * 1.45f);
                if (infoY + titleH + subH + 34 < h)
                {
                    string title = string.IsNullOrEmpty(media.Title) ? "WAITING FOR AUDIO..." : media.Title;
                    string sub = string.IsNullOrEmpty(media.Artist) ? "Play music on Spotify, YouTube, or any app" : media.Artist;
                    using (var fTitle = new Font("Segoe UI", titlePx, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var fSub = new Font("Segoe UI", subPx, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var bIce = new SolidBrush(ice))
                    using (var bAcc = new SolidBrush(accent))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        g.DrawString(title, fTitle, bIce, new RectangleF(infoX, infoY, infoW, titleH), sf);
                        g.DrawString(sub, fSub, bAcc, new RectangleF(infoX, infoY + titleH, infoW, subH), sf);
                    }
                    DrawProgressBar(g, new Rectangle(infoX + 40, infoY + titleH + subH + 8, infoW - 80, 26), media, accent, ice);
                }
            }
        }

        void DrawTrackCard(Graphics g, Rectangle rect, MediaReader media, Color accent, Color ice)
        {
            int pad = Math.Max(14, rect.Height / 11);
            int artSize = Math.Max(64, rect.Height - pad * 2);
            var artRect = new Rectangle(rect.X + pad, rect.Y + pad, artSize, artSize);

            Bitmap coverBmp = GetCachedRectCover(artSize, accent, media);
            g.DrawImageUnscaled(coverBmp, artRect.X - 4, artRect.Y - 4);

            int tx = artRect.Right + Math.Max(18, pad + 6);
            int tw = Math.Max(120, rect.Right - tx - pad);
            string tag = media.IsPlaying ? "NOW PLAYING" : (string.IsNullOrEmpty(media.Title) ? "STANDBY · WASAPI LOOPBACK" : "PAUSED");
            string title = string.IsNullOrEmpty(media.Title) ? "Listening for System Audio..." : media.Title;
            string artist = string.IsNullOrEmpty(media.Artist) ? "Play any music or video to animate" : media.Artist;
            string album = media.Album ?? "";

            float scale = Math.Max(0.75f, Math.Min(1.30f, rect.Height / 195f));
            float tagPx = 14f * scale;
            float titlePx = 38f * scale;
            float artistPx = 22f * scale;
            float albumPx = 16f * scale;
            using (var fTag = new Font("Segoe UI", tagPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fTitle = new Font("Segoe UI", titlePx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fArtist = new Font("Segoe UI", artistPx, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fAlbum = new Font("Segoe UI", albumPx, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bAcc = new SolidBrush(accent))
            using (var bIce = new SolidBrush(ice))
            using (var bDim = new SolidBrush(Color.FromArgb(165, 212, 226)))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float y = artRect.Top;
                float tagH = tagPx * 1.35f;
                float titleH = titlePx * 1.32f;
                float artistH = artistPx * 1.35f;
                float albumH = albumPx * 1.35f;
                g.DrawString(tag, fTag, bAcc, new RectangleF(tx, y, tw, tagH), sf);
                g.DrawString(title, fTitle, bIce, new RectangleF(tx, y + tagH, tw, titleH), sf);
                g.DrawString(artist, fArtist, bAcc, new RectangleF(tx, y + tagH + titleH, tw, artistH), sf);
                if (!string.IsNullOrEmpty(album) && (y + tagH + titleH + artistH + albumH < artRect.Bottom - 28))
                    g.DrawString(album, fAlbum, bDim, new RectangleF(tx, y + tagH + titleH + artistH, tw, albumH), sf);
            }

            DrawProgressBar(g, new Rectangle(tx, artRect.Bottom - 26, tw, 26), media, accent, ice);
        }

        void DisposeGlyphSprites()
        {
            if (glyphSprites == null) return;
            for (int c = 0; c < glyphSprites.GetLength(0); c++)
                for (int t = 0; t < glyphSprites.GetLength(1); t++)
                    if (glyphSprites[c, t] != null) glyphSprites[c, t].Dispose();
            glyphSprites = null;
        }

        public void Dispose()
        {
            if (cachedBg != null) { cachedBg.Dispose(); cachedBg = null; }
            if (cachedRectCover != null) { cachedRectCover.Dispose(); cachedRectCover = null; }
            if (cachedCircleCover != null) { cachedCircleCover.Dispose(); cachedCircleCover = null; }
            DisposeGlyphSprites();
        }

        static void DrawProgressBar(Graphics g, Rectangle rect, MediaReader media, Color accent, Color ice)
        {
            double frac = 0.0;
            if (media.Duration.TotalSeconds > 1.0)
                frac = Math.Max(0.0, Math.Min(1.0, media.Position.TotalSeconds / media.Duration.TotalSeconds));

            int barY = rect.Y + 4;
            int barH = 5;
            using (var trackBr = new SolidBrush(Color.FromArgb(48, accent)))
            using (var fillBr = new SolidBrush(accent))
            using (var knobBr = new SolidBrush(ice))
            using (var fTime = new Font("Consolas", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sfRight = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.FillRectangle(trackBr, rect.X, barY, rect.Width, barH);
                int fw = (int)(rect.Width * frac);
                if (fw > 0)
                {
                    g.FillRectangle(fillBr, rect.X, barY, fw, barH);
                    g.FillEllipse(knobBr, rect.X + fw - 5, barY - 3, 11, 11);
                }
                if (media.Duration.TotalSeconds > 1.0)
                {
                    string cur = FormatTime(media.Position);
                    string tot = FormatTime(media.Duration);
                    g.DrawString(cur, fTime, knobBr, rect.X, barY + 9);
                    g.DrawString(tot, fTime, knobBr, new RectangleF(rect.X, barY + 9, rect.Width, 18), sfRight);
                }
            }
        }

        static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1.0)
                return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
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

    // =========================================================================
    // WINDOWS MEDIA SESSION READER + PLAYBACK CONTROLS
    // =========================================================================
    enum MediaCommand { None, TogglePlayPause, Next, Previous }

    sealed class MediaReader : IDisposable
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public bool IsPlaying;
        public TimeSpan Position = TimeSpan.Zero;
        public TimeSpan Duration = TimeSpan.Zero;
        public Bitmap CoverArt;
        public Bitmap BlurredBg;
        public Color AdaptiveAccent = Color.FromArgb(0, 255, 255);
        public bool HasAdaptiveAccent;
        public volatile int CoverVersion;
        public readonly object CoverLock = new object();

        string lastTrackKey = "";
        ulong loadedThumbHash = 0;
        bool hasLoadedThumb = false;
        int pendingThumbChecks = 0;
        DateTime nextThumbCheckUtc = DateTime.MinValue;
        volatile bool propsChangedSignal = false;
        int pendingCommand = 0;
        GlobalSystemMediaTransportControlsSessionManager mgr;
        GlobalSystemMediaTransportControlsSession subscribedSession;

        public void SendCommand(MediaCommand cmd)
        {
            pendingCommand = (int)cmd;
        }

        static T WaitWinRT<T>(IAsyncOperation<T> op, int timeoutMs = 600)
        {
            int waited = 0;
            while (op.Status == AsyncStatus.Started && waited < timeoutMs) { Thread.Sleep(10); waited += 10; }
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        static T WaitWinRTProg<T, P>(IAsyncOperationWithProgress<T, P> op, int timeoutMs = 600)
        {
            int waited = 0;
            while (op.Status == AsyncStatus.Started && waited < timeoutMs) { Thread.Sleep(10); waited += 10; }
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        void OnPropsChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            propsChangedSignal = true;
        }

        void EnsureSubscription(GlobalSystemMediaTransportControlsSession session)
        {
            if (object.ReferenceEquals(session, subscribedSession)) return;
            if (subscribedSession != null)
            {
                try { subscribedSession.MediaPropertiesChanged -= OnPropsChanged; } catch { }
                subscribedSession = null;
            }
            if (session != null)
            {
                try { session.MediaPropertiesChanged += OnPropsChanged; subscribedSession = session; } catch { }
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
                EnsureSubscription(session);
                if (session == null) { IsPlaying = false; return; }

                int cmd = Interlocked.Exchange(ref pendingCommand, 0);
                if (cmd == (int)MediaCommand.TogglePlayPause) WaitWinRT(session.TryTogglePlayPauseAsync(), 400);
                else if (cmd == (int)MediaCommand.Next) WaitWinRT(session.TrySkipNextAsync(), 400);
                else if (cmd == (int)MediaCommand.Previous) WaitWinRT(session.TrySkipPreviousAsync(), 400);

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
                        if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(10)) pos += elapsed;
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
                    if (string.IsNullOrEmpty(Title))
                    {
                        if (!string.IsNullOrEmpty(lastTrackKey)) { lastTrackKey = ""; ClearCover(); }
                        return;
                    }

                    string key = Title + "|" + Artist + "|" + Album;
                    if (key != lastTrackKey)
                    {
                        lastTrackKey = key;
                        pendingThumbChecks = 10;
                        nextThumbCheckUtc = DateTime.UtcNow;
                        propsChangedSignal = false;
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
                EnsureSubscription(null);
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
                HasAdaptiveAccent = false;
                CoverVersion++;
            }
        }

        static ulong ComputeFnv1a64(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 1099511628211UL; }
            return h;
        }

        void RefreshThumbnailIfChanged(IRandomAccessStreamReference thumbRef)
        {
            if (thumbRef == null) { ClearCover(); return; }
            try
            {
                using (var stream = WaitWinRT(thumbRef.OpenReadAsync(), 1500))
                {
                    if (stream == null || stream.Size == 0) { ClearCover(); return; }
                    uint sz = (uint)stream.Size;
                    var buf = new Windows.Storage.Streams.Buffer(sz);
                    var readBuf = WaitWinRTProg(stream.ReadAsync(buf, sz, InputStreamOptions.None), 1500);
                    if (readBuf == null) readBuf = buf;
                    using (var dr = DataReader.FromBuffer(readBuf))
                    {
                        byte[] bytes = new byte[readBuf.Length];
                        dr.ReadBytes(bytes);
                        if (bytes.Length == 0) { ClearCover(); return; }
                        ulong hash = ComputeFnv1a64(bytes);
                        if (hasLoadedThumb && hash == loadedThumbHash) return;

                        using (var ms = new MemoryStream(bytes))
                        using (var raw = new Bitmap(ms))
                        {
                            var newCover = new Bitmap(raw, new Size(512, 512));
                            var newBg = CreateBlurredBackdrop(raw, 640, 360);
                            Color extracted = ExtractVibrantAccent(raw);
                            lock (CoverLock)
                            {
                                if (CoverArt != null) CoverArt.Dispose();
                                if (BlurredBg != null) BlurredBg.Dispose();
                                CoverArt = newCover;
                                BlurredBg = newBg;
                                AdaptiveAccent = extracted;
                                HasAdaptiveAccent = true;
                                loadedThumbHash = hash;
                                hasLoadedThumb = true;
                                CoverVersion++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static Bitmap CreateBlurredBackdrop(Bitmap src, int w, int h)
        {
            using (var tiny = new Bitmap(28, 16))
            {
                using (var g = Graphics.FromImage(tiny))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, 28, 16));
                }
                var bg = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bg))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(tiny, new Rectangle(0, 0, w, h));
                }
                return bg;
            }
        }

        static Color ExtractVibrantAccent(Bitmap src)
        {
            try
            {
                using (var small = new Bitmap(src, new Size(24, 24)))
                {
                    Color best = Color.FromArgb(0, 255, 255);
                    float bestScore = -1f;
                    for (int y = 0; y < 24; y++)
                    {
                        for (int x = 0; x < 24; x++)
                        {
                            Color c = small.GetPixel(x, y);
                            float sat = c.GetSaturation();
                            float bri = c.GetBrightness();
                            if (bri < 0.25f || bri > 0.88f) continue;
                            float score = sat * 2.0f + (1f - Math.Abs(bri - 0.55f));
                            if (score > bestScore) { bestScore = score; best = c; }
                        }
                    }
                    // Boost vibrancy slightly so it pops on dark glass
                    int r = Math.Min(255, (int)(best.R * 1.15f + 20));
                    int g = Math.Min(255, (int)(best.G * 1.15f + 20));
                    int b = Math.Min(255, (int)(best.B * 1.15f + 20));
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { return Color.FromArgb(0, 255, 255); }
        }

        public void Dispose()
        {
            EnsureSubscription(null);
            ClearCover();
        }
    }

    // =========================================================================
    // WASAPI LOOPBACK AUDIO CAPTURE + 96-BAR CAVA SPECTRUM ENGINE
    // =========================================================================
    sealed class WasapiCava : IDisposable
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
        public float PeakLevel { get; private set; }
        public float BassEnergy { get; private set; }

        readonly float[] fallVelocity;
        readonly float[] ringBuffer = new float[2048];
        int ringPos;
        float smoothMax = 0f;
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
            catch { client = null; capture = null; }
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
            catch { capture = null; return; }

            PeakLevel = maxAmp * 0.6f + PeakLevel * 0.4f;
            ComputeSpectrum();
        }

        void ComputeSpectrum()
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

            double fMin = 40.0, fMax = 15000.0;
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
                double maxMag = 0;
                for (int k = bin0; k <= bin1; k++)
                {
                    double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    if (mag > maxMag) maxMag = mag;
                }
                double eq = Math.Pow(freq0 / 100.0, 0.30) * (b < 10 ? 1.22 : 1.0);
                float val = (float)(maxMag * eq);
                rawTarget[b] = val;
                if (val > framePeak) framePeak = val;
            }

            float[] smoothed = new float[NumBars];
            for (int b = 0; b < NumBars; b++)
            {
                float v = rawTarget[b] * 0.60f;
                if (b > 0) v += rawTarget[b - 1] * 0.20f;
                if (b + 1 < NumBars) v += rawTarget[b + 1] * 0.20f;
                smoothed[b] = v;
            }

            if (smoothMax <= 0.001f) smoothMax = Math.Max(0.15f, framePeak);
            else if (framePeak > smoothMax) smoothMax = smoothMax * 0.65f + framePeak * 0.35f;
            else smoothMax = Math.Max(0.08f, smoothMax * 0.988f + framePeak * 0.012f);

            float bassSum = 0f;
            for (int b = 0; b < NumBars; b++)
            {
                float norm = Math.Min(1.0f, smoothed[b] / (smoothMax * 0.92f));
                if (PeakLevel < 0.002f) norm = 0f;

                if (norm >= Bars[b])
                {
                    Bars[b] = Bars[b] * 0.35f + norm * 0.65f;
                    fallVelocity[b] = 0.010f;
                }
                else
                {
                    Bars[b] = Math.Max(0f, Bars[b] - fallVelocity[b]);
                    fallVelocity[b] = Math.Min(0.055f, fallVelocity[b] + 0.0045f);
                }

                if (Bars[b] >= Peaks[b]) Peaks[b] = Bars[b];
                else Peaks[b] = Math.Max(0f, Peaks[b] - 0.009f);

                if (b < 12) bassSum += Bars[b];
            }
            BassEnergy = BassEnergy * 0.70f + (bassSum / 12f) * 0.30f;
        }

        public void InjectDemoSpectrum()
        {
            for (int i = 0; i < NumBars; i++)
            {
                double x = (double)i / NumBars;
                double wave = Math.Exp(-x * 1.25) * (0.58 + 0.42 * Math.Sin(x * 22.0));
                Bars[i] = (float)Math.Max(0.08, Math.Min(0.96, wave));
                Peaks[i] = Math.Min(1.0f, Bars[i] + 0.06f);
            }
            BassEnergy = 0.72f;
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
            if (client != null) { try { client.Stop(); } catch { } client = null; }
        }
    }
}
