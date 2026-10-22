using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sensensturm
{
    // Turniermodus: Setup, animierter Turnierbaum, Turnier-Matches und Siegerehrung
    public partial class Game
    {
        Tournament tour;                          // laufendes Turnier (bleibt beim Verlassen erhalten)
        readonly Tournament draft = NewDraft();   // Einstellungen und Teilnehmer im Setup
        int tourCount = 8;
        bool tourMatch;                           // das laufende Match gehört zum Turnier
        int tourR, tourM;                         // Runde und Paarung des laufenden Matches
        readonly int[] tourSlots = new int[2];    // Teilnehmer auf Spielerplatz 0 und 1
        Bot bot0;                                 // CPU auf Spielerplatz 0 (nur CPU gegen CPU)
        int tourAutoAt = -1;                      // Frame, an dem das nächste CPU-Match ausgerechnet wird
        bool tourShowBracket;                     // nach dem Turnier: Baum statt Siegerehrung zeigen
        readonly Random tourRng = new Random();
        readonly List<Confetto> confetti = new List<Confetto>();

        class Confetto { public float X, Y, VX, VY, Rot, VR, W, H, Phase; public Color C; }

        const int LineFrames = 28, PopFrames = 14, OutFrames = 20;
        static readonly Color Gold = Color.FromArgb(255, 214, 90);

        // ================================================================ Setup

        static Tournament NewDraft()
        {
            var t = new Tournament();
            for (int i = 0; i < Tournament.MaxPlayers; i++)
                t.E.Add(new Entrant { Char = i == 0 ? Settings.P1Char : i % CharDef.Count, Cpu = i == 0 ? 0 : 2 });
            RenameDraft(t);
            return t;
        }

        // Menschen heißen "Spieler 1, 2, ...", CPUs bekommen feste Namen
        static void RenameDraft(Tournament t)
        {
            int humans = 0;
            for (int i = 0; i < t.E.Count; i++)
                t.E[i].Name = t.E[i].Human ? "Spieler " + (++humans) : Tournament.CpuName(i);
        }

        static string CpuText(int cpu) { return cpu == 0 ? "Mensch" : "CPU " + new string('●', cpu); }

        void BuildTourneySetupItems()
        {
            Tournament d = draft;
            Add("Teilnehmer", tourCount.ToString(), null, x => tourCount = Math.Max(2, Math.Min(Tournament.MaxPlayers, tourCount + x)));
            Add("Leben", d.Stocks.ToString(), null, x => d.Stocks = Math.Max(1, Math.Min(9, d.Stocks + x)));
            Add("Zeitlimit", MinutesText(d.Minutes), null, x => d.Minutes = NextMinutes(d.Minutes, x));
            Add("Arena", d.Stage < 0 ? "Zufällig" : StageDef.Names[d.Stage], null, x => d.Stage = Cycle(d.Stage + 1, x, StageDef.Count + 1) - 1);
            Add("CPU gegen CPU", d.AutoCpu ? "Ausrechnen" : "Zuschauen", null, x => d.AutoCpu = !d.AutoCpu);
            Add("CPU-Kämpfer auslosen", null, () => { foreach (Entrant e in d.E) if (!e.Human) e.Char = tourRng.Next(CharDef.Count); }, null);
            Add("Turnier starten!", null, StartTournament, null);
            Add("Zurück", null, () => Go(Scr.Main), null);
            for (int i = 0; i < tourCount; i++)
            {
                Entrant e = d.E[i];
                items.Add(new Item
                {
                    Label = e.Name, Value = CharDef.All[e.Char].Name + " · " + CpuText(e.Cpu), Small = true,
                    Ok = () => { e.Cpu = (e.Cpu + 1) % 4; RenameDraft(d); },
                    Change = x => e.Char = Cycle(e.Char, x, CharDef.Count)
                });
            }
        }

        void DrawTourneySetup(Graphics g)
        {
            DrawTitle(g, "Turnier");
            DrawItems(g, 40, 165, 400, 46, 6, 0, 8);
            DrawItems(g, 460, 165, 395, 42, 4, 8, Math.Min(8, tourCount));
            if (tourCount > 8) DrawItems(g, 865, 165, 395, 42, 4, 16, tourCount - 8);
            int size = 2, rounds = 1;
            while (size < tourCount) { size *= 2; rounds++; }
            int byes = size - tourCount, humans = 0;
            for (int i = 0; i < tourCount; i++) if (draft.E[i].Human) humans++;
            Color info = Color.FromArgb(220, 215, 235), dim = Color.FromArgb(170, 170, 160, 200);
            Panel(g, 460, 482, 800, 118);
            Line(g, tourCount + " Teilnehmer  ·  " + rounds + (rounds == 1 ? " Runde" : " Runden") + (byes > 0 ? "  ·  " + byes + (byes == 1 ? " Freilos" : " Freilose") : "") +
                "  ·  " + humans + (humans == 1 ? " Mensch" : " Menschen"), 478, 492, Gold, R.FHudName);
            Line(g, "Enter / Klick: Mensch ↔ CPU leicht / mittel / schwer   ·   Links / Rechts: Kämpfer wählen", 478, 520, info, R.FTiny);
            Line(g, "Menschen spielen nacheinander am selben PC: gegen die CPU mit den Tasten von Spieler 1,", 478, 541, info, R.FTiny);
            Line(g, "zwei Menschen gegeneinander mit der Belegung von Spieler 1 und Spieler 2.", 478, 560, info, R.FTiny);
            Line(g, "Die Paarungen werden ausgelost. Freilose ziehen ohne Kampf in die nächste Runde ein.", 478, 579, dim, R.FTiny);
        }

        void StartTournament()
        {
            tour = new Tournament { Stocks = draft.Stocks, Minutes = draft.Minutes, Stage = draft.Stage, AutoCpu = draft.AutoCpu };
            for (int i = 0; i < tourCount; i++)
            {
                Entrant e = draft.E[i];
                tour.E.Add(new Entrant { Name = e.Name, Char = e.Char, Cpu = e.Cpu });
            }
            tour.Build(tourRng, frame + 15);
            confetti.Clear();
            tourAutoAt = -1;
            tourShowBracket = false;
            Go(Scr.Tourney);
        }

        // ================================================================ Matches

        bool CpuOnly(int r, int m) { return !tour.E[tour.Slot[r][2 * m]].Human && !tour.E[tour.Slot[r][2 * m + 1]].Human; }

        void StartTourMatch()
        {
            int r, m;
            if (tour == null || !tour.NextMatch(out r, out m)) return;
            int ia = tour.Slot[r][2 * m], ib = tour.Slot[r][2 * m + 1];
            // ein einzelner Mensch spielt immer auf Platz 0 (Tasten von Spieler 1, Anzeige links)
            bool swap = !tour.E[ia].Human && tour.E[ib].Human;
            tourSlots[0] = swap ? ib : ia;
            tourSlots[1] = swap ? ia : ib;
            Entrant e0 = tour.E[tourSlots[0]], e1 = tour.E[tourSlots[1]];
            mode = Mode.Local;
            bot0 = e0.Human ? null : new Bot(e0.Cpu);
            bot = e1.Human ? null : new Bot(e1.Cpu);
            names[0] = e0.Name;
            names[1] = e1.Name;
            tourMatch = true;
            tourR = r; tourM = m;
            int stage = tour.Stage < 0 ? tourRng.Next(StageDef.Count) : tour.Stage;
            BeginMatch(GameState.NewMatch(e0.Char, e1.Char, tour.Stocks, tour.Minutes, stage));
        }

        // Match beendet: Sieger eintragen, zurück zum Baum, wo der Aufstieg animiert wird
        void TourMatchDone()
        {
            int w = Tournament.WinnerOf(state, tourRng);
            int winner = tourSlots[w];
            if (state.winner < 0) Toast("Gleichstand – das Los entscheidet: " + tour.E[winner].Name + " kommt weiter.", false);
            int r = tourR, m = tourM;
            LeaveTourMatch();
            tour.Advance(r, m, winner, frame + 20);
        }

        void LeaveTourMatch()
        {
            tourMatch = false;
            bot0 = null;
            state = null;
            paused = false;
            Go(Scr.Tourney);
        }

        void SimulateTourMatch()
        {
            int r, m;
            if (tour == null || !tour.NextMatch(out r, out m)) return;
            tour.Advance(r, m, tour.Simulate(r, m, tourRng), frame);
            Audio.Play(Sfx.Hit, 0.5f, 1.2f);
        }

        void BuildTourneyItems()
        {
            if (tour == null) { Add("Zurück", null, () => Go(Scr.Main), null); return; }
            if (tour.Done)
            {
                Add("Neues Turnier", null, () => { tour = null; Go(Scr.TourneySetup); }, null);
                Add(tourShowBracket ? "Siegerehrung" : "Turnierbaum ansehen", null, () => tourShowBracket = !tourShowBracket, null);
                Add("Hauptmenü", null, () => { tour = null; Go(Scr.Main); }, null);
                return;
            }
            int r, m;
            tour.NextMatch(out r, out m);
            bool cpuOnly = r >= 0 && CpuOnly(r, m);
            // CPU gegen CPU mit "Ausrechnen" läuft von selbst (TickTourney)
            if (!cpuOnly) Add("Kampf starten!", null, StartTourMatch, null);
            else if (!tour.AutoCpu) { Add("Zuschauen", null, StartTourMatch, null); Add("Ergebnis ausrechnen", null, SimulateTourMatch, null); }
            Add("Turnier verlassen", null, () => Go(Scr.Main), null);
            if (sel >= items.Count) sel = 0;
            if (!items[sel].Enabled) sel = NextSel(1);
        }

        // Ende aller laufenden Animationen im Baum
        int TourAnimEnd()
        {
            int end = 0;
            for (int r = 0; r <= tour.Rounds; r++)
                for (int i = 0; i < tour.Slot[r].Length; i++)
                    if (tour.Slot[r][i] != Tournament.Open) end = Math.Max(end, tour.FillTime[r][i] + (r > 0 ? LineFrames : 0) + PopFrames);
            return end;
        }

        void TickTourney()
        {
            if (tour == null) return;
            // Klänge zu den Animationen
            for (int r = 0; r <= tour.Rounds; r++)
                for (int i = 0; i < tour.Slot[r].Length; i++)
                {
                    if (tour.Slot[r][i] < 0) continue;
                    int t = tour.FillTime[r][i];
                    if (r == 0 && frame == t) Audio.Play(Sfx.Menu, 0.25f, 1f + i * 0.03f);
                    if (r > 0 && frame == t) Audio.Play(Sfx.Dash, 0.35f, 1.3f);
                    if (r > 0 && frame == t + LineFrames) Audio.Play(r == tour.Rounds ? Sfx.Parry : Sfx.Charge, r == tour.Rounds ? 1f : 0.6f, 1f + 0.08f * r);
                }
            if (tour.Done)
            {
                int pop = tour.FillTime[tour.Rounds][0] + LineFrames;
                if (frame == pop) { Audio.Play(Sfx.Go, 0.9f); SpawnConfetti(180, true); }
                if (frame > pop && frame % 2 == 0 && confetti.Count < 260) SpawnConfetti(2, false);
                TickConfetti();
                return;
            }
            int nr, nm;
            if (!tour.AutoCpu || !tour.NextMatch(out nr, out nm) || !CpuOnly(nr, nm)) { tourAutoAt = -1; return; }
            if (tourAutoAt < 0) tourAutoAt = Math.Max(frame, TourAnimEnd()) + 45;
            if (frame >= tourAutoAt) { tourAutoAt = -1; SimulateTourMatch(); }
        }

        // für Tests/Screenshots: Turnier mit n Teilnehmern starten und einige Frames laufen lassen
        public void DebugTournament(int n, bool allCpu, int frames)
        {
            tourCount = n;
            draft.AutoCpu = true;
            draft.E[0].Cpu = allCpu ? 2 : 0;
            RenameDraft(draft);
            StartTournament();
            DebugFrames(frames);
        }

        public void DebugFrames(int frames) { for (int i = 0; i < frames; i++) Tick(); }

        // Turnier-Match starten, Spielerplatz loserSlot fliegt raus, Ergebnis zurück in den Baum; liefert den Turniersieger
        public string DebugPlayTourMatch(int loserSlot)
        {
            StartTourMatch();
            if (!tourMatch) return null;
            state.p[loserSlot].stocks = 1;
            state.p[loserSlot].x = 5000;
            DebugFrames(300); // Countdown (180) + K.O.
            if (!tourMatch || state == null || state.phase != GameState.Over) return null;
            TourMatchDone();
            return tour.Done ? tour.E[tour.Champion].Name : "";
        }

        public void DebugFinishTournament()
        {
            int r, m;
            while (tour.NextMatch(out r, out m)) tour.Advance(r, m, tour.Simulate(r, m, tourRng), frame);
        }

        // ================================================================ Konfetti

        void SpawnConfetti(int n, bool burst)
        {
            Color[] cols = { Gold, Color.FromArgb(255, 110, 150), Color.FromArgb(120, 220, 255), Color.FromArgb(160, 255, 140), Color.FromArgb(210, 150, 255), Color.White };
            for (int i = 0; i < n; i++)
            {
                var c = new Confetto
                {
                    X = burst ? 640 + (float)(tourRng.NextDouble() - 0.5) * 200 : (float)tourRng.NextDouble() * Renderer.VW,
                    Y = burst ? 300 : -20,
                    W = 6 + (float)tourRng.NextDouble() * 6, H = 3 + (float)tourRng.NextDouble() * 4,
                    Rot = (float)(tourRng.NextDouble() * Math.PI * 2), VR = (float)(tourRng.NextDouble() - 0.5) * 0.3f,
                    Phase = (float)(tourRng.NextDouble() * 6.28), C = cols[tourRng.Next(cols.Length)]
                };
                if (burst)
                {
                    double a = -Math.PI / 2 + (tourRng.NextDouble() - 0.5) * 2.6;
                    float sp = 6 + (float)tourRng.NextDouble() * 12;
                    c.VX = (float)Math.Cos(a) * sp; c.VY = (float)Math.Sin(a) * sp;
                }
                else { c.VX = (float)(tourRng.NextDouble() - 0.5) * 1.5f; c.VY = 1 + (float)tourRng.NextDouble() * 2; }
                confetti.Add(c);
            }
        }

        void TickConfetti()
        {
            for (int i = confetti.Count - 1; i >= 0; i--)
            {
                Confetto c = confetti[i];
                c.VY = Math.Min(c.VY + 0.12f, 3.2f);
                c.VX *= 0.97f;
                c.X += c.VX + (float)Math.Sin(frame * 0.07 + c.Phase) * 0.8f;
                c.Y += c.VY;
                c.Rot += c.VR;
                if (c.Y > Renderer.VH + 20) confetti.RemoveAt(i);
            }
        }

        void DrawConfetti(Graphics g)
        {
            foreach (Confetto c in confetti)
            {
                // flatterndes Rechteck: Breite schwingt mit der Drehung
                float cw = c.W * (float)Math.Abs(Math.Cos(frame * 0.15 + c.Phase)) + 1, ch = c.H;
                float co = (float)Math.Cos(c.Rot), si = (float)Math.Sin(c.Rot);
                var pts = new PointF[4];
                float[,] k = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 } };
                for (int j = 0; j < 4; j++)
                {
                    float px = k[j, 0] * cw / 2, py = k[j, 1] * ch / 2;
                    pts[j] = new PointF(c.X + px * co - py * si, c.Y + px * si + py * co);
                }
                using (var b = new SolidBrush(c.C)) g.FillPolygon(b, pts);
            }
        }

        // ================================================================ Turnierbaum

        float tbBoxW, tbBoxH, tbColStep;
        const float TbTop = 128, TbBottom = 590, TbGap = 56, TbChampW = 190, TbChampH = 46;
        float TbChampY { get { return (TbTop + TbBottom) / 2 - 92; } }

        void TbLayout()
        {
            int R = tour.Rounds;
            tbBoxW = R >= 4 ? 128 : R == 3 ? 150 : 176;
            tbBoxH = R >= 4 ? 30 : 36;
            tbColStep = R > 1 ? (Renderer.VW / 2f - TbGap / 2 - tbBoxW - 24) / (R - 1) : 0;
        }

        bool TbLeft(int r, int i) { return i < tour.Slot[r].Length / 2; }

        // linke Kante einer Box; die linke Hälfte des Baums läuft nach rechts, die rechte nach links, das Finale in der Mitte
        float TbX(int r, int i)
        {
            if (r == tour.Rounds) return Renderer.VW / 2f - TbChampW / 2;
            float off = TbGap / 2 + (tour.Rounds - 1 - r) * tbColStep;
            return TbLeft(r, i) ? Renderer.VW / 2f - off - tbBoxW : Renderer.VW / 2f + off;
        }

        // senkrechte Mitte einer Box
        float TbY(int r, int i)
        {
            if (r == tour.Rounds) return TbChampY;
            if (r == 0)
            {
                int n = tour.Slot[0].Length / 2, li = i % n;
                return TbTop + (li + 0.5f) * (TbBottom - TbTop) / n;
            }
            return (TbY(r - 1, 2 * i) + TbY(r - 1, 2 * i + 1)) / 2;
        }

        // Linienzug von einer Box zur Box der nächsten Runde
        PointF[] TbPath(int r, int i)
        {
            bool left = TbLeft(r, i);
            float y = TbY(r, i);
            float sx = left ? TbX(r, i) + tbBoxW : TbX(r, i);
            float cx = Renderer.VW / 2f;
            if (r == tour.Rounds - 1)
                return new[] { new PointF(sx, y), new PointF(cx, y), new PointF(cx, TbChampY + TbChampH / 2) };
            int p = i / 2;
            float py = TbY(r + 1, p);
            float ex = left ? TbX(r + 1, p) : TbX(r + 1, p) + tbBoxW;
            float mx = (sx + ex) / 2;
            return new[] { new PointF(sx, y), new PointF(mx, y), new PointF(mx, py), new PointF(ex, py) };
        }

        static float Dist(PointF a, PointF b) { float dx = b.X - a.X, dy = b.Y - a.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        // vorderer Teil eines Linienzugs (t = 0..1)
        static PointF[] PartialPath(PointF[] pts, float t)
        {
            float total = 0;
            for (int k = 1; k < pts.Length; k++) total += Dist(pts[k - 1], pts[k]);
            float want = total * Math.Max(0, Math.Min(1, t));
            var list = new List<PointF> { pts[0] };
            for (int k = 1; k < pts.Length; k++)
            {
                float d = Dist(pts[k - 1], pts[k]);
                if (want >= d) { list.Add(pts[k]); want -= d; continue; }
                float f = d > 0 ? want / d : 0;
                list.Add(new PointF(pts[k - 1].X + (pts[k].X - pts[k - 1].X) * f, pts[k - 1].Y + (pts[k].Y - pts[k - 1].Y) * f));
                break;
            }
            return list.ToArray();
        }

        static float Clamp01(float v) { return Math.Max(0, Math.Min(1, v)); }
        static float EaseOutBack(float t) { float c = 1.7f; t -= 1; return 1 + (c + 1) * t * t * t + c * t * t; }

        void DrawConnector(Graphics g, int r, int i, int now)
        {
            int v = tour.Slot[r][i];
            if (v == Tournament.Bye || v == Tournament.Open || now < tour.FillTime[r][i] + (r > 0 ? LineFrames : 0)) return;
            PointF[] path = TbPath(r, i);
            using (var p = new Pen(Color.FromArgb(70, 200, 190, 230), 2)) g.DrawLines(p, path);
            int pr = r + 1, pi = i / 2;
            if (tour.Slot[pr][pi] != v || now < tour.FillTime[pr][pi]) return;
            // Sieger: leuchtende Linie läuft in die nächste Runde
            float t = Clamp01((now - tour.FillTime[pr][pi]) / (float)LineFrames);
            PointF[] part = PartialPath(path, t);
            if (part.Length < 2) return;
            Color c = pr == tour.Rounds ? Gold : CharDef.All[tour.E[v].Char].Glow;
            using (var glow = new Pen(Color.FromArgb(t < 1 ? 120 : 60, c), t < 1 ? 9 : 6) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(glow, part);
            using (var core = new Pen(Color.FromArgb(240, Mixc(c, Color.White, 0.4f)), 2.6f) { LineJoin = LineJoin.Round }) g.DrawLines(core, part);
            if (t < 1)
            {
                PointF h = part[part.Length - 1];
                using (var b = new SolidBrush(Color.FromArgb(120, c))) g.FillEllipse(b, h.X - 11, h.Y - 11, 22, 22);
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, h.X - 4.5f, h.Y - 4.5f, 9, 9);
            }
        }

        static Color Mixc(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void DrawSlot(Graphics g, int r, int i, int now, bool current)
        {
            int rounds = tour.Rounds;
            bool champ = r == rounds;
            float w = champ ? TbChampW : tbBoxW, h = champ ? TbChampH : tbBoxH;
            float x = TbX(r, i), y = TbY(r, i) - h / 2;
            int v = tour.Slot[r][i];
            int appear = r == 0 ? tour.FillTime[0][i] : tour.FillTime[r][i] + LineFrames;
            bool shown = v != Tournament.Open && now >= appear;

            if (r == 0 && !shown) return; // Einflug noch nicht begonnen
            if (!shown)
            {
                // leerer Platz, wartet auf einen Sieger
                using (var path = Renderer.RoundRect(x, y, w, h, 8))
                {
                    using (var b = new SolidBrush(Color.FromArgb(champ ? 150 : 110, 12, 6, 24))) g.FillPath(b, path);
                    using (var p = new Pen(champ ? Color.FromArgb(150, Gold) : Color.FromArgb(90, 180, 160, 230), champ ? 2 : 1.5f) { DashStyle = DashStyle.Dash }) g.DrawPath(p, path);
                }
                string q = champ ? "TURNIERSIEGER" : "?";
                var sz = g.MeasureString(q, R.FTiny);
                using (var b = new SolidBrush(champ ? Color.FromArgb(180, Gold) : Color.FromArgb(110, 200, 190, 230))) g.DrawString(q, R.FTiny, b, x + (w - sz.Width) / 2, y + (h - sz.Height) / 2);
                return;
            }

            float k = Clamp01((now - appear) / (float)PopFrames);
            var gs = g.Save();
            float alpha = 1f;
            if (r == 0)
            {
                // Einflug von außen
                float e = EaseOutBack(Clamp01((now - appear) / 22f));
                g.TranslateTransform((1 - e) * (TbLeft(r, i) ? -260 : 260), 0);
                alpha = Clamp01((now - appear) / 10f);
            }
            else
            {
                // Aufploppen beim Einzug in die nächste Runde
                float s = 1 + 0.45f * (1 - k) * (1 - k);
                g.TranslateTransform(x + w / 2, y + h / 2);
                g.ScaleTransform(s, s);
                g.TranslateTransform(-(x + w / 2), -(y + h / 2));
            }

            if (v == Tournament.Bye)
            {
                using (var path = Renderer.RoundRect(x, y, w, h, 8))
                {
                    using (var b = new SolidBrush(Color.FromArgb((int)(90 * alpha), 12, 6, 24))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb((int)(70 * alpha), 160, 150, 190), 1.5f) { DashStyle = DashStyle.Dot }) g.DrawPath(p, path);
                }
                using (var b = new SolidBrush(Color.FromArgb((int)(130 * alpha), 170, 160, 200))) g.DrawString("Freilos", R.FTiny, b, x + 10, y + (h - 18) / 2);
                g.Restore(gs);
                return;
            }

            Entrant en = tour.E[v];
            CharDef c = CharDef.All[en.Char];
            // ausgeschieden in dieser Runde?
            float outK = 0;
            bool won = false;
            if (!champ)
            {
                int pv = tour.Slot[r + 1][i / 2], pt = tour.FillTime[r + 1][i / 2];
                if (pv >= 0 && now >= pt)
                {
                    if (pv == v) won = true;
                    else outK = Clamp01((now - pt) / (float)OutFrames);
                }
            }
            float pulse = 0.5f + 0.5f * (float)Math.Sin(now * 0.15);
            if (current && now >= TourAnimEnd())
                using (var path = Renderer.RoundRect(x - 5, y - 5, w + 10, h + 10, 12))
                using (var b = new SolidBrush(Color.FromArgb((int)(40 + 70 * pulse), Gold))) g.FillPath(b, path);
            if (champ)
                using (var b = new SolidBrush(Color.FromArgb((int)(60 + 50 * pulse), Gold))) g.FillEllipse(b, x - 30, y - 24, w + 60, h + 48);

            using (var path = Renderer.RoundRect(x, y, w, h, 8))
            {
                if (champ)
                    using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), Color.FromArgb(255, 250, 220, 120), Color.FromArgb(255, 190, 120, 30), LinearGradientMode.Vertical)) g.FillPath(b, path);
                else
                    using (var b = new SolidBrush(Color.FromArgb((int)(225 * alpha), 18, 10, 34))) g.FillPath(b, path);
                Color border = champ ? Color.White : current ? Color.FromArgb((int)(170 + 85 * pulse), Gold) : won ? c.Glow : Color.FromArgb(170, c.Glow);
                if (outK > 0) border = Mixc(border, Color.FromArgb(150, 110, 110, 120), outK);
                using (var p = new Pen(Color.FromArgb((int)(border.A * alpha), border), champ || current ? 2.5f : won ? 2.2f : 1.6f)) g.DrawPath(p, path);
            }
            float icon = h * 0.024f;
            R.DrawHeadIcon(g, en.Char, x + h * 0.55f, y + h * 0.58f, icon, alpha * (1 - 0.6f * outK));
            Font f = champ ? R.FHudName : tbBoxH < 34 ? R.FTiny : R.FSmall;
            Color tc = champ ? Color.FromArgb(70, 40, 10) : en.Human ? Color.White : Color.FromArgb(215, 215, 235);
            if (outK > 0) tc = Mixc(tc, Color.FromArgb(120, 120, 130), outK);
            var ts = g.MeasureString(en.Name, f);
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * alpha), tc))) g.DrawString(en.Name, f, b, x + h * 1.05f, y + (h - ts.Height) / 2);
            if (!en.Human && !champ && w >= 150)
                using (var b = new SolidBrush(Color.FromArgb((int)(150 * alpha), 180, 180, 210))) g.DrawString("CPU", R.FTiny, b, x + w - 34, y + (h - 18) / 2);
            if (outK > 0)
            {
                // Verlierer: abgedunkelt und durchgestrichen
                using (var path = Renderer.RoundRect(x, y, w, h, 8))
                using (var b = new SolidBrush(Color.FromArgb((int)(110 * outK), 0, 0, 0))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(220, 230, 70, 80), 2.5f)) g.DrawLine(p, x + 6, y + h / 2, x + 6 + (w - 12) * outK, y + h / 2);
            }
            if (r > 0 && k < 1)
                using (var path = Renderer.RoundRect(x, y, w, h, 8))
                using (var b = new SolidBrush(Color.FromArgb((int)(220 * (1 - k)), 255, 255, 255))) g.FillPath(b, path);
            g.Restore(gs);
        }

        void DrawTourney(Graphics g)
        {
            if (tour == null) return;
            using (var b = new SolidBrush(Color.FromArgb(195, 4, 2, 12))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            TbLayout();
            int now = frame;
            int nr, nm;
            bool has = tour.NextMatch(out nr, out nm);
            bool idle = now >= TourAnimEnd();

            if (tour.Done && !tourShowBracket && now >= tour.FillTime[tour.Rounds][0] + LineFrames + 10)
            {
                DrawChampion(g, now);
                DrawTourItems(g);
                return;
            }

            R.Shadowed(g, "TURNIER", R.FBig, Gold, Renderer.VW / 2f, 8, true);
            string sub = tour.Done ? (now >= tour.FillTime[tour.Rounds][0] + LineFrames ? tour.E[tour.Champion].Name + " gewinnt das Turnier!" : "Das Finale ist entschieden ...")
                : has ? Tournament.RoundName(tour.Size, nr) + "  ·  Match " + (nm + 1) + " von " + tour.Slot[nr].Length / 2 : "";
            R.Shadowed(g, sub, R.FSmall, Color.FromArgb(225, 215, 245), Renderer.VW / 2f, 56, true);

            // Rundennamen über den Spalten
            for (int r = 0; r < tour.Rounds; r++)
                for (int side = 0; side < 2; side++)
                {
                    int i = side == 0 ? 0 : tour.Slot[r].Length - 1;
                    string rn = Tournament.RoundName(tour.Size, r);
                    var sz = g.MeasureString(rn, R.FTiny);
                    bool active = has && r == nr;
                    using (var b = new SolidBrush(active ? Gold : Color.FromArgb(150, 190, 180, 220)))
                        g.DrawString(rn, R.FTiny, b, TbX(r, i) + (tbBoxW - sz.Width) / 2, 92);
                }

            for (int r = 0; r < tour.Rounds; r++)
                for (int i = 0; i < tour.Slot[r].Length; i++) DrawConnector(g, r, i, now);
            for (int r = 0; r <= tour.Rounds; r++)
                for (int i = 0; i < tour.Slot[r].Length; i++) DrawSlot(g, r, i, now, has && r == nr && i / 2 == nm);

            // VS-Marke an der Stelle, wo sich die beiden Linien des nächsten Matches treffen
            if (has && idle)
            {
                PointF[] pa = TbPath(nr, 2 * nm), pb = TbPath(nr, 2 * nm + 1);
                float vx = nr == tour.Rounds - 1 ? Renderer.VW / 2f : pa[1].X, vy = nr == tour.Rounds - 1 ? pa[0].Y : (pa[0].Y + pb[0].Y) / 2;
                float s = 1 + 0.12f * (float)Math.Sin(now * 0.2);
                float rr = 17 * s;
                using (var b = new SolidBrush(Color.FromArgb(235, 40, 14, 30))) g.FillEllipse(b, vx - rr, vy - rr, rr * 2, rr * 2);
                using (var p = new Pen(Gold, 2.5f)) g.DrawEllipse(p, vx - rr, vy - rr, rr * 2, rr * 2);
                var vs = g.MeasureString("VS", R.FTiny);
                using (var b = new SolidBrush(Color.White)) g.DrawString("VS", R.FTiny, b, vx - vs.Width / 2, vy - vs.Height / 2);
            }

            if (has) DrawNextMatchCard(g, nr, nm, now);
            DrawTourItems(g);
        }

        // Karte unten: wer gegen wen als Nächstes kämpft
        void DrawNextMatchCard(Graphics g, int r, int m, int now)
        {
            float y = 606;
            Panel(g, 240, y, 800, 52);
            for (int side = 0; side < 2; side++)
            {
                Entrant e = tour.E[tour.Slot[r][2 * m + side]];
                CharDef c = CharDef.All[e.Char];
                float cx = side == 0 ? 300 : 980;
                R.DrawHeadIcon(g, e.Char, cx, y + 30, 0.95f);
                string txt = e.Name, info = c.Name + " · " + CpuText(e.Cpu);
                var sz = g.MeasureString(txt, R.FHudName);
                var si = g.MeasureString(info, R.FTiny);
                float tx = side == 0 ? cx + 28 : cx - 28 - Math.Max(sz.Width, si.Width);
                using (var b = new SolidBrush(c.Glow)) g.DrawString(txt, R.FHudName, b, tx, y + 5);
                using (var b = new SolidBrush(Color.FromArgb(190, 210, 205, 230))) g.DrawString(info, R.FTiny, b, tx, y + 27);
            }
            bool cpuOnly = CpuOnly(r, m);
            string mid = cpuOnly && tour.AutoCpu ? "wird ausgerechnet" + new string('.', 1 + now / 15 % 3) : "VS";
            Font f = mid == "VS" ? R.FMed : R.FSmall;
            R.Shadowed(g, mid, f, mid == "VS" ? Gold : Color.FromArgb(220, 220, 240), Renderer.VW / 2f, y + (mid == "VS" ? 9 : 14), true);
        }

        void DrawTourItems(Graphics g)
        {
            int n = items.Count;
            float w = n > 2 ? 300 : 340, gap = 20, x0 = Renderer.VW / 2f - (n * w + (n - 1) * gap) / 2;
            for (int i = 0; i < n; i++) DrawItems(g, x0 + i * (w + gap), 668, w, 42, 0, i, 1);
        }

        // ================================================================ Siegerehrung

        void DrawChampion(Graphics g, int now)
        {
            Entrant e = tour.E[tour.Champion];
            CharDef c = CharDef.All[e.Char];
            float k = Clamp01((now - tour.FillTime[tour.Rounds][0] - LineFrames - 10) / 40f);
            float cx = Renderer.VW / 2f;
            // Lichtstrahlen
            var gs = g.Save();
            g.TranslateTransform(cx - 150, 380);
            g.RotateTransform(now * 0.4f);
            using (var b = new SolidBrush(Color.FromArgb((int)(45 * k), Gold)))
                for (int i = 0; i < 14; i++)
                {
                    double a0 = i * Math.PI * 2 / 14, a1 = a0 + 0.12;
                    g.FillPolygon(b, new[] { new PointF(0, 0), new PointF((float)Math.Cos(a0) * 700, (float)Math.Sin(a0) * 700), new PointF((float)Math.Cos(a1) * 700, (float)Math.Sin(a1) * 700) });
                }
            g.Restore(gs);

            R.Shadowed(g, "TURNIERSIEGER", R.FTitle, Color.FromArgb((int)(255 * k), Gold), cx, 24, true);
            R.Shadowed(g, e.Name, R.FBig, Color.FromArgb((int)(255 * k), c.Glow), cx, 108, true);
            R.Shadowed(g, c.Name + " · " + c.TypeName + "  ·  " + tour.E.Count + " Teilnehmer  ·  " + ChampWins() + (ChampWins() == 1 ? " Sieg" : " Siege") + " in Folge",
                R.FSmall, Color.FromArgb((int)(230 * k), 230, 225, 245), cx, 160, true);

            float s = EaseOutBack(k);
            DrawTrophy(g, cx - 150, 430, 1.25f * s);
            if (k > 0.05f) R.DrawPortrait(g, e.Char, cx + 170, 590, 2.3f * s, now, false);
            DrawConfetti(g);
        }

        int ChampWins()
        {
            int v = tour.Champion, n = 0;
            for (int r = 0; r < tour.Rounds; r++)
                for (int i = 0; i < tour.Slot[r].Length; i += 2)
                    if ((tour.Slot[r][i] == v || tour.Slot[r][i + 1] == v) && tour.Slot[r][i] >= 0 && tour.Slot[r][i + 1] >= 0) n++;
            return n;
        }

        static void DrawTrophy(Graphics g, float cx, float cy, float s)
        {
            if (s <= 0.01f) return;
            var gs = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(s, s);
            using (var outline = new Pen(Color.FromArgb(110, 60, 10), 4) { LineJoin = LineJoin.Round })
            using (var gold = new LinearGradientBrush(new PointF(-70, 0), new PointF(70, 0), Color.FromArgb(255, 196, 120, 30), Color.FromArgb(255, 255, 236, 140)))
            using (var dark = new SolidBrush(Color.FromArgb(200, 140, 40)))
            {
                gold.SetBlendTriangularShape(0.35f);
                // Henkel
                using (var hp = new Pen(Color.FromArgb(110, 60, 10), 16)) { g.DrawArc(hp, -102, -118, 62, 76, 90, 180); g.DrawArc(hp, 40, -118, 62, 76, 270, 180); }
                using (var hp = new Pen(Color.FromArgb(240, 200, 80), 9)) { g.DrawArc(hp, -102, -118, 62, 76, 90, 180); g.DrawArc(hp, 40, -118, 62, 76, 270, 180); }
                // Sockel und Stiel
                using (var path = Renderer.RoundRect(-58, 52, 116, 34, 6)) { g.FillPath(dark, path); g.DrawPath(outline, path); }
                using (var path = Renderer.RoundRect(-36, 40, 72, 16, 4)) { g.FillPath(gold, path); g.DrawPath(outline, path); }
                g.FillRectangle(gold, -11, 0, 22, 42);
                g.DrawRectangle(outline, -11, 0, 22, 42);
                // Pokal
                using (var cup = new GraphicsPath())
                {
                    cup.AddLine(-70, -128, 70, -128);
                    cup.AddBezier(70, -128, 68, -40, 36, 2, 0, 4);
                    cup.AddBezier(0, 4, -36, 2, -68, -40, -70, -128);
                    cup.CloseFigure();
                    g.FillPath(gold, cup);
                    g.DrawPath(outline, cup);
                }
                using (var b = new SolidBrush(Color.FromArgb(255, 250, 200))) g.FillEllipse(b, -70, -136, 140, 16);
                g.DrawEllipse(outline, -70, -136, 140, 16);
                // Stern und Glanz
                var star = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5;
                    float rr = i % 2 == 0 ? 22 : 9;
                    star[i] = new PointF((float)Math.Cos(a) * rr, -64 + (float)Math.Sin(a) * rr);
                }
                using (var b = new SolidBrush(Color.FromArgb(255, 255, 250, 220))) g.FillPolygon(b, star);
                using (var p = new Pen(Color.FromArgb(170, 110, 20), 2)) g.DrawPolygon(p, star);
                using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillEllipse(b, -52, -118, 20, 70);
                using (var plate = new SolidBrush(Color.FromArgb(255, 236, 160))) g.FillRectangle(plate, -30, 62, 60, 14);
            }
            g.Restore(gs);
        }
    }
}
