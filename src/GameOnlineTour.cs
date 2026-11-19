using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Sensensturm
{
    // Online-/LAN-Turnier über mehrere PCs.
    // Der Host öffnet eine Turnier-Lobby (Raum-Code oder LAN), alle Beigetretenen sind Teilnehmer und wählen ihren Kämpfer.
    // Freie Plätze füllen CPUs. Der Host-PC rechnet jedes Match: Seine eigenen Eingaben, die der beiden Spieler an anderen
    // PCs (je eine Eingabe-Warteschlange) oder eine CPU. Alle anderen Teilnehmer schauen live zu.
    // Turnierbaum, Lobby und laufende Paarung schickt der Host als TourInfo an alle; die Clients bauen daraus den Baum nach.
    public partial class Game
    {
        bool tourOnline;          // Host: das laufende Turnier/die Lobby ist ein Online-/LAN-Turnier
        bool tourClient;          // Client: Teilnehmer eines Online-Turniers
        int tourId;               // Kennung des Turniers (Clients erkennen daran ein neues)
        int otPhase;              // 0 Lobby, 1 Turnierbaum, 2 Match läuft
        int otCpuFill, otCpuLevel = 2;
        readonly Tournament otSet = new Tournament { Stocks = 3, Minutes = 3, Stage = -1, AutoCpu = true };
        byte[] otLastInfo;
        // Client-Sicht
        class OtMember { public int Id; public string Name; public int Char; }
        readonly List<OtMember> otMembers = new List<OtMember>();
        string otHostName = "";
        bool otIsOnline;
        int otCurR = -1, otCurM = -1;
        int tourClientSeq = -1;
        bool otMatchSeen;         // Client: Info "Match läuft" für das aktuelle Match schon erhalten

        public const int OwnerCpu = 255;

        // ================================================================ Host: Lobby

        void StartTourHost(bool onlineRoom)
        {
            CloseHost();
            LeaveClient();
            host = new HostSession { Tourney = true };
            string err = host.Start(onlineRoom ? 0 : Settings.Port);
            if (err != null) { host = null; Toast(err, true); return; }
            online = onlineRoom;
            if (onlineRoom)
            {
                lastLinkError = null;
                link = new OnlineLink();
                link.Start(host.Sock, true, OnlineLink.NewCode());
            }
            tourOnline = true;
            tourClient = false;
            tour = null;
            otPhase = 0;
            otLastInfo = null;
            Go(Scr.TourLobby);
        }

        void CloseTourHost()
        {
            CloseHost();
            tourOnline = false;
            tour = null;
            otPhase = 0;
            Go(Scr.Multiplayer);
        }

        int OtHumans { get { return 1 + (host != null ? host.Members.Count : 0); } }
        int OtMaxCpu { get { return Math.Max(0, Tournament.MaxPlayers - OtHumans); } }

        void BuildTourLobbyItems()
        {
            if (tourClient) { BuildTourLobbyClientItems(); return; }
            if (host == null) { Add("Zurück", null, () => Go(Scr.Multiplayer), null); return; }
            otCpuFill = Math.Min(otCpuFill, OtMaxCpu);
            Tournament d = otSet;
            Add("Dein Kämpfer", CharDef.All[hostChar].Name + " (" + CharDef.All[hostChar].TypeName + ")", null, x => { hostChar = Cycle(hostChar, x, CharDef.Count); Settings.P1Char = hostChar; });
            Add("Leben", d.Stocks.ToString(), null, x => d.Stocks = Math.Max(1, Math.Min(9, d.Stocks + x)));
            Add("Zeitlimit", MinutesText(d.Minutes), null, x => d.Minutes = NextMinutes(d.Minutes, x));
            Add("Arena", d.Stage < 0 ? "Zufällig" : StageDef.Names[d.Stage], null, x => d.Stage = Cycle(d.Stage + 1, x, StageDef.Count + 1) - 1);
            Add("CPU-Gegner dazu", otCpuFill == 0 ? "keine" : otCpuFill.ToString(), null, x => otCpuFill = Math.Max(0, Math.Min(OtMaxCpu, otCpuFill + x)));
            Add("CPU-Stärke", new[] { "", "leicht", "mittel", "schwer" }[otCpuLevel], null, x => otCpuLevel = Cycle(otCpuLevel - 1, x, 3) + 1);
            Add("CPU gegen CPU", d.AutoCpu ? "Ausrechnen" : "Zuschauen", null, x => d.AutoCpu = !d.AutoCpu);
            int total = OtHumans + otCpuFill;
            Add(total >= 2 ? "Turnier starten! (" + total + " Teilnehmer)" : "Turnier starten (mind. 2 Teilnehmer)", null, StartOnlineTournament, null, total >= 2);
            if (online && link != null) Add("Raum-Code kopieren", null, () => { try { Clipboard.SetText(link.Code); Toast("Code " + link.Code + " kopiert – zum Mitmachen beim Turnier.", false); } catch { } }, null);
            Add("Lobby schließen", null, CloseTourHost, null);
        }

        void StartOnlineTournament()
        {
            if (host == null) return;
            var t = new Tournament { Stocks = otSet.Stocks, Minutes = otSet.Minutes, Stage = otSet.Stage, AutoCpu = otSet.AutoCpu };
            t.E.Add(new Entrant { Name = Settings.PlayerName, Char = hostChar, Cpu = 0, Owner = 0 });
            foreach (Peer m in host.Members) t.E.Add(new Entrant { Name = m.Name, Char = m.Char, Cpu = 0, Owner = m.Id });
            for (int i = 0; i < otCpuFill && t.E.Count < Tournament.MaxPlayers; i++)
                t.E.Add(new Entrant { Name = Tournament.CpuName(i), Char = tourRng.Next(CharDef.Count), Cpu = otCpuLevel, Owner = OwnerCpu });
            if (t.E.Count < 2) { Toast("Für ein Turnier braucht es mindestens 2 Teilnehmer.", true); return; }
            // gleiche Namen unterscheidbar machen: "Max", "Max (2)", "Max (3)"
            var orig = new List<string>();
            foreach (Entrant e in t.E) orig.Add(e.Name);
            for (int i = 0; i < t.E.Count; i++)
            {
                int same = 0;
                for (int j = 0; j < i; j++) if (orig[j] == orig[i]) same++;
                if (same > 0) t.E[i].Name = orig[i] + " (" + (same + 1) + ")";
            }
            tour = t;
            tour.Build(tourRng, frame + 15);
            tourId = tourRng.Next(1, int.MaxValue);
            confetti.Clear();
            tourAutoAt = -1;
            tourShowBracket = false;
            otPhase = 1;
            Settings.P1Char = hostChar;
            Settings.Save();
            Go(Scr.Tourney);
            PushTourInfo(true);
        }

        // Turnier abbrechen oder nach dem Ende: alle zurück in die Lobby
        void TourBackToLobby()
        {
            tour = null;
            otPhase = 0;
            state = null;
            tourMatch = false;
            if (host != null) { host.InMatch = false; host.SlotPeer[0] = host.SlotPeer[1] = null; }
            Go(Scr.TourLobby);
            PushTourInfo(true);
        }

        // ================================================================ Host: Matches

        void StartOnlineTourMatch()
        {
            int r, m;
            if (host == null || tour == null || !tour.NextMatch(out r, out m)) return;
            tourSlots[0] = tour.Slot[r][2 * m];
            tourSlots[1] = tour.Slot[r][2 * m + 1];
            host.ResetMatch();
            host.InMatch = true;
            for (int k = 0; k < 2; k++)
            {
                Entrant e = tour.E[tourSlots[k]];
                Peer p = e.Human && e.Owner > 0 && e.Owner != OwnerCpu ? host.MemberById(e.Owner) : null;
                // Spieler inzwischen weg: CPU übernimmt
                if (e.Human && e.Owner > 0 && p == null) MakeCpu(e);
                host.SlotPeer[k] = p;
            }
            Entrant e0 = tour.E[tourSlots[0]], e1 = tour.E[tourSlots[1]];
            mode = Mode.Host;
            bot0 = e0.Human ? null : new Bot(e0.Cpu);
            bot = e1.Human ? null : new Bot(e1.Cpu);
            names[0] = e0.Name;
            names[1] = e1.Name;
            tourMatch = true;
            tourR = r; tourM = m;
            int stage = tour.Stage < 0 ? tourRng.Next(StageDef.Count) : tour.Stage;
            otPhase = 2;
            BeginMatch(GameState.NewMatch(e0.Char, e1.Char, tour.Stocks, tour.Minutes, stage));
            PushTourInfo(true);
        }

        void MakeCpu(Entrant e)
        {
            if (!e.Human) return;
            e.Cpu = otCpuLevel;
            e.Owner = OwnerCpu;
            if (!e.Name.EndsWith(" (CPU)")) e.Name += " (CPU)";
        }

        // Spielerplatz k wird an diesem PC gesteuert
        bool LocalTourSlot(int k)
        {
            if (!tourMatch || tour == null) return false;
            Entrant e = tour.E[tourSlots[k]];
            return e.Human && e.Owner == 0;
        }

        // ein Simulationsschritt eines Turnier-Matches auf dem Host
        void TickOnlineTourHost(bool menuOpen)
        {
            var h = new int[2];
            var p = new int[2];
            for (int k = 0; k < 2; k++)
            {
                Bot b = k == 0 ? bot0 : bot;
                Peer pe = host.SlotPeer[k];
                if (b != null) b.Think(state, k, out h[k], out p[k]);
                else if (pe != null) host.TakeInput(pe, out h[k], out p[k]);
                else if (LocalTourSlot(k)) { In.Sample(-1, In.PadIndex(0), out h[k], out p[k]); if (menuOpen) h[k] = p[k] = 0; }
                else if (state.phase != GameState.Over)
                {
                    // Spieler hat mitten im Match das Turnier verlassen: Sieg für den Gegner
                    int winner = tourSlots[1 - k];
                    string who = tour.E[tourSlots[k]].Name;
                    MakeCpu(tour.E[tourSlots[k]]);
                    int r = tourR, m = tourM;
                    LeaveTourMatch();
                    tour.Advance(r, m, winner, frame + 20);
                    Toast(who + " ist weg – " + tour.E[winner].Name + " kommt weiter.", true);
                    PushTourInfo(true);
                    return;
                }
            }
            Record(h[0], p[0], h[1], p[1]);
            Sim.Step(state, h, p, evs, false);
            host.SendSnapshot(state, evs);
            foreach (GameEvent e in evs) OnEvent(e);
        }

        // Knöpfe des Hosts im Turnierbaum
        void BuildOnlineTourneyItems()
        {
            if (tour == null) { Add("Zur Lobby", null, TourBackToLobby, null); return; }
            if (tour.Done)
            {
                Add("Neues Turnier (Lobby)", null, TourBackToLobby, null);
                AddTourReplay("Finale ansehen", Tournament.Key(tour.Rounds - 1, 0));
                Add(tourShowBracket ? "Siegerehrung" : "Turnierbaum ansehen", null, () => tourShowBracket = !tourShowBracket, null);
                Add("Lobby schließen", null, CloseTourHost, null);
                return;
            }
            int r, m;
            tour.NextMatch(out r, out m);
            bool cpuOnly = r >= 0 && CpuOnly(r, m);
            if (!cpuOnly) Add("Kampf starten!", null, StartTourMatch, null);
            else if (!tour.AutoCpu) { Add("Zuschauen", null, StartTourMatch, null); Add("Ergebnis ausrechnen", null, SimulateTourMatch, null); }
            if (tour.LastKey >= 0) AddTourReplay("Letztes Match ansehen", tour.LastKey);
            Add("Turnier abbrechen", null, TourBackToLobby, null);
            if (sel >= items.Count) sel = 0;
            if (!items[sel].Enabled) sel = NextSel(1);
        }

        // ================================================================ Host: jeden Frame

        void TickOnlineTourHostAlways()
        {
            if (host == null || !host.Tourney) return;
            // getrennte Teilnehmer: im laufenden Turnier übernimmt eine CPU
            if (host.MembersLeft.Count > 0)
            {
                if (tour != null)
                    foreach (byte id in host.MembersLeft)
                        foreach (Entrant e in tour.E)
                            if (e.Human && e.Owner == id) MakeCpu(e);
                host.MembersLeft.Clear();
            }
            if (frame % 6 == 0) PushTourInfo(false);
        }

        // Turnierstand packen; nur bei Änderung sofort senden (sonst wiederholt der Host ihn regelmäßig)
        void PushTourInfo(bool force)
        {
            if (host == null || !host.Tourney) return;
            byte[] d = WriteTourInfo();
            bool changed = otLastInfo == null || !BytesEqual(d, otLastInfo);
            otLastInfo = d;
            host.SetTourInfo(d, Now, force || changed);
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        byte[] WriteTourInfo()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(tourId);
            w.Write((byte)otPhase);
            w.Write(online);
            w.Write(Settings.PlayerName);
            Tournament set = tour ?? otSet;
            w.Write((byte)set.Stocks); w.Write((byte)set.Minutes); w.Write((sbyte)set.Stage); w.Write(set.AutoCpu);
            w.Write((byte)otCpuFill); w.Write((byte)otCpuLevel);
            // Teilnehmer der Lobby (Host = Kennung 0)
            w.Write((byte)(1 + host.Members.Count));
            w.Write((byte)0); w.Write(Settings.PlayerName); w.Write((byte)hostChar);
            foreach (Peer m in host.Members) { w.Write(m.Id); w.Write(m.Name); w.Write((byte)m.Char); }
            // Turnierbaum
            w.Write(tour != null);
            if (tour != null)
            {
                w.Write((byte)tour.E.Count);
                foreach (Entrant e in tour.E) { w.Write(e.Name); w.Write((byte)e.Char); w.Write((byte)e.Cpu); w.Write((byte)e.Owner); }
                for (int r = 0; r <= tour.Rounds; r++)
                    for (int i = 0; i < tour.Slot[r].Length; i++) w.Write((short)tour.Slot[r][i]);
                w.Write((sbyte)(otPhase == 2 ? tourR : -1));
                w.Write((sbyte)(otPhase == 2 ? tourM : -1));
            }
            w.Flush();
            return ms.ToArray();
        }

        // ================================================================ Client

        void ReadTourInfo(byte[] d)
        {
            var r = new BinaryReader(new MemoryStream(d));
            int id = r.ReadInt32();
            otPhase = r.ReadByte();
            otIsOnline = r.ReadBoolean();
            otHostName = r.ReadString();
            otSet.Stocks = r.ReadByte(); otSet.Minutes = r.ReadByte(); otSet.Stage = r.ReadSByte(); otSet.AutoCpu = r.ReadBoolean();
            otCpuFill = r.ReadByte(); otCpuLevel = r.ReadByte();
            otMembers.Clear();
            int nm = r.ReadByte();
            for (int i = 0; i < nm; i++) otMembers.Add(new OtMember { Id = r.ReadByte(), Name = r.ReadString(), Char = r.ReadByte() });
            otCurR = otCurM = -1;
            if (!r.ReadBoolean()) { tour = null; return; }
            var es = new List<Entrant>();
            int ne = r.ReadByte();
            for (int i = 0; i < ne; i++) es.Add(new Entrant { Name = r.ReadString(), Char = r.ReadByte(), Cpu = r.ReadByte(), Owner = r.ReadByte() });
            bool fresh = tour == null || id != tourId || tour.E.Count != ne;
            if (fresh)
            {
                tourId = id;
                tour = new Tournament { Stocks = otSet.Stocks, Minutes = otSet.Minutes, Stage = otSet.Stage, AutoCpu = otSet.AutoCpu };
                tour.E.AddRange(es);
                tour.Layout(frame + 15);
                confetti.Clear();
                tourShowBracket = false;
            }
            else for (int i = 0; i < ne; i++) { tour.E[i].Name = es[i].Name; tour.E[i].Char = es[i].Char; tour.E[i].Cpu = es[i].Cpu; tour.E[i].Owner = es[i].Owner; }
            var slots = new int[tour.Rounds + 1][];
            for (int k = 0; k <= tour.Rounds; k++)
            {
                slots[k] = new int[tour.Slot[k].Length];
                for (int i = 0; i < slots[k].Length; i++) slots[k][i] = r.ReadInt16();
            }
            otCurR = r.ReadSByte();
            otCurM = r.ReadSByte();
            // Runde 0 übernehmen, danach jeden neu besetzten Platz als Aufstieg (mit Animation) nachspielen
            for (int i = 0; i < slots[0].Length; i++) tour.Slot[0][i] = slots[0][i];
            for (int k = 1; k <= tour.Rounds; k++)
                for (int i = 0; i < slots[k].Length; i++)
                    if (tour.Slot[k][i] == Tournament.Open && slots[k][i] >= 0)
                    {
                        int a = tour.Slot[k - 1][2 * i], b = tour.Slot[k - 1][2 * i + 1];
                        int when = fresh ? frame + 15 + tour.Size * 3 + 20 + (k - 1) * 10 : frame;
                        if (slots[k][i] == a || slots[k][i] == b) tour.Advance(k - 1, i, slots[k][i], when);
                        else tour.Slot[k][i] = slots[k][i];
                    }
        }

        void BuildTourLobbyClientItems()
        {
            if (client == null) { Add("Zurück", null, () => Go(Scr.Multiplayer), null); return; }
            Add("Dein Kämpfer", CharDef.All[client.WantChar].Name + " (" + CharDef.All[client.WantChar].TypeName + ")", null, x =>
            {
                client.WantChar = Cycle(client.WantChar, x, CharDef.Count);
                Settings.P1Char = client.WantChar;
                client.SendHello();
            });
            Add("Turnier verlassen", null, LeaveOnlineTour, null);
        }

        void LeaveOnlineTour()
        {
            LeaveClient();
            tourClient = false;
            tour = null;
            state = null;
            otPhase = 0;
            Settings.Save();
            Go(Scr.Multiplayer);
        }

        // jeden Frame, solange dieses Spiel Teilnehmer eines Online-Turniers ist
        void TickOnlineTourClient()
        {
            if (!tourClient) return;
            if (client == null) { tourClient = false; return; }
            if (client.Status == ClientSession.Failed)
            {
                string er = client.Error;
                LeaveOnlineTour();
                Toast(er, true);
                return;
            }
            if (client.TourData != null && client.TourSeq != tourClientSeq)
            {
                tourClientSeq = client.TourSeq;
                try { ReadTourInfo(client.TourData); } catch { }
                if (otPhase == 2) otMatchSeen = true;
            }
            if (scr == Scr.Match)
            {
                // Host ist zurück am Turnierbaum (oder in der Lobby): Match-Bildschirm verlassen
                if (otMatchSeen && otPhase != 2 && replay == null)
                {
                    state = null;
                    Go(otPhase == 0 || tour == null ? Scr.TourLobby : Scr.Tourney);
                }
                return;
            }
            if (scr == Scr.TourLobby && otPhase >= 1 && tour != null) Go(Scr.Tourney);
            else if (scr == Scr.Tourney && (otPhase == 0 || tour == null)) Go(Scr.TourLobby);
            if ((scr == Scr.Tourney || scr == Scr.TourLobby) && client.NewMatch && client.Auth != null) StartClientTourMatch();
        }

        void StartClientTourMatch()
        {
            mode = Mode.Client;
            tourMatch = false; bot0 = null;
            for (int k = 0; k < 2; k++) names[k] = "Spieler " + (k + 1);
            if (tour != null && otCurR >= 0 && otCurR < tour.Rounds)
                for (int k = 0; k < 2; k++)
                {
                    int v = tour.Slot[otCurR][2 * otCurM + k];
                    if (v >= 0) names[k] = tour.E[v].Name;
                }
            otMatchSeen = otPhase == 2;
            client.NewMatch = false;
            BeginMatch(ClientState(client.Auth));
        }

        // nach dem Beitritt: Turnier-Lobby statt normaler Lobby
        bool JoinedTour()
        {
            if (client == null || !client.TourMember || client.Status != ClientSession.Connected) return false;
            tourClient = true;
            tourOnline = false;
            tour = null;
            tourClientSeq = -1;
            otPhase = 0;
            client.WantChar = Settings.P1Char;
            client.SendHello();
            Go(Scr.TourLobby);
            Toast("Turnier-Lobby von " + client.HostName + " beigetreten.", false);
            return true;
        }

        // ================================================================ Zeichnen

        void DrawTourLobby(Graphics g)
        {
            Color info = Color.FromArgb(220, 215, 235), dim = Color.FromArgb(170, 170, 160, 200), good = Color.FromArgb(150, 240, 160), warn = Color.FromArgb(255, 200, 120);
            bool isHost = !tourClient;
            bool onlineRoom = isHost ? online : otIsOnline;
            DrawTitle(g, onlineRoom ? "Online-Turnier" : "LAN-Turnier");
            // links: Verbindung und Einstellungen
            Panel(g, 40, 150, 560, 110);
            if (isHost)
            {
                if (online && link != null)
                {
                    Line(g, "Raum-Code für alle Teilnehmer:", 58, 158, dim, R.FTiny);
                    R.Shadowed(g, link.Code, R.FBig, Color.FromArgb(255, 235, 160), 58, 178, false);
                    Line(g, link.StatusText, 300, 192, link.Error != null ? Color.FromArgb(255, 130, 130) : link.RelayState == 1 ? good : warn, R.FTiny);
                    Line(g, "Mitspieler: \"Online-Spiel beitreten\" und den Code eingeben.", 58, 230, info, R.FTiny);
                }
                else
                {
                    List<string> ips = NetUtil.LocalIPs();
                    Line(g, "Deine IP-Adresse:", 58, 158, dim, R.FTiny);
                    R.Shadowed(g, ips[0] + "  ·  Port " + Settings.Port, R.FBig, Color.FromArgb(255, 235, 160), 58, 178, false);
                    Line(g, "Mitspieler: \"LAN-Spiel beitreten\" (Lobby wird meist gefunden).", 58, 230, info, R.FTiny);
                }
                DrawItems(g, 40, 274, 560, 36, 4);
            }
            else
            {
                Line(g, "Turnier von", 58, 158, dim, R.FTiny);
                R.Shadowed(g, otHostName, R.FBig, Color.FromArgb(255, 235, 160), 58, 178, false);
                Line(g, (frame % 60 < 40 ? "• " : "   ") + "Warte, bis der Host das Turnier startet ...", 58, 230, warn, R.FTiny);
                DrawItems(g, 40, 274, 560, 44, 8);
                Panel(g, 40, 390, 560, 130);
                Line(g, "Leben: " + otSet.Stocks + "   ·   Zeitlimit: " + MinutesText(otSet.Minutes), 58, 402, info, R.FSmall);
                Line(g, "Arena: " + (otSet.Stage < 0 ? "Zufällig" : StageDef.Names[StageDef.Valid(otSet.Stage)]), 58, 432, info, R.FSmall);
                Line(g, otCpuFill > 0 ? "+ " + otCpuFill + " CPU-Gegner (" + new[] { "", "leicht", "mittel", "schwer" }[Math.Max(1, Math.Min(3, otCpuLevel))] + ")" : "ohne CPU-Gegner", 58, 462, info, R.FSmall);
                Line(g, "Gespielt wird mit deinen Tasten von Spieler 1.", 58, 492, dim, R.FTiny);
            }
            // rechts: Teilnehmer
            var list = new List<OtMember>();
            if (isHost)
            {
                list.Add(new OtMember { Id = 0, Name = Settings.PlayerName, Char = hostChar });
                if (host != null) foreach (Peer m in host.Members) list.Add(new OtMember { Id = m.Id, Name = m.Name, Char = m.Char });
            }
            else list.AddRange(otMembers);
            int cpu = isHost ? otCpuFill : otCpuFill;
            Panel(g, 630, 150, 610, 520);
            R.Shadowed(g, "TEILNEHMER  " + list.Count + (cpu > 0 ? " + " + cpu + " CPU" : ""), R.FHudName, Gold, 650, 160, false);
            float rowH = list.Count > 12 ? 28 : 34;
            for (int i = 0; i < list.Count; i++)
            {
                OtMember m = list[i];
                float y = 200 + i * rowH;
                CharDef c = CharDef.All[Math.Max(0, Math.Min(CharDef.Count - 1, m.Char))];
                R.DrawHeadIcon(g, m.Char, 668, y + rowH / 2, rowH > 30 ? 0.75f : 0.6f);
                bool me = tourClient ? client != null && m.Id == client.MyId : m.Id == 0;
                Line(g, m.Name + (m.Id == 0 ? "  (Host)" : "") + (me ? "  ← du" : ""), 692, y + 2, me ? Color.White : info, rowH > 30 ? R.FSmall : R.FTiny);
                Line(g, c.Name, 1020, y + 2, c.Glow, rowH > 30 ? R.FSmall : R.FTiny);
            }
            if (list.Count == 1 && isHost)
                Line(g, (frame % 60 < 40 ? "• " : "   ") + "Warte auf Mitspieler ...", 668, 240, warn, R.FSmall);
        }

        // Zeile über den Knöpfen im Turnierbaum: wer spielt, wer schaut zu
        void DrawOnlineTourStatus(Graphics g)
        {
            if (tour == null || tour.Done) return;
            int r, m;
            if (!tour.NextMatch(out r, out m)) return;
            Entrant a = tour.E[tour.Slot[r][2 * m]], b = tour.E[tour.Slot[r][2 * m + 1]];
            int myId = tourClient ? (client != null ? client.MyId : -1) : 0;
            bool mine = (a.Human && a.Owner == myId) || (b.Human && b.Owner == myId);
            string t = mine ? "Du bist dran! " + (tourClient ? "Gleich startet der Host das Match." : "Starte das Match, wenn alle bereit sind.")
                : tourClient ? "Du schaust bei diesem Match zu – der Host startet es." : "Du schaust bei diesem Match zu.";
            R.Shadowed(g, t, R.FSmall, mine ? Color.FromArgb(255, 225, 120) : Color.FromArgb(210, 205, 230), Renderer.VW / 2f, 84, true);
        }

        // ================================================================ Tests

        public void DebugStartTourHostLan(int port) { Settings.Port = port; StartTourHost(false); }
        public HostSession DebugHost { get { return host; } }
        public Tournament DebugTour { get { return tour; } }
        public int DebugOtPhase { get { return otPhase; } }
        public bool DebugTourClient { get { return tourClient; } }
        public Scr DebugScr { get { return scr; } }
        public void DebugSetCpuFill(int n, int level) { otCpuFill = n; otCpuLevel = level; }
        public void DebugStartOnlineTournament() { StartOnlineTournament(); }
        public void DebugStartTourMatch() { StartTourMatch(); }
        public void DebugJoinLan(string ip, int port) { LeaveClient(); Go(Scr.Join); OpenJoin(); ipText = ip; portText = port.ToString(); ConnectClient(); }
        public ClientSession DebugClient { get { return client; } }
        public void DebugTourContinue() { if (tourMatch) TourMatchDone(); }
        public void DebugLeaveOnlineTour() { LeaveOnlineTour(); }
        public void DebugBackToLobby() { TourBackToLobby(); }
    }
}
