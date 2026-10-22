using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Sensensturm
{
    // Auswählbare Arenen. Alle nutzen dieselbe Plattform-Geometrie (Arena), nur das Aussehen unterscheidet sich.
    public static class StageDef
    {
        public const int Graveyard = 0, Blossom = 1, Autumn = 2, Grove = 3, Count = 4;
        public static readonly string[] Names = { "Mondfriedhof", "Kirschblütental", "Herbsttempel", "Sternenhain" };
        public static int Valid(int s) { return s >= 0 && s < Count ? s : Graveyard; }
    }

    // Farbpalette einer Arena
    class StageTheme
    {
        public Color Top, TopDark, Block, BlockDark, Joint, Moss, MossDark, Bits, BitsAlt, Under, Abyss, Light;
        public Color Bark, LeafA, LeafB, LeafC, MidRock, MidTop;
        public bool Glow;
    }

    public partial class Renderer
    {
        public int Stage;

        static readonly StageTheme[] themes =
        {
            null,
            new StageTheme
            {
                Top = Color.FromArgb(216, 198, 176), TopDark = Color.FromArgb(166, 146, 134), Block = Color.FromArgb(98, 94, 108), BlockDark = Color.FromArgb(50, 48, 62),
                Joint = Color.FromArgb(28, 26, 38), Moss = Color.FromArgb(110, 146, 62), MossDark = Color.FromArgb(70, 100, 46),
                Bits = Color.FromArgb(255, 176, 208), BitsAlt = Color.FromArgb(255, 222, 236), Under = Color.FromArgb(255, 170, 220), Abyss = Color.FromArgb(210, 150, 230), Light = Color.FromArgb(255, 196, 110),
                Bark = Color.FromArgb(84, 56, 52), LeafA = Color.FromArgb(214, 104, 156), LeafB = Color.FromArgb(244, 150, 194), LeafC = Color.FromArgb(255, 206, 228),
                MidRock = Color.FromArgb(200, 112, 108, 160), MidTop = Color.FromArgb(220, 236, 168, 206)
            },
            new StageTheme
            {
                Top = Color.FromArgb(200, 188, 162), TopDark = Color.FromArgb(150, 138, 120), Block = Color.FromArgb(104, 98, 98), BlockDark = Color.FromArgb(54, 50, 56),
                Joint = Color.FromArgb(30, 26, 30), Moss = Color.FromArgb(118, 140, 58), MossDark = Color.FromArgb(76, 98, 40),
                Bits = Color.FromArgb(226, 64, 36), BitsAlt = Color.FromArgb(250, 140, 44), Under = Color.FromArgb(255, 176, 120), Abyss = Color.FromArgb(236, 150, 130), Light = Color.FromArgb(255, 186, 100),
                Bark = Color.FromArgb(66, 48, 44), LeafA = Color.FromArgb(150, 28, 24), LeafB = Color.FromArgb(214, 50, 32), LeafC = Color.FromArgb(250, 112, 50),
                MidRock = Color.FromArgb(200, 128, 110, 140), MidTop = Color.FromArgb(220, 200, 76, 64)
            },
            new StageTheme
            {
                Top = Color.FromArgb(74, 82, 136), TopDark = Color.FromArgb(44, 50, 98), Block = Color.FromArgb(54, 60, 108), BlockDark = Color.FromArgb(22, 26, 60),
                Joint = Color.FromArgb(10, 12, 32), Moss = Color.FromArgb(70, 226, 236), MossDark = Color.FromArgb(40, 120, 176),
                Bits = Color.FromArgb(120, 240, 255), BitsAlt = Color.FromArgb(196, 146, 255), Under = Color.FromArgb(90, 160, 255), Abyss = Color.FromArgb(70, 110, 236), Light = Color.FromArgb(120, 232, 255),
                Bark = Color.FromArgb(40, 36, 70), LeafA = Color.FromArgb(30, 36, 96), LeafB = Color.FromArgb(52, 64, 150), LeafC = Color.FromArgb(96, 120, 220),
                MidRock = Color.FromArgb(210, 36, 40, 92), MidTop = Color.FromArgb(220, 70, 200, 230), Glow = true
            },
        };

        StageTheme Th { get { return themes[Stage]; } }

        // ================================================================ Geometrie (einmal pro Arena berechnet)

        int geoStage = -1, motesStage = 0;
        GraphicsPath stones, stoneHi, moss, tufts, joints, bits, bitsAlt, platRock, platSlab, platMoss, canopyA, canopyB, canopyC;
        readonly List<PointF[]> branches = new List<PointF[]>();
        readonly List<float> branchW = new List<float>();

        // Statische Teile (Bühne, Bäume) werden einmal pro Arena in Bitmaps gezeichnet, pro Frame nur noch eingeblendet
        static readonly RectangleF arenaRect = new RectangleF(-700, -400, 1400, 980);
        static readonly RectangleF[] treeRects = { new RectangleF(-1000, -640, 700, 680), new RectangleF(300, -640, 700, 680) };
        Bitmap arenaBmp;
        readonly Bitmap[] treeBmp = new Bitmap[2];

        static Bitmap CacheLayer(RectangleF world, float res, Action<Graphics> draw)
        {
            var bmp = new Bitmap((int)Math.Ceiling(world.Width * res), (int)Math.Ceiling(world.Height * res), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(res, res);
                g.TranslateTransform(-world.X, -world.Y);
                draw(g);
            }
            return bmp;
        }

        static readonly float[,] floatRocks =
        {
            // x, y, Größe, Art (0 nur Fels, 1 mit Pagode und Wasserfall, 2 mit Baum)
            { -820, -430, 1f, 1 }, { -450, -560, 0.6f, 0 }, { 380, -600, 0.55f, 2 }, { 760, -380, 0.9f, 1 },
            { 1150, -520, 0.7f, 2 }, { -1250, -300, 0.8f, 2 }, { 60, -650, 0.45f, 0 }, { -1500, -620, 0.6f, 0 },
        };

        void SyncStage()
        {
            Stage = StageDef.Valid(Stage);
            if (motesStage != Stage)
            {
                motesStage = Stage;
                motes.Clear();
                for (int i = 0; i < 46; i++) motes.Add(NewMote(true));
            }
            if (Stage != StageDef.Graveyard && geoStage != Stage) BuildStageGeometry();
        }

        static GraphicsPath WPath() { return new GraphicsPath(FillMode.Winding); }

        // Blütenblatt (Ellipse) oder Ahornblatt (Stern) als Polygon
        static void AddBit(GraphicsPath p, float x, float y, float s, float ang, bool leaf, float flat)
        {
            int n = leaf ? 10 : 6;
            var pts = new PointF[n];
            double c = Math.Cos(ang), sn = Math.Sin(ang);
            for (int k = 0; k < n; k++)
            {
                double a = k * Math.PI * 2 / n;
                float rr = leaf ? (k % 2 == 0 ? s : s * 0.45f) : s;
                float px = (float)Math.Cos(a) * rr, py = (float)Math.Sin(a) * rr * (leaf ? 1f : 0.55f);
                pts[k] = new PointF(x + (float)(px * c - py * sn), y + (float)(px * sn + py * c) * flat);
            }
            p.AddPolygon(pts);
        }

        void AddDrips(Random r, GraphicsPath p, float x0, float x1, float y, float maxLen)
        {
            float mx = x0;
            while (mx < x1)
            {
                float w = 24 + (float)r.NextDouble() * 80;
                w = Math.Min(w, x1 - mx);
                if (w > 8 && r.Next(3) != 0)
                {
                    var pts = new List<PointF> { new PointF(mx, y - 2) };
                    int n = 1 + r.Next(4);
                    for (int k = 0; k < n; k++)
                    {
                        float px = mx + w * (k + 0.5f) / n, hw = w / n * 0.42f;
                        float d = 6 + (float)r.NextDouble() * (r.Next(4) == 0 ? maxLen : maxLen * 0.35f);
                        pts.Add(new PointF(px - hw, y + d * 0.35f));
                        pts.Add(new PointF(px, y + d));
                        pts.Add(new PointF(px + hw, y + d * 0.35f));
                    }
                    pts.Add(new PointF(mx + w, y - 2));
                    p.AddPolygon(pts.ToArray());
                }
                mx += w + (float)r.NextDouble() * 50;
            }
        }

        void AddTufts(Random r, GraphicsPath p, float x0, float x1, float y, float density)
        {
            for (float tx = x0; tx < x1; tx += 5 + (float)r.NextDouble() * 26 / density)
            {
                if (r.Next(3) == 0) continue;
                int blades = 2 + r.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    float bx = tx + b * 3, bh = 4 + (float)r.NextDouble() * 9, lean = (float)(r.NextDouble() - 0.5) * 7;
                    p.AddPolygon(new[] { new PointF(bx - 2.4f, y), new PointF(bx + lean, y - bh), new PointF(bx + 2.4f, y) });
                }
            }
        }

        void AddBits(Random r, float x0, float x1, float y0, float y1, int n)
        {
            bool leaf = Stage == StageDef.Autumn;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + (float)r.NextDouble() * (x1 - x0), y = y0 + (float)r.NextDouble() * (y1 - y0);
                if (Stage == StageDef.Grove) { (i % 2 == 0 ? bits : bitsAlt).AddEllipse(x - 1.6f, y - 1.2f, 3.2f, 2.4f); continue; }
                AddBit(i % 3 == 0 ? bitsAlt : bits, x, y, leaf ? 4 + (float)r.NextDouble() * 2 : 3 + (float)r.NextDouble() * 2.5f, (float)(r.NextDouble() * Math.PI), leaf, 0.5f);
            }
        }

        void BuildStageGeometry()
        {
            geoStage = Stage;
            var r = new Random(100 + Stage);
            stones = WPath(); stoneHi = WPath(); moss = WPath(); tufts = WPath(); joints = new GraphicsPath(); bits = WPath(); bitsAlt = WPath();
            platRock = WPath(); platSlab = WPath(); platMoss = WPath();

            // Steinblöcke der Hauptinsel
            float y = 16;
            int row = 0;
            while (y < 580)
            {
                float h = 46 + (float)r.NextDouble() * 18;
                float x = -670 - (row % 2) * 55;
                while (x < 670)
                {
                    float w = 80 + (float)r.NextDouble() * 75;
                    stones.AddPath(RoundRect(x, y, w - 6, h - 6, 10), false);
                    stoneHi.AddPath(RoundRect(x + 7, y + 4, w - 20, 6, 3), false);
                    x += w;
                }
                y += h; row++;
            }
            AddDrips(r, moss, -650, 650, 14, Stage == StageDef.Grove ? 46 : 70);
            AddTufts(r, tufts, -646, 646, -6, 1f);
            for (float jx = -640 + r.Next(60); jx < 640; jx += 70 + r.Next(70)) { joints.StartFigure(); joints.AddLine(jx, -6, jx - 3, 3); }
            for (float jx = -610 + r.Next(60); jx < 640; jx += 80 + r.Next(70)) { joints.StartFigure(); joints.AddLine(jx, 3, jx - 4, 13); }
            joints.StartFigure(); joints.AddLine(-640, 3, 640, 3);
            AddBits(r, -630, 630, -5, 11, Stage == StageDef.Grove ? 50 : 80);

            // Schwebeplattformen: Felsplatte mit zerklüfteter Unterseite
            foreach (SoftPlat sp in Arena.Soft)
            {
                float w = sp.R - sp.L;
                var pts = new List<PointF> { new PointF(sp.L - 2, sp.Y + 8) };
                const int n = 8;
                for (int k = 1; k < n; k++)
                {
                    float t = k / (float)n;
                    float depth = (float)Math.Sin(t * Math.PI) * (30 + w * 0.13f) + r.Next(12);
                    pts.Add(new PointF(sp.L + w * t + r.Next(-7, 7), sp.Y + 10 + depth));
                }
                pts.Add(new PointF(sp.R + 2, sp.Y + 8));
                platRock.AddPolygon(pts.ToArray());
                platSlab.AddPath(RoundRect(sp.L - 7, sp.Y - 6, w + 14, 20, 7), false);
                AddDrips(r, platMoss, sp.L - 4, sp.R + 4, sp.Y + 12, 34);
                AddTufts(r, tufts, sp.L - 4, sp.R + 4, sp.Y - 5, 1.4f);
                AddBits(r, sp.L, sp.R, sp.Y - 4, sp.Y + 8, 12);
            }

            // Bäume am Bühnenrand
            branches.Clear(); branchW.Clear();
            canopyA = WPath(); canopyB = WPath(); canopyC = WPath();
            AddTree(r, -650, 1);
            if (Stage != StageDef.Grove) AddTree(r, 650, -1);

            if (arenaBmp != null) arenaBmp.Dispose();
            arenaBmp = CacheLayer(arenaRect, 1.25f, DrawArenaStatic);
            for (int i = 0; i < 2; i++)
            {
                if (treeBmp[i] != null) treeBmp[i].Dispose();
                treeBmp[i] = Stage == StageDef.Grove && i == 1 ? null : CacheLayer(treeRects[i], 1f, DrawTrees);
            }
        }

        void AddTree(Random r, float x, int dir)
        {
            Func<float, float, PointF> P = (dx, dy) => new PointF(x + dx * dir, dy);
            // Stamm und Äste (Bezier-Kurven mit Breite)
            float[][] b =
            {
                new float[] { 0, 6, -40, -120, -30, -220, -60, -330, 38 },
                new float[] { -35, -200, 40, -260, 120, -330, 190, -410, 17 },
                new float[] { -55, -310, -90, -380, -150, -430, -210, -470, 14 },
                new float[] { -58, -320, -40, -400, 0, -460, 40, -520, 13 },
                new float[] { -40, -250, -110, -270, -190, -290, -270, -330, 11 },
                new float[] { 60, -270, 110, -300, 170, -310, 236, -360, 8 },
                new float[] { 0, 2, -30, 8, -50, 6, -70, 14, 12 },
                new float[] { 0, 2, 26, 6, 40, 4, 56, 8, 10 },
            };
            foreach (float[] v in b)
            {
                branches.Add(new[] { P(v[0], v[1]), P(v[2], v[3]), P(v[4], v[5]), P(v[6], v[7]) });
                branchW.Add(v[8]);
            }
            // Laubkronen aus vielen Kreisen in drei Tönen
            float[,] cl = { { 190, -420, 95, 66 }, { 40, -520, 90, 64 }, { -210, -480, 100, 66 }, { -280, -345, 80, 56 }, { -80, -440, 92, 70 }, { 250, -365, 70, 48 }, { 110, -478, 82, 58 } };
            for (int i = 0; i < cl.GetLength(0); i++)
            {
                PointF c = P(cl[i, 0], cl[i, 1]);
                float rx = cl[i, 2], ry = cl[i, 3];
                for (int k = 0; k < 24; k++) Blob(r, canopyA, c.X, c.Y + 12, rx, ry, 14, 30);
                for (int k = 0; k < 22; k++) Blob(r, canopyB, c.X, c.Y - 2, rx * 0.9f, ry * 0.85f, 12, 26);
                for (int k = 0; k < 10; k++) Blob(r, canopyC, c.X - rx * 0.1f, c.Y - ry * 0.35f, rx * 0.6f, ry * 0.45f, 8, 18);
            }
        }

        static void Blob(Random r, GraphicsPath p, float cx, float cy, float rx, float ry, float rmin, float rmax)
        {
            double a = r.NextDouble() * Math.PI * 2, d = Math.Sqrt(r.NextDouble());
            float x = cx + (float)(Math.Cos(a) * d) * rx, y = cy + (float)(Math.Sin(a) * d) * ry;
            float rr = rmin + (float)r.NextDouble() * (rmax - rmin);
            p.AddEllipse(x - rr, y - rr, rr * 2, rr * 2);
        }

        // ================================================================ Partikel (Blüten, Blätter, Glühwürmchen)

        Particle NewStageMote(bool anywhere)
        {
            StageTheme t = Th;
            if (Stage == StageDef.Grove)
                return new Particle
                {
                    Kind = 1,
                    X = (float)(rng.NextDouble() * 3000 - 1500),
                    Y = anywhere ? (float)(rng.NextDouble() * 1400 - 900) : 520,
                    VX = (float)(rng.NextDouble() - 0.5) * 0.5f,
                    VY = -0.25f - (float)rng.NextDouble() * 0.5f,
                    Life = 700, Max = 700,
                    Size = 2 + (float)rng.NextDouble() * 3,
                    C = rng.Next(3) == 0 ? t.BitsAlt : t.Bits
                };
            return new Particle
            {
                Kind = 9,
                X = anywhere ? (float)(rng.NextDouble() * 3200 - 1600) : (float)(rng.NextDouble() * 3000 - 1900),
                Y = anywhere ? (float)(rng.NextDouble() * 1500 - 950) : -950 - (float)rng.NextDouble() * 150,
                VX = 0.4f + (float)rng.NextDouble() * 0.9f,
                VY = 0.5f + (float)rng.NextDouble() * 0.9f,
                Rot = (float)(rng.NextDouble() * Math.PI * 2),
                VR = (float)(rng.NextDouble() - 0.5) * 0.08f,
                Life = 2400, Max = 2400,
                Size = Stage == StageDef.Autumn ? 6 + (float)rng.NextDouble() * 4 : 4 + (float)rng.NextDouble() * 3,
                C = rng.Next(3) == 0 ? t.BitsAlt : t.Bits
            };
        }

        // front: ein Drittel der Partikel fliegt vor den Kämpfern
        void DrawStageMotes(Graphics g, bool front)
        {
            for (int i = 0; i < motes.Count; i++)
            {
                if ((i % 3 == 0) != front) continue;
                Particle m = motes[i];
                if (m.Kind == 9)
                {
                    using (var path = new GraphicsPath())
                    {
                        float sw = (float)Math.Sin(frame * 0.05 + i) * 0.5f;
                        AddBit(path, m.X, m.Y, m.Size, m.Rot + sw, Stage == StageDef.Autumn, (float)Math.Abs(Math.Cos(frame * 0.04 + i)) * 0.7f + 0.3f);
                        using (var b = new SolidBrush(A(m.C, 0.9f))) g.FillPath(b, path);
                    }
                }
                else
                {
                    float pl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.07 + i * 1.7);
                    using (var b = new SolidBrush(A(m.C, 0.12f + 0.15f * pl))) g.FillEllipse(b, m.X - m.Size * 2.5f, m.Y - m.Size * 2.5f, m.Size * 5, m.Size * 5);
                    using (var b = new SolidBrush(A(m.C, 0.5f + 0.5f * pl))) g.FillEllipse(b, m.X - m.Size * 0.6f, m.Y - m.Size * 0.6f, m.Size * 1.2f, m.Size * 1.2f);
                }
            }
        }

        // ================================================================ Hintergrundbild

        static void VGrad(Graphics g, float x, float y, float w, float h, Color a, Color b)
        {
            using (var br = new LinearGradientBrush(new PointF(0, y - 1), new PointF(0, y + h + 1), a, b)) g.FillRectangle(br, x, y, w, h);
        }

        static void Halo(Graphics g, float x, float y, float r, Color c, int layers, float spread)
        {
            for (int k = layers; k >= 1; k--)
            {
                float rr = r * (1 + k * spread);
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, c.A / layers), c))) g.FillEllipse(b, x - rr, y - rr, rr * 2, rr * 2);
            }
        }

        static void Cloud(Graphics g, Random r, float x, float y, float s, Color c, Color shade)
        {
            using (var b = new SolidBrush(shade))
                for (int k = 0; k < 7; k++)
                {
                    float rx = s * (0.5f + (float)r.NextDouble() * 0.5f);
                    g.FillEllipse(b, x + (k - 3) * s * 0.55f - rx, y - rx * 0.55f + s * 0.12f, rx * 2, rx * 1.1f);
                }
            using (var b = new SolidBrush(c))
                for (int k = 0; k < 7; k++)
                {
                    float rx = s * (0.45f + (float)r.NextDouble() * 0.45f);
                    g.FillEllipse(b, x + (k - 3) * s * 0.5f - rx, y - rx * 0.7f - Math.Abs(k - 3) * -s * 0.05f, rx * 2, rx * 1.1f);
                }
        }

        static void Spires(Graphics g, Random r, float x0, float x1, float baseY, float minTop, float maxTop, float width, Color c)
        {
            var pts = new List<PointF> { new PointF(x0, baseY + 400) };
            float x = x0;
            while (x < x1 + width)
            {
                float top = minTop + (float)r.NextDouble() * (maxTop - minTop);
                float w = width * (0.6f + (float)r.NextDouble() * 0.8f);
                pts.Add(new PointF(x, baseY - (baseY - top) * 0.2f));
                pts.Add(new PointF(x + w * 0.3f, top + (baseY - top) * 0.12f));
                pts.Add(new PointF(x + w * 0.42f, top));
                pts.Add(new PointF(x + w * 0.58f, top + (baseY - top) * 0.05f));
                pts.Add(new PointF(x + w * 0.72f, top + (baseY - top) * 0.3f));
                x += w;
            }
            pts.Add(new PointF(x, baseY + 400));
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts.ToArray());
        }

        static void Pagoda(Graphics g, float x, float baseY, float s, int tiers, Color wall, Color roof, Color window)
        {
            float y = baseY;
            using (var wb = new SolidBrush(wall))
            using (var rb = new SolidBrush(roof))
            using (var lb = new SolidBrush(window))
            {
                for (int k = 0; k < tiers; k++)
                {
                    float tw = s * (80 - k * 13), wh = s * 20;
                    g.FillRectangle(wb, x - tw * 0.36f, y - wh, tw * 0.72f, wh);
                    int nw = Math.Max(1, (int)(tw / (s * 16)));
                    for (int i = 0; i < nw; i++) g.FillRectangle(lb, x - tw * 0.3f + (i + 0.25f) * tw * 0.6f / nw, y - wh * 0.75f, tw * 0.3f / nw, wh * 0.45f);
                    float ry = y - wh;
                    g.FillPolygon(rb, new[]
                    {
                        new PointF(x - tw * 0.64f, ry - s * 5), new PointF(x - tw * 0.5f, ry + s * 2), new PointF(x + tw * 0.5f, ry + s * 2),
                        new PointF(x + tw * 0.64f, ry - s * 5), new PointF(x + tw * 0.3f, ry - s * 12), new PointF(x - tw * 0.3f, ry - s * 12)
                    });
                    y = ry - s * 12;
                }
                g.FillRectangle(rb, x - s * 1.5f, y - s * 16, s * 3, s * 16);
            }
        }

        static void Waterfall(Graphics g, float x, float top, float bottom, float w, Color c)
        {
            using (var b = new LinearGradientBrush(new PointF(0, top - 1), new PointF(0, bottom + 1), Color.FromArgb(230, c), Color.FromArgb(150, c))) g.FillRectangle(b, x - w / 2, top, w, bottom - top);
            using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1, w * 0.12f)))
                for (int k = 0; k < 3; k++) g.DrawLine(p, x - w * 0.3f + k * w * 0.3f, top, x - w * 0.3f + k * w * 0.3f, bottom);
            using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                for (int k = 0; k < 5; k++) g.FillEllipse(b, x - w * 1.4f + k * w * 0.5f, bottom - w * 0.6f, w * 1.2f, w * 0.9f);
        }

        static void Torii(Graphics g, float x, float baseY, float s, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var d = new SolidBrush(Mixc(c, Color.Black, 0.45f)))
            {
                g.FillRectangle(b, x - 34 * s, baseY - 92 * s, 7 * s, 92 * s);
                g.FillRectangle(b, x + 27 * s, baseY - 92 * s, 7 * s, 92 * s);
                g.FillRectangle(b, x - 42 * s, baseY - 78 * s, 84 * s, 6 * s);
                g.FillPolygon(d, new[] { new PointF(x - 56 * s, baseY - 104 * s), new PointF(x + 56 * s, baseY - 104 * s), new PointF(x + 48 * s, baseY - 96 * s), new PointF(x - 48 * s, baseY - 96 * s) });
                g.FillRectangle(b, x - 50 * s, baseY - 97 * s, 100 * s, 6 * s);
            }
        }

        static void IslandBg(Graphics g, Random r, float x, float y, float s, Color rock, Color top)
        {
            var pts = new List<PointF> { new PointF(x - 60 * s, y) };
            for (int k = 1; k < 6; k++)
            {
                float t = k / 6f;
                pts.Add(new PointF(x - 60 * s + 120 * s * t + (float)(r.NextDouble() - 0.5) * 14 * s, y + (float)Math.Sin(t * Math.PI) * 110 * s * (0.6f + (float)r.NextDouble() * 0.5f)));
            }
            pts.Add(new PointF(x + 60 * s, y));
            using (var b = new SolidBrush(rock)) g.FillPolygon(b, pts.ToArray());
            using (var b = new SolidBrush(top)) g.FillEllipse(b, x - 64 * s, y - 9 * s, 128 * s, 18 * s);
        }

        static void Clump(Graphics g, Random r, float x, float y, float rx, float ry, int n, float rmin, float rmax, Color c)
        {
            using (var p = new GraphicsPath(FillMode.Winding))
            {
                for (int i = 0; i < n; i++) Blob(r, p, x, y, rx, ry, rmin, rmax);
                using (var b = new SolidBrush(c)) g.FillPath(b, p);
            }
        }

        void PaintStageBackground(Graphics g, int w, int h)
        {
            float u = h / 720f;
            var r = new Random(7 + Stage);
            if (Stage == StageDef.Blossom) PaintBlossom(g, r, w, h, u);
            else if (Stage == StageDef.Autumn) PaintAutumn(g, r, w, h, u);
            else PaintGrove(g, r, w, h, u);
        }

        void PaintBlossom(Graphics g, Random r, int w, int h, float u)
        {
            VGrad(g, 0, 0, w, h * 0.45f, Color.FromArgb(72, 100, 196), Color.FromArgb(150, 150, 222));
            VGrad(g, 0, h * 0.45f - 1, w, h * 0.2f + 1, Color.FromArgb(150, 150, 222), Color.FromArgb(240, 206, 214));
            // Sonne
            float sx = w * 0.6f, sy = h * 0.36f, sr = h * 0.095f;
            Halo(g, sx, sy, sr, Color.FromArgb(120, 255, 236, 200), 8, 0.4f);
            using (var b = new SolidBrush(Color.FromArgb(255, 246, 214))) g.FillEllipse(b, sx - sr, sy - sr, sr * 2, sr * 2);
            for (int i = 0; i < 6; i++) Cloud(g, r, w * (0.05f + i * 0.19f), h * (0.08f + (i % 3) * 0.07f), 40 * u, Color.FromArgb(200, 250, 246, 255), Color.FromArgb(150, 176, 170, 226));
            // Felsnadeln
            Spires(g, r, -40, w * 0.36f, h * 0.64f, h * 0.06f, h * 0.3f, 90 * u, Color.FromArgb(146, 150, 212));
            Spires(g, r, w * 0.64f, w + 40, h * 0.64f, h * 0.1f, h * 0.32f, 90 * u, Color.FromArgb(146, 150, 212));
            Spires(g, r, -40, w + 40, h * 0.66f, h * 0.3f, h * 0.5f, 70 * u, Color.FromArgb(112, 118, 184));
            Waterfall(g, w * 0.39f, h * 0.24f, h * 0.62f, 12 * u, Color.FromArgb(220, 230, 255));
            // Klippe mit Burg
            using (var b = new SolidBrush(Color.FromArgb(76, 84, 128)))
                g.FillPolygon(b, new[] { new PointF(w * 0.4f, h * 0.68f), new PointF(w * 0.43f, h * 0.5f), new PointF(w * 0.47f, h * 0.44f), new PointF(w * 0.6f, h * 0.43f), new PointF(w * 0.64f, h * 0.52f), new PointF(w * 0.66f, h * 0.68f) });
            Clump(g, r, w * 0.53f, h * 0.45f, w * 0.08f, h * 0.025f, 40, 6 * u, 14 * u, Color.FromArgb(76, 112, 92));
            Pagoda(g, w * 0.53f, h * 0.44f, 1.25f * u, 4, Color.FromArgb(96, 84, 112), Color.FromArgb(46, 46, 86), Color.FromArgb(255, 206, 130));
            Pagoda(g, w * 0.47f, h * 0.47f, 0.8f * u, 2, Color.FromArgb(96, 84, 112), Color.FromArgb(46, 46, 86), Color.FromArgb(255, 206, 130));
            Waterfall(g, w * 0.575f, h * 0.46f, h * 0.66f, 16 * u, Color.FromArgb(230, 238, 255));
            Waterfall(g, w * 0.605f, h * 0.48f, h * 0.66f, 9 * u, Color.FromArgb(230, 238, 255));
            // schwebende Insel mit Pagode
            IslandBg(g, r, w * 0.76f, h * 0.2f, 0.9f * u, Color.FromArgb(96, 100, 150), Color.FromArgb(110, 150, 110));
            Pagoda(g, w * 0.76f, h * 0.195f, 0.55f * u, 3, Color.FromArgb(96, 84, 112), Color.FromArgb(46, 46, 86), Color.FromArgb(255, 206, 130));
            Waterfall(g, w * 0.79f, h * 0.22f, h * 0.55f, 5 * u, Color.FromArgb(220, 230, 255));
            // See mit Spiegelung
            VGrad(g, 0, h * 0.64f, w, h * 0.36f, Color.FromArgb(134, 140, 214), Color.FromArgb(72, 80, 156));
            using (var p = new Pen(Color.FromArgb(110, 255, 236, 210), 2 * u))
                for (int k = 0; k < 12; k++) { float yy = h * (0.655f + k * 0.018f), ww = (40 - k * 2.5f) * u; g.DrawLine(p, sx - ww, yy, sx + ww, yy); }
            Torii(g, w * 0.73f, h * 0.67f, 1.1f * u, Color.FromArgb(204, 76, 62));
            // Inselchen im See und rosa Bäume
            for (int i = 0; i < 4; i++) Clump(g, r, w * (0.3f + i * 0.14f), h * 0.655f, 30 * u, 8 * u, 18, 5 * u, 10 * u, Color.FromArgb(70, 102, 86));
            Clump(g, r, w * 0.06f, h * 0.5f, w * 0.07f, h * 0.06f, 70, 8 * u, 18 * u, Color.FromArgb(230, 140, 186));
            Clump(g, r, w * 0.95f, h * 0.48f, w * 0.06f, h * 0.06f, 70, 8 * u, 18 * u, Color.FromArgb(230, 140, 186));
            Clump(g, r, w * 0.88f, h * 0.58f, w * 0.05f, h * 0.04f, 50, 6 * u, 14 * u, Color.FromArgb(244, 170, 206));
            VGrad(g, 0, h * 0.58f, w, h * 0.1f, Color.FromArgb(0, 255, 240, 250), Color.FromArgb(110, 255, 240, 250));
        }

        void PaintAutumn(Graphics g, Random r, int w, int h, float u)
        {
            VGrad(g, 0, 0, w, h * 0.4f, Color.FromArgb(92, 108, 196), Color.FromArgb(206, 160, 204));
            VGrad(g, 0, h * 0.4f - 1, w, h * 0.6f + 1, Color.FromArgb(206, 160, 204), Color.FromArgb(255, 204, 168));
            float sx = w * 0.82f, sy = h * 0.4f, sr = h * 0.085f;
            Halo(g, sx, sy, sr, Color.FromArgb(150, 255, 230, 180), 9, 0.45f);
            using (var b = new SolidBrush(Color.FromArgb(255, 248, 220))) g.FillEllipse(b, sx - sr, sy - sr, sr * 2, sr * 2);
            for (int i = 0; i < 9; i++) Cloud(g, r, w * (0.02f + i * 0.12f), h * (0.06f + (i % 3) * 0.08f), 34 * u, Color.FromArgb(200, 255, 214, 200), Color.FromArgb(150, 196, 140, 186));
            Spires(g, r, -40, w * 0.3f, h * 0.64f, h * 0.18f, h * 0.36f, 80 * u, Color.FromArgb(150, 132, 190));
            Spires(g, r, w * 0.7f, w + 40, h * 0.64f, h * 0.26f, h * 0.4f, 90 * u, Color.FromArgb(160, 130, 180));
            // Riesige Statue im Zentrum
            float cx = w * 0.5f, by = h * 0.62f, s = h * 0.00125f;
            Color stone = Color.FromArgb(152, 150, 182), light = Color.FromArgb(188, 182, 210), dark = Color.FromArgb(112, 108, 146);
            using (var p = new Pen(light, 16 * s)) g.DrawEllipse(p, cx - 105 * s, by - 445 * s, 210 * s, 210 * s);
            using (var b = new SolidBrush(Color.FromArgb(60, light))) g.FillEllipse(b, cx - 98 * s, by - 438 * s, 196 * s, 196 * s);
            using (var b = new SolidBrush(stone))
            {
                g.FillEllipse(b, cx - 170 * s, by - 100 * s, 340 * s, 120 * s);
                g.FillClosedCurve(b, new[] { new PointF(cx - 62 * s, by - 290 * s), new PointF(cx + 62 * s, by - 290 * s), new PointF(cx + 112 * s, by - 248 * s), new PointF(cx + 126 * s, by - 150 * s), new PointF(cx + 116 * s, by - 60 * s), new PointF(cx - 116 * s, by - 60 * s), new PointF(cx - 126 * s, by - 150 * s), new PointF(cx - 112 * s, by - 248 * s) }, FillMode.Winding, 0.4f);
                g.FillEllipse(b, cx - 44 * s, by - 392 * s, 88 * s, 106 * s);
                g.FillEllipse(b, cx - 26 * s, by - 418 * s, 52 * s, 44 * s);
                g.FillEllipse(b, cx - 54 * s, by - 360 * s, 18 * s, 44 * s);
                g.FillEllipse(b, cx + 36 * s, by - 360 * s, 18 * s, 44 * s);
            }
            using (var b = new SolidBrush(Color.FromArgb(120, dark)))
            {
                g.FillPolygon(b, new[] { new PointF(cx - 126 * s, by - 150 * s), new PointF(cx - 112 * s, by - 248 * s), new PointF(cx - 40 * s, by - 280 * s), new PointF(cx - 70 * s, by - 60 * s), new PointF(cx - 116 * s, by - 60 * s) });
                g.FillEllipse(b, cx - 44 * s, by - 392 * s, 40 * s, 106 * s);
            }
            using (var b = new SolidBrush(light)) g.FillEllipse(b, cx - 56 * s, by - 140 * s, 112 * s, 42 * s);
            using (var p = new Pen(dark, 3 * s))
            {
                g.DrawArc(p, cx - 26 * s, by - 350 * s, 18 * s, 8 * s, 0, 180);
                g.DrawArc(p, cx + 8 * s, by - 350 * s, 18 * s, 8 * s, 0, 180);
                g.DrawArc(p, cx - 80 * s, by - 250 * s, 160 * s, 120 * s, 20, 140);
                g.DrawArc(p, cx - 100 * s, by - 200 * s, 200 * s, 120 * s, 30, 120);
            }
            // Tempel auf Klippen und Felsinseln
            using (var b = new SolidBrush(Color.FromArgb(110, 104, 142)))
            {
                g.FillPolygon(b, new[] { new PointF(w * 0.24f, h * 0.7f), new PointF(w * 0.27f, h * 0.42f), new PointF(w * 0.33f, h * 0.36f), new PointF(w * 0.4f, h * 0.4f), new PointF(w * 0.43f, h * 0.7f) });
                g.FillPolygon(b, new[] { new PointF(w * 0.6f, h * 0.7f), new PointF(w * 0.62f, h * 0.5f), new PointF(w * 0.66f, h * 0.47f), new PointF(w * 0.69f, h * 0.55f), new PointF(w * 0.7f, h * 0.7f) });
            }
            Color temple = Color.FromArgb(150, 128, 132), red = Color.FromArgb(170, 54, 48), win = Color.FromArgb(255, 210, 140);
            Pagoda(g, w * 0.33f, h * 0.37f, 0.9f * u, 3, temple, red, win);
            Pagoda(g, w * 0.385f, h * 0.43f, 0.7f * u, 2, temple, red, win);
            Pagoda(g, w * 0.29f, h * 0.45f, 0.6f * u, 2, temple, red, win);
            Pagoda(g, w * 0.66f, h * 0.48f, 0.7f * u, 2, temple, red, win);
            IslandBg(g, r, w * 0.64f, h * 0.1f, 0.6f * u, Color.FromArgb(120, 110, 150), Color.FromArgb(180, 80, 64));
            Pagoda(g, w * 0.64f, h * 0.097f, 0.45f * u, 2, temple, red, win);
            IslandBg(g, r, w * 0.71f, h * 0.36f, 0.8f * u, Color.FromArgb(120, 110, 150), Color.FromArgb(180, 80, 64));
            Pagoda(g, w * 0.71f, h * 0.355f, 0.55f * u, 3, temple, red, win);
            using (var p = new Pen(Color.FromArgb(110, 70, 60), 2 * u)) { g.DrawBezier(p, w * 0.69f, h * 0.5f, w * 0.7f, h * 0.53f, w * 0.7f, h * 0.53f, w * 0.71f, h * 0.43f); g.DrawBezier(p, w * 0.69f, h * 0.49f, w * 0.7f, h * 0.52f, w * 0.7f, h * 0.52f, w * 0.71f, h * 0.42f); }
            Waterfall(g, w * 0.475f, h * 0.55f, h * 0.7f, 18 * u, Color.FromArgb(232, 238, 255));
            Waterfall(g, w * 0.51f, h * 0.56f, h * 0.7f, 12 * u, Color.FromArgb(232, 238, 255));
            Waterfall(g, w * 0.355f, h * 0.44f, h * 0.68f, 7 * u, Color.FromArgb(232, 238, 255));
            // roter Ahorn
            float[,] maple = { { 0.305f, 0.375f }, { 0.4f, 0.415f }, { 0.275f, 0.445f } };
            for (int i = 0; i < maple.GetLength(0); i++) Clump(g, r, w * maple[i, 0], h * maple[i, 1], 18 * u, 8 * u, 16, 3 * u, 7 * u, Color.FromArgb(206, 56, 40));
            // Wolkenmeer
            for (int i = 0; i < 14; i++) Cloud(g, r, w * (i * 0.08f - 0.02f), h * (0.68f + (i % 3) * 0.05f), 48 * u, Color.FromArgb(235, 255, 230, 226), Color.FromArgb(200, 214, 170, 196));
            VGrad(g, 0, h * 0.78f, w, h * 0.22f, Color.FromArgb(200, 250, 214, 214), Color.FromArgb(255, 226, 180, 196));
        }

        void PaintGrove(Graphics g, Random r, int w, int h, float u)
        {
            VGrad(g, 0, 0, w, h * 0.65f, Color.FromArgb(8, 12, 42), Color.FromArgb(36, 44, 118));
            VGrad(g, 0, h * 0.65f - 1, w, h * 0.35f + 1, Color.FromArgb(36, 44, 118), Color.FromArgb(20, 26, 80));
            for (int i = 0; i < 300; i++)
            {
                float x = (float)r.NextDouble() * w, y = (float)r.NextDouble() * h * 0.7f, s = (0.6f + (float)r.NextDouble() * 1.8f) * u;
                using (var b = new SolidBrush(Color.FromArgb(r.Next(80, 230), 220, 230, 255))) g.FillEllipse(b, x, y, s, s);
            }
            for (int i = 0; i < 5; i++) Halo(g, w * (0.1f + i * 0.2f), h * (0.2f + (i % 2) * 0.15f), 60 * u, Color.FromArgb(40, 140, 90, 230), 6, 0.5f);
            // Sichelmond
            float mx = w * 0.69f, my = h * 0.4f, mr = h * 0.045f;
            Halo(g, mx, my, mr, Color.FromArgb(70, 200, 220, 255), 6, 0.5f);
            using (var a = new GraphicsPath())
            using (var bp = new GraphicsPath())
            {
                a.AddEllipse(mx - mr, my - mr, mr * 2, mr * 2);
                bp.AddEllipse(mx - mr * 0.55f, my - mr * 1.15f, mr * 2, mr * 2);
                using (var reg = new Region(a))
                {
                    reg.Exclude(bp);
                    using (var b = new SolidBrush(Color.FromArgb(240, 246, 255))) g.FillRegion(b, reg);
                }
            }
            // Ruinen und Felsen in der Ferne
            Spires(g, r, -40, w * 0.35f, h * 0.68f, h * 0.3f, h * 0.48f, 70 * u, Color.FromArgb(40, 50, 116));
            Spires(g, r, w * 0.65f, w + 40, h * 0.68f, h * 0.26f, h * 0.46f, 70 * u, Color.FromArgb(40, 50, 116));
            using (var b = new SolidBrush(Color.FromArgb(30, 38, 92)))
                for (int i = 0; i < 9; i++)
                {
                    float x = w * (0.04f + i * 0.115f), top = h * (0.44f + (i % 3) * 0.05f), pw = (16 + (i % 2) * 10) * u;
                    if (x > w * 0.36f && x < w * 0.64f) continue;
                    g.FillPolygon(b, new[] { new PointF(x, h * 0.75f), new PointF(x, top + 8 * u), new PointF(x + pw * 0.4f, top), new PointF(x + pw, top + 12 * u), new PointF(x + pw, h * 0.75f) });
                    if (i % 3 == 1) g.FillRectangle(b, x - 6 * u, top - 4 * u, pw * 3.2f, 8 * u);
                }
            for (int i = 0; i < 5; i++)
            {
                float ix = w * (0.12f + i * 0.19f), iy = h * (0.18f + (i % 2) * 0.16f);
                if (ix > w * 0.35f && ix < w * 0.65f) iy = h * 0.06f;
                IslandBg(g, r, ix, iy, 0.5f * u, Color.FromArgb(36, 44, 104), Color.FromArgb(50, 110, 150));
                Pagoda(g, ix, iy - 3 * u, 0.35f * u, 2, Color.FromArgb(40, 48, 100), Color.FromArgb(26, 30, 74), Color.FromArgb(140, 220, 255));
            }
            // Leuchtender Weltenbaum
            float cx = w * 0.5f, base0 = h * 0.72f;
            Halo(g, cx, h * 0.36f, 90 * u, Color.FromArgb(110, 130, 120, 255), 10, 0.45f);
            Color bark = Color.FromArgb(70, 52, 132), barkL = Color.FromArgb(120, 92, 200);
            using (var p = new Pen(bark, 70 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(p, cx, base0, cx - 14 * u, h * 0.55f, cx + 12 * u, h * 0.45f, cx, h * 0.33f);
            using (var p = new Pen(bark, 26 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawBezier(p, cx, base0 - 10 * u, cx - 50 * u, base0, cx - 100 * u, base0 + 6 * u, cx - 150 * u, base0 + 10 * u);
                g.DrawBezier(p, cx, base0 - 10 * u, cx + 50 * u, base0, cx + 100 * u, base0 + 6 * u, cx + 150 * u, base0 + 10 * u);
                g.DrawBezier(p, cx, h * 0.38f, cx - 60 * u, h * 0.3f, cx - 140 * u, h * 0.25f, cx - 220 * u, h * 0.22f);
                g.DrawBezier(p, cx, h * 0.38f, cx + 60 * u, h * 0.3f, cx + 140 * u, h * 0.24f, cx + 230 * u, h * 0.2f);
                g.DrawBezier(p, cx, h * 0.36f, cx - 10 * u, h * 0.26f, cx + 20 * u, h * 0.18f, cx + 10 * u, h * 0.1f);
            }
            using (var p = new Pen(Color.FromArgb(150, barkL), 6 * u)) g.DrawBezier(p, cx - 24 * u, base0, cx - 30 * u, h * 0.55f, cx - 14 * u, h * 0.45f, cx - 18 * u, h * 0.35f);
            using (var gp = new Pen(Color.FromArgb(70, 120, 230, 255), 9 * u))
            using (var vp = new Pen(Color.FromArgb(230, 150, 236, 255), 2.2f * u))
            {
                PointF[][] veins =
                {
                    new[] { new PointF(cx - 10 * u, base0 - 8 * u), new PointF(cx + 14 * u, h * 0.6f), new PointF(cx - 12 * u, h * 0.5f), new PointF(cx + 6 * u, h * 0.38f) },
                    new[] { new PointF(cx + 18 * u, base0 - 4 * u), new PointF(cx + 6 * u, h * 0.6f), new PointF(cx + 22 * u, h * 0.48f), new PointF(cx + 4 * u, h * 0.4f) },
                    new[] { new PointF(cx - 60 * u, base0 + 2 * u), new PointF(cx - 30 * u, base0 - 6 * u), new PointF(cx - 20 * u, h * 0.64f), new PointF(cx - 14 * u, h * 0.58f) },
                };
                foreach (PointF[] v in veins) { g.DrawBezier(gp, v[0], v[1], v[2], v[3]); g.DrawBezier(vp, v[0], v[1], v[2], v[3]); }
            }
            Clump(g, r, cx, h * 0.2f, w * 0.3f, h * 0.15f, 260, 16 * u, 40 * u, Color.FromArgb(46, 32, 106));
            Clump(g, r, cx, h * 0.17f, w * 0.26f, h * 0.12f, 200, 14 * u, 32 * u, Color.FromArgb(78, 54, 156));
            Clump(g, r, cx - w * 0.03f, h * 0.12f, w * 0.18f, h * 0.07f, 90, 10 * u, 22 * u, Color.FromArgb(128, 96, 214));
            for (int i = 0; i < 90; i++)
            {
                double a = r.NextDouble() * Math.PI * 2, d = Math.Sqrt(r.NextDouble());
                float x = cx + (float)(Math.Cos(a) * d) * w * 0.28f, y = h * 0.19f + (float)(Math.Sin(a) * d) * h * 0.14f, s = (1.5f + (float)r.NextDouble() * 2.5f) * u;
                using (var b = new SolidBrush(Color.FromArgb(60, 150, 230, 255))) g.FillEllipse(b, x - s * 2.5f, y - s * 2.5f, s * 5, s * 5);
                using (var b = new SolidBrush(Color.FromArgb(230, 200, 240, 255))) g.FillEllipse(b, x - s / 2, y - s / 2, s, s);
            }
            Halo(g, cx, h * 0.37f, 8 * u, Color.FromArgb(200, 230, 250, 255), 5, 0.8f);
            Waterfall(g, w * 0.44f, h * 0.6f, h * 0.73f, 9 * u, Color.FromArgb(140, 210, 255));
            Waterfall(g, w * 0.56f, h * 0.62f, h * 0.73f, 7 * u, Color.FromArgb(140, 210, 255));
            VGrad(g, 0, h * 0.6f, w, h * 0.4f, Color.FromArgb(0, 70, 90, 200), Color.FromArgb(130, 60, 80, 190));
        }

        // ================================================================ Mittlere Ebene: schwebende Felsinseln (Parallaxe)

        void DrawStageMid(Graphics g)
        {
            StageTheme t = Th;
            var gs = g.Save();
            g.TranslateTransform(VW / 2f, VH / 2f);
            float z = 0.25f + Zoom * 0.5f;
            g.ScaleTransform(z, z);
            g.TranslateTransform(-CamX * 0.35f, -CamY * 0.35f + 80);
            using (var rock = new SolidBrush(t.MidRock))
            using (var shade = new SolidBrush(A(Mixc(t.MidRock, Color.Black, 0.35f), 0.9f)))
            using (var top = new SolidBrush(t.MidTop))
            using (var pag = new SolidBrush(A(Mixc(t.MidRock, Color.Black, 0.5f), 0.95f)))
            using (var win = new SolidBrush(A(t.Light, 0.8f)))
            using (var fall = new SolidBrush(Color.FromArgb(120, 230, 240, 255)))
                for (int i = 0; i < floatRocks.GetLength(0); i++)
                {
                    float bob = (float)Math.Sin(frame * 0.012 + i * 1.3) * 9;
                    var s2 = g.Save();
                    g.TranslateTransform(floatRocks[i, 0], floatRocks[i, 1] + bob);
                    g.ScaleTransform(floatRocks[i, 2], floatRocks[i, 2]);
                    int kind = (int)floatRocks[i, 3];
                    if (kind == 1) g.FillRectangle(fall, 22, 6, 7, 260);
                    g.FillPolygon(rock, new[] { new PointF(-66, 0), new PointF(66, 0), new PointF(44, 34), new PointF(16, 90), new PointF(2, 132), new PointF(-16, 80), new PointF(-48, 30) });
                    g.FillPolygon(shade, new[] { new PointF(10, 0), new PointF(66, 0), new PointF(44, 34), new PointF(16, 90), new PointF(2, 132), new PointF(8, 60) });
                    g.FillEllipse(top, -70, -9, 140, 18);
                    if (kind == 1)
                    {
                        g.FillRectangle(pag, -16, -30, 32, 24);
                        g.FillPolygon(pag, new[] { new PointF(-30, -28), new PointF(30, -28), new PointF(16, -42), new PointF(-16, -42) });
                        g.FillRectangle(pag, -10, -58, 20, 16);
                        g.FillPolygon(pag, new[] { new PointF(-22, -56), new PointF(22, -56), new PointF(10, -68), new PointF(-10, -68) });
                        g.FillRectangle(win, -8, -24, 6, 8); g.FillRectangle(win, 3, -24, 6, 8);
                    }
                    else if (kind == 2)
                    {
                        g.FillRectangle(pag, -4, -36, 7, 30);
                        g.FillEllipse(top, -30, -76, 60, 46);
                        g.FillEllipse(top, -40, -60, 40, 30);
                        g.FillEllipse(top, 2, -62, 38, 30);
                    }
                    g.Restore(s2);
                }
            g.Restore(gs);
        }

        // ================================================================ Bühne

        void DrawStageArena(Graphics g)
        {
            using (var b = new SolidBrush(A(Th.Under, 0.14f))) g.FillEllipse(b, -540, 360, 1080, 280);
            g.DrawImage(arenaBmp, arenaRect);
        }

        void DrawArenaStatic(Graphics g)
        {
            StageTheme t = Th;
            Box M = Arena.Main;
            using (var b = new SolidBrush(t.Joint)) g.FillPath(b, island);
            var clip = g.Save();
            g.SetClip(island, CombineMode.Intersect);
            using (var b = new LinearGradientBrush(new PointF(0, 10), new PointF(0, 570), t.Block, t.BlockDark)) g.FillPath(b, stones);
            using (var b = new SolidBrush(Color.FromArgb(26, 255, 255, 255))) g.FillPath(b, stoneHi);
            if (t.Glow)
                using (var p = new Pen(A(t.Moss, 0.22f), 7) { LineJoin = LineJoin.Round }) g.DrawPath(p, moss);
            using (var b = new SolidBrush(t.MossDark)) g.FillPath(b, moss);
            using (var b = new SolidBrush(A(t.Moss, 0.85f)))
            {
                var s2 = g.Save();
                g.TranslateTransform(0, -4);
                g.ScaleTransform(1, 0.7f);
                g.FillPath(b, moss);
                g.Restore(s2);
            }
            g.Restore(clip);
            // begehbare Oberkante aus Steinplatten
            using (var b = new LinearGradientBrush(new PointF(0, -8), new PointF(0, 15), t.Top, t.TopDark))
                g.FillRectangle(b, M.L - 10, -7, M.R - M.L + 20, 21);
            using (var p = new Pen(A(t.Joint, 0.55f), 1.6f)) g.DrawPath(p, joints);
            using (var p = new Pen(A(Mixc(t.Top, Color.White, 0.5f), 0.9f), 2.5f)) g.DrawLine(p, M.L - 10, -7, M.R + 10, -7);
            using (var p = new Pen(t.Joint, 2.5f)) g.DrawLine(p, M.L - 10, 14, M.R + 10, 14);
            // Wandkanten (zum Festhalten)
            using (var p = new Pen(A(t.Moss, 0.45f), 3))
            {
                g.DrawLine(p, M.L, 16, M.L, M.B);
                g.DrawLine(p, M.R, 16, M.R, M.B);
            }
            // Schwebeplattformen
            using (var b = new LinearGradientBrush(new PointF(0, -380), new PointF(0, -100), t.Block, t.BlockDark)) g.FillPath(b, platRock);
            using (var p = new Pen(t.Joint, 3) { LineJoin = LineJoin.Round }) g.DrawPath(p, platRock);
            using (var p = new Pen(t.Joint, 4)) g.DrawPath(p, platSlab);
            foreach (SoftPlat sp in Arena.Soft)
                using (var b = new LinearGradientBrush(new PointF(0, sp.Y - 7), new PointF(0, sp.Y + 15), t.Top, t.TopDark))
                using (var path = RoundRect(sp.L - 7, sp.Y - 6, sp.R - sp.L + 14, 20, 7))
                    g.FillPath(b, path);
            if (t.Glow)
                using (var p = new Pen(A(t.Moss, 0.22f), 6) { LineJoin = LineJoin.Round }) g.DrawPath(p, platMoss);
            using (var b = new SolidBrush(t.MossDark)) g.FillPath(b, platMoss);
            // Gras, Blüten/Blätter
            using (var b = new SolidBrush(t.Glow ? A(t.Moss, 0.85f) : t.Moss)) g.FillPath(b, tufts);
            using (var b = new SolidBrush(t.Bits)) g.FillPath(b, bits);
            using (var b = new SolidBrush(t.BitsAlt)) g.FillPath(b, bitsAlt);
        }

        // ================================================================ Dekoration hinter den Kämpfern

        void Lantern(Graphics g, float x, StageTheme t)
        {
            Color stone = Mixc(t.Block, Color.White, 0.2f), dark = t.BlockDark, ol = t.Joint;
            float fl = 0.8f + 0.2f * (float)Math.Sin(frame * 0.21 + x) * (float)Math.Sin(frame * 0.07 + x * 0.3);
            using (var b = new SolidBrush(A(t.Light, 0.18f * fl))) g.FillEllipse(b, x - 50, -110, 100, 90);
            using (var o = new SolidBrush(ol))
            using (var s = new SolidBrush(stone))
            using (var d = new SolidBrush(dark))
            {
                RectangleF[] parts =
                {
                    new RectangleF(x - 17, -12, 34, 12), new RectangleF(x - 7, -42, 14, 30), new RectangleF(x - 18, -48, 36, 7),
                    new RectangleF(x - 13, -72, 26, 24), new RectangleF(x - 5, -96, 10, 10)
                };
                foreach (RectangleF rc in parts) g.FillRectangle(o, rc.X - 2.5f, rc.Y - 2.5f, rc.Width + 5, rc.Height + 5);
                foreach (RectangleF rc in parts) g.FillRectangle(s, rc);
                g.FillRectangle(d, x - 7, -42, 5, 30);
                var roof = new[] { new PointF(x - 26, -70), new PointF(x + 26, -70), new PointF(x + 14, -84), new PointF(x - 14, -84) };
                using (var p = new Pen(ol, 5) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, roof);
                g.FillPolygon(s, roof);
                g.FillPolygon(d, new[] { new PointF(x - 26, -70), new PointF(x + 26, -70), new PointF(x + 22, -74), new PointF(x - 22, -74) });
            }
            using (var b = new SolidBrush(A(t.Light, 0.9f * fl))) g.FillRectangle(b, x - 8, -67, 16, 14);
            using (var b = new SolidBrush(A(Color.White, 0.6f * fl))) g.FillRectangle(b, x - 3, -64, 6, 8);
        }

        void Mushroom(Graphics g, float x, float s, int i, StageTheme t)
        {
            float pl = 0.6f + 0.4f * (float)Math.Sin(frame * 0.05 + i * 2.1);
            using (var b = new SolidBrush(A(t.Light, 0.16f * pl))) g.FillEllipse(b, x - 30 * s, -46 * s, 60 * s, 50 * s);
            using (var b = new SolidBrush(Color.FromArgb(190, 210, 255))) g.FillRectangle(b, x - 3 * s, -18 * s, 6 * s, 18 * s);
            using (var b = new SolidBrush(t.Joint)) g.FillPie(b, x - 17 * s, -32 * s, 34 * s, 30 * s, 180, 180);
            using (var b = new SolidBrush(Mixc(t.Light, t.MossDark, 0.35f * (1 - pl)))) g.FillPie(b, x - 15 * s, -30 * s, 30 * s, 26 * s, 180, 180);
            using (var b = new SolidBrush(Color.FromArgb(220, 230, 250, 255)))
            {
                g.FillEllipse(b, x - 8 * s, -25 * s, 4 * s, 4 * s);
                g.FillEllipse(b, x + 3 * s, -27 * s, 3 * s, 3 * s);
            }
        }

        void DrawTrees(Graphics g)
        {
            StageTheme t = Th;
            using (var o = RPen(t.Joint, 1))
            using (var p = RPen(t.Bark, 1))
            using (var hl = RPen(A(Mixc(t.Bark, Color.White, 0.25f), 0.8f), 1))
                for (int i = 0; i < branches.Count; i++)
                {
                    PointF[] b = branches[i];
                    o.Width = branchW[i] + 5; p.Width = branchW[i]; hl.Width = Math.Max(2, branchW[i] * 0.22f);
                    g.DrawBezier(o, b[0], b[1], b[2], b[3]);
                    g.DrawBezier(p, b[0], b[1], b[2], b[3]);
                    if (branchW[i] > 12) g.DrawBezier(hl, new PointF(b[0].X - 4, b[0].Y), new PointF(b[1].X - 4, b[1].Y), new PointF(b[2].X - 4, b[2].Y), b[3]);
                }
            using (var b = new SolidBrush(t.LeafA)) g.FillPath(b, canopyA);
            using (var b = new SolidBrush(t.LeafB)) g.FillPath(b, canopyB);
            using (var b = new SolidBrush(t.LeafC)) g.FillPath(b, canopyC);
        }

        void DrawStageDecor(Graphics g)
        {
            StageTheme t = Th;
            for (int i = 0; i < 2; i++)
                if (treeBmp[i] != null) g.DrawImage(treeBmp[i], treeRects[i]);
            if (t.Glow)
                for (int k = 0; k < 14; k++)
                {
                    float x = -650 + (k * 97 % 420) - 230, y = -330 - (k * 61 % 220);
                    float pl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.06 + k);
                    using (var b = new SolidBrush(A(t.Light, 0.25f * pl))) g.FillEllipse(b, x - 8, y - 8, 16, 16);
                    using (var b = new SolidBrush(A(Color.White, 0.8f * pl))) g.FillEllipse(b, x - 1.5f, y - 1.5f, 3, 3);
                }

            if (Stage == StageDef.Grove)
            {
                // Ruinentor rechts mit leuchtenden Runen
                using (var o = new SolidBrush(t.Joint))
                using (var s = new SolidBrush(t.Block))
                using (var d = new SolidBrush(t.BlockDark))
                {
                    RectangleF[] parts = { new RectangleF(520, -270, 34, 270), new RectangleF(606, -210, 32, 210), new RectangleF(504, -294, 112, 26) };
                    foreach (RectangleF rc in parts) g.FillRectangle(o, rc.X - 3, rc.Y - 3, rc.Width + 6, rc.Height + 6);
                    foreach (RectangleF rc in parts) g.FillRectangle(s, rc);
                    g.FillRectangle(d, 540, -270, 14, 270);
                    g.FillRectangle(d, 626, -210, 12, 210);
                    g.FillPolygon(o, new[] { new PointF(606, -210), new PointF(618, -222), new PointF(626, -212), new PointF(638, -226), new PointF(638, -210) });
                }
                float pl = 0.6f + 0.4f * (float)Math.Sin(frame * 0.05);
                using (var gp = new Pen(A(t.Light, 0.3f * pl), 7))
                using (var rp = new Pen(A(t.Light, 0.95f * pl), 2.2f))
                    foreach (Pen p in new[] { gp, rp })
                    {
                        g.DrawEllipse(p, 528, -200, 18, 18);
                        g.DrawLine(p, 537, -176, 537, -150);
                        g.DrawLine(p, 530, -140, 544, -126);
                        g.DrawLine(p, 612, -150, 630, -150);
                        g.DrawLine(p, 621, -160, 621, -130);
                    }
                float[] mx = { -600, -560, -505, 470, 590 };
                for (int i = 0; i < mx.Length; i++) Mushroom(g, mx[i], i % 2 == 0 ? 1.1f : 0.75f, i, t);
            }
            else
            {
                Lantern(g, -560, t);
                Lantern(g, 560, t);
            }
        }
    }
}
