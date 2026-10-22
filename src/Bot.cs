using System;

namespace Sensensturm
{
    // Einfacher CPU-Gegner für "Lokal spielen" (zum Üben ohne zweiten Spieler).
    public class Bot
    {
        readonly Random rng = new Random();
        readonly int level; // 1..3
        int prev, hold, holdBits, think, jumpWait, frameToggle;

        public Bot(int level) { this.level = Math.Max(0, Math.Min(3, level)); } // 0 = Trainingspuppe

        public void Think(GameState s, int me, out int held, out int pressed)
        {
            PlayerState P = s.p[me], O = s.p[1 - me];
            int h = 0;
            if (level == 0)
            {
                // Puppe: steht still, kehrt nur zur Bühne zurück
                bool off = P.x < Arena.Main.L || P.x > Arena.Main.R || P.y > 20;
                if (off && P.state == St.Air && P.vy > 0) h = (P.x < 0 ? Btn.Right : Btn.Left) | (P.airJumps > 0 && frameToggle++ % 12 == 0 ? Btn.Jump : 0);
                else if (off) h = P.x < 0 ? Btn.Right : Btn.Left;
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
                h = P.x < 0 ? Btn.Right : Btn.Left;
                if (P.state == St.Wall) { if (rng.Next(6) == 0) h |= Btn.Jump; }
                else if (P.vy > 0 && P.y > -80)
                {
                    if (P.airJumps > 0 && jumpWait <= 0) { h |= Btn.Jump; jumpWait = 14; }
                    else if (!P.recoveryUsed && P.airJumps == 0 && P.y > -20) { h |= Btn.Up | Btn.Heavy; }
                }
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

            if (think > 0) { think--; Finish(h | (prev & (Btn.Left | Btn.Right)), out held, out pressed); return; }
            think = level == 3 ? rng.Next(2, 6) : level == 2 ? rng.Next(4, 10) : rng.Next(8, 18);

            float adx = Math.Abs(dx);
            bool canAct = P.state == St.Idle || P.state == St.Run || P.state == St.Air || P.state == St.Dash || (P.state == St.Attack && P.atkHit);
            if (!canAct) { Finish(h, out held, out pressed); return; }

            if (adx > 150)
            {
                h |= toward;
                if (adx > 450 && P.onGround && rng.Next(4) == 0) h |= Btn.Dodge;
            }
            if (dy < -110 && P.onGround && rng.Next(3) == 0) h |= Btn.Jump;
            if (dy < -60 && !P.onGround && P.vy > 0 && P.airJumps > 0 && rng.Next(4) == 0) h |= Btn.Jump;

            if (adx < 160 && Math.Abs(dy) < 170 && O.invuln == 0)
            {
                int roll = rng.Next(10);
                if ((O.state == St.Shield || roll == 0) && Math.Abs(dy) < 60 && adx < 120) h |= toward | Btn.Grab;
                else if (dy < -80) h |= Btn.Up | (roll < 7 ? Btn.Light : Btn.Heavy);
                else if (dy > 70 && !P.onGround) h |= Btn.Down | Btn.Light;
                else if (roll < 4) h |= toward | Btn.Light;
                else if (roll < 6) h |= Btn.Light;
                else if (roll < 7) h |= Btn.Down | Btn.Light;
                else if (roll < 9) h |= toward | Btn.Heavy;
                else h |= Btn.Heavy;
                if ((h & Btn.Heavy) != 0 && P.onGround) { holdBits = Btn.Heavy; hold = rng.Next(2, level == 3 ? 20 : 8); }
            }
            Finish(h, out held, out pressed);
        }

        void Finish(int h, out int held, out int pressed)
        {
            held = h;
            pressed = h & ~prev;
            prev = h;
        }
    }
}
