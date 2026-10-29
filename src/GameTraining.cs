using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Sensensturm
{
    // Trainingsmodus: Puppe mit einstellbarem Verhalten, Bilddaten und Frame-Vorteil, Puppen-Aufnahme, Combo-Aufgaben.
    // Keine Leben, kein Zeitlimit, keine Wiederholung (das Spiel greift von außen in den Zustand ein).
    public partial class Game
    {
        bool training;
        readonly TrainingTracker trk = new TrainingTracker();
        Bot dummyBot;
        int dummyBotMode = -1, dummyCalm;
        // Aufnahme der Puppe: der Spieler steuert sie, danach spielt sie die Eingaben in Schleife ab (Verhalten 6)
        bool recording;
        readonly List<int> recHeld = new List<int>(), recPressed = new List<int>();
        int recPlay;
        const int RecMax = 600;
        int trialIndex = -1;        // Index in ComboTrial.For(Waffe des Spielers), -1 = keine Aufgabe
        Keys trKey = Keys.None;     // Hotkey F5/F6/F7, im nächsten Trainings-Tick ausgewertet

        public static readonly string[] DummyNames = { "Stehen", "Springen", "Schild halten", "CPU leicht", "CPU mittel", "CPU schwer", "Aufnahme abspielen" };
        const int DummyPlayback = 6;

        List<ComboTrial> Trials { get { return ComboTrial.For(CharDef.All[state != null ? state.p[0].charId : Settings.P1Char].Weapon); } }
        ComboTrial CurrentTrial { get { var t = Trials; return trialIndex >= 0 && trialIndex < t.Count ? t[trialIndex] : null; } }

        GameState NewTrainingState()
        {
            GameState s = GameState.NewMatch(Settings.P1Char, Settings.TrainChar, 3, 0, Settings.Stage);
            s.phase = GameState.Fight; // kein Countdown
            s.phaseTimer = 0;
            s.p[1].damage = Settings.TrainDamage;
            return s;
        }

        void StartTraining()
        {
            mode = Mode.Local;
            bot = null; bot0 = null;
            tourMatch = false;
            names[0] = Settings.PlayerName;
            names[1] = "Trainingspuppe";
            recording = false;
            recHeld.Clear(); recPressed.Clear();
            if (Settings.TrainDummy == DummyPlayback) Settings.TrainDummy = 0;
            dummyBot = null;
            GameState s = NewTrainingState();
            BeginMatch(s, true);
            trk.Clear();
            trk.Sync(s);
            trk.SetTrial(CurrentTrial);
            Settings.Save();
        }

        // F5: zurück auf die Startpositionen (Bestwerte und Aufnahme bleiben)
        void ResetTraining()
        {
            state = NewTrainingState();
            R.ClearFx();
            R.SnapCamera(state);
            dummyBot = null;
            recPlay = 0;
            trk.Sync(state);
            Audio.Play(Sfx.Select, 0.6f);
        }

        void ToggleRecording()
        {
            if (recording) { StopRecording(); return; }
            recording = true;
            recHeld.Clear(); recPressed.Clear();
            if (Settings.TrainDummy == DummyPlayback) Settings.TrainDummy = 0;
            Audio.Play(Sfx.Beep, 0.8f);
        }

        void StopRecording()
        {
            recording = false;
            recPlay = 0;
            if (recHeld.Count >= 20)
            {
                Settings.TrainDummy = DummyPlayback;
                Toast("Aufnahme (" + (recHeld.Count / 60f).ToString("0.0") + " s) gespeichert – die Puppe spielt sie jetzt ab. F7 = an/aus", false);
            }
            else { recHeld.Clear(); recPressed.Clear(); }
            Audio.Play(Sfx.Select, 0.6f);
        }

        void TogglePlayback()
        {
            if (recHeld.Count == 0) { Toast("Noch keine Aufnahme – F6 startet eine (du steuerst dann die Puppe).", true); return; }
            Settings.TrainDummy = Settings.TrainDummy == DummyPlayback ? 0 : DummyPlayback;
            recPlay = 0;
            Audio.Play(Sfx.Select, 0.6f);
        }

        void DummyInput(out int h, out int p)
        {
            int m = Settings.TrainDummy;
            if (m == DummyPlayback && recHeld.Count > 0)
            {
                if (recPlay >= recHeld.Count) recPlay = 0;
                h = recHeld[recPlay]; p = recPressed[recPlay];
                recPlay++;
                return;
            }
            if (dummyBot == null || dummyBotMode != m)
            {
                dummyBot = new Bot(m >= 3 && m <= 5 ? m - 2 : 0);
                dummyBot.DummyMode = m == 1 ? 1 : m == 2 ? 2 : 0;
                dummyBotMode = m;
            }
            dummyBot.Think(state, 1, out h, out p);
        }

        void TickTraining()
        {
            Keys k = trKey;
            trKey = Keys.None;
            if (k == Keys.F5) ResetTraining();
            else if (k == Keys.F6) ToggleRecording();
            else if (k == Keys.F7) TogglePlayback();

            int h0, p0, h1, p1;
            In.Sample(-1, In.PadIndex(0), out h0, out p0);
            if (recording)
            {
                // während der Aufnahme steuert der Spieler die Puppe, der eigene Kämpfer steht still
                recHeld.Add(h0); recPressed.Add(p0);
                h1 = h0; p1 = p0; h0 = p0 = 0;
                if (recHeld.Count >= RecMax) StopRecording();
            }
            else DummyInput(out h1, out p1);

            Sim.Step(state, new[] { h0, h1 }, new[] { p0, p1 }, evs, false);
            trk.Step(state, evs);
            foreach (GameEvent e in evs) OnEvent(e);
            if (trk.JustCompleted && trk.Trial != null)
            {
                Audio.Play(Sfx.Ult, 0.8f, 1.3f);
                Settings.TrainTrials |= trk.Trial.Bit;
                Settings.Save();
            }

            // Trainingsregeln: unendlich Leben, Puppen-Schaden zurücksetzen, Ultimate auf Wunsch immer voll
            PlayerState A = state.p[0], B = state.p[1];
            A.stocks = B.stocks = state.stocks;
            if (Settings.TrainUlt) A.ult = Tuning.UltMax;
            bool busy = B.state == St.Hitstun || B.state == St.Grabbed || B.state == St.KO || B.hitlag > 0;
            dummyCalm = busy ? 0 : dummyCalm + 1;
            if (B.state == St.Respawn || (Settings.TrainReset && dummyCalm >= 45)) B.damage = Settings.TrainDamage;
            trk.Sync(state);
        }

        // ================================================================ Auswahl

        void BuildTrainingItems()
        {
            Add("Dein Kämpfer", CharDef.All[Settings.P1Char].Name + " (" + CharDef.All[Settings.P1Char].TypeName + ")", null, d => { Settings.P1Char = CharDef.Next(Settings.P1Char, d, -1); trialIndex = -1; });
            Add("Puppe", CharDef.Look(Settings.TrainChar, Settings.TrainChar == Settings.P1Char ? 1 : 0).Name + " (" + CharDef.All[Settings.TrainChar].TypeName + ")", null, d => Settings.TrainChar = CharDef.Next(Settings.TrainChar, d, -1));
            Add("Arena", StageDef.Names[Settings.Stage], null, d => Settings.Stage = Cycle(Settings.Stage, d, StageDef.Count));
            Add("Training starten", null, StartTraining, null);
            Add("Zurück", null, () => Go(Scr.Main), null);
        }

        void DrawTrainingSetup(Graphics g)
        {
            Color info = Color.FromArgb(220, 215, 235), dim = Color.FromArgb(170, 170, 160, 200);
            DrawTitle(g, "Training");
            DrawItems(g, 330, 160, 620, 46, 8);
            DrawCharCard(g, Settings.P1Char, 20, 150, "DU", false);
            DrawCharCard(g, Settings.TrainChar, 960, 150, "TRAININGSPUPPE", true, Settings.TrainChar == Settings.P1Char ? 1 : 0);
            Panel(g, 330, 440, 620, 196);
            Line(g, "Was dich erwartet", 350, 450, Color.FromArgb(200, 200, 170, 255), R.FHudName);
            Line(g, "• Unendlich Leben, kein Zeitlimit – Esc öffnet die Trainingsoptionen", 350, 478, info, R.FTiny);
            Line(g, "• Bilddaten jedes Treffers und der gemessene Frame-Vorteil", 350, 500, info, R.FTiny);
            Line(g, "• Puppe: stehen, springen, Schild, CPU oder deine eigene Aufnahme", 350, 522, info, R.FTiny);
            Line(g, "• Startschaden einstellbar, setzt sich nach jeder Combo zurück", 350, 544, info, R.FTiny);
            Line(g, "• Combo-Aufgaben für jede Waffe (geschaffte werden gespeichert)", 350, 566, info, R.FTiny);
            Line(g, "F5 Positionen zurücksetzen  ·  F6 Puppe aufnehmen  ·  F7 Aufnahme an/aus", 350, 600, dim, R.FTiny);
        }

        // ================================================================ Pausemenü (Trainingsoptionen)

        void BuildTrainingPauseItems()
        {
            var trials = Trials;
            ComboTrial ct = CurrentTrial;
            Add("Weiter", null, () => paused = false, null);
            Add("Puppe", DummyNames[Settings.TrainDummy] + (Settings.TrainDummy == DummyPlayback && recHeld.Count == 0 ? " (leer)" : ""), null,
                d => { Settings.TrainDummy = Cycle(Settings.TrainDummy, d, DummyNames.Length); recPlay = 0; });
            Add("Startschaden der Puppe", Settings.TrainDamage + " %", null, d =>
            {
                Settings.TrainDamage = Math.Max(0, Math.Min(300, Settings.TrainDamage + d * 10));
                state.p[1].damage = Settings.TrainDamage;
                trk.Sync(state);
            });
            Add("Schaden nach Combo zurücksetzen", OnOff(Settings.TrainReset), null, d => Settings.TrainReset = !Settings.TrainReset);
            Add("Ultimate-Leiste immer voll", OnOff(Settings.TrainUlt), null, d => Settings.TrainUlt = !Settings.TrainUlt);
            Add("Hitboxen anzeigen (F3)", OnOff(Settings.ShowHitboxes), null, d => Settings.ShowHitboxes = !Settings.ShowHitboxes);
            Add("Combo-Aufgabe", ct == null ? "Keine" : ct.Name + ((Settings.TrainTrials & ct.Bit) != 0 ? "  (geschafft)" : ""), null, d =>
            {
                trialIndex = (trialIndex + 1 + d + trials.Count + 1) % (trials.Count + 1) - 1;
                trk.SetTrial(CurrentTrial);
            });
            Add("Positionen zurücksetzen (F5)", null, () => { ResetTraining(); paused = false; }, null);
            Add(recording ? "Aufnahme beenden (F6)" : "Puppe aufnehmen (F6)", null, () => { ToggleRecording(); paused = false; }, null);
            Add("Combos ansehen (F1)", null, OpenComboView, null);
            Add("Kämpfer wechseln", null, () => { state = null; Settings.Save(); Go(Scr.Training); }, null);
            Add("Hauptmenü", null, () => { state = null; Settings.Save(); Go(Scr.Main); }, null);
            foreach (Item it in items) it.Small = true;
        }

        void DrawTrainingPause(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            R.Shadowed(g, "TRAININGSOPTIONEN", R.FBig, Color.White, Renderer.VW / 2f, 40, true);
            DrawItems(g, 330, 112, 620, 46, 4);
            R.Shadowed(g, "Links/Rechts ändert Werte · Esc = zurück zum Training", R.FTiny, Color.FromArgb(200, 200, 220), Renderer.VW / 2f, 690, true);
        }

        // ================================================================ Anzeige im Training

        void DrawTrainingHud(Graphics g)
        {
            Color head = Color.FromArgb(200, 200, 170, 255), info = Color.FromArgb(225, 220, 238), dim = Color.FromArgb(175, 185, 178, 210);
            // Kopf: Puppe und Tasten
            Panel(g, 16, 12, 470, 70);
            Line(g, "TRAINING", 32, 18, head, R.FHudName);
            string dummy = DummyNames[Settings.TrainDummy] + (Settings.TrainDummy == DummyPlayback ? " (" + (recHeld.Count / 60f).ToString("0.0") + " s)" : "");
            Line(g, "Puppe: " + dummy + "  ·  Startschaden " + Settings.TrainDamage + " %", 122, 20, info, R.FTiny);
            Line(g, "F5 Reset  ·  F6 Puppe aufnehmen  ·  F7 Aufnahme an/aus  ·  Esc Optionen", 32, 50, dim, R.FTiny);

            // Bilddaten des letzten Treffers
            Panel(g, 16, 90, 470, 150);
            Line(g, "LETZTER TREFFER", 32, 96, head, R.FHudName);
            if (!trk.HasHit) Line(g, "Triff die Puppe, um Schaden, Bilddaten und den Frame-Vorteil zu sehen.", 32, 124, dim, R.FTiny);
            else
            {
                Line(g, trk.LastName, 32, 118, Color.White, R.FMed);
                string frames = trk.LastTotal > 0 ? "Start " + trk.LastStartup + "  ·  Aktiv " + trk.LastActive + "  ·  Gesamt " + trk.LastTotal : "Wurf aus dem Griff";
                Line(g, trk.LastDamage.ToString("0.0") + " %   ·   " + frames, 32, 152, info, R.FTiny);
                string adv; Color ac;
                if (trk.AdvPending) { adv = "…"; ac = dim; }
                else if (!trk.AdvKnown) { adv = "–"; ac = dim; }
                else { adv = (trk.Advantage > 0 ? "+" : "") + trk.Advantage; ac = trk.Advantage > 0 ? Color.FromArgb(140, 255, 160) : trk.Advantage < 0 ? Color.FromArgb(255, 130, 130) : info; }
                Line(g, "Frame-Vorteil", 32, 176, info, R.FTiny);
                Line(g, adv, 132, 170, ac, R.FHudName);
                Line(g, "Combo  " + trk.ComboHits + " Treffer · " + trk.ComboDamage.ToString("0.0") + " %", 210, 176, info, R.FTiny);
                Line(g, "Beste Combo  " + trk.BestHits + " Treffer · " + trk.BestDamage.ToString("0.0") + " %", 32, 206, Color.FromArgb(255, 225, 140), R.FTiny);
            }

            // Combo-Aufgabe
            ComboTrial ct = trk.Trial;
            if (ct != null)
            {
                float x = 864, w = 400, y0 = 30, h = 92 + ct.Steps.Length * 26;
                Panel(g, x, y0, w, h);
                bool done = (Settings.TrainTrials & ct.Bit) != 0;
                Line(g, "COMBO-AUFGABE" + (done ? "  ·  geschafft" : ""), x + 16, y0 + 6, done ? Color.FromArgb(150, 255, 170) : head, R.FHudName);
                Line(g, ct.Name, x + 16, y0 + 28, Color.White, R.FMed);
                string keys = TrialKeys(ct);
                if (keys != null) Line(g, keys, x + 16, y0 + 62, Color.FromArgb(255, 235, 170), R.FTiny);
                for (int i = 0; i < ct.Steps.Length; i++)
                {
                    bool ok = i < trk.TrialStep, next = i == trk.TrialStep;
                    float y = y0 + 88 + i * 26;
                    using (var b = new SolidBrush(ok ? Color.FromArgb(120, 230, 140) : next ? Color.FromArgb(255, 225, 140) : Color.FromArgb(70, 255, 255, 255)))
                        g.FillEllipse(b, x + 18, y + 3, 14, 14);
                    Line(g, (i + 1) + ".  " + ct.StepName(i), x + 42, y, ok ? Color.FromArgb(170, 245, 180) : next ? Color.White : dim, R.FTiny);
                }
            }
            if (trk.CompleteFlash > 0)
            {
                float a = Math.Min(1f, trk.CompleteFlash / 30f);
                R.Shadowed(g, "AUFGABE GESCHAFFT!", R.FTitle, Color.FromArgb((int)(255 * a), 150, 255, 170), Renderer.VW / 2f, 150, true);
            }

            if (recording)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(frame * 0.15);
                Panel(g, 440, 12, 400, 62);
                using (var b = new SolidBrush(Color.FromArgb((int)(140 + 115 * pulse), 235, 50, 60))) g.FillEllipse(b, 458, 26, 16, 16);
                Line(g, "AUFNAHME  " + (recHeld.Count / 60f).ToString("0.0") + " / " + RecMax / 60 + " s", 484, 20, Color.White, R.FHudName);
                Line(g, "Du steuerst jetzt die Puppe  ·  F6 = Stopp", 484, 46, info, R.FTiny);
            }
        }

        // Tastenfolge der Aufgabe aus der Combo-Übersicht (mit der aktuellen Belegung)
        string TrialKeys(ComboTrial ct)
        {
            foreach (string[] row in CombosFor(ct.Weapon))
                if (row[0].StartsWith(ct.Name)) return row[2];
            return null;
        }

        public void DebugTraining(int dummy, int trial) { Settings.TrainDummy = dummy; trialIndex = trial; StartTraining(); }
        public TrainingTracker DebugTracker { get { return trk; } }
    }
}
