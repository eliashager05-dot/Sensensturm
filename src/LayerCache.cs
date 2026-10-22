using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace Sensensturm
{
    // Statische Ebene (Bühne, Bäume), die einmal in ein Bitmap gezeichnet und pro Frame nur eingeblendet wird.
    // Skaliertes Einblenden ist in GDI+ sehr langsam (bilinear ~100 ms bei 1080p, Nächster Nachbar ~5 ms, 1:1 ~1,5 ms).
    // Deshalb wird das Bitmap genau im Bildschirmmaßstab gerendert und die Kamera rastet über Snap() auf diesen Maßstab ein.
    // Weicht der Zoom zu weit ab, wird im Hintergrund neu gerendert.
    sealed class LayerCache : IDisposable
    {
        const float Rebuild = 0.004f;   // ab dieser Abweichung im Hintergrund neu rendern
        const float Tolerance = 0.02f;  // bis zu dieser Abweichung rastet die Kamera auf den Cache-Maßstab ein
        const float MaxScale = 2.25f;   // begrenzt den Speicherbedarf bei sehr hohen Auflösungen
        readonly RectangleF world;
        readonly Action<Graphics> draw;
        Bitmap bmp;
        float bmpScale;
        Task<Bitmap> job;
        float jobScale;

        public LayerCache(RectangleF world, Action<Graphics> draw) { this.world = world; this.draw = draw; }

        Bitmap Render(float scale)
        {
            var b = new Bitmap(Math.Max(1, (int)Math.Ceiling(world.Width * scale)), Math.Max(1, (int)Math.Ceiling(world.Height * scale)), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-world.X, -world.Y);
                draw(g);
            }
            return b;
        }

        // fertiges Hintergrund-Rendern übernehmen und bei Bedarf ein neues anstoßen
        void Update(float scale)
        {
            if (job != null && job.IsCompleted)
            {
                Bitmap done = null;
                try { done = job.Result; } catch { }
                if (done != null) { if (bmp != null) bmp.Dispose(); bmp = done; bmpScale = jobScale; }
                job = null;
            }
            float want = Math.Min(MaxScale, scale);
            if (bmp == null) { bmp = Render(want); bmpScale = want; }
            else if (job == null && Math.Abs(want / bmpScale - 1) > Rebuild)
            {
                jobScale = want;
                job = Task.Factory.StartNew(() => Render(want));
            }
        }

        // Liefert den Maßstab (Gerätepixel pro Welteinheit), in dem gezeichnet werden sollte: liegt der gewünschte nahe
        // am Cache, den des Caches, damit Draw() ihn 1:1 kopieren kann
        public float Snap(float scale)
        {
            if (scale <= 0) return scale;
            Update(scale);
            return Math.Abs(scale / bmpScale - 1) <= Tolerance ? bmpScale : scale;
        }

        public void Draw(Graphics g)
        {
            float scale;
            PointF[] o = { world.Location };
            using (Matrix m = g.Transform) { scale = m.Elements[0]; m.TransformPoints(o); }
            if (scale <= 0) return;
            Update(scale);

            // in Gerätepixeln auf ganze Pixel ausgerichtet zeichnen, bei gleichem Maßstab ohne Skalierung
            float k = scale / bmpScale;
            int w = (int)Math.Round(bmp.Width * k), h = (int)Math.Round(bmp.Height * k);
            var st = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, new Rectangle((int)Math.Round(o[0].X), (int)Math.Round(o[0].Y), w, h), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        // Hintergrund-Rendern abwarten, bevor die gemeinsam genutzten Pfade geändert oder anderweitig gezeichnet werden
        public void Wait()
        {
            if (job == null) return;
            try { job.Wait(); } catch { }
        }

        public void Dispose()
        {
            Wait();
            if (job != null && job.Status == TaskStatus.RanToCompletion) job.Result.Dispose();
            job = null;
            if (bmp != null) bmp.Dispose();
            bmp = null;
        }
    }
}
