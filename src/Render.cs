using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Sensensturm
{
    class Style
    {
        public Pen Outline, Bone, BoneBack, Thin, ShaftOut, Shaft, Edge, Rim, Spine;
        public Pen Thick, ThickBack, ThickOut, Crack, GlowLine, HeadOut, BowString, Arrow, Fine;
        public Pen Wrap, WrapBack, WrapOut, LimbOut, Sleeve, SleeveBack, Pants, PantsBack, Leather, LeatherBack, Fold, BladeDark;
        public SolidBrush BoneB, BoneBackB, OutlineB, Socket, EyeGlow, EyeCore, BladeB, ShieldB, RimB, GlowB, SkinB;
        public SolidBrush EyeRing, ClothB, ClothShadeB, ClothDarkB, LeatherB, LeatherDarkB, HairB, TrimB, Highlight;
        public float Alpha;
    }

    class Particle
    {
        public int Kind; // 0 Funke, 1 Glühen, 2 Ring, 3 Knochen, 4 Text, 5 Schädel, 6 Staub, 7 Splitter, 8 Bodenwelle
        public float X, Y, VX, VY, Life, Max, Size, Rot, VR, Grav, Drag = 1f;
        public Color C;
        public string Text;
    }

    class Rig
    {
        public PointF Pel, Chest, Sh, Head, EF, HF, EB, HB, KF, FF, KB, FB, Butt, Top, Tip, Inner, BladeA, BladeB, InA, InB;
    }

    public partial class Renderer
    {
        public const int VW = 1280, VH = 720;
        public float CamX = 0, CamY = -120, Zoom = 0.8f;
        float shake, shakeX, shakeY;
        public float Flash;
        Color flashColor = Color.White;
        readonly List<Particle> parts = new List<Particle>();
        readonly List<Particle> motes = new List<Particle>();
        readonly Random rng = new Random();
        public float[] OffX = new float[2], OffY = new float[2];
        readonly List<PointF>[] trailA = { new List<PointF>(), new List<PointF>() };
        readonly List<PointF>[] trailB = { new List<PointF>(), new List<PointF>() };

        class Ghost { public float X, Y; public int Facing, Char, Life; public Pose Pose; public bool Ground; }
        readonly List<Ghost> ghosts = new List<Ghost>();

        int goTimer;
        int frame;
        Bitmap bg;
        int bgW, bgH, bgStage = -1;
        readonly Dictionary<int, Style> styles = new Dictionary<int, Style>();
        public Font FTitle, FBig, FMed, FSmall, FTiny, FHudDmg, FHudName, FHuge;
        GraphicsPath island, islandBg, midLayer; // islandBg: Kopie nur für das Rendern im Hintergrund (LayerCache)
        LayerCache graveCache;

        public Renderer()
        {
            FTitle = new Font("Segoe UI Black", 64, FontStyle.Bold, GraphicsUnit.Pixel);
            FHuge = new Font("Segoe UI Black", 120, FontStyle.Bold, GraphicsUnit.Pixel);
            FBig = new Font("Segoe UI", 40, FontStyle.Bold, GraphicsUnit.Pixel);
            FMed = new Font("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
            FSmall = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            FTiny = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
            FHudDmg = new Font("Segoe UI Black", 38, FontStyle.Bold, GraphicsUnit.Pixel);
            FHudName = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            BuildArena();
            graveCache = new LayerCache(arenaRect, DrawGraveyardStatic);
            for (int i = 0; i < 46; i++) motes.Add(NewMote(true));
        }

        // ================================================================ Stil-Cache

        static Color A(Color c, float a) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * a))), c.R, c.G, c.B); }
        static Color Mixc(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static Pen RPen(Color c, float w) { var p = new Pen(c, w); p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round; return p; }

        // mode: 0 normal, 1 weißer Trefferblitz, 2 Geisterbild
        Style GetStyle(int ch, int mode, float alpha)
        {
            int ab = (int)(Math.Max(0, Math.Min(1, alpha)) * 10);
            int key = ch * 1000 + mode * 100 + ab;
            Style s;
            if (styles.TryGetValue(key, out s)) return s;
            CharDef c = CharDef.All[ch];
            float a = ab / 10f;
            Color bone = c.Bone, shade = c.Shade, outl = c.Outline, blade = c.Blade, shaft = c.Shaft, face = c.ShieldFace, rim = c.Rim;
            if (mode == 1) { bone = Color.White; shade = Color.FromArgb(235, 235, 245); blade = Color.White; shaft = Color.FromArgb(220, 220, 230); face = Color.FromArgb(240, 240, 250); rim = Color.White; outl = Color.FromArgb(255, 255, 255); }
            if (mode == 2) { bone = c.Glow; shade = c.Glow; outl = A(c.Glow, 0.3f); blade = c.Glow; shaft = c.Glow; face = c.Glow; rim = c.Glow; }
            // Kleidungsfarben: Trefferblitz hellt auf, Geisterbild färbt alles im Leuchtton
            Func<Color, Color> M = x => A(mode == 1 ? Mixc(x, Color.White, 0.85f) : mode == 2 ? c.Glow : x, a);
            Func<Color, Color> D = x => Mixc(x, Color.Black, 0.3f);
            Color pants = Mixc(c.LeatherDark, c.Outline, 0.55f);
            s = new Style
            {
                Outline = RPen(A(outl, a), 8f),
                Bone = RPen(A(bone, a), 4.6f),
                BoneBack = RPen(A(shade, a), 4.6f),
                Spine = RPen(A(bone, a), 5.2f),
                Thin = RPen(A(outl, a), 2.4f),
                ShaftOut = RPen(A(outl, a), 7.5f),
                Shaft = RPen(A(shaft, a), 4.2f),
                Edge = RPen(A(c.Edge, a * 0.95f), 2.6f),
                Rim = RPen(A(rim, a), 2.6f),
                BoneB = new SolidBrush(A(bone, a)),
                BoneBackB = new SolidBrush(A(shade, a)),
                OutlineB = new SolidBrush(A(outl, a)),
                Socket = new SolidBrush(A(Color.FromArgb(14, 4, 22), a)),
                EyeGlow = new SolidBrush(A(Color.FromArgb(120, c.EyeAura.A > 0 ? c.EyeAura : c.Glow), a)),
                EyeCore = new SolidBrush(A(c.Eye, a)),
                BladeB = new SolidBrush(A(blade, a)),
                ShieldB = new SolidBrush(A(face, a)),
                RimB = new SolidBrush(A(rim, a)),
                Thick = RPen(A(bone, a), 11f),
                ThickBack = RPen(A(shade, a), 11f),
                ThickOut = RPen(A(outl, a), 15.5f),
                Crack = RPen(A(mode == 2 ? c.Glow : c.Glow, a * 0.85f), 2f),
                GlowLine = RPen(A(c.Glow, a), 2f),
                HeadOut = RPen(A(outl, a), 5f),
                BowString = RPen(A(Color.FromArgb(240, 236, 224), a * 0.9f), 1.4f),
                Arrow = RPen(A(blade, a), 2.6f),
                Fine = RPen(A(outl, a), 1.4f),
                Wrap = RPen(M(c.LeatherDark), 7f),
                WrapBack = RPen(M(D(c.LeatherDark)), 7f),
                WrapOut = RPen(A(outl, a), 10f),
                LimbOut = RPen(A(outl, a), 10.5f),
                Sleeve = RPen(M(c.ClothDark), 6.5f),
                SleeveBack = RPen(M(D(c.ClothDark)), 6.5f),
                Pants = RPen(M(pants), 7f),
                PantsBack = RPen(M(D(pants)), 7f),
                Leather = RPen(M(c.LeatherDark), 7.5f),
                LeatherBack = RPen(M(D(c.LeatherDark)), 7.5f),
                Fold = RPen(M(D(c.Cloth)), 1.8f),
                BladeDark = RPen(M(D(c.Blade)), 9f),
                GlowB = new SolidBrush(A(c.Glow, a)),
                SkinB = new SolidBrush(A(mode == 0 ? c.Skin : bone, a)),
                EyeRing = new SolidBrush(A(c.EyeAura.A > 0 ? c.EyeAura : c.Glow, a)),
                ClothB = new SolidBrush(M(c.Cloth)),
                ClothShadeB = new SolidBrush(M(D(c.Cloth))),
                ClothDarkB = new SolidBrush(M(c.ClothDark)),
                LeatherB = new SolidBrush(M(c.Leather)),
                LeatherDarkB = new SolidBrush(M(c.LeatherDark)),
                HairB = new SolidBrush(M(c.Hair)),
                TrimB = new SolidBrush(M(c.Trim)),
                Highlight = new SolidBrush(A(Color.White, a * (mode == 2 ? 0 : 0.35f))),
                Alpha = a,
            };
            styles[key] = s;
            return s;
        }

        // ================================================================ Rig

        static PointF Dir(float deg, float len)
        {
            double r = deg * Math.PI / 180.0;
            return new PointF((float)Math.Cos(r) * len, (float)Math.Sin(r) * len);
        }
        static PointF Add(PointF a, PointF b) { return new PointF(a.X + b.X, a.Y + b.Y); }
        static PointF Mul(PointF a, float k) { return new PointF(a.X * k, a.Y * k); }
        static PointF Lp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        static void Leg(PointF hip, float a1, float a2, bool grounded, out PointF knee, out PointF foot)
        {
            knee = Add(hip, Dir(a1, 25));
            foot = Add(knee, Dir(a2, 25));
            if (grounded && foot.Y > 0)
            {
                float tx = foot.X - hip.X, ty = -hip.Y;
                float d = (float)Math.Sqrt(tx * tx + ty * ty);
                float dc = Math.Min(49.5f, Math.Max(1f, d));
                double bas = Math.Atan2(ty, tx);
                double off = Math.Acos(dc / 50.0);
                double ka = bas - off;
                knee = new PointF(hip.X + (float)Math.Cos(ka) * 25, hip.Y + (float)Math.Sin(ka) * 25);
                foot = new PointF(hip.X + tx / Math.Max(1, d) * dc, hip.Y + ty / Math.Max(1, d) * dc);
            }
        }

        static Rig BuildRig(Pose p, bool grounded)
        {
            var r = new Rig();
            r.Pel = new PointF(0, -50 + p.Crouch);
            double lr = p.Lean * Math.PI / 180;
            r.Chest = new PointF(r.Pel.X + (float)Math.Sin(lr) * 32, r.Pel.Y - (float)Math.Cos(lr) * 32);
            r.Sh = Lp(r.Chest, r.Pel, 0.1f);
            double hr = (p.Lean + p.Head) * Math.PI / 180;
            r.Head = new PointF(r.Chest.X + (float)Math.Sin(hr) * 17, r.Chest.Y - (float)Math.Cos(hr) * 17);
            r.EF = Add(r.Sh, Dir(p.AF1, 19)); r.HF = Add(r.EF, Dir(p.AF2, 19));
            r.EB = Add(r.Sh, Dir(p.AB1, 19)); r.HB = Add(r.EB, Dir(p.AB2, 19));
            Leg(r.Pel, p.LF1, p.LF2, grounded, out r.KF, out r.FF);
            Leg(r.Pel, p.LB1, p.LB2, grounded, out r.KB, out r.FB);
            PointF d = Dir(p.Scy, 1), b = Dir(p.Scy + 90, 1);
            r.Butt = Add(r.HF, Mul(d, -26));
            r.Top = Add(r.HF, Mul(d, 64));
            r.Tip = Add(r.Top, Add(Mul(b, 60), Mul(d, -26)));
            r.Inner = Add(r.Top, Add(Mul(b, 24), Mul(d, -4)));
            r.BladeA = Add(r.Top, Add(Mul(b, 22), Mul(d, 14)));
            r.BladeB = Add(r.Top, Add(Mul(b, 50), Mul(d, 6)));
            r.InA = Add(r.Top, Add(Mul(b, 40), Mul(d, -10)));
            r.InB = Add(r.Top, Add(Mul(b, 14), Mul(d, -12)));
            return r;
        }

        static PointF ToWorld(PointF q, float spin, int facing, float wx, float wy)
        {
            if (spin != 0)
            {
                double a = spin * Math.PI / 180;
                float x = q.X, y = q.Y + 50;
                q = new PointF((float)(x * Math.Cos(a) - y * Math.Sin(a)), (float)(x * Math.Sin(a) + y * Math.Cos(a)) - 50);
            }
            return new PointF(wx + q.X * facing, wy + q.Y);
        }

        // ================================================================ Kämpfer zeichnen

        void DrawSkull(Graphics g, Style st, PointF c, float ang, float scale, float eyeGlow, bool dizzy)
        {
            var gs = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(ang);
            if (scale != 1) g.ScaleTransform(scale, scale);
            // Umriss von Schädeldecke und Kiefer
            g.FillEllipse(st.OutlineB, -16, -18.5f, 32, 29.5f);
            using (var jaw = RoundRect(-3.5f, -2.5f, 20, 17, 5.5f)) g.FillPath(st.OutlineB, jaw);
            // Schädel mit Schatten am Hinterkopf
            g.FillEllipse(st.BoneBackB, -13.5f, -16, 27, 25);
            g.FillEllipse(st.BoneB, -11.5f, -16.5f, 25.5f, 23);
            using (var jaw = RoundRect(-1, 0, 15, 12, 4)) g.FillPath(st.BoneB, jaw);
            g.FillEllipse(st.Highlight, -6, -14.5f, 11, 5);
            // Riss in der Schädeldecke
            g.DrawLines(st.Fine, new[] { new PointF(-7, -15.5f), new PointF(-4, -11), new PointF(-6.5f, -7.5f) });
            // Zähne
            g.DrawLine(st.Fine, 0.5f, 7.5f, 13.5f, 7.5f);
            for (int t = 0; t < 4; t++) g.DrawLine(st.Fine, 2.6f + t * 3.2f, 4.8f, 2.6f + t * 3.2f, 10.5f);
            // Augenhöhlen, oben schräg abgeschnitten (grimmiger Blick)
            g.FillEllipse(st.Socket, 2, -8, 10.5f, 10);
            g.FillEllipse(st.Socket, -6, -7, 7, 9);
            g.FillPolygon(st.BoneB, new[] { new PointF(1, -11), new PointF(14, -11), new PointF(14, -4.6f), new PointF(1, -8.6f) });
            g.FillPolygon(st.BoneB, new[] { new PointF(-7, -10), new PointF(1.5f, -10), new PointF(1.5f, -5.2f), new PointF(-7, -7.6f) });
            g.DrawLine(st.Fine, 2, -8.4f, 13, -5);
            g.DrawLine(st.Fine, -5.5f, -7.4f, 1, -5.4f);
            // Nase
            g.FillPolygon(st.Socket, new[] { new PointF(10.6f, 0.2f), new PointF(12.8f, 4f), new PointF(8.8f, 4f) });
            // Leuchtende Augen
            if (eyeGlow > 0)
            {
                var gb = st.EyeGlow;
                float r1 = 5.5f + eyeGlow * 2.5f;
                g.FillEllipse(gb, 7.4f - r1, -3f - r1, r1 * 2, r1 * 2);
                g.FillEllipse(gb, -2.5f - r1 * 0.7f, -2.8f - r1 * 0.7f, r1 * 1.4f, r1 * 1.4f);
            }
            if (dizzy)
            {
                g.DrawArc(st.Rim, 4.5f, -6, 6, 6, 0, 300);
                g.DrawArc(st.Rim, -5, -5.5f, 5, 5, 0, 300);
            }
            else
            {
                g.FillEllipse(st.EyeRing, 4.4f, -5.6f, 6.2f, 5.4f);
                g.FillEllipse(st.EyeCore, 5.6f, -4.8f, 3.8f, 3.4f);
                g.FillEllipse(st.EyeRing, -4, -4.8f, 4.2f, 4.4f);
                g.FillEllipse(st.EyeCore, -3.2f, -4.1f, 2.6f, 2.8f);
            }
            g.Restore(gs);
        }

        static PointF Bez(PointF p0, PointF p1, PointF p2, PointF p3, float t)
        {
            float u = 1 - t;
            float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            return new PointF(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
        }

        static PointF Norm(PointF v)
        {
            float l = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
            return l < 0.001f ? new PointF(0, 0) : new PointF(v.X / l, v.Y / l);
        }

        void DrawScythe(Graphics g, Style st, Rig r, CharDef c, bool glow, float alpha)
        {
            // Schaft aus dunklem Holz mit Lederwicklung
            g.DrawLine(st.ShaftOut, r.Butt, r.Top);
            g.DrawLine(st.Shaft, r.Butt, r.Top);
            PointF ax = Norm(new PointF(r.Top.X - r.Butt.X, r.Top.Y - r.Butt.Y)), pr = new PointF(-ax.Y * 3.4f, ax.X * 3.4f);
            foreach (float t in new[] { 0.06f, 0.14f, 0.52f, 0.6f })
            {
                PointF q = Lp(r.Butt, r.Top, t);
                g.DrawLine(st.Fine, q.X - pr.X, q.Y - pr.Y, q.X + pr.X + ax.X * 2, q.Y + pr.Y + ax.Y * 2);
            }
            // Klinge aus altem Eisen
            using (var path = new GraphicsPath())
            {
                path.AddBezier(r.Top, r.BladeA, r.BladeB, r.Tip);
                path.AddBezier(r.Tip, r.InA, r.InB, Lp(r.Top, r.Butt, 0.08f));
                path.CloseFigure();
                if (glow)
                    using (var gp = RPen(A(c.Glow, 0.5f * alpha), 11f)) g.DrawPath(gp, path);
                g.DrawPath(st.HeadOut, path);
                g.FillPath(st.BladeB, path);
                // dunkle Patina am Klingenrücken
                var clip = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (var back = new GraphicsPath())
                {
                    back.AddBezier(r.Top, r.BladeA, r.BladeB, r.Tip);
                    g.DrawPath(st.BladeDark, back);
                }
                g.Restore(clip);
            }
            // Schneide mit Scharten
            using (var edge = new GraphicsPath())
            {
                edge.AddBezier(r.Tip, r.InA, r.InB, r.Top);
                g.DrawPath(st.Edge, edge);
            }
            foreach (float t in new[] { 0.3f, 0.55f, 0.78f })
            {
                PointF q = Bez(r.Tip, r.InA, r.InB, r.Top, t);
                PointF tn = Norm(new PointF(Bez(r.Tip, r.InA, r.InB, r.Top, t + 0.02f).X - q.X, Bez(r.Tip, r.InA, r.InB, r.Top, t + 0.02f).Y - q.Y));
                PointF o = Bez(r.Top, r.BladeA, r.BladeB, r.Tip, 1 - t);
                PointF into = Norm(new PointF(o.X - q.X, o.Y - q.Y));
                g.FillPolygon(st.OutlineB, new[]
                {
                    new PointF(q.X - tn.X * 2.6f, q.Y - tn.Y * 2.6f), new PointF(q.X + into.X * 4.2f, q.Y + into.Y * 4.2f), new PointF(q.X + tn.X * 2.6f, q.Y + tn.Y * 2.6f)
                });
            }
            // Eisenmanschette am Kopf der Sense
            g.FillEllipse(st.OutlineB, r.Top.X - 5, r.Top.Y - 5, 10, 10);
            g.FillEllipse(st.RimB, r.Top.X - 3.4f, r.Top.Y - 3.4f, 6.8f, 6.8f);
        }

        // Gliedmaßen je Körpertyp
        void DrawLimb(Graphics g, Style st, int body, PointF a, PointF b, PointF c, bool back, bool leg)
        {
            var pts = new[] { a, b, c };
            if (body == Body.Golem)
            {
                g.DrawLines(st.ThickOut, pts);
                g.DrawLines(back ? st.ThickBack : st.Thick, pts);
                g.DrawLine(st.Crack, Lp(a, b, 0.3f), Lp(a, b, 0.7f));
                g.DrawLine(st.Crack, Lp(b, c, 0.25f), Lp(b, c, 0.6f));
                SolidBrush gk = back ? st.BoneBackB : st.BoneB;
                g.FillEllipse(st.OutlineB, b.X - 7.5f, b.Y - 7.5f, 15, 15);
                g.FillEllipse(gk, b.X - 6, b.Y - 6, 12, 12);
                if (leg)
                {
                    var toe = new PointF(c.X + 10, c.Y);
                    g.DrawLine(st.ThickOut, c, toe);
                    g.DrawLine(back ? st.ThickBack : st.Thick, c, toe);
                }
                else
                {
                    g.FillEllipse(st.OutlineB, c.X - 9, c.Y - 9, 18, 18);
                    g.FillEllipse(gk, c.X - 7.2f, c.Y - 7.2f, 14.4f, 14.4f);
                }
                return;
            }
            if (body == Body.Huntress)
            {
                // Arm: grüner Ärmel, Lederarmschiene, Hand im fingerlosen Handschuh
                // Bein: dunkle Hose, Knieschoner, hohe Lederstiefel mit Stulpe
                PointF s0 = leg ? Lp(b, c, 0.38f) : Lp(b, c, 0.22f);
                g.DrawLines(st.LimbOut, pts);
                if (leg) g.DrawLine(st.LimbOut, c, new PointF(c.X + 9, c.Y));
                g.DrawLines(leg ? (back ? st.PantsBack : st.Pants) : (back ? st.SleeveBack : st.Sleeve), pts);
                g.DrawLine(back ? st.LeatherBack : st.Leather, s0, c);
                g.DrawLine(st.Fine, Lp(s0, c, 0.5f).X - 3.5f, Lp(s0, c, 0.5f).Y - 1, Lp(s0, c, 0.5f).X + 3.5f, Lp(s0, c, 0.5f).Y + 1);
                if (leg)
                {
                    g.DrawLine(back ? st.LeatherBack : st.Leather, c, new PointF(c.X + 9, c.Y));
                    // Stiefelstulpe
                    PointF dd = Norm(new PointF(c.X - b.X, c.Y - b.Y)), pp = new PointF(-dd.Y * 5.2f, dd.X * 5.2f);
                    g.FillPolygon(st.OutlineB, new[] { new PointF(s0.X - pp.X - dd.X, s0.Y - pp.Y - dd.Y), new PointF(s0.X + pp.X - dd.X, s0.Y + pp.Y - dd.Y), new PointF(s0.X + pp.X * 0.9f + dd.X * 5, s0.Y + pp.Y * 0.9f + dd.Y * 5), new PointF(s0.X - pp.X * 0.9f + dd.X * 5, s0.Y - pp.Y * 0.9f + dd.Y * 5) });
                    g.FillPolygon(st.LeatherB, new[] { new PointF(s0.X - pp.X * 0.75f, s0.Y - pp.Y * 0.75f), new PointF(s0.X + pp.X * 0.75f, s0.Y + pp.Y * 0.75f), new PointF(s0.X + pp.X * 0.65f + dd.X * 3.8f, s0.Y + pp.Y * 0.65f + dd.Y * 3.8f), new PointF(s0.X - pp.X * 0.65f + dd.X * 3.8f, s0.Y - pp.Y * 0.65f + dd.Y * 3.8f) });
                    // Knieschoner
                    var kp = new[] { new PointF(b.X, b.Y - 5.5f), new PointF(b.X + 5, b.Y), new PointF(b.X, b.Y + 5.5f), new PointF(b.X - 5, b.Y) };
                    g.DrawPolygon(st.HeadOut, kp);
                    g.FillPolygon(back ? st.LeatherDarkB : st.LeatherB, kp);
                }
                else
                {
                    g.FillEllipse(st.OutlineB, c.X - 4.8f, c.Y - 4.8f, 9.6f, 9.6f);
                    g.FillEllipse(st.SkinB, c.X - 3.4f, c.Y - 3.4f, 6.8f, 6.8f);
                    g.FillPie(st.LeatherDarkB, c.X - 3.4f, c.Y - 3.4f, 6.8f, 6.8f, 120, 180);
                }
                return;
            }
            // Skelett: Knochen, dunkle Stoffwickel um Unterarme und Schienbeine
            g.DrawLines(st.Outline, pts);
            g.DrawLines(back ? st.BoneBack : st.Bone, pts);
            SolidBrush kb = back ? st.BoneBackB : st.BoneB;
            if (leg)
            {
                PointF toe = new PointF(c.X + 9, c.Y + 0.5f);
                g.DrawLine(st.Outline, c, toe);
                g.DrawLine(back ? st.BoneBack : st.Bone, c, toe);
            }
            PointF w0 = Lp(b, c, leg ? 0.32f : 0.42f), w1 = leg ? new PointF(c.X + 3.5f, c.Y) : Lp(b, c, 0.86f);
            g.DrawLine(st.WrapOut, w0, w1);
            g.DrawLine(back ? st.WrapBack : st.Wrap, w0, w1);
            PointF wd = Norm(new PointF(w1.X - w0.X, w1.Y - w0.Y)), wp = new PointF(-wd.Y * 3.6f, wd.X * 3.6f);
            for (int k = 1; k < 4; k++)
            {
                PointF q = Lp(w0, w1, k / 4f);
                g.DrawLine(st.Fine, q.X - wp.X - wd.X * 1.5f, q.Y - wp.Y - wd.Y * 1.5f, q.X + wp.X + wd.X * 1.5f, q.Y + wp.Y + wd.Y * 1.5f);
            }
            g.FillEllipse(st.OutlineB, b.X - 4.8f, b.Y - 4.8f, 9.6f, 9.6f);
            g.FillEllipse(kb, b.X - 3.4f, b.Y - 3.4f, 6.8f, 6.8f);
            if (!leg)
            {
                // Knochenhand mit Krallen
                PointF hd = Norm(new PointF(c.X - b.X, c.Y - b.Y));
                for (int k = -1; k <= 1; k++)
                {
                    PointF fdir = Norm(new PointF(hd.X - hd.Y * k * 0.5f, hd.Y + hd.X * k * 0.5f));
                    PointF f = new PointF(c.X + fdir.X * 6.5f, c.Y + fdir.Y * 6.5f);
                    g.DrawLine(st.Thin, c, f);
                }
                g.FillEllipse(st.OutlineB, c.X - 4.8f, c.Y - 4.8f, 9.6f, 9.6f);
                g.FillEllipse(kb, c.X - 3.5f, c.Y - 3.5f, 7f, 7f);
            }
        }

        void DrawTorso(Graphics g, Style st, int body, Rig r, Pose p, CharDef c)
        {
            var gs = g.Save();
            g.TranslateTransform(r.Pel.X, r.Pel.Y);
            g.RotateTransform(p.Lean);
            if (body == Body.Golem)
            {
                using (var path = RoundRect(-19, -42, 38, 48, 10)) g.FillPath(st.OutlineB, path);
                using (var path = RoundRect(-16, -39, 32, 42, 8)) g.FillPath(st.BoneB, path);
                g.FillEllipse(st.OutlineB, -15, -47, 28, 17);
                g.FillEllipse(st.BoneBackB, -13, -45, 24, 13);
                float pul = 0.6f + 0.4f * (float)Math.Sin(frame * 0.1);
                using (var b = new SolidBrush(A(c.Glow, 0.35f * st.Alpha * pul))) g.FillEllipse(b, -8, -33, 22, 22);
                g.FillEllipse(st.GlowB, -2, -27, 10, 10);
                g.DrawLine(st.Crack, 2, -17, -4, -6);
                g.DrawLine(st.Crack, 7, -30, 12, -37);
                g.DrawLine(st.Crack, -6, -26, -13, -31);
            }
            else if (body == Body.Huntress)
            {
                // Köcher mit Pfeilen auf dem Rücken
                var q = g.Save();
                g.TranslateTransform(-10, -22);
                g.RotateTransform(-24);
                for (int k = 0; k < 3; k++)
                {
                    float x = -3 + k * 3;
                    g.DrawLine(st.HeadOut, x, -12, x - 0.5f, -24 + k);
                    g.DrawLine(st.Arrow, x, -12, x - 0.5f, -24 + k);
                    var fl = new[] { new PointF(x - 0.5f, -24 + k), new PointF(x - 3.2f, -29 + k), new PointF(x - 0.5f, -31 + k), new PointF(x + 2.2f, -29 + k) };
                    g.DrawPolygon(st.Fine, fl);
                    g.FillPolygon(st.EyeCore, fl);
                }
                using (var path = RoundRect(-5.5f, -15, 11, 30, 3.5f)) { g.DrawPath(st.HeadOut, path); g.FillPath(st.LeatherDarkB, path); }
                g.FillRectangle(st.TrimB, -5.5f, -15, 11, 3.2f);
                g.FillRectangle(st.TrimB, -5.5f, 8, 11, 2.4f);
                g.Restore(q);
                // Untergewand
                var tunic = new[] { new PointF(-9, -35), new PointF(10, -35), new PointF(13, 4), new PointF(-12, 4) };
                g.DrawPolygon(st.HeadOut, tunic);
                g.FillPolygon(st.ClothDarkB, tunic);
                // Lederweste mit Naht und Rautenmuster
                var vest = new[] { new PointF(-8, -31), new PointF(9.5f, -31), new PointF(11.5f, -7), new PointF(-9.5f, -7) };
                g.FillPolygon(st.LeatherB, vest);
                g.DrawPolygon(st.Fine, vest);
                g.DrawLine(st.Fine, 3, -30, 3.5f, -8);
                g.FillPolygon(st.LeatherDarkB, new[] { new PointF(3.2f, -25), new PointF(6, -21), new PointF(3.2f, -17), new PointF(0.4f, -21) });
                // Schulterkragen
                var col = new[] { new PointF(-11, -38), new PointF(11, -37), new PointF(9, -29), new PointF(2, -27), new PointF(-9, -30) };
                g.DrawPolygon(st.HeadOut, col);
                g.FillPolygon(st.ClothB, col);
                // Gürtel mit goldener Schnalle
                g.FillRectangle(st.OutlineB, -12.5f, -9.5f, 26, 7);
                g.FillRectangle(st.LeatherDarkB, -11.5f, -8.5f, 24, 5);
                g.FillEllipse(st.OutlineB, 1, -10.5f, 9, 9);
                g.FillEllipse(st.TrimB, 2.2f, -9.3f, 6.6f, 6.6f);
                // Rockschöße
                foreach (var tab in new[]
                {
                    new[] { new PointF(-12, -2.5f), new PointF(-4, -2.5f), new PointF(-5.5f, 9), new PointF(-12.5f, 6.5f) },
                    new[] { new PointF(-3, -2.5f), new PointF(5, -2.5f), new PointF(4.5f, 10), new PointF(-2.5f, 10) },
                    new[] { new PointF(6, -2.5f), new PointF(13.5f, -2.5f), new PointF(15, 7), new PointF(8, 8.5f) },
                })
                {
                    g.DrawPolygon(st.HeadOut, tab);
                    g.FillPolygon(st.ClothB, tab);
                }
            }
            else
            {
                // Becken
                g.FillEllipse(st.OutlineB, -13, -7, 26, 14);
                g.FillEllipse(st.BoneB, -11, -5.5f, 22, 11);
                // Wirbelsäule und Rippen
                g.DrawLine(st.Outline, 0, -2, -1, -36);
                g.DrawLine(st.Spine, 0, -2, -1, -36);
                for (int k = 0; k < 4; k++)
                {
                    float y = -14 - k * 5.4f;
                    float w = 16 - Math.Abs(k - 1.5f) * 2.4f;
                    g.DrawArc(st.Outline, -w * 0.45f, y - 4, w * 1.5f, 9.5f, 205, 145);
                    g.DrawArc(st.Bone, -w * 0.45f, y - 4, w * 1.5f, 9.5f, 205, 145);
                }
                // Gürtel mit Schädelschnalle
                g.FillRectangle(st.OutlineB, -14, -6.5f, 29, 7.5f);
                g.FillRectangle(st.LeatherB, -13, -5.5f, 27, 5.5f);
                g.FillEllipse(st.OutlineB, 1, -8.5f, 11, 11);
                g.FillEllipse(st.BoneB, 2.2f, -7.3f, 8.6f, 8.6f);
                g.FillEllipse(st.Socket, 3.6f, -5.2f, 2.4f, 2.6f);
                g.FillEllipse(st.Socket, 6.8f, -5.2f, 2.4f, 2.6f);
            }
            g.Restore(gs);
        }

        // zerfetzter Umhang um die Schultern des Skeletts
        void DrawShawl(Graphics g, Style st, Rig r, Pose p, PlayerState P)
        {
            float wv = (float)Math.Sin(frame * 0.22 + P.slot) * 2.5f;
            float len = 10 + Math.Min(26, Math.Abs(P.vx) * 2.2f + Math.Abs(P.vy));
            var gs = g.Save();
            g.TranslateTransform(r.Chest.X, r.Chest.Y);
            g.RotateTransform(p.Lean);
            var tail = new[]
            {
                new PointF(-6, -6), new PointF(-14 - len, 1 + wv), new PointF(-10 - len * 0.75f, 4 + wv), new PointF(-15 - len * 0.6f, 10 - wv),
                new PointF(-8 - len * 0.3f, 9), new PointF(-9, 14 - wv), new PointF(-4, 6)
            };
            g.DrawPolygon(st.HeadOut, tail);
            g.FillPolygon(st.ClothShadeB, tail);
            var col = new[]
            {
                new PointF(-12, -6), new PointF(-4, -10), new PointF(6, -9.5f), new PointF(12.5f, -4), new PointF(12.5f, 4), new PointF(10, 12),
                new PointF(7, 7.5f), new PointF(4, 15), new PointF(0.5f, 8.5f), new PointF(-3.5f, 14), new PointF(-6, 6.5f), new PointF(-11, 9), new PointF(-13, 1)
            };
            g.DrawPolygon(st.HeadOut, col);
            g.FillPolygon(st.ClothB, col);
            g.DrawLine(st.Fold, 2, -6, 3.5f, 6);
            g.DrawLine(st.Fold, 8, -4, 9, 5);
            g.DrawLine(st.Fold, -5, -5, -4.5f, 4);
            g.Restore(gs);
        }

        // zerfetztes Lendentuch (hinten dunkler, vorne über den Oberschenkeln)
        void DrawLoincloth(Graphics g, Style st, Rig r, Pose p, PlayerState P, bool front)
        {
            float sw = Math.Max(-9, Math.Min(9, -P.vx * P.facing * 0.9f)) + (float)Math.Sin(frame * 0.2 + P.slot) * 1.5f;
            var gs = g.Save();
            g.TranslateTransform(r.Pel.X, r.Pel.Y);
            g.RotateTransform(p.Lean);
            PointF[] pts = front
                ? new[] { new PointF(-3, 0), new PointF(10, 0), new PointF(10 + sw, 15), new PointF(7 + sw, 12), new PointF(4.5f + sw, 18), new PointF(1.5f + sw, 12.5f), new PointF(-1 + sw, 16), new PointF(-4 + sw * 0.6f, 9) }
                : new[] { new PointF(-13, -1), new PointF(-1, 0), new PointF(-2 + sw * 1.3f, 17), new PointF(-6 + sw * 1.3f, 13), new PointF(-9.5f + sw * 1.3f, 19), new PointF(-12 + sw, 12), new PointF(-15 + sw, 14) };
            g.DrawPolygon(st.HeadOut, pts);
            g.FillPolygon(front ? st.ClothDarkB : st.LeatherDarkB, pts);
            if (front) g.DrawLine(st.Fine, 3.5f + sw * 0.3f, 3, 4.5f + sw * 0.8f, 11);
            g.Restore(gs);
        }

        // grüner Umhang der Jägerin
        void DrawCape(Graphics g, Style st, Rig r, PlayerState P)
        {
            float wv = (float)Math.Sin(frame * 0.2 + P.slot) * 3;
            float len = 6 + Math.Min(32, Math.Abs(P.vx) * 2.6f + Math.Abs(P.vy) * 1.2f);
            PointF n = r.Chest;
            float bot = 44 - len * 0.45f;
            using (var path = new GraphicsPath())
            {
                path.AddLine(new PointF(n.X + 3, n.Y - 3), new PointF(n.X - 8, n.Y - 2));
                path.AddBezier(new PointF(n.X - 8, n.Y - 2), new PointF(n.X - 16, n.Y + 8), new PointF(n.X - 14 - len * 0.5f, n.Y + bot * 0.6f), new PointF(n.X - 14 - len * 0.7f, n.Y + bot + wv));
                path.AddLine(new PointF(n.X - 14 - len * 0.7f, n.Y + bot + wv), new PointF(n.X - 8 - len * 0.5f, n.Y + bot - 5 - wv));
                path.AddLine(new PointF(n.X - 8 - len * 0.5f, n.Y + bot - 5 - wv), new PointF(n.X - 4 - len * 0.3f, n.Y + bot + 2 + wv * 0.5f));
                path.AddLine(new PointF(n.X - 4 - len * 0.3f, n.Y + bot + 2 + wv * 0.5f), new PointF(n.X + 1 - len * 0.1f, n.Y + bot - 6));
                path.AddBezier(new PointF(n.X + 1 - len * 0.1f, n.Y + bot - 6), new PointF(n.X + 2, n.Y + 22), new PointF(n.X + 4, n.Y + 6), new PointF(n.X + 3, n.Y - 3));
                path.CloseFigure();
                g.DrawPath(st.HeadOut, path);
                g.FillPath(st.ClothShadeB, path);
                var clip = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (var inner = new GraphicsPath())
                {
                    inner.AddBezier(new PointF(n.X - 8, n.Y - 2), new PointF(n.X - 16, n.Y + 8), new PointF(n.X - 14 - len * 0.5f, n.Y + bot * 0.6f), new PointF(n.X - 14 - len * 0.7f, n.Y + bot + wv));
                    using (var pen = RPen(((SolidBrush)st.ClothB).Color, 9)) g.DrawPath(pen, inner);
                }
                g.Restore(clip);
            }
        }

        void DrawHead(Graphics g, Style st, int body, PointF c, float ang, float scale, float eyeGlow, bool dizzy)
        {
            if (body == Body.Skeleton) { DrawSkull(g, st, c, ang, scale, eyeGlow, dizzy); return; }
            var gs = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(ang);
            if (scale != 1) g.ScaleTransform(scale, scale);
            if (body == Body.Golem)
            {
                using (var path = RoundRect(-15, -15, 31, 29, 7)) g.FillPath(st.OutlineB, path);
                using (var path = RoundRect(-12.5f, -12.5f, 26, 24, 5)) g.FillPath(st.BoneB, path);
                g.FillRectangle(st.BoneBackB, -12, -9, 26, 5);
                g.FillPolygon(st.GlowB, new[] { new PointF(-7, -13), new PointF(-4, -22), new PointF(-1, -13) });
                g.FillPolygon(st.GlowB, new[] { new PointF(1, -13), new PointF(3, -19), new PointF(5, -13) });
                if (eyeGlow > 0) g.FillEllipse(st.EyeGlow, -1, -10, 17, 11);
                if (dizzy) { g.DrawArc(st.Rim, 4, -7, 6, 6, 0, 300); g.DrawArc(st.Rim, -5, -7, 5, 5, 0, 300); }
                else { g.FillRectangle(st.EyeCore, 3, -5.5f, 9, 3.2f); g.FillRectangle(st.EyeCore, -5, -5.5f, 5, 3.2f); }
                g.DrawLine(st.Crack, -8, 4, -2, 9);
                g.DrawLine(st.Thin, 2, 6, 11, 6);
            }
            else
            {
                // Spitze Kapuze
                using (var hood = new GraphicsPath())
                {
                    hood.AddPolygon(new[]
                    {
                        new PointF(14, -5), new PointF(11, -14), new PointF(2, -19.5f), new PointF(-8, -18.5f), new PointF(-22, -25),
                        new PointF(-16, -10), new PointF(-16, 2), new PointF(-11, 12), new PointF(0, 15), new PointF(10, 12)
                    });
                    g.DrawPath(st.HeadOut, hood);
                    g.FillPath(st.ClothB, hood);
                }
                g.FillPolygon(st.ClothShadeB, new[] { new PointF(-8, -18.5f), new PointF(-22, -25), new PointF(-16, -10), new PointF(-16, 2), new PointF(-11, 12), new PointF(-6, 2), new PointF(-7, -10) });
                g.DrawLine(st.Fold, -7, -12, -13, -18);
                // Gesicht
                g.FillEllipse(st.ClothDarkB, -3.5f, -11, 19, 23);
                g.FillEllipse(st.OutlineB, -1.5f, -9.5f, 17, 20.5f);
                g.FillEllipse(st.SkinB, -0.3f, -8.3f, 14.8f, 18.2f);
                // Haare (Pony)
                g.FillPolygon(st.HairB, new[]
                {
                    new PointF(-1, -9.5f), new PointF(14, -8), new PointF(14.5f, -3), new PointF(11.5f, -5.5f), new PointF(9.5f, -1),
                    new PointF(7, -5.5f), new PointF(4, 0), new PointF(2, -5), new PointF(-1, 2)
                });
                g.FillPolygon(st.ClothB, new[] { new PointF(-3, -12), new PointF(13, -11), new PointF(15, -7.5f), new PointF(-2, -8) });
                g.DrawLine(st.Fine, -2, -8, 15, -7.5f);
                if (dizzy) g.DrawArc(st.Rim, 6, -3, 5, 5, 0, 300);
                else
                {
                    // entschlossener Blick
                    g.FillEllipse(st.EyeCore, 5.4f, -3.2f, 5.6f, 5.6f);
                    g.FillEllipse(st.OutlineB, 7.2f, -3f, 3.4f, 5);
                    g.FillEllipse(st.EyeCore, 8.4f, -2.4f, 1.3f, 1.3f);
                    g.DrawLine(st.Thin, 5, -5.2f, 11.5f, -3.6f);
                }
                g.DrawLine(st.Fine, 9.6f, 6.2f, 12.4f, 5.6f);
            }
            g.Restore(gs);
        }

        static float BowPull(PlayerState P)
        {
            if (P.state != St.Attack || P.atk < 0) return 0;
            AttackDef d = Attacks.Of(P);
            if (d.ProjKind != 0 || P.atkFrame >= d.Startup) return 0;
            return Math.Min(1f, 0.3f + 0.7f * P.atkFrame / (float)d.Startup);
        }

        void DrawWeapon(Graphics g, Style st, Rig r, CharDef c, PlayerState P, Pose pose, bool glow, float alpha)
        {
            if (c.Weapon == Weapon.Scythe) { DrawScythe(g, st, r, c, glow, alpha); return; }
            PointF d = Dir(pose.Scy, 1), b = Dir(pose.Scy + 90, 1);
            if (c.Weapon == Weapon.Hammer)
            {
                PointF butt = Add(r.HF, Mul(d, -24)), top = Add(r.HF, Mul(d, 58));
                g.DrawLine(st.ShaftOut, butt, top);
                g.DrawLine(st.Shaft, butt, top);
                PointF[] hd =
                {
                    Add(top, Add(Mul(b, 27), Mul(d, 16))), Add(top, Add(Mul(b, 27), Mul(d, -16))),
                    Add(top, Add(Mul(b, -22), Mul(d, -13))), Add(top, Add(Mul(b, -22), Mul(d, 13)))
                };
                if (glow) using (var gp = RPen(A(c.Edge, 0.45f * alpha), 11)) g.DrawPolygon(gp, hd);
                g.DrawPolygon(st.HeadOut, hd);
                g.FillPolygon(st.BladeB, hd);
                g.DrawLine(st.Thin, hd[0], hd[1]);
                g.DrawLine(st.Edge, Add(top, Mul(b, 16)), Add(top, Mul(b, -12)));
                g.FillEllipse(st.GlowB, top.X - 4, top.Y - 4, 8, 8);
                return;
            }
            // Recurve-Bogen aus Holz mit goldenen Beschlägen
            float pull = BowPull(P);
            PointF tipA = Add(r.HF, Add(Mul(b, 34), Mul(d, -9))), tipB = Add(r.HF, Add(Mul(b, -34), Mul(d, -9)));
            PointF endA = Add(tipA, Add(Mul(b, 3), Mul(d, 6))), endB = Add(tipB, Add(Mul(b, -3), Mul(d, 6)));
            PointF str = Add(r.HF, Mul(d, -8 - pull * 22));
            g.DrawLine(st.BowString, endA, str);
            g.DrawLine(st.BowString, str, endB);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(endA, Add(tipA, Mul(d, -3)), Add(r.HF, Add(Mul(b, 20), Mul(d, 13))), r.HF);
                path.AddBezier(r.HF, Add(r.HF, Add(Mul(b, -20), Mul(d, 13))), Add(tipB, Mul(d, -3)), endB);
                if (glow) using (var gp = RPen(A(c.Glow, 0.4f * alpha), 11)) g.DrawPath(gp, path);
                g.DrawPath(st.ShaftOut, path);
                g.DrawPath(st.Shaft, path);
            }
            // Griffwicklung, Blattverzierung, Spitzen
            g.DrawLine(st.Leather, Add(r.HF, Mul(b, 5)), Add(r.HF, Mul(b, -5)));
            foreach (int sgn in new[] { 1, -1 })
            {
                PointF lf = Add(r.HF, Add(Mul(b, 13 * sgn), Mul(d, 8)));
                var leaf = new[] { Add(lf, Mul(b, 5 * sgn)), Add(lf, Mul(d, 2.6f)), Add(lf, Mul(b, -5 * sgn)), Add(lf, Mul(d, -2.6f)) };
                g.DrawPolygon(st.Fine, leaf);
                g.FillPolygon(st.ClothB, leaf);
            }
            g.FillEllipse(st.OutlineB, endA.X - 3, endA.Y - 3, 6, 6);
            g.FillEllipse(st.TrimB, endA.X - 2, endA.Y - 2, 4, 4);
            g.FillEllipse(st.OutlineB, endB.X - 3, endB.Y - 3, 6, 6);
            g.FillEllipse(st.TrimB, endB.X - 2, endB.Y - 2, 4, 4);
            if (pull > 0.05f)
            {
                PointF ah = Add(r.HF, Mul(d, 26));
                if (P.charge > 0) using (var gb = new SolidBrush(A(c.Glow, 0.35f * alpha))) g.FillEllipse(gb, ah.X - 13, ah.Y - 13, 26, 26);
                g.DrawLine(st.Fine, str, ah);
                g.DrawLine(st.Arrow, str, ah);
                g.FillPolygon(st.EyeCore, new[] { str, Add(str, Add(Mul(d, -5), Mul(b, 3.5f))), Add(str, Mul(d, 4)), Add(str, Add(Mul(d, -5), Mul(b, -3.5f))) });
                g.FillPolygon(st.GlowB, new[] { Add(ah, Mul(d, 10)), Add(ah, Mul(b, 4.5f)), Add(ah, Mul(b, -4.5f)) });
            }
        }

        void DrawShield(Graphics g, Style st, PointF hand, float lean, bool front, float alpha, CharDef c)
        {
            var gs = g.Save();
            g.TranslateTransform(hand.X + (front ? 5 : 2), hand.Y);
            g.RotateTransform(lean * 0.5f);
            float s = (front ? 1.08f : 0.92f) * (c.Body == Body.Huntress ? 0.8f : c.Body == Body.Golem ? 1.15f : 1f);
            g.ScaleTransform(s, s);
            PointF[] pts = c.Body == Body.Golem
                ? new[] { new PointF(-12, -20), new PointF(12, -20), new PointF(13, 12), new PointF(0, 22), new PointF(-13, 12) }
                : new[] { new PointF(0, -22), new PointF(12, -16), new PointF(10, 8), new PointF(0, 23), new PointF(-10, 8), new PointF(-12, -16) };
            g.DrawPolygon(st.HeadOut, pts);
            g.FillPolygon(st.ShieldB, pts);
            g.DrawPolygon(st.Rim, pts);
            if (c.Body == Body.Skeleton)
            {
                g.FillEllipse(st.RimB, -5, -11, 10, 9);
                g.FillRectangle(st.RimB, -3, -4, 6, 4);
                g.FillEllipse(st.Socket, -3.5f, -8.5f, 3, 3);
                g.FillEllipse(st.Socket, 0.5f, -8.5f, 3, 3);
                g.DrawLine(st.Rim, 0, 2, 0, 15);
            }
            else if (c.Body == Body.Golem)
            {
                g.FillEllipse(st.GlowB, -5, -6, 10, 10);
                g.DrawLine(st.Crack, -9, -14, -3, -6);
                g.DrawLine(st.Crack, 9, 9, 3, 3);
            }
            else
            {
                // Blattmuster
                g.DrawLine(st.Rim, 0, -15, 0, 16);
                g.DrawLine(st.Rim, 0, -4, 7, -10);
                g.DrawLine(st.Rim, 0, 4, -7, -2);
            }
            g.Restore(gs);
        }

        static float BodyScale(int body) { return body == Body.Golem ? 1.12f : body == Body.Huntress ? 0.97f : 1f; }

        void DrawFighterPose(Graphics g, PlayerState P, Pose pose, float wx, float wy, int facing, bool grounded, int mode, float alpha, bool record)
        {
            CharDef c = CharDef.All[P.charId];
            Style st = GetStyle(P.charId, mode, alpha);
            Rig r = BuildRig(pose, grounded && Math.Abs(pose.Spin % 360) < 1);
            bool shieldFront = P.state == St.Shield;
            bool weaponGlow = P.state == St.Attack && P.atk >= 0 && (P.charge > 0 || Attacks.Of(P).Heavy);
            int body = c.Body;
            float sc = BodyScale(body);

            if (record) RecordTrail(P, r, pose, wx, wy, facing, sc);

            var gs = g.Save();
            g.TranslateTransform(wx, wy);
            g.ScaleTransform(facing * sc, sc);
            if (pose.Spin != 0) { g.TranslateTransform(0, -50); g.RotateTransform(pose.Spin); g.TranslateTransform(0, 50); }

            if (body == Body.Huntress) DrawCape(g, st, r, P);
            if (body == Body.Skeleton) DrawLoincloth(g, st, r, pose, P, false);
            DrawLimb(g, st, body, r.Pel, r.KB, r.FB, true, true);
            if (!shieldFront)
            {
                DrawLimb(g, st, body, r.Sh, r.EB, r.HB, true, false);
                DrawShield(g, st, r.HB, pose.Lean, false, alpha, c);
            }
            DrawTorso(g, st, body, r, pose, c);
            if (body == Body.Skeleton) DrawShawl(g, st, r, pose, P);
            float glow = 0.6f + 0.4f * (float)Math.Sin(frame * 0.12 + P.slot);
            DrawHead(g, st, body, r.Head, pose.Lean + pose.Head, 1f, mode == 2 ? 0 : glow, P.state == St.Broken);
            DrawLimb(g, st, body, r.Pel, r.KF, r.FF, false, true);
            if (body == Body.Skeleton) DrawLoincloth(g, st, r, pose, P, true);
            DrawWeapon(g, st, r, c, P, pose, weaponGlow && mode == 0, alpha);
            DrawLimb(g, st, body, r.Sh, r.EF, r.HF, false, false);
            if (shieldFront)
            {
                DrawLimb(g, st, body, r.Sh, r.EB, r.HB, true, false);
                DrawShield(g, st, r.HB, pose.Lean, true, alpha, c);
            }
            g.Restore(gs);
        }

        void RecordTrail(PlayerState P, Rig r, Pose pose, float wx, float wy, int facing, float sc)
        {
            int i = P.slot;
            bool rec = false;
            AttackDef d = null;
            if (P.state == St.Attack && P.atk >= 0)
            {
                int ph; float t;
                Anim.AttackPhase(P, out ph, out t);
                d = Attacks.Of(P);
                rec = d.HW > 0 && (ph == 1 || (ph == 2 && P.atkFrame < d.Startup + d.Active + 3));
            }
            if (rec)
            {
                int w = CharDef.All[P.charId].Weapon;
                PointF a, b;
                if (d.FootTrail) { a = r.FF; b = Lp(r.KF, r.FF, 0.4f); }
                else if (w == Weapon.Hammer) { PointF dd = Dir(pose.Scy, 1); a = Add(r.HF, Mul(dd, 74)); b = Add(r.HF, Mul(dd, 30)); }
                else if (w == Weapon.Bow) { PointF dd = Dir(pose.Scy, 1); a = Add(r.HF, Mul(dd, 20)); b = r.HF; }
                else { a = r.Tip; b = Lp(r.Top, r.Inner, 0.5f); }
                a = Mul(a, sc); b = Mul(b, sc);
                trailA[i].Add(ToWorld(a, pose.Spin, facing, wx, wy));
                trailB[i].Add(ToWorld(b, pose.Spin, facing, wx, wy));
                if (trailA[i].Count > 7) { trailA[i].RemoveAt(0); trailB[i].RemoveAt(0); }
            }
            else if (trailA[i].Count > 0) { trailA[i].RemoveAt(0); trailB[i].RemoveAt(0); }
        }

        void DrawTrail(Graphics g, PlayerState P)
        {
            var ta = trailA[P.slot]; var tb = trailB[P.slot];
            if (ta.Count < 2) return;
            CharDef c = CharDef.All[P.charId];
            bool heavy = P.state == St.Attack && P.atk >= 0 && Attacks.Of(P).Heavy;
            for (int k = 1; k < ta.Count; k++)
            {
                float a = k / (float)ta.Count;
                using (var b = new SolidBrush(A(Mixc(c.Glow, Color.White, 0.35f), (heavy ? 0.55f : 0.42f) * a)))
                    g.FillPolygon(b, new[] { ta[k - 1], ta[k], tb[k], tb[k - 1] });
            }
            using (var p = RPen(A(Color.White, 0.75f), heavy ? 4f : 2.6f))
                if (ta.Count >= 2) g.DrawLines(p, ta.ToArray());
        }

        void DrawProjectiles(Graphics g, PlayerState[] ps)
        {
            foreach (PlayerState P in ps)
            {
                if (!P.projOn) continue;
                CharDef c = CharDef.All[P.charId];
                if (P.projKind == 2)
                {
                    float x = P.projX, y = P.projY;
                    float fl = (float)Math.Sin(frame * 0.6) * 4;
                    using (var b = new SolidBrush(A(c.Glow, 0.35f))) g.FillEllipse(b, x - 34, y - 52 - fl, 68, 64);
                    using (var b = new SolidBrush(c.Bone))
                        for (int k = -2; k <= 2; k++)
                        {
                            float h = 34 - Math.Abs(k) * 9 + fl * (k % 2 == 0 ? 1 : -1);
                            g.FillPolygon(b, new[] { new PointF(x + k * 11 - 7, y), new PointF(x + k * 11, y - h), new PointF(x + k * 11 + 7, y) });
                        }
                    using (var p = RPen(c.Glow, 3)) g.DrawArc(p, x - 30, y - 30, 60, 60, 200, 140);
                    if (frame % 3 == 0) Dust(x - P.projDir * 20, y, 2, 20);
                }
                else
                {
                    float len = (float)Math.Sqrt(P.projVx * P.projVx + P.projVy * P.projVy);
                    float dx = P.projVx / Math.Max(0.01f, len), dy = P.projVy / Math.Max(0.01f, len);
                    PointF tip = new PointF(P.projX + dx * 16, P.projY + dy * 16), tail = new PointF(P.projX - dx * 20, P.projY - dy * 20);
                    using (var p = RPen(A(c.Glow, 0.35f), 10)) g.DrawLine(p, tip, new PointF(P.projX - dx * 60, P.projY - dy * 60));
                    using (var p = RPen(c.Outline, 5)) g.DrawLine(p, tip, tail);
                    using (var p = RPen(c.Blade, 3)) g.DrawLine(p, tip, tail);
                    using (var b = new SolidBrush(c.Glow))
                    {
                        g.FillPolygon(b, new[] { new PointF(tip.X + dx * 10, tip.Y + dy * 10), new PointF(tip.X - dy * 6, tip.Y + dx * 6), new PointF(tip.X + dy * 6, tip.Y - dx * 6) });
                        g.FillPolygon(b, new[] { tail, new PointF(tail.X - dx * 8 - dy * 6, tail.Y - dy * 8 + dx * 6), new PointF(tail.X - dx * 4, tail.Y - dy * 4) });
                    }
                    if (frame % 2 == 0)
                        parts.Add(new Particle { Kind = 1, X = P.projX, Y = P.projY, Size = 5, Life = 12, Max = 12, C = c.Glow });
                }
            }
        }

        public void DrawFighter(Graphics g, PlayerState P, bool showHitbox)
        {
            if (P.state == St.KO) return;
            float wx = P.x + OffX[P.slot], wy = P.y + OffY[P.slot];
            CharDef c = CharDef.All[P.charId];
            Pose pose = Anim.Get(P);
            bool grounded = P.onGround && P.state != St.Air;
            // Trefferstopp-Zittern
            if (P.hitlag > 0 && P.hurtFlash > 0) wx += (float)Math.Sin(frame * 2.7) * 3.5f;

            // Geisterbilder für Dash/Dodge/Wirbel
            bool fast = P.state == St.Dash || P.state == St.AirDodge || P.state == St.Dodge || (P.state == St.Hitstun && P.tumble) ||
                        (P.state == St.Attack && (P.atk == Attacks.DAirHeavy || P.atk == Attacks.UAirHeavy || P.atk == Attacks.NHeavy) && P.atkFrame >= Attacks.Of(P).Startup);
            if (fast && frame % 2 == 0) ghosts.Add(new Ghost { X = wx, Y = wy, Facing = P.facing, Char = P.charId, Life = 12, Pose = pose, Ground = grounded });

            // Rankenseil der Bogenschützin
            if (c.Weapon == Weapon.Bow && curPlayers != null)
            {
                PointF hand = new PointF(wx + P.facing * 22, wy - 62);
                PointF? to = null;
                if (P.state == St.Attack && P.atk == Attacks.Grab)
                {
                    AttackDef d = Attacks.Of(P);
                    if (P.atkFrame >= d.Startup - 3 && P.atkFrame < d.Startup + d.Active + 4)
                    {
                        float k = Math.Min(1f, (P.atkFrame - d.Startup + 3) / 5f);
                        to = new PointF(wx + P.facing * (20 + (d.HX + d.HW - 10) * k), wy - 64);
                    }
                }
                if (P.state == St.Grabbing)
                {
                    PlayerState V = curPlayers[1 - P.slot];
                    to = new PointF(V.x + OffX[V.slot], V.y - 50 + OffY[V.slot]);
                }
                if (to.HasValue)
                {
                    PointF t = to.Value;
                    PointF mid = new PointF((hand.X + t.X) / 2, (hand.Y + t.Y) / 2 + 10 + (float)Math.Sin(frame * 0.4) * 4);
                    using (var p = RPen(Color.FromArgb(220, 20, 50, 30), 6)) g.DrawBezier(p, hand, mid, mid, t);
                    using (var p = RPen(Color.FromArgb(255, 90, 190, 90), 3)) g.DrawBezier(p, hand, mid, mid, t);
                    using (var b = new SolidBrush(c.Glow)) g.FillEllipse(b, t.X - 5, t.Y - 5, 10, 10);
                }
            }

            // Respawn-Plattform
            if (P.state == St.Respawn)
            {
                using (var b = new SolidBrush(A(c.Glow, 0.35f))) g.FillEllipse(b, wx - 55, wy - 6, 110, 16);
                using (var p = RPen(A(Color.White, 0.6f), 2)) g.DrawEllipse(p, wx - 48, wy - 4, 96, 11);
            }

            // Schatten am Boden
            if (P.y <= 2 && P.y > -400 && P.x > Arena.Main.L && P.x < Arena.Main.R)
            {
                float h = -P.y;
                float sw = Math.Max(10, 46 - h * 0.08f);
                using (var b = new SolidBrush(Color.FromArgb((int)Math.Max(0, 70 - h * 0.15f), 0, 0, 0))) g.FillEllipse(b, wx - sw / 2, -4, sw, 8);
            }

            // Aufladen
            if (P.state == St.Attack && P.charge > 0)
            {
                float t = P.charge / (float)Math.Max(1, Attacks.Of(P).ChargeMax);
                float rr = 50 + t * 40 + (float)Math.Sin(frame * 0.5) * 6;
                using (var b = new SolidBrush(A(c.Glow, 0.12f + 0.25f * t))) g.FillEllipse(b, wx - rr, wy - 50 - rr, rr * 2, rr * 2);
                if (P.chargeFull && frame % 3 == 0)
                    parts.Add(new Particle { Kind = 0, X = wx + (float)(rng.NextDouble() - 0.5) * 80, Y = wy - 50 + (float)(rng.NextDouble() - 0.5) * 80, VY = -3, Life = 14, Max = 14, Size = 2, C = c.Glow });
            }

            // Parade-Aura
            if (P.parryFlash > 0)
            {
                float t = P.parryFlash / 24f;
                using (var b = new SolidBrush(A(Color.FromArgb(255, 250, 220), 0.5f * t))) g.FillEllipse(b, wx - 60, wy - 110, 120, 120);
            }

            float alpha = 1f;
            if (P.invuln > 0 && (P.state == St.Dodge || P.state == St.AirDodge)) alpha = 0.55f;
            else if (P.invuln > 0 && P.state != St.Respawn && frame % 8 < 3) alpha = 0.6f;
            int mode = P.hurtFlash > 4 ? 1 : 0;

            // Umrissleuchten zur besseren Erkennbarkeit
            DrawFighterPose(g, P, pose, wx, wy, P.facing, grounded, mode, alpha, true);

            // Schild-Energiefeld
            if (P.state == St.Shield)
            {
                bool parry = P.parryOk && P.stateTime <= Tuning.ParryWindow;
                float hp = P.shieldHp / Tuning.ShieldMax;
                Color sc = parry ? Color.FromArgb(255, 255, 240) : Mixc(Color.FromArgb(255, 70, 70), c.Glow, hp);
                float cx = wx + P.facing * 22;
                using (var b = new SolidBrush(A(sc, parry ? 0.38f : 0.16f + 0.12f * hp))) g.FillEllipse(b, cx - 34, wy - 100, 68, 100);
                using (var p = RPen(A(sc, parry ? 0.95f : 0.65f), parry ? 4 : 2.5f))
                    g.DrawArc(p, cx - 34, wy - 100, 68, 100, P.facing == 1 ? -70 : 110, 140);
            }

            // Betäubt (Parade erhalten / Schild gebrochen)
            if (P.state == St.Parried || P.state == St.Broken)
            {
                for (int k = 0; k < 3; k++)
                {
                    double a = frame * 0.15 + k * 2.094;
                    float sx = wx + (float)Math.Cos(a) * 22, sy = wy - 118 + (float)Math.Sin(a) * 6;
                    DrawStar(g, sx, sy, 6, P.state == St.Parried ? Color.FromArgb(255, 240, 150) : Color.FromArgb(255, 120, 120));
                }
            }

            if (showHitbox)
            {
                Box h = P.Hurt;
                using (var p = new Pen(Color.FromArgb(160, 80, 255, 80), 1.5f)) g.DrawRectangle(p, h.L, h.T, h.R - h.L, h.B - h.T);
                Box hb; AttackDef d;
                if (Sim.HitboxActive(P, out hb, out d))
                    using (var b = new SolidBrush(Color.FromArgb(90, 255, 40, 40))) g.FillRectangle(b, hb.L, hb.T, hb.R - hb.L, hb.B - hb.T);
            }
        }

        void DrawStar(Graphics g, float x, float y, float r, Color c)
        {
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                float rr = i % 2 == 0 ? r : r * 0.45f;
                pts[i] = new PointF(x + (float)Math.Cos(a) * rr, y + (float)Math.Sin(a) * rr);
            }
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        void DrawGhosts(Graphics g, PlayerState[] ps)
        {
            if (ghosts.Count == 0) return;
            // Geisterbilder sind blass und einfarbig, Kantenglättung fällt dort nicht auf, kostet aber viel Zeit
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost gh = ghosts[i];
                PlayerState dummy = new PlayerState { charId = gh.Char, state = St.Air, slot = 0 };
                float a = gh.Life / 12f * 0.35f;
                DrawFighterPose(g, dummy, gh.Pose, gh.X, gh.Y, gh.Facing, gh.Ground, 2, a, false);
            }
            g.SmoothingMode = sm;
        }

        // ================================================================ Arena

        void BuildArena()
        {
            island = new GraphicsPath();
            island.AddPolygon(new[]
            {
                new PointF(-640, 0), new PointF(640, 0), new PointF(640, 300), new PointF(595, 340), new PointF(475, 395),
                new PointF(325, 450), new PointF(170, 515), new PointF(50, 560), new PointF(-60, 550), new PointF(-210, 495),
                new PointF(-390, 425), new PointF(-545, 360), new PointF(-640, 300)
            });
            islandBg = (GraphicsPath)island.Clone();

            midLayer = new GraphicsPath();
            var r = new Random(11);
            float x = -2200;
            while (x < 2200)
            {
                float w = 120 + (float)r.NextDouble() * 200;
                float h = 200 + (float)r.NextDouble() * 380;
                int kind = r.Next(3);
                if (kind == 0)
                {
                    // Turm mit Spitze
                    midLayer.AddPolygon(new[] { new PointF(x, 700), new PointF(x, 300 - h), new PointF(x + w * 0.5f, 300 - h - 120), new PointF(x + w, 300 - h), new PointF(x + w, 700) });
                }
                else if (kind == 1)
                {
                    // Ruinenmauer mit Bögen
                    midLayer.AddRectangle(new RectangleF(x, 300 - h * 0.5f, w * 1.6f, 400 + h * 0.5f));
                    for (int k = 0; k < 4; k++) midLayer.AddRectangle(new RectangleF(x + k * w * 0.42f, 300 - h * 0.5f - 30, w * 0.2f, 32));
                }
                else
                {
                    // toter Baum
                    midLayer.AddRectangle(new RectangleF(x + w * 0.45f, 300 - h * 0.6f, 18, h * 0.6f + 400));
                    midLayer.AddPolygon(new[] { new PointF(x + w * 0.5f, 300 - h * 0.45f), new PointF(x + w * 0.05f, 300 - h * 0.85f), new PointF(x + w * 0.12f, 300 - h * 0.86f), new PointF(x + w * 0.55f, 300 - h * 0.55f) });
                    midLayer.AddPolygon(new[] { new PointF(x + w * 0.5f, 300 - h * 0.35f), new PointF(x + w * 1.0f, 300 - h * 0.75f), new PointF(x + w * 1.05f, 300 - h * 0.7f), new PointF(x + w * 0.56f, 300 - h * 0.28f) });
                }
                x += w * (kind == 1 ? 1.6f : 1f) + 40 + (float)r.NextDouble() * 140;
            }
        }

        void BuildBackground(int w, int h)
        {
            if (bg != null) bg.Dispose();
            bgW = w; bgH = h; bgStage = Stage;
            bg = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bg))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Stage != StageDef.Graveyard) { PaintStageBackground(g, w, h); return; }
                using (var b = new LinearGradientBrush(new Point(0, 0), new Point(0, h), Color.FromArgb(10, 6, 26), Color.FromArgb(58, 26, 74)))
                    g.FillRectangle(b, 0, 0, w, h);
                var r = new Random(3);
                for (int i = 0; i < 260; i++)
                {
                    float sx = (float)r.NextDouble() * w, sy = (float)r.NextDouble() * h * 0.7f;
                    float ss = (float)r.NextDouble() * 2.2f * w / 1280f + 0.5f;
                    using (var b = new SolidBrush(Color.FromArgb(r.Next(80, 230), 235, 225, 255))) g.FillEllipse(b, sx, sy, ss, ss);
                }
                // Mond
                float mx = w * 0.74f, my = h * 0.22f, mr = h * 0.1f;
                for (int k = 6; k >= 1; k--)
                    using (var b = new SolidBrush(Color.FromArgb(10, 230, 200, 255))) g.FillEllipse(b, mx - mr * (1 + k * 0.35f), my - mr * (1 + k * 0.35f), mr * 2 * (1 + k * 0.35f), mr * 2 * (1 + k * 0.35f));
                using (var b = new SolidBrush(Color.FromArgb(240, 232, 250))) g.FillEllipse(b, mx - mr, my - mr, mr * 2, mr * 2);
                using (var b = new SolidBrush(Color.FromArgb(40, 120, 90, 160)))
                {
                    g.FillEllipse(b, mx - mr * 0.5f, my - mr * 0.3f, mr * 0.45f, mr * 0.4f);
                    g.FillEllipse(b, mx + mr * 0.15f, my + mr * 0.2f, mr * 0.35f, mr * 0.3f);
                }
                // ferne Berge
                for (int layer = 0; layer < 2; layer++)
                {
                    var pts = new List<PointF> { new PointF(0, h) };
                    float baseY = h * (0.62f + layer * 0.1f);
                    for (float x = 0; x <= w + 40; x += w / 18f)
                        pts.Add(new PointF(x, baseY - (float)(r.NextDouble() * h * (0.16f - layer * 0.05f))));
                    pts.Add(new PointF(w, h));
                    using (var b = new SolidBrush(layer == 0 ? Color.FromArgb(34, 20, 54) : Color.FromArgb(26, 14, 42))) g.FillPolygon(b, pts.ToArray());
                }
                // Nebel
                using (var b = new LinearGradientBrush(new Point(0, (int)(h * 0.6f)), new Point(0, h), Color.FromArgb(0, 120, 60, 170), Color.FromArgb(90, 120, 60, 170)))
                    g.FillRectangle(b, 0, h * 0.6f, w, h * 0.4f);
            }
        }

        public void DrawBackground(Graphics g, int pixW, int pixH, int ox, int oy)
        {
            SyncStage();
            int bw = (int)(pixW * 1.12f), bh = (int)(pixH * 1.12f);
            if (bg == null || bgW != bw || bgH != bh || bgStage != Stage) BuildBackground(bw, bh);
            float px = Math.Max(-1, Math.Min(1, CamX / 1400f)), py = Math.Max(-1, Math.Min(1, (CamY + 150) / 900f));
            int dx = (int)(-(bw - pixW) / 2f - px * (bw - pixW) / 2f);
            int dy = (int)(-(bh - pixH) / 2f - py * (bh - pixH) / 2f);
            var old = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(bg, ox + dx, oy + dy);
            g.CompositingMode = old;
        }

        void DrawMidLayer(Graphics g)
        {
            if (Stage != StageDef.Graveyard) { DrawStageMid(g); return; }
            var gs = g.Save();
            g.TranslateTransform(VW / 2f, VH / 2f);
            float z = 0.25f + Zoom * 0.5f;
            g.ScaleTransform(z, z);
            g.TranslateTransform(-CamX * 0.35f, -CamY * 0.35f + 80);
            using (var b = new SolidBrush(Color.FromArgb(200, 34, 20, 52))) g.FillPath(b, midLayer);
            // Fensterlichter
            var r = new Random(5);
            for (int i = 0; i < 26; i++)
            {
                float x = -2100 + i * 165 + (float)r.NextDouble() * 60, y = 20 + (float)r.NextDouble() * 220;
                float fl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.05 + i);
                using (var b = new SolidBrush(Color.FromArgb((int)(60 + 50 * fl), 200, 140, 255))) g.FillRectangle(b, x, y, 9, 16);
            }
            g.Restore(gs);
        }

        // unbewegliche Teile des Mondfriedhofs, gecacht über LayerCache (läuft im Hintergrund)
        void DrawGraveyardStatic(Graphics g)
        {
            Box M = Arena.Main;
            // Leuchten unter der Insel
            using (var b = new SolidBrush(Color.FromArgb(28, 170, 90, 255))) g.FillEllipse(b, -520, 380, 1040, 260);
            using (var ib = new LinearGradientBrush(new PointF(0, -10), new PointF(0, 570), Color.FromArgb(78, 64, 104), Color.FromArgb(18, 12, 32))) g.FillPath(ib, islandBg);
            var clip = g.Save();
            g.SetClip(islandBg, CombineMode.Intersect);
            using (var p = new Pen(Color.FromArgb(70, 10, 5, 20), 3))
            {
                for (int row = 0; row < 9; row++)
                {
                    float y = 30 + row * 38;
                    g.DrawLine(p, -640, y, 640, y);
                    for (float x = -640 + (row % 2) * 45; x < 640; x += 90) g.DrawLine(p, x, y - 38, x, y);
                }
            }
            g.Restore(clip);
            // Oberkante
            using (var b = new LinearGradientBrush(new PointF(0, -7), new PointF(0, 14), Color.FromArgb(150, 136, 182), Color.FromArgb(82, 68, 110)))
                g.FillRectangle(b, M.L - 10, -7, M.R - M.L + 20, 20);
            using (var p = new Pen(Color.FromArgb(215, 205, 240), 2.5f)) g.DrawLine(p, M.L - 10, -7, M.R + 10, -7);
            // Wandkanten (zum Festhalten)
            using (var p = new Pen(Color.FromArgb(120, 200, 170, 255), 3))
            {
                g.DrawLine(p, M.L, 12, M.L, M.B);
                g.DrawLine(p, M.R, 12, M.R, M.B);
            }
            // Schwebeplattformen
            foreach (var sp in Arena.Soft)
            {
                using (var b = new SolidBrush(Color.FromArgb(48, 30, 70)))
                    g.FillPolygon(b, new[] { new PointF(sp.L + 10, sp.Y + 14), new PointF(sp.R - 10, sp.Y + 14), new PointF((sp.L + sp.R) / 2 + 20, sp.Y + 54), new PointF((sp.L + sp.R) / 2 - 25, sp.Y + 48) });
                using (var b = new LinearGradientBrush(new PointF(0, sp.Y - 4), new PointF(0, sp.Y + 16), Color.FromArgb(160, 146, 196), Color.FromArgb(76, 62, 104)))
                    g.FillRectangle(b, sp.L, sp.Y - 4, sp.R - sp.L, 18);
                using (var p = new Pen(Color.FromArgb(225, 215, 250), 2)) g.DrawLine(p, sp.L, sp.Y - 4, sp.R, sp.Y - 4);
            }
        }

        void DrawArena(Graphics g)
        {
            if (Stage != StageDef.Graveyard) { DrawStageArena(g); return; }
            graveCache.Draw(g);
            var clip = g.Save();
            g.SetClip(island, CombineMode.Intersect);
            // Kristalle
            for (int i = 0; i < 9; i++)
            {
                float x = -500 + i * 125, y = 340 + (i % 3) * 40 - Math.Abs(x) * 0.12f;
                float fl = 0.6f + 0.4f * (float)Math.Sin(frame * 0.04 + i * 1.3);
                using (var b = new SolidBrush(Color.FromArgb((int)(110 * fl), 190, 120, 255)))
                    g.FillPolygon(b, new[] { new PointF(x, y), new PointF(x + 10, y + 28), new PointF(x, y + 50), new PointF(x - 10, y + 28) });
            }
            g.Restore(clip);
            // Runen
            for (int i = 0; i < 8; i++)
            {
                float x = -560 + i * 160;
                float fl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.05 + i);
                using (var b = new SolidBrush(Color.FromArgb((int)(40 + 70 * fl), 200, 150, 255))) g.FillEllipse(b, x - 14, 96, 28, 28);
                using (var p = new Pen(Color.FromArgb((int)(120 + 100 * fl), 230, 200, 255), 2)) { g.DrawEllipse(p, x - 8, 102, 16, 16); g.DrawLine(p, x, 98, x, 122); }
            }
            // Leuchten unter den Schwebeplattformen
            foreach (var sp in Arena.Soft)
            {
                float fl = 0.5f + 0.5f * (float)Math.Sin(frame * 0.06 + sp.L);
                using (var b = new SolidBrush(Color.FromArgb((int)(60 + 60 * fl), 200, 140, 255))) g.FillEllipse(b, (sp.L + sp.R) / 2 - 10, sp.Y + 22, 20, 20);
            }
        }

        void DrawDecor(Graphics g)
        {
            if (Stage != StageDef.Graveyard) { DrawStageDecor(g); return; }
            // Grabsteine und Kerzen hinter den Kämpfern
            float[] gx = { -560, -470, 430, 540 };
            for (int i = 0; i < gx.Length; i++)
            {
                float x = gx[i], h = i % 2 == 0 ? 54 : 40;
                using (var b = new SolidBrush(Color.FromArgb(64, 56, 84)))
                {
                    g.FillRectangle(b, x - 16, -h + 12, 32, h - 12);
                    g.FillEllipse(b, x - 16, -h, 32, 26);
                }
                using (var p = new Pen(Color.FromArgb(110, 30, 20, 45), 2)) { g.DrawLine(p, x, -h + 8, x, -h + 30); g.DrawLine(p, x - 8, -h + 15, x + 8, -h + 15); }
            }
            float[] cx = { -515, 485, -120, 120 };
            for (int i = 0; i < cx.Length; i++)
            {
                float x = cx[i];
                using (var b = new SolidBrush(Color.FromArgb(220, 210, 190))) g.FillRectangle(b, x - 3, -16, 6, 16);
                float fl = (float)Math.Sin(frame * 0.3 + i * 2) * 1.5f;
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 190, 120))) g.FillEllipse(b, x - 12, -36 + fl, 24, 24);
                using (var b = new SolidBrush(Color.FromArgb(255, 230, 150))) g.FillEllipse(b, x - 3, -26 + fl, 6, 10);
            }
        }

        // ================================================================ Effekte

        Particle NewMote(bool anywhere)
        {
            if (Stage != StageDef.Graveyard) return NewStageMote(anywhere);
            return new Particle
            {
                Kind = 1,
                X = (float)(rng.NextDouble() * 3000 - 1500),
                Y = anywhere ? (float)(rng.NextDouble() * 1400 - 900) : 520,
                VX = (float)(rng.NextDouble() - 0.5) * 0.4f,
                VY = -0.3f - (float)rng.NextDouble() * 0.6f,
                Life = 600, Max = 600,
                Size = 2 + (float)rng.NextDouble() * 3,
                C = rng.Next(2) == 0 ? Color.FromArgb(200, 140, 255) : Color.FromArgb(120, 200, 255)
            };
        }

        void Sparks(float x, float y, int n, Color c, float speed, float size)
        {
            for (int i = 0; i < n; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                float s = speed * (0.4f + (float)rng.NextDouble());
                parts.Add(new Particle { Kind = 0, X = x, Y = y, VX = (float)Math.Cos(a) * s, VY = (float)Math.Sin(a) * s, Life = 14 + rng.Next(10), Max = 24, Size = size, C = c, Drag = 0.88f, Grav = 0.15f });
            }
        }

        void Ring(float x, float y, float size, Color c, int life)
        {
            parts.Add(new Particle { Kind = 2, X = x, Y = y, Size = size, Life = life, Max = life, C = c });
        }

        void Dust(float x, float y, int n, float spread)
        {
            for (int i = 0; i < n; i++)
                parts.Add(new Particle { Kind = 6, X = x + (float)(rng.NextDouble() - 0.5) * spread, Y = y - 4, VX = (float)(rng.NextDouble() - 0.5) * 4, VY = -(float)rng.NextDouble() * 1.5f, Life = 22, Max = 22, Size = 8 + (float)rng.NextDouble() * 8, C = Color.FromArgb(150, 140, 160), Drag = 0.9f });
        }

        void Text(float x, float y, string t, Color c, int life)
        {
            parts.Add(new Particle { Kind = 4, X = x, Y = y, VY = -1.2f, Life = life, Max = life, C = c, Text = t, Drag = 0.95f });
        }

        void AddShake(float s) { if (Settings.ScreenShake) shake = Math.Max(shake, s); }

        public void ClearFx()
        {
            parts.Clear(); ghosts.Clear();
            for (int i = 0; i < 2; i++) { trailA[i].Clear(); trailB[i].Clear(); OffX[i] = OffY[i] = 0; }
            Flash = 0; shake = 0; goTimer = 0;
        }

        public void OnEvent(GameEvent e, GameState s)
        {
            PlayerState P = s.p[Math.Min(1, (int)e.Player)];
            CharDef c = CharDef.All[P.charId];
            switch (e.Type)
            {
                case Ev.Hit:
                    {
                        if (e.V >= 2000)
                        {
                            Sparks(e.X, e.Y, 10, Color.FromArgb(255, 200, 120), 6, 2.5f);
                            Text(P.x, P.y - 140, "RÜSTUNG!", Color.FromArgb(255, 190, 110), 34);
                            Audio.Play(Sfx.Clank, 0.7f, 0.6f);
                            AddShake(3);
                            break;
                        }
                        bool heavy = e.V >= 1000;
                        float kb = heavy ? e.V - 1000 : e.V;
                        Sparks(e.X, e.Y, 8 + (int)kb, Color.FromArgb(255, 240, 200), 6 + kb * 0.4f, 2.5f);
                        Sparks(e.X, e.Y, 5, CharDef.All[s.p[1 - P.slot].charId].Glow, 5, 3);
                        Ring(e.X, e.Y, 30 + kb * 3, Color.White, 12);
                        parts.Add(new Particle { Kind = 1, X = e.X, Y = e.Y, Size = 40 + kb * 3, Life = 8, Max = 8, C = Color.FromArgb(255, 255, 230) });
                        AddShake(Math.Min(18, kb * (heavy ? 0.9f : 0.45f)));
                        if (heavy && kb > 14) { Flash = 0.25f; flashColor = Color.White; }
                        Audio.Play(heavy ? Sfx.HitHeavy : Sfx.Hit, heavy ? 1f : 0.8f, heavy ? 1f : 1.1f);
                        break;
                    }
                case Ev.Block:
                    Sparks(e.X, e.Y, 10, Color.FromArgb(190, 230, 255), 6, 2.2f);
                    Ring(e.X, e.Y, 34, c.Glow, 10);
                    Text(P.x, P.y - 130, "BLOCK", Color.FromArgb(200, 230, 255), 34);
                    AddShake(3);
                    Audio.Play(Sfx.Block);
                    break;
                case Ev.Parry:
                    Sparks(e.X, e.Y, 26, Color.FromArgb(255, 235, 140), 10, 3f);
                    Sparks(e.X, e.Y, 12, Color.White, 14, 2f);
                    Ring(e.X, e.Y, 120, Color.FromArgb(255, 250, 210), 18);
                    Ring(e.X, e.Y, 70, c.Glow, 14);
                    Text(P.x, P.y - 140, "PARIERT!", Color.FromArgb(255, 240, 150), 60);
                    Flash = 0.4f; flashColor = Color.FromArgb(255, 250, 225);
                    AddShake(8);
                    Audio.Play(Sfx.Parry, 1.1f, 1f);
                    break;
                case Ev.ShieldBreak:
                    for (int i = 0; i < 16; i++)
                        parts.Add(new Particle { Kind = 7, X = e.X, Y = e.Y, VX = (float)(rng.NextDouble() - 0.5) * 14, VY = -(float)rng.NextDouble() * 10, Life = 40, Max = 40, Size = 7, Rot = (float)rng.NextDouble() * 360, VR = (float)(rng.NextDouble() - 0.5) * 30, Grav = 0.45f, C = c.Glow });
                    Text(P.x, P.y - 140, "SCHILD GEBROCHEN!", Color.FromArgb(255, 130, 130), 70);
                    AddShake(10);
                    Audio.Play(Sfx.Break);
                    break;
                case Ev.KO:
                    {
                        float ex = e.X, ey = e.Y;
                        Ring(ex, ey, 260, Color.White, 26);
                        Ring(ex, ey, 160, c.Glow, 22);
                        Sparks(ex, ey, 40, c.Glow, 18, 4);
                        double toCenter = Math.Atan2(-150 - ey, 0 - ex);
                        for (int i = 0; i < 10; i++)
                        {
                            double a = toCenter + (rng.NextDouble() - 0.5) * 1.6;
                            float sp = 8 + (float)rng.NextDouble() * 12;
                            parts.Add(new Particle { Kind = i == 0 ? 5 : 3, X = ex, Y = ey, VX = (float)Math.Cos(a) * sp, VY = (float)Math.Sin(a) * sp, Life = 70, Max = 70, Size = i == 0 ? 1.6f : 12 + (float)rng.NextDouble() * 10, Rot = (float)rng.NextDouble() * 360, VR = (float)(rng.NextDouble() - 0.5) * 30, Grav = 0.35f, Drag = 0.97f, C = c.Bone, Text = P.charId.ToString() });
                        }
                        Text(Math.Max(-900, Math.Min(900, ex)), Math.Max(-700, Math.Min(300, ey)), "K.O.!", Color.White, 80);
                        Flash = 0.35f; flashColor = c.Glow;
                        AddShake(22);
                        Audio.Play(Sfx.KO, 1.2f);
                        trailA[P.slot].Clear(); trailB[P.slot].Clear();
                        break;
                    }
                case Ev.Jump:
                    Dust(e.X, e.Y, 5, 30);
                    Audio.Play(Sfx.Jump, 0.6f);
                    break;
                case Ev.AirJump:
                    parts.Add(new Particle { Kind = 8, X = e.X, Y = e.Y + 4, Size = 50, Life = 16, Max = 16, C = c.Glow });
                    Audio.Play(Sfx.AirJump, 0.6f);
                    break;
                case Ev.Land:
                    Dust(e.X, e.Y, e.V > 12 ? 9 : 5, 40);
                    Audio.Play(Sfx.Land, Math.Min(1f, e.V / 14f));
                    break;
                case Ev.Dash:
                    Dust(e.X - e.V * 10, e.Y, 6, 20);
                    Audio.Play(Sfx.Dash, 0.7f);
                    break;
                case Ev.Dodge:
                    Ring(e.X, e.Y, 60, A(c.Glow, 0.7f), 12);
                    Audio.Play(Sfx.Dodge, 0.6f);
                    break;
                case Ev.WallJump:
                    Dust(e.X, e.Y, 6, 10);
                    Audio.Play(Sfx.Jump, 0.7f, 1.2f);
                    break;
                case Ev.Swing:
                    Audio.Play(e.V > 0 ? Sfx.SwingHeavy : Sfx.Swing, 0.7f);
                    break;
                case Ev.Clank:
                    Sparks(e.X, e.Y, 22, Color.FromArgb(255, 230, 120), 11, 2.5f);
                    Ring(e.X, e.Y, 70, Color.White, 12);
                    Text(e.X, e.Y - 40, "KLIRR!", Color.FromArgb(255, 230, 150), 40);
                    AddShake(6);
                    Audio.Play(Sfx.Clank);
                    break;
                case Ev.Countdown:
                    Audio.Play(Sfx.Beep, 0.8f);
                    break;
                case Ev.Go:
                    goTimer = 60;
                    Audio.Play(Sfx.Go, 0.9f);
                    break;
                case Ev.ChargeFull:
                    Ring(e.X, e.Y, 80, c.Glow, 14);
                    Audio.Play(Sfx.Charge, 0.7f);
                    break;
                case Ev.Shockwave:
                    parts.Add(new Particle { Kind = 8, X = e.X, Y = e.Y, Size = e.V > 1 ? 240 : 300, Life = 20, Max = 20, C = c.Glow });
                    Dust(e.X - 60, e.Y, 6, 60); Dust(e.X + 60, e.Y, 6, 60);
                    AddShake(e.V > 1 ? 9 : 6);
                    Audio.Play(Sfx.Shock, 0.8f);
                    break;
                case Ev.Bounce:
                    Dust(e.X, e.Y, 6, 30);
                    Audio.Play(Sfx.Land, 0.8f, 0.8f);
                    break;
                case Ev.Respawn:
                    Ring(e.X, e.Y - 50, 140, c.Glow, 24);
                    Audio.Play(Sfx.Charge, 0.5f, 0.7f);
                    break;
                case Ev.Grab:
                    Sparks(e.X, e.Y, 10, Color.FromArgb(230, 230, 255), 5, 2f);
                    Ring(e.X, e.Y, 50, CharDef.All[s.p[1 - P.slot].charId].Glow, 12);
                    Text(e.X, e.Y - 90, "GEPACKT!", Color.FromArgb(220, 220, 255), 40);
                    AddShake(4);
                    Audio.Play(Sfx.Clank, 0.7f, 0.7f);
                    break;
                case Ev.Throw:
                    Audio.Play(Sfx.SwingHeavy, 0.8f, 1.2f);
                    break;
                case Ev.Shoot:
                    if (e.V == 2) { Dust(e.X, e.Y, 8, 40); AddShake(5); Audio.Play(Sfx.Shock, 0.8f, 1.1f); }
                    else { Ring(e.X, e.Y, 30, c.Glow, 8); Audio.Play(Sfx.Dash, 0.6f, 1.8f); Audio.Play(Sfx.Charge, 0.25f, 0.8f); }
                    break;
                case Ev.ProjBreak:
                    Sparks(e.X, e.Y, 6, c.Glow, 4, 2f);
                    break;
                case Ev.Perfect:
                    {
                        Ring(e.X, e.Y + 60, 90, Color.FromArgb(255, 230, 120), 14);
                        Text(e.X, e.Y, e.V > 1 ? "PERFEKT x" + (int)e.V : "PERFEKT!", Color.FromArgb(255, 225, 90), 40);
                        Audio.Play(Sfx.Charge, 0.6f, 1f + Math.Min(1f, e.V * 0.06f));
                        break;
                    }
                case Ev.GameOver:
                    Flash = 0.3f; flashColor = Color.White;
                    break;
            }
        }

        public void Tick()
        {
            frame++;
            if (goTimer > 0) goTimer--;
            if (Flash > 0) Flash = Math.Max(0, Flash - 0.025f);
            shake *= 0.85f;
            if (shake < 0.3f) shake = 0;
            shakeX = (float)(rng.NextDouble() - 0.5) * shake * 2;
            shakeY = (float)(rng.NextDouble() - 0.5) * shake * 2;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle p = parts[i];
                p.X += p.VX; p.Y += p.VY;
                p.VX *= p.Drag; p.VY *= p.Drag;
                p.VY += p.Grav;
                p.Rot += p.VR;
                p.Life--;
                if (p.Life <= 0) parts.RemoveAt(i);
            }
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                Particle p = motes[i];
                p.X += p.VX + (float)Math.Sin(frame * 0.01 + i) * 0.2f; p.Y += p.VY;
                p.Rot += p.VR;
                p.Life--;
                if (p.Life <= 0 || p.Y < -1100 || p.Y > 720) motes[i] = NewMote(false);
            }
            for (int i = ghosts.Count - 1; i >= 0; i--) { ghosts[i].Life--; if (ghosts[i].Life <= 0) ghosts.RemoveAt(i); }
            for (int i = 0; i < 2; i++)
            {
                OffX[i] *= 0.75f; OffY[i] *= 0.75f;
                if (Math.Abs(OffX[i]) < 0.05f) OffX[i] = 0;
                if (Math.Abs(OffY[i]) < 0.05f) OffY[i] = 0;
            }
        }

        void DrawParticles(Graphics g, bool front)
        {
            foreach (Particle p in parts)
            {
                float t = p.Life / p.Max;
                switch (p.Kind)
                {
                    case 0:
                        using (var pen = RPen(A(p.C, t), p.Size)) g.DrawLine(pen, p.X, p.Y, p.X - p.VX * 1.8f, p.Y - p.VY * 1.8f);
                        break;
                    case 1:
                        {
                            float s = p.Size * (0.5f + 0.5f * t);
                            using (var b = new SolidBrush(A(p.C, t * 0.6f))) g.FillEllipse(b, p.X - s, p.Y - s, s * 2, s * 2);
                            break;
                        }
                    case 2:
                        {
                            float s = p.Size * (1.15f - t);
                            using (var pen = new Pen(A(p.C, t), 2 + 6 * t)) g.DrawEllipse(pen, p.X - s, p.Y - s, s * 2, s * 2);
                            break;
                        }
                    case 3:
                        {
                            var gs = g.Save();
                            g.TranslateTransform(p.X, p.Y);
                            g.RotateTransform(p.Rot);
                            using (var pen = RPen(Color.FromArgb((int)(255 * Math.Min(1, t * 2)), 20, 10, 30), 7)) g.DrawLine(pen, -p.Size / 2, 0, p.Size / 2, 0);
                            using (var pen = RPen(A(p.C, Math.Min(1, t * 2)), 4)) g.DrawLine(pen, -p.Size / 2, 0, p.Size / 2, 0);
                            g.Restore(gs);
                            break;
                        }
                    case 4:
                        {
                            float a = Math.Min(1f, t * 3);
                            var sz = g.MeasureString(p.Text, FMed);
                            using (var b = new SolidBrush(Color.FromArgb((int)(200 * a), 0, 0, 0))) g.DrawString(p.Text, FMed, b, p.X - sz.Width / 2 + 2, p.Y + 2);
                            using (var b = new SolidBrush(A(p.C, a))) g.DrawString(p.Text, FMed, b, p.X - sz.Width / 2, p.Y);
                            break;
                        }
                    case 5:
                        {
                            int ch = 0; int.TryParse(p.Text, out ch);
                            Style st = GetStyle(ch, 0, Math.Min(1, t * 2));
                            DrawHead(g, st, CharDef.All[ch].Body, new PointF(p.X, p.Y), p.Rot, p.Size, 0.5f, false);
                            break;
                        }
                    case 6:
                        {
                            float s = p.Size * (1.6f - t * 0.6f);
                            using (var b = new SolidBrush(A(p.C, t * 0.45f))) g.FillEllipse(b, p.X - s, p.Y - s * 0.6f, s * 2, s * 1.2f);
                            break;
                        }
                    case 7:
                        {
                            var gs = g.Save();
                            g.TranslateTransform(p.X, p.Y);
                            g.RotateTransform(p.Rot);
                            using (var b = new SolidBrush(A(p.C, t))) g.FillPolygon(b, new[] { new PointF(-p.Size, 0), new PointF(0, -p.Size * 0.6f), new PointF(p.Size * 0.8f, p.Size * 0.5f) });
                            g.Restore(gs);
                            break;
                        }
                    case 8:
                        {
                            float s = p.Size * (1.1f - t);
                            using (var pen = new Pen(A(p.C, t), 2 + 5 * t)) g.DrawEllipse(pen, p.X - s, p.Y - s * 0.18f, s * 2, s * 0.36f);
                            break;
                        }
                }
            }
        }

        // ================================================================ Kamera

        public void SnapCamera(GameState s) { UpdateCamera(s, 1f); }

        public void UpdateCamera(GameState s, float lerp = 0.1f)
        {
            float minX = -720, maxX = 720, minY = -470, maxY = 170;
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i];
                if (P.state == St.KO) continue;
                float px = Math.Max(Arena.BlastL + 200, Math.Min(Arena.BlastR - 200, P.x));
                float py = Math.Max(Arena.BlastT + 200, Math.Min(Arena.BlastB - 150, P.y));
                minX = Math.Min(minX, px - 260); maxX = Math.Max(maxX, px + 260);
                minY = Math.Min(minY, py - 230); maxY = Math.Max(maxY, py + 120);
            }
            float tx = (minX + maxX) / 2, ty = (minY + maxY) / 2;
            float tz = Math.Min(VW / (maxX - minX), VH / (maxY - minY));
            tz = Math.Max(0.42f, Math.Min(1.05f, tz));
            ty = Math.Min(ty, 120);
            CamX += (tx - CamX) * lerp;
            CamY += (ty - CamY) * lerp;
            Zoom += (tz - Zoom) * lerp * 0.8f;
        }

        PointF W2S(float x, float y) { return new PointF((x - CamX) * Zoom + VW / 2f, (y - CamY) * Zoom + VH / 2f); }

        // ================================================================ Gesamtszene

        PlayerState[] curPlayers;

        public void DrawWorld(Graphics g, GameState s, bool showHitboxes)
        {
            curPlayers = s.p;
            Stage = s.stage;
            SyncStage();
            DrawMidLayer(g);
            var gs = g.Save();
            g.TranslateTransform(VW / 2f, VH / 2f);
            // Zoom auf den Maßstab des Bühnen-Bitmaps einrasten lassen, damit es unskaliert (schnell) kopiert werden kann
            float dev;
            using (Matrix m = g.Transform) dev = m.Elements[0];
            float zoom = StaticCache.Snap(dev * Zoom) / dev;
            g.ScaleTransform(zoom, zoom);
            g.TranslateTransform(-CamX + shakeX, -CamY + shakeY);

            if (Stage != StageDef.Graveyard) DrawStageMotes(g, false);
            else
                foreach (var m in motes)
                    using (var b = new SolidBrush(A(m.C, 0.35f + 0.25f * (float)Math.Sin(frame * 0.05 + m.X)))) g.FillEllipse(b, m.X, m.Y, m.Size, m.Size);

            DrawArena(g);
            DrawDecor(g);
            DrawGhosts(g, s.p);
            for (int i = 0; i < 2; i++) DrawTrail(g, s.p[i]);
            // der zuletzt angreifende Spieler wird oben gezeichnet
            int first = s.p[0].state == St.Attack && s.p[1].state != St.Attack ? 1 : 0;
            DrawFighter(g, s.p[first], showHitboxes);
            DrawFighter(g, s.p[1 - first], showHitboxes);
            DrawProjectiles(g, s.p);
            DrawParticles(g, true);
            if (Stage != StageDef.Graveyard) DrawStageMotes(g, true);

            // Namensschilder
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i];
                if (P.state == St.KO) continue;
                CharDef c = CharDef.All[P.charId];
                float x = P.x + OffX[i], y = P.y + OffY[i] - 150;
                string tag = "P" + (i + 1);
                var sz = g.MeasureString(tag, FSmall);
                using (var b = new SolidBrush(Color.FromArgb(170, c.Outline))) g.FillRectangle(b, x - sz.Width / 2 - 4, y - 4, sz.Width + 8, sz.Height + 2);
                using (var b = new SolidBrush(c.Glow))
                {
                    g.DrawString(tag, FSmall, b, x - sz.Width / 2, y - 3);
                    g.FillPolygon(b, new[] { new PointF(x - 7, y + sz.Height - 2), new PointF(x + 7, y + sz.Height - 2), new PointF(x, y + sz.Height + 7) });
                }
            }
            g.Restore(gs);

            // Indikator für Spieler außerhalb des Bildes
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i];
                if (P.state == St.KO) continue;
                PointF sp = W2S(P.x, P.y - 46);
                if (sp.X > 0 && sp.X < VW && sp.Y > 0 && sp.Y < VH) continue;
                float cx = Math.Max(40, Math.Min(VW - 40, sp.X)), cy = Math.Max(40, Math.Min(VH - 140, sp.Y));
                CharDef c = CharDef.All[P.charId];
                using (var b = new SolidBrush(Color.FromArgb(200, c.Outline))) g.FillEllipse(b, cx - 26, cy - 26, 52, 52);
                using (var p = new Pen(c.Glow, 3)) g.DrawEllipse(p, cx - 26, cy - 26, 52, 52);
                DrawHead(g, GetStyle(P.charId, 0, 1), c.Body, new PointF(cx, cy), 0, 1.1f, 0.6f, false);
            }

            // Schimmer am unteren Rand (Abgrund)
            // achsenparallele Flächen über den ganzen Bildschirm brauchen keine Kantenglättung
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            Color abyss = Stage != StageDef.Graveyard ? Th.Abyss : Color.FromArgb(120, 60, 200);
            using (var b = new LinearGradientBrush(new PointF(0, VH - 160), new PointF(0, VH), Color.FromArgb(0, abyss), Color.FromArgb(70, abyss)))
                g.FillRectangle(b, 0, VH - 160, VW, 160);

            if (Flash > 0)
                using (var b = new SolidBrush(A(flashColor, Flash * 0.8f))) g.FillRectangle(b, 0, 0, VW, VH);
            g.SmoothingMode = sm;
        }

        public static Color DamageColor(float d)
        {
            if (d < 50) return Mixc(Color.White, Color.FromArgb(255, 232, 90), d / 50f);
            if (d < 100) return Mixc(Color.FromArgb(255, 232, 90), Color.FromArgb(255, 140, 40), (d - 50) / 50f);
            if (d < 150) return Mixc(Color.FromArgb(255, 140, 40), Color.FromArgb(235, 35, 35), (d - 100) / 50f);
            return Mixc(Color.FromArgb(235, 35, 35), Color.FromArgb(140, 0, 10), (d - 150) / 80f);
        }

        public void Shadowed(Graphics g, string t, Font f, Color c, float x, float y, bool center)
        {
            if (center) { var sz = g.MeasureString(t, f); x -= sz.Width / 2; }
            using (var b = new SolidBrush(Color.FromArgb(Math.Min(200, (int)c.A), 0, 0, 0))) g.DrawString(t, f, b, x + 2, y + 2);
            using (var b = new SolidBrush(c)) g.DrawString(t, f, b, x, y);
        }

        public static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void DrawHud(Graphics g, GameState s, string[] names)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i];
                CharDef c = CharDef.All[P.charId];
                float x = i == 0 ? 30 : VW - 330, y = VH - 98, w = 300, h = 84;
                using (var path = RoundRect(x, y, w, h, 14))
                {
                    using (var b = new SolidBrush(Color.FromArgb(185, 14, 8, 26))) g.FillPath(b, path);
                    using (var p = new Pen(A(c.Glow, 0.85f), 2.5f)) g.DrawPath(p, path);
                }
                // Porträt
                using (var b = new SolidBrush(Color.FromArgb(160, c.Outline))) g.FillEllipse(b, x + 10, y + 10, 64, 64);
                DrawHead(g, GetStyle(P.charId, 0, 1), c.Body, new PointF(x + 41, y + 44), 0, 1.75f, 0.7f, P.state == St.Broken);
                string nm = names != null && names[i] != null ? names[i] : c.Name;
                Shadowed(g, nm, FHudName, c.Glow, x + 84, y + 6, false);
                // Schaden
                Color dc = DamageColor(P.damage);
                float jx = P.hurtFlash > 0 ? (float)Math.Sin(frame * 3.1) * 3 : 0;
                string dmg = P.state == St.KO ? "K.O." : ((int)P.damage) + "%";
                Shadowed(g, dmg, FHudDmg, dc, x + 84 + jx, y + 24, false);
                // Leben
                for (int k = 0; k < Math.Max(s.stocks, P.stocks); k++)
                {
                    float sx = x + w - 22 - k * 20, sy = y + 16;
                    bool on = k < P.stocks;
                    using (var b = new SolidBrush(on ? c.Bone : Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(b, sx - 7, sy - 7, 14, 13);
                    if (on) using (var b = new SolidBrush(c.Outline)) { g.FillEllipse(b, sx - 4, sy - 3, 3.5f, 3.5f); g.FillEllipse(b, sx + 0.5f, sy - 3, 3.5f, 3.5f); }
                }
                // Schild
                float sh = Math.Max(0, P.shieldHp / Tuning.ShieldMax);
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, x + 190, y + 64, 96, 8);
                Color shc = P.state == St.Broken ? Color.FromArgb(255, 90, 90) : (P.shieldCd > 0 ? Color.FromArgb(150, 150, 170) : c.Glow);
                using (var b = new SolidBrush(shc)) g.FillRectangle(b, x + 190, y + 64, 96 * sh, 8);
                using (var b = new SolidBrush(Color.FromArgb(170, 220, 210, 240))) g.DrawString("Schild", FTiny, b, x + 190, y + 45);
                // Dodge-Abklingzeit
                if (P.dodgeCd > 0)
                    using (var b = new SolidBrush(Color.FromArgb(150, 200, 200, 220))) g.DrawString("Dodge " + (P.dodgeCd / 60f).ToString("0.0") + "s", FTiny, b, x + 84, y + 64);
                // Combo
                if (P.combo >= 2 && P.comboTimer > 0)
                    Shadowed(g, P.combo + " TREFFER COMBO" + (P.perfects > 0 ? "  (" + P.perfects + " perfekt)" : ""), FMed, Color.FromArgb(Math.Min(255, P.comboTimer * 5), 255, 220, 120), x + (i == 0 ? 0 : w), y - 40, false);
            }

            if (s.timeLeft >= 0)
            {
                int secs = (s.timeLeft + 59) / 60;
                string t = (secs / 60) + ":" + (secs % 60).ToString("00");
                Shadowed(g, t, FBig, secs <= 30 && s.phase == GameState.Fight ? Color.FromArgb(255, 110, 110) : Color.White, VW / 2f, 14, true);
            }

            if (s.phase == GameState.Countdown)
            {
                int n = (s.phaseTimer + 59) / 60;
                float k = (s.phaseTimer % 60) / 60f;
                using (var f = new Font("Segoe UI Black", 90 + 60 * k, FontStyle.Bold, GraphicsUnit.Pixel))
                    Shadowed(g, n.ToString(), f, Color.FromArgb((int)(255 * Math.Min(1, k * 2 + 0.3f)), 255, 255, 255), VW / 2f, VH / 2f - 110 - 30 * k, true);
            }
            if (goTimer > 0)
            {
                float k = goTimer / 60f;
                using (var f = new Font("Segoe UI Black", 110 + 30 * (1 - k), FontStyle.Bold, GraphicsUnit.Pixel))
                    Shadowed(g, "KAMPF!", f, Color.FromArgb((int)(255 * Math.Min(1, k * 2)), 255, 225, 130), VW / 2f, VH / 2f - 130, true);
            }
        }

        // Menühintergrund mit langsam animierter Szene
        public void DrawMenuBackdrop(Graphics g)
        {
            SyncStage();
            DrawMidLayer(g);
            var gs = g.Save();
            g.TranslateTransform(VW / 2f, VH / 2f);
            g.ScaleTransform(0.6f, 0.6f);
            g.TranslateTransform(0, 120);
            if (Stage != StageDef.Graveyard) DrawStageMotes(g, false);
            else
                foreach (var m in motes)
                    using (var b = new SolidBrush(A(m.C, 0.35f))) g.FillEllipse(b, m.X, m.Y, m.Size, m.Size);
            DrawArena(g);
            DrawDecor(g);
            g.Restore(gs);
            using (var b = new SolidBrush(Color.FromArgb(120, 6, 2, 14))) g.FillRectangle(b, 0, 0, VW, VH);
        }

        public void DrawPortrait(Graphics g, int ch, float x, float y, float scale, int animFrame, bool attackPose)
        {
            var p = new PlayerState { charId = ch, anim = animFrame, facing = 1, onGround = true, state = St.Idle };
            Pose pose = Anim.Idle(p);
            if (attackPose) pose = Attacks.SetOf(ch)[Attacks.SHeavy].Wind;
            var gs = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(scale, Math.Abs(scale));
            using (var b = new SolidBrush(A(CharDef.All[ch].Glow, 0.12f))) g.FillEllipse(b, -75, -135, 150, 150);
            DrawFighterPose(g, p, pose, 0, 0, 1, true, 0, 1f, false);
            g.Restore(gs);
        }
    }
}
