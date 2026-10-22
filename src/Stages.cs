using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace Sensensturm
{
    // Auswählbare Arenen. Die Plattformen jeder Arena stehen in Arena.Layouts.
    public static class StageDef
    {
        public const int Graveyard = 0, Blossom = 1, Autumn = 2, Grove = 3, Count = 4;
        public static readonly string[] Names = { "Mondfriedhof", "Kirschblütental", "Herbsttempel", "Sternenhain" };
        // Hintergrundbild (eingebettete Ressource bzw. Datei im Ordner assets)
        public static readonly string[] Images = { null, "stage_blossom.jpg", "stage_autumn.jpg", "stage_grove.jpg" };
        // Weltbereich, den jedes Arena-Bild abdeckt (das Bild ist so vorbereitet, dass Bühne und Plattformen genau passen)
        public static readonly RectangleF ImageWorld = new RectangleF(-1150, -950, 2300, 1500);
        public static int Valid(int s) { return s >= 0 && s < Count ? s : Graveyard; }
    }

    // Farben für Partikel und Abgrund-Schimmer
    class StageTheme
    {
        public Color Bits, BitsAlt, Abyss, Sky, Ground;
    }

    public partial class Renderer
    {
        public int Stage;

        static readonly StageTheme[] themes =
        {
            null,
            new StageTheme { Bits = Color.FromArgb(255, 176, 208), BitsAlt = Color.FromArgb(255, 222, 236), Abyss = Color.FromArgb(210, 150, 230), Sky = Color.FromArgb(150, 150, 220), Ground = Color.FromArgb(40, 34, 50) },
            new StageTheme { Bits = Color.FromArgb(226, 64, 36), BitsAlt = Color.FromArgb(250, 140, 44), Abyss = Color.FromArgb(236, 150, 130), Sky = Color.FromArgb(180, 150, 200), Ground = Color.FromArgb(30, 24, 30) },
            new StageTheme { Bits = Color.FromArgb(120, 240, 255), BitsAlt = Color.FromArgb(196, 146, 255), Abyss = Color.FromArgb(70, 110, 236), Sky = Color.FromArgb(24, 32, 80), Ground = Color.FromArgb(12, 16, 44) },
        };

        StageTheme Th { get { return themes[Stage]; } }

        int motesStage = 0, imageStage = -1;
        Bitmap stageImg;
        readonly PointF[] bitPts = new PointF[10], petalPts = new PointF[6];

        void SyncStage()
        {
            Stage = StageDef.Valid(Stage);
            if (motesStage != Stage)
            {
                motesStage = Stage;
                motes.Clear();
                for (int i = 0; i < 46; i++) motes.Add(NewMote(true));
            }
            if (imageStage != Stage)
            {
                imageStage = Stage;
                if (stageImg != null) { stageImg.Dispose(); stageImg = null; }
                if (Stage != StageDef.Graveyard) stageImg = LoadStageImage(StageDef.Images[Stage]);
            }
        }

        // Lädt das Bild einmal und wandelt es in das schnellste Format für GDI+ (32bpp PArgb)
        static Bitmap LoadStageImage(string name)
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Sensensturm.assets." + name);
                if (s == null)
                {
                    string file = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets"), name);
                    if (!File.Exists(file)) return null;
                    s = File.OpenRead(file);
                }
                using (s)
                using (var src = new Bitmap(s))
                {
                    var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        // Zeichnet das Arena-Bild im Weltkoordinatensystem. Nur der sichtbare Ausschnitt wird skaliert.
        void DrawStageImage(Graphics g)
        {
            StageTheme t = Th;
            RectangleF world = StageDef.ImageWorld;
            RectangleF vis = g.VisibleClipBounds;
            if (!world.Contains(vis))
                using (var b = new LinearGradientBrush(new PointF(0, world.Top), new PointF(0, world.Bottom), t.Sky, t.Ground))
                {
                    b.WrapMode = WrapMode.TileFlipXY;
                    g.FillRectangle(b, vis);
                }
            if (stageImg == null) { DrawStageFallback(g); return; }
            RectangleF part = RectangleF.Intersect(vis, world);
            if (part.Width <= 0 || part.Height <= 0) return;
            float sx = stageImg.Width / world.Width, sy = stageImg.Height / world.Height;
            var src = new RectangleF((part.X - world.X) * sx, (part.Y - world.Y) * sy, part.Width * sx, part.Height * sy);
            // bewusst ohne ImageAttributes: die machen GDI+ hier um ein Vielfaches langsamer
            g.DrawImage(stageImg, part, src, GraphicsUnit.Pixel);
        }

        // Falls ein Bild fehlt: schlichte Bühne, damit die Arena spielbar bleibt
        void DrawStageFallback(Graphics g)
        {
            StageTheme t = Th;
            Box M = Arena.Main;
            using (var b = new SolidBrush(Mixc(t.Ground, Color.White, 0.25f))) g.FillRectangle(b, M.L, M.T, M.R - M.L, M.B - M.T);
            using (var b = new SolidBrush(Mixc(t.Ground, Color.White, 0.5f)))
                foreach (SoftPlat sp in Arena.SoftOf(Stage)) g.FillRectangle(b, sp.L, sp.Y - 4, sp.R - sp.L, 16);
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

        // Blütenblatt (Ellipse) oder Ahornblatt (Stern) als Polygon, im wiederverwendeten Punktpuffer
        PointF[] BitShape(float x, float y, float s, float ang, bool leaf, float flat)
        {
            int n = leaf ? 10 : 6;
            PointF[] pts = leaf ? bitPts : petalPts;
            double c = Math.Cos(ang), sn = Math.Sin(ang);
            for (int k = 0; k < n; k++)
            {
                double a = k * Math.PI * 2 / n;
                float rr = leaf ? (k % 2 == 0 ? s : s * 0.45f) : s;
                float px = (float)Math.Cos(a) * rr, py = (float)Math.Sin(a) * rr * (leaf ? 1f : 0.55f);
                pts[k] = new PointF(x + (float)(px * c - py * sn), y + (float)(px * sn + py * c) * flat);
            }
            return pts;
        }

        // front: ein Drittel der Partikel fliegt vor den Kämpfern
        void DrawStageMotes(Graphics g, bool front)
        {
            StageTheme t = Th;
            bool leaf = Stage == StageDef.Autumn;
            using (var b1 = new SolidBrush(A(t.Bits, 0.9f)))
            using (var b2 = new SolidBrush(A(t.BitsAlt, 0.9f)))
                for (int i = 0; i < motes.Count; i++)
                {
                    if ((i % 3 == 0) != front) continue;
                    Particle m = motes[i];
                    if (m.Kind == 9)
                    {
                        float sw = (float)Math.Sin(frame * 0.05 + i) * 0.5f;
                        g.FillPolygon(m.C == t.Bits ? b1 : b2, BitShape(m.X, m.Y, m.Size, m.Rot + sw, leaf, (float)Math.Abs(Math.Cos(frame * 0.04 + i)) * 0.7f + 0.3f));
                    }
                    else
                    {
                        float pl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.07 + i * 1.7);
                        using (var b = new SolidBrush(A(m.C, 0.12f + 0.15f * pl))) g.FillEllipse(b, m.X - m.Size * 2.5f, m.Y - m.Size * 2.5f, m.Size * 5, m.Size * 5);
                        using (var b = new SolidBrush(A(m.C, 0.5f + 0.5f * pl))) g.FillEllipse(b, m.X - m.Size * 0.6f, m.Y - m.Size * 0.6f, m.Size * 1.2f, m.Size * 1.2f);
                    }
                }
        }
    }
}
