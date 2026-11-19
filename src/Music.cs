using System;
using System.Collections.Generic;
using System.Text;

namespace Sensensturm
{
    public enum Song { None = -1, Menu, Graveyard, Blossom, Autumn, Grove, Victory, Count }

    // Prozedurale Hintergrundmusik: Step-Sequencer mit kleinen Synthesizern und Hall.
    // Läuft im Mixer-Thread von Audio, das Spiel wählt nur Stück, Finale-Modus und Abdämpfen (Pause).
    // Keine Dateien: alle Stücke stehen unten als Notenzeilen im Code.
    public static class Music
    {
        public const int Rate = 22050;
        public static float Volume = 0.6f;
        static volatile int reqSong = (int)Song.None;
        static volatile bool reqIntense, reqDuck;
        static readonly MusicPlayer live = new MusicPlayer();
        static bool ready;

        // Beim Start (Audio.Init): Notenzeilen einlesen. Schlägt das fehl, bleibt die Musik stumm.
        public static void Init()
        {
            try { ready = SongDef.All.Length == (int)Song.Count; } catch { ready = false; }
        }

        public static string[] Names = { "Hauptthema", "Mondfriedhof", "Kirschblütental", "Herbsttempel", "Sternenhain", "Siegesfanfare" };

        public static Song ForStage(int stage)
        {
            switch (StageDef.Valid(stage))
            {
                case StageDef.Blossom: return Song.Blossom;
                case StageDef.Autumn: return Song.Autumn;
                case StageDef.Grove: return Song.Grove;
                default: return Song.Graveyard;
            }
        }

        // Vom Spiel jeden Frame aufgerufen. Gleiches Stück erneut anfordern startet es nicht neu.
        public static void Set(Song s, bool intense, bool duck)
        {
            reqSong = (int)s;
            reqIntense = intense;
            reqDuck = duck;
        }

        // Vom Mixer-Thread: Musik auf den Puffer addieren.
        internal static void Mix(float[] mix)
        {
            float vol = Volume;
            if (!ready || vol <= 0) return;
            live.Request((Song)reqSong, reqIntense, reqDuck);
            live.Render(mix, 0, mix.Length, vol);
        }

        // Für Tests: ein Stück offline rendern (Werte etwa -1..1, Lautstärke 1).
        public static float[] RenderOffline(Song s, float seconds, bool intense)
        {
            var p = new MusicPlayer();
            p.Request(s, intense, false);
            var b = new float[(int)(seconds * Rate)];
            for (int i = 0; i < b.Length; i += 512) p.Render(b, i, Math.Min(512, b.Length - i), 1f);
            return b;
        }

        public static SongDef Def(Song s) { return SongDef.All[(int)s]; }
    }

    // ------------------------------------------------------------------ Instrumente
    public enum Inst { Pluck, Pad, Bass, Organ, Bell, Flute, Lead, Kick, Snare, Hat, Taiko, Gong, Wood }

    public class NoteEv { public int Midi; public int Len; public float Vel; }

    public class Chan
    {
        public Inst I;
        public float Vol;
        public int Div;              // Sechzehntel pro Zeichen (1 = 16tel, 2 = Achtel, 4 = Viertel, 16 = Takt)
        public bool IntenseOnly, CalmOnly;
        public List<NoteEv>[] Steps; // je Zeichen: Noten, die dort beginnen (oder null)
        public int Ticks { get { return Steps.Length * Div; } }
    }

    public class SongDef
    {
        public string Name;
        public float Bpm, IntenseTempo = 1.08f, Gain = 1f, Reverb = 0.25f;
        public bool Loop = true;
        public Song Next = Song.None;
        public List<Chan> Chans = new List<Chan>();
        public int LoopTicks;

        public static readonly SongDef[] All = Songs.Build();

        public SongDef Ch(Inst i, float vol, int div, string pattern, int mode = 0)
        {
            var c = new Chan { I = i, Vol = vol, Div = div, IntenseOnly = mode > 0, CalmOnly = mode < 0, Steps = Notation.Parse(pattern, i >= Inst.Kick) };
            Chans.Add(c);
            LoopTicks = Math.Max(LoopTicks, c.Ticks);
            return this;
        }
    }

