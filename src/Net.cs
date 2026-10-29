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
        public const byte Version = 8;
        public const byte Discover = 1, DiscoverReply = 2, Join = 3, Welcome = 4, Reject = 5, Lobby = 6, Input = 7,
            Snapshot = 8, Ping = 9, Pong = 10, Bye = 11, Hello = 12, Punch = 13, Rematch = 14, ReplayReq = 15, ReplayChunk = 16;
        // Rolle eines Teilnehmers (Welcome/Lobby)
        public const byte RolePlayer = 0, RoleSpectator = 1;
        // Wiederholungen werden nach dem Match in Stücken dieser Größe übertragen
        public const int ReplayChunkSize = 1024;

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

    // UDP-Socket. Online kann zusätzlich ein Relay (MQTT) hängen: Pakete an eine Relay-Adresse (0.0.0.1) laufen dann
    // über den Vermittlungsserver, eingehende Relay-Pakete erscheinen als Pakete von dieser Adresse. Host/Client merken davon nichts.
    // Der Host unterscheidet mehrere Relay-Teilnehmer (Spieler und Zuschauer) am Port der Adresse (= Relay-Kennung des Clients).
    public class NetSocket
    {
        static readonly IPAddress RelayAddr = new IPAddress(new byte[] { 0, 0, 0, 1 });
        public static readonly IPEndPoint RelayPeer = new IPEndPoint(RelayAddr, 1); // der Host aus Sicht eines Clients
        public static bool IsRelay(IPEndPoint ep) { return ep != null && ep.Address.Equals(RelayAddr); }

        Socket s;
        readonly byte[] rb = new byte[16384];
        public int LocalPort;
        public volatile MqttRelay Relay;
        // Themen: Clients senden an Basis/h (Spielpakete mit ihrer Kennung davor), der Host antwortet an Basis/c/<Kennung>.
        // Signale für den Verbindungsaufbau schickt der Host an alle (Basis/c), Clients an Basis/h.
        public string RelayBase;
        public bool RelayHost;
        public int RelayId;
        public string[] RelayTopics { get { return RelayHost ? new[] { RelayBase + "/h" } : new[] { RelayBase + "/c", RelayBase + "/c/" + RelayId }; } }
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
            var ep = to as IPEndPoint;
            if (IsRelay(ep))
            {
                MqttRelay r = Relay;
                if (r == null || RelayBase == null) return;
                int head = RelayHost ? 1 : 3;
                var p = new byte[d.Length + head];
                p[0] = (byte)'G';
                if (!RelayHost) { p[1] = (byte)(RelayId >> 8); p[2] = (byte)RelayId; }
                Buffer.BlockCopy(d, 0, p, head, d.Length);
                r.Publish(RelayHost ? RelayBase + "/c/" + ep.Port : RelayBase + "/h", p);
                return;
            }
            if (s == null) return;
            try { s.SendTo(d, to); } catch { }
        }

        public void SendSignal(string msg)
        {
            MqttRelay r = Relay;
            if (r == null || RelayBase == null) return;
            byte[] t = Encoding.UTF8.GetBytes(msg);
            var p = new byte[t.Length + 1];
            p[0] = (byte)'S';
            Buffer.BlockCopy(t, 0, p, 1, t.Length);
            r.Publish(RelayBase + (RelayHost ? "/c" : "/h"), p);
        }

        public bool TryGetSignal(out string msg)
        {
            PumpRelaySignals();
            if (signals.Count > 0) { msg = signals.Dequeue(); return true; }
            msg = null;
            return false;
        }

        // Signale können auch eintreffen, während niemand Spielpakete liest
        struct RelayPacket { public IPEndPoint From; public byte[] Data; public int Offset; }
        readonly Queue<RelayPacket> relayGame = new Queue<RelayPacket>();
        void PumpRelaySignals()
        {
            MqttRelay r = Relay;
            byte[] pl;
            while (r != null && r.TryReceive(out pl))
            {
                if (pl.Length == 0) continue;
                if (pl[0] == 'S') signals.Enqueue(Encoding.UTF8.GetString(pl, 1, pl.Length - 1));
                else if (pl[0] == 'G' && relayGame.Count < 500)
                {
                    if (!RelayHost) relayGame.Enqueue(new RelayPacket { From = RelayPeer, Data = pl, Offset = 1 });
                    else if (pl.Length >= 3) relayGame.Enqueue(new RelayPacket { From = new IPEndPoint(RelayAddr, pl[1] << 8 | pl[2]), Data = pl, Offset = 3 });
                }
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
                RelayPacket pk = relayGame.Dequeue();
                len = Math.Min(rb.Length, pk.Data.Length - pk.Offset);
                Buffer.BlockCopy(pk.Data, pk.Offset, rb, 0, len);
                from = pk.From;
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
    // Teilnehmer neben dem Spieler: Zuschauer bekommen Lobby und Snapshots, senden aber keine Eingaben.
    public class Peer
    {
        public IPEndPoint Ep;
        public string Name = "";
        public double LastRecv;
    }

    public class HostSession
    {
        public const int MaxSpectators = 4;
        public readonly NetSocket Sock = new NetSocket();
        public IPEndPoint Client;
        public string ClientName = "";
        public int Ping = -1;
        public readonly List<string> Messages = new List<string>();
        public readonly List<Peer> Spectators = new List<Peer>();
        public int HostChar, Stocks, Minutes, Stage, ClientChar = 1;
        public bool InMatch;
        public bool ClientJustJoined;
        // Der Spieler hat das laufende Match verlassen (auch wenn schon ein Zuschauer nachgerückt ist)
        public bool PlayerLeft;
        // Ergebnisbildschirm: Revanche-Wünsche beider Spieler und der Kämpfer des Clients für das nächste Match
        public bool Results, HostVote, ClientVote;
        public int ClientNextChar;

        double lastRecv, lastPing, lastLobby;
        readonly Queue<NetInput> queue = new Queue<NetInput>();
        int lastQueued, lastAck, lastHeld;
        readonly List<GameEvent> history = new List<GameEvent>();
        readonly List<int> historyTick = new List<int>();
        int nextEventId = 1;
        byte[] replayData;       // Wiederholung des letzten Matches (gepackt), wird auf Anfrage in Stücken verschickt
        int replayMatch = -1;

        public bool HasClient { get { return Client != null; } }
        public bool OverRelay { get { return NetSocket.IsRelay(Client); } }
        public bool Full { get { return HasClient && Spectators.Count >= MaxSpectators; } }

        public string Start(int port) { return Sock.Open(port); }

        Peer FindSpectator(IPEndPoint ep) { return Spectators.Find(p => p.Ep.Equals(ep)); }

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
            for (int i = Spectators.Count - 1; i >= 0; i--)
                if (now - Spectators[i].LastRecv > 5.0)
                {
                    Messages.Add(Spectators[i].Name + " schaut nicht mehr zu (Verbindung verloren).");
                    Spectators.RemoveAt(i);
                }
            if (HasClient && now - lastPing > 0.5)
            {
                lastPing = now;
                var w = Pk.Begin(Pk.Ping); w.Write(now); Sock.Send(Client, Pk.End(w));
            }
            if ((HasClient || Spectators.Count > 0) && !InMatch && now - lastLobby > 0.2)
            {
                lastLobby = now;
                SendLobby();
            }
        }

        void Handle(byte type, byte ver, BinaryReader r, IPEndPoint from, double now)
        {
            bool fromClient = HasClient && from.Equals(Client);
            Peer spec = fromClient ? null : FindSpectator(from);
            switch (type)
            {
                case Pk.Discover:
                    {
                        var w = Pk.Begin(Pk.DiscoverReply);
                        w.Write(Settings.PlayerName);
                        w.Write(Full ? (byte)2 : HasClient ? (byte)1 : (byte)0); // 1 = Spieler da, Zuschauen möglich
                        Sock.Send(from, Pk.End(w));
                        break;
                    }
                case Pk.Join:
                    {
                        string name = r.ReadString();
                        if (ver != Pk.Version) { SendReject(from, 2); return; }
                        if (string.IsNullOrEmpty(name)) name = "Spieler 2";
                        // Der erste wird Spieler 2, alle weiteren schauen zu
                        byte role = Pk.RolePlayer;
                        if (fromClient) lastRecv = now;
                        else if (spec != null) { spec.LastRecv = now; role = Pk.RoleSpectator; }
                        else if (!HasClient)
                        {
                            SetClient(from, name);
                            lastRecv = now;
                            Messages.Add(ClientName + " ist der Lobby beigetreten.");
                        }
                        else if (Spectators.Count < MaxSpectators)
                        {
                            Spectators.Add(new Peer { Ep = from, Name = name, LastRecv = now });
                            Messages.Add(name + " schaut zu.");
                            role = Pk.RoleSpectator;
                        }
                        else { SendReject(from, 1); return; }
                        var wl = Pk.Begin(Pk.Welcome);
                        wl.Write(Settings.PlayerName);
                        byte[] t = Tuning.ToBytes();
                        wl.Write((ushort)t.Length);
                        wl.Write(t);
                        wl.Write(role);
                        Sock.Send(from, Pk.End(wl));
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
                    else if (spec != null) { spec.LastRecv = now; spec.Name = r.ReadString(); }
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
                    if (fromClient || spec != null)
                    {
                        if (fromClient) lastRecv = now; else spec.LastRecv = now;
                        var w = Pk.Begin(Pk.Pong); w.Write(r.ReadDouble()); Sock.Send(from, Pk.End(w));
                    }
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
                    else if (spec != null)
                    {
                        Messages.Add(spec.Name + " schaut nicht mehr zu.");
                        Spectators.Remove(spec);
                    }
                    break;
                case Pk.Rematch:
                    if (!fromClient) return;
                    {
                        lastRecv = now;
                        int mid = r.ReadInt32();
                        bool vote = r.ReadByte() != 0;
                        int ch = r.ReadByte();
                        if (mid != MatchId || !Results) return; // verspätetes Paket aus einem anderen Match
                        ClientVote = vote;
                        if (ch < CharDef.Count && ch != HostChar) ClientNextChar = ch;
                        break;
                    }
                case Pk.ReplayReq:
                    if (!fromClient && spec == null) return;
                    {
                        int mid = r.ReadInt32(), off = r.ReadInt32(), n = Math.Min(8, (int)r.ReadByte());
                        if (replayData == null || mid != replayMatch || off < 0) return;
                        for (int i = 0; i < n && off < replayData.Length; i++, off += Pk.ReplayChunkSize)
                        {
                            int len = Math.Min(Pk.ReplayChunkSize, replayData.Length - off);
                            var w = Pk.Begin(Pk.ReplayChunk);
                            w.Write(mid); w.Write(replayData.Length); w.Write(off); w.Write((ushort)len); w.Write(replayData, off, len);
                            Sock.Send(from, Pk.End(w));
                        }
                        break;
                    }
            }
        }

        void SetClient(IPEndPoint ep, string name)
        {
            Client = ep;
            ClientName = name;
            ClientJustJoined = true;
            ClientChar = CharDef.Next(HostChar, 1, HostChar);
            Ping = -1;
            queue.Clear(); lastQueued = 0; lastAck = 0; lastHeld = 0;
        }

        void SendReject(IPEndPoint to, byte reason)
        {
            var w = Pk.Begin(Pk.Reject); w.Write(reason); Sock.Send(to, Pk.End(w));
        }

        byte[] LobbyPacket(byte role)
        {
            var w = Pk.Begin(Pk.Lobby);
            w.Write(Settings.PlayerName);
            w.Write(HasClient ? ClientName : "");
            w.Write((byte)HostChar);
            w.Write((byte)ClientChar);
            w.Write((byte)Stocks);
            w.Write((byte)Minutes);
            w.Write((byte)Stage);
            w.Write(role);
            w.Write((byte)Spectators.Count);
            return Pk.End(w);
        }

        public void SendLobby()
        {
            if (HasClient) Sock.Send(Client, LobbyPacket(Pk.RolePlayer));
            if (Spectators.Count == 0) return;
            byte[] b = LobbyPacket(Pk.RoleSpectator);
            foreach (Peer p in Spectators) Sock.Send(p.Ep, b);
        }

        void DropClient()
        {
            Client = null;
            Ping = -1;
            queue.Clear();
            if (InMatch) PlayerLeft = true;
            // der erste Zuschauer rückt als Spieler 2 nach
            if (Spectators.Count > 0)
            {
                Peer p = Spectators[0];
                Spectators.RemoveAt(0);
                SetClient(p.Ep, p.Name);
                lastRecv = p.LastRecv;
                Messages.Add(p.Name + " rückt als Spieler 2 nach.");
            }
        }

        public string SpectatorNames()
        {
            var n = new List<string>();
            foreach (Peer p in Spectators) n.Add(p.Name);
            return string.Join(", ", n.ToArray());
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
            Results = HostVote = ClientVote = false;
            PlayerLeft = false;
            replayData = null;
        }

        // Wiederholung des gerade beendeten Matches für Client und Zuschauer bereitstellen
        public void OfferReplay(byte[] data)
        {
            replayData = data;
            replayMatch = MatchId;
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
            if (!HasClient && Spectators.Count == 0) return;
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
            // Ergebnisbildschirm: Revanche-Wünsche, gewählte Kämpfer, Größe der bereitliegenden Wiederholung
            w.Write((byte)((HostVote ? 1 : 0) | (ClientVote ? 2 : 0)));
            w.Write((byte)HostChar);
            w.Write((byte)ClientNextChar);
            w.Write(replayData != null && replayMatch == MatchId ? replayData.Length : 0);
            byte[] b = Pk.End(w);
            // Relay: 30 Snapshots/s genügen
            bool odd = (s.tick & 1) == 1;
            if (HasClient && !(odd && OverRelay)) Sock.Send(Client, b);
            foreach (Peer p in Spectators)
                if (!(odd && NetSocket.IsRelay(p.Ep))) Sock.Send(p.Ep, b);
        }

        public void Close()
        {
            var w = Pk.Begin(Pk.Bye); w.Write((byte)0); byte[] b = Pk.End(w);
            for (int i = 0; i < 3; i++)
            {
                if (HasClient) Sock.Send(Client, b);
                foreach (Peer p in Spectators) Sock.Send(p.Ep, b);
            }
            Sock.Close();
            Client = null;
            Spectators.Clear();
        }
    }

    // ===================================================================== CLIENT
    // Full: Spieler und alle Zuschauerplätze besetzt; Spectate: Spieler 2 ist schon da, Beitritt nur als Zuschauer
    public class FoundHost { public string Name, Ip; public bool Full, Spectate; public double Seen; }

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
        // Zuschauer: bekommt Lobby und Snapshots, sendet keine Eingaben. MyChar ist dann der Kämpfer von Spieler 2.
        public bool Spectator;
        public string PlayerName = ""; // Name von Spieler 2 laut Lobby
        public int Spectators;

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

        // Ergebnisbildschirm: eigener Revanche-Wunsch und Kämpfer (vom Spiel gesetzt), Stand beim Host (aus dem Snapshot)
        public bool InResults, MyVote;
        public int NextChar;
        public bool HostVote, ClientVote;
        public int HostNextChar, ClientNextChar, ReplayLen;
        double lastRematch, lastReplayReq;
        // Wiederholung vom Host: wird nach dem Match in Stücken geladen, fertig liegt sie in ReplayData
        byte[] replayBuf;
        bool[] replayGot;
        int replayMid = -1;
        bool replayComplete;
        public byte[] ReplayData;

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

        public bool OverRelay { get { return NetSocket.IsRelay(Host); } }

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
            MatchId = -1;
            Spectator = false;
            InResults = MyVote = false;
            ReplayLen = 0;
            replayMid = -1;
            ReplayData = null;
        }

        // Anteil der schon empfangenen Wiederholung (0..1), -1 = keine angeboten
        public float ReplayProgress
        {
            get
            {
                if (ReplayLen <= 0 || replayGot == null || replayMid != MatchId) return -1;
                int n = 0;
                foreach (bool b in replayGot) if (b) n++;
                return n / (float)replayGot.Length;
            }
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
                if (InResults && !Spectator && now - lastRematch > 0.2) { lastRematch = now; SendRematch(); }
                RequestReplay(now);
            }
        }

        void Handle(byte type, BinaryReader r, IPEndPoint from, double now)
        {
            if (type == Pk.DiscoverReply)
            {
                string name = r.ReadString();
                int st = r.ReadByte();
                string ip = from.Address.ToString();
                FoundHost f = Found.Find(x => x.Ip == ip);
                if (f == null) { f = new FoundHost { Ip = ip }; Found.Add(f); }
                f.Name = name; f.Full = st == 2; f.Spectate = st == 1; f.Seen = now;
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
                    Spectator = r.ReadByte() == Pk.RoleSpectator;
                    Status = Connected;
                    break;
                case Pk.Reject:
                    {
                        byte reason = r.ReadByte();
                        Status = Failed;
                        Error = reason == 1 ? "Die Lobby ist voll (Spieler 2 und alle " + HostSession.MaxSpectators + " Zuschauerplätze sind besetzt)." :
                                reason == 2 ? "Host und Client verwenden unterschiedliche Spielversionen." : "Beitritt abgelehnt.";
                        break;
                    }
                case Pk.Lobby:
                    HostName = r.ReadString();
                    PlayerName = r.ReadString();
                    HostChar = r.ReadByte();
                    MyChar = r.ReadByte();
                    Stocks = r.ReadByte();
                    Minutes = r.ReadByte();
                    Stage = StageDef.Valid(r.ReadByte());
                    Spectator = r.ReadByte() == Pk.RoleSpectator;
                    Spectators = r.ReadByte();
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
                        int votes = r.ReadByte(), hostNext = r.ReadByte(), clientNext = r.ReadByte(), replayLen = r.ReadInt32();
                        if (tick >= AuthTick)
                        {
                            // Ergebnisbildschirm: auch aus einem Snapshot desselben Ticks übernehmen
                            HostVote = (votes & 1) != 0;
                            ClientVote = (votes & 2) != 0;
                            HostNextChar = hostNext;
                            ClientNextChar = clientNext;
                            ReplayLen = replayLen;
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
                case Pk.ReplayChunk:
                    {
                        int mid = r.ReadInt32(), total = r.ReadInt32(), off = r.ReadInt32(), n = r.ReadUInt16();
                        byte[] part = r.ReadBytes(n);
                        if (mid != replayMid || replayBuf == null || total != replayBuf.Length || off < 0 || off % Pk.ReplayChunkSize != 0 || off + part.Length > total) break;
                        Buffer.BlockCopy(part, 0, replayBuf, off, part.Length);
                        replayGot[off / Pk.ReplayChunkSize] = true;
                        if (!replayComplete && Array.IndexOf(replayGot, false) < 0) { replayComplete = true; ReplayData = replayBuf; }
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

        // Fehlende Stücke der angebotenen Wiederholung anfordern (bis zu 8 auf einmal, verlorene werden neu angefragt)
        void RequestReplay(double now)
        {
            if (ReplayLen <= 0) return;
            if (replayMid != MatchId)
            {
                replayMid = MatchId;
                replayBuf = new byte[ReplayLen];
                replayGot = new bool[(ReplayLen + Pk.ReplayChunkSize - 1) / Pk.ReplayChunkSize];
                replayComplete = false;
                ReplayData = null;
            }
            if (replayComplete || now - lastReplayReq < 0.15) return;
            lastReplayReq = now;
            int first = Array.IndexOf(replayGot, false);
            var w = Pk.Begin(Pk.ReplayReq);
            w.Write(replayMid); w.Write(first * Pk.ReplayChunkSize); w.Write((byte)8);
            Sock.Send(Host, Pk.End(w));
        }

        public void SendHello()
        {
            if (Host == null) return;
            var w = Pk.Begin(Pk.Hello);
            w.Write(Settings.PlayerName);
            w.Write((byte)WantChar);
            Sock.Send(Host, Pk.End(w));
        }

        void SendRematch()
        {
            var w = Pk.Begin(Pk.Rematch);
            w.Write(MatchId);
            w.Write(MyVote ? (byte)1 : (byte)0);
            w.Write((byte)NextChar);
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
