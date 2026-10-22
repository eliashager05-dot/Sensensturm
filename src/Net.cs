using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Sensensturm
{
    // LAN-Netzwerk über UDP.
    // Architektur: Der Host simuliert das Spiel autoritativ (Treffer, Block, Parade, KO entscheidet nur er).
    // Der Client schickt pro Tick seine Eingaben (redundant, gegen Paketverlust), der Host schickt pro Tick
    // einen vollständigen Zustands-Snapshot + Ereignisse. Der Client sagt seine eigene Bewegung voraus
    // (Client-Side Prediction) und gleicht sie mit jedem Snapshot ab (Reconciliation).
    public static class Pk
    {
        public const byte Version = 7;
        public const byte Discover = 1, DiscoverReply = 2, Join = 3, Welcome = 4, Reject = 5, Lobby = 6, Input = 7,
            Snapshot = 8, Ping = 9, Pong = 10, Bye = 11, Hello = 12, Punch = 13;

        public static BinaryWriter Begin(byte type)
        {
            var w = new BinaryWriter(new MemoryStream());
            w.Write((byte)'S'); w.Write((byte)'Z'); w.Write(Version); w.Write(type);
            return w;
        }

        public static byte[] End(BinaryWriter w) { return ((MemoryStream)w.BaseStream).ToArray(); }

        public static bool Parse(byte[] d, int len, out byte type, out byte ver, out BinaryReader r)
        {
            type = 0; ver = 0; r = null;
            if (len < 4 || d[0] != 'S' || d[1] != 'Z') return false;
            ver = d[2]; type = d[3];
            r = new BinaryReader(new MemoryStream(d, 4, len - 4));
            return true;
        }
    }

    public static class NetUtil
    {
        public static List<string> LocalIPs()
        {
            var res = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) res.Add(ua.Address.ToString());
                }
            }
            catch { }
            if (res.Count == 0) res.Add("127.0.0.1");
            // private LAN-Adressen zuerst
            res.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return res;
        }

        static int Rank(string ip)
        {
            if (ip.StartsWith("192.168.")) return 0;
            if (ip.StartsWith("10.")) return 1;
            if (ip.StartsWith("172.")) return 2;
            if (ip.StartsWith("169.254.")) return 5;
            return 3;
        }

        public static List<IPAddress> Broadcasts()
        {
            var res = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] ip = ua.Address.GetAddressBytes(), m = ua.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~m[i]);
                        res.Add(new IPAddress(b));
                    }
                }
            }
            catch { }
            return res;
        }
    }

    // UDP-Socket. Online kann zusätzlich ein Relay (MQTT) hängen: Pakete an RelayPeer laufen dann über den
    // Vermittlungsserver, eingehende Relay-Pakete erscheinen als Pakete von RelayPeer. Host/Client merken davon nichts.
    public class NetSocket
    {
        public static readonly IPEndPoint RelayPeer = new IPEndPoint(new IPAddress(new byte[] { 0, 0, 0, 1 }), 1);

        Socket s;
        readonly byte[] rb = new byte[16384];
        public int LocalPort;
        public volatile MqttRelay Relay;
        public string RelayIn, RelayOut;
        public volatile IPEndPoint StunResult;
        public bool AutoPunchReply;
        readonly Queue<string> signals = new Queue<string>();
        readonly List<IPEndPoint> punchFrom = new List<IPEndPoint>();
        byte[] stunTx;

        public string Open(int port)
        {
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                try { s.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { } // SIO_UDP_CONNRESET aus
                s.Bind(new IPEndPoint(IPAddress.Any, port));
                s.Blocking = false;
                LocalPort = ((IPEndPoint)s.LocalEndPoint).Port;
                return null;
            }
            catch (SocketException e)
            {
                Close();
                if (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    return "Port " + port + " wird bereits verwendet (läuft schon eine Lobby?). Wähle in den Einstellungen einen anderen Port.";
                return "Netzwerk konnte nicht geöffnet werden: " + e.Message;
            }
        }

        public void Send(EndPoint to, byte[] d)
        {
            if (to == null) return;
            if (to.Equals(RelayPeer))
            {
                MqttRelay r = Relay;
                if (r != null && RelayOut != null)
                {
                    var p = new byte[d.Length + 1];
                    p[0] = (byte)'G';
                    Buffer.BlockCopy(d, 0, p, 1, d.Length);
                    r.Publish(RelayOut, p);
                }
                return;
            }
            if (s == null) return;
            try { s.SendTo(d, to); } catch { }
        }

        public void SendSignal(string msg)
        {
            MqttRelay r = Relay;
            if (r == null || RelayOut == null) return;
            byte[] t = Encoding.UTF8.GetBytes(msg);
            var p = new byte[t.Length + 1];
            p[0] = (byte)'S';
            Buffer.BlockCopy(t, 0, p, 1, t.Length);
            r.Publish(RelayOut, p);
        }

        public bool TryGetSignal(out string msg)
        {
            PumpRelaySignals();
            if (signals.Count > 0) { msg = signals.Dequeue(); return true; }
            msg = null;
            return false;
        }

        // Signale können auch eintreffen, während niemand Spielpakete liest
        readonly Queue<byte[]> relayGame = new Queue<byte[]>();
        void PumpRelaySignals()
        {
            MqttRelay r = Relay;
            byte[] pl;
            while (r != null && r.TryReceive(out pl))
            {
                if (pl.Length == 0) continue;
                if (pl[0] == 'S') signals.Enqueue(Encoding.UTF8.GetString(pl, 1, pl.Length - 1));
                else if (pl[0] == 'G' && relayGame.Count < 500) relayGame.Enqueue(pl);
            }
        }

        public void SendPunch(IPEndPoint to) { Send(to, new byte[] { (byte)'S', (byte)'Z', Pk.Version, Pk.Punch }); }

        public IPEndPoint FirstPunchFrom(List<IPEndPoint> cands)
        {
            foreach (var p in punchFrom)
                foreach (var c in cands)
                    if (p.Address.Equals(c.Address)) return p;
            return null;
        }

        public void SendStunRequest(IPEndPoint server)
        {
            if (stunTx == null) { stunTx = new byte[12]; new Random().NextBytes(stunTx); }
            var req = new byte[20];
            req[1] = 1; // Binding Request
            req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;
            Buffer.BlockCopy(stunTx, 0, req, 8, 12);
            Send(server, req);
        }

        bool TryStun(byte[] d, int len)
        {
            if (len < 20 || d[0] != 0x01 || d[1] != 0x01 || d[4] != 0x21 || d[5] != 0x12 || d[6] != 0xA4 || d[7] != 0x42) return false;
            int i = 20;
            while (i + 4 <= len)
            {
                int t = d[i] * 256 + d[i + 1], l = d[i + 2] * 256 + d[i + 3];
                if ((t == 0x20 || t == 0x01) && l >= 8 && d[i + 5] == 1 && StunResult == null)
                {
                    bool x = t == 0x20;
                    int port = d[i + 6] * 256 + d[i + 7];
                    if (x) port ^= 0x2112;
                    var ip = new byte[4];
                    byte[] cookie = { 0x21, 0x12, 0xA4, 0x42 };
                    for (int k = 0; k < 4; k++) ip[k] = (byte)(x ? d[i + 8 + k] ^ cookie[k] : d[i + 8 + k]);
                    StunResult = new IPEndPoint(new IPAddress(ip), port);
                }
                i += 4 + ((l + 3) & ~3);
            }
            return true;
        }

        public bool Recv(out byte[] data, out int len, out IPEndPoint from)
        {
            data = rb; len = 0; from = null;
            for (int guard = 0; guard < 64 && s != null; guard++)
            {
                try
                {
                    if (s.Available <= 0) break;
                    EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    len = s.ReceiveFrom(rb, ref ep);
                    from = (IPEndPoint)ep;
                }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { return false; }
                if (TryStun(rb, len)) continue;
                if (len == 4 && rb[0] == 'S' && rb[1] == 'Z' && rb[3] == Pk.Punch)
                {
                    if (!punchFrom.Contains(from)) punchFrom.Add(from);
                    if (AutoPunchReply) SendPunch(from);
                    continue;
                }
                return true;
            }
            PumpRelaySignals();
            if (relayGame.Count > 0)
            {
                byte[] pl = relayGame.Dequeue();
                len = Math.Min(rb.Length, pl.Length - 1);
                Buffer.BlockCopy(pl, 1, rb, 0, len);
                from = RelayPeer;
                return true;
            }
            len = 0;
            return false;
        }

        public void Close()
        {
            try { if (s != null) s.Close(); } catch { }
            s = null;
            MqttRelay r = Relay;
            Relay = null;
            if (r != null) r.Close();
        }
    }

    public struct NetInput { public int Seq, Held, Pressed; }

    // ===================================================================== HOST
    public class HostSession
    {
        public readonly NetSocket Sock = new NetSocket();
        public IPEndPoint Client;
        public string ClientName = "";
        public int Ping = -1;
        public readonly List<string> Messages = new List<string>();
        public int HostChar, Stocks, Minutes, Stage, ClientChar = 1;
        public bool InMatch;
        public bool ClientJustJoined;

        double lastRecv, lastPing, lastLobby;
        readonly Queue<NetInput> queue = new Queue<NetInput>();
        int lastQueued, lastAck, lastHeld;
        readonly List<GameEvent> history = new List<GameEvent>();
        readonly List<int> historyTick = new List<int>();
        int nextEventId = 1;

        public bool HasClient { get { return Client != null; } }
        public bool OverRelay { get { return Client != null && Client.Equals(NetSocket.RelayPeer); } }

        public string Start(int port) { return Sock.Open(port); }

        public void Poll(double now)
        {
            byte[] d; int len; IPEndPoint from;
            int guard = 0;
            while (guard++ < 200 && Sock.Recv(out d, out len, out from))
            {
                byte type, ver; BinaryReader r;
                if (from == null || !Pk.Parse(d, len, out type, out ver, out r)) continue;
                try { Handle(type, ver, r, from, now); } catch { }
            }
            if (HasClient && now - lastRecv > 5.0)
            {
                Messages.Add("Verbindung zu " + ClientName + " verloren (Zeitüberschreitung).");
                DropClient();
            }
            if (HasClient && now - lastPing > 0.5)
            {
                lastPing = now;
                var w = Pk.Begin(Pk.Ping); w.Write(now); Sock.Send(Client, Pk.End(w));
            }
            if (HasClient && !InMatch && now - lastLobby > 0.2)
            {
                lastLobby = now;
                SendLobby();
            }
        }

        void Handle(byte type, byte ver, BinaryReader r, IPEndPoint from, double now)
        {
            bool fromClient = HasClient && from.Equals(Client);
            switch (type)
            {
                case Pk.Discover:
                    {
                        var w = Pk.Begin(Pk.DiscoverReply);
                        w.Write(Settings.PlayerName);
                        w.Write(HasClient ? (byte)1 : (byte)0);
                        Sock.Send(from, Pk.End(w));
                        break;
                    }
                case Pk.Join:
                    {
                        string name = r.ReadString();
                        if (ver != Pk.Version) { SendReject(from, 2); return; }
                        if (HasClient && !fromClient) { SendReject(from, 1); return; }
                        if (!HasClient)
                        {
                            Client = from;
                            ClientName = string.IsNullOrEmpty(name) ? "Spieler 2" : name;
                            ClientJustJoined = true;
                            ClientChar = CharDef.Next(HostChar, 1, HostChar);
                            Messages.Add(ClientName + " ist der Lobby beigetreten.");
                            queue.Clear(); lastQueued = 0; lastAck = 0; lastHeld = 0;
                        }
                        lastRecv = now;
                        var w = Pk.Begin(Pk.Welcome);
                        w.Write(Settings.PlayerName);
                        byte[] t = Tuning.ToBytes();
                        w.Write((ushort)t.Length);
                        w.Write(t);
                        Sock.Send(from, Pk.End(w));
                        SendLobby();
                        break;
                    }
                case Pk.Hello:
                    if (fromClient)
                    {
                        lastRecv = now;
                        ClientName = r.ReadString();
                        if (r.BaseStream.Position < r.BaseStream.Length)
                        {
                            int want = r.ReadByte();
                            if (want < CharDef.Count && want != HostChar && !InMatch) ClientChar = want;
                        }
                    }
                    break;
                case Pk.Input:
                    if (!fromClient) return;
                    {
                        lastRecv = now;
                        int newest = r.ReadInt32();
                        int n = r.ReadByte();
                        var tmp = new NetInput[n];
                        for (int i = 0; i < n; i++) tmp[i] = new NetInput { Seq = newest - i, Held = r.ReadUInt16(), Pressed = r.ReadUInt16() };
                        for (int i = n - 1; i >= 0; i--)
                            if (tmp[i].Seq > lastQueued) { queue.Enqueue(tmp[i]); lastQueued = tmp[i].Seq; }
                        break;
                    }
                case Pk.Ping:
                    if (fromClient) { lastRecv = now; var w = Pk.Begin(Pk.Pong); w.Write(r.ReadDouble()); Sock.Send(from, Pk.End(w)); }
                    break;
                case Pk.Pong:
                    if (fromClient) { lastRecv = now; Ping = (int)((now - r.ReadDouble()) * 1000); }
                    break;
                case Pk.Bye:
                    if (fromClient)
                    {
                        Messages.Add(ClientName + " hat die Verbindung getrennt.");
                        DropClient();
                    }
                    break;
            }
        }

        void SendReject(IPEndPoint to, byte reason)
        {
            var w = Pk.Begin(Pk.Reject); w.Write(reason); Sock.Send(to, Pk.End(w));
        }

        public void SendLobby()
        {
            if (!HasClient) return;
            var w = Pk.Begin(Pk.Lobby);
            w.Write(Settings.PlayerName);
            w.Write(ClientName);
            w.Write((byte)HostChar);
            w.Write((byte)ClientChar);
            w.Write((byte)Stocks);
            w.Write((byte)Minutes);
            w.Write((byte)Stage);
            Sock.Send(Client, Pk.End(w));
        }

        void DropClient()
        {
            Client = null;
            Ping = -1;
            queue.Clear();
        }

        public int MatchId;

        public void ResetMatch()
        {
            MatchId++;
            queue.Clear();
            history.Clear();
            historyTick.Clear();
            lastAck = lastQueued;
            lastHeld = 0;
        }

        // Eine Client-Eingabe pro Host-Tick; wächst die Warteschlange (Jitter), wird aufgeholt.
        public void TakeInput(out int held, out int pressed)
        {
            pressed = 0;
            if (queue.Count == 0) { held = lastHeld; return; }
            NetInput i = queue.Dequeue();
            pressed |= i.Pressed;
            while (queue.Count > 3) { i = queue.Dequeue(); pressed |= i.Pressed; }
            held = i.Held;
            lastHeld = held;
            lastAck = i.Seq;
        }

        public void SendSnapshot(GameState s, List<GameEvent> newEvents)
        {
            foreach (var e in newEvents) { e.Id = nextEventId++; history.Add(e); historyTick.Add(s.tick); }
            while (historyTick.Count > 0 && historyTick[0] < s.tick - 40) { history.RemoveAt(0); historyTick.RemoveAt(0); }
            if (!HasClient) return;
            if (OverRelay && (s.tick & 1) == 1) return; // Relay: 30 Snapshots/s genügen
            var w = Pk.Begin(Pk.Snapshot);
            w.Write(s.tick);
            w.Write(MatchId);
            w.Write(lastAck);
            s.Write(w);
            int n = Math.Min(60, history.Count);
            w.Write((byte)n);
            for (int i = history.Count - n; i < history.Count; i++)
            {
                GameEvent e = history[i];
                w.Write(e.Id); w.Write(e.Type); w.Write(e.Player); w.Write(e.X); w.Write(e.Y); w.Write(e.V);
            }
            Sock.Send(Client, Pk.End(w));
        }

        public void Close()
        {
            if (HasClient) { var w = Pk.Begin(Pk.Bye); w.Write((byte)0); byte[] b = Pk.End(w); for (int i = 0; i < 3; i++) Sock.Send(Client, b); }
            Sock.Close();
            Client = null;
        }
    }

    // ===================================================================== CLIENT
    public class FoundHost { public string Name, Ip; public bool Full; public double Seen; }

    public class ClientSession
    {
        public readonly NetSocket Sock = new NetSocket();
        public IPEndPoint Host;
        public const int Idle = 0, Connecting = 1, Connected = 2, Failed = 3;
        public int Status = Idle;
        public string Error = "";
        public string HostName = "";
        public int HostChar, Stocks = 3, Minutes = 5, Stage, Ping = -1, MyChar = 1, WantChar = 1;
        public bool LobbyReceived;
        public readonly List<FoundHost> Found = new List<FoundHost>();
        public bool Opened;

        double connectStart, lastJoin, lastRecv, lastPing, lastHello, lastDiscover;
        int discoverPort;

        // Vorhersage
        public readonly List<NetInput> Pending = new List<NetInput>();
        int seq;
        public GameState Auth;
        public int AuthTick = -1, Ack;
        public bool NewSnapshot;
        public readonly List<GameEvent> NewEvents = new List<GameEvent>();
        int lastEventId;
        public double LastSnapshotTime;
        public int MatchId = -1;
        public bool NewMatch;
        public byte[] SavedTuning;

        public string Open()
        {
            if (Opened) return null;
            string e = Sock.Open(0);
            Opened = e == null;
            return e;
        }

        public void Discover(double now, int port)
        {
            discoverPort = port;
            if (now - lastDiscover < 1.0) return;
            lastDiscover = now;
            byte[] b = Pk.End(Pk.Begin(Pk.Discover));
            foreach (var a in NetUtil.Broadcasts()) Sock.Send(new IPEndPoint(a, port), b);
            Found.RemoveAll(f => now - f.Seen > 4.0);
        }

        public void Connect(string ip, int port, double now)
        {
            IPAddress addr;
            Error = "";
            if (!IPAddress.TryParse(ip.Trim(), out addr) || addr.AddressFamily != AddressFamily.InterNetwork)
            {
                Status = Failed;
                Error = "Ungültige IP-Adresse \"" + ip + "\". Beispiel: 192.168.0.23";
                return;
            }
            ConnectEndpoint(new IPEndPoint(addr, port), now);
        }

        public bool OverRelay { get { return Host != null && Host.Equals(NetSocket.RelayPeer); } }

        public void ConnectEndpoint(IPEndPoint ep, double now)
        {
            Error = "";
            Host = ep;
            Status = Connecting;
            connectStart = now;
            lastJoin = 0;
            LobbyReceived = false;
            AuthTick = -1;
            Auth = null;
            lastEventId = 0;
            Pending.Clear();
            seq = 0;
        }

        public void Poll(double now)
        {
            byte[] d; int len; IPEndPoint from;
            int guard = 0;
            NewSnapshot = false;
            while (guard++ < 300 && Sock.Recv(out d, out len, out from))
            {
                byte type, ver; BinaryReader r;
                if (from == null || !Pk.Parse(d, len, out type, out ver, out r)) continue;
                try { Handle(type, r, from, now); } catch { }
            }

            if (Status == Connecting)
            {
                if (now - lastJoin > 0.25)
                {
                    lastJoin = now;
                    var w = Pk.Begin(Pk.Join); w.Write(Settings.PlayerName); Sock.Send(Host, Pk.End(w));
                }
                if (now - connectStart > 6.0)
                {
                    Status = Failed;
                    Error = OverRelay ? "Keine Antwort vom Host über das Relay. Ist das Online-Spiel noch geöffnet?" : "Keine Antwort von " + Host + ". Prüfe IP-Adresse und Port, ob der Host die Lobby geöffnet hat, ob beide PCs im selben Netzwerk sind und ob die Windows-Firewall Sensensturm erlaubt.";
                }
            }
            else if (Status == Connected)
            {
                if (now - lastRecv > 5.0)
                {
                    Status = Failed;
                    Error = "Verbindung zum Host verloren (seit 5 Sekunden keine Daten).";
                }
                if (now - lastPing > 0.5) { lastPing = now; var w = Pk.Begin(Pk.Ping); w.Write(now); Sock.Send(Host, Pk.End(w)); }
                if (now - lastHello > 0.5) { lastHello = now; SendHello(); }
            }
        }

        void Handle(byte type, BinaryReader r, IPEndPoint from, double now)
        {
            if (type == Pk.DiscoverReply)
            {
                string name = r.ReadString();
                bool full = r.ReadByte() != 0;
                string ip = from.Address.ToString();
                FoundHost f = Found.Find(x => x.Ip == ip);
                if (f == null) { f = new FoundHost { Ip = ip }; Found.Add(f); }
                f.Name = name; f.Full = full; f.Seen = now;
                return;
            }
            if (Host == null || !from.Equals(Host)) return;
            lastRecv = now;
            switch (type)
            {
                case Pk.Welcome:
                    if (Status != Connecting) break;
                    HostName = r.ReadString();
                    {
                        int n = r.ReadUInt16();
                        byte[] t = r.ReadBytes(n);
                        if (SavedTuning == null) SavedTuning = Tuning.ToBytes();
                        try { Tuning.FromBytes(t); } catch { }
                    }
                    Status = Connected;
                    break;
                case Pk.Reject:
                    {
                        byte reason = r.ReadByte();
                        Status = Failed;
                        Error = reason == 1 ? "Die Lobby ist bereits voll (es ist schon ein Spieler verbunden)." :
                                reason == 2 ? "Host und Client verwenden unterschiedliche Spielversionen." : "Beitritt abgelehnt.";
                        break;
                    }
                case Pk.Lobby:
                    HostName = r.ReadString();
                    r.ReadString();
                    HostChar = r.ReadByte();
                    MyChar = r.ReadByte();
                    Stocks = r.ReadByte();
                    Minutes = r.ReadByte();
                    Stage = StageDef.Valid(r.ReadByte());
                    LobbyReceived = true;
                    AuthTick = -1;
                    break;
                case Pk.Snapshot:
                    {
                        int tick = r.ReadInt32();
                        int mid = r.ReadInt32();
                        int ack = r.ReadInt32();
                        if (mid != MatchId) { MatchId = mid; AuthTick = -1; Pending.Clear(); NewMatch = true; }
                        GameState s = GameState.Read(r);
                        int n = r.ReadByte();
                        for (int i = 0; i < n; i++)
                        {
                            var e = new GameEvent { Id = r.ReadInt32(), Type = r.ReadByte(), Player = r.ReadByte(), X = r.ReadSingle(), Y = r.ReadSingle(), V = r.ReadSingle() };
                            if (e.Id > lastEventId) { NewEvents.Add(e); lastEventId = e.Id; }
                        }
                        if (tick > AuthTick || AuthTick < 0)
                        {
                            AuthTick = tick;
                            Ack = ack;
                            Auth = s;
                            NewSnapshot = true;
                            LastSnapshotTime = now;
                            LobbyReceived = false;
                            Pending.RemoveAll(p => p.Seq <= ack);
                        }
                        break;
                    }
                case Pk.Ping:
                    { var w = Pk.Begin(Pk.Pong); w.Write(r.ReadDouble()); Sock.Send(Host, Pk.End(w)); break; }
                case Pk.Pong:
                    Ping = (int)((now - r.ReadDouble()) * 1000);
                    break;
                case Pk.Bye:
                    Status = Failed;
                    Error = "Der Host hat die Lobby geschlossen.";
                    break;
            }
        }

        public void SendHello()
        {
            if (Host == null) return;
            var w = Pk.Begin(Pk.Hello);
            w.Write(Settings.PlayerName);
            w.Write((byte)WantChar);
            Sock.Send(Host, Pk.End(w));
        }

        public NetInput SendInput(int held, int pressed)
        {
            var ni = new NetInput { Seq = ++seq, Held = held, Pressed = pressed };
            Pending.Add(ni);
            if (Pending.Count > 120) Pending.RemoveAt(0);
            var w = Pk.Begin(Pk.Input);
            w.Write(ni.Seq);
            int n = Math.Min(12, Pending.Count);
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                NetInput p = Pending[Pending.Count - 1 - i];
                w.Write((ushort)p.Held);
                w.Write((ushort)p.Pressed);
            }
            Sock.Send(Host, Pk.End(w));
            return ni;
        }

        public void RestoreTuning()
        {
            if (SavedTuning != null) { try { Tuning.FromBytes(SavedTuning); } catch { } SavedTuning = null; }
        }

        public void Close()
        {
            if (Host != null && (Status == Connected || Status == Connecting))
            {
                var w = Pk.Begin(Pk.Bye); w.Write((byte)0); byte[] b = Pk.End(w);
                for (int i = 0; i < 3; i++) Sock.Send(Host, b);
            }
            Sock.Close();
            Opened = false;
            Status = Idle;
            RestoreTuning();
        }
    }
}