    // ------------------------------------------------------------------ Notenschrift
    // Zeichen durch Leerzeichen getrennt, "|" ist nur ein Taktstrich zum Lesen.
    //   "."  Pause      "-"  vorige Note halten      "a4", "c#5", "bb3"  Note      "d3+f3+a3"  Akkord
    //   Schlagzeug: "x" normal, "X" betont, "o" leise
    public static class Notation
    {
        public static List<NoteEv>[] Parse(string s, bool drum)
        {
            var toks = new List<string>();
            foreach (string t in s.Split(new[] { ' ', '\t', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries)) toks.Add(t);
            var steps = new List<NoteEv>[toks.Count];
            List<NoteEv> last = null;
            for (int i = 0; i < toks.Count; i++)
            {
                string t = toks[i];
                if (t == ".") { last = null; continue; }
                if (t == "-") { if (last != null) foreach (NoteEv e in last) e.Len++; continue; }
                var list = new List<NoteEv>();
                if (drum)
                {
                    float v = t == "X" ? 1f : t == "o" ? 0.35f : 0.65f;
                    list.Add(new NoteEv { Midi = 0, Len = 1, Vel = v });
                }
                else foreach (string n in t.Split('+')) list.Add(new NoteEv { Midi = Midi(n), Len = 1, Vel = 1f });
                steps[i] = list;
                last = list;
            }
            return steps;
        }

        public static int Midi(string n)
        {
            int semi;
            switch (char.ToLowerInvariant(n[0]))
            {
                case 'c': semi = 0; break;
                case 'd': semi = 2; break;
                case 'e': semi = 4; break;
                case 'f': semi = 5; break;
                case 'g': semi = 7; break;
                case 'a': semi = 9; break;
                case 'b': semi = 11; break;
                default: throw new FormatException("Note: " + n);
            }
            int i = 1;
            if (i < n.Length && n[i] == '#') { semi++; i++; }
            else if (i < n.Length && n[i] == 'b' && i + 1 < n.Length) { semi--; i++; }
            int oct = int.Parse(n.Substring(i));
            return 12 * (oct + 1) + semi;
        }

        static readonly string[] Nm = { "c", "c#", "d", "d#", "e", "f", "f#", "g", "g#", "a", "a#", "b" };
        public static string Name(int midi) { return Nm[midi % 12] + (midi / 12 - 1); }

        public static string Rep(string s, int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s).Append(' ');
            return sb.ToString();
        }

        // Arpeggio: je Akkord ein Takt, Muster mit Indizes in die Akkordtöne (Index >= Anzahl = eine Oktave höher).
        public static string Arp(string chords, string pat)
        {
            var sb = new StringBuilder();
            foreach (string c in chords.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ns = c.Split('+');
                foreach (string p in pat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (p == "." || p == "-") { sb.Append(p).Append(' '); continue; }
                    int k = int.Parse(p);
                    int m = Midi(ns[k % ns.Length]) + 12 * (k / ns.Length);
                    sb.Append(Name(m)).Append(' ');
                }
            }
            return sb.ToString();
        }

