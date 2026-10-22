using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Sensensturm
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Tuning.Load();
            Settings.Load();
            Audio.Init();
            using (var f = new GameForm())
            {
                f.Show();
                f.RunLoop();
            }
            Audio.Shutdown();
        }
    }

    // Zeichenpuffer im Arbeitsspeicher (DIB-Section): GDI+ zeichnet schnell in eine normale Bitmap,
    // die danach mit einem einzigen BitBlt/StretchBlt ins Fenster kopiert wird.
    sealed class FrameBuffer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr pt);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        const uint SRCCOPY = 0x00CC0020;
        const int HALFTONE = 4;

        readonly IntPtr memDC, dib, oldObj;
        public readonly Bitmap Bmp;
        public readonly Graphics G;
        public readonly int W, H;

        public FrameBuffer(int w, int h)
        {
            W = w; H = h;
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            memDC = CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            dib = CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (memDC == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                if (dib != IntPtr.Zero) DeleteObject(dib);
                if (memDC != IntPtr.Zero) DeleteDC(memDC);
                throw new InvalidOperationException("Zeichenpuffer konnte nicht angelegt werden.");
            }
            oldObj = SelectObject(memDC, dib);
            Bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            G = Graphics.FromImage(Bmp);
        }

        public void Present(IntPtr hwnd, int dw, int dh)
        {
            G.Flush(FlushIntention.Sync);
            IntPtr dc = GetDC(hwnd);
            if (dc == IntPtr.Zero) return;
            try
            {
                if (dw == W && dh == H) BitBlt(dc, 0, 0, W, H, memDC, 0, 0, SRCCOPY);
                else
                {
                    SetStretchBltMode(dc, HALFTONE);
                    SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
                    StretchBlt(dc, 0, 0, dw, dh, memDC, 0, 0, W, H, SRCCOPY);
                }
            }
            finally { ReleaseDC(hwnd, dc); }
        }

        public void Dispose()
        {
            G.Dispose();
            Bmp.Dispose();
            SelectObject(memDC, oldObj);
            DeleteObject(dib);
            DeleteDC(memDC);
        }
    }

    class GameForm : Form
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint p);

        readonly Game game = new Game();
        BufferedGraphicsContext ctx;
        BufferedGraphics buffer;
        Size bufSize;
        bool closing;
        FrameBuffer fb;
        bool fbFailed;
        float mouseScale = 1;
        // Grafikstufe: 1 hoch (volle Auflösung, max. 1080p), 2 mittel (75 %), 3 niedrig (50 %)
        int autoLevel = 1, slowFrames, measuredFrames;
        double msSum;

        public GameForm()
        {
            Text = "Sensensturm";
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ctx = BufferedGraphicsManager.Current;
            game.SetFullscreen = SetFullscreen;
            if (Settings.Fullscreen) SetFullscreen(true);
        }

        void SetFullscreen(bool on)
        {
            if (on)
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                ClientSize = new Size(1280, 720);
                CenterToScreen();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Pfeiltasten, Tab usw. selbst verarbeiten
            return false;
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Keys k = e.KeyCode;
            if (k == Keys.Menu || k == Keys.F10) e.Handled = true;
            if (k == Keys.Enter && e.Alt) { Settings.Fullscreen = !Settings.Fullscreen; SetFullscreen(Settings.Fullscreen); e.Handled = true; return; }
            game.OnKey(k);
            game.In.KeyDown(k);
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e) { game.In.KeyUp(e.KeyCode); e.Handled = true; }
        protected override void OnKeyPress(KeyPressEventArgs e) { game.OnChar(e.KeyChar); e.Handled = true; }
        protected override void OnMouseMove(MouseEventArgs e) { game.OnMouseMove(MX(e), MY(e)); }
        int MX(MouseEventArgs e) { return (int)(e.X * mouseScale); }
        int MY(MouseEventArgs e) { return (int)(e.Y * mouseScale); }
        static Keys MouseKey(MouseButtons b)
        {
            return b == MouseButtons.Left ? Keys.LButton : b == MouseButtons.Right ? Keys.RButton : b == MouseButtons.Middle ? Keys.MButton :
                   b == MouseButtons.XButton1 ? Keys.XButton1 : b == MouseButtons.XButton2 ? Keys.XButton2 : Keys.None;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Keys k = MouseKey(e.Button);
            if (game.OnMouseDown(MX(e), MY(e), e.Button == MouseButtons.Right, k)) return;
            if (k != Keys.None) game.In.KeyDown(k);
        }
        protected override void OnMouseUp(MouseEventArgs e) { Keys k = MouseKey(e.Button); if (k != Keys.None) game.In.KeyUp(k); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); game.OnFocusLost(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; base.OnFormClosing(e); }

        static readonly string[] levelNames = { "Automatisch", "Hoch", "Mittel", "Niedrig" };

        // Auflösung, in der gezeichnet wird (wird danach aufs Fenster gestreckt)
        static Size RenderSize(Size client, int level)
        {
            float f = Math.Min(1f, Math.Min(1920f / client.Width, 1080f / client.Height));
            if (level == 2) f *= 0.75f;
            else if (level == 3) f *= 0.5f;
            return new Size(Math.Max(320, (int)(client.Width * f)), Math.Max(180, (int)(client.Height * f)));
        }

        void RenderFrame()
        {
            int level = Settings.Graphics == 0 ? autoLevel : Settings.Graphics;
            game.AutoLevel = autoLevel;
            if (!fbFailed)
            {
                Size rs = RenderSize(ClientSize, level);
                if (fb == null || fb.W != rs.Width || fb.H != rs.Height)
                {
                    if (fb != null) fb.Dispose();
                    fb = null;
                    try { fb = new FrameBuffer(rs.Width, rs.Height); }
                    catch (Exception ex) { Debug.WriteLine(ex); fbFailed = true; }
                }
                if (fb != null)
                {
                    mouseScale = fb.W / (float)ClientSize.Width;
                    game.RenderInfo = fb.W + "x" + fb.H;
                    try
                    {
                        game.Render(fb.G, fb.W, fb.H);
                        fb.Present(Handle, ClientSize.Width, ClientSize.Height);
                    }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                    return;
                }
            }
            // Ersatzweg (falls der Speicherpuffer nicht angelegt werden kann)
            mouseScale = 1;
            if (buffer == null || bufSize != ClientSize)
            {
                if (buffer != null) buffer.Dispose();
                bufSize = ClientSize;
                ctx.MaximumBuffer = new Size(bufSize.Width + 1, bufSize.Height + 1);
                buffer = ctx.Allocate(CreateGraphics(), new Rectangle(Point.Empty, bufSize));
            }
            try
            {
                game.Render(buffer.Graphics, bufSize.Width, bufSize.Height);
                buffer.Render();
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        // Bei "Automatisch": dauerhaft unter ~45 FPS -> eine Grafikstufe tiefer
        void AutoGraphics(double ms)
        {
            if (Settings.Graphics != 0 || autoLevel >= 3) return;
            if (ms > 250) return; // Ladeschluckauf (z. B. Arena-Bild) nicht mitzählen
            msSum += ms;
            measuredFrames++;
            if (measuredFrames < 90) return;
            double avg = msSum / measuredFrames;
            msSum = 0; measuredFrames = 0;
            slowFrames = avg > 21 ? slowFrames + 1 : 0;
            if (slowFrames >= 2)
            {
                slowFrames = 0;
                autoLevel++;
                game.Notify("Grafik automatisch auf \"" + levelNames[autoLevel] + "\" gesenkt (Einstellungen > Grafik).");
            }
        }

        public void RunLoop()
        {
            timeBeginPeriod(1);
            var sw = Stopwatch.StartNew();
            const double dt = 1.0 / 60.0;
            double acc = 0, last = sw.Elapsed.TotalSeconds, fpsT = last;
            int frames = 0;
            try
            {
                while (!closing && !IsDisposed && game.Running)
                {
                    Application.DoEvents();
                    if (closing || IsDisposed) break;
                    double now = sw.Elapsed.TotalSeconds;
                    acc += now - last;
                    last = now;
                    if (acc > 0.25) acc = 0.25;
                    bool stepped = false;
                    while (acc >= dt)
                    {
                        game.Tick();
                        acc -= dt;
                        stepped = true;
                    }
                    if (stepped && ClientSize.Width > 0 && ClientSize.Height > 0 && WindowState != FormWindowState.Minimized)
                    {
                        double t0 = sw.Elapsed.TotalMilliseconds;
                        RenderFrame();
                        double ms = sw.Elapsed.TotalMilliseconds - t0;
                        game.FrameMs = game.FrameMs * 0.9 + ms * 0.1;
                        AutoGraphics(ms);
                        frames++;
                        if (now - fpsT >= 1) { game.Fps = frames; frames = 0; fpsT = now; }
                    }
                    else Thread.Sleep(1);
                }
            }
            finally
            {
                if (fb != null) fb.Dispose();
                game.Shutdown();
                timeEndPeriod(1);
            }
            if (!IsDisposed) Close();
        }
    }
}
