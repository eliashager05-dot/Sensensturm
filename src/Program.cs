using System;
using System.Diagnostics;
using System.Drawing;
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

    class GameForm : Form
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint p);

        readonly Game game = new Game();
        BufferedGraphicsContext ctx;
        BufferedGraphics buffer;
        Size bufSize;
        bool closing;

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
        protected override void OnMouseMove(MouseEventArgs e) { game.OnMouseMove(e.X, e.Y); }
        static Keys MouseKey(MouseButtons b)
        {
            return b == MouseButtons.Left ? Keys.LButton : b == MouseButtons.Right ? Keys.RButton : b == MouseButtons.Middle ? Keys.MButton :
                   b == MouseButtons.XButton1 ? Keys.XButton1 : b == MouseButtons.XButton2 ? Keys.XButton2 : Keys.None;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Keys k = MouseKey(e.Button);
            if (game.OnMouseDown(e.X, e.Y, e.Button == MouseButtons.Right, k)) return;
            if (k != Keys.None) game.In.KeyDown(k);
        }
        protected override void OnMouseUp(MouseEventArgs e) { Keys k = MouseKey(e.Button); if (k != Keys.None) game.In.KeyUp(k); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); game.OnFocusLost(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; base.OnFormClosing(e); }

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
                        frames++;
                        if (now - fpsT >= 1) { game.Fps = frames; frames = 0; fpsT = now; }
                    }
                    else Thread.Sleep(1);
                }
            }
            finally
            {
                game.Shutdown();
                timeEndPeriod(1);
            }
            if (!IsDisposed) Close();
        }
    }
}
