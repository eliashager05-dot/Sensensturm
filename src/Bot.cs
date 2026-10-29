using System;

namespace Sensensturm
{
    // Einfacher CPU-Gegner für "Lokal spielen" (zum Üben ohne zweiten Spieler).
    public class Bot
    {
        readonly Random rng;
        readonly int level; // 1..3
        int prev, hold, holdBits, think, jumpWait, frameToggle, dummyTime;
        // nur Trainingspuppe: 0 stehen, 1 regelmäßig springen, 2 Schild halten
        public int DummyMode;

        static readonly Random seeder = new Random(); // verschiedene Seeds auch für zwei gleichzeitig erzeugte Bots
        public Bot(int level) : this(level, seeder.Next()) { }
        // seed: fester Zufall für reproduzierbare Tests
        public Bot(int level, int seed) { this.level = Math.Max(0, Math.Min(3, level)); rng = new Random(seed); } // 0 = Trainingspuppe

        public void Think(GameState s, int me, out int held, out int pressed)
        {
            PlayerState P = s.p[me], O = s.p[1 - me];
            curP = P; curO = O;
            int h = 0;
            if (level == 0)
            {
                // Puppe: kehrt zur Bühne zurück, sonst je nach Modus stehen, springen (volle Höhe, etwa jede Sekunde) oder Schild halten
                bool off = P.x < Arena.Main.L || P.x > Arena.Main.R || P.y > 20;
                if (off && P.state == St.Air && P.vy > 0) h = (P.x < 0 ? Btn.Right : Btn.Left) | (P.airJumps > 0 && frameToggle++ % 12 == 0 ? Btn.Jump : 0);
                else if (off) h = P.x < 0 ? Btn.Right : Btn.Left;
                else if (DummyMode == 1) h = dummyTime++ % 64 < 12 ? Btn.Jump : 0;
                else if (DummyMode == 2) h = Btn.Shield;
                Finish(h, out held, out pressed);
                return;
            }
            float dx = O.x - P.x, dy = O.y - P.y;
            int toward = dx >= 0 ? Btn.Right : Btn.Left;
            int away = dx >= 0 ? Btn.Left : Btn.Right;
            Box M = Arena.Main;
            bool offstage = P.x < M.L - 5 || P.x > M.R + 5 || P.y > 30;

            if (hold > 0) { hold--; h |= holdBits; }

            if (P.state == St.Respawn || P.state == St.KO) { Finish(0, out held, out pressed); return; }

            if (offstage)
            {
                // zurück zur Bühne
                int toStage = P.x < 0 ? Btn.Right : Btn.Left, outward = P.x < 0 ? Btn.Left : Btn.Right;
                if (P.state == St.Wall)
                {
                    // an der Wand hochklettern: Sprung immer wieder neu drücken, ohne Wandsprünge den Aufschwung nutzen
                    if (P.wallJumps > 0) h = toStage | ((prev & Btn.Jump) == 0 ? Btn.Jump : 0);
                    else h = !P.recoveryUsed ? Btn.Up | Btn.Heavy : toStage;
                    hold = 0;
                    Finish(h, out held, out pressed);
                    return;
                }
                // unter der Unterkante der Bühne: erst nach außen, sonst stößt man sich den Kopf.
                // Darüber zur Bühne steuern – an der Wand gibt es Wandsprünge und der Aufschwung lädt sich wieder auf.
                bool belowLip = P.y > M.B - 10 && P.x > M.L - 60 && P.x < M.R + 60;
                h = belowLip ? outward : toStage;
                bool clear = !(P.x > M.L - 25 && P.x < M.R + 25 && P.y > M.T + 15); // nicht direkt unter der Bühne
                if (P.vy > -1.5f && jumpWait <= 0 && (P.y > -60 || belowLip))
                {
                    if (P.airJumps > 0) { h |= Btn.Jump; holdBits = Btn.Jump; hold = 10; jumpWait = level == 1 ? 22 : 13; }
                    else if (!P.recoveryUsed && clear && P.y > -120) h = Btn.Up | Btn.Heavy | (belowLip ? outward : toStage);
                    else if (!P.airDodgeUsed && P.dodgeCd <= 0 && clear) h = Btn.Dodge | Btn.Up | toStage;
                }
                if (hold > 0 && (holdBits & Btn.Jump) != 0) h |= Btn.Jump; // Sprung halten = volle Höhe
                if (jumpWait > 0) jumpWait--;
                Finish(h, out held, out pressed);
                return;
            }
            if (jumpWait > 0) jumpWait--;

            float react = level == 1 ? 0.04f : level == 2 ? 0.12f : 0.25f;
            // Verteidigung gegen anlaufende Angriffe
            if (O.state == St.Attack && Math.Abs(dx) < 220 && Math.Abs(dy) < 160)
            {
                AttackDef d = Attacks.Of(O);
                if (O.atkFrame < d.Startup && O.atkFrame >= d.Startup - 6 && rng.NextDouble() < react)
                {
                    int r = rng.Next(3);
                    if (r == 0) { holdBits = Btn.Shield; hold = 14; h |= Btn.Shield; }
                    else if (r == 1) h |= Btn.Dodge | away;
                    else h |= Btn.Dodge;
                    Finish(h, out held, out pressed);
                    return;
                }
            }

            // Combo: nach einem Treffer sofort nachsetzen statt die Denkpause abzuwarten (mittel: manchmal, schwer: immer)
            if (P.state == St.Attack && P.atkHit && O.state == St.Hitstun && (level == 3 || (level == 2 && rng.Next(2) == 0))) think = 0;
            if (think > 0) { think--; Finish(h | (prev & (Btn.Left | Btn.Right)), out held, out pressed); return; }
            think = level == 3 ? rng.Next(2, 6) : level == 2 ? rng.Next(4, 10) : rng.Next(8, 18);

            float adx = Math.Abs(dx);
            bool canAct = P.state == St.Idle || P.state == St.Run || P.state == St.Air || P.state == St.Dash || (P.state == St.Attack && P.atkHit);
            if (!canAct) { Finish(h, out held, out pressed); return; }

            // Ultimate, sobald die Leiste voll ist und der Gegner in Reichweite
            if (P.ult >= Tuning.UltMax && O.invuln == 0 && O.state != St.Respawn)
            {
                int w = CharDef.All[P.charId].Weapon;
                float range = w == Weapon.Bow ? 700 : w == Weapon.Hammer ? 300 : w == Weapon.Staff ? 290 : w == Weapon.Daggers ? 330 : 260;
                if (adx < range && Math.Abs(dy) < (w == Weapon.Bow ? 70 : 130) && rng.Next(level == 3 ? 2 : 5) == 0)
                {
                    Finish(h | toward | Btn.Ult, out held, out pressed);
                    return;
                }
            }

            // ab diesem Schaden des Gegners wird auf K.O. gespielt
            float killPct = level == 3 ? 95 : level == 2 ? 125 : 170;
            bool finish = O.damage >= killPct;

            // Kante bewachen: Gegner ist neben der Bühne -> nicht hinterherlaufen, an der Kante warten und wieder rausschlagen
            bool oOff = O.x < M.L - 10 || O.x > M.R + 10 || O.y > 25;
            if (oOff && O.state != St.Respawn && O.state != St.KO)
            {
                float edge = O.x < 0 ? M.L + 45 : M.R - 45;
                bool reach = adx < 200 && dy > -230 && dy < 140 && O.invuln == 0;
                if (reach && rng.NextDouble() < (level == 3 ? 0.8 : level == 2 ? 0.5 : 0.25))
                {
                    if (dy < -70) h |= Btn.Up | (finish ? Btn.Heavy : Btn.Light);
                    else if (!P.onGround) h |= toward | Btn.Light;
                    else h |= toward | (finish || rng.Next(2) == 0 ? Btn.Heavy : Btn.Light);
                    if ((h & Btn.Heavy) != 0 && P.onGround) { holdBits = Btn.Heavy; hold = rng.Next(2, 8); }
                }
                else if (level == 3 && P.onGround && P.airJumps >= 2 && adx < 300 && O.y > -260 && O.y < 60 && rng.Next(6) == 0)
                {
                    // schwere CPU springt hinterher und fängt in der Luft ab (der Rückweg übernimmt die Rettung oben)
                    h |= toward | Btn.Jump;
                    holdBits = toward | Btn.Light; hold = 10;
                }
                else if (Math.Abs(P.x - edge) > 25) h |= P.x < edge ? Btn.Right : Btn.Left;
                else if ((O.x > P.x) != (P.facing > 0)) h |= toward;
                Finish(h, out held, out pressed);
                return;
            }

            if (adx > 150)
            {
                h |= toward;
                if (adx > 450 && P.onGround && rng.Next(4) == 0) h |= Btn.Dodge;
            }
            // Gegner unter einem: durch die Schwebeplattform fallen (Unten halten)
            if (P.onGround && P.plat > 0 && dy > 60) { holdBits = Btn.Down; hold = 10; h = (h & ~(Btn.Left | Btn.Right)) | Btn.Down; Finish(h, out held, out pressed); return; }
            // Gegner höher (z. B. auf einer Plattform): Sprungtaste halten, sonst wird es durch die variable Sprunghöhe nur ein Hüpfer
            if (dy < -110 && P.onGround && rng.Next(3) == 0) { h |= Btn.Jump; holdBits = Btn.Jump | (adx > 40 ? toward : 0); hold = 16; }
            if (dy < -60 && !P.onGround && P.vy > -2 && P.airJumps > 0 && rng.Next(level == 3 ? 2 : 4) == 0) { h |= Btn.Jump; holdBits = Btn.Jump | (adx > 40 ? toward : 0); hold = 14; }

            if (adx < 160 && Math.Abs(dy) < 170 && O.invuln == 0)
            {
                int roll = rng.Next(10);
                if (finish && O.state != St.Shield && roll < (level == 3 ? 7 : level == 2 ? 5 : 3))
                {
                    // Abschluss: Angriffe, die weit schleudern (am Boden kurz aufladen)
                    if (dy < -70) h |= Btn.Up | Btn.Heavy;
                    else if (P.onGround) h |= (adx > 50 ? toward : 0) | Btn.Heavy;
                    else h |= toward | Btn.Heavy;
                    if (P.onGround) { holdBits = Btn.Heavy; hold = O.state == St.Hitstun ? 2 : rng.Next(4, level == 3 ? 16 : 10); }
                    Finish(h, out held, out pressed);
                    return;
                }
                if ((O.state == St.Shield || roll == 0) && Math.Abs(dy) < 60 && adx < 120) h |= toward | Btn.Grab;
                else if (dy < -80) h |= Btn.Up | (roll < 7 ? Btn.Light : Btn.Heavy);
                else if (dy > 70 && !P.onGround) h |= Btn.Down | Btn.Light;
                else if (roll < 4) h |= toward | Btn.Light;
                else if (roll < 6) h |= Btn.Light;
                else if (roll < 7) h |= Btn.Down | Btn.Light;
                else if (roll < 9) h |= toward | Btn.Heavy;
                else if (P.onGround || dy > 60) h |= Btn.Heavy; // in der Luft wäre das der Sturzangriff, nur sinnvoll über dem Gegner
                else h |= toward | Btn.Heavy;
                if ((h & Btn.Heavy) != 0 && P.onGround) { holdBits = Btn.Heavy; hold = rng.Next(2, level == 3 ? 20 : 8); }
            }
            Finish(h, out held, out pressed);
        }

