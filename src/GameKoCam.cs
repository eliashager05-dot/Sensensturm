using System;
using System.Collections.Generic;

namespace Sensensturm
{
    // Zeitlupe beim letzten K.O.: Das Spiel merkt sich die letzten zwei Sekunden (Zustand + Ereignisse je Frame).
    // Beim entscheidenden K.O. läuft der letzte Treffer und der Flug in Zeitlupe noch einmal ab, die Kamera zoomt auf
    // den Verlierer, dazu Kinobalken. Danach folgt die K.-o.-Explosion in Echtzeit, dann der Siegerbildschirm.
    // Rein optisch: Die Simulation (und damit LAN/Online und Wiederholungen) läuft normal weiter.
    public partial class Game
    {
        const int HistMax = 120;
        readonly GameState[] hist = new GameState[HistMax];
        readonly List<GameEvent>[] histEv = new List<GameEvent>[HistMax];
        int histHead, histCount, histLastTick = -1;
        readonly List<GameEvent> frameEvs = new List<GameEvent>();
        GameEvent koPending;
        readonly List<GameEvent> koDeferred = new List<GameEvent>();
        KoCam koCam;
        public bool DebugKoCamSeen;

        class KoCam
        {
            public GameState[] Frames;
            public List<GameEvent>[] Evs;
            public float T;          // Position in Frames (mit Bruchteil)
            public int End, HitIdx, Victim, Burst = -1;
            public GameEvent Ko;
            public List<GameEvent> Deferred;
            public GameState Shown;
        }

        // der Zustand, der gerade gezeigt wird (in der Zeitlupe ein vergangener)
        GameState ShownState { get { return koCam != null && koCam.Shown != null ? koCam.Shown : state; } }
        bool KoCamActive { get { return koCam != null; } }

        static GameEvent CopyEv(GameEvent e) { return new GameEvent { Id = e.Id, Type = e.Type, Player = e.Player, X = e.X, Y = e.Y, V = e.V }; }

        void ResetKoCam()
        {
            koCam = null;
            koPending = null;
            koDeferred.Clear();
            frameEvs.Clear();
            histCount = 0; histHead = 0; histLastTick = -1;
            Audio.PitchScale = 1f;
            R.TimeScale = 1f; R.CamLocked = false; R.Letterbox = 0;
        }

        // aus OnEvent: true = Ereignis wird (noch) nicht gezeigt
        bool KoCamEvent(GameEvent e)
        {
            if (koCam != null) return true; // Live-Effekte während der Zeitlupe unterdrücken
            if (koPending != null) { koDeferred.Add(CopyEv(e)); return true; }
            if (e.Type == Ev.KO && e.V <= 0 && !training && state != null && state.stocks > 0)
            {
                koPending = CopyEv(e);
                return true;
            }
            frameEvs.Add(CopyEv(e));
            return false;
        }

        // einmal pro Frame nach dem Spielschritt
        void TickKoCam()
        {
            if (scr != Scr.Match || state == null) { if (koCam != null || histCount > 0) ResetKoCam(); return; }
            if (koCam == null)
            {
                if (state.tick != histLastTick || koPending != null)
                {
                    histLastTick = state.tick;
                    hist[histHead] = state.Clone();
                    histEv[histHead] = new List<GameEvent>(frameEvs);
                    histHead = (histHead + 1) % HistMax;
                    histCount = Math.Min(HistMax, histCount + 1);
                }
                frameEvs.Clear();
                if (koPending != null)
                {
                    GameEvent ko = koPending;
                    koPending = null;
                    StartKoCam(ko);
                }
                return;
            }

            KoCam k = koCam;
            // Esc/Start überspringt die Zeitlupe
            if (k.Burst < 0 && In.Menu.Contains(MenuKey.Pause)) { In.Menu.Remove(MenuKey.Pause); k.T = k.End; }
            if (k.Burst < 0)
            {
                int before = (int)k.T;
                // um den letzten Treffer herum sehr langsam, danach schneller hinterher
                float speed = k.T < k.HitIdx + 8 ? 0.25f : 0.6f;
                k.T = Math.Min(k.End, k.T + speed);
                for (int f = before + 1; f <= (int)k.T && f < k.End; f++)
                    foreach (GameEvent e in k.Evs[f]) R.OnEvent(e, k.Frames[f]);
                int i0 = Math.Min(k.End, (int)k.T), i1 = Math.Min(k.End, i0 + 1);
                k.Shown = Lerp(k.Frames[i0], k.Frames[i1], k.T - i0);
                // Kamera: am Treffer zwischen beiden Kämpfern, dann dem Verlierer hinterher
                PlayerState V = k.Shown.p[k.Victim], A = k.Shown.p[1 - k.Victim];
                float prog = Math.Max(0, Math.Min(1, (k.T - k.HitIdx) / 14f));
                float fx = (A.x + V.x) / 2 * (1 - prog) + V.x * prog, fy = ((A.y + V.y) / 2 * (1 - prog) + V.y * prog) - 60;
                R.FocusCamera(fx, fy, 1.25f - 0.15f * prog, 0.14f);
                R.Letterbox = Math.Min(1f, R.Letterbox + 0.08f);
                if (k.T >= k.End)
                {
                    // Explosion in Echtzeit
                    k.Burst = 0;
                    R.TimeScale = 1f;
                    Audio.PitchScale = 1f;
                    k.Shown = k.Frames[k.End];
                    R.OnEvent(k.Ko, k.Frames[k.End]);
                    foreach (GameEvent e in k.Deferred) R.OnEvent(e, state);
                }
                return;
            }

            k.Burst++;
            if (k.Burst < 22)
            {
                R.FocusCamera(k.Ko.X, k.Ko.Y, 1.0f, 0.06f);
                return;
            }
            // zurück zur Live-Kamera, Balken ausblenden
            k.Shown = null;
            R.CamLocked = false;
            R.UpdateCamera(state, 0.08f);
            R.Letterbox = Math.Max(0f, R.Letterbox - 0.05f);
            if (k.Burst >= 48) { koCam = null; R.Letterbox = 0; histCount = 0; }
        }