        // Basslinie: je Grundton ein Takt. R = Grundton, O = Oktave höher, F = Quinte, "." / "-" wie oben.
        public static string Line(string roots, string pat)
        {
            var sb = new StringBuilder();
            foreach (string r in roots.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int m = Midi(r);
                foreach (string p in pat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (p == "R") sb.Append(Name(m));
                    else if (p == "O") sb.Append(Name(m + 12));
                    else if (p == "F") sb.Append(Name(m + 7));
                    else sb.Append(p);
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ Die Stücke
    static class Songs
    {
        static string Rep(string s, int n) { return Notation.Rep(s, n); }
        static string Arp(string c, string p) { return Notation.Arp(c, p); }
        static string Line(string r, string p) { return Notation.Line(r, p); }

        public static SongDef[] Build()
        {
            var all = new SongDef[(int)Song.Count];
            all[(int)Song.Menu] = Menu();
            all[(int)Song.Graveyard] = Graveyard();
            all[(int)Song.Blossom] = Blossom();
            all[(int)Song.Autumn] = Autumn();
            all[(int)Song.Grove] = Grove();
            all[(int)Song.Victory] = Victory();
            for (int i = 0; i < all.Length; i++) all[i].Name = Music.Names[i];
            return all;
        }

        // Hauptthema: d-Moll, heroisch, Marsch. 16 Takte.
        static SongDef Menu()
        {
            const string chords = "d3+f3+a3 bb2+d3+f3 c3+e3+g3 a2+c#3+e3 d3+f3+a3 bb2+d3+f3 g2+bb2+d3 a2+c#3+e3 " +
                                  "bb2+d3+f3 c3+e3+g3 d3+f3+a3 d3+f3+a3 g2+bb2+d3 c3+e3+g3 a2+c#3+e3 a2+c#3+e3";
            const string roots = "d2 bb1 c2 a1 d2 bb1 g1 a1 bb1 c2 d2 d2 g1 c2 a1 a1";
            var s = new SongDef { Bpm = 116, Reverb = 0.28f, Gain = 0.9f };
            s.Ch(Inst.Pad, 0.55f, 16, chords);
            s.Ch(Inst.Bass, 0.7f, 2, Line(roots, "R . R R O . R F"));
            s.Ch(Inst.Pluck, 0.3f, 2, Arp("d4+f4+a4 d4+f4+bb4 e4+g4+c5 e4+a4+c#5 d4+f4+a4 d4+f4+bb4 d4+g4+bb4 e4+a4+c#5 " +
                                          "d4+f4+bb4 e4+g4+c5 d4+f4+a4 d4+f4+a4 d4+g4+bb4 e4+g4+c5 e4+a4+c#5 e4+a4+c#5", "0 1 2 3 2 1 2 1"));
            s.Ch(Inst.Lead, 0.5f,
                2, "d5 - - a4 d5 - e5 - | f5 - - - e5 - d5 - | e5 - - c5 g4 - c5 - | e5 - - - c#5 - . . | " +
                   "d5 - - a4 d5 - f5 - | g5 - - f5 d5 - bb4 - | d5 - - - g5 - f5 - | e5 - - - - - - . | " +
                   "f5 - - - d5 - f5 - | g5 - - - e5 - c5 - | a5 - - - - - g5 f5 | f5 - e5 - d5 - - - | " +
                   "bb4 - d5 - g5 - - - | g5 - f5 - e5 - c5 - | e5 - - - c#5 - e5 - | a5 - - - - - . .");
            s.Ch(Inst.Kick, 0.8f, 1, "X . . . . . . x x . . . . . . .");
            s.Ch(Inst.Snare, 0.45f, 1, ". . . . x . . . . . . . x . . o");
            s.Ch(Inst.Hat, 0.3f, 2, "o x o x o x o x", -1);
            s.Ch(Inst.Hat, 0.35f, 1, "x o x o x o x o x o x o x o x x", 1);
            s.Ch(Inst.Taiko, 0.5f, 1, Rep(". . . . . . . . . . . . . . . .", 7) + "X . . X . . X . X . X . X X X X", 1);
            return s;
        }

        // Mondfriedhof: a-Moll (harmonisch), schaurige Spieluhr im Dreivierteltakt über Orgel. 16 Takte à 12 Sechzehntel.
        static SongDef Graveyard()
        {
            const string bars = "a2 a2 f2 e2 a2 d2 e2 e2";
            const string ch = "c4+e4 c4+e4 a3+c4 g#3+b3 c4+e4 a3+f4 g#3+d4 g#3+b3";
            var s = new SongDef { Bpm = 138, Reverb = 0.42f, Gain = 1f };
            // Walzer: Grundton auf 1, Akkord auf 2 und 3 (Orgel)
            string[] c = ch.Split(' ');
            s.Ch(Inst.Organ, 0.42f, 4, Line(bars + " " + bars, "R - -"));
            var chords = new StringBuilder();
            for (int r = 0; r < 2; r++) for (int i = 0; i < 8; i++) chords.Append(". ").Append(c[i]).Append(' ').Append(c[i]).Append(' ');
            s.Ch(Inst.Organ, 0.26f, 4, chords.ToString());
            s.Ch(Inst.Pad, 0.35f, 12, "a3+c4+e4 a3+c4+e4 f3+a3+c4 e3+g#3+b3 a3+c4+e4 d3+f3+a3 e3+g#3+b3 e3+g#3+b3");
            s.Ch(Inst.Bell, 0.42f, 2,
                "a4 - c5 - e5 - | e5 - - - d5 c5 | d5 - c5 - a4 - | b4 - - - g#4 - | " +
                "a4 - c5 - e5 - | f5 - - - e5 d5 | e5 - g#4 - b4 - | b4 - - - . . | " +
                "e5 - a5 - c6 - | b5 - - - a5 - | a5 - f5 - c5 - | g#5 - - - e5 - | " +
                "c6 - b5 - a5 - | d6 - - - a5 f5 | e5 - g#5 - b5 - | e6 - - - . .");
            s.Ch(Inst.Wood, 0.22f, 4, "x o o", -1);
            s.Ch(Inst.Taiko, 0.55f, 12, "x . . . x . . . x . . . x . x x", -1);
            s.Ch(Inst.Kick, 0.75f, 4, "X . x", 1);
            s.Ch(Inst.Snare, 0.35f, 2, ". . x . x o", 1);
            s.Ch(Inst.Hat, 0.28f, 1, "x o x o x o x o x o x x", 1);
            s.Ch(Inst.Gong, 0.5f, 12, "x " + Rep(".", 15));
            return s;
        }

        // Kirschblütental: Miyako-bushi-Tonleiter auf E (E F A B C), Koto, Shakuhachi-Flöte, Taiko.
        static SongDef Blossom()
        {
            var s = new SongDef { Bpm = 100, Reverb = 0.36f, Gain = 1f };
            string kE = "e4 b4 e5 b4 c5 b4 a4 b4 ", kA = "a3 e4 a4 e4 b4 a4 f4 e4 ", kF = "f3 c4 f4 c4 a4 f4 e4 c4 ";
            string koto = kE + kE + kA + kA + kF + kF + "e4 b4 e5 f5 e5 c5 b4 a4 " + "b4 - e4 - - - . . ";
            s.Ch(Inst.Pluck, 0.5f, 2, koto);
            s.Ch(Inst.Bass, 0.5f, 16, "e2 e2 a1 a1 f1 f1 e2 e2");
            s.Ch(Inst.Pad, 0.32f, 16, "e3+b3 e3+b3 a2+e3 a2+e3 f2+c3 f2+c3 e3+b3 e3+b3");
            s.Ch(Inst.Flute, 0.5f, 2,
                "b4 - - - - - c5 - | b4 - a4 - b4 - - - | e5 - - - - - f5 e5 | c5 - - - b4 - - - | " +
                "a4 - - - c5 - - - | b4 - - - a4 - f4 - | e4 - - - - - - - | . . . . . . . . | " +
                "e5 - - - f5 - e5 - | c5 - b4 - c5 - - - | a5 - - - f5 - e5 - | e5 - - - - - . . | " +
                "c5 - - - e5 - f5 - | e5 - c5 - b4 - a4 - | b4 - - - - - - - | e4 - - - - - . .");
            s.Ch(Inst.Taiko, 0.75f, 1, "X . . . . . . . x . . x . . . .", -1);
            s.Ch(Inst.Taiko, 0.8f, 1, "X . x . X . . . x . x x X . . . X . x . X . . . x . x x X . X X", 1);
            s.Ch(Inst.Wood, 0.3f, 1, ". . . . x . . . . . . . x . . o");
            s.Ch(Inst.Hat, 0.22f, 1, "x o o x o o x o x o o x o o x o", 1);
            s.Ch(Inst.Gong, 0.45f, 16, "x " + Rep(".", 15));
            return s;
        }

        // Herbsttempel: D-dorisch, warm und ruhig. Tempelglocken, Zupfarpeggio, weiche Trommeln.
        static SongDef Autumn()
        {
            const string chords = "d3+f3+a3+c4 g3+b3+d4 a3+c4+e4 g3+b3+d4 f3+a3+c4 c3+e3+g3 e3+g3+b3 g3+b3+d4";
            var s = new SongDef { Bpm = 88, Reverb = 0.38f, Gain = 1f };
            s.Ch(Inst.Pad, 0.42f, 16, chords);
            s.Ch(Inst.Bass, 0.6f, 2, Line("d2 g1 a1 g1 f1 c2 e2 g1", "R - - R . . F -"));
            s.Ch(Inst.Pluck, 0.4f, 2, Arp(chords, "0 1 2 3 4 3 2 1"));
            s.Ch(Inst.Bell, 0.4f, 2,
                "a5 - - - f5 - - - | g5 - - - d5 - - - | e5 - - - a5 - - - | g5 - - - - - . . | " +
                "f5 - - - a5 - c6 - | g5 - - - e5 - - - | b5 - - - g5 - e5 - | d5 - - - - - . . | " +
                "d6 - - - c6 - a5 - | b5 - - - g5 - d5 - | c6 - - - b5 - a5 - | b5 - - - - - . . | " +
                "a5 - c6 - f5 - - - | e5 - g5 - c6 - - - | b5 - - - g5 - b5 - | a5 - - - - - . .");
            s.Ch(Inst.Kick, 0.65f, 1, "X . . . . . . . . . x . . . . .");
            s.Ch(Inst.Wood, 0.22f, 1, ". . . . x . . . . . . . x . . .", -1);
            s.Ch(Inst.Snare, 0.38f, 1, ". . . . x . . . . . . . x . . o", 1);
            s.Ch(Inst.Hat, 0.18f, 2, "o x o x o x o x", -1);
            s.Ch(Inst.Hat, 0.3f, 1, "x o x o x o x x x o x o x o x o", 1);
            s.Ch(Inst.Gong, 0.32f, 16, "x " + Rep(".", 15));
            return s;
        }

        // Sternenhain: E-lydisch, schimmernde Glocken-Arpeggios, Harfenmelodie, sanfter Puls.
        static SongDef Grove()
        {
            const string chords = "e3+g#3+b3 f#3+a#3+c#4 g#3+b3+d#4 f#3+a#3+c#4 c#3+e3+g#3 b2+d#3+f#3 e3+g#3+b3 f#3+a#3+c#4";
            var s = new SongDef { Bpm = 108, Reverb = 0.45f, Gain = 1f };
            s.Ch(Inst.Pad, 0.45f, 16, chords);
            s.Ch(Inst.Bass, 0.55f, 4, Line("e2 e2 g#1 f#1 c#2 b1 e2 f#1", "R R O R"));
            const string up = "e4+g#4+b4 f#4+a#4+c#5 g#4+b4+d#5 f#4+a#4+c#5 c#4+e4+g#4 b3+d#4+f#4 e4+g#4+b4 f#4+a#4+c#5";
            s.Ch(Inst.Bell, 0.2f, 2, Arp(up, "0 1 2 1 0 1 2 1"), -1);
            s.Ch(Inst.Bell, 0.22f, 1, Arp(up, "0 1 2 3 2 1 0 1 0 1 2 3 2 1 0 1"), 1);
            s.Ch(Inst.Pluck, 0.5f, 2,
                "b5 - - - g#5 - e5 - | a#5 - - - c#6 - - - | b5 - - - d#6 - - - | c#6 - a#5 - f#5 - - - | " +
                "e5 - g#5 - c#6 - - - | d#6 - - - b5 - f#5 - | g#5 - - - b5 - - - | a#5 - - - - - . .");
            s.Ch(Inst.Kick, 0.6f, 1, "X . . . . . . . x . . . . . . .");
            s.Ch(Inst.Hat, 0.16f, 2, "o x o x o x o x", -1);
            s.Ch(Inst.Snare, 0.32f, 1, ". . . . x . . . . . . . x . . .", 1);
            s.Ch(Inst.Hat, 0.26f, 1, "x o x o x o x o x o x o x o x x", 1);
            return s;
        }

        // Siegesfanfare: D-Dur, einmal gespielt, danach das Hauptthema.
        static SongDef Victory()
        {
            var s = new SongDef { Bpm = 132, Loop = false, Next = Song.Menu, Reverb = 0.32f, Gain = 1f, IntenseTempo = 1f };
            s.Ch(Inst.Lead, 0.55f, 2, "a4 a4 a4 d5 - - a4 d5 | f#5 - e5 - f#5 g5 a5 - | d6 - - - - - - - | - - - - . . . .");
            s.Ch(Inst.Pluck, 0.4f, 1, Rep(".", 16) + Rep(".", 16) + "d5 f#5 a5 d6 f#6 a6 d7 . " + Rep(".", 8) + Rep(".", 16));
            s.Ch(Inst.Pad, 0.5f, 8, "d3+f#3+a3 - g3+b3+d4 a3+c#4+e4 d3+f#3+a3+d4 - - -");
            s.Ch(Inst.Bass, 0.7f, 8, "d2 - g1 a1 d2 - - -");
            s.Ch(Inst.Taiko, 0.8f, 1, "X . . . . . . . X . . . . . . . X . . . X . . . X . X X X X X X X . . . . . . . . . . . . . . . " + Rep(".", 16));
            s.Ch(Inst.Gong, 0.5f, 16, ". . x .");
            return s;
        }
    }

    // ------------------------------------------------------------------ Abspielen
    sealed class MVoice
    {
        public Inst I;
        public float F, Vel;
        public int Age, Len;
        public double Ph;
        public float A, B; // Filterzustände
    }

    sealed class MusicPlayer
    {
        const int Rate = Music.Rate, MaxVoices = 40;
        const int TabBits = 12, TabSize = 1 << TabBits;
        static readonly float[] Sin = MakeSin();

        static float[] MakeSin()
        {
            var t = new float[TabSize + 1];
            for (int i = 0; i <= TabSize; i++) t[i] = (float)Math.Sin(2 * Math.PI * i / TabSize);
            return t;
        }

        static float S(double ph)
        {
            double f = ph - Math.Floor(ph);
            return Sin[(int)(f * TabSize)];
        }

        readonly List<MVoice> voices = new List<MVoice>();
        uint rng = 0x9E3779B9u;
        float Noise() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng / 2147483648f) - 1f; }

        Song lastReq = Song.None - 1;
        Song cur = Song.None, pending = Song.None;
        bool switching;
        float fade = 1f, fadeStep;
        long tick;
        double tickPos;
        int endWait;
        float tempo = 1f, duckGain = 1f, lp;
        bool intense, duck;

        // Hall (Freeverb-artig, 4 Kammfilter + 2 Allpässe)
        readonly float[][] comb = { new float[557], new float[593], new float[641], new float[677] };
        readonly int[] combPos = new int[4];
        readonly float[] combLp = new float[4];
        readonly float[][] ap = { new float[277], new float[211] };
        readonly int[] apPos = new int[2];

        public void Request(Song s, bool intense, bool duck)
        {
            this.intense = intense;
            this.duck = duck;
            if (s == lastReq) return;
            lastReq = s;
            if (s == cur && !switching) return;
            pending = s;
            switching = true;
            fadeStep = 1f / ((s == Song.Victory || cur == Song.None ? 0.12f : 0.7f) * Rate);
        }

        void Start(Song s)
        {
            cur = s;
            tick = 0;
            tickPos = 0;
            endWait = 0;
            fade = 1f;
            switching = false;
            voices.Clear();
        }

        public void Render(float[] mix, int off, int n, float volume)
        {
            for (int k = off; k < off + n; k++)
            {
                if (switching)
                {
                    fade -= fadeStep;
                    if (fade <= 0 || cur == Song.None) Start(pending);
                }
                SongDef d = cur == Song.None ? null : Music.Def(cur);
                if (d != null) Sequence(d);

                float x = 0;
                for (int v = voices.Count - 1; v >= 0; v--)
                {
                    MVoice vc = voices[v];
                    float o;
                    if (!Voice(vc, out o)) { voices.RemoveAt(v); continue; }
                    x += o;
                }

                float wet = d != null ? d.Reverb : 0.3f;
                x = x * (1 - wet * 0.4f) + Reverb(x) * wet;

                float g = d != null ? d.Gain : 1f;
                float dt = duck ? 0.3f : 1f;
                duckGain += (dt - duckGain) * 0.0004f;
                // gedämpft (Pause): Tiefpass öffnet und schließt mit der Lautstärke
                float cut = 0.08f + 0.92f * (duckGain - 0.3f) / 0.7f;
                lp += (x - lp) * Math.Max(0.08f, Math.Min(1f, cut));
                mix[k] += lp * g * fade * duckGain * volume * 0.32f;
            }
        }

        void Sequence(SongDef d)
        {
            float tt = intense ? d.IntenseTempo : 1f;
            tempo += (tt - tempo) * 0.00005f;
            double spt = Rate * 60.0 / (d.Bpm * tempo * 4);
            if (tickPos <= 0)
            {
                if (!d.Loop && tick >= d.LoopTicks)
                {
                    // einmaliges Stück zu Ende: Ausklingen lassen, dann weiter
                    if (++endWait > Rate * 1.4 && d.Next != Song.None) { Start(d.Next); }
                    return;
                }
                foreach (Chan c in d.Chans)
                {
                    if (c.IntenseOnly && !intense) continue;
                    if (c.CalmOnly && intense) continue;
                    if (tick % c.Div != 0) continue;
                    int idx = (int)((tick / c.Div) % c.Steps.Length);
                    List<NoteEv> st = c.Steps[idx];
                    if (st == null) continue;
                    foreach (NoteEv e in st) Trigger(c.I, e, c.Vol, (int)(e.Len * c.Div * spt));
                }
                tickPos += spt;
                tick++;
                if (d.Loop && tick >= d.LoopTicks) tick = 0;
            }
            tickPos -= 1;
        }

        void Trigger(Inst i, NoteEv e, float vol, int len)
        {
            if (voices.Count >= MaxVoices) voices.RemoveAt(0);
            float f = (float)(440.0 * Math.Pow(2, (e.Midi - 69) / 12.0));
            if (i == Inst.Gong) f = 98;
            voices.Add(new MVoice { I = i, F = f, Vel = e.Vel * vol, Len = Math.Max(1, len) });
        }

        // Ein Sample eines Instruments; false = Stimme ist verklungen.
        bool Voice(MVoice v, out float o)
        {
            float t = v.Age / (float)Rate;
            int age = v.Age++;
            float rel = age < v.Len ? 1f : 0f;
            float relT = (age - v.Len) / (float)Rate; // Zeit seit Notenende
            double ph = (double)v.F * age / Rate;
            o = 0;
            switch (v.I)
            {
                case Inst.Pluck:
                    {
                        float s = 0, dk = (float)Math.Exp(-t * 1.5f), a = (float)Math.Exp(-t * 1.6f);
                        for (int k = 1; k <= 6 && v.F * k < 10000; k++) { a *= dk; s += S(ph * k) * a / k; }
                        if (t < 0.004f) s += Noise() * 0.25f * (1 - t / 0.004f);
                        float env = Math.Min(1f, t * 600) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 9));
                        o = s * env * 0.55f;
                        if (t > 3f || (rel == 0 && relT > 0.6f)) return false;
                        break;
                    }
                case Inst.Pad:
                    {
                        float s = 0;
                        for (int k = 1; k <= 5; k++) s += (S(ph * k * 1.004) + S(ph * k * 0.996)) / k;
                        float env = Math.Min(1f, t / 0.35f) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 3.5f));
                        v.A += (s - v.A) * 0.12f;
                        o = v.A * env * 0.13f;
                        if (rel == 0 && relT > 1.6f) return false;
                        break;
                    }
                case Inst.Bass:
                    {
                        float s = S(ph) + 0.35f * S(ph * 2) + 0.1f * S(ph * 3);
                        float env = Math.Min(1f, t * 250) * (0.72f + 0.28f * (float)Math.Exp(-t * 6)) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 30));
                        o = (float)Math.Tanh(s * 1.4f) * env * 0.42f;
                        if (rel == 0 && relT > 0.2f) return false;
                        break;
                    }
                case Inst.Organ:
                    {
                        float s = S(ph) + 0.7f * S(ph * 2) + 0.4f * S(ph * 3) + 0.28f * S(ph * 4) + 0.14f * S(ph * 6);
                        float trem = 1f + 0.16f * S(t * 5.5);
                        float env = Math.Min(1f, t / 0.03f) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 14));
                        o = s * trem * env * 0.14f;
                        if (rel == 0 && relT > 0.5f) return false;
                        break;
                    }
                case Inst.Bell:
                    {
                        float s = S(ph) * (float)Math.Exp(-t * 1.5f) + 0.5f * S(ph * 2.0) * (float)Math.Exp(-t * 2.6f)
                                + 0.33f * S(ph * 2.76) * (float)Math.Exp(-t * 3.8f) + (v.F < 1850 ? 0.18f * S(ph * 5.4) * (float)Math.Exp(-t * 7f) : 0);
                        o = s * Math.Min(1f, t * 500) * 0.5f;
                        if (t > 3.2f) return false;
                        break;
                    }
                case Inst.Flute:
                    {
                        float vib = 1f + 0.006f * S(t * 5.2) * Math.Max(0f, Math.Min(1f, (t - 0.18f) * 4));
                        v.Ph += v.F * vib / Rate;
                        float s = S(v.Ph) + 0.22f * S(v.Ph * 2) + 0.06f * S(v.Ph * 3);
                        v.B += (Noise() - v.B) * 0.25f;
                        float env = Math.Min(1f, t / 0.07f) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 16));
                        float breath = 0.12f + 0.4f * (float)Math.Exp(-t * 14);
                        o = (s + v.B * breath) * env * 0.3f;
                        if (rel == 0 && relT > 0.4f) return false;
                        break;
                    }
                case Inst.Lead:
                    {
                        float vib = 1f + 0.005f * S(t * 5.5) * Math.Max(0f, Math.Min(1f, (t - 0.2f) * 3));
                        v.Ph += v.F * vib / Rate;
                        float s = S(v.Ph) + 0.25f * S(v.Ph * 2) + 0.33f * S(v.Ph * 3) + 0.12f * S(v.Ph * 4) + 0.2f * S(v.Ph * 5) + 0.1f * S(v.Ph * 7);
                        v.A += (s - v.A) * 0.35f;
                        float env = Math.Min(1f, t / 0.015f) * (0.8f + 0.2f * (float)Math.Exp(-t * 4)) * (rel > 0 ? 1f : (float)Math.Exp(-relT * 22));
                        o = v.A * env * 0.2f;
                        if (rel == 0 && relT > 0.3f) return false;
                        break;
                    }
                case Inst.Kick:
                    {
                        v.Ph += (45 + 85 * Math.Exp(-t * 32)) / Rate;
                        o = (S(v.Ph) * (float)Math.Exp(-t * 7) + (t < 0.003f ? Noise() * 0.3f : 0)) * 0.9f;
                        if (t > 0.45f) return false;
                        break;
                    }
                case Inst.Snare:
                    {
                        float n = Noise();
                        v.A += (n - v.A) * 0.5f;
                        float hp = n - v.A;
                        o = (hp * (float)Math.Exp(-t * 17) * 0.8f + S(t * 190.0) * (float)Math.Exp(-t * 28) * 0.45f);
                        if (t > 0.3f) return false;
                        break;
                    }
                case Inst.Hat:
                    {
                        float n = Noise();
                        v.A += (n - v.A) * 0.6f;
                        o = (n - v.A) * (float)Math.Exp(-t * 55) * 0.4f;
                        if (t > 0.12f) return false;
                        break;
                    }
                case Inst.Taiko:
                    {
                        v.Ph += (68 + 45 * Math.Exp(-t * 16)) / Rate;
                        v.A += (Noise() - v.A) * 0.08f;
                        o = (S(v.Ph) * (float)Math.Exp(-t * 5) + v.A * (float)Math.Exp(-t * 22) * 1.4f) * 0.85f;
                        if (t > 0.9f) return false;
                        break;
                    }
                case Inst.Gong:
                    {
                        float s = S(ph) * (float)Math.Exp(-t * 0.6f) + 0.6f * S(ph * 1.483) * (float)Math.Exp(-t * 0.9f)
                                + 0.45f * S(ph * 2.03) * (float)Math.Exp(-t * 1.2f) + 0.3f * S(ph * 2.74 + 0.3 * S(t * 3.1)) * (float)Math.Exp(-t * 1.6f)
                                + 0.2f * S(ph * 3.6) * (float)Math.Exp(-t * 2.2f);
                        o = s * Math.Min(1f, t / 0.02f) * 0.32f;
                        if (t > 4.5f) return false;
                        break;
                    }
                case Inst.Wood:
                    {
                        o = (S(t * 880.0) * (float)Math.Exp(-t * 48) + 0.5f * S(t * 1390.0) * (float)Math.Exp(-t * 70)) * 0.5f;
                        if (t > 0.14f) return false;
                        break;
                    }
            }
            o *= v.Vel;
            return true;
        }

        float Reverb(float x)
        {
            float outS = 0;
            for (int i = 0; i < 4; i++)
            {
                float[] b = comb[i];
                float y = b[combPos[i]];
                combLp[i] = y * 0.7f + combLp[i] * 0.3f;
                b[combPos[i]] = x * 0.5f + combLp[i] * 0.8f;
                if (++combPos[i] >= b.Length) combPos[i] = 0;
                outS += y;
            }
            for (int i = 0; i < 2; i++)
            {
                float[] b = ap[i];
                float bo = b[apPos[i]];
                float y = -outS + bo;
                b[apPos[i]] = outS + bo * 0.5f;
                if (++apPos[i] >= b.Length) apPos[i] = 0;
                outS = y;
            }
            return outS * 0.25f;
        }
    }
}
