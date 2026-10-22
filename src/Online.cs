using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Sensensturm
{
    // Minimaler MQTT-3.1.1-Client (nur QoS 0). Dient als Vermittlung (Raum-Code) und als Relay,
    // falls keine direkte Verbindung zwischen den Routern möglich ist. Beide Spieler öffnen nur
    // ausgehende Verbindungen – funktioniert daher auch hinter strengen Firewalls.
    public class MqttRelay
    {
        static readonly string[] Brokers = { "broker.hivemq.com", "broker.emqx.io", "test.mosquitto.org" };

        TcpClient tcp;
        NetworkStream ns;
        volatile bool alive;
        readonly object wlk = new object(), rlk = new object();
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        public string Error, BrokerName;
        public bool Connected { get { return alive; } }

        static void AddStr(List<byte> b, string s)
        {
            byte[] d = Encoding.UTF8.GetBytes(s);
            b.Add((byte)(d.Length >> 8)); b.Add((byte)(d.Length & 255));
            b.AddRange(d);
        }

        void WritePacket(byte hdr, byte[] body)
        {
            var ms = new MemoryStream();
            ms.WriteByte(hdr);
            int l = body.Length;
            do { byte d = (byte)(l % 128); l /= 128; if (l > 0) d |= 128; ms.WriteByte(d); } while (l > 0);
            ms.Write(body, 0, body.Length);
            byte[] a = ms.ToArray();
            lock (wlk) ns.Write(a, 0, a.Length);
        }

        int ReadLen()
        {
            int mult = 1, val = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = ns.ReadByte();
                if (b < 0) throw new IOException("Ende");
                val += (b & 127) * mult;
                if ((b & 128) == 0) break;
                mult *= 128;
            }
            return val;
        }

        byte[] ReadN(int n)
        {
            var d = new byte[n];
            int o = 0;
            while (o < n)
            {
                int r = ns.Read(d, o, n - o);
                if (r <= 0) throw new IOException("Ende");
                o += r;
            }
            return d;
        }

        public bool Connect(out string err)
        {
            foreach (string b in Brokers)
            {
                try
                {
                    tcp = new TcpClient();
                    IAsyncResult ar = tcp.BeginConnect(b, 1883, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(4000)) { tcp.Close(); continue; }
                    tcp.EndConnect(ar);
                    tcp.NoDelay = true;
                    ns = tcp.GetStream();
                    ns.WriteTimeout = 3000;
                    var vh = new List<byte> { 0, 4, (byte)'M', (byte)'Q', (byte)'T', (byte)'T', 4, 2, 0, 30 };
                    AddStr(vh, "sz" + Guid.NewGuid().ToString("N").Substring(0, 18));
                    WritePacket(0x10, vh.ToArray());
                    ns.ReadTimeout = 4000;
                    int t = ns.ReadByte();
                    byte[] body = ReadN(ReadLen());
                    ns.ReadTimeout = Timeout.Infinite;
                    if (t != 0x20 || body.Length < 2 || body[1] != 0) { tcp.Close(); continue; }
                    alive = true;
                    BrokerName = b;
                    new Thread(ReadLoop) { IsBackground = true }.Start();
                    err = null;
                    return true;
                }
                catch { try { tcp.Close(); } catch { } }
            }
            err = "Vermittlungsserver nicht erreichbar. Prüfe die Internetverbindung (ausgehender TCP-Port 1883 muss erlaubt sein).";
            return false;
        }

        void ReadLoop()
        {
            try
            {
                while (alive)
                {
                    int t = ns.ReadByte();
                    if (t < 0) break;
                    byte[] body = ReadN(ReadLen());
                    if ((t & 0xF0) == 0x30)
                    {
                        int tl = body[0] * 256 + body[1];
                        int off = 2 + tl + (((t >> 1) & 3) > 0 ? 2 : 0);
                        var pl = new byte[body.Length - off];
                        Array.Copy(body, off, pl, 0, pl.Length);
                        lock (rlk) { if (inbox.Count < 2000) inbox.Enqueue(pl); }
                    }
                }
            }
            catch { }
            if (alive && Error == null) Error = "Verbindung zum Vermittlungsserver verloren.";
            alive = false;
        }

        public void Subscribe(string topic)
        {
            var b = new List<byte> { 0, 1 };
            AddStr(b, topic);
            b.Add(0);
            try { WritePacket(0x82, b.ToArray()); } catch { Fail(); }
        }

        public void Publish(string topic, byte[] payload)
        {
            if (!alive) return;
            var b = new List<byte>(payload.Length + topic.Length + 2);
            AddStr(b, topic);
            b.AddRange(payload);
            try { WritePacket(0x30, b.ToArray()); } catch { Fail(); }
        }

        public void Ping()
        {
            if (!alive) return;
            try { WritePacket(0xC0, new byte[0]); } catch { Fail(); }
        }

        void Fail()
        {
            if (alive && Error == null) Error = "Verbindung zum Vermittlungsserver verloren.";
            alive = false;
        }

        public bool TryReceive(out byte[] data)
        {
            lock (rlk)
            {
                if (inbox.Count > 0) { data = inbox.Dequeue(); return true; }
            }
            data = null;
            return false;
        }

        public void Close()
        {
            try { if (alive) WritePacket(0xE0, new byte[0]); } catch { }
            alive = false;
            try { if (tcp != null) tcp.Close(); } catch { }
        }
    }

    // Verbindungsaufbau über das Internet:
    // 1. Beide verbinden sich mit dem Vermittlungsserver und treffen sich über den Raum-Code.
    // 2. Sie tauschen ihre öffentlichen (per STUN ermittelten) und lokalen Adressen aus.
    // 3. Beide senden sich gleichzeitig UDP-Pakete (Hole Punching) -> direkte Verbindung.
    // 4. Klappt das nicht innerhalb von 3 Sekunden, läuft das Spiel über das Relay.
    public class OnlineLink
    {
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        static readonly Random rng = new Random();

        public static string NewCode()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
            return sb.ToString();
        }

        public static bool ForceRelay; // nur für Tests
        public string Code;
        public bool IsHost;
        public volatile int RelayState; // 0 verbindet, 1 bereit, 2 Fehler
        public volatile string Error;
        public int Phase;                // Client: 0 warten, 1 Host suchen, 2 Hole Punching, 3 entschieden
        public IPEndPoint Decided;
        public string Mode = "";

        NetSocket sock;
        volatile IPEndPoint[] stunServers;
        readonly List<IPEndPoint> cands = new List<IPEndPoint>();
        double started = -1, lastStun, lastHello, lastPunch, punchUntil, lastPing;

        public void Start(NetSocket s, bool host, string code)
        {
            sock = s;
            IsHost = host;
            Code = code;
            string b = "sensensturm/eh05/v" + Pk.Version + "/" + code;
            sock.RelayIn = b + (host ? "/h" : "/c");
            sock.RelayOut = b + (host ? "/c" : "/h");
            sock.AutoPunchReply = host;
            new Thread(() =>
            {
                var list = new List<IPEndPoint>();
                foreach (var hp in new[] { new KeyValuePair<string, int>("stun.l.google.com", 19302), new KeyValuePair<string, int>("stun.cloudflare.com", 3478) })
                {
                    try
                    {
                        foreach (IPAddress a in Dns.GetHostAddresses(hp.Key))
                            if (a.AddressFamily == AddressFamily.InterNetwork) { list.Add(new IPEndPoint(a, hp.Value)); break; }
                    }
                    catch { }
                }
                stunServers = list.ToArray();
                var r = new MqttRelay();
                string err;
                if (r.Connect(out err))
                {
                    r.Subscribe(sock.RelayIn);
                    sock.Relay = r;
                    RelayState = 1;
                }
                else { Error = err; RelayState = 2; }
            }) { IsBackground = true }.Start();
        }

        public string RelayName { get { return sock != null && sock.Relay != null ? sock.Relay.BrokerName : ""; } }

        string MyCandidates()
        {
            string pub = sock.StunResult != null ? sock.StunResult.ToString() : "-";
            string local = NetUtil.LocalIPs()[0] + ":" + sock.LocalPort;
            return pub + "|" + local;
        }

        void AddCands(string[] p)
        {
            for (int i = 1; i < p.Length; i++)
            {
                string[] hp = p[i].Split(':');
                IPAddress a; int port;
                if (hp.Length == 2 && IPAddress.TryParse(hp[0], out a) && int.TryParse(hp[1], out port))
                {
                    var ep = new IPEndPoint(a, port);
                    if (!cands.Contains(ep)) cands.Add(ep);
                }
            }
        }

        public void Tick(double now)
        {
            if (started < 0) started = now;
            if (RelayState == 2) return;
            // öffentliche Adresse per STUN ermitteln (parallel zum Relay-Aufbau)
            IPEndPoint[] st = stunServers;
            if (st != null && sock.StunResult == null && now - lastStun > 0.4 && now - started < 6)
            {
                lastStun = now;
                foreach (var ep in st) sock.SendStunRequest(ep);
            }
            if (RelayState != 1) return;
            if (!sock.Relay.Connected) { Error = sock.Relay.Error ?? "Verbindung zum Vermittlungsserver verloren."; return; }
            if (now - lastPing > 15) { lastPing = now; sock.Relay.Ping(); }

            string m;
            while (sock.TryGetSignal(out m))
            {
                string[] p = m.Split('|');
                if (IsHost && p[0] == "H")
                {
                    sock.SendSignal("A|" + MyCandidates());
                    AddCands(p);
                    punchUntil = now + 4;
                }
                else if (!IsHost && p[0] == "A" && Phase == 1)
                {
                    AddCands(p);
                    punchUntil = now + 3;
                    Phase = 2;
                }
            }

            if (!IsHost)
            {
                if (Phase == 0 && (sock.StunResult != null || now - started > 2.0)) Phase = 1;
                if (Phase == 1 && now - lastHello > 1.0)
                {
                    lastHello = now;
                    sock.SendSignal("H|" + MyCandidates());
                    if (now - started > 15) Error = "Kein offenes Spiel mit dem Code " + Code + " gefunden. Ist der Code richtig und hat der Host das Online-Spiel geöffnet?";
                }
                if (Phase == 2)
                {
                    IPEndPoint hit = ForceRelay ? null : sock.FirstPunchFrom(cands);
                    if (hit != null) { Decided = hit; Phase = 3; Mode = "direkt"; }
                    else if (now >= punchUntil) { Decided = NetSocket.RelayPeer; Phase = 3; Mode = "Relay"; }
                }
            }

            if (now < punchUntil && now - lastPunch > 0.08)
            {
                lastPunch = now;
                foreach (var ep in cands) sock.SendPunch(ep);
            }
        }

        public string StatusText
        {
            get
            {
                if (Error != null) return Error;
                if (RelayState == 0) return "Verbinde mit Vermittlungsserver ...";
                if (IsHost) return "Bereit (über " + RelayName + ")";
                switch (Phase)
                {
                    case 0: case 1: return "Suche Spiel " + Code + " ...";
                    case 2: return "Baue Direktverbindung auf ...";
                    default: return "Verbinde (" + Mode + ") ...";
                }
            }
        }
    }
}
