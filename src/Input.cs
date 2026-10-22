using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sensensturm
{
    public static class Btn
    {
        public const int Left = 1, Right = 2, Up = 4, Down = 8, Jump = 16, Light = 32, Heavy = 64, Dodge = 128, Shield = 256, Grab = 512;
        public const int Count = 10;
        public static readonly string[] Ids = { "Left", "Right", "Up", "Down", "Jump", "Light", "Heavy", "Dodge", "Shield", "Grab" };
        public static readonly string[] Names = { "Links", "Rechts", "Oben / Zielen", "Unten / Schnellfall", "Springen", "Leichter Angriff", "Schwerer Angriff", "Ausweichen / Dash", "Schild / Parieren", "Greifen / Werfen" };
    }

    public enum MenuKey { Up, Down, Left, Right, Ok, Back, Pause }

    // Tastatur + XInput-Controller. Liefert pro Tick gehaltene Tasten und "seit letztem Tick gedrückt" (Latch),
    // damit auch sehr kurze Tastendrücke nicht verloren gehen.
    public class InputManager
    {
        readonly HashSet<Keys> down = new HashSet<Keys>();
        readonly HashSet<Keys> latch = new HashSet<Keys>();
        public readonly List<MenuKey> Menu = new List<MenuKey>();
        public Keys LastKey = Keys.None;

        [StructLayout(LayoutKind.Sequential)]
        struct XGamepad { public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint dwPacketNumber; public XGamepad Gamepad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int XGet14(int i, out XState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int XGet91(int i, out XState s);

        int xinputMode = 0; // 0 = 1_4, 1 = 9_1_0, 2 = keine
        public readonly int[] PadHeld = new int[4];
        readonly int[] padLatch = new int[4];
        readonly int[] padPrev = new int[4];
        readonly ushort[] padMenuPrev = new ushort[4];
        readonly bool[] padConnected = new bool[4];
        int padRetry = 0;

        public bool AnyPad { get { for (int i = 0; i < 4; i++) if (padConnected[i]) return true; return false; } }
        public int PadCount { get { int n = 0; for (int i = 0; i < 4; i++) if (padConnected[i]) n++; return n; } }
        public int PadIndex(int n) { int c = 0; for (int i = 0; i < 4; i++) if (padConnected[i]) { if (c == n) return i; c++; } return -1; }

        public void KeyDown(Keys k)
        {
            if (!down.Contains(k)) { down.Add(k); latch.Add(k); }
            LastKey = k;
            switch (k)
            {
                case Keys.Up: case Keys.W: Menu.Add(MenuKey.Up); break;
                case Keys.Down: case Keys.S: Menu.Add(MenuKey.Down); break;
                case Keys.Left: case Keys.A: Menu.Add(MenuKey.Left); break;
                case Keys.Right: case Keys.D: Menu.Add(MenuKey.Right); break;
                case Keys.Enter: case Keys.Space: case Keys.J: Menu.Add(MenuKey.Ok); break;
                case Keys.Escape: Menu.Add(MenuKey.Back); Menu.Add(MenuKey.Pause); break;
            }
        }

        public void KeyUp(Keys k) { down.Remove(k); }
        public void ReleaseAll() { down.Clear(); }
        public bool IsDown(Keys k) { return down.Contains(k); }

        int ReadPad(int i, out XState s)
        {
            s = new XState();
            if (xinputMode == 0)
            {
                try { return XGet14(i, out s); }
                catch { xinputMode = 1; }
            }
            if (xinputMode == 1)
            {
                try { return XGet91(i, out s); }
                catch { xinputMode = 2; }
            }
            return -1;
        }

        public void PollPads()
        {
            if (xinputMode == 2) return;
            padRetry--;
            for (int i = 0; i < 4; i++)
            {
                // nicht verbundene Controller abzufragen ist teuer -> nur alle 2 Sekunden
                if (!padConnected[i] && padRetry > 0) continue;
                XState s;
                if (ReadPad(i, out s) != 0) { padConnected[i] = false; PadHeld[i] = 0; continue; }
                padConnected[i] = true;
                var g = s.Gamepad;
                int h = 0;
                const int dz = 14000;
                if ((g.wButtons & 0x0004) != 0 || g.sThumbLX < -dz) h |= Btn.Left;
                if ((g.wButtons & 0x0008) != 0 || g.sThumbLX > dz) h |= Btn.Right;
                if ((g.wButtons & 0x0001) != 0 || g.sThumbLY > dz) h |= Btn.Up;
                if ((g.wButtons & 0x0002) != 0 || g.sThumbLY < -dz) h |= Btn.Down;
                if ((g.wButtons & 0x1000) != 0) h |= Btn.Jump;                                   // A
                if ((g.wButtons & 0x8000) != 0) h |= Btn.Grab;                                   // Y
                if ((g.wButtons & 0x4000) != 0) h |= Btn.Light;                                  // X
                if ((g.wButtons & 0x2000) != 0) h |= Btn.Heavy;                                  // B
                if (g.bRightTrigger > 90 || (g.wButtons & 0x0200) != 0) h |= Btn.Dodge;          // RT, RB
                if (g.bLeftTrigger > 90 || (g.wButtons & 0x0100) != 0) h |= Btn.Shield;          // LT, LB
                padLatch[i] |= h & ~PadHeld[i];
                PadHeld[i] = h;

                // Menünavigation
                ushort mb = 0;
                if ((h & Btn.Up) != 0) mb |= 1;
                if ((h & Btn.Down) != 0) mb |= 2;
                if ((h & Btn.Left) != 0) mb |= 4;
                if ((h & Btn.Right) != 0) mb |= 8;
                if ((g.wButtons & 0x1000) != 0) mb |= 16;
                if ((g.wButtons & 0x2000) != 0) mb |= 32; // B
                if ((g.wButtons & 0x0010) != 0) mb |= 64; // Start
                ushort e = (ushort)(mb & ~padMenuPrev[i]);
                if ((e & 1) != 0) Menu.Add(MenuKey.Up);
                if ((e & 2) != 0) Menu.Add(MenuKey.Down);
                if ((e & 4) != 0) Menu.Add(MenuKey.Left);
                if ((e & 8) != 0) Menu.Add(MenuKey.Right);
                if ((e & 16) != 0) Menu.Add(MenuKey.Ok);
                if ((e & 32) != 0) Menu.Add(MenuKey.Back);
                if ((e & 64) != 0) { Menu.Add(MenuKey.Back); Menu.Add(MenuKey.Pause); }
                padMenuPrev[i] = mb;
            }
            if (padRetry <= 0) padRetry = 120;
        }

        // keySet: 0 = Belegung Spieler 1, 1 = Spieler 2, -1 = beide (Solo/LAN); pad: physischer Index oder -1
        public void Sample(int keySet, int pad, out int held, out int pressed)
        {
            held = 0; pressed = 0;
            for (int p = 0; p < 2; p++)
            {
                if (keySet >= 0 && keySet != p) continue;
                for (int a = 0; a < Btn.Count; a++)
                    for (int s = 0; s < 2; s++)
                    {
                        Keys k = Settings.Bind[p, a, s];
                        if (k == Keys.None) continue;
                        if (down.Contains(k)) held |= 1 << a;
                        if (latch.Contains(k)) pressed |= 1 << a;
                    }
            }
            if (pad >= 0 && pad < 4 && padConnected[pad])
            {
                held |= PadHeld[pad];
                pressed |= padLatch[pad];
            }
            // gegensätzliche Richtungen neutralisieren
            if ((held & (Btn.Left | Btn.Right)) == (Btn.Left | Btn.Right)) held &= ~(Btn.Left | Btn.Right);
        }

        public void EndTick()
        {
            latch.Clear();
            for (int i = 0; i < 4; i++) padLatch[i] = 0;
            Menu.Clear();
        }
    }
}
