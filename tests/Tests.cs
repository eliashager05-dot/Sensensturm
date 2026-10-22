using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

namespace Sensensturm
{
    // Automatisierte Tests: Kampfsystem, Parade, Netzwerk-Loopback und Screenshots.
    static class Tests
    {
        static string outDir;

        static void Main(string[] args)
        {
            outDir = args.Length > 0 ? args[0] : ".";
            Settings.Load();
            int fails = 0;
            fails += TestMovement();
            fails += TestAttacks();
            fails += TestParry();
            fails += TestBlock();
            fails += TestKO();
            fails += TestNetwork();
            if (Environment.GetEnvironmentVariable("ONLINE") == "1") { fails += TestOnline(false); fails += TestOnline(true); }
            fails += TestCombos();
            fails += TestNewMoves();
            fails += TestTournament();
            fails += TestPolish();
            fails += TestRecovery();
            fails += TestStages();
            fails += Screenshots();
            Console.WriteLine(fails == 0 ? "ALLE TESTS OK" : fails + " TEST(S) FEHLGESCHLAGEN");
        }

        static void Check(bool ok, string name, ref int fails)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name);
            if (!ok) fails++;
        }

        static GameState Fresh()
        {
            var s = GameState.NewMatch(0, 1, 3, 0);
            s.phase = GameState.Fight;
            s.phaseTimer = 0;
            return s;
        }

        static void Run(GameState s, int n, int h0, int h1, List<GameEvent> ev)
        {
            for (int i = 0; i < n; i++)
            {
                int p0 = h0 & ~s.p[0].prevHeld, p1 = h1 & ~s.p[1].prevHeld;
                Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, ev, false);
            }
        }

        static int TestMovement()
        {
            Console.WriteLine("Bewegung:");
            int f = 0;
            var s = Fresh();
            var ev = new List<GameEvent>();
            Run(s, 4, Btn.Right, 0, ev);
            Check(s.p[0].vx >= Tuning.RunSpeed - 0.01f, "Laufen erreicht volle Geschwindigkeit in 4 Frames (vx=" + s.p[0].vx + ")", ref f);
            Run(s, 6, 0, 0, ev);
            Check(Math.Abs(s.p[0].vx) < 0.01f, "Abbremsen ohne Eingabe", ref f);
            // voller Sprung
            s = Fresh();
            float minY = 0;
            Run(s, 1, Btn.Jump, 0, ev);
            for (int i = 0; i < 60; i++) { Run(s, 1, Btn.Jump, 0, ev); minY = Math.Min(minY, s.p[0].y); }
            var s2 = Fresh();
            float minY2 = 0;
            Run(s2, 1, Btn.Jump, 0, ev);
            for (int i = 0; i < 60; i++) { Run(s2, 1, 0, 0, ev); minY2 = Math.Min(minY2, s2.p[0].y); }
            Check(minY < minY2 - 40, "Variable Sprunghöhe (gehalten " + (int)minY + " vs. kurz " + (int)minY2 + ")", ref f);
            // Luftsprünge
            s = Fresh();
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 12, 0, 0, ev);
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 12, 0, 0, ev);
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 12, 0, 0, ev);
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 2, 0, 0, ev);
            Check(s.p[0].airJumps == 0, "Doppel-/Dreifachsprung verbraucht Luftsprünge", ref f);
            Run(s, 120, 0, 0, ev);
            Check(s.p[0].onGround && s.p[0].airJumps == Tuning.AirJumps, "Landung setzt Sprünge zurück", ref f);
            // Dash
            s = Fresh();
            Run(s, 1, Btn.Right | Btn.Dodge, 0, ev);
            Check(s.p[0].state == St.Dash && s.p[0].vx > 12, "Dash am Boden", ref f);
            Run(s, 12, Btn.Right, 0, ev);
            Check(s.p[0].sprint && s.p[0].vx > Tuning.RunSpeed, "Sprint nach Dash", ref f);
            // Fast Fall
            s = Fresh();
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 24, Btn.Jump, 0, ev);
            Run(s, 2, Btn.Down | Btn.Jump, 0, ev);
            Check(s.p[0].vy >= Tuning.FastFallSpeed - 0.01f, "Schnellfallen", ref f);
            // Luft-Dodge
            s = Fresh();
            Run(s, 1, Btn.Jump, 0, ev); Run(s, 5, 0, 0, ev);
            Run(s, 1, Btn.Dodge | Btn.Right, 0, ev);
            Check(s.p[0].state == St.AirDodge && s.p[0].invuln > 0, "Luft-Dodge mit Unverwundbarkeit", ref f);
            Run(s, 30, 0, 0, ev);
            Run(s, 1, Btn.Dodge, 0, ev);
            Check(s.p[0].state != St.AirDodge, "Zweiter Luft-Dodge gesperrt bis zur Landung", ref f);
            // Wandsprung
            s = Fresh();
            s.p[0].x = Arena.Main.L - Arena.HalfW - 30; s.p[0].y = 80; s.p[0].onGround = false; s.p[0].state = St.Air;
            Run(s, 10, Btn.Right, 0, ev);
            Check(s.p[0].state == St.Wall, "Wandkontakt / Festhalten", ref f);
            Run(s, 1, Btn.Right | Btn.Jump, 0, ev);
            Check(s.p[0].vy < -10 && s.p[0].vx < 0, "Wandsprung", ref f);
            // Soft-Plattform durchfallen
            s = Fresh();
            s.p[0].x = -350; s.p[0].y = -185; s.p[0].onGround = true; s.p[0].plat = 1;
            Run(s, 3, 0, 0, ev);
            Check(s.p[0].onGround && s.p[0].plat == 1, "Stehen auf Schwebeplattform", ref f);
            Run(s, 1, Btn.Down | Btn.Jump, 0, ev); Run(s, 10, 0, 0, ev);
            Check(s.p[0].y > -175, "Durch Schwebeplattform fallen (Unten + Springen)", ref f);
            return f;
        }

        static int TestAttacks()
        {
            int f = 0;
            foreach (int ch in new[] { 0, 2, 3 }) f += TestAttacksFor(ch);
            return f;
        }

        static int TestAttacksFor(int ch)
        {
            Console.WriteLine("Angriffe " + CharDef.All[ch].Name + " (" + CharDef.All[ch].TypeName + "):");
            int f = 0;
            bool bow = CharDef.All[ch].Weapon == Weapon.Bow;
            var combos = new[]
            {
                new { Name = "N-Light", G = true, H = Btn.Light, Dy = 0f, Dx = 70f },
                new { Name = "S-Light", G = true, H = Btn.Light | Btn.Right, Dy = 0f, Dx = 110f },
                new { Name = "U-Light", G = true, H = Btn.Light | Btn.Up, Dy = -100f, Dx = 20f },
                new { Name = "D-Light", G = true, H = Btn.Light | Btn.Down, Dy = 0f, Dx = 90f },
                new { Name = "S-Heavy", G = true, H = Btn.Heavy | Btn.Right, Dy = 0f, Dx = bow ? 260f : 150f },
                new { Name = "N-Heavy", G = true, H = Btn.Heavy, Dy = bow ? -170f : 0f, Dx = bow ? 170f : -90f },
                new { Name = "D-Heavy", G = true, H = Btn.Heavy | Btn.Down, Dy = 0f, Dx = 120f },
                new { Name = "N-Air", G = false, H = Btn.Light, Dy = 0f, Dx = 60f },
                new { Name = "S-Air", G = false, H = Btn.Light | Btn.Right, Dy = 0f, Dx = 100f },
                new { Name = "U-Air", G = false, H = Btn.Light | Btn.Up, Dy = -110f, Dx = 0f },
                new { Name = "D-Air", G = false, H = Btn.Light | Btn.Down, Dy = 70f, Dx = 20f },
                new { Name = "S-Air-Heavy", G = false, H = Btn.Heavy | Btn.Right, Dy = bow ? 40f : 0f, Dx = bow ? 240f : 120f },
                new { Name = "D-Air-Heavy", G = false, H = Btn.Heavy, Dy = 60f, Dx = 0f },
                new { Name = "U-Air-Heavy", G = false, H = Btn.Heavy | Btn.Up, Dy = -90f, Dx = 40f },
            };
            foreach (var c in combos)
            {
                var s = Fresh();
                s.p[0].charId = ch; s.p[1].charId = ch == 0 ? 1 : 0;
                var A = s.p[0]; var B = s.p[1];
                A.x = 0; A.facing = 1;
                if (!c.G) { A.x = 900; A.y = -520; A.onGround = false; A.state = St.Air; A.plat = -1; }
                B.x = A.x + c.Dx; B.y = A.y + c.Dy; B.onGround = false; B.state = St.Air; B.stateTime = 0;
                bool bAir = !(B.y >= -0.1f && B.x > Arena.Main.L && B.x < Arena.Main.R);
                if (!bAir) { B.y = 0; B.onGround = true; B.state = St.Idle; }
                B.invuln = 0;
                var ev = new List<GameEvent>();
                int hitTick = -1, startup = -1;
                for (int t = 0; t < 110; t++)
                {
                    int h = t < 2 ? c.H : 0;
                    // Gegner bis zum Treffer relativ zum Angreifer festhalten
                    if (hitTick < 0 && bAir) { B.x = A.x + c.Dx; B.y = A.y + c.Dy; B.vx = B.vy = 0; }
                    Sim.Step(s, new[] { h, 0 }, new[] { t == 0 ? h : 0, 0 }, ev, false);
                    if (A.state == St.Attack && startup < 0) startup = Attacks.Of(A).Startup;
                    if (Environment.GetEnvironmentVariable("DBG") == ch + c.Name && t < 24) { Box hb; AttackDef dd; bool on = Sim.HitboxActive(A, out hb, out dd); Box hu = B.Hurt; Console.WriteLine("    t" + t + " A " + A.state + "/" + A.atk + " f" + A.atkFrame + " y" + (int)A.y + " hb " + (on ? (int)hb.L + ".." + (int)hb.R + " / " + (int)hb.T + ".." + (int)hb.B : "-") + " | B " + B.state + " hurt " + (int)hu.L + ".." + (int)hu.R + " / " + (int)hu.T + ".." + (int)hu.B); }
                    foreach (var e in ev) if (e.Type == Ev.Hit && hitTick < 0) hitTick = t;
                    ev.Clear();
                }
                bool ok = hitTick >= 0 && B.damage > 0;
                Check(ok, c.Name + ": Treffer an Tick " + hitTick + " (Startup " + startup + "), Schaden " + B.damage.ToString("0.0") + ", Knockback-Richtung vx=" + B.vx.ToString("0.0") + " vy=" + B.vy.ToString("0.0"), ref f);
            }
            // Keine Mehrfachtreffer derselben Attacke
            {
                var s = Fresh();
                s.p[1].x = s.p[0].x + 70;
                var ev = new List<GameEvent>();
                int hits = 0;
                for (int t = 0; t < 30; t++)
                {
                    Sim.Step(s, new[] { t < 1 ? Btn.Light : 0, 0 }, new[] { t == 0 ? Btn.Light : 0, 0 }, ev, false);
                    foreach (var e in ev) if (e.Type == Ev.Hit) hits++;
                    ev.Clear();
                }
                Check(hits == 1, "Mehrfachtreffer-Schutz (Treffer: " + hits + ")", ref f);
            }
            // Combo: N-Light -> U-Light per Cancel nach Treffer
            {
                var s = Fresh();
                s.p[1].x = s.p[0].x + 70;
                var ev = new List<GameEvent>();
                int hits = 0;
                for (int t = 0; t < 60; t++)
                {
                    int h = t == 0 ? Btn.Light : (t >= 10 && t <= 12 ? Btn.Light | Btn.Up : 0);
                    int p = (t == 0 || t == 10) ? Btn.Light : 0;
                    Sim.Step(s, new[] { h, 0 }, new[] { p, 0 }, ev, false);
                    foreach (var e in ev) if (e.Type == Ev.Hit) hits++;
                    ev.Clear();
                }
                Check(hits == 2 && s.p[0].combo == 2, "Combo N-Light > U-Light (Treffer " + hits + ", Combo " + s.p[0].combo + ")", ref f);
            }
            // Whiff: kein Cancel ohne Treffer
            {
                var s = Fresh();
                s.p[1].x = 600;
                var ev = new List<GameEvent>();
                int t0 = -1;
                for (int t = 0; t < 40; t++)
                {
                    int p = (t == 0 || t == 10) ? Btn.Light : 0;
                    Sim.Step(s, new[] { p, 0 }, new[] { p, 0 }, ev, false);
                    if (t == 11) t0 = s.p[0].atk;
                }
                Check(t0 == Attacks.NLight, "Kein Cancel bei verfehltem Angriff", ref f);
            }
            // Aufladen erhöht Schaden
            {
                float d1 = ChargeDamage(0), d2 = ChargeDamage(60);
                Check(d2 > d1 * 1.5f, "Aufgeladener Angriff: " + d1.ToString("0.0") + " -> " + d2.ToString("0.0") + " Schaden", ref f);
            }
            return f;
        }

        static float ChargeDamage(int hold)
        {
            var s = Fresh();
            s.p[1].x = s.p[0].x + 100;
            var ev = new List<GameEvent>();
            for (int t = 0; t < 140; t++)
                Sim.Step(s, new[] { t <= hold ? Btn.Heavy : 0, 0 }, new[] { t == 0 ? Btn.Heavy : 0, 0 }, ev, false);
            return s.p[1].damage;
        }

        static int ParryAt(int shieldTick, out List<GameEvent> all, out GameState st)
        {
            var s = Fresh();
            s.p[0].x = 0; s.p[1].x = 100; s.p[1].facing = -1; s.p[0].facing = 1;
            all = new List<GameEvent>();
            var ev = new List<GameEvent>();
            // P1 macht S-Heavy (Startup 18) Richtung P0, P0 drückt Schild bei shieldTick
            for (int t = 0; t < 70; t++)
            {
                int h1 = t < 2 ? Btn.Heavy | Btn.Left : 0;
                int h0 = t >= shieldTick ? Btn.Shield : 0;
                Sim.Step(s, new[] { h0, h1 }, new[] { t == shieldTick ? Btn.Shield : 0, t == 0 ? Btn.Heavy : 0 }, ev, false);
                all.AddRange(ev);
                ev.Clear();
            }
            st = s;
            return 0;
        }

        static int TestParry()
        {
            Console.WriteLine("Parade:");
            int f = 0;
            List<GameEvent> ev; GameState s;
            ParryAt(14, out ev, out s);
            bool parried = ev.Exists(e => e.Type == Ev.Parry);
            Check(parried && s.p[0].damage == 0, "Perfekt getimte Parade wehrt ab (Schaden " + s.p[0].damage + ")", ref f);
            Check(s.p[0].statParries == 1, "Parade gezählt", ref f);
            ParryAt(14, out ev, out s);
            // Angreifer betäubt?
            var s3 = Fresh();
            s3.p[0].x = 0; s3.p[1].x = 100; s3.p[1].facing = -1;
            var e3 = new List<GameEvent>();
            bool stunned = false;
            for (int t = 0; t < 40; t++)
            {
                Sim.Step(s3, new[] { t >= 14 ? Btn.Shield : 0, t < 2 ? Btn.Heavy | Btn.Left : 0 }, new[] { t == 14 ? Btn.Shield : 0, t == 0 ? Btn.Heavy : 0 }, e3, false);
                if (s3.p[1].state == St.Parried) stunned = true;
            }
            Check(stunned, "Angreifer wird nach Parade betäubt", ref f);
            ParryAt(2, out ev, out s);
            bool p2 = ev.Exists(e => e.Type == Ev.Parry);
            Check(!p2, "Zu frühes Schild = keine Parade (nur Block oder Treffer)", ref f);
            return f;
        }

        static int TestBlock()
        {
            Console.WriteLine("Block / Schild:");
            int f = 0;
            var s = Fresh();
            s.p[0].x = 0; s.p[1].x = 70; s.p[1].facing = -1;
            var ev = new List<GameEvent>();
            var all = new List<GameEvent>();
            for (int t = 0; t < 40; t++)
            {
                Sim.Step(s, new[] { Btn.Shield, t == 15 ? Btn.Light : 0 }, new[] { t == 0 ? Btn.Shield : 0, t == 15 ? Btn.Light : 0 }, ev, false);
                all.AddRange(ev); ev.Clear();
            }
            Check(all.Exists(e => e.Type == Ev.Block) && s.p[0].damage < 2, "Block reduziert Schaden (" + s.p[0].damage.ToString("0.0") + ")", ref f);
            Check(s.p[0].shieldHp < Tuning.ShieldMax - 5, "Block belastet Schild (" + (int)s.p[0].shieldHp + ")", ref f);
            // Schildbruch durch schwere Angriffe
            s = Fresh();
            s.p[0].x = 0; s.p[1].x = 120; s.p[1].facing = -1;
            all.Clear();
            for (int t = 0; t < 400; t++)
            {
                bool start = t % 60 == 0;
                Sim.Step(s, new[] { Btn.Shield, start ? Btn.Heavy | Btn.Left : 0 }, new[] { t == 0 ? Btn.Shield : 0, start ? Btn.Heavy : 0 }, ev, false);
                all.AddRange(ev); ev.Clear();
                s.p[0].x = 0; s.p[1].x = 120;
            }
            Check(all.Exists(e => e.Type == Ev.ShieldBreak), "Schwere Angriffe brechen den Schild", ref f);
            // Schild von hinten
            s = Fresh();
            s.p[0].x = 0; s.p[0].facing = -1; s.p[1].x = 70; s.p[1].facing = -1;
            all.Clear();
            for (int t = 0; t < 40; t++)
            {
                Sim.Step(s, new[] { Btn.Shield, t < 1 ? Btn.Light : 0 }, new[] { 0, t == 0 ? Btn.Light : 0 }, ev, false);
                all.AddRange(ev); ev.Clear();
                if (t == 0) s.p[0].facing = -1;
            }
            Check(all.Exists(e => e.Type == Ev.Hit), "Block schützt nicht von hinten", ref f);
            return f;
        }

        static int TestKO()
        {
            Console.WriteLine("KO:");
            int f = 0;
            var s = Fresh();
            s.p[1].damage = 160;
            s.p[0].x = 300; s.p[1].x = 430;
            var ev = new List<GameEvent>();
            var all = new List<GameEvent>();
            for (int t = 0; t < 400; t++)
            {
                Sim.Step(s, new[] { t < 50 ? Btn.Heavy | Btn.Right : 0, 0 }, new[] { t == 0 ? Btn.Heavy : 0, 0 }, ev, false);
                all.AddRange(ev); ev.Clear();
            }
            Check(all.Exists(e => e.Type == Ev.KO) && s.p[1].stocks == 2, "Aufgeladener schwerer Hieb bei 160% schleudert aus der Arena (Leben " + s.p[1].stocks + ")", ref f);
            Check(s.p[1].state != St.KO, "Respawn nach KO (Zustand " + s.p[1].state + ")", ref f);
            return f;
        }

        static int TestNetwork()
        {
            Console.WriteLine("Netzwerk (Loopback):");
            int f = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Func<double> now = () => sw.Elapsed.TotalSeconds;
            var host = new HostSession();
            string err = host.Start(47123);
            Check(err == null, "Host öffnet Port 47123 " + (err ?? ""), ref f);
            var cl = new ClientSession();
            Check(cl.Open() == null, "Client-Socket", ref f);
            cl.Connect("127.0.0.1", 47123, now());
            for (int i = 0; i < 100 && cl.Status != ClientSession.Connected; i++) { host.Poll(now()); cl.Poll(now()); Thread.Sleep(5); }
            Check(cl.Status == ClientSession.Connected && host.HasClient, "Client tritt bei", ref f);

            // zweiter Client wird abgewiesen
            var c2 = new ClientSession(); c2.Open(); c2.Connect("127.0.0.1", 47123, now());
            for (int i = 0; i < 60 && c2.Status == ClientSession.Connecting; i++) { host.Poll(now()); c2.Poll(now()); Thread.Sleep(5); }
            Check(c2.Status == ClientSession.Failed && c2.Error.Contains("voll"), "Zweiter Spieler: \"" + c2.Error + "\"", ref f);
            c2.Close();

            // falscher Port -> Zeitüberschreitung
            var c3 = new ClientSession(); c3.Open(); c3.Connect("127.0.0.1", 47199, now() - 5.9);
            for (int i = 0; i < 40 && c3.Status == ClientSession.Connecting; i++) { c3.Poll(now()); Thread.Sleep(5); }
            Check(c3.Status == ClientSession.Failed, "Fehlermeldung bei falscher Adresse: \"" + c3.Error.Substring(0, Math.Min(60, c3.Error.Length)) + "...\"", ref f);
            c3.Close();
            var c4 = new ClientSession(); c4.Open(); c4.Connect("192.168.abc", 47123, now());
            Check(c4.Status == ClientSession.Failed, "Ungültige IP erkannt", ref f);
            c4.Close();

            // Discovery
            var c5 = new ClientSession(); c5.Open();
            for (int i = 0; i < 60 && c5.Found.Count == 0; i++) { c5.Discover(now() + i, 47123); host.Poll(now()); c5.Poll(now()); Thread.Sleep(10); }
            Check(c5.Found.Count > 0, "LAN-Suche findet Lobby (" + (c5.Found.Count > 0 ? c5.Found[0].Ip : "-") + ")", ref f);
            c5.Close();

            // Match: Client läuft nach rechts und greift an; Host bleibt stehen
            host.ResetMatch();
            host.InMatch = true;
            var hs = GameState.NewMatch(0, 1, 3, 0);
            hs.phase = GameState.Fight; hs.phaseTimer = 0;
            GameState pred = null;
            var ev = new List<GameEvent>();
            int clientHits = 0, maxErr = 0;
            for (int t = 0; t < 240; t++)
            {
                host.Poll(now());
                cl.Poll(now());
                if (cl.NewSnapshot && cl.Auth != null)
                {
                    pred = cl.Auth.Clone();
                    foreach (var ni in cl.Pending) Sim.Step(pred, new[] { pred.p[0].prevHeld, ni.Held }, new[] { 0, ni.Pressed }, null, true);
                }
                int h = t < 54 ? Btn.Left : (t % 30 == 0 ? Btn.Light : 0);
                int pr = t < 54 ? (t == 0 ? Btn.Left : 0) : h;
                cl.SendInput(h, pr);
                if (pred != null) Sim.Step(pred, new[] { pred.p[0].prevHeld, h }, new[] { 0, pr }, null, true);
                Thread.Sleep(2);
                host.Poll(now());
                int h1, p1;
                host.TakeInput(out h1, out p1);
                ev.Clear();
                Sim.Step(hs, new[] { 0, h1 }, new[] { 0, p1 }, ev, false);
                host.SendSnapshot(hs, ev);
                foreach (var e in cl.NewEvents) if (e.Type == Ev.Hit) clientHits++;
                cl.NewEvents.Clear();
                if (pred != null && t > 20) maxErr = Math.Max(maxErr, (int)Math.Abs(pred.p[1].x - hs.p[1].x));
            }
            cl.Poll(now());
            foreach (var e in cl.NewEvents) if (e.Type == Ev.Hit) clientHits++;
            Check(hs.p[1].x < 200, "Host verarbeitet Client-Eingaben (Client-x=" + (int)hs.p[1].x + ")", ref f);
            Check(hs.p[0].damage > 0, "Client-Angriff vom Host gewertet (Host-Schaden " + hs.p[0].damage.ToString("0.0") + ")", ref f);
            Check(clientHits > 0, "Treffer-Ereignisse erreichen den Client (" + clientHits + ")", ref f);
            Check(cl.AuthTick > 200, "Snapshots synchron (Tick " + cl.AuthTick + "/" + hs.tick + ")", ref f);
            Check(maxErr < 40, "Vorhersage-Abweichung klein (max " + maxErr + " px)", ref f);
            Check(Math.Abs(Tuning.RunSpeed - 9.4f) < 0.01f, "Tuning übertragen", ref f);

            host.Close();
            for (int i = 0; i < 20; i++) { cl.Poll(now()); Thread.Sleep(5); }
            Check(cl.Status == ClientSession.Failed && cl.Error.Contains("geschlossen"), "Client erkennt geschlossene Lobby: \"" + cl.Error + "\"", ref f);
            cl.Close();
            return f;
        }

        // ---------------------------------------------------------------- Combos
        // Trigger: 0 = n Frames nach dem vorigen Schritt, 1 = direkt nach dem Trefferstopp (perfekt), 2 = beim Greifen
        class StepDef { public int Bits, Trigger, Wait, Hold; public StepDef(int b, int t, int w, int h) { Bits = b; Trigger = t; Wait = w; Hold = h; } }

        static StepDef At(int bits, int wait) { return new StepDef(bits, 0, wait, 3); }
        static StepDef OnHit(int bits) { return new StepDef(bits, 1, 0, 3); }
        static StepDef OnHitHold(int bits, int hold) { return new StepDef(bits, 1, 0, hold); }
        static StepDef OnGrab(int bits) { return new StepDef(bits, 2, 0, 3); }

        static int comboChar = 0;

        static void RunCombo(StepDef[] steps, float dummyX, float startDamage, out int hits, out int combo, out int perfects, out GameState st, bool trace)
        {
            var s = Fresh();
            s.p[0].charId = comboChar; s.p[1].charId = comboChar == 0 ? 1 : 0;
            var A = s.p[0]; var B = s.p[1];
            A.x = 0; A.facing = 1; B.x = dummyX; B.facing = -1; B.damage = startDamage;
            var ev = new List<GameEvent>();
            hits = 0; combo = 0; perfects = 0;
            int idx = 0, lastFire = 0, holdBits = 0, holdLeft = 0, hitsAtFire = 0;
            for (int t = 0; t < 900 && (idx < steps.Length || holdLeft > 0 || A.state == St.Attack); t++)
            {
                int pressed = 0;
                if (idx < steps.Length)
                {
                    StepDef sd = steps[idx];
                    bool fire = false;
                    if (sd.Trigger == 0) fire = t - lastFire >= sd.Wait;
                    else if (sd.Trigger == 1) fire = hits > hitsAtFire && A.state == St.Attack && A.atkHit && A.hitlag == 0 && A.sinceHit == 2;
                    else if (sd.Trigger == 3) fire = hits > hitsAtFire && A.state == St.Attack && A.atkHit && A.hitlag == 0 && A.sinceHit == 12;
                    else fire = A.state == St.Grabbing && A.stateTime >= 6;
                    if (fire)
                    {
                        holdBits = sd.Bits; holdLeft = sd.Hold; pressed = sd.Bits; lastFire = t; hitsAtFire = hits; idx++;
                    }
                }
                int held = holdLeft > 0 ? holdBits : 0;
                if (holdLeft > 0) holdLeft--;
                Sim.Step(s, new[] { held, 0 }, new[] { pressed, 0 }, ev, false);
                foreach (var e in ev)
                {
                    if (e.Type == Ev.Hit && e.Player == 1) hits++;
                    if (e.Type == Ev.Perfect) perfects++;
                }
                ev.Clear();
                combo = Math.Max(combo, A.combo);
                if (trace) Console.WriteLine("    t" + t + " A:" + A.state + "/" + A.atk + " f" + A.atkFrame + " x" + (int)A.x + " y" + (int)A.y + " | B:" + B.state + " hs" + B.hitstun + " x" + (int)B.x + " y" + (int)B.y + " hits" + hits);
                if (B.state == St.KO) break;
            }
            st = s;
        }

        static int TestCombos()
        {
            int f = 0;
            foreach (int ch in new[] { 0, 2, 3 }) { comboChar = ch; f += TestCombosFor(ch); }
            comboChar = 0;
            return f;
        }

        static int TestCombosFor(int ch)
        {
            Console.WriteLine("Combos " + CharDef.All[ch].Name + " (perfektes Timing, Trainingspuppe):");
            int f = 0;
            int w = CharDef.All[ch].Weapon;
            int L = Btn.Light, H = Btn.Heavy, J = Btn.Jump, R = Btn.Right, U = Btn.Up, D = Btn.Down;
            var scythe = new[]
            {
                new { Name = "Erster Schnitt", X = 70f, Steps = new[] { At(L, 0), OnHit(R | L) }, Need = 2 },
                new { Name = "Aufwärtsspirale", X = 70f, Steps = new[] { At(L, 0), OnHit(U | L), OnHitHold(J, 8), At(U | L, 5) }, Need = 3 },
                new { Name = "Bodenschnitt-Kette", X = 90f, Steps = new[] { At(D | L, 0), OnHitHold(J, 8), At(R | L, 4), OnHitHold(J, 8), At(U | L, 4) }, Need = 3 },
                new { Name = "Haken & Wurf", X = 80f, Steps = new[] { At(Btn.Grab, 0), OnGrab(D), OnHit(U | L), OnHitHold(J, 8), At(U | L, 5) }, Need = 3 },
                new { Name = "Schnitter-Jagd", X = 100f, Steps = new[] { At(R | L, 0), OnHit(Btn.Dodge), At(R | L, 5), OnHit(R | H) }, Need = 3 },
            };
            var hammer = new[]
            {
                new { Name = "Steinschlag-Kette", X = 80f, Steps = new[] { At(L, 0), OnHit(R | L) }, Need = 2 },
                new { Name = "Gipfelstürmer", X = 50f, Steps = new[] { At(U | L, 0), OnHitHold(J, 8), At(U | L, 4) }, Need = 2 },
                new { Name = "Steingriff-Wurf", X = 80f, Steps = new[] { At(Btn.Grab, 0), OnGrab(D), OnHit(U | L), OnHitHold(J, 8), At(U | L, 4) }, Need = 3 },
                new { Name = "Felsbrecher", X = 80f, Steps = new[] { At(L, 0), OnHit(R | H) }, Need = 2 },
            };
            var bow = new[]
            {
                new { Name = "Trittwirbel", X = 70f, Steps = new[] { At(L, 0), OnHit(R | L) }, Need = 2 },
                new { Name = "Mondaufstieg", X = 50f, Steps = new[] { At(U | L, 0), OnHitHold(J, 8), At(U | L, 4) }, Need = 2 },
                new { Name = "Gleitfalle", X = 90f, Steps = new[] { At(D | L, 0), OnHitHold(J, 8), At(R | L, 4) }, Need = 2 },
                new { Name = "Rankenwurf", X = 170f, Steps = new[] { At(Btn.Grab, 0), OnGrab(D), OnHit(U | L), OnHitHold(J, 8), At(U | L, 4) }, Need = 3 },
                new { Name = "Pfeilhagel", X = 80f, Steps = new[] { At(R | L, 0), OnHit(R | H) }, Need = 2 },
            };
            var combos = w == Weapon.Hammer ? hammer : w == Weapon.Bow ? bow : scythe;
            foreach (var c in combos)
            {
                int hits, combo, perf; GameState s;
                RunCombo(c.Steps, c.X, 0, out hits, out combo, out perf, out s, Environment.GetEnvironmentVariable("TRACE") == c.Name);
                if (hits < c.Need && Environment.GetEnvironmentVariable("AUTOTRACE") == "1") RunCombo(c.Steps, c.X, 0, out hits, out combo, out perf, out s, true);
                Check(hits >= c.Need && combo >= c.Need, c.Name + ": " + hits + " Treffer, Combo " + combo + ", " + perf + "x PERFEKT", ref f);
            }
            // Greifen geht durch den Schild
            {
                var s = Fresh();
                s.p[0].x = 0; s.p[1].x = 80; s.p[1].facing = -1;
                var ev = new List<GameEvent>(); var all = new List<GameEvent>();
                for (int t = 0; t < 60; t++)
                {
                    int g = t == 12 ? Btn.Grab : 0, th = t >= 30 ? Btn.Up : 0;
                    Sim.Step(s, new[] { g | th, Btn.Shield }, new[] { g | (t == 30 ? Btn.Up : 0), t == 0 ? Btn.Shield : 0 }, ev, false);
                    all.AddRange(ev); ev.Clear();
                }
                Check(all.Exists(e => e.Type == Ev.Grab) && all.Exists(e => e.Type == Ev.Throw) && s.p[1].damage > 0, "Sensenhaken packt durch den Schild und wirft nach oben (Schaden " + s.p[1].damage + ")", ref f);
            }
            // Himmelsleiter: endlos mit perfektem Timing
            {
                var steps = new List<StepDef> { At(U | L, 0) };
                for (int i = 0; i < 10; i++) { steps.Add(OnHitHold(J, 6)); steps.Add(At(U | L, 4)); }
                int hits, combo, perf; GameState s;
                RunCombo(steps.ToArray(), 40, 0, out hits, out combo, out perf, out s, Environment.GetEnvironmentVariable("TRACE") == "Himmel" + ch);
                Check(combo >= 10 || (combo >= 6 && s.p[1].state == St.KO), "Himmelsleiter (endlos): Combo " + combo + ", " + perf + "x PERFEKT, Gegner " + (s.p[1].state == St.KO ? "K.O. oben raus" : "y=" + (int)s.p[1].y), ref f);
            }
            // Gegenprobe: ohne perfektes Timing (zu spät) reißt die Himmelsleiter ab
            {
                var steps = new List<StepDef> { At(U | L, 0) };
                for (int i = 0; i < 10; i++) { steps.Add(new StepDef(J, 3, 0, 6)); steps.Add(At(U | L, 4)); }
                int hits, combo, perf; GameState s;
                RunCombo(steps.ToArray(), 40, 0, out hits, out combo, out perf, out s, false);
                Check(combo < 10, "Gegenprobe: zu spätes Timing bricht ab (Combo " + combo + ")", ref f);
            }
            return f;
        }

        static GameState Duel(int c0, int c1, float x1)
        {
            var s = GameState.NewMatch(c0, c1, 3, 0);
            s.phase = GameState.Fight;
            s.phaseTimer = 0;
            s.p[0].x = 0; s.p[0].facing = 1;
            s.p[1].x = x1; s.p[1].facing = x1 >= 0 ? -1 : 1;
            return s;
        }

        // input(t) liefert die gehaltenen Tasten beider Spieler; gedrückt = neu gegenüber dem Vorframe
        static List<GameEvent> Play(GameState s, int frames, Func<int, int[]> input)
        {
            var all = new List<GameEvent>(); var ev = new List<GameEvent>();
            int[] prev = { 0, 0 };
            for (int t = 0; t < frames; t++)
            {
                int[] h = input(t);
                Sim.Step(s, h, new[] { h[0] & ~prev[0], h[1] & ~prev[1] }, ev, false);
                prev = h;
                all.AddRange(ev); ev.Clear();
            }
            return all;
        }

        static int TestNewMoves()
        {
            Console.WriteLine("Neue Angriffe und Ultimate:");
            int f = 0;
            // Ultimate braucht eine volle Leiste
            {
                var s = Duel(0, 1, 120);
                Play(s, 3, t => new[] { t == 0 ? Btn.Ult : 0, 0 });
                Check(s.p[0].atk != Attacks.Ult && s.p[1].damage == 0, "Ultimate ohne volle Leiste löst nicht aus", ref f);
            }
            // Leiste füllt sich durch Treffer
            {
                var s = Duel(0, 1, 70);
                Play(s, 30, t => new[] { t == 0 ? Btn.Light : 0, 0 });
                float dmg = Attacks.Sets[Weapon.Scythe][Attacks.NLight].Damage;
                Check(Math.Abs(s.p[0].ult - dmg * Tuning.UltGainDeal) < 0.01f && Math.Abs(s.p[1].ult - dmg * Tuning.UltGainTake) < 0.01f,
                    "Leiste füllt sich: Angreifer " + s.p[0].ult + ", Getroffener " + s.p[1].ult, ref f);
            }
            // jede Ultimate: Zeitstopp, Treffer, Leiste leer
            foreach (var u in new[] { new { Ch = 0, X = 150f }, new { Ch = 2, X = 200f }, new { Ch = 2, X = -220f }, new { Ch = 3, X = 520f } })
            {
                var s = Duel(u.Ch, 1, u.X);
                s.p[0].ult = Tuning.UltMax;
                var all = Play(s, 1, t => new[] { Btn.Ult, 0 });
                bool frozen = s.p[1].hitlag >= Tuning.UltFreeze - 1;
                all.AddRange(Play(s, 100, t => new[] { 0, 0 }));
                string nm = Attacks.SetOf(u.Ch)[Attacks.Ult].Name;
                Check(frozen && s.p[0].ult == 0 && all.Exists(e => e.Type == Ev.Ult) && s.p[1].damage >= 25,
                    "Ultimate " + nm + " (" + CharDef.All[u.Ch].Name + ", Gegner bei x=" + u.X + "): Zeitstopp " + frozen + ", Schaden " + s.p[1].damage, ref f);
            }
            // Ultimate zerbricht den Schild
            {
                var s = Duel(2, 1, 160);
                s.p[0].ult = Tuning.UltMax;
                var all = Play(s, 80, t => new[] { t >= 20 && t < 22 ? Btn.Ult : 0, Btn.Shield });
                Check(all.Exists(e => e.Type == Ev.ShieldBreak), "Weltenbrecher zerbricht einen gehaltenen Schild", ref f);
            }
            // Ultimate als Combo-Abschluss
            {
                var s = Duel(0, 1, 70);
                s.p[0].ult = Tuning.UltMax;
                bool fired = false;
                Play(s, 120, t =>
                {
                    int h = t == 0 ? Btn.Light : 0;
                    if (!fired && s.p[0].atk == Attacks.NLight && s.p[0].atkHit && s.p[0].hitlag == 0 && s.p[0].sinceHit >= Tuning.ComboCancelDelay) { h = Btn.Ult; fired = true; }
                    return new[] { h, 0 };
                });
                Check(s.p[0].statBestCombo >= 2 && s.p[1].damage >= 35, "Leicht > Ultimate als Combo: Combo " + s.p[0].statBestCombo + ", Schaden " + s.p[1].damage, ref f);
            }
            // Angriff aus dem Lauf und Hochschlag für jede Waffe
            foreach (int ch in new[] { 0, 2, 3 })
            {
                var s = Duel(ch, 1, 600);
                bool dashAtk = false;
                Play(s, 30, t =>
                {
                    if (s.p[0].atk == Attacks.DashAtk) dashAtk = true;
                    return new[] { Btn.Right | (t == 0 ? Btn.Dodge : 0) | (t == 4 ? Btn.Light : 0), 0 };
                });
                if (s.p[0].atk == Attacks.DashAtk) dashAtk = true;
                s = Duel(ch, 1, 600);
                bool up = false;
                Play(s, 12, t => { if (s.p[0].atk == Attacks.UHeavy) up = true; return new[] { Btn.Up | (t == 0 ? Btn.Heavy : 0), 0 }; });
                if (s.p[0].atk == Attacks.UHeavy) up = true;
                string w = CharDef.All[ch].Name;
                Check(dashAtk, w + ": Dash + Leicht = " + Attacks.SetOf(ch)[Attacks.DashAtk].Name, ref f);
                Check(up, w + ": Oben + Schwer = " + Attacks.SetOf(ch)[Attacks.UHeavy].Name, ref f);
            }
            // Hochschlag trifft einen Gegner über einem
            {
                var s = Duel(0, 1, 30);
                s.p[1].y = -140; s.p[1].onGround = false; s.p[1].state = St.Air; s.p[1].vy = 0;
                Play(s, 30, t => new[] { Btn.Up | (t == 0 ? Btn.Heavy : 0), 0 });
                Check(s.p[1].damage > 0 && s.p[1].vy < 0, "Mondaufgang trifft den Gegner über einem und schleudert ihn hoch (Schaden " + s.p[1].damage + ")", ref f);
            }
            // CPU nutzt die Ultimate
            {
                var s = Duel(1, 0, 120);
                s.p[1].x = 120; s.p[0].x = 0;
                s.p[1].ult = Tuning.UltMax;
                var bot = new Bot(3);
                bool used = false;
                for (int t = 0; t < 120 && !used; t++)
                {
                    int h, p; bot.Think(s, 1, out h, out p);
                    Sim.Step(s, new[] { 0, h }, new[] { 0, p }, null, false);
                    if (s.p[1].atk == Attacks.Ult) used = true;
                }
                Check(used, "CPU (schwer) setzt die Ultimate ein", ref f);
            }
            return f;
        }

        static int TestTournament()
        {
            Console.WriteLine("Turnier:");
            int f = 0;
            var rng = new Random(5);
            bool treeOk = true;
            string why = "";
            for (int n = 2; n <= Tournament.MaxPlayers; n++)
            {
                var t = new Tournament();
                for (int i = 0; i < n; i++) t.E.Add(new Entrant { Name = "T" + i, Char = i % CharDef.Count, Cpu = 2 });
                t.Build(rng, 0);
                int byes = 0, placed = 0;
                var seen = new HashSet<int>();
                for (int i = 0; i < t.Size; i++)
                {
                    int v = t.Slot[0][i];
                    if (v == Tournament.Bye) byes++;
                    else if (v >= 0 && seen.Add(v)) placed++;
                }
                for (int m = 0; m < t.Size / 2; m++)
                {
                    bool pairOk = t.Slot[0][2 * m] >= 0 || t.Slot[0][2 * m + 1] >= 0;
                    bool byeAdvanced = t.Slot[0][2 * m + 1] != Tournament.Bye || t.Slot[1][m] == t.Slot[0][2 * m];
                    if (!pairOk || !byeAdvanced) { treeOk = false; why = "n=" + n + " Paarung " + m; }
                }
                if (t.Size < n || t.Size >= 2 * n || byes != t.Size - n || placed != n) { treeOk = false; why = "n=" + n + " Size " + t.Size + " Freilose " + byes; }
            }
            Check(treeOk, "Turnierbaum für 2 bis 16 Teilnehmer: alle gesetzt, Freilose korrekt verteilt und weitergereicht" + (treeOk ? "" : " (" + why + ")"), ref f);

            // komplettes CPU-Turnier mit 16 Teilnehmern
            {
                var t = new Tournament { Stocks = 2, Minutes = 2 };
                for (int i = 0; i < 16; i++) t.E.Add(new Entrant { Name = Tournament.CpuName(i), Char = i % CharDef.Count, Cpu = 1 + i % 3 });
                t.Build(rng, 0);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int matches = 0, r, m;
                while (t.NextMatch(out r, out m)) { t.Advance(r, m, t.Simulate(r, m, rng), 0); matches++; }
                int outCount = 0;
                for (int i = 0; i < 16; i++) if (t.OutTime[i] >= 0) outCount++;
                Check(t.Done && matches == 15 && outCount == 15 && t.Champion >= 0,
                    "16 CPUs: 15 Matches ausgerechnet, Sieger " + (t.Champion >= 0 ? t.E[t.Champion].Name : "?") + " (" + sw.ElapsedMilliseconds + " ms)", ref f);
            }
            // 5 Teilnehmer: 3 Freilose, 4 echte Matches
            {
                var t = new Tournament { Stocks = 1, Minutes = 2 };
                for (int i = 0; i < 5; i++) t.E.Add(new Entrant { Name = "T" + i, Char = i % CharDef.Count, Cpu = 3 });
                t.Build(rng, 0);
                int matches = 0, r, m;
                while (t.NextMatch(out r, out m)) { t.Advance(r, m, t.Simulate(r, m, rng), 0); matches++; }
                Check(t.Done && t.Size == 8 && matches == 4, "5 Teilnehmer: Baum mit 8 Plätzen, 4 Matches bis zum Sieger", ref f);
            }
            // Gleichstand: Los entscheidet, Leben und Schaden zuerst
            {
                var s = GameState.NewMatch(0, 1, 3, 1);
                s.p[1].stocks = 2;
                bool a = Tournament.WinnerOf(s, rng) == 0;
                s.p[1].stocks = 3; s.p[0].damage = 80; s.p[1].damage = 20;
                bool b = Tournament.WinnerOf(s, rng) == 1;
                Check(a && b, "Sieger nach Zeitablauf: erst Leben, dann Schaden", ref f);
            }
            return f;
        }

        static int TestRecovery()
        {
            Console.WriteLine("Rettung nach Rauswurf:");
            int f = 0;
            var s = Fresh();
            var P = s.p[0];
            s.p[1].x = -300;
            P.x = 1000; P.y = 150; P.vx = 6; P.vy = 2; P.onGround = false; P.state = St.Air; P.airJumps = Tuning.AirJumps; P.facing = -1;
            var ev = new List<GameEvent>();
            bool saved = false;
            for (int t = 0; t < 400; t++)
            {
                int h = Btn.Left;
                int pr = 0;
                if (t == 1 || t == 22 || t == 44) { h |= Btn.Jump; pr = Btn.Jump; }
                if (t == 64) { h |= Btn.Dodge | Btn.Up; pr = Btn.Dodge; }
                if (t == 84) { h |= Btn.Heavy | Btn.Up; pr = Btn.Heavy; }
                if (P.state == St.Wall && t % 6 == 0) { h |= Btn.Jump; pr = Btn.Jump; }
                Sim.Step(s, new[] { h, 0 }, new[] { pr, 0 }, ev, false);
                if (P.onGround && P.plat >= 0) { saved = true; break; }
                if (P.state == St.KO) break;
            }
            Check(saved, "Aus x=1000 mit 3 Sprüngen + Dodge + Aufschwung zurück auf die Bühne (x=" + (int)P.x + ")", ref f);
            // Plattformen mit einem Sprung erreichbar
            s = Fresh();
            s.p[0].x = -350;
            for (int t = 0; t < 70; t++) Sim.Step(s, new[] { Btn.Jump, 0 }, new[] { t == 0 ? Btn.Jump : 0, 0 }, ev, false);
            Check(s.p[0].onGround && s.p[0].plat == 1, "Seitliche Plattform mit einem Sprung erreichbar", ref f);
            return f;
        }

        static int TestOnline(bool forceRelay)
        {
            Console.WriteLine("Online über das Internet (" + (forceRelay ? "Relay erzwungen" : "automatisch") + "):");
            int f = 0;
            OnlineLink.ForceRelay = forceRelay;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Func<double> now = () => sw.Elapsed.TotalSeconds;
            string code = OnlineLink.NewCode();
            var host = new HostSession(); host.Start(0);
            var lh = new OnlineLink(); lh.Start(host.Sock, true, code);
            var cl = new ClientSession(); cl.Open();
            var lc = new OnlineLink(); lc.Start(cl.Sock, false, code);
            double tConn = -1;
            while (now() < 25 && cl.Status != ClientSession.Connected)
            {
                lh.Tick(now()); lc.Tick(now());
                host.Poll(now()); cl.Poll(now());
                if (lc.Decided != null && cl.Status == ClientSession.Idle) cl.ConnectEndpoint(lc.Decided, now());
                if (lc.Error != null || lh.Error != null || cl.Status == ClientSession.Failed) break;
                Thread.Sleep(10);
            }
            if (cl.Status == ClientSession.Connected) tConn = now();
            Console.WriteLine("    Vermittlung: " + lh.RelayName + " | STUN Host " + host.Sock.StunResult + " | STUN Client " + cl.Sock.StunResult);
            Check(cl.Status == ClientSession.Connected, "Verbunden per Raum-Code " + code + " (" + lc.Mode + ") nach " + tConn.ToString("0.0") + " s " + (lc.Error ?? lh.Error ?? cl.Error ?? ""), ref f);
            if (cl.Status != ClientSession.Connected) { host.Close(); cl.Close(); return f; }
            Check(forceRelay ? host.OverRelay : true, "Verbindungsart Host-Seite: " + (host.OverRelay ? "Relay" : "direkt"), ref f);

            host.ResetMatch(); host.InMatch = true;
            var hs = GameState.NewMatch(0, 1, 3, 0); hs.phase = GameState.Fight; hs.phaseTimer = 0;
            var ev = new List<GameEvent>();
            int snaps = 0;
            double t0 = now();
            for (int t = 0; t < 240; t++)
            {
                lh.Tick(now()); lc.Tick(now());
                host.Poll(now()); cl.Poll(now());
                if (cl.NewSnapshot) snaps++;
                cl.SendInput(t < 40 ? Btn.Left : 0, t == 0 ? Btn.Left : 0);
                int h1, p1; host.TakeInput(out h1, out p1);
                ev.Clear();
                Sim.Step(hs, new[] { 0, h1 }, new[] { 0, p1 }, ev, false);
                host.SendSnapshot(hs, ev);
                Thread.Sleep(16);
            }
            double secs = now() - t0;
            Check(hs.p[1].x < 200, "Eingaben kommen beim Host an (Client-x=" + (int)hs.p[1].x + ")", ref f);
            Check(snaps > 40, "Snapshots beim Client: " + snaps + " in " + secs.ToString("0.0") + " s (" + (snaps / secs).ToString("0") + "/s), Ping " + cl.Ping + " ms", ref f);
            host.Close(); cl.Close();
            OnlineLink.ForceRelay = false;
            return f;
        }

        static int TestStages()
        {
            Console.WriteLine("Arenen:");
            int fails = 0;
            for (int st = 0; st < StageDef.Count; st++)
            {
                var s = GameState.NewMatch(0, 3, 3, 0, st);
                var ms = new System.IO.MemoryStream();
                s.Write(new System.IO.BinaryWriter(ms));
                ms.Position = 0;
                var back = GameState.Read(new System.IO.BinaryReader(ms));
                Check(back.stage == st, StageDef.Names[st] + " wird im Snapshot übertragen", ref fails);
            }
            Check(GameState.NewMatch(0, 1, 3, 0, 99).stage == StageDef.Graveyard, "Ungültige Arena fällt auf den Mondfriedhof zurück", ref fails);
            return fails;
        }

        static void Shot(Renderer r, GameState s, string name, string[] names)
        {
            r.Stage = s.stage;
            using (var bmp = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                r.DrawBackground(g, 1280, 720, 0, 0);
                r.DrawWorld(g, s, false);
                r.DrawHud(g, s, names);
                bmp.Save(System.IO.Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
        }

        static int Screenshots()
        {
            var r = new Renderer();
            var s = Fresh();
            var names = new[] { "Spieler 1", "Spieler 2" };
            var ev = new List<GameEvent>();
            s.p[0].x = -120; s.p[1].x = 120;
            for (int i = 0; i < 30; i++) { Sim.Step(s, new[] { 0, 0 }, new[] { 0, 0 }, ev, false); r.Tick(); r.UpdateCamera(s); }
            r.SnapCamera(s);
            Shot(r, s, "shot_idle", names);
            // P0 S-Heavy mitten im Schwung, P1 blockt
            for (int t = 0; t < 22; t++)
            {
                ev.Clear();
                Sim.Step(s, new[] { t < 2 ? Btn.Heavy | Btn.Right : 0, Btn.Shield | Btn.Left }, new[] { t == 0 ? Btn.Heavy : 0, t == 0 ? Btn.Shield | Btn.Left : 0 }, ev, false);
                foreach (var e in ev) r.OnEvent(e, s);
                r.Tick(); r.UpdateCamera(s);
            }
            Shot(r, s, "shot_attack", names);
            // Luftkampf
            s = Fresh();
            s.p[0].x = -60; s.p[1].x = 60; s.p[1].damage = 45; s.p[0].damage = 112;
            for (int t = 0; t < 40; t++)
            {
                ev.Clear();
                int h0 = t < 3 ? Btn.Jump : (t == 14 ? Btn.Light | Btn.Right : (t > 14 && t < 17 ? Btn.Right : 0));
                int h1 = t < 3 ? Btn.Jump : (t == 10 ? Btn.Jump : 0);
                Sim.Step(s, new[] { h0, h1 }, new[] { (t == 0 || t == 14) ? h0 : 0, (t == 0 || t == 10) ? h1 : 0 }, ev, false);
                foreach (var e in ev) r.OnEvent(e, s);
                r.Tick(); r.UpdateCamera(s);
            }
            Shot(r, s, "shot_air", names);
            s = Fresh();
            s.p[0].x = -40; s.p[1].x = 40; s.p[1].facing = -1;
            for (int t = 0; t < 16; t++)
            {
                ev.Clear();
                Sim.Step(s, new[] { t == 0 ? Btn.Grab : 0, 0 }, new[] { t == 0 ? Btn.Grab : 0, 0 }, ev, false);
                foreach (var e in ev) r.OnEvent(e, s);
                r.Tick(); r.UpdateCamera(s);
            }
            Shot(r, s, "shot_grab", names);
            // Brokk gegen Lyra: Lyra schießt, Brokk holt mit Rüstung aus
            s = GameState.NewMatch(2, 3, 3, 0); s.phase = GameState.Fight; s.phaseTimer = 0;
            s.p[0].x = -160; s.p[1].x = 200; s.p[1].facing = -1;
            r.ClearFx();
            for (int t = 0; t < 30; t++)
            {
                ev.Clear();
                int h0 = t >= 6 ? Btn.Heavy | Btn.Right : 0, h1 = t >= 10 ? Btn.Heavy | Btn.Left : 0;
                Sim.Step(s, new[] { h0, h1 }, new[] { t == 6 ? Btn.Heavy : 0, t == 10 ? Btn.Heavy : 0 }, ev, false);
                foreach (var e in ev) r.OnEvent(e, s);
                r.Tick(); r.UpdateCamera(s);
            }
            Shot(r, s, "shot_new1", new[] { "Brokk", "Lyra" });
            for (int st = 1; st < StageDef.Count; st++) { s.stage = st; Shot(r, s, "shot_stage" + st, new[] { "Brokk", "Lyra" }); }
            s.stage = 0;
            for (int t = 0; t < 22; t++)
            {
                ev.Clear();
                Sim.Step(s, new[] { 0, 0 }, new[] { 0, 0 }, ev, false);
                foreach (var e in ev) r.OnEvent(e, s);
                r.Tick(); r.UpdateCamera(s);
            }
            Shot(r, s, "shot_new2", new[] { "Brokk", "Lyra" });
            // Spiegelduelle: Spieler 2 bekommt die Farbvariante
            for (int mc = 0; mc < CharDef.Count; mc++)
            {
                s = GameState.NewMatch(mc, mc, 3, 0, mc % StageDef.Count); s.phase = GameState.Fight; s.phaseTimer = 0;
                s.p[0].x = -90; s.p[1].x = 90; s.p[1].facing = -1;
                r.ClearFx();
                for (int t = 0; t < 20; t++) { Sim.Step(s, new[] { 0, 0 }, new[] { 0, 0 }, null, false); r.Tick(); r.UpdateCamera(s); }
                r.SnapCamera(s);
                Shot(r, s, "shot_mirror" + mc, new[] { "Spieler 1", "Spieler 2" });
            }
            // Ultimate beim Ausholen (Zeitstopp) und kurz nach dem Einschlag
            foreach (int uc in new[] { 0, 2, 3 })
            {
                s = GameState.NewMatch(uc, 1, 3, 0, uc == 0 ? 1 : uc == 2 ? 2 : 3); s.phase = GameState.Fight; s.phaseTimer = 0;
                s.p[0].x = -150; s.p[1].x = uc == 3 ? 350 : 60; s.p[1].facing = -1;
                s.p[0].ult = Tuning.UltMax; s.p[1].ult = 64;
                r.ClearFx();
                int ultFrames = Attacks.SetOf(uc)[Attacks.Ult].Startup;
                for (int t = 0; t < ultFrames + 4; t++)
                {
                    ev.Clear();
                    Sim.Step(s, new[] { t == 0 ? Btn.Ult : 0, 0 }, new[] { t == 0 ? Btn.Ult : 0, 0 }, ev, false);
                    foreach (var e in ev) r.OnEvent(e, s);
                    r.Tick(); r.UpdateCamera(s);
                    if (t == ultFrames - 8) Shot(r, s, "shot_ult" + uc + "_wind", names);
                }
                Shot(r, s, "shot_ult" + uc + "_strike", names);
            }
            var game = new Game();
            for (int i = 0; i < 5; i++) game.Tick();
            using (var bmp = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, "shot_menu.png"), ImageFormat.Png); }
            foreach (var ms in new[] { Scr.Multiplayer, Scr.Extras, Scr.Settings })
            {
                game.DebugScreen(ms);
                for (int i = 0; i < 3; i++) game.Tick();
                using (var bmp = new Bitmap(1280, 720))
                using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, "shot_" + ms.ToString().ToLower() + ".png"), ImageFormat.Png); }
            }
            Settings.P1Char = 2; Settings.P2Char = 3;
            game.DebugScreen(Scr.Local);
            for (int i = 0; i < 3; i++) game.Tick();
            using (var bmp = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, "shot_select.png"), ImageFormat.Png); }
            game.DebugScreen(Scr.Combos);
            for (int i = 0; i < 3; i++) game.Tick();
            using (var bmp = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, "shot_combos.png"), ImageFormat.Png); }
            Settings.P1Char = 0; Settings.P2Char = 2; Settings.Opponent = 3;
            game.DebugComboView();
            for (int i = 0; i < 3; i++) game.Tick();
            using (var bmp = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, "shot_comboview.png"), ImageFormat.Png); }
            // Turnier: Setup, Einflug, laufende Animation, Siegerehrung
            Action<string> shotGame = name =>
            {
                using (var bmp = new Bitmap(1280, 720))
                using (var g = Graphics.FromImage(bmp)) { game.Render(g, 1280, 720); bmp.Save(System.IO.Path.Combine(outDir, name + ".png"), ImageFormat.Png); }
            };
            game.DebugScreen(Scr.TourneySetup);
            for (int i = 0; i < 3; i++) game.Tick();
            shotGame("shot_tour_setup");
            game.DebugTournament(12, true, 40);
            shotGame("shot_tour_intro");
            game.DebugFrames(420);
            shotGame("shot_tour_mid");
            game.DebugFrames(14);
            shotGame("shot_tour_anim");
            game.DebugTournament(2, false, 60);
            string champ = game.DebugPlayTourMatch(1);
            int sf = 0;
            Check(champ == "Spieler 1", "Turnier im Spiel: Match gespielt, Sieger \"" + champ + "\" in den Baum übernommen", ref sf);
            string rerr = game.DebugReplayCheck(System.IO.Path.Combine(outDir, "test_replay.ssr"));
            Check(rerr == "", "Wiederholung: CPU-Match im Spiel aufgenommen, Byte für Byte nachsimuliert (auch nach Speichern/Laden), abgespielt und zurück zum Ergebnis" + (rerr == "" ? "" : " – " + rerr), ref sf);
            game.DebugTournament(6, false, 160);
            shotGame("shot_tour_human");
            game.DebugFinishTournament();
            game.DebugFrames(16);
            shotGame("shot_tour_final_anim");
            game.DebugFrames(140);
            shotGame("shot_tour_champion");
            // Ergebnisbildschirm mit Wiederholungs-Knöpfen und Wiedergabe-Ansicht
            game.DebugReplayCheck(System.IO.Path.Combine(outDir, "test_replay.ssr"));
            game.DebugFrames(2);
            shotGame("shot_results_replay");
            game.DebugWatchLastReplay();
            game.DebugFrames(500);
            shotGame("shot_replay");
            game.DebugScreen(Scr.Replays);
            game.DebugFrames(3);
            shotGame("shot_replay_list");
            return sf;
        }

        static int TestPolish()
        {
            Console.WriteLine("Eingabepuffer, CPU, Farbvarianten, Wiederholungen:");
            int f = 0;
            // Eingabepuffer verfällt: Druck mitten im langen Hitstun löst danach keinen Angriff aus
            {
                var s = Duel(0, 1, 80);
                s.p[0].state = St.Hitstun; s.p[0].hitstun = 30;
                bool attacked = false;
                Play(s, 50, t => { if (s.p[0].state == St.Attack) attacked = true; return new[] { t == 5 ? Btn.Light : 0, 0 }; });
                Check(!attacked, "Gedrückt im Hitstun (25 Frames vor Ende): kein verspäteter Angriff", ref f);
                s = Duel(0, 1, 80);
                s.p[0].state = St.Hitstun; s.p[0].hitstun = 30;
                attacked = false;
                Play(s, 50, t => { if (s.p[0].state == St.Attack) attacked = true; return new[] { t == 27 ? Btn.Light : 0, 0 }; });
                Check(attacked, "Gedrückt kurz vor Ende des Hitstuns: Angriff kommt (Puffer " + Tuning.InputBufferFrames + " Frames)", ref f);
            }
            // CPU: gegen die Trainingspuppe alle Leben, ohne selbst abzustürzen; schwer schlägt leicht meistens
            {
                int kos = 0, self = 0;
                for (int c = 0; c < CharDef.Count; c++)
                {
                    var s = GameState.NewMatch(c, (c + 1) % CharDef.Count, 3, 3, 0); s.phase = GameState.Fight; s.phaseTimer = 0;
                    var b0 = new Bot(3, 11 + c); var b1 = new Bot(0, 1);
                    var ev = new List<GameEvent>();
                    for (int t = 0; t < 60 * 60 * 3 && s.phase != GameState.Over; t++)
                    {
                        int h0, p0, h1, p1; b0.Think(s, 0, out h0, out p0); b1.Think(s, 1, out h1, out p1);
                        ev.Clear(); Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, ev, false);
                        foreach (var e in ev) if (e.Type == Ev.KO) { if (e.Player == 1) kos++; else self++; }
                    }
                }
                Check(kos == 3 * CharDef.Count && self == 0, "CPU schwer gegen Puppe: " + kos + "/" + 3 * CharDef.Count + " K.O.s, eigene Abstürze " + self, ref f);
                int wins = 0;
                var rng = new Random(3);
                for (int seed = 1; seed <= 12; seed++)
                {
                    var s = GameState.NewMatch(seed % 4, seed % 4, 3, 3, 0); // Spiegelduell: nur die Stufe entscheidet
                    var b0 = new Bot(3, seed * 7); var b1 = new Bot(1, seed * 13 + 1);
                    for (int t = 0; t < 60 * 60 * 4 && s.phase != GameState.Over; t++)
                    {
                        int h0, p0, h1, p1; b0.Think(s, 0, out h0, out p0); b1.Think(s, 1, out h1, out p1);
                        Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, null, false);
                    }
                    if (Tournament.WinnerOf(s, rng) == 0) wins++;
                }
                Check(wins >= 8, "CPU schwer gegen leicht (gleicher Kämpfer): " + wins + "/12 Siege", ref f);
            }
            // Farbvariante nur im Spiegelduell, und nur für Spieler 2
            {
                var a = GameState.NewMatch(2, 2, 3, 0); var b = GameState.NewMatch(0, 1, 3, 0);
                Check(a.p[0].skin == 0 && a.p[1].skin == 1 && b.p[1].skin == 0 && CharDef.Look(2, 1).Glow != CharDef.Look(2, 0).Glow,
                    "Spiegelduell: Spieler 2 bekommt die Variante \"" + CharDef.Look(2, 1).Name + "\"", ref f);
            }
            // Wiederholung: gleiche Eingaben -> exakt gleicher Ablauf; Datei aus anderer Version wird abgelehnt
            {
                var s = GameState.NewMatch(3, 2, 3, 2, 1); s.phase = GameState.Fight; s.phaseTimer = 0;
                var rp = Replay.Begin(s, "A", "B");
                var b0 = new Bot(3, 5); var b1 = new Bot(2, 6);
                for (int t = 0; t < 3000; t++)
                {
                    int h0, p0, h1, p1; b0.Think(s, 0, out h0, out p0); b1.Think(s, 1, out h1, out p1);
                    rp.Add(h0, p0, h1, p1);
                    Sim.Step(s, new[] { h0, h1 }, new[] { p0, p1 }, null, false);
                }
                var r2 = rp.StartState();
                for (int t = 0; t < rp.Ticks; t++)
                {
                    int h0, p0, h1, p1; rp.Get(t, out h0, out p0, out h1, out p1);
                    Sim.Step(r2, new[] { h0, h1 }, new[] { p0, p1 }, null, false);
                }
                var m1 = new System.IO.MemoryStream(); s.Write(new System.IO.BinaryWriter(m1));
                var m2 = new System.IO.MemoryStream(); r2.Write(new System.IO.BinaryWriter(m2));
                bool same = Convert.ToBase64String(m1.ToArray()) == Convert.ToBase64String(m2.ToArray());
                string path = System.IO.Path.Combine(outDir, "test_replay2.ssr");
                rp.Save(path);
                long size = new System.IO.FileInfo(path).Length;
                // Fingerabdruck verfälschen (Byte 4..7 nach dem Entpacken) -> muss abgelehnt werden
                byte[] raw;
                using (var fs = System.IO.File.OpenRead(path)) using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress)) using (var ms = new System.IO.MemoryStream()) { gz.CopyTo(ms); raw = ms.ToArray(); }
                raw[4] ^= 0xFF;
                using (var fs = System.IO.File.Create(path)) using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Compress)) gz.Write(raw, 0, raw.Length);
                bool rejected = false;
                try { Replay.Load(path); } catch (System.IO.InvalidDataException) { rejected = true; }
                Check(same && rejected && size < 20000, "Wiederholung: 3000 Frames identisch nachsimuliert, Datei " + size / 1024.0 + " KB, fremde Version abgelehnt", ref f);
            }
            return f;
        }
    }
}
