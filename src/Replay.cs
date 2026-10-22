using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Sensensturm
{
    // Wiederholung eines Matches. Die Simulation hängt nur von den Eingaben ab, deshalb reichen Anfangszustand,
    // Spielwerte und pro Tick die Tasten beider Spieler. Abspielen = dieselben Eingaben noch einmal simulieren.
    public class Replay
    {
        const int Magic = 0x31525353; // "SSR1"
        public string Name0 = "", Name1 = "";
        public DateTime Date = DateTime.Now;
        public byte[] Start;      // GameState beim Matchbeginn
        public byte[] TuningData; // Spielwerte während des Matches
        readonly List<ushort> input = new List<ushort>(); // pro Tick: gehalten0, gedrückt0, gehalten1, gedrückt1

        public int Ticks { get { return input.Count / 4; } }

        public static string Folder { get { return Path.Combine(Application.StartupPath, "replays"); } }

        public static Replay Begin(GameState s, string name0, string name1)
        {
            var r = new Replay { Name0 = name0 ?? "", Name1 = name1 ?? "", TuningData = Tuning.ToBytes() };
            var ms = new MemoryStream();
            s.Write(new BinaryWriter(ms));
            r.Start = ms.ToArray();
            return r;
        }

        public void Add(int held0, int pressed0, int held1, int pressed1)
        {
            input.Add((ushort)held0); input.Add((ushort)pressed0); input.Add((ushort)held1); input.Add((ushort)pressed1);
        }

        public GameState StartState() { return GameState.Read(new BinaryReader(new MemoryStream(Start))); }

        public void Get(int tick, out int held0, out int pressed0, out int held1, out int pressed1)
        {
            int i = tick * 4;
            held0 = input[i]; pressed0 = input[i + 1]; held1 = input[i + 2]; pressed1 = input[i + 3];
        }

        // Fingerabdruck aller Daten, die das Ergebnis der Simulation beeinflussen (Zustandsfelder, Angriffswerte,
        // Kämpferwerte). Weicht er ab, stammt die Datei aus einer anderen Spielversion und würde anders ablaufen.
        static uint signature;
        public static uint Signature
        {
            get
            {
                if (signature != 0) return signature;
                uint h = 2166136261;
                Action<string> mix = t => { foreach (char ch in t) { h ^= ch; h *= 16777619; } };
                foreach (Type t in new[] { typeof(GameState), typeof(PlayerState), typeof(Tuning) })
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) mix(t.Name + "." + f.Name + ":" + f.FieldType.Name);
                foreach (AttackDef[] set in Attacks.Sets)
                    foreach (AttackDef d in set)
                        foreach (FieldInfo f in typeof(AttackDef).GetFields())
                            if (f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(bool)) mix(Convert.ToString(f.GetValue(d), System.Globalization.CultureInfo.InvariantCulture));
                foreach (CharDef c in CharDef.All) mix(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", c.Weapon, c.Speed, c.Jump, c.Grav, c.Weight));
                signature = h == 0 ? 1 : h;
                return signature;
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(Signature);
                w.Write(Name0); w.Write(Name1);
                w.Write(Date.ToBinary());
                w.Write(Start.Length); w.Write(Start);
                w.Write(TuningData.Length); w.Write(TuningData);
                w.Write(input.Count);
                foreach (ushort v in input) w.Write(v);
            }
        }

        // headerOnly: nur Namen und Datum (für die Liste)
        public static Replay Load(string path, bool headerOnly = false)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new BinaryReader(gz, Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic) throw new InvalidDataException("Keine Sensensturm-Wiederholung.");
                if (r.ReadUInt32() != Signature) throw new InvalidDataException("Die Wiederholung stammt aus einer anderen Spielversion.");
                var rp = new Replay { Name0 = r.ReadString(), Name1 = r.ReadString(), Date = DateTime.FromBinary(r.ReadInt64()) };
                if (headerOnly) return rp;
                rp.Start = r.ReadBytes(r.ReadInt32());
                rp.TuningData = r.ReadBytes(r.ReadInt32());
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) rp.input.Add(r.ReadUInt16());
                return rp;
            }
        }

        public string FileName()
        {
            Func<string, string> clean = s => { var sb = new StringBuilder(); foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c); return sb.Length > 0 ? sb.ToString() : "X"; };
            return Date.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + clean(Name0) + "_vs_" + clean(Name1) + ".ssr";
        }
    }
}
