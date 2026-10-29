using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sensensturm
{
    // Werte eines Matches, die nicht im Spielzustand stehen (aus den Ereignissen gezählt), und der Schadensverlauf fürs Diagramm.
    // Funktioniert lokal, als Host, als Client/Zuschauer (Ereignisse vom Host) und in Wiederholungen gleich.
    class MatchStats
    {
        public const int SampleEvery = 30;
        public readonly int[] Perfects = new int[2], Ults = new int[2];
        public readonly bool[] WasBehind = new bool[2];          // lag zwischendurch mit weniger Leben hinten
        public readonly List<int> Ticks = new List<int>();        // Zeitpunkte der Messpunkte (Kampf-Frames)
        public readonly List<float>[] Dmg = { new List<float>(), new List<float>() }; // verursachter Schaden je Messpunkt
        public readonly List<int> KoTick = new List<int>(), KoBy = new List<int>();
        public int FightTicks;

        public void OnEvent(GameEvent e, GameState s)
        {
            int p = Math.Min(1, (int)e.Player);
            switch (e.Type)
            {
                case Ev.Perfect: Perfects[p]++; break;
                case Ev.Ult: Ults[p]++; break;
                case Ev.KO:
                    // Spieler = wer fliegt, V = seine restlichen Leben
                    KoTick.Add(FightTicks);
                    KoBy.Add(1 - p);
                    if ((int)e.V < s.p[1 - p].stocks) WasBehind[p] = true;
                    break;
            }
        }

        public void Sample(GameState s)
        {
            if (s.phase != GameState.Fight) return;
            if (FightTicks % SampleEvery == 0) Add(s);
            FightTicks++;
        }

        // letzter Messpunkt beim Spielende
        public void Finish(GameState s)
        {
            if (Ticks.Count == 0 || Ticks[Ticks.Count - 1] != FightTicks) Add(s);
        }

        void Add(GameState s)
        {
            Ticks.Add(FightTicks);
            for (int i = 0; i < 2; i++) Dmg[i].Add(s.p[i].statDamage);
        }
    }

    // Siegerbildschirm: Siegerpose mit Spruch, Auszeichnungen, Werte im Vergleich, Schadensverlauf; dazu die Knöpfe
    // für Revanche (online mit Abstimmung beider Spieler), Kämpferwechsel und Wiederholungen.
    public partial class Game
    {
        MatchStats stats = new MatchStats();
        int resultsT; // Frames seit Beginn des Ergebnisbildschirms (Animation)
        Font fQuote;

        // Alle Spielereignisse laufen hierüber: Effekte/Klang und Statistik
        void OnEvent(GameEvent e)
        {
            R.OnEvent(e, state);
            stats.OnEvent(e, state);
        }

        static readonly string[][] Quotes =
        {
            new[] { "Deine Seele gehört jetzt mir.", "Der Mond hat zugesehen – und gelacht.", "Die Sense mäht, was das Schicksal sät." },
            new[] { "Kalt wie der Frost, scharf wie die Klinge.", "Erstarrt? Dann bleib liegen.", "Der Winter fragt nicht. Er nimmt." },
            new[] { "Stein bricht nicht.", "Das war nur ein Kieselstein.", "Berge weichen nicht zurück." },
            new[] { "Kein Ziel ist zu weit für meine Sterne.", "Zu langsam, zu nah, zu schade.", "Ich hab dich getroffen, bevor du gesprungen bist." },
        };

        // Auszeichnungen eines Spielers für dieses Match (Name, Erklärung)
        List<string[]> Awards(int i)
        {
            PlayerState P = state.p[i], O = state.p[1 - i];
            var a = new List<string[]>();
            bool won = state.winner == i;
            if (won && P.stocks == state.stocks) a.Add(new[] { "Makellos", "kein Leben verloren" });
            if (won && stats.WasBehind[i]) a.Add(new[] { "Aufholjagd", "Rückstand gedreht" });
            if (won && stats.FightTicks < 60 * 60) a.Add(new[] { "Blitzsieg", "unter einer Minute" });
            if (P.statBestCombo >= 5 && P.statBestCombo > O.statBestCombo) a.Add(new[] { "Combo-Meister", P.statBestCombo + " Treffer am Stück" });
            if (stats.Perfects[i] >= 3 && stats.Perfects[i] > stats.Perfects[1 - i]) a.Add(new[] { "Perfektionist", stats.Perfects[i] + "× PERFEKT" });
            if (P.statParries >= 3 && P.statParries > O.statParries) a.Add(new[] { "Eiserne Wand", P.statParries + " Paraden" });
            if (stats.Ults[i] >= 2) a.Add(new[] { "Ultimative Macht", stats.Ults[i] + " Ultimates" });
            return a;
        }

        // ================================================================ Ablauf

        // einmal beim Erscheinen des Ergebnisbildschirms
        void EnterResults()
        {
            resultsT = 0;
            stats.Finish(state);
            if (mode == Mode.Host && host != null)
            {
                host.Results = true;
                host.HostVote = host.ClientVote = false;
                host.HostChar = hostChar;
                host.ClientNextChar = state.p[1].charId;
            }
            if (mode == Mode.Client && client != null && !client.Spectator)
            {
                client.InResults = true;
                client.MyVote = false;
                client.NextChar = state.p[1].charId;
            }
            Audio.Play(Sfx.Go, 0.7f, 1.2f);
        }

        void BuildResultItems()
        {
            bool canReplay = lastReplay != null;
            if (tourMatch) Add("Weiter zum Turnierbaum", null, TourMatchDone, null);
            else if (mode == Mode.Local)
            {
                Add("Revanche", null, StartLocal, null);
                Add("Hauptmenü", null, () => { state = null; Go(Scr.Main); }, null);
            }
            else if (mode == Mode.Host && host != null)
            {
                Add(host.HostVote ? "Revanche – bereit" : "Revanche", null, () => host.HostVote = !host.HostVote, null);
                Add("<  " + CharDef.All[hostChar].Name + "  >", null, null, d => { hostChar = CharDef.Next(hostChar, d, host.ClientNextChar); host.HostChar = hostChar; });
                Add("Zur Lobby", null, () => BackToHostLobby(null), null);
            }
            else if (mode == Mode.Client && client != null && !client.Spectator)
            {
                Add(client.MyVote ? "Revanche – bereit" : "Revanche", null, () => client.MyVote = !client.MyVote, null);
                Add("<  " + CharDef.All[client.NextChar].Name + "  >", null, null, d =>
                {
                    client.NextChar = CharDef.Next(client.NextChar, d, client.HostNextChar);
                    client.WantChar = client.NextChar;
                    Settings.P1Char = client.NextChar;
                });
                Add("Verbindung trennen", null, () => { LeaveClient(); state = null; Go(Scr.Multiplayer); }, null);
            }
            else Add("Zuschauen beenden", null, () => { LeaveClient(); state = null; Go(Scr.Multiplayer); }, null);

            if (canReplay)
            {
                items.Add(new Item { Label = "Wiederholung ansehen", Ok = WatchLastReplay, Small = true });
                items.Add(new Item { Label = "Speichern", Ok = SaveLastReplay, Small = true });
            }
            else if (mode == Mode.Client && client != null && client.ReplayProgress >= 0)
                items.Add(new Item { Label = "Wiederholung lädt ... " + (int)(client.ReplayProgress * 100) + " %", Small = true, Enabled = false });
        }

        // Revanche online: startet, sobald beide zugestimmt haben (der Host entscheidet)
        bool TryStartRematch()
        {
            if (mode != Mode.Host || host == null || !host.HostVote || !host.ClientVote) return false;
            host.ClientChar = host.ClientNextChar;
            StartHostMatch();
            return true;
        }

        // Client/Zuschauer: fertig geladene Wiederholung vom Host übernehmen
        void TakeClientReplay()
        {
            if (client == null || client.ReplayData == null) return;
            byte[] d = client.ReplayData;
            client.ReplayData = null;
            try { lastReplay = Replay.FromBytes(d); }
            catch { lastReplay = null; }
        }

        string ResultStatus(out Color c)
        {
            c = Color.FromArgb(255, 225, 140);
            if (mode == Mode.Host && host != null)
            {
                string who = host.ClientName;
                string sp = host.Spectators.Count > 0 ? "   ·   " + host.Spectators.Count + " Zuschauer" : "";
                if (host.ClientVote && !host.HostVote) return who + " will eine Revanche!" + sp;
                if (host.HostVote && !host.ClientVote) { c = Color.FromArgb(255, 200, 120); return "Warte auf " + who + " ..." + sp; }
                c = Color.FromArgb(200, 210, 200, 235);
                return "Revanche: beide müssen zustimmen" + sp;
            }
            if (mode == Mode.Client && client != null)
            {
                if (client.Spectator) { c = Color.FromArgb(200, 210, 200, 235); return "Du schaust zu – der Host startet das nächste Match."; }
                if (client.HostVote && !client.MyVote) return client.HostName + " will eine Revanche!";
                if (client.MyVote && !client.HostVote) { c = Color.FromArgb(255, 200, 120); return "Warte auf " + client.HostName + " ..."; }
                c = Color.FromArgb(200, 210, 200, 235);
                return "Revanche: beide müssen zustimmen";
            }
            return null;
        }

        // ================================================================ Zeichnen

        void DrawResults(Graphics g)
        {
            int t = resultsT;
            float k = Clamp01(t / 24f);
            using (var b = new SolidBrush(Color.FromArgb((int)(210 * k), 6, 3, 14))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            int w = state.winner;
            float cx = 300;

            // Überschrift
            string head;
            Color hc = Color.White;
            if (w < 0) head = "UNENTSCHIEDEN";
            else
            {
                head = (names[w] ?? CharDef.All[state.p[w].charId].Name).ToUpper() + " GEWINNT!";
                hc = CharDef.Look(state.p[w].charId, state.p[w].skin).Glow;
            }
            float drop = (1 - EaseOutBack(Clamp01(t / 20f))) * -60;
            R.Shadowed(g, head, R.FTitle, Color.FromArgb((int)(255 * k), hc), Renderer.VW / 2f, 14 + drop, true);
            if (tourMatch && tour != null && w >= 0)
            {
                string next = tourR + 1 >= tour.Rounds ? "GEWINNT DAS TURNIER!" : "zieht ins " + Tournament.RoundName(tour.Size, tourR + 1) + " ein!";
                R.Shadowed(g, next, R.FMed, Color.FromArgb((int)(255 * k), Gold), Renderer.VW / 2f, 94, true);
            }

            if (w >= 0)
            {
                PlayerState P = state.p[w];
                CharDef c = CharDef.Look(P.charId, P.skin);
                float pk = Clamp01((t - 6) / 26f);
                // Lichtstrahlen hinter dem Sieger
                var gs = g.Save();
                g.TranslateTransform(cx, 410);
                g.RotateTransform(t * 0.35f);
                using (var b = new SolidBrush(Color.FromArgb((int)(38 * pk), c.Glow)))
                    for (int i = 0; i < 12; i++)
                    {
                        double a0 = i * Math.PI * 2 / 12, a1 = a0 + 0.16;
                        g.FillPolygon(b, new[] { new PointF(0, 0), new PointF((float)Math.Cos(a0) * 420, (float)Math.Sin(a0) * 420), new PointF((float)Math.Cos(a1) * 420, (float)Math.Sin(a1) * 420) });
                    }
                g.Restore(gs);
                float size = c.Body == Body.Golem ? 1.5f : 1.75f; // der Golem ist deutlich größer
                if (pk > 0.02f) R.DrawVictory(g, P.charId, cx, 552, size * EaseOutBack(pk), t, P.skin);
                // Siegesspruch in einer Sprechblase
                string[] qs = Quotes[P.charId];
                DrawQuote(g, "„" + qs[(state.tick / 13) % qs.Length] + "“", cx, 112, Clamp01((t - 24) / 16f), c.Glow);
            }
            else
            {
                float pk = Clamp01((t - 6) / 26f);
                for (int i = 0; i < 2; i++)
                    if (pk > 0.02f) R.DrawPortrait(g, state.p[i].charId, cx + (i == 0 ? -95 : 95), 510, (i == 0 ? 1.5f : -1.5f) * EaseOutBack(pk), frame + i * 30, false, state.p[i].skin);
                DrawQuote(g, "Gleich viele Leben und gleicher Schaden – niemand gibt nach.", cx, 112, Clamp01((t - 24) / 16f), Color.White);
            }

            // Auszeichnungen beider Spieler (Sieger zuerst)
            {
                float ay = 564;
                int first = w >= 0 ? w : 0;
                for (int n = 0; n < 2; n++)
                {
                    int i = n == 0 ? first : 1 - first;
                    List<string[]> aw = Awards(i);
                    if (aw.Count == 0) continue;
                    int max = n == 0 ? 3 : 2;
                    if (aw.Count > max) aw.RemoveRange(max, aw.Count - max);
                    float a = Clamp01((t - 40 - n * 12) / 14f);
                    CharDef c = CharDef.Look(state.p[i].charId, state.p[i].skin);
                    DrawAwardRow(g, aw, cx, ay, a, c.Glow, n == 0 && w >= 0);
                    ay += 30;
                }
            }

            DrawStatsPanel(g, t);

            // Status (Revanche online) und Knöpfe
            Color sc;
            string status = ResultStatus(out sc);
            if (status != null)
            {
                float pul = 0.75f + 0.25f * (float)Math.Sin(frame * 0.12);
                R.Shadowed(g, status, R.FSmall, Color.FromArgb((int)(255 * pul), sc), 920, 600, true);
            }
            DrawResultButtons(g);
        }

        void DrawQuote(Graphics g, string text, float cx, float y, float a, Color glow)
        {
            if (a <= 0.01f) return;
            if (fQuote == null) fQuote = new Font("Segoe UI", 19, FontStyle.Italic, GraphicsUnit.Pixel);
            float w = 470;
            SizeF sz = g.MeasureString(text, fQuote, (int)(w - 40));
            float h = sz.Height + 26, x = cx - w / 2;
            using (var path = Renderer.RoundRect(x, y, w, h, 16))
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(215 * a), 16, 10, 30))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb((int)(200 * a), glow), 2)) g.DrawPath(p, path);
            }
            // Spitze der Sprechblase zum Sieger
            using (var b = new SolidBrush(Color.FromArgb((int)(215 * a), 16, 10, 30)))
                g.FillPolygon(b, new[] { new PointF(cx - 14, y + h - 1), new PointF(cx + 14, y + h - 1), new PointF(cx + 4, y + h + 18) });
            using (var p = new Pen(Color.FromArgb((int)(200 * a), glow), 2))
            {
                g.DrawLine(p, cx - 14, y + h, cx + 4, y + h + 18);
                g.DrawLine(p, cx + 14, y + h, cx + 4, y + h + 18);
            }
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), 240, 234, 252)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(text, fQuote, b, new RectangleF(x + 20, y + 13, w - 40, sz.Height + 4), sf);
        }

        void DrawAwardRow(Graphics g, List<string[]> aw, float cx, float y, float a, Color glow, bool winner)
        {
            if (a <= 0.01f) return;
            var widths = new float[aw.Count];
            float total = 0;
            for (int i = 0; i < aw.Count; i++) { widths[i] = g.MeasureString(aw[i][0], R.FHudName).Width + 30; total += widths[i] + (i > 0 ? 10 : 0); }
            float x = cx - total / 2;
            for (int i = 0; i < aw.Count; i++)
            {
                using (var path = Renderer.RoundRect(x, y, widths[i], 26, 13))
                {
                    using (var b = new SolidBrush(Color.FromArgb((int)(220 * a), winner ? 54 : 26, winner ? 40 : 18, winner ? 12 : 40))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb((int)(230 * a), winner ? Gold : glow), 1.8f)) g.DrawPath(p, path);
                }
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), winner ? Gold : glow))) g.FillEllipse(b, x + 10, y + 9, 8, 8);
                Line(g, aw[i][0], x + 22, y + 2, Color.FromArgb((int)(255 * a), 245, 240, 255), R.FHudName);
                x += widths[i] + 10;
            }
        }

        void DrawStatsPanel(Graphics g, int t)
        {
            float a = Clamp01((t - 10) / 18f);
            if (a <= 0.01f) return;
            float px = 600, py = 128, pw = 640, ph = 452;
            var gs = g.Save();
            g.TranslateTransform((1 - EaseOutBack(a)) * 60, 0);
            Panel(g, px, py, pw, ph);
            float mid = px + pw / 2;
            PlayerState[] P = state.p;
            // Kopfzeile: beide Kämpfer
            for (int i = 0; i < 2; i++)
            {
                CharDef c = CharDef.Look(P[i].charId, P[i].skin);
                string nm = names[i] ?? c.Name;
                bool win = state.winner == i;
                if (i == 0)
                {
                    R.DrawHeadIcon(g, P[i].charId, px + 36, py + 34, 1.3f, 1f, P[i].skin);
                    Line(g, nm, px + 64, py + 12, c.Glow, R.FHudName);
                    Line(g, win ? "SIEGER" : c.Name, px + 64, py + 34, win ? Gold : Color.FromArgb(170, 200, 190, 225), R.FTiny);
                }
                else
                {
                    R.DrawHeadIcon(g, P[i].charId, px + pw - 36, py + 34, 1.3f, 1f, P[i].skin);
                    float nw = g.MeasureString(nm, R.FHudName).Width, sw = g.MeasureString(win ? "SIEGER" : c.Name, R.FTiny).Width;
                    Line(g, nm, px + pw - 64 - nw, py + 12, c.Glow, R.FHudName);
                    Line(g, win ? "SIEGER" : c.Name, px + pw - 64 - sw, py + 34, win ? Gold : Color.FromArgb(170, 200, 190, 225), R.FTiny);
                }
            }
            int secs = stats.FightTicks / 60;
            R.Shadowed(g, "Kampfdauer " + secs / 60 + ":" + (secs % 60).ToString("00"), R.FTiny, Color.FromArgb(200, 210, 200, 235), mid, py + 22, true);

            // Werte im Vergleich: Zahl außen, Balken zur Mitte hin, der bessere Wert leuchtet
            var rows = new[]
            {
                new { L = "Schaden verursacht", A = (float)(int)P[0].statDamage, B = (float)(int)P[1].statDamage, Suffix = " %" },
                new { L = "Treffer", A = (float)P[0].statHits, B = (float)P[1].statHits, Suffix = "" },
                new { L = "Längste Combo", A = (float)P[0].statBestCombo, B = (float)P[1].statBestCombo, Suffix = "" },
                new { L = "PERFEKT-Treffer", A = (float)stats.Perfects[0], B = (float)stats.Perfects[1], Suffix = "" },
                new { L = "Paraden", A = (float)P[0].statParries, B = (float)P[1].statParries, Suffix = "" },
                new { L = "K.O.s", A = (float)P[0].statKOs, B = (float)P[1].statKOs, Suffix = "" },
                new { L = "Ultimates", A = (float)stats.Ults[0], B = (float)stats.Ults[1], Suffix = "" },
            };
            Color c0 = CharDef.Look(P[0].charId, P[0].skin).Glow, c1 = CharDef.Look(P[1].charId, P[1].skin).Glow;
            float barW = 210;
            for (int r = 0; r < rows.Length; r++)
            {
                float ra = Clamp01((t - 18 - r * 5) / 14f);
                if (ra <= 0) continue;
                float y = py + 66 + r * 36;
                var row = rows[r];
                float max = Math.Max(1, Math.Max(row.A, row.B));
                float grow = 1 - (1 - ra) * (1 - ra);
                R.Shadowed(g, row.L, R.FTiny, Color.FromArgb((int)(220 * ra), 205, 200, 228), mid, y - 2, true);
                for (int i = 0; i < 2; i++)
                {
                    float v = i == 0 ? row.A : row.B, o = i == 0 ? row.B : row.A;
                    bool better = v > o;
                    Color col = i == 0 ? c0 : c1;
                    float bw = barW * (v / max) * grow;
                    using (var b = new SolidBrush(Color.FromArgb((int)(55 * ra), 255, 255, 255)))
                        g.FillRectangle(b, i == 0 ? mid - 12 - barW : mid + 12, y + 17, barW, 9);
                    using (var b = new SolidBrush(Color.FromArgb((int)((better ? 255 : 140) * ra), col)))
                        g.FillRectangle(b, i == 0 ? mid - 12 - bw : mid + 12, y + 17, bw, 9);
                    string txt = ((int)Math.Round(v * grow)) + row.Suffix;
                    Color tc = Color.FromArgb((int)(255 * ra), better ? Color.White : Color.FromArgb(185, 180, 205));
                    if (i == 0) Line(g, txt, px + 20, y + 6, tc, R.FHudName);
                    else Line(g, txt, px + pw - 20 - g.MeasureString(txt, R.FHudName).Width, y + 6, tc, R.FHudName);
                }
            }
            DrawDamageChart(g, px + 20, py + 330, pw - 40, 108, Clamp01((t - 50) / 40f), c0, c1);
            g.Restore(gs);
        }

        // Verlauf des verursachten Schadens beider Spieler, K.O.s als Punkte auf der Linie dessen, der sie erzielt hat
        void DrawDamageChart(Graphics g, float x, float y, float w, float h, float reveal, Color c0, Color c1)
        {
            if (reveal <= 0) return;
            Line(g, "SCHADEN IM VERLAUF   ·   Punkte = K.O.", x, y - 4, Color.FromArgb(200, 200, 170, 255), R.FTiny);
            float top = y + 18, bot = y + h;
            using (var p = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) { g.DrawLine(p, x, bot, x + w, bot); g.DrawLine(p, x, top, x + w, top); }
            int n = stats.Ticks.Count;
            if (n < 2) return;
            float maxT = Math.Max(1, stats.Ticks[n - 1]);
            float maxD = 1;
            foreach (List<float> l in stats.Dmg) foreach (float v in l) maxD = Math.Max(maxD, v);
            using (var b = new SolidBrush(Color.FromArgb(150, 200, 195, 225))) g.DrawString(((int)maxD).ToString(), R.FTiny, b, x + w - g.MeasureString(((int)maxD).ToString(), R.FTiny).Width, top - 18);
            Func<int, float, PointF> pt = (tick, d) => new PointF(x + w * tick / maxT, bot - (bot - top) * d / maxD);
            float lim = maxT * reveal;
            var gs = g.Save();
            g.SetClip(new RectangleF(x - 6, top - 6, w * reveal + 12, bot - top + 12), CombineMode.Intersect);
            for (int i = 0; i < 2; i++)
            {
                var pts = new PointF[n];
                for (int j = 0; j < n; j++) pts[j] = pt(stats.Ticks[j], stats.Dmg[i][j]);
                using (var p = new Pen(Color.FromArgb(70, i == 0 ? c0 : c1), 7) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
                using (var p = new Pen(i == 0 ? c0 : c1, 2.6f) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
            }
            // K.O.-Punkte
            for (int j = 0; j < stats.KoTick.Count; j++)
            {
                int by = stats.KoBy[j], tk = stats.KoTick[j];
                if (tk > lim) continue;
                float d = Interp(stats.Ticks, stats.Dmg[by], tk);
                PointF q = pt(tk, d);
                using (var b = new SolidBrush(by == 0 ? c0 : c1)) g.FillEllipse(b, q.X - 6, q.Y - 6, 12, 12);
                using (var p = new Pen(Color.White, 2)) g.DrawEllipse(p, q.X - 6, q.Y - 6, 12, 12);
            }
            g.Restore(gs);
        }

        static float Interp(List<int> ts, List<float> vs, int t)
        {
            for (int i = 1; i < ts.Count; i++)
                if (ts[i] >= t)
                {
                    float f = ts[i] == ts[i - 1] ? 1 : (t - ts[i - 1]) / (float)(ts[i] - ts[i - 1]);
                    return vs[i - 1] + (vs[i] - vs[i - 1]) * f;
                }
            return vs[vs.Count - 1];
        }

        // große Knöpfe in einer Reihe, kleine (Wiederholung) darunter
        void DrawResultButtons(Graphics g)
        {
            while (itemRects.Count < items.Count) itemRects.Add(RectangleF.Empty);
            var big = new List<int>();
            var small = new List<int>();
            for (int i = 0; i < items.Count; i++) (items[i].Small ? small : big).Add(i);
            DrawButtonRow(g, big, 626, 300, 46);
            DrawButtonRow(g, small, 680, 300, 46);
        }

        void DrawButtonRow(Graphics g, List<int> idx, float y, float maxW, float h)
        {
            if (idx.Count == 0) return;
            float gap = 16, w = Math.Min(maxW, (Renderer.VW - 60 - (idx.Count - 1) * gap) / idx.Count);
            float x0 = Renderer.VW / 2f - (idx.Count * w + (idx.Count - 1) * gap) / 2;
            for (int n = 0; n < idx.Count; n++) DrawItems(g, x0 + n * (w + gap), y, w, h, 0, idx[n], 1);
        }
    }
}
