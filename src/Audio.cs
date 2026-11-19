using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sensensturm
{
    public enum Sfx { Swing, SwingHeavy, Hit, HitHeavy, Block, Parry, Break, KO, Jump, AirJump, Land, Dash, Dodge, Clank, Menu, Select, Beep, Go, Charge, Shock, Ult,
        ScreamReaper, ScreamFrost, ScreamGolem, ScreamArcher, ScreamShadow, ScreamMage, Count }

    // Kleiner Software-Mixer über waveOut. Alle Klänge werden beim Start synthetisiert (keine Dateien nötig).
    // Die Hintergrundmusik (Music.cs) wird im selben Thread live dazugemischt.
    public static class Audio
    {
        const int Rate = 22050, BufSamples = 512, NumBufs = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, IntPtr dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        class Voice { public float[] D; public double Pos, Rate; public float Vol; }

        static float[][] clips = new float[(int)Sfx.Count][];
        static readonly List<Voice> voices = new List<Voice>();
        static readonly object lk = new object();
        static IntPtr hwo;
        static IntPtr[] hdrs = new IntPtr[NumBufs], bufs = new IntPtr[NumBufs];
        static Thread thread;
        static volatile bool running;
        static readonly Random rng = new Random(7);
        public static float Volume = 0.8f;
        // Zeitlupe (letzter K.O.): alle Klänge tiefer und langsamer
        public static float PitchScale = 1f;

        // K.-o.-Schrei je Kämpfer (Index = CharDef.All)
        static readonly Sfx[] screams = { Sfx.ScreamReaper, Sfx.ScreamFrost, Sfx.ScreamGolem, Sfx.ScreamArcher, Sfx.ScreamShadow, Sfx.ScreamMage };
        public static Sfx Scream(int charId) { return screams[((charId % screams.Length) + screams.Length) % screams.Length]; }
        public static float[] Clip(Sfx s) { return clips[(int)s]; }

        public static void Init()
        {
            try { Generate(); } catch { return; }
            Music.Init();
            try
            {
                var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Rate * 2 };
                if (waveOutOpen(out hwo, new IntPtr(-1), ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
                int hs = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < NumBufs; i++)
                {
                    bufs[i] = Marshal.AllocHGlobal(BufSamples * 2);
                    var h = new WAVEHDR { lpData = bufs[i], dwBufferLength = BufSamples * 2 };
                    hdrs[i] = Marshal.AllocHGlobal(hs);
                    Marshal.StructureToPtr(h, hdrs[i], false);
                    waveOutPrepareHeader(hwo, hdrs[i], hs);
                }
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                thread.Start();
            }
            catch { running = false; }
        }

        public static void Shutdown()
        {
            running = false;
            try { if (thread != null) thread.Join(200); if (hwo != IntPtr.Zero) { waveOutReset(hwo); waveOutClose(hwo); } } catch { }
        }

        public static void Play(Sfx s, float vol = 1f, float pitch = 1f)
        {
            if (!running || Volume <= 0) return;
            float[] d = clips[(int)s];
            if (d == null) return;
            lock (lk)
            {
                if (voices.Count > 24) voices.RemoveAt(0);
                voices.Add(new Voice { D = d, Pos = 0, Rate = pitch * PitchScale * (0.97 + rng.NextDouble() * 0.06), Vol = vol });
            }
        }

        static void Loop()
        {
            int hs = Marshal.SizeOf(typeof(WAVEHDR));
            int flagsOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
            float[] mix = new float[BufSamples];
            short[] pcm = new short[BufSamples];
            bool[] started = new bool[NumBufs];
            while (running)
            {
                for (int i = 0; i < NumBufs; i++)
                {
                    int fl = Marshal.ReadInt32(hdrs[i], flagsOff);
                    if (started[i] && (fl & 1) == 0) continue;
                    Fill(mix);
                    for (int k = 0; k < BufSamples; k++)
                    {
                        float v = mix[k] * Volume;
                        v = v > 1 ? 1 : v < -1 ? -1 : v;
                        pcm[k] = (short)(v * 32000);
                    }
                    Marshal.Copy(pcm, 0, bufs[i], BufSamples);
                    waveOutWrite(hwo, hdrs[i], hs);
                    started[i] = true;
                }
                Thread.Sleep(2);
            }
        }

        static void Fill(float[] mix)
        {
            Array.Clear(mix, 0, mix.Length);
            lock (lk)
            {
                for (int v = voices.Count - 1; v >= 0; v--)
                {
                    Voice vc = voices[v];
                    for (int k = 0; k < mix.Length; k++)
                    {
                        int i = (int)vc.Pos;
                        if (i >= vc.D.Length - 1) { vc.Pos = vc.D.Length; break; }
                        float fr = (float)(vc.Pos - i);
                        mix[k] += (vc.D[i] * (1 - fr) + vc.D[i + 1] * fr) * vc.Vol;
                        vc.Pos += vc.Rate;
                    }
                    if (vc.Pos >= vc.D.Length - 1) voices.RemoveAt(v);
                }
            }
            Music.Mix(mix);
            // weicher Limiter
            for (int k = 0; k < mix.Length; k++) mix[k] = (float)Math.Tanh(mix[k] * 0.9);
        }

        // ------------------------------------------------------------ Synthese

        static float[] Buf(float sec) { return new float[(int)(sec * Rate)]; }
        static float Noise() { return (float)(rng.NextDouble() * 2 - 1); }

        static float[] Whoosh(float sec, float c0, float c1, float vol)
        {
            var b = Buf(sec);
            float y = 0, y2 = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float a = c0 + (c1 - c0) * t;
                y += a * (Noise() - y);
                y2 += a * (y - y2);
                float env = (float)Math.Pow(Math.Sin(Math.PI * Math.Min(1, t * 1.4)), 1.5) * (1 - t * 0.3f);
                b[i] = (y - y2 * 0.5f) * env * vol * 2.2f;
            }
            return b;
        }

        static float[] Thump(float sec, float f0, float f1, float noiseAmt, float vol, float drive)
        {
            var b = Buf(sec);
            double ph = 0;
            float y = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                float f = f0 + (f1 - f0) * t;
                ph += 2 * Math.PI * f / Rate;
                float env = (float)Math.Exp(-t * 6);
                y += 0.35f * (Noise() - y);
                float n = y * (float)Math.Exp(-t * 18) * noiseAmt;
                float s = (float)Math.Sin(ph) * env + n;
                b[i] = (float)Math.Tanh(s * drive) * vol;
            }
            return b;
        }

        static float[] Bell(float sec, float[] freqs, float decay, float vol, float click)
        {
            var b = Buf(sec);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0;
                for (int k = 0; k < freqs.Length; k++)
                    s += (float)(Math.Sin(2 * Math.PI * freqs[k] * t) * Math.Exp(-t * decay * (1 + k * 0.4))) / (k + 1);
                if (t < 0.012f) s += Noise() * click * (1 - t / 0.012f);
                b[i] = s * vol;
            }
            return b;
        }

        static float[] Tone(float sec, float f0, float f1, float vol, bool square)
        {
            var b = Buf(sec);
            double ph = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length;
                ph += 2 * Math.PI * (f0 + (f1 - f0) * t) / Rate;
                float s = (float)Math.Sin(ph);
                if (square) s = (float)Math.Tanh(s * 4) * 0.6f;
                float env = Math.Min(1, t * 30) * (1 - t);
                b[i] = s * env * vol;
            }
            return b;
        }

        static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n = Math.Max(n, p.Length);
            var b = new float[n];
            foreach (var p in parts) for (int i = 0; i < p.Length; i++) b[i] += p[i];
            return b;
        }

        // Vokale als Formanten (F1, F2, F3 in Hz)
        static readonly float[] VA = { 800, 1200, 2600 }, VO = { 500, 850, 2500 }, VU = { 340, 800, 2400 }, VE = { 550, 1800, 2600 }, VAE = { 700, 1650, 2500 };

        static float[] Sc(float[] v, float k) { return new[] { v[0] * k, v[1] * k, v[2] * k }; }

        // Schrei aus dem Synthesizer: Sägezahn-Stimme mit Tonhöhenverlauf (Anstieg, dann Abfall beim Wegfliegen),
        // drei Formantfilter, die von Vokal a zu Vokal b gleiten, dazu Heiserkeit (Rauschen), Knochenklappern und Verzerrung.
        static float[] Scream(float sec, float f0a, float f0b, float f0c, float[] va, float[] vb, float rasp, float rattle, float vib, float drive, float vol)
        {
            var b = Buf(sec);
            double ph = 0;
            float[] y1 = new float[3], y2 = new float[3];
            float jit = 0, peak = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)b.Length, tm = i / (float)Rate;
                float f0 = t < 0.14f ? f0a + (f0b - f0a) * (t / 0.14f) : f0b + (f0c - f0b) * (float)Math.Pow((t - 0.14f) / 0.86f, 0.8);
                jit += (Noise() - jit) * 0.02f;
                f0 *= 1 + vib * (float)Math.Sin(2 * Math.PI * 6.3 * tm) * Math.Min(1f, t * 4) + jit * 0.03f;
                ph += f0 / Rate;
                float src = (float)(2 * (ph - Math.Floor(ph)) - 1);
                src = src * (1 - rasp) + Noise() * rasp * 1.4f;
                if (rattle > 0)
                {
                    float clack = (float)Math.Sin(2 * Math.PI * 23 * tm);
                    src *= 1 - rattle * 0.45f * (clack > 0 ? 1 : 0);
                }
                float o = 0;
                for (int k = 0; k < 3; k++)
                {
                    float F = va[k] + (vb[k] - va[k]) * t, bw = 90 + 60 * k;
                    double r = Math.Exp(-Math.PI * bw / Rate);
                    float c1 = (float)(2 * r * Math.Cos(2 * Math.PI * F / Rate)), c2 = (float)(-r * r);
                    float y = src * (float)(1 - r) + c1 * y1[k] + c2 * y2[k];
                    y2[k] = y1[k]; y1[k] = y;
                    o += y * (k == 0 ? 1f : k == 1 ? 0.6f : 0.3f);
                }
                float env = Math.Min(1f, tm / 0.025f) * (float)Math.Pow(1 - t, 1.3);
                b[i] = (float)Math.Tanh(o * drive) * env;
                peak = Math.Max(peak, Math.Abs(b[i]));
            }
            if (peak > 0) for (int i = 0; i < b.Length; i++) b[i] *= vol / peak;
            return b;
        }

        static void Generate()
        {
            clips[(int)Sfx.Swing] = Whoosh(0.16f, 0.35f, 0.08f, 0.45f);
            clips[(int)Sfx.SwingHeavy] = Whoosh(0.32f, 0.2f, 0.04f, 0.6f);
            clips[(int)Sfx.Hit] = Mix(Thump(0.16f, 180, 60, 1.2f, 0.75f, 2.2f), Whoosh(0.05f, 0.6f, 0.3f, 0.25f));
            clips[(int)Sfx.HitHeavy] = Mix(Thump(0.38f, 120, 35, 1.6f, 0.95f, 3.5f), Bell(0.25f, new float[] { 310, 465 }, 12, 0.15f, 0.6f));
            clips[(int)Sfx.Block] = Mix(Bell(0.22f, new float[] { 820, 1230, 1690 }, 18, 0.35f, 0.9f), Thump(0.08f, 240, 120, 0.6f, 0.4f, 1.5f));
            clips[(int)Sfx.Parry] = Mix(Bell(0.9f, new float[] { 1318, 1975, 2637, 3951 }, 4.5f, 0.42f, 1.2f), Tone(0.35f, 900, 2400, 0.12f, false), Thump(0.06f, 300, 200, 0.8f, 0.3f, 1.2f));
            clips[(int)Sfx.Break] = Mix(Whoosh(0.45f, 0.9f, 0.6f, 0.5f), Tone(0.5f, 900, 160, 0.3f, true), Bell(0.4f, new float[] { 2100, 3300 }, 10, 0.2f, 1f));
            clips[(int)Sfx.KO] = Mix(Thump(0.9f, 70, 28, 2.2f, 1f, 4f), Tone(0.7f, 420, 70, 0.3f, true), Whoosh(0.6f, 0.4f, 0.05f, 0.5f));
            clips[(int)Sfx.Jump] = Whoosh(0.12f, 0.12f, 0.3f, 0.3f);
            clips[(int)Sfx.AirJump] = Mix(Whoosh(0.16f, 0.15f, 0.4f, 0.3f), Tone(0.14f, 520, 880, 0.1f, false));
            clips[(int)Sfx.Land] = Thump(0.08f, 110, 60, 0.9f, 0.35f, 1.2f);
            clips[(int)Sfx.Dash] = Whoosh(0.16f, 0.25f, 0.15f, 0.4f);
            clips[(int)Sfx.Dodge] = Mix(Whoosh(0.22f, 0.5f, 0.2f, 0.3f), Tone(0.2f, 1200, 700, 0.06f, false));
            clips[(int)Sfx.Clank] = Mix(Bell(0.35f, new float[] { 1520, 2280, 3010 }, 9, 0.4f, 1.4f), Thump(0.06f, 400, 200, 1f, 0.3f, 1.5f));
            clips[(int)Sfx.Menu] = Tone(0.05f, 660, 660, 0.25f, true);
            clips[(int)Sfx.Select] = Tone(0.12f, 700, 1320, 0.3f, true);
            clips[(int)Sfx.Beep] = Tone(0.18f, 520, 520, 0.35f, true);
            clips[(int)Sfx.Go] = Mix(Tone(0.45f, 780, 780, 0.3f, true), Tone(0.45f, 1040, 1040, 0.2f, true));
            clips[(int)Sfx.Charge] = Bell(0.3f, new float[] { 1760, 2640 }, 10, 0.3f, 0.2f);
            clips[(int)Sfx.Shock] = Thump(0.35f, 80, 40, 2f, 0.7f, 3f);
            // K.-o.-Schreie: jeder Kämpfer mit eigener Stimme
            clips[(int)Sfx.ScreamReaper] = Mix(Scream(1.1f, 290, 380, 150, VA, VU, 0.38f, 1f, 0.03f, 2.2f, 0.5f), Whoosh(1.0f, 0.08f, 0.02f, 0.12f));
            clips[(int)Sfx.ScreamFrost] = Mix(Scream(1.05f, 350, 470, 200, VE, VU, 0.28f, 0.6f, 0.035f, 2f, 0.46f), Bell(0.9f, new float[] { 2637, 3520, 4186 }, 4f, 0.1f, 0.3f));
            clips[(int)Sfx.ScreamGolem] = Mix(Scream(1.25f, 92, 125, 52, Sc(VO, 0.72f), Sc(VU, 0.72f), 0.5f, 0f, 0.02f, 3.5f, 0.6f), Thump(0.9f, 70, 30, 2.2f, 0.45f, 3f));
            clips[(int)Sfx.ScreamArcher] = Scream(0.95f, 470, 630, 320, Sc(VA, 1.15f), Sc(VO, 1.15f), 0.08f, 0f, 0.028f, 1.6f, 0.48f);
            clips[(int)Sfx.ScreamShadow] = Scream(0.75f, 560, 720, 380, Sc(VAE, 1.15f), Sc(VA, 1.15f), 0.13f, 0f, 0.02f, 1.8f, 0.46f);
            clips[(int)Sfx.ScreamMage] = Mix(Scream(1.1f, 195, 265, 105, VA, VO, 0.45f, 0.4f, 0.04f, 2.6f, 0.5f), Whoosh(1.1f, 0.5f, 0.15f, 0.3f));
            clips[(int)Sfx.Ult] = Mix(Bell(1.3f, new float[] { 440, 660, 880, 1320 }, 2.2f, 0.32f, 0.8f), Tone(0.8f, 160, 1400, 0.16f, false), Whoosh(0.9f, 0.15f, 0.9f, 0.4f), Thump(0.5f, 90, 40, 1.8f, 0.6f, 3f));
        }
    }
}
