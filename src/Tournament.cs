using System;
using System.Collections.Generic;

namespace Sensensturm
{
    // Teilnehmer eines Turniers. Cpu: 0 = Mensch, 1..3 = CPU leicht/mittel/schwer
    public class Entrant
    {
        public string Name;
        public int Char, Cpu;
        public bool Human { get { return Cpu == 0; } }
    }

    // K.-o.-Turnier für 2 bis 16 Teilnehmer. Reine Logik ohne Grafik.
    // Slot[r][i]: Teilnehmer auf Platz i in Runde r (Bye = Freilos, Open = noch offen).
    // Die Paarung m in Runde r besteht aus den Plätzen 2m und 2m+1, der Sieger zieht auf Slot[r+1][m].
    public class Tournament
    {
        public const int Bye = -2, Open = -1, MaxPlayers = 16;
        public readonly List<Entrant> E = new List<Entrant>();
        public int Size, Rounds;
        public int[][] Slot;
        public int[][] FillTime;   // Zeitpunkt (Frame), an dem ein Platz besetzt wurde – für Animationen
        public int[] OutTime;      // je Teilnehmer: Zeitpunkt des Ausscheidens (-1 = noch dabei)
        public int Stocks = 3, Minutes = 3, Stage = -1; // Stage -1 = zufällig je Match
        public bool AutoCpu = true;                     // CPU gegen CPU automatisch ausrechnen

        static readonly string[] cpuNames =
        {
            "Morgana", "Grimbart", "Sylas", "Nyx", "Brakka", "Elowen", "Vorn", "Ravena",
            "Thorgal", "Kira", "Malek", "Isolde", "Fenrik", "Zara", "Odran", "Lumi"
        };

        public static string CpuName(int i) { return cpuNames[((i % cpuNames.Length) + cpuNames.Length) % cpuNames.Length]; }

        public static string RoundName(int size, int r)
        {
            int left = size >> r;
            return left == 2 ? "Finale" : left == 4 ? "Halbfinale" : left == 8 ? "Viertelfinale" : left == 16 ? "Achtelfinale" : "Runde " + (r + 1);
        }

        // Teilnehmer auslosen und Baum aufbauen; Freilose werden sofort (gestaffelt) weitergereicht
        public void Build(Random rng, int now)
        {
            int n = E.Count;
            if (n < 2) throw new InvalidOperationException("Mindestens 2 Teilnehmer");
            Size = 2; Rounds = 1;
            while (Size < n) { Size *= 2; Rounds++; }
            Slot = new int[Rounds + 1][];
            FillTime = new int[Rounds + 1][];
            for (int r = 0; r <= Rounds; r++)
            {
                Slot[r] = new int[Size >> r];
                FillTime[r] = new int[Size >> r];
                for (int i = 0; i < Slot[r].Length; i++) Slot[r][i] = Open;
            }
            OutTime = new int[n];
            for (int i = 0; i < n; i++) OutTime[i] = -1;

            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            // jede Paarung bekommt mindestens einen echten Teilnehmer, Freilose verteilen sich gleichmäßig
            int half = Size / 2;
            for (int m = 0; m < half; m++)
            {
                Slot[0][2 * m] = order[m];
                Slot[0][2 * m + 1] = m + half < n ? order[m + half] : Bye;
            }
            for (int i = 0; i < Size; i++) FillTime[0][i] = now + i * 3; // Einflug nacheinander
            for (int m = 0; m < half; m++)
            {
                int a = Slot[0][2 * m], b = Slot[0][2 * m + 1];
                if (b == Bye) Advance(0, m, a, now + Size * 3 + 20 + m * 6);
            }
        }

        public bool Done { get { return Slot != null && Slot[Rounds][0] >= 0; } }
        public int Champion { get { return Done ? Slot[Rounds][0] : -1; } }

        // nächste auszutragende Paarung (Runde für Runde von oben nach unten), false = Turnier vorbei
        public bool NextMatch(out int r, out int m)
        {
            for (r = 0; r < Rounds; r++)
                for (m = 0; m < Slot[r].Length / 2; m++)
                    if (Slot[r][2 * m] >= 0 && Slot[r][2 * m + 1] >= 0 && Slot[r + 1][m] == Open) return true;
            r = m = -1;
            return false;
        }

        public void Advance(int r, int m, int winner, int now)
        {
            int a = Slot[r][2 * m], b = Slot[r][2 * m + 1];
            if (winner != a && winner != b) throw new ArgumentException("Sieger gehört nicht zur Paarung");
            Slot[r + 1][m] = winner;
            FillTime[r + 1][m] = now;
            int loser = winner == a ? b : a;
            if (loser >= 0) OutTime[loser] = now;
        }

        // CPU gegen CPU ohne Grafik austragen, liefert den Sieger (Index in E)
        public int Simulate(int r, int m, Random rng)
        {
            int ia = Slot[r][2 * m], ib = Slot[r][2 * m + 1];
            Entrant a = E[ia], b = E[ib];
            var s = GameState.NewMatch(a.Char, b.Char, Stocks, Minutes, 0);
            var ba = new Bot(Math.Max(1, a.Cpu));
            var bb = new Bot(Math.Max(1, b.Cpu));
            int cap = 60 * 60 * (Minutes > 0 ? Minutes + 1 : 6);
            for (int t = 0; t < cap && s.phase != GameState.Over; t++)
            {
                int h0, p0, h1, p1;
                ba.Think(s, 0, out h0, out p0);
                bb.Think(s, 1, out h1, out p1);
                Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, null, false);
            }
            return WinnerOf(s, rng) == 0 ? ia : ib;
        }

        // Sieger eines beendeten (oder abgebrochenen) Matches: Leben, dann Schaden, dann Los
        public static int WinnerOf(GameState s, Random rng)
        {
            if (s.phase == GameState.Over && s.winner >= 0) return s.winner;
            PlayerState a = s.p[0], b = s.p[1];
            if (a.stocks != b.stocks) return a.stocks > b.stocks ? 0 : 1;
            if (Math.Abs(a.damage - b.damage) > 0.5f) return a.damage < b.damage ? 0 : 1;
            return rng.Next(2);
        }
    }
}