        // Richtung eines gerade gedrückten Angriffs so lange halten, wie der Eingabepuffer läuft:
        // sonst wird z. B. aus "Oben + Schwer" in der Luft der Sturzangriff, falls der Angriff erst später startet
        int lastDir, dirFrames;
        const int Dirs = Btn.Left | Btn.Right | Btn.Up | Btn.Down;

        PlayerState curP, curO;

        void Finish(int h, out int held, out int pressed)
        {
            // Links + Rechts zugleich (gehaltene alte + neue Richtung) heben sich auf -> die neuere gewinnt
            const int LR = Btn.Left | Btn.Right;
            if ((h & LR) == LR) h &= ~(holdBits & LR);
            if ((h & LR) == LR) h &= ~(lastDir & LR);
            // Schwer ohne Richtung in der Luft ist der Sturzangriff: nur über der Bühne und mit dem Gegner darunter
            if (curP != null && !curP.onGround && (h & Btn.Heavy) != 0 && (h & (Btn.Up | LR)) == 0)
            {
                bool overStage = curP.x > Arena.Main.L + 30 && curP.x < Arena.Main.R - 30;
                if (!overStage || curO.y < curP.y + 40) h |= curO.x >= curP.x ? Btn.Right : Btn.Left;
            }
            if (((h & ~prev) & (Btn.Light | Btn.Heavy | Btn.Grab | Btn.Ult)) != 0) { lastDir = h & Dirs; dirFrames = Tuning.InputBufferFrames; }
            else if (dirFrames > 0)
            {
                dirFrames--;
                if ((h & (Btn.Up | Btn.Down)) == 0) h |= lastDir & (Btn.Up | Btn.Down);
                if ((h & (Btn.Left | Btn.Right)) == 0) h |= lastDir & (Btn.Left | Btn.Right);
            }
            held = h;
            pressed = h & ~prev;
            prev = h;
        }
    }
}
