using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Sensensturm
{
    // Wiederholungen: Aufnahme lokaler/gehosteter Matches, Abspielen, Speichern und die Liste gespeicherter Dateien
    public partial class Game
    {
        Replay rec;              // Aufnahme des laufenden Matches
        Replay lastReplay;       // zuletzt beendetes Match
        Replay replay;           // wird gerade abgespielt
        int replayTick, replaySpeed = 1, replayEndWait;
        bool replayPaused;
        byte[] replayTuningBackup;
        GameState replayBackupState;
        MatchStats replayBackupStats;
        readonly string[] replayBackupNames = new string[2];
        Action replayDone;       // nach dem Abspielen
        List<string> replayFiles; // Liste im Menü (null = neu einlesen)
        readonly List<Replay> replayHeaders = new List<Replay>();

        // Aufnahme starten. Nicht als Client (dort fehlen die Eingaben des Hosts, die Aufnahme kommt nach dem Match
        // vom Host) und nicht im Training (das Spiel greift dort von außen in den Zustand ein).
        void BeginRecording()
        {
            rec = mode == Mode.Client || training ? null : Replay.Begin(state, names[0], names[1]);
        }

        void Record(int h0, int p0, int h1, int p1)
        {
            if (rec == null) return;
            rec.Add(h0, p0, h1, p1);
            // kurz nach dem Spielende abschließen (sonst wächst die Aufnahme im Ergebnisbildschirm weiter)
            if (state.phase == GameState.Over && state.phaseTimer >= 100)
            {
                lastReplay = rec;
                rec = null;
                if (mode == Mode.Host && host != null) host.OfferReplay(lastReplay.ToBytes());
            }
        }

        void StartReplay(Replay rp, Action done)
        {
            if (rp == null) return;
            replayTuningBackup = Tuning.ToBytes();
            replayBackupState = state;
            replayBackupStats = stats;
            replayBackupNames[0] = names[0]; replayBackupNames[1] = names[1];
            try
            {
                Tuning.FromBytes(rp.TuningData);
                state = rp.StartState();
            }
            catch
            {
                Tuning.FromBytes(replayTuningBackup);
                state = replayBackupState;
                Toast("Die Wiederholung konnte nicht geladen werden.", true);
                return;
            }
            replay = rp;
            replayDone = done;
            stats = new MatchStats();
            replayTick = 0; replaySpeed = 1; replayEndWait = 0; replayPaused = false;
            names[0] = rp.Name0; names[1] = rp.Name1;
            comboView = false;
            R.ClearFx();
            R.SnapCamera(state);
            scr = Scr.Match;
            In.Menu.Clear();
            Audio.Play(Sfx.Select, 0.6f);
        }

        void EndReplay()
        {
            Tuning.FromBytes(replayTuningBackup);
            replay = null;
            state = replayBackupState;
            stats = replayBackupStats ?? new MatchStats();
            names[0] = replayBackupNames[0]; names[1] = replayBackupNames[1];
            R.ClearFx();
            if (state != null) R.SnapCamera(state);
            In.Menu.Clear();
            Action d = replayDone;
            replayDone = null;
            if (d != null) d();
        }

        // zurück zum Ergebnisbildschirm des gerade beendeten Matches
        void WatchLastReplay()
        {
            StartReplay(lastReplay, () => { scr = Scr.Match; swallowMenu = true; });
        }

        void TickReplay()
        {
            // Client/Zuschauer: startet der Host ein neues Match oder geht zur Lobby, endet die Wiedergabe sofort
            if (mode == Mode.Client && client != null && replayBackupState != null &&
                (client.NewMatch || client.LobbyReceived || client.Status == ClientSession.Failed)) { EndReplay(); return; }
            foreach (MenuKey k in In.Menu)
            {
                if (k == MenuKey.Back || k == MenuKey.Pause) { EndReplay(); return; }
                if (k == MenuKey.Ok) replayPaused = !replayPaused;
                if (k == MenuKey.Right) replaySpeed = Math.Min(4, replaySpeed * 2);
                if (k == MenuKey.Left) replaySpeed = Math.Max(1, replaySpeed / 2);
            }
            In.Menu.Clear();
            if (replayPaused) return;
            for (int i = 0; i < replaySpeed; i++)
            {
                if (replayTick >= replay.Ticks)
                {
                    if (++replayEndWait > 90) { EndReplay(); return; }
                    break;
                }
                int h0, p0, h1, p1;
                replay.Get(replayTick++, out h0, out p0, out h1, out p1);
                evs.Clear();
                Sim.Step(state, new[] { h0, h1 }, new[] { p0, p1 }, evs, false);
                foreach (GameEvent e in evs) OnEvent(e);
                stats.Sample(state);
            }
            R.UpdateCamera(state);
        }

        void DrawReplayOverlay(Graphics g)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(frame * 0.12);
            Panel(g, 20, 14, 330, 62);
            using (var b = new SolidBrush(Color.FromArgb((int)(140 + 115 * pulse), 235, 50, 60))) g.FillEllipse(b, 36, 26, 14, 14);
            Line(g, "WIEDERHOLUNG" + (replayPaused ? "  (Pause)" : replaySpeed > 1 ? "  " + replaySpeed + "×" : ""), 58, 20, Color.White, R.FHudName);
            float t = replay.Ticks > 0 ? Math.Min(1f, replayTick / (float)replay.Ticks) : 1f;
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 36, 50, 296, 6);
            using (var b = new SolidBrush(Color.FromArgb(235, 50, 60))) g.FillRectangle(b, 36, 50, 296 * t, 6);
            int secs = replayTick / 60, total = replay.Ticks / 60;
            Line(g, (secs / 60) + ":" + (secs % 60).ToString("00") + " / " + (total / 60) + ":" + (total % 60).ToString("00"), 36, 56, Color.FromArgb(200, 220, 215, 240), R.FTiny);
            R.Shadowed(g, "Enter = Pause · Links/Rechts = Tempo · Esc = Ende", R.FTiny, Color.FromArgb(200, 220, 215, 240), Renderer.VW / 2f, 84, true);
        }

        void SaveLastReplay()
        {
            if (lastReplay == null) return;
            try
            {
                string path = Path.Combine(Replay.Folder, lastReplay.FileName());
                lastReplay.Save(path);
                replayFiles = null;
                Toast("Wiederholung gespeichert: replays\\" + Path.GetFileName(path), false);
            }
            catch (Exception ex) { Toast("Speichern fehlgeschlagen: " + ex.Message, true); }
        }

        // Test: CPU gegen CPU im Spiel austragen, Aufnahme nachsimulieren und mit dem echten Endzustand vergleichen,
        // danach im Spiel abspielen und zum Ergebnis zurückkehren. Liefert "" bei Erfolg, sonst den Grund.
        public string DebugReplayCheck(string savePath)
        {
            tourCount = 2; draft.AutoCpu = false; draft.Stocks = 1; draft.Minutes = 1;
            draft.E[0].Cpu = 3; draft.E[1].Cpu = 3; RenameDraft(draft);
            StartTournament();
            lastReplay = null;
            StartTourMatch();
            for (int i = 0; i < 60 * 60 * 3 && lastReplay == null; i++) Tick();
            if (lastReplay == null) return "keine Aufnahme";
            Func<GameState, byte[]> bytes = st => { var ms = new MemoryStream(); st.Write(new BinaryWriter(ms)); return ms.ToArray(); };
            byte[] want = bytes(state);
            Func<Replay, string> resim = rp =>
            {
                GameState s = rp.StartState();
                for (int t = 0; t < rp.Ticks; t++)
                {
                    int h0, p0, h1, p1;
                    rp.Get(t, out h0, out p0, out h1, out p1);
                    Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, null, false);
                }
                byte[] got = bytes(s);
                if (got.Length != want.Length) return "Zustandsgröße weicht ab";
                for (int k = 0; k < got.Length; k++) if (got[k] != want[k]) return "Zustand weicht ab (Byte " + k + ")";
                return "";
            };
            string err = resim(lastReplay);
            if (err != "") return "Nachsimulation: " + err;
            lastReplay.Save(savePath);
            err = resim(Replay.Load(savePath));
            if (err != "") return "nach Speichern/Laden: " + err;
            GameState orig = state;
            WatchLastReplay();
            if (replay == null) return "Wiedergabe startet nicht";
            replaySpeed = 4;
            for (int i = 0; i < 60 * 60 && replay != null; i++) { replaySpeed = 4; Tick(); }
            if (replay != null) return "Wiedergabe endet nicht";
            if (state != orig || scr != Scr.Match) return "nicht zum Ergebnis zurückgekehrt";
            return "";
        }

        public void DebugWatchLastReplay() { WatchLastReplay(); }

        // ================================================================ Liste gespeicherter Wiederholungen

        void LoadReplayList()
        {
            replayFiles = new List<string>();
            replayHeaders.Clear();
            try
            {
                if (!Directory.Exists(Replay.Folder)) return;
                var files = new List<string>(Directory.GetFiles(Replay.Folder, "*.ssr"));
                files.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                foreach (string f in files)
                {
                    if (replayFiles.Count >= 10) break;
                    Replay h;
                    try { h = Replay.Load(f, true); }
                    catch { h = null; } // andere Spielversion oder beschädigt
                    replayFiles.Add(f);
                    replayHeaders.Add(h);
                }
            }
            catch { }
        }

        void BuildReplayItems()
        {
            if (replayFiles == null) LoadReplayList();
            for (int i = 0; i < replayFiles.Count; i++)
            {
                string f = replayFiles[i];
                Replay h = replayHeaders[i];
                if (h == null) { Add(Path.GetFileNameWithoutExtension(f), "andere Version", null, null, false); continue; }
                items.Add(new Item
                {
                    Label = h.Name0 + "  vs  " + h.Name1, Value = h.Date.ToString("dd.MM.yyyy  HH:mm"), Small = true,
                    Ok = () =>
                    {
                        Replay rp;
                        try { rp = Replay.Load(f); }
                        catch (Exception ex) { Toast(ex.Message, true); return; }
                        StartReplay(rp, () => { state = null; Go(Scr.Replays); });
                    }
                });
            }
            Add("Ordner öffnen", null, () => { try { Directory.CreateDirectory(Replay.Folder); Process.Start("explorer.exe", "\"" + Replay.Folder + "\""); } catch { } }, null);
            Add("Zurück", null, () => Go(Scr.Extras), null);
        }

        void DrawReplayList(Graphics g)
        {
            DrawTitle(g, "Wiederholungen");
            if (replayFiles != null && replayFiles.Count == 0)
                R.Shadowed(g, "Noch keine gespeicherten Wiederholungen. Nach einem Match: \"Wiederholung speichern\".", R.FSmall, Color.FromArgb(200, 210, 200, 235), Renderer.VW / 2f, 200, true);
            int n = items.Count - 2;
            DrawItems(g, 290, 168, 700, 44, 4, 0, n);
            DrawItems(g, 290, n > 0 ? 168 + n * 38.3f + 14 : 250, 700, 42, 6, n, 2);
        }
    }
}
