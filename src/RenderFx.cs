using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sensensturm
{
    // Kleine Effekte fürs Spielgefühl: Bodenrisse und Staub bei harten Landungen, Combo-Zähler mit Schaden,
    // Zeitlupe und Kamerafahrt für den letzten K.O. (Kinobalken).
    public partial class Renderer
    {
        // ------------------------------------------------------------------ Zeitlupe / Kamera

        // Geschwindigkeit der Effekte (1 = normal). Partikel bewegen sich anteilig, ganzzahlige Zähler über den Akku.
        public float TimeScale = 1f;
        float timeAcc;
        // Kamera wird von außen geführt (letzter K.O.), UpdateCamera ändert sie dann nicht
        public bool CamLocked;
        // Kinobalken oben und unten (0..1)
        public float Letterbox;

        // nur fliegende Effekte entfernen (Risse bleiben), z. B. beim Start der Zeitlupe
        public void ClearParticles()
        {
            parts.Clear(); ghosts.Clear();
            for (int i = 0; i < 2; i++) { trailA[i].Clear(); trailB[i].Clear(); }
            Flash = 0; shake = 0;
        }

        public void FocusCamera(float x, float y, float zoom, float lerp)
        {
            x = Math.Max(Arena.BlastL + 380, Math.Min(Arena.BlastR - 380, x));
            y = Math.Max(Arena.BlastT + 260, Math.Min(Arena.BlastB - 260, y));
            CamX += (x - CamX) * lerp;
            CamY += (y - CamY) * lerp;
            Zoom += (zoom - Zoom) * lerp;
        }

        // wie viele ganze Schritte die ganzzahligen Effekt-Zähler in diesem Frame machen
        int SlowSteps()
        {
            if (TimeScale >= 1f) { timeAcc = 0; return 1; }
            timeAcc += TimeScale;
            int n = (int)timeAcc;
            timeAcc -= n;
            return n;
        }

        public void DrawLetterbox(Graphics g)
        {
            if (Letterbox <= 0.001f) return;
            float h = 74 * Letterbox;
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, Letterbox * 1.5f)), 4, 2, 8)))
            {
                g.FillRectangle(b, 0, 0, VW, h);
                g.FillRectangle(b, 0, VH - h, VW, h);
            }
            // leichte Vignette
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-VW * 0.25f, -VH * 0.35f, VW * 1.5f, VH * 1.7f);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb((int)(150 * Letterbox), 0, 0, 0) } })
                    g.FillRectangle(pb, 0, 0, VW, VH);
            }
            g.SmoothingMode = sm;
        }

        // ------------------------------------------------------------------ Harte Landungen: Staubwelle, Steinsplitter, Bodenrisse

        class Crack
        {
            public float X, Y, Life, Max;
            public PointF[][] Lines; // je Riss eine Zickzack-Linie (relativ zum Einschlag)
            public float[] Widths;
        }

        readonly List<Crack> cracks = new List<Crack>();
        public int CrackCount { get { return cracks.Count; } }

        // strength 0..1
        void HeavyImpact(float x, float y, float strength, Color tint)
        {
            strength = Math.Max(0, Math.Min(1, strength));
            // Staubwelle zu beiden Seiten am Boden entlang
            int n = 10 + (int)(strength * 12);
            for (int i = 0; i < n; i++)
            {
                float dir = i % 2 == 0 ? -1 : 1;
                float sp = 3 + (float)rng.NextDouble() * (5 + strength * 6);
                parts.Add(new Particle
                {
                    Kind = 6, X = x + dir * (float)rng.NextDouble() * 18, Y = y - 6 - (float)rng.NextDouble() * 6,
                    VX = dir * sp, VY = -(float)rng.NextDouble() * 1.4f, Life = 30 + rng.Next(14), Max = 44,
                    Size = 10 + (float)rng.NextDouble() * (10 + strength * 10), C = Color.FromArgb(170, 160, 175), Drag = 0.9f
                });
            }
            // Steinsplitter
            Color rock = StoneColor();
            for (int i = 0; i < 5 + (int)(strength * 8); i++)
                parts.Add(new Particle
                {
                    Kind = 7, X = x + (float)(rng.NextDouble() - 0.5) * 30, Y = y - 2,
                    VX = (float)(rng.NextDouble() - 0.5) * (6 + strength * 8), VY = -3 - (float)rng.NextDouble() * (5 + strength * 5),
                    Life = 30 + rng.Next(14), Max = 44, Size = 3 + (float)rng.NextDouble() * 4, Rot = (float)rng.NextDouble() * 360,
                    VR = (float)(rng.NextDouble() - 0.5) * 30, Grav = 0.5f, C = rng.Next(3) == 0 ? tint : rock
                });
            parts.Add(new Particle { Kind = 8, X = x, Y = y, Size = 90 + strength * 90, Life = 16, Max = 16, C = Color.FromArgb(220, 210, 200) });
            AddCrack(x, y, strength);
            AddShake(4 + strength * 6);
            Audio.Play(Sfx.Shock, 0.35f + 0.4f * strength, 1.25f - 0.25f * strength);
        }

        Color StoneColor()
        {
            return Stage != StageDef.Graveyard ? Th.BlockDark : Color.FromArgb(70, 62, 92);
        }

        void AddCrack(float x, float y, float strength)
        {
            // Hauptbühne: tiefe Risse in die Steinfront; Schwebeplattformen sind dünn: kurze, flache Risse
            bool main = Math.Abs(y - Arena.Main.T) < 3 && x > Arena.Main.L + 4 && x < Arena.Main.R - 4;
            bool soft = false;
            foreach (SoftPlat sp in Arena.Soft) if (Math.Abs(y - sp.Y) < 3 && x > sp.L && x < sp.R) soft = true;
            if (!main && !soft) return;
            float depth = main ? 26 + 50 * strength : 9;
            int branches = 3 + (int)(strength * 3);
            var lines = new List<PointF[]>();
            var widths = new List<float>();
            for (int b = 0; b < branches; b++)
            {
                // Richtung: nach unten aufgefächert (0 = rechts, 90° = unten)
                double ang = (b + 0.5) / branches * Math.PI + (rng.NextDouble() - 0.5) * 0.4;
                ang = Math.Max(0.12, Math.Min(Math.PI - 0.12, ang));
                float len = depth * (0.6f + 0.6f * (float)rng.NextDouble()) / (float)Math.Max(0.35, Math.Sin(ang));
                len = Math.Min(len, depth * 2.6f);
                int segs = 4 + rng.Next(3);
                var pts = new PointF[segs + 1];
                float px = 0, py = 0;
                pts[0] = new PointF(0, 0);
                for (int k = 1; k <= segs; k++)
                {
                    double a = ang + (rng.NextDouble() - 0.5) * 0.9;
                    float step = len / segs;
                    px += (float)Math.Cos(a) * step;
                    py += (float)Math.Sin(a) * step;
                    py = Math.Min(py, depth);
                    pts[k] = new PointF(px, Math.Max(1, py));
                }
                lines.Add(pts);
                widths.Add(2.2f + 1.6f * strength);
                // kleiner Seitenast
                if (main && segs > 3 && rng.Next(2) == 0)
                {
                    PointF s = pts[2];
                    double a2 = ang + (rng.Next(2) == 0 ? 0.7 : -0.7);
                    lines.Add(new[] { s, new PointF(s.X + (float)Math.Cos(a2) * len * 0.35f, Math.Min(depth, s.Y + (float)Math.Abs(Math.Sin(a2)) * len * 0.35f)) });
                    widths.Add(1.4f);
                }
            }
            // flache Risse entlang der Oberkante
            for (int side = -1; side <= 1; side += 2)
                lines.Add(new[] { new PointF(0, 1), new PointF(side * (14 + 18 * strength), 2 + (float)rng.NextDouble() * 2), new PointF(side * (26 + 30 * strength), 1.5f) });
            widths.Add(1.6f); widths.Add(1.6f);
            if (cracks.Count >= 14) cracks.RemoveAt(0);
            int life = 300 + (int)(strength * 180);
            cracks.Add(new Crack { X = x, Y = y, Life = life, Max = life, Lines = lines.ToArray(), Widths = widths.ToArray() });
        }

        void TickCracks(int steps)
        {
            for (int i = cracks.Count - 1; i >= 0; i--)
            {
                cracks[i].Life -= steps;
                if (cracks[i].Life <= 0) cracks.RemoveAt(i);
            }
        }

        void DrawCracks(Graphics g)
        {
            if (cracks.Count == 0) return;
            Color dark = Stage != StageDef.Graveyard ? Mixc(Th.BlockDark, Color.Black, 0.55f) : Color.FromArgb(20, 12, 34);
            Color lit = Stage != StageDef.Graveyard ? Mixc(Th.Top, Color.White, 0.25f) : Color.FromArgb(150, 120, 200);
            foreach (Crack c in cracks)
            {
                float a = Math.Min(1f, c.Life / (c.Max * 0.35f));
                // Einschlag frisch: kurz glühende Kanten
                float fresh = Math.Max(0, (c.Life - (c.Max - 20)) / 20f);
                for (int i = 0; i < c.Lines.Length; i++)
                {
                    PointF[] pts = c.Lines[i];
                    var w = new PointF[pts.Length];
                    for (int k = 0; k < pts.Length; k++) w[k] = new PointF(c.X + pts[k].X, c.Y + pts[k].Y);
                    var hl = new PointF[w.Length];
                    for (int k = 0; k < w.Length; k++) hl[k] = new PointF(w[k].X + 0.8f, w[k].Y + 1.4f);
                    using (var p = RPen(A(lit, 0.45f * a), c.Widths[i] * 0.6f)) g.DrawLines(p, hl);
                    using (var p = RPen(A(dark, 0.9f * a), c.Widths[i])) g.DrawLines(p, w);
                    if (fresh > 0) using (var p = RPen(A(Color.FromArgb(255, 220, 160), fresh * 0.8f), c.Widths[i] * 0.5f)) g.DrawLines(p, w);
                }
            }
        }

        // ------------------------------------------------------------------ Combo-Zähler

        // Pro Angreifer: Schaden des Gegners zu Beginn der Combo, letzter Trefferstand, Zeitpunkt des letzten Treffers (für den Pop)
        readonly float[] comboStartDmg = new float[2], lastVictimDmg = new float[2], comboDmgShown = new float[2];
        readonly int[] lastHits = new int[2], lastCombo = new int[2], comboPopFrame = { -999, -999 };
        Font fComboNum, fComboLbl;

        // Combo-Schaden eines Angreifers (für Anzeige und Tests)
        public float ComboDamage(int i) { return comboDmgShown[i]; }

        // einmal pro Frame aus DrawHud: Trefferzähler beobachten (die Simulation kennt nur die Trefferzahl)
        public void TrackCombos(GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i], V = s.p[1 - i];
                if (P.statHits != lastHits[i])
                {
                    // neuer Treffer: beginnt eine Combo, zählt ab dem Schaden davor
                    if (P.combo <= 1 || P.combo < lastCombo[i]) comboStartDmg[i] = Math.Min(lastVictimDmg[i], V.damage);
                    if (P.combo >= 2) comboPopFrame[i] = frame;
                }
                if (P.combo >= 1 && P.comboTimer > 0) comboDmgShown[i] = Math.Max(0, V.damage - comboStartDmg[i]);
                lastHits[i] = P.statHits;
                lastCombo[i] = P.combo;
                lastVictimDmg[i] = V.damage;
            }
        }

        static Color ComboColor(int hits)
        {
            if (hits >= 10) return Color.FromArgb(235, 120, 255);
            if (hits >= 7) return Color.FromArgb(255, 95, 85);
            if (hits >= 4) return Color.FromArgb(255, 170, 70);
            return Color.FromArgb(255, 228, 140);
        }

        void DrawComboCounter(Graphics g, PlayerState P, int i, float px, float py, float pw)
        {
            if (P.combo < 2 || P.comboTimer <= 0) return;
            if (fComboNum == null)
            {
                fComboNum = new Font("Segoe UI Black", 62, FontStyle.Bold, GraphicsUnit.Pixel);
                fComboLbl = new Font("Segoe UI Black", 19, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            float a = Math.Min(1f, P.comboTimer / 25f);
            int age = frame - comboPopFrame[i];
            float pop = 1f + 0.5f * Math.Max(0, 1 - age / 9f);
            Color col = ComboColor(P.combo);
            if (P.combo >= 10) col = Mixc(col, Color.White, 0.25f + 0.25f * (float)Math.Sin(frame * 0.3));
            bool right = i == 1;
            string num = P.combo.ToString();
            SizeF ns = g.MeasureString(num, fComboNum);
            float baseY = py - 148;
            float nx = right ? px + pw - ns.Width + 8 : px - 8;
            // Zahl mit Pop um ihre Mitte
            var st = g.Save();
            float cx = nx + ns.Width / 2, cy = baseY + ns.Height / 2;
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(pop, pop);
            g.RotateTransform(right ? 6 : -6);
            using (var b = new SolidBrush(Color.FromArgb((int)(220 * a), 10, 4, 20))) g.DrawString(num, fComboNum, b, -ns.Width / 2 + 3, -ns.Height / 2 + 4);
            using (var b = new SolidBrush(A(col, a))) g.DrawString(num, fComboNum, b, -ns.Width / 2, -ns.Height / 2);
            g.Restore(st);
            // Beschriftung: TREFFER / Schaden / perfekt
            string l1 = "TREFFER";
            string l2 = ((int)Math.Round(comboDmgShown[i])) + " % SCHADEN";
            string l3 = P.perfects > 0 ? P.perfects + "× PERFEKT" : null;
            SizeF s1 = g.MeasureString(l1, fComboLbl), s2 = g.MeasureString(l2, FTiny);
            float lx = right ? nx - 10 : nx + ns.Width + 4;
            float ly = baseY + 16;
            Action<string, Font, Color, float, float> lbl = (t, f, c, w, y) =>
            {
                float x = right ? lx - w : lx;
                using (var b = new SolidBrush(Color.FromArgb((int)(200 * a), 0, 0, 0))) g.DrawString(t, f, b, x + 2, y + 2);
                using (var b = new SolidBrush(A(c, a))) g.DrawString(t, f, b, x, y);
            };
            lbl(l1, fComboLbl, col, s1.Width, ly);
            lbl(l2, FTiny, DamageColor(Math.Min(150, 40 + comboDmgShown[i] * 2.2f)), s2.Width, ly + 24);
            if (l3 != null) lbl(l3, FTiny, Color.FromArgb(255, 225, 90), g.MeasureString(l3, FTiny).Width, ly + 46);
        }
    }
}