        void StartKoCam(GameEvent ko)
        {
            int n = histCount;
            var frames = new GameState[n];
            var evl = new List<GameEvent>[n];
            for (int i = 0; i < n; i++)
            {
                int idx = (histHead - n + i + HistMax) % HistMax;
                frames[i] = hist[idx];
                evl[i] = histEv[idx];
            }
            int victim = Math.Min(1, (int)ko.Player);
            int hitIdx = -1;
            for (int i = n - 1; i >= 0 && hitIdx < 0; i--)
                foreach (GameEvent e in evl[i])
                    if (e.Type == Ev.Hit && e.Player == victim && e.V < 2000) { hitIdx = i; break; }
            var deferred = new List<GameEvent>(koDeferred);
            koDeferred.Clear();
            // zu wenig Vorlauf (z. B. Selbstzerstörung direkt nach dem Start): nur die Explosion zeigen
            if (n < 8)
            {
                R.OnEvent(ko, state);
                foreach (GameEvent e in deferred) R.OnEvent(e, state);
                return;
            }
            int start = hitIdx >= 0 ? hitIdx - 6 : n - 30;
            start = Math.Max(0, Math.Max(start, n - 60));
            if (hitIdx < start) hitIdx = start;
            koCam = new KoCam { Frames = frames, Evs = evl, T = start, End = n - 1, HitIdx = hitIdx, Victim = victim, Ko = ko, Deferred = deferred };
            koCam.Shown = frames[start];
            DebugKoCamSeen = true;
            // die Effekte nach dem Treffer gehören in die Zukunft: weg damit, die Zeitlupe spielt sie erneut ab
            R.ClearParticles();
            R.TimeScale = 0.3f;
            R.CamLocked = true;
            Audio.PitchScale = 0.6f;
            // erstes Bild ohne Sprung: Kamera direkt auf den Treffer
            PlayerState V = frames[start].p[victim], A = frames[start].p[1 - victim];
            R.FocusCamera((A.x + V.x) / 2, (A.y + V.y) / 2 - 60, 1.2f, 0.5f);
        }

        public bool DebugKoCamActive { get { return KoCamActive; } }
        public Renderer DebugRenderer { get { return R; } }

        // CPU-Match nur starten (Tests)
        public void DebugCpuMatchStart(int c0, int c1, int stocks, int seed)
        {
            mode = Mode.Local;
            bot0 = new Bot(3, seed); bot = new Bot(3, seed + 1);
            tourMatch = false;
            names[0] = "Spieler 1"; names[1] = "CPU";
            BeginMatch(GameState.NewMatch(c0, c1, stocks, 3, 1));
        }

        // CPU-Match bis zum Start der Zeitlupe (für Tests und Screenshots)
        public bool DebugCpuMatchUntilKoCam(int c0, int c1, int stocks, int seed)
        {
            mode = Mode.Local;
            bot0 = new Bot(3, seed); bot = new Bot(3, seed + 1);
            tourMatch = false;
            names[0] = "Spieler 1"; names[1] = "CPU";
            BeginMatch(GameState.NewMatch(c0, c1, stocks, 3, 1));
            for (int i = 0; i < 60 * 60 * 4 && !KoCamActive; i++) Tick();
            return KoCamActive;
        }

        static GameState Lerp(GameState a, GameState b, float t)
        {
            if (t <= 0.001f || a == b) return a;
            GameState s = a.Clone();
            for (int i = 0; i < 2; i++)
            {
                PlayerState p = s.p[i], q = b.p[i];
                if (q.state == St.KO && p.state != St.KO) continue; // nicht in die Explosion hinein mitteln
                p.x += (q.x - p.x) * t;
                p.y += (q.y - p.y) * t;
                if (p.projOn && q.projOn) { p.projX += (q.projX - p.projX) * t; p.projY += (q.projY - p.projY) * t; }
            }
            return s;
        }
    }
}
