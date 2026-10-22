using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Sensensturm
{
    public enum Scr { Main, Local, Host, Join, OnlineJoin, ClientLobby, Settings, Controls, Combos, Match, TourneySetup, Tourney, Replays }
    public enum Mode { Local, Host, Client }

    class Item
    {
        public string Label, Value;
        public Action Ok;
        public Action<int> Change;
        public bool Enabled = true;
        public int TextField = -1;
        public bool Small;
    }

    public partial class Game
    {
        public bool Running = true;
        public readonly InputManager In = new InputManager();
        public readonly Renderer R = new Renderer();
        public Action<bool> SetFullscreen;
        public int Fps;

        readonly Stopwatch clock = Stopwatch.StartNew();
        double Now { get { return clock.Elapsed.TotalSeconds; } }

        Scr scr = Scr.Main;
        Mode mode;
        int sel;
        readonly List<Item> items = new List<Item>();
        readonly List<RectangleF> itemRects = new List<RectangleF>();
        string toast = "";
        int toastTimer;
        bool toastErr;
        int frame;

        // Text-Eingabe
        int editing = -1;
        bool swallowMenu;
        string ipText = "", portText = "", nameText = "", setPortText = "";

        // Steuerung
        int ctrlPlayer, ctrlCol;
        bool waitingKey;

        // Match
        GameState state;
        Bot bot;
        bool paused;
        int pauseSel;
        bool resultsShown;
        // Combo-Übersicht im Kampf (F1 / Controller-Back / Pausemenü)
        bool comboView;
        int comboViewSlot;
        HostSession host;
        ClientSession client;
        readonly List<GameEvent> evs = new List<GameEvent>();
        readonly string[] names = new string[2];
        int hostChar;
        int comboWeapon;
        OnlineLink link;
        bool online;
        string codeText = "";
        string lastLinkError;

        float mouseSc = 1, mouseOx, mouseOy;
        PointF mouse;

        public Game()
        {
            ipText = Settings.LastIp;
            portText = Settings.Port.ToString();
            hostChar = Settings.P1Char;
            Audio.Volume = Settings.Volume / 10f;
        }

        void Toast(string msg, bool err)
        {
            toast = msg;
            toastErr = err;
            toastTimer = err ? 420 : 200;
        }

        void Go(Scr s)
        {
            scr = s;
            sel = 0;
            editing = -1;
            waitingKey = false;
            Audio.Play(Sfx.Select, 0.6f);
        }

        // ================================================================ Eingabe-Hooks (Form)

        public void OnKey(Keys k)
        {
            if (waitingKey && scr == Scr.Controls)
            {
                int row = sel;
                if (k == Keys.Escape) { waitingKey = false; swallowMenu = true; return; }
                if (k == Keys.Back || k == Keys.Delete) k = Keys.None;
                if (row >= 0 && row < Btn.Count)
                {
                    // Mehrfachbelegung ist erlaubt (z. B. W = Oben und Springen)
                    Settings.Bind[ctrlPlayer, row, ctrlCol] = k;
                }
                waitingKey = false;
                swallowMenu = true;
                Audio.Play(Sfx.Select, 0.6f);
                return;
            }
            if (editing >= 0)
            {
                if (k == Keys.Enter || k == Keys.Escape || k == Keys.Tab) { FinishEdit(); swallowMenu = true; }
                else if (k == Keys.Back)
                {
                    string t = GetText(editing);
                    if (t.Length > 0) SetText(editing, t.Substring(0, t.Length - 1));
                }
                else if (k == Keys.V && Control.ModifierKeys == Keys.Control)
                {
                    try { string c = Clipboard.GetText(); foreach (char ch in c) OnChar(ch); } catch { }
                }
                return;
            }
            if (k == Keys.F11) { Settings.Fullscreen = !Settings.Fullscreen; if (SetFullscreen != null) SetFullscreen(Settings.Fullscreen); }
            if (k == Keys.F3) Settings.ShowHitboxes = !Settings.ShowHitboxes;
        }

        public void OnChar(char c)
        {
            if (editing < 0 || char.IsControl(c)) return;
            string t = GetText(editing);
            bool ok;
            int max;
            if (editing == 4) { c = char.ToUpperInvariant(c); ok = OnlineLink.Alphabet.IndexOf(c) >= 0 || c == '0' || c == 'O' || c == 'I' || c == '1'; max = 6; if (c == '0') c = 'O'; if (c == '1') c = 'I'; }
            else if (editing == 0) { ok = char.IsDigit(c) || c == '.'; max = 15; }
            else if (editing == 1 || editing == 3) { ok = char.IsDigit(c); max = 5; }
            else { ok = char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_'; max = 14; }
            if (ok && t.Length < max) SetText(editing, t + c);
        }

        string GetText(int f) { return f == 0 ? ipText : f == 1 ? portText : f == 2 ? nameText : f == 4 ? codeText : setPortText; }
        void SetText(int f, string v) { if (f == 0) ipText = v; else if (f == 1) portText = v; else if (f == 2) nameText = v; else if (f == 4) codeText = v; else setPortText = v; }

        void FinishEdit()
        {
            if (editing == 2) { string n = nameText.Trim(); if (n.Length > 0) Settings.PlayerName = n; nameText = Settings.PlayerName; }
            if (editing == 3) { int p; if (int.TryParse(setPortText, out p) && p >= 1024 && p <= 65535) Settings.Port = p; else Toast("Port muss zwischen 1024 und 65535 liegen.", true); setPortText = Settings.Port.ToString(); portText = setPortText; }
            editing = -1;
        }

        public void OnMouseMove(int x, int y)
        {
            mouse = new PointF((x - mouseOx) / mouseSc, (y - mouseOy) / mouseSc);
            for (int i = 0; i < itemRects.Count && i < items.Count; i++)
                if (itemRects[i].Contains(mouse) && items[i].Enabled && sel != i && !waitingKey && editing < 0) { sel = i; break; }
        }

        // true = Klick wurde verbraucht (Tastenbelegung), sonst geht er zusätzlich als Spieleingabe weiter
        public bool OnMouseDown(int x, int y, bool right, Keys button)
        {
            if (waitingKey && scr == Scr.Controls) { OnKey(button); return true; }
            OnMouseMove(x, y);
            for (int i = 0; i < itemRects.Count && i < items.Count; i++)
            {
                if (!itemRects[i].Contains(mouse) || !items[i].Enabled) continue;
                sel = i;
                Activate(items[i], right ? -1 : 1, true);
                return false;
            }
            if (editing >= 0) FinishEdit();
            return false;
        }

        public void OnFocusLost() { In.ReleaseAll(); }

        // ================================================================ Tick

        public void Tick()
        {
            frame++;
            In.PollPads();
            if (link != null)
            {
                link.Tick(Now);
                if (link.Error != null && link.Error != lastLinkError) { lastLinkError = link.Error; Toast(link.Error, true); }
            }
            if (host != null) host.Poll(Now);
            if (client != null) client.Poll(Now);
            if (host != null) foreach (string m in host.Messages) Toast(m, m.Contains("verloren") || m.Contains("getrennt"));
            if (host != null) host.Messages.Clear();
            if (toastTimer > 0) toastTimer--;

            if (scr == Scr.Match) { if (replay != null) TickReplay(); else TickMatch(); }
            else TickMenus();

            R.Tick();
            In.EndTick();
        }

        void TickMenus()
        {
            if (scr == Scr.Host && host != null)
            {
                if (host.HasClient && hostChar == host.ClientChar) hostChar = CharDef.Next(hostChar, 1, host.ClientChar);
                host.HostChar = hostChar;
                host.Stocks = Settings.Stocks;
                host.Minutes = Settings.MatchMinutes;
                host.Stage = Settings.Stage;
                host.InMatch = false;
                if (host.ClientJustJoined) { host.ClientJustJoined = false; Audio.Play(Sfx.Charge, 0.8f); }
            }
            if (scr == Scr.Join && client != null)
            {
                int port;
                if (!int.TryParse(portText, out port)) port = Settings.Port;
                client.Discover(Now, port);
                if (client.Status == ClientSession.Connected) { Go(Scr.ClientLobby); Toast("Verbunden mit " + client.HostName + ".", false); }
                else if (client.Status == ClientSession.Failed) { Toast(client.Error, true); client.Status = ClientSession.Idle; client.RestoreTuning(); }
            }
            if (scr == Scr.OnlineJoin && client != null && link != null)
            {
                if (link.Error != null) { LeaveClient(); }
                else if (link.Decided != null && client.Status == ClientSession.Idle) client.ConnectEndpoint(link.Decided, Now);
                else if (client.Status == ClientSession.Connected) { Go(Scr.ClientLobby); Toast("Online verbunden mit " + client.HostName + (client.OverRelay ? " (über Relay)" : " (direkt)") + ".", false); }
                else if (client.Status == ClientSession.Failed) { Toast(client.Error, true); LeaveClient(); }
            }
            if (scr == Scr.ClientLobby && client != null)
            {
                if (client.Status == ClientSession.Failed) { bool wasOnline = link != null; Toast(client.Error, true); LeaveClient(); if (wasOnline) Go(Scr.OnlineJoin); else { Go(Scr.Join); OpenJoin(); } return; }
                if (client.NewSnapshot && client.Auth != null) { StartClientMatch(); return; }
            }

            if (scr == Scr.Tourney) TickTourney();
            BuildItems();
            HandleMenu();
        }

        void HandleMenu()
        {
            if (swallowMenu) { swallowMenu = false; return; }
            if (editing >= 0 || waitingKey) return;
            if (sel >= items.Count) sel = Math.Max(0, items.Count - 1);
            foreach (MenuKey k in In.Menu)
            {
                if (items.Count == 0) break;
                switch (k)
                {
                    case MenuKey.Up: sel = NextSel(-1); Audio.Play(Sfx.Menu, 0.5f); break;
                    case MenuKey.Down: sel = NextSel(1); Audio.Play(Sfx.Menu, 0.5f); break;
                    case MenuKey.Left: if (items[sel].Change != null) { items[sel].Change(-1); Audio.Play(Sfx.Menu, 0.5f); } else if (scr == Scr.Controls && sel < Btn.Count) ctrlCol = 0; else if (scr == Scr.Tourney) { sel = NextSel(-1); Audio.Play(Sfx.Menu, 0.5f); } break;
                    case MenuKey.Right: if (items[sel].Change != null) { items[sel].Change(1); Audio.Play(Sfx.Menu, 0.5f); } else if (scr == Scr.Controls && sel < Btn.Count) ctrlCol = 1; else if (scr == Scr.Tourney) { sel = NextSel(1); Audio.Play(Sfx.Menu, 0.5f); } break;
                    case MenuKey.Ok: Activate(items[sel], 1, false); break;
                    case MenuKey.Back: Back(); break;
                }
                if (scr == Scr.Match || editing >= 0 || waitingKey) break;
            }
        }

        int NextSel(int d)
        {
            int n = items.Count;
            int s = sel;
            for (int i = 0; i < n; i++)
            {
                s = (s + d + n) % n;
                if (items[s].Enabled) return s;
            }
            return sel;
        }

        void Activate(Item it, int dir, bool mouseClick)
        {
            if (!it.Enabled) return;
            if (it.TextField >= 0)
            {
                editing = it.TextField;
                if (editing == 2) nameText = Settings.PlayerName;
                if (editing == 3) setPortText = Settings.Port.ToString();
                return;
            }
            if (it.Ok != null) { Audio.Play(Sfx.Select, 0.6f); it.Ok(); }
            else if (it.Change != null) { it.Change(dir); Audio.Play(Sfx.Menu, 0.5f); }
        }

        void Back()
        {
            switch (scr)
            {
                case Scr.Local: Go(Scr.Main); break;
                case Scr.Host: CloseHost(); Go(Scr.Main); break;
                case Scr.Join: LeaveClient(); Go(Scr.Main); break;
                case Scr.OnlineJoin: LeaveClient(); Go(Scr.Main); break;
                case Scr.ClientLobby: LeaveClient(); Go(Scr.Main); break;
                case Scr.Settings: Settings.Save(); Go(Scr.Main); break;
                case Scr.Controls: Settings.Save(); Go(Scr.Settings); break;
                case Scr.Combos: Go(Scr.Settings); break;
                case Scr.TourneySetup: Go(Scr.Main); break;
                case Scr.Tourney: Go(Scr.Main); break;
                case Scr.Replays: Go(Scr.Main); break;
            }
        }

        static int Cycle(int v, int d, int n) { return ((v + d) % n + n) % n; }
        static readonly int[] minuteOpts = { 0, 2, 3, 4, 5, 8, 10 };
        static int NextMinutes(int cur, int d)
        {
            int i = Array.IndexOf(minuteOpts, cur);
            if (i < 0) i = 4;
            return minuteOpts[Cycle(i, d, minuteOpts.Length)];
        }
        static string MinutesText(int m) { return m == 0 ? "Unbegrenzt" : m + " Minuten"; }
        static string OnOff(bool b) { return b ? "An" : "Aus"; }

        void Add(string label, string value, Action ok, Action<int> change, bool enabled = true, int textField = -1)
        {
            items.Add(new Item { Label = label, Value = value, Ok = ok, Change = change, Enabled = enabled, TextField = textField });
        }

        void BuildItems()
        {
            items.Clear();
            switch (scr)
            {
                case Scr.Main:
                    Add("Lokal spielen", null, () => Go(Scr.Local), null);
                    Add("Turnier (bis 16 Spieler)", null, () => Go(Scr.TourneySetup), null);
                    if (tour != null && !tour.Done) Add("Turnier fortsetzen", null, () => Go(Scr.Tourney), null);
                    Add("Online-Spiel erstellen", null, StartOnlineHost, null);
                    Add("Online-Spiel beitreten", null, () => { LeaveClient(); Go(Scr.OnlineJoin); }, null);
                    Add("LAN-Spiel erstellen", null, StartHost, null);
                    Add("LAN-Spiel beitreten", null, () => { Go(Scr.Join); OpenJoin(); }, null);
                    Add("Wiederholungen", null, () => { replayFiles = null; Go(Scr.Replays); }, null);
                    Add("Einstellungen", null, () => { nameText = Settings.PlayerName; setPortText = Settings.Port.ToString(); Go(Scr.Settings); }, null);
                    Add("Spiel verlassen", null, () => Running = false, null);
                    break;
                case Scr.Local:
                    {
                        string[] opp = { "Spieler 2 (selber PC)", "CPU - leicht", "CPU - mittel", "CPU - schwer", "Trainingspuppe (Combos üben)" };
                        Add("Gegner", opp[Settings.Opponent], null, d => Settings.Opponent = Cycle(Settings.Opponent, d, 5));
                        Add("Spieler 1", CharDef.All[Settings.P1Char].Name + " (" + CharDef.All[Settings.P1Char].TypeName + ")", null, d => Settings.P1Char = CharDef.Next(Settings.P1Char, d, -1));
                        Add(Settings.Opponent == 0 ? "Spieler 2" : Settings.Opponent == 4 ? "Puppe" : "CPU", CharDef.Look(Settings.P2Char, Settings.P2Char == Settings.P1Char ? 1 : 0).Name + " (" + CharDef.All[Settings.P2Char].TypeName + ")", null, d => Settings.P2Char = CharDef.Next(Settings.P2Char, d, -1));
                        Add("Leben", Settings.Stocks.ToString(), null, d => Settings.Stocks = Math.Max(1, Math.Min(9, Settings.Stocks + d)));
                        Add("Zeitlimit", MinutesText(Settings.MatchMinutes), null, d => Settings.MatchMinutes = NextMinutes(Settings.MatchMinutes, d));
                        Add("Arena", StageDef.Names[Settings.Stage], null, d => Settings.Stage = Cycle(Settings.Stage, d, StageDef.Count));
                        Add("Kampf starten", null, StartLocal, null);
                        Add("Zurück", null, () => Go(Scr.Main), null);
                        break;
                    }
                case Scr.Host:
                    Add("Dein Charakter", CharDef.All[hostChar].Name + " (" + CharDef.All[hostChar].TypeName + ")", null, d => hostChar = CharDef.Next(hostChar, d, host != null && host.HasClient ? host.ClientChar : -1));
                    Add("Leben", Settings.Stocks.ToString(), null, d => Settings.Stocks = Math.Max(1, Math.Min(9, Settings.Stocks + d)));
                    Add("Zeitlimit", MinutesText(Settings.MatchMinutes), null, d => Settings.MatchMinutes = NextMinutes(Settings.MatchMinutes, d));
                    Add("Arena", StageDef.Names[Settings.Stage], null, d => Settings.Stage = Cycle(Settings.Stage, d, StageDef.Count));
                    Add(host != null && host.HasClient ? "Match starten" : "Match starten (warte auf Spieler 2)", null, StartHostMatch, null, host != null && host.HasClient);
                    if (online) Add("Raum-Code kopieren", null, () => { try { Clipboard.SetText(link.Code); Toast("Code " + link.Code + " kopiert – schick ihn deinem Mitspieler.", false); } catch { } }, null);
                    Add("Lobby schließen", null, () => { CloseHost(); Go(Scr.Main); }, null);
                    break;
                case Scr.OnlineJoin:
                    {
                        bool busy = link != null && client != null;
                        Add("Raum-Code", editing == 4 ? codeText + (frame % 60 < 30 ? "|" : " ") : (codeText.Length > 0 ? codeText : "(eingeben)"), null, null, !busy, 4);
                        Add(busy ? "Verbinde ..." : "Verbinden", null, ConnectOnline, null, !busy);
                        Add("Zurück", null, () => { LeaveClient(); Go(Scr.Main); }, null);
                        break;
                    }
                case Scr.Join:
                    {
                        bool busy = client != null && client.Status == ClientSession.Connecting;
                        Add("IP-Adresse des Hosts", editing == 0 ? ipText + (frame % 60 < 30 ? "|" : " ") : (ipText.Length > 0 ? ipText : "(eingeben)"), null, null, !busy, 0);
                        Add("Port", editing == 1 ? portText + (frame % 60 < 30 ? "|" : " ") : portText, null, null, !busy, 1);
                        Add(busy ? "Verbinde ..." : "Verbinden", null, ConnectClient, null, !busy);
                        if (client != null)
                            foreach (FoundHost f in client.Found)
                            {
                                FoundHost ff = f;
                                Add("Gefunden: " + f.Name, f.Ip + (f.Full ? "  (voll)" : ""), () => { ipText = ff.Ip; ConnectClient(); }, null, !busy && !f.Full);
                            }
                        Add("Zurück", null, () => { LeaveClient(); Go(Scr.Main); }, null);
                        break;
                    }
                case Scr.ClientLobby:
                    if (client != null)
                        Add("Dein Charakter", CharDef.All[client.MyChar].Name + " (" + CharDef.All[client.MyChar].TypeName + ")", null, d =>
                        {
                            client.WantChar = CharDef.Next(client.MyChar, d, client.HostChar);
                            client.MyChar = client.WantChar;
                            Settings.P1Char = client.WantChar;
                            client.SendHello();
                        });
                    Add("Verbindung trennen", null, () => { LeaveClient(); Go(Scr.Main); }, null);
                    break;
                case Scr.Settings:
                    Add("Combos lernen", null, () => { comboWeapon = CharDef.All[Settings.P1Char].Weapon; Go(Scr.Combos); }, null);
                    Add("Lautstärke", Settings.Volume + " / 10", null, d => { Settings.Volume = Math.Max(0, Math.Min(10, Settings.Volume + d)); Audio.Volume = Settings.Volume / 10f; });
                    Add("Bildschirmwackeln", OnOff(Settings.ScreenShake), null, d => Settings.ScreenShake = !Settings.ScreenShake);
                    Add("Vollbild (F11)", OnOff(Settings.Fullscreen), null, d => { Settings.Fullscreen = !Settings.Fullscreen; if (SetFullscreen != null) SetFullscreen(Settings.Fullscreen); });
                    Add("FPS / Ping anzeigen", OnOff(Settings.ShowStats), null, d => Settings.ShowStats = !Settings.ShowStats);
                    Add("Hitboxen anzeigen (F3)", OnOff(Settings.ShowHitboxes), null, d => Settings.ShowHitboxes = !Settings.ShowHitboxes);
                    Add("Spielername", editing == 2 ? nameText + (frame % 60 < 30 ? "|" : " ") : Settings.PlayerName, null, null, true, 2);
                    Add("LAN-Port", editing == 3 ? setPortText + (frame % 60 < 30 ? "|" : " ") : Settings.Port.ToString(), null, null, true, 3);
                    Add("Steuerung Spieler 1", null, () => { ctrlPlayer = 0; ctrlCol = 0; Go(Scr.Controls); }, null);
                    Add("Steuerung Spieler 2", null, () => { ctrlPlayer = 1; ctrlCol = 0; Go(Scr.Controls); }, null);
                    Add("Spielwerte neu laden (tuning.ini)", null, () => { Tuning.Load(); Toast("Spielwerte aus tuning.ini geladen.", false); }, null);
                    Add("tuning.ini im Editor öffnen", null, () => { try { Tuning.Save(); Process.Start("notepad.exe", "\"" + Tuning.FilePath + "\""); } catch { Toast("Editor konnte nicht geöffnet werden.", true); } }, null);
                    Add("Zurück", null, () => { Settings.Save(); Go(Scr.Main); }, null);
                    break;
                case Scr.Controls:
                    for (int a = 0; a < Btn.Count; a++)
                    {
                        string k1 = KeyName(Settings.Bind[ctrlPlayer, a, 0]), k2 = KeyName(Settings.Bind[ctrlPlayer, a, 1]);
                        bool w = waitingKey && sel == a;
                        string v = (ctrlCol == 0 && sel == a ? "[" + (w ? "Taste ..." : k1) + "]" : k1) + "   " + (ctrlCol == 1 && sel == a ? "[" + (w ? "Taste ..." : k2) + "]" : k2);
                        items.Add(new Item { Label = Btn.Names[a], Value = v, Ok = () => { waitingKey = true; }, Small = true });
                    }
                    Add("Standard wiederherstellen", null, () => Settings.DefaultBindings(ctrlPlayer), null);
                    Add("Zurück", null, () => { Settings.Save(); Go(Scr.Settings); }, null);
                    break;
                case Scr.TourneySetup:
                    BuildTourneySetupItems();
                    break;
                case Scr.Tourney:
                    BuildTourneyItems();
                    break;
                case Scr.Replays:
                    BuildReplayItems();
                    break;
                case Scr.Combos:
                    Add("Waffe", Weapon.Names[comboWeapon], null, d => comboWeapon = ((comboWeapon + d) % 3 + 3) % 3);
                    Add("Combos in der Trainingspuppe üben", null, () =>
                    {
                        Settings.Opponent = 4;
                        if (CharDef.All[Settings.P1Char].Weapon != comboWeapon)
                            for (int i = 0; i < CharDef.Count; i++) if (CharDef.All[i].Weapon == comboWeapon) { Settings.P1Char = i; break; }
                        if (Settings.P2Char == Settings.P1Char) Settings.P2Char = CharDef.Next(Settings.P1Char, 1, Settings.P1Char);
                        Go(Scr.Local);
                    }, null);
                    Add("Zurück", null, () => Go(Scr.Settings), null);
                    break;
            }
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.None: return "-";
                case Keys.Space: return "Leertaste";
                case Keys.Left: return "Pfeil links";
                case Keys.Right: return "Pfeil rechts";
                case Keys.Up: return "Pfeil hoch";
                case Keys.Down: return "Pfeil runter";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.ShiftKey: return "Shift";
                case Keys.ControlKey: return "Strg";
                case Keys.Return: return "Enter";
                case Keys.PageDown: return "Bild runter";
                case Keys.PageUp: return "Bild hoch";
                case Keys.End: return "Ende";
                case Keys.LButton: return "Linksklick";
                case Keys.RButton: return "Rechtsklick";
                case Keys.MButton: return "Mittelklick";
                case Keys.XButton1: return "Maus 4";
                case Keys.XButton2: return "Maus 5";
            }
            string s = k.ToString();
            if (s.StartsWith("NumPad")) return "Num " + s.Substring(6);
            if (s.StartsWith("D") && s.Length == 2) return s.Substring(1);
            return s;
        }

        // ================================================================ Lobby / Netzwerk

        void StartHost()
        {
            CloseHost();
            LeaveClient();
            host = new HostSession();
            string err = host.Start(Settings.Port);
            if (err != null) { host = null; Toast(err, true); return; }
            online = false;
            Go(Scr.Host);
        }

        void StartOnlineHost()
        {
            CloseHost();
            LeaveClient();
            host = new HostSession();
            string err = host.Start(0);
            if (err != null) { host = null; Toast(err, true); return; }
            online = true;
            lastLinkError = null;
            link = new OnlineLink();
            link.Start(host.Sock, true, OnlineLink.NewCode());
            Go(Scr.Host);
        }

        void ConnectOnline()
        {
            if (editing >= 0) FinishEdit();
            string code = codeText.Trim().ToUpperInvariant();
            if (code.Length != 6) { Toast("Der Raum-Code hat 6 Zeichen (z. B. K7M2QX).", true); return; }
            LeaveClient();
            client = new ClientSession();
            client.WantChar = Settings.P1Char;
            string err = client.Open();
            if (err != null) { Toast(err, true); client = null; return; }
            lastLinkError = null;
            link = new OnlineLink();
            link.Start(client.Sock, false, code);
        }

        void CloseHost()
        {
            if (host != null) host.Close();
            host = null;
            if (client == null) link = null;
        }

        void OpenJoin()
        {
            if (client == null) client = new ClientSession();
            client.WantChar = Settings.P1Char;
            string err = client.Open();
            if (err != null) Toast(err, true);
            if (portText.Length == 0) portText = Settings.Port.ToString();
        }

        void ConnectClient()
        {
            if (editing >= 0) FinishEdit();
            if (client == null) OpenJoin();
            int port;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535) { Toast("Ungültiger Port.", true); return; }
            client.Connect(ipText, port, Now);
            if (client.Status == ClientSession.Failed) { Toast(client.Error, true); client.Status = ClientSession.Idle; return; }
            Settings.LastIp = ipText;
            Settings.Save();
        }

        void LeaveClient()
        {
            if (client != null) client.Close();
            client = null;
            if (host == null) link = null;
        }

        // ================================================================ Match-Start

        void BeginMatch(GameState s)
        {
            state = s;
            paused = false;
            pauseSel = 0;
            resultsShown = false;
            comboView = false;
            R.ClearFx();
            BeginRecording();
            R.SnapCamera(state);
            scr = Scr.Match;
            sel = 0;
            In.Menu.Clear();
        }

        void StartLocal()
        {
            mode = Mode.Local;
            bot = Settings.Opponent > 0 ? new Bot(Settings.Opponent == 4 ? 0 : Settings.Opponent) : null;
            bot0 = null;
            tourMatch = false;
            names[0] = "Spieler 1";
            names[1] = Settings.Opponent == 4 ? "Trainingspuppe" : bot != null ? "CPU" : "Spieler 2";
            BeginMatch(GameState.NewMatch(Settings.P1Char, Settings.P2Char, Settings.Stocks, Settings.MatchMinutes, Settings.Stage));
            Settings.Save();
        }

        void StartHostMatch()
        {
            if (host == null || !host.HasClient) return;
            mode = Mode.Host;
            tourMatch = false; bot0 = null;
            host.ResetMatch();
            host.InMatch = true;
            names[0] = Settings.PlayerName;
            names[1] = host.ClientName;
            int cc = host.ClientChar == hostChar ? CharDef.Next(hostChar, 1, hostChar) : host.ClientChar;
            BeginMatch(GameState.NewMatch(hostChar, cc, Settings.Stocks, Settings.MatchMinutes, Settings.Stage));
            Settings.P1Char = hostChar;
            Settings.Save();
        }

        void StartClientMatch()
        {
            mode = Mode.Client;
            tourMatch = false; bot0 = null;
            names[0] = client.HostName;
            names[1] = Settings.PlayerName;
            client.NewMatch = false;
            BeginMatch(Predict(client.Auth));
        }

        GameState Predict(GameState auth)
        {
            GameState g = auth.Clone();
            int oppHeld = g.p[0].prevHeld;
            foreach (NetInput ni in client.Pending)
                Sim.Step(g, new[] { oppHeld, ni.Held }, new[] { 0, ni.Pressed }, null, true);
            return g;
        }

        void BackToHostLobby(string msg)
        {
            if (host != null) { host.InMatch = false; host.SendLobby(); }
            state = null;
            Go(host != null ? Scr.Host : Scr.Main);
            if (msg != null) Toast(msg, true);
        }

        // ================================================================ Match-Tick

        void OpenComboView()
        {
            comboView = true;
            comboViewSlot = mode == Mode.Client ? 1 : 0;
            Audio.Play(Sfx.Select, 0.6f);
        }

        // Eingaben in der Combo-Übersicht: schließen oder zum anderen Kämpfer wechseln
        void ComboViewInput()
        {
            foreach (MenuKey k in In.Menu)
            {
                if (k == MenuKey.Left || k == MenuKey.Right) { comboViewSlot = 1 - comboViewSlot; Audio.Play(Sfx.Menu, 0.5f); }
                else if (k == MenuKey.Back || k == MenuKey.Pause || k == MenuKey.Combos || k == MenuKey.Ok) { comboView = false; Audio.Play(Sfx.Select, 0.6f); break; }
            }
            In.Menu.Clear();
        }

        void TickMatch()
        {
            bool over = state.phase == GameState.Over && state.phaseTimer > 100;
            if (over) comboView = false;
            if (comboView) ComboViewInput();
            else if (!over && In.Menu.Contains(MenuKey.Combos)) { OpenComboView(); In.Menu.Clear(); }
            bool pausePressed = In.Menu.Contains(MenuKey.Pause);

            if (over)
            {
                if (!resultsShown) { resultsShown = true; pauseSel = 0; paused = false; }
            }
            else if (pausePressed) { paused = !paused; pauseSel = 0; Audio.Play(Sfx.Select, 0.6f); In.Menu.Clear(); }

            bool menuOpen = paused || over || comboView;
            if ((paused || over) && !comboView) MatchMenu(over);
            if (scr != Scr.Match || state == null) return;

            evs.Clear();
            int h0, p0, h1, p1;
            switch (mode)
            {
                case Mode.Local:
                    if (paused || comboView) return; // lokal pausiert die Combo-Übersicht das Spiel
                    // Spielerplatz 0: CPU (nur Turnier, CPU gegen CPU) oder Mensch; mit CPU-Gegner gelten beide Tastenbelegungen
                    if (bot0 != null) bot0.Think(state, 0, out h0, out p0);
                    else In.Sample(bot == null ? 0 : -1, In.PadIndex(0), out h0, out p0);
                    if (bot != null) bot.Think(state, 1, out h1, out p1);
                    else In.Sample(1, In.PadIndex(1), out h1, out p1);
                    if (menuOpen) { if (bot0 == null) h0 = p0 = 0; if (bot == null) h1 = p1 = 0; }
                    Record(h0, p0, h1, p1);
                    Sim.Step(state, new[] { h0, h1 }, new[] { p0, p1 }, evs, false);
                    foreach (GameEvent e in evs) R.OnEvent(e, state);
                    break;

                case Mode.Host:
                    if (host == null || !host.HasClient) { BackToHostLobby("Spieler 2 hat das Spiel verlassen. Zurück in der Lobby."); return; }
                    In.Sample(-1, In.PadIndex(0), out h0, out p0);
                    if (menuOpen) h0 = p0 = 0;
                    host.TakeInput(out h1, out p1);
                    Record(h0, p0, h1, p1);
                    Sim.Step(state, new[] { h0, h1 }, new[] { p0, p1 }, evs, false);
                    host.SendSnapshot(state, evs);
                    foreach (GameEvent e in evs) R.OnEvent(e, state);
                    break;

                case Mode.Client:
                    if (client == null) { Go(Scr.Main); return; }
                    if (client.Status == ClientSession.Failed) { string er = client.Error; LeaveClient(); state = null; Go(Scr.Main); Toast(er, true); return; }
                    if (client.LobbyReceived) { state = null; Go(Scr.ClientLobby); Toast("Der Host hat das Match beendet.", false); return; }
                    if (client.NewMatch) { client.NewMatch = false; if (client.Auth != null) { BeginMatch(Predict(client.Auth)); } }
                    else if (client.NewSnapshot && client.Auth != null)
                    {
                        GameState np = Predict(client.Auth);
                        for (int i = 0; i < 2; i++)
                        {
                            float ex = state.p[i].x + R.OffX[i] - np.p[i].x, ey = state.p[i].y + R.OffY[i] - np.p[i].y;
                            bool snap = Math.Abs(ex) > 120 || Math.Abs(ey) > 120 || np.p[i].state == St.KO || np.p[i].state == St.Respawn;
                            R.OffX[i] = snap ? 0 : ex;
                            R.OffY[i] = snap ? 0 : ey;
                        }
                        state = np;
                    }
                    In.Sample(-1, In.PadIndex(0), out h1, out p1);
                    if (menuOpen) h1 = p1 = 0;
                    client.SendInput(h1, p1);
                    Sim.Step(state, new[] { state.p[0].prevHeld, h1 }, new[] { 0, p1 }, null, true);
                    foreach (GameEvent e in client.NewEvents) R.OnEvent(e, state);
                    client.NewEvents.Clear();
                    break;
            }
            R.UpdateCamera(state);
        }

        void MatchMenu(bool over)
        {
            items.Clear();
            if (over)
            {
                if (lastReplay != null && mode != Mode.Client)
                {
                    Add("Wiederholung ansehen", null, WatchLastReplay, null);
                    Add("Speichern", null, SaveLastReplay, null);
                }
                if (tourMatch)
                    Add("Weiter zum Turnierbaum", null, TourMatchDone, null);
                else if (mode == Mode.Local)
                {
                    Add("Revanche", null, StartLocal, null);
                    Add("Hauptmenü", null, () => { state = null; Go(Scr.Main); }, null);
                }
                else if (mode == Mode.Host)
                {
                    Add("Revanche", null, StartHostMatch, null);
                    Add("Zur Lobby", null, () => BackToHostLobby(null), null);
                }
                else
                {
                    Add("Verbindung trennen", null, () => { LeaveClient(); state = null; Go(Scr.Main); }, null);
                }
            }
            else
            {
                Add("Weiter", null, () => paused = false, null);
                Add("Combos ansehen (F1)", null, OpenComboView, null);
                if (tourMatch)
                {
                    if (bot0 != null) Add("Match überspringen (ausrechnen)", null, () => { int r = tourR, m = tourM; LeaveTourMatch(); tour.Advance(r, m, tour.Simulate(r, m, tourRng), frame + 20); }, null);
                    Add("Match neu starten", null, () => StartTourMatch(), null);
                    Add("Zum Turnierbaum (später spielen)", null, LeaveTourMatch, null);
                }
                else if (mode == Mode.Local)
                {
                    Add("Neustart", null, StartLocal, null);
                    Add("Hauptmenü", null, () => { state = null; Go(Scr.Main); }, null);
                }
                else if (mode == Mode.Host)
                {
                    Add("Match beenden (zur Lobby)", null, () => BackToHostLobby(null), null);
                    Add("Lobby schließen", null, () => { CloseHost(); state = null; Go(Scr.Main); }, null);
                }
                else
                {
                    Add("Verbindung trennen", null, () => { LeaveClient(); state = null; Go(Scr.Main); }, null);
                }
            }
            sel = Math.Min(pauseSel, items.Count - 1);
            if (!swallowMenu)
                foreach (MenuKey k in In.Menu)
                {
                    if (k == MenuKey.Up || k == MenuKey.Left) { sel = NextSel(-1); Audio.Play(Sfx.Menu, 0.5f); }
                    else if (k == MenuKey.Down || k == MenuKey.Right) { sel = NextSel(1); Audio.Play(Sfx.Menu, 0.5f); }
                    else if (k == MenuKey.Ok) { pauseSel = sel; Activate(items[sel], 1, false); In.Menu.Clear(); return; }
                    else if (k == MenuKey.Back && !over) { paused = false; In.Menu.Clear(); return; }
                }
            swallowMenu = false;
            pauseSel = sel;
        }

        // ================================================================ Rendering

        public void Render(Graphics g, int W, int H)
        {
            g.ResetTransform();
            g.ResetClip();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            float sc = Math.Min(W / (float)Renderer.VW, H / (float)Renderer.VH);
            float ox = (W - Renderer.VW * sc) / 2f, oy = (H - Renderer.VH * sc) / 2f;
            mouseSc = sc; mouseOx = ox; mouseOy = oy;
            // das Hintergrundbild deckt alles ab, schwarz löschen nur bei Rändern (anderes Seitenverhältnis)
            if (ox > 0.01f || oy > 0.01f) g.Clear(Color.Black);
            // Arena im Hintergrund: im Match die gespielte, in den Lobbys die gewählte
            R.Stage = scr == Scr.Match && state != null ? state.stage
                : scr == Scr.Local || scr == Scr.Host ? Settings.Stage
                : scr == Scr.ClientLobby && client != null ? client.Stage : StageDef.Graveyard;
            R.DrawBackground(g, (int)Math.Ceiling(Renderer.VW * sc), (int)Math.Ceiling(Renderer.VH * sc), (int)ox, (int)oy);
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(sc, sc);
            g.SetClip(new RectangleF(0, 0, Renderer.VW, Renderer.VH));

            itemRects.Clear();
            if (scr == Scr.Match && state != null && replay != null)
            {
                R.DrawWorld(g, state, false);
                R.UltKeys[0] = R.UltKeys[1] = "";
                R.DrawHud(g, state, names);
                DrawReplayOverlay(g);
            }
            else if (scr == Scr.Match && state != null)
            {
                R.DrawWorld(g, state, Settings.ShowHitboxes);
                // Ultimate-Taste nur für Spieler an diesem PC anzeigen
                bool twoLocal = mode == Mode.Local && bot == null;
                R.UltKeys[0] = mode == Mode.Client || bot0 != null ? "" : K(Btn.UltIndex, 0);
                R.UltKeys[1] = mode == Mode.Client ? K(Btn.UltIndex, 0) : twoLocal ? K(Btn.UltIndex, 1) : "";
                R.DrawHud(g, state, names);
                if (state.phase == GameState.Countdown && !comboView)
                    R.Shadowed(g, "F1 / Controller-Back: Combos & Angriffe ansehen", R.FSmall, Color.FromArgb(220, 230, 220, 255), Renderer.VW / 2f, 74, true);
                bool over = state.phase == GameState.Over && state.phaseTimer > 100;
                if (state.phase == GameState.Over && !over)
                    R.Shadowed(g, "SPIEL VORBEI", R.FTitle, Color.White, Renderer.VW / 2f, 250, true);
                if (over) DrawResults(g);
                else if (paused && !comboView) DrawPause(g);
                if (comboView && !over) DrawComboOverlay(g);
                if (mode == Mode.Client && client != null && Now - client.LastSnapshotTime > 0.5)
                    R.Shadowed(g, "Warte auf Daten vom Host ...", R.FMed, Color.FromArgb(255, 200, 120), Renderer.VW / 2f, 120, true);
            }
            else
            {
                R.DrawMenuBackdrop(g);
                DrawMenuScreen(g);
            }

            // Statuszeile
            if (Settings.ShowStats)
            {
                string st = Fps + " FPS";
                if (host != null) st += host.HasClient ? (host.OverRelay ? "  ·  Online-Relay" : online ? "  ·  Online direkt" : "") + "  ·  Host  ·  Ping " + (host.Ping >= 0 ? host.Ping + " ms" : "-") : "  ·  Host  ·  kein Client";
                if (client != null && client.Status == ClientSession.Connected) st += (client.OverRelay ? "  ·  Online-Relay" : "") + "  ·  Client  ·  Ping " + (client.Ping >= 0 ? client.Ping + " ms" : "-");
                using (var b = new SolidBrush(Color.FromArgb(170, 220, 210, 240))) g.DrawString(st, R.FTiny, b, Renderer.VW - g.MeasureString(st, R.FTiny).Width - 10, 6);
            }

            if (toastTimer > 0 && toast.Length > 0)
            {
                float a = Math.Min(1f, toastTimer / 30f);
                var rect = new RectangleF(190, Renderer.VH - 190, 900, 80);
                using (var path = Renderer.RoundRect(rect.X, rect.Y, rect.Width, rect.Height, 12))
                {
                    using (var b = new SolidBrush(Color.FromArgb((int)(225 * a), toastErr ? 70 : 20, toastErr ? 12 : 30, toastErr ? 24 : 50))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb((int)(255 * a), toastErr ? 255 : 150, toastErr ? 110 : 210, toastErr ? 110 : 255), 2)) g.DrawPath(p, path);
                }
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), 255, 255, 255)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(toast, R.FSmall, b, rect, sf);
            }
        }

        void DrawTitle(Graphics g, string sub)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(frame * 0.04);
            for (int k = 3; k >= 1; k--)
                using (var b = new SolidBrush(Color.FromArgb((int)(18 + 10 * pulse), 200, 130, 255)))
                {
                    var sz = g.MeasureString("SENSENSTURM", R.FTitle);
                    g.DrawString("SENSENSTURM", R.FTitle, b, Renderer.VW / 2f - sz.Width / 2 + (k - 2) * 3, 34 + (k - 2) * 2);
                }
            using (var lg = new LinearGradientBrush(new PointF(0, 40), new PointF(0, 120), Color.FromArgb(240, 220, 255), Color.FromArgb(150, 170, 255)))
            {
                var sz = g.MeasureString("SENSENSTURM", R.FTitle);
                g.DrawString("SENSENSTURM", R.FTitle, lg, Renderer.VW / 2f - sz.Width / 2, 34);
            }
            if (sub != null) R.Shadowed(g, sub, R.FMed, Color.FromArgb(210, 200, 230), Renderer.VW / 2f, 120, true);
        }

        void DrawItems(Graphics g, float x, float y, float w, float h, float gap)
        {
            DrawItems(g, x, y, w, h, gap, 0, items.Count);
        }

        void DrawItems(Graphics g, float x, float y, float w, float h, float gap, int from, int count)
        {
            while (itemRects.Count < items.Count) itemRects.Add(RectangleF.Empty);
            for (int i = from; i < Math.Min(items.Count, from + count); i++)
            {
                Item it = items[i];
                float ih = it.Small ? h * 0.78f : h;
                var rect = new RectangleF(x, y, w, ih);
                itemRects[i] = rect;
                bool s = i == sel;
                using (var path = Renderer.RoundRect(rect.X, rect.Y, rect.Width, rect.Height, 10))
                {
                    if (s)
                    {
                        using (var b = new LinearGradientBrush(rect, Color.FromArgb(220, 110, 60, 170), Color.FromArgb(220, 40, 80, 170), LinearGradientMode.Horizontal)) g.FillPath(b, path);
                        using (var p = new Pen(Color.FromArgb(230, 230, 200, 255), 2.5f)) g.DrawPath(p, path);
                    }
                    else using (var b = new SolidBrush(Color.FromArgb(150, 16, 10, 30))) g.FillPath(b, path);
                }
                Font f = it.Small ? R.FSmall : R.FMed;
                Color tc = !it.Enabled ? Color.FromArgb(120, 150, 140, 170) : s ? Color.White : Color.FromArgb(215, 205, 235);
                float ty = rect.Y + (ih - g.MeasureString("Ag", f).Height) / 2;
                using (var b = new SolidBrush(tc))
                {
                    if (it.Value == null)
                    {
                        var sz = g.MeasureString(it.Label, f);
                        g.DrawString(it.Label, f, b, rect.X + (w - sz.Width) / 2, ty);
                    }
                    else
                    {
                        g.DrawString(it.Label, f, b, rect.X + 18, ty);
                        string v = it.Change != null && s ? "<  " + it.Value + "  >" : it.Value;
                        if (it.TextField >= 0 && editing == it.TextField) v = "> " + v;
                        var sz = g.MeasureString(v, f);
                        using (var vb = new SolidBrush(it.TextField >= 0 ? Color.FromArgb(255, 235, 170) : Color.FromArgb(190, 225, 255)))
                            g.DrawString(v, f, vb, rect.Right - sz.Width - 18, ty);
                    }
                }
                y += ih + gap;
            }
        }

        void Panel(Graphics g, float x, float y, float w, float h)
        {
            using (var path = Renderer.RoundRect(x, y, w, h, 14))
            {
                using (var b = new SolidBrush(Color.FromArgb(190, 12, 6, 24))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(160, 180, 140, 255), 2)) g.DrawPath(p, path);
            }
        }

        void Line(Graphics g, string t, float x, float y, Color c, Font f = null)
        {
            using (var b = new SolidBrush(c)) g.DrawString(t, f ?? R.FSmall, b, x, y);
        }

        void DrawMenuScreen(Graphics g)
        {
            Color info = Color.FromArgb(220, 215, 235), dim = Color.FromArgb(170, 170, 160, 200), good = Color.FromArgb(150, 255, 170), warn = Color.FromArgb(255, 200, 120);
            switch (scr)
            {
                case Scr.Main:
                    DrawTitle(g, "Ein Plattform-Kampfspiel mit Sensen und Schilden");
                    R.DrawPortrait(g, 0, 210, 600, 2.1f, frame, false);
                    R.DrawPortrait(g, 1, 1070, 600, -2.1f, frame + 40, false);
                    DrawItems(g, 400, 172, 480, 40, 6);
                    Line(g, "Steuerung: Pfeiltasten/WASD · Enter/Leertaste · Esc  —  oder Maus / Controller", 350, 690, dim, R.FTiny);
                    break;
                case Scr.Local:
                    DrawTitle(g, "Lokal spielen");
                    DrawItems(g, 330, 160, 620, 44, 6);
                    DrawCharCard(g, Settings.P1Char, 20, 150, "SPIELER 1", false);
                    DrawCharCard(g, Settings.P2Char, 960, 150, Settings.Opponent == 0 ? "SPIELER 2" : Settings.Opponent == 4 ? "TRAININGSPUPPE" : "CPU", true, Settings.P2Char == Settings.P1Char ? 1 : 0);
                    Panel(g, 230, 580, 820, 110);
                    Line(g, "Spieler 1:  A/D · W/Leertaste springen · J/Linksklick leicht · K schwer · L/Shift Dodge · H/Rechtsklick Schild · E greifen", 250, 592, info, R.FTiny);
                    Line(g, "Spieler 2:  Pfeiltasten (Hoch = springen) · Num1 leicht · Num2 schwer · Num3 Dodge · Num5 Schild · Num4 greifen", 250, 616, info, R.FTiny);
                    Line(g, "Controller: A springen · X leicht · B schwer · Y greifen · RT/RB Dodge · LT/LB Schild · RS-Klick Ultimate  (1. Controller = P1, 2. = P2)", 200, 640, info, R.FTiny);
                    Line(g, "Esc / Start = Pause · F3 = Hitboxen · Tasten in den Einstellungen änderbar", 250, 664, dim, R.FTiny);
                    break;
                case Scr.Host:
                    {
                        if (online && link != null)
                        {
                            DrawTitle(g, "Online-Spiel erstellen");
                            Panel(g, 330, 150, 620, 215);
                            Line(g, "Raum-Code für deinen Mitspieler:", 350, 160, dim);
                            R.Shadowed(g, link.Code, R.FTitle, Color.FromArgb(255, 235, 160), 640, 178, true);
                            Line(g, link.StatusText, 350, 262, link.Error != null ? Color.FromArgb(255, 130, 130) : link.RelayState == 1 ? good : warn, R.FTiny);
                            Line(g, "Mitspieler: \"Online-Spiel beitreten\" und diesen Code eingeben.", 350, 284, info, R.FTiny);
                            if (host != null && host.HasClient)
                                Line(g, "• " + host.ClientName + " verbunden (" + (host.OverRelay ? "Relay" : "direkt") + ") · " + (host.Ping >= 0 ? host.Ping + " ms" : "..."), 350, 312, good, R.FMed);
                            else
                                Line(g, (frame % 60 < 40 ? "• " : "   ") + "Warte auf Mitspieler ...", 350, 312, warn, R.FMed);
                            DrawItems(g, 330, 380, 620, 40, 5);
                            DrawCharCard(g, hostChar, 20, 150, "DU (HOST)", false);
                            if (host != null && host.HasClient) DrawCharCard(g, host.ClientChar, 960, 150, host.ClientName.ToUpper(), true);
                            break;
                        }
                        DrawTitle(g, "LAN-Spiel erstellen");
                        Panel(g, 330, 150, 620, 215);
                        List<string> ips = NetUtil.LocalIPs();
                        Line(g, "Deine IP-Adresse:", 350, 160, dim);
                        R.Shadowed(g, ips[0] + "  ·  Port " + Settings.Port, R.FBig, Color.FromArgb(255, 235, 160), 350, 184, false);
                        if (ips.Count > 1) Line(g, "Weitere Adressen: " + string.Join(", ", ips.GetRange(1, Math.Min(3, ips.Count - 1)).ToArray()), 350, 236, dim, R.FTiny);
                        Line(g, "Spieler 2: \"LAN-Spiel beitreten\" und diese IP eingeben", 350, 258, info, R.FTiny);
                        Line(g, "(oder die gefundene Lobby auswählen).", 350, 278, info, R.FTiny);
                        if (host != null && host.HasClient)
                            Line(g, "• " + host.ClientName + " verbunden  ·  Ping " + (host.Ping >= 0 ? host.Ping + " ms" : "..."), 350, 312, good, R.FMed);
                        else
                            Line(g, (frame % 60 < 40 ? "• " : "   ") + "Warte auf Spieler 2 ...", 350, 312, warn, R.FMed);
                        DrawItems(g, 330, 380, 620, 40, 5);
                        DrawCharCard(g, hostChar, 20, 150, "DU (HOST)", false);
                        if (host != null && host.HasClient) DrawCharCard(g, host.ClientChar, 960, 150, host.ClientName.ToUpper(), true);
                        break;
                    }
                case Scr.Join:
                    {
                        DrawTitle(g, "LAN-Spiel beitreten");
                        DrawItems(g, 330, 180, 620, 52, 10);
                        float y = 190 + items.Count * 62;
                        if (client != null && client.Status == ClientSession.Connecting)
                            R.Shadowed(g, "Verbinde mit " + client.Host + " ...", R.FMed, warn, Renderer.VW / 2f, y, true);
                        else if (client != null && client.Found.Count == 0)
                            Line(g, "Suche nach Lobbys im lokalen Netzwerk ...", 450, y, dim);
                        Line(g, "Tipp: Die IP-Adresse wird beim Host unter \"LAN-Spiel erstellen\" angezeigt. Beide PCs müssen im selben WLAN/LAN sein.", 190, 650, dim, R.FTiny);
                        break;
                    }
                case Scr.OnlineJoin:
                    {
                        DrawTitle(g, "Online-Spiel beitreten");
                        DrawItems(g, 330, 180, 620, 52, 10);
                        if (link != null && client != null)
                            R.Shadowed(g, client.Status == ClientSession.Connecting ? "Verbinde mit dem Host (" + link.Mode + ") ..." : link.StatusText, R.FMed, warn, Renderer.VW / 2f, 390, true);
                        Panel(g, 230, 470, 820, 120);
                        Line(g, "Funktioniert über das Internet – ihr müsst NICHT im selben Netzwerk sein.", 250, 482, info, R.FTiny);
                        Line(g, "Der Host wählt \"Online-Spiel erstellen\" und schickt dir den 6-stelligen Raum-Code.", 250, 504, info, R.FTiny);
                        Line(g, "Zuerst wird eine Direktverbindung versucht; klappt das nicht (strenge Firewall/NAT),", 250, 526, dim, R.FTiny);
                        Line(g, "läuft das Spiel automatisch über einen Vermittlungsserver (etwas höhere Verzögerung).", 250, 548, dim, R.FTiny);
                        break;
                    }
                case Scr.ClientLobby:
                    {
                        DrawTitle(g, "Lobby");
                        Panel(g, 330, 150, 620, 250);
                        if (client != null)
                        {
                            Line(g, "Verbunden mit " + client.HostName, 350, 165, good, R.FMed);
                            Line(g, client.Host + "   ·   Ping: " + (client.Ping >= 0 ? client.Ping + " ms" : "..."), 350, 205, info);
                            Line(g, "Leben: " + client.Stocks + "     Zeitlimit: " + MinutesText(client.Minutes), 350, 235, info);
                            Line(g, "Arena: " + StageDef.Names[client.Stage], 350, 262, info);
                            Line(g, "Wähle unten deinen Charakter.", 350, 289, info);
                            Line(g, (frame % 60 < 40 ? "• " : "   ") + "Warte auf den Host ...", 350, 330, warn, R.FMed);
                            DrawCharCard(g, client.HostChar, 20, 150, "HOST", false);
                            DrawCharCard(g, client.MyChar, 960, 150, "DU", true);
                        }
                        DrawItems(g, 330, 420, 620, 52, 10);
                        break;
                    }
                case Scr.Settings:
                    DrawTitle(g, "Einstellungen");
                    DrawItems(g, 330, 160, 620, 38, 6);
                    break;
                case Scr.Combos:
                    DrawCombos(g);
                    break;
                case Scr.TourneySetup:
                    DrawTourneySetup(g);
                    break;
                case Scr.Tourney:
                    DrawTourney(g);
                    break;
                case Scr.Replays:
                    DrawReplayList(g);
                    break;
                case Scr.Controls:
                    DrawTitle(g, "Steuerung Spieler " + (ctrlPlayer + 1));
                    DrawItems(g, 300, 140, 680, 40, 4);
                    Line(g, "Enter = Taste ändern · Links/Rechts = Haupt-/Zweittaste · Entf = Taste löschen · Esc = abbrechen", 260, 650, dim, R.FTiny);
                    Line(g, "Auch Maustasten möglich (Klick während \"Taste ...\"). Controller: A springen · X leicht · B schwer · Y greifen · RT/RB Dodge · LT/LB Schild · RS-Klick Ultimate", 160, 672, dim, R.FTiny);
                    break;
            }
        }

        static string ShortKey(Keys k)
        {
            return k == Keys.LButton ? "LMB" : k == Keys.RButton ? "RMB" : k == Keys.MButton ? "MMB" : k == Keys.Space ? "Space" : KeyName(k);
        }

        string K(int act, int pl = 0)
        {
            Keys a = Settings.Bind[pl, act, 0], b = Settings.Bind[pl, act, 1];
            if (a == Keys.None) return ShortKey(b);
            if (b == Keys.None || act == 2 || act == 3) return ShortKey(a);
            return ShortKey(a) + "/" + ShortKey(b);
        }

        void DrawCharCard(Graphics g, int ch, float x, float y, string label, bool mirror, int skin = 0)
        {
            CharDef c = CharDef.Look(ch, skin);
            float w = 300, h = 420;
            using (var path = Renderer.RoundRect(x, y, w, h, 14))
            {
                using (var b = new SolidBrush(Color.FromArgb(200, 12, 6, 24))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(220, c.Glow), 2.5f)) g.DrawPath(p, path);
            }
            Line(g, label, x + 16, y + 10, Color.FromArgb(200, 210, 200, 235), R.FTiny);
            R.DrawPortrait(g, ch, x + w / 2, y + 240, mirror ? -1.15f : 1.15f, frame + ch * 17, false, skin);
            Line(g, c.Name, x + 16, y + 26, c.Glow, R.FMed);
            Line(g, c.Title + "  ·  Typ: " + c.TypeName, x + 16, y + 60, Color.FromArgb(220, 220, 210, 240), R.FTiny);
            string[] st = { "Tempo", "Kraft", "Gewicht", "Reichweite" };
            int[] v = { c.StatSpeed, c.StatPower, c.StatWeight, c.StatRange };
            for (int i = 0; i < 4; i++)
            {
                float yy = y + 256 + i * 21;
                Line(g, st[i], x + 16, yy, Color.FromArgb(200, 200, 230), R.FTiny);
                for (int k = 0; k < 5; k++)
                    using (var b = new SolidBrush(k < v[i] ? c.Glow : Color.FromArgb(50, 255, 255, 255))) g.FillRectangle(b, x + 110 + k * 34, yy + 4, 30, 10);
            }
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 230, 160)))
            using (var sf = new StringFormat())
                g.DrawString("Spezial: " + c.Special, R.FTiny, b, new RectangleF(x + 16, y + 342, w - 32, 40), sf);
            using (var b = new SolidBrush(Color.FromArgb(190, 200, 200, 225)))
                g.DrawString(c.Desc, R.FTiny, b, new RectangleF(x + 16, y + 380, w - 32, 38));
        }

        // pl: Tastenbelegung von Spieler 1 (0) oder 2 (1)
        string[][] CombosFor(int w, int pl = 0)
        {
            string L = K(5, pl), H = K(6, pl), J = K(4, pl), D = K(7, pl), G = K(9, pl), Ul = K(Btn.UltIndex, pl), U = ShortKey(Settings.Bind[pl, 2, 0]), Dn = ShortKey(Settings.Bind[pl, 3, 0]);
            string F = "Vor", a = "  >  ";
            string ladder = U + "+" + L + a + "[ " + J + a + U + "+" + L + " ]  wiederholen ...";
            string rescue = J + a + J + a + J + a + D + "+" + U + a + U + "+" + H + a + "Wand: " + J;
            string moves = "Dash/Sprint + " + F + "+" + L + "     ·     " + U + "+" + H;
            if (w == Weapon.Hammer)
                return new[]
                {
                    new[] { "Rammbock · Vulkanhieb", "Neu", moves, "Beide mit Super-Rüstung: Rammbock aus dem Lauf, Vulkanhieb schleudert nach oben." },
                    new[] { "ULTIMATE: Weltenbrecher", "Ultimate", L + a + Ul + "   (Leiste voll)", "Zeitstopp, dann zerreißt der Hammer die Bühne zu beiden Seiten. Zerbricht jeden Schild." },
                    new[] { "Steinschlag-Kette", "Leicht", L + a + F + "+" + L, "Kurzer Schlag, dann der weite Seitwärtsschwung." },
                    new[] { "Gipfelstürmer", "Mittel", U + "+" + L + a + J + a + U + "+" + L, "Aufwärtshieb und in der Luft den Himmelshammer hinterher." },
                    new[] { "Steingriff-Wurf", "Mittel", G + a + Dn + " (Wurf unten)" + a + U + "+" + L + a + J + a + U + "+" + L, "Packen, auf den Boden schmettern, hochhämmern." },
                    new[] { "Felsbrecher", "Schwer", L + a + F + "+" + H + " (perfekt!)", "Nur mit PERFEKT-Timing trifft der langsame Felsbrecher sicher." },
                    new[] { "Himmelsleiter  (ENDLOS)", "Meister", ladder, "Auch mit dem Hammer: perfekte Sprünge kosten nichts." },
                    new[] { "Rettung (Recovery)", "Pflicht", rescue, "Schwerer Golem fällt schnell – früh springen!" },
                };
            if (w == Weapon.Bow)
                return new[]
                {
                    new[] { "Kometentritt · Sternensalto", "Neu", moves, "Fliegender Tritt aus dem Lauf · aufladbarer Salto-Tritt nach oben." },
                    new[] { "ULTIMATE: Sternenflut", "Ultimate", F + "+" + L + a + Ul + "   (Leiste voll)", "Zeitstopp, dann ein riesiger Sternenpfeil quer über die Bühne. Zerbricht jeden Schild." },
                    new[] { "Trittwirbel", "Leicht", L + a + F + "+" + L, "Bogenstich direkt in den Wirbeltritt." },
                    new[] { "Mondaufstieg", "Mittel", U + "+" + L + a + J + a + U + "+" + L, "Mondsicheltritt nach oben, Himmelskick hinterher." },
                    new[] { "Gleitfalle", "Mittel", Dn + "+" + L + a + J + a + F + "+" + L, "Unter den Gegner gleiten, hochtreten, Doppeltritt." },
                    new[] { "Rankenwurf", "Mittel", G + " (Rankenseil)" + a + Dn + a + U + "+" + L + a + J + a + U + "+" + L, "Das Seil packt auch weit entfernte Gegner." },
                    new[] { "Pfeilhagel", "Schwer", F + "+" + L + a + F + "+" + H + " (Sternenpfeil)", "Tritt, dann sofort der Pfeil hinterher – PERFEKT nötig." },
                    new[] { "Himmelsleiter  (ENDLOS)", "Meister", ladder, "Leicht und schnell: Lyras Leiter ist die schnellste." },
                    new[] { "Rettung (Recovery)", "Pflicht", rescue, "Sternensprung trägt besonders hoch." },
                };
            return new[]
            {
                new[] { "Schnitterlauf · Mondaufgang", "Neu", moves, "Angriff aus dem Lauf · aufladbarer Hochschlag gegen Gegner über dir." },
                new[] { "ULTIMATE: Seelensturm", "Ultimate", L + a + F + "+" + L + a + Ul + "   (Leiste voll)", "Zeitstopp, dann ein Sturmschnitt nach vorne. Geht auch als Combo-Abschluss nach jedem Treffer." },
                new[] { "Erster Schnitt", "Leicht", L + a + F + "+" + L, "Schneller Hieb direkt in den weiten Vorwärtshieb." },
                new[] { "Aufwärtsspirale", "Mittel", L + a + U + "+" + L + a + J + a + U + "+" + L, "Hochschleudern, nachspringen, in der Luft nachsetzen." },
                new[] { "Bodenschnitt-Kette", "Mittel", Dn + "+" + L + a + J + a + F + "+" + L + a + J + a + U + "+" + L, "Vom Bodenschnitt in die Luft und nach oben weiter." },
                new[] { "Haken & Wurf", "Mittel", G + a + Dn + " (Wurf unten)" + a + U + "+" + L + a + J + a + U + "+" + L, "Greifen geht durch Schild und Parade." },
                new[] { "Schnitter-Jagd", "Schwer", F + "+" + L + a + D + " (Verfolgung)" + a + F + "+" + L + a + F + "+" + H, "Dodge nach einem Treffer jagt dem Gegner hinterher." },
                new[] { "Himmelsleiter  (ENDLOS)", "Meister", ladder, "Bei PERFEKTEM Timing kostet jeder Sprung nichts – die Combo hört nicht auf!" },
                new[] { "Rettung (Recovery)", "Pflicht", rescue, "Aus der Arena geflogen? Sprünge, Dodge zur Bühne, Aufschwung, Wandsprung." },
            };
        }

        // Liste der Angriffe und Combos einer Waffe ab y, liefert das untere Ende
        float DrawComboRows(Graphics g, int weapon, int pl, float y)
        {
            foreach (var cmb in CombosFor(weapon, pl))
            {
                bool ult = cmb[1] == "Ultimate";
                Panel(g, 110, y, 1060, 44);
                Color dc = cmb[1] == "Meister" ? Color.FromArgb(255, 210, 90) : cmb[1] == "Pflicht" ? Color.FromArgb(150, 255, 170)
                    : ult ? Color.FromArgb(255, 130, 230) : cmb[1] == "Neu" ? Color.FromArgb(120, 230, 255) : Color.FromArgb(190, 200, 255);
                Line(g, cmb[0], 126, y + 2, ult ? Color.FromArgb(255, 205, 245) : Color.White, R.FHudName);
                Line(g, cmb[1], 126, y + 23, dc, R.FTiny);
                Line(g, cmb[2], 380, y + 2, Color.FromArgb(255, 235, 170), R.FHudName);
                Line(g, cmb[3], 380, y + 24, Color.FromArgb(190, 190, 215), R.FTiny);
                y += 48;
            }
            Panel(g, 110, y, 1060, 44);
            Line(g, "PERFEKT-Timing: Nächste Taste kurz NACH dem Trefferstopp drücken (nicht vorher hämmern) – Gegner bleibt länger betäubt,", 126, y + 4, Color.FromArgb(255, 225, 120), R.FTiny);
            Line(g, "perfekte Sprünge kosten nichts und zielen auf den Gegner. Die Ultimate-Leiste füllt sich durch Treffer (verteilt und eingesteckt).", 126, y + 23, Color.FromArgb(255, 225, 120), R.FTiny);
            return y + 44;
        }

        void DrawCombos(Graphics g)
        {
            DrawTitle(g, null);
            DrawItems(g, 390, 132, 500, 38, 0, 0, 1);
            float y = DrawComboRows(g, comboWeapon, 0, 176);
            DrawItems(g, 200, y + 6, 430, 32, 0, 1, 1);
            DrawItems(g, 650, y + 6, 430, 32, 0, 2, 1);
        }

        // Combo-Übersicht über dem laufenden Kampf
        void DrawComboOverlay(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb(238, 6, 2, 14))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            PlayerState P = state.p[comboViewSlot];
            CharDef c = CharDef.Look(P.charId, P.skin);
            R.Shadowed(g, "COMBOS & ANGRIFFE", R.FBig, Color.White, Renderer.VW / 2f, 12, true);
            R.Shadowed(g, "<  " + (names[comboViewSlot] ?? "P" + (comboViewSlot + 1)) + "  ·  " + c.Name + " (" + c.TypeName + ")  >", R.FSmall, c.Glow, Renderer.VW / 2f, 62, true);
            // im lokalen 2-Spieler-Modus spielt P2 mit der eigenen Belegung
            int pl = mode == Mode.Local && bot == null && comboViewSlot == 1 ? 1 : 0;
            float y = DrawComboRows(g, c.Weapon, pl, 96);
            string hint = "F1 / Esc / Controller-Back: zurück zum Kampf   ·   Links/Rechts: anderer Kämpfer   ·   " +
                (mode == Mode.Local ? "Das Spiel ist pausiert." : "Achtung: Das Spiel läuft weiter!");
            R.Shadowed(g, hint, R.FTiny, mode == Mode.Local ? Color.FromArgb(200, 200, 220) : Color.FromArgb(255, 200, 120), Renderer.VW / 2f, y + 12, true);
        }

        void DrawPause(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            R.Shadowed(g, "PAUSE", R.FTitle, Color.White, Renderer.VW / 2f, 160, true);
            if (mode != Mode.Local) R.Shadowed(g, "Das Spiel läuft im LAN-Modus weiter!", R.FSmall, Color.FromArgb(255, 200, 120), Renderer.VW / 2f, 250, true);
            DrawItems(g, 440, 300, 400, 52, 10);
        }

        void DrawResults(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(b, 0, 0, Renderer.VW, Renderer.VH);
            string head;
            Color hc = Color.White;
            if (state.winner < 0) head = "UNENTSCHIEDEN";
            else
            {
                head = (names[state.winner] ?? CharDef.All[state.p[state.winner].charId].Name).ToUpper() + " GEWINNT!";
                hc = CharDef.Look(state.p[state.winner].charId, state.p[state.winner].skin).Glow;
            }
            R.Shadowed(g, head, R.FTitle, hc, Renderer.VW / 2f, 70, true);
            if (tourMatch && tour != null && state.winner >= 0)
            {
                string next = tourR + 1 >= tour.Rounds ? "GEWINNT DAS TURNIER!" : "zieht ins " + Tournament.RoundName(tour.Size, tourR + 1) + " ein!";
                R.Shadowed(g, next, R.FMed, Color.FromArgb(255, 225, 140), Renderer.VW / 2f, 424, true);
            }
            if (state.winner >= 0) R.DrawPortrait(g, state.p[state.winner].charId, Renderer.VW / 2f, 400, 1.6f, frame, true, state.p[state.winner].skin);
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = state.p[i];
                float x = i == 0 ? 150 : 850;
                Panel(g, x, 170, 280, 210);
                CharDef c = CharDef.Look(P.charId, P.skin);
                Line(g, names[i] ?? c.Name, x + 20, 182, c.Glow, R.FMed);
                Line(g, c.Name, x + 20, 216, Color.FromArgb(180, 200, 190, 220), R.FTiny);
                Line(g, "Treffer:  " + P.statHits, x + 20, 245, Color.White);
                Line(g, "Schaden verursacht:  " + (int)P.statDamage, x + 20, 272, Color.White);
                Line(g, "Paraden:  " + P.statParries, x + 20, 299, Color.White);
                Line(g, "K.O.s:  " + P.statKOs, x + 20, 326, Color.White);
            }
            if (mode == Mode.Client)
                R.Shadowed(g, "Warte auf den Host (Revanche oder Lobby) ...", R.FMed, Color.FromArgb(255, 200, 120), Renderer.VW / 2f, 520, true);
            DrawItemsRow(g, mode == Mode.Client ? 600 : 560);
        }

        public void DebugScreen(Scr s) { scr = s; sel = 0; }
        public void DebugComboView() { StartLocal(); OpenComboView(); }

        public void Shutdown()
        {
            CloseHost();
            LeaveClient();
            Settings.Save();
        }
    }
}
