using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Sensensturm
{
    public struct Box
    {
        public float L, T, R, B;
        public Box(float l, float t, float r, float b) { L = l; T = t; R = r; B = b; }
        public bool Hits(Box o) { return L < o.R && R > o.L && T < o.B && B > o.T; }
        public float CX { get { return (L + R) * 0.5f; } }
        public float CY { get { return (T + B) * 0.5f; } }
    }

    public struct SoftPlat
    {
        public float L, R, Y;
        public SoftPlat(float l, float r, float y) { L = l; R = r; Y = y; }
    }

    public static class Arena
    {
        public static readonly Box Main = new Box(-640, 0, 640, 300);
        public static readonly SoftPlat[] Soft = { new SoftPlat(-480, -230, -185), new SoftPlat(230, 480, -185), new SoftPlat(-125, 125, -360) };
        public const float BlastL = -1600, BlastR = 1600, BlastT = -1250, BlastB = 950;
        public const float HalfW = 18, Height = 92;
    }

    public static class St
    {
        public const int Idle = 0, Run = 1, Dash = 2, Air = 3, Wall = 4, Attack = 5, Shield = 6, Dodge = 7, AirDodge = 8,
            Hitstun = 9, Parried = 10, Broken = 11, Landing = 12, KO = 13, Respawn = 14, Clank = 15, Grabbing = 16, Grabbed = 17;
    }

    public static class Ev
    {
        public const byte Hit = 1, Block = 2, Parry = 3, ShieldBreak = 4, KO = 5, Jump = 6, AirJump = 7, Land = 8, Dash = 9, Dodge = 10,
            Swing = 11, Clank = 12, Countdown = 13, Go = 14, ChargeFull = 15, Shockwave = 16, WallJump = 17, GameOver = 18, Respawn = 19, Bounce = 20,
            Grab = 21, Throw = 22, Perfect = 23, Shoot = 24, ProjBreak = 25;
    }

    public class GameEvent
    {
        public int Id;
        public byte Type, Player;
        public float X, Y, V;
    }

    // Kompletter Zustand eines Kämpfers. Alle Felder werden automatisch per Reflection serialisiert (Netzwerk-Snapshots).
    public class PlayerState
    {
        public int slot, charId;
        public float x, y, vx, vy;
        public int facing = 1;
        public bool onGround, groundJumpAvail;
        public int plat = -1, airTime;
        public int state, stateTime;
        public int airJumps, wallJumps;
        public bool recoveryUsed, airDodgeUsed, jumpCut, fastFall, parryOk, tumble, sprint, chargeFull, chase, grabCombo;
        public int airJumpTime = 999, dodgeCd, dashDir;
        public float dvx, dvy;
        public int atk = -1, atkFrame, charge, sinceHit, pressMark = -1;
        public bool atkHit;
        public float damage;
        public int stocks;
        public int hitstun, hitlag, invuln, blockstun, landLag, hurtFlash;
        public float shieldHp;
        public int shieldCd;
        public int prevHeld, bufJump, bufLight, bufHeavy, bufDodge, bufShield, bufGrab, downTime, dropTimer;
        public int wallDir, koTimer, combo, comboTimer, perfects, anim, parryFlash, landAnim;
        public float runPhase;
        public int statHits, statParries, statKOs, statBestCombo;
        // ein Projektil pro Spieler (Pfeil oder Bodenwelle)
        public bool projOn;
        public int projKind, projLife, projDir;
        public float projX, projY, projVx, projVy, projDmg, projKbB, projKbG, projAng;
        public float statDamage;

        public PlayerState Clone() { return (PlayerState)MemberwiseClone(); }
        public bool Alive { get { return state != St.KO; } }
        public Box Hurt { get { return new Box(x - 20, y - 94, x + 20, y); } }
    }

    public class GameState
    {
        public int tick, phase, phaseTimer, timeLeft, winner = -1, stocks;
        public int stage; // nur Optik (StageDef), wird mit dem Snapshot an den Client übertragen
        public PlayerState[] p = new PlayerState[2];

        public const int Countdown = 0, Fight = 1, Over = 2;

        public GameState Clone()
        {
            var g = (GameState)MemberwiseClone();
            g.p = new PlayerState[] { p[0].Clone(), p[1].Clone() };
            return g;
        }

        public static GameState NewMatch(int char0, int char1, int stocks, int minutes, int stage = 0)
        {
            var s = new GameState();
            s.stocks = stocks;
            s.stage = StageDef.Valid(stage);
            s.phase = Countdown;
            s.phaseTimer = 180;
            s.timeLeft = minutes > 0 ? minutes * 3600 : -1;
            for (int i = 0; i < 2; i++)
            {
                var p = new PlayerState();
                p.slot = i;
                p.charId = i == 0 ? char0 : char1;
                p.x = i == 0 ? -300 : 300;
                p.y = 0;
                p.facing = i == 0 ? 1 : -1;
                p.onGround = true;
                p.plat = 0;
                p.stocks = stocks;
                p.shieldHp = Tuning.ShieldMax;
                p.airJumps = Tuning.AirJumps;
                p.wallJumps = Tuning.WallJumps;
                p.groundJumpAvail = true;
                s.p[i] = p;
            }
            return s;
        }

        // ---------- Serialisierung ----------
        static FieldInfo[] gFields, pFields;
        static FieldInfo[] FieldsOf(Type t)
        {
            var list = new List<FieldInfo>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(bool)) list.Add(f);
            list.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return list.ToArray();
        }

        static void WriteObj(BinaryWriter w, object o, FieldInfo[] fs)
        {
            foreach (var f in fs)
            {
                object v = f.GetValue(o);
                if (v is int) w.Write((int)v);
                else if (v is float) w.Write((float)v);
                else w.Write((bool)v);
            }
        }

        static void ReadObj(BinaryReader r, object o, FieldInfo[] fs)
        {
            foreach (var f in fs)
            {
                if (f.FieldType == typeof(int)) f.SetValue(o, r.ReadInt32());
                else if (f.FieldType == typeof(float)) f.SetValue(o, r.ReadSingle());
                else f.SetValue(o, r.ReadBoolean());
            }
        }

        public void Write(BinaryWriter w)
        {
            if (gFields == null) { gFields = FieldsOf(typeof(GameState)); pFields = FieldsOf(typeof(PlayerState)); }
            WriteObj(w, this, gFields);
            WriteObj(w, p[0], pFields);
            WriteObj(w, p[1], pFields);
        }

        public static GameState Read(BinaryReader r)
        {
            if (gFields == null) { gFields = FieldsOf(typeof(GameState)); pFields = FieldsOf(typeof(PlayerState)); }
            var s = new GameState();
            ReadObj(r, s, gFields);
            s.p[0] = new PlayerState();
            s.p[1] = new PlayerState();
            ReadObj(r, s.p[0], pFields);
            ReadObj(r, s.p[1], pFields);
            return s;
        }
    }

    // Die eigentliche Spielsimulation. Läuft lokal bzw. auf dem Host autoritativ.
    // Der Client nutzt sie mit predict=true nur zur Vorhersage der Bewegung (ohne Trefferauswertung).
    public static class Sim
    {
        static List<GameEvent> evs;
        static bool perfectCancel;
        static PlayerState perfectTarget;

        // Perfekter Jump-Cancel zielt: springt so, dass der Gegner in ~10 Frames knapp über einem ist
        static void AimJump(PlayerState P)
        {
            PlayerState V = perfectTarget;
            if (V == null) return;
            const int n = 10;
            float g = Tuning.Gravity * C(V).Grav * Tuning.HitstunGravity;
            float fx = V.x + V.vx * n * 0.9f, fy = V.y + V.vy * n + 0.5f * g * n * n;
            float vy0 = (fy + 60f - P.y - 0.5f * Tuning.Gravity * C(P).Grav * n * n) / n;
            P.vy = Math.Max(-Tuning.AirJumpForce * C(P).Jump * 1.3f, Math.Min(-3f, vy0));
            int side = fx >= P.x ? 1 : -1;
            float maxV = Tuning.AirMaxSpeed * 1.2f;
            P.vx = Math.Max(-maxV, Math.Min(maxV, (fx - side * 40f - P.x) / n));
            P.facing = side;
            P.jumpCut = true;
        }
        const int ComboKeys = Btn.Light | Btn.Heavy | Btn.Jump | Btn.Dodge | Btn.Grab;

        static CharDef C(PlayerState p) { return CharDef.All[p.charId]; }

        static void Emit(byte type, int player, float x, float y, float v)
        {
            if (evs == null) return;
            evs.Add(new GameEvent { Type = type, Player = (byte)player, X = x, Y = y, V = v });
        }

        static float Approach(float v, float target, float step)
        {
            if (v < target) return Math.Min(v + step, target);
            if (v > target) return Math.Max(v - step, target);
            return v;
        }

        static void SetState(PlayerState p, int st)
        {
            p.state = st;
            p.stateTime = 0;
            if (st != St.Attack) { p.atk = -1; p.charge = 0; }
        }

        public static void Step(GameState s, int[] held, int[] pressed, List<GameEvent> events, bool predict)
        {
            evs = events;
            s.tick++;
            if (s.phase == GameState.Countdown)
            {
                if (s.phaseTimer % 60 == 0 && s.phaseTimer > 0) Emit(Ev.Countdown, 0, 0, 0, s.phaseTimer / 60);
                s.phaseTimer--;
                if (s.phaseTimer <= 0) { s.phase = GameState.Fight; s.phaseTimer = 0; Emit(Ev.Go, 0, 0, 0, 0); }
            }
            else if (s.phase == GameState.Over) s.phaseTimer++;

            bool act = s.phase == GameState.Fight;
            for (int i = 0; i < 2; i++)
                UpdatePlayer(s, s.p[i], act ? held[i] : 0, act ? pressed[i] : 0);

            Separate(s.p[0], s.p[1]);
            for (int i = 0; i < 2; i++) MoveProjectile(s.p[i]);

            if (!predict && s.phase != GameState.Countdown)
            {
                Combat(s);
                ProjectileHits(s);
                CheckBlast(s);
            }

            if (s.phase == GameState.Fight && s.timeLeft > 0)
            {
                s.timeLeft--;
                if (s.timeLeft == 0 && !predict)
                {
                    PlayerState a = s.p[0], b = s.p[1];
                    if (a.stocks != b.stocks) s.winner = a.stocks > b.stocks ? 0 : 1;
                    else if (Math.Abs(a.damage - b.damage) > 0.5f) s.winner = a.damage < b.damage ? 0 : 1;
                    else s.winner = -1;
                    s.phase = GameState.Over;
                    s.phaseTimer = 0;
                    Emit(Ev.GameOver, Math.Max(0, s.winner), 0, 0, 0);
                }
            }
            evs = null;
        }

        // ------------------------------------------------------------------ Spieler

        static void UpdatePlayer(GameState s, PlayerState P, int held, int pressed)
        {
            P.anim++;
            if (P.comboTimer > 0) P.comboTimer--; else { P.combo = 0; P.perfects = 0; }
            if (P.parryFlash > 0) P.parryFlash--;
            if (P.hurtFlash > 0) P.hurtFlash--;
            if (P.landAnim > 0) P.landAnim--;

            if (P.state == St.KO)
            {
                P.koTimer--;
                if (P.koTimer <= 0 && P.stocks > 0 && s.phase == GameState.Fight) Respawn(s, P);
                P.prevHeld = held;
                return;
            }

            int buf = Tuning.InputBufferFrames;
            if ((pressed & Btn.Jump) != 0) P.bufJump = buf;
            if ((pressed & Btn.Light) != 0) P.bufLight = buf;
            if ((pressed & Btn.Heavy) != 0) P.bufHeavy = buf;
            if ((pressed & Btn.Dodge) != 0) P.bufDodge = buf;
            if ((pressed & Btn.Shield) != 0) P.bufShield = buf;
            if ((pressed & Btn.Grab) != 0) P.bufGrab = buf;
            // Zeitpunkt des ersten Combo-Tastendrucks nach einem Treffer merken (für "perfektes" Timing).
            // Wer schon während des Trefferstopps hämmert, bekommt kein PERFEKT.
            if ((pressed & ComboKeys) != 0 && P.state == St.Attack && P.atkHit && P.pressMark < 0)
                P.pressMark = P.hitlag > 0 ? 0 : P.sinceHit;

            // Trefferstopp: alles eingefroren, Eingaben werden gepuffert
            if (P.hitlag > 0)
            {
                P.hitlag--;
                P.prevHeld = held;
                return;
            }

            if ((held & Btn.Down) != 0) P.downTime++; else P.downTime = 0;
            if (P.invuln > 0) P.invuln--;
            if (P.dodgeCd > 0) P.dodgeCd--;
            if (P.shieldCd > 0) P.shieldCd--;
            if (P.dropTimer > 0) P.dropTimer--;
            P.airJumpTime++;
            if (P.state != St.Shield && P.state != St.Broken) P.shieldHp = Math.Min(Tuning.ShieldMax, P.shieldHp + Tuning.ShieldRegen);

            int dx = ((held & Btn.Right) != 0 ? 1 : 0) - ((held & Btn.Left) != 0 ? 1 : 0);
            bool up = (held & Btn.Up) != 0, down = (held & Btn.Down) != 0;
            P.stateTime++;

            switch (P.state)
            {
                case St.Idle:
                case St.Run:
                    if (!P.onGround) { SetState(P, St.Air); break; }
                    if (P.plat > 0 && down && (P.bufJump > 0 || (P.downTime >= 7 && P.bufLight == 0 && P.bufHeavy == 0)))
                    {
                        P.bufJump = 0; P.dropTimer = 14; P.onGround = false; P.plat = -1; P.y += 2; P.vy = 2;
                        SetState(P, St.Air);
                        break;
                    }
                    if (TryGround(s, P, dx, up, down, held, false)) break;
                    GroundMove(P, dx);
                    break;

                case St.Dash:
                    {
                        float t = Math.Min(1f, P.stateTime / (float)Math.Max(1, Tuning.DashFrames));
                        P.vx = P.dashDir * (Tuning.DashSpeed + (Tuning.SprintSpeed - Tuning.DashSpeed) * t) * C(P).Speed;
                        if (!P.onGround) { SetState(P, St.Air); break; }
                        if (TryGround(s, P, dx, up, down, held, false)) break;
                        if (P.stateTime >= Tuning.DashFrames)
                        {
                            P.sprint = dx == P.dashDir;
                            SetState(P, dx != 0 ? St.Run : St.Idle);
                        }
                        break;
                    }

                case St.Air:
                    if (P.onGround) { SetState(P, St.Idle); break; }
                    AirMove(P, dx, held);
                    if ((pressed & Btn.Down) != 0 && P.vy > -3f) P.fastFall = true;
                    if (P.wallDir != 0 && dx == P.wallDir && P.vy > -3f && !down) { SetState(P, St.Wall); P.facing = -P.wallDir; P.fastFall = false; break; }
                    TryAir(s, P, dx, up, down, held, false);
                    break;

                case St.Wall:
                    if (P.onGround) { SetState(P, St.Idle); break; }
                    if (P.wallDir == 0 || dx == -P.wallDir || down) { SetState(P, St.Air); break; }
                    P.vx = P.wallDir * 0.6f;
                    P.facing = -P.wallDir;
                    P.recoveryUsed = false;
                    P.airDodgeUsed = false;
                    if (P.bufJump > 0 && P.wallJumps > 0)
                    {
                        P.bufJump = 0;
                        P.wallJumps--;
                        P.vx = -P.wallDir * Tuning.WallJumpX;
                        P.vy = -Tuning.WallJumpY * C(P).Jump;
                        P.jumpCut = false;
                        Emit(Ev.WallJump, P.slot, P.x + P.wallDir * Arena.HalfW, P.y - 40, P.wallDir);
                        SetState(P, St.Air);
                        break;
                    }
                    if (P.bufLight > 0 || P.bufHeavy > 0 || P.bufDodge > 0 || P.bufShield > 0 || P.bufGrab > 0)
                    {
                        SetState(P, St.Air);
                        TryAir(s, P, -P.wallDir, up, down, held, false);
                    }
                    break;

                case St.Attack:
                    UpdateAttack(s, P, dx, up, down, held);
                    break;

                case St.Shield:
                    if (P.onGround) P.vx = Approach(P.vx, 0, Tuning.GroundDecel * 1.5f);
                    else P.vx = Approach(P.vx, 0, Tuning.AirDecel);
                    if (P.blockstun > 0) { P.blockstun--; break; }
                    P.shieldHp -= Tuning.ShieldDrain;
                    if (P.shieldHp <= 0) { BreakShield(P); break; }
                    if ((held & Btn.Shield) == 0 || (!P.onGround && P.stateTime > Tuning.AirShieldMax))
                    {
                        P.shieldCd = P.stateTime <= Tuning.ParryWindow + 2 ? Tuning.ParryWhiffCooldown : Tuning.ShieldCooldown;
                        SetState(P, P.onGround ? St.Idle : St.Air);
                        break;
                    }
                    if (P.stateTime > Tuning.ParryWindow)
                    {
                        if (P.bufJump > 0 || P.bufDodge > 0)
                        {
                            P.shieldCd = Tuning.ShieldCooldown;
                            SetState(P, P.onGround ? St.Idle : St.Air);
                            if (P.onGround) TryGround(s, P, dx, up, down, 0, false); else TryAir(s, P, dx, up, down, 0, false);
                        }
                    }
                    break;

                case St.Dodge:
                    P.vx = Approach(P.vx, 0, Tuning.GroundDecel * 2f);
                    if (P.stateTime >= Tuning.DodgeActionableFrame && TryGround(s, P, dx, up, down, held, true)) break;
                    if (P.stateTime >= Tuning.DodgeFrames) SetState(P, P.onGround ? St.Idle : St.Air);
                    break;

                case St.AirDodge:
                    {
                        int frames = P.chase ? Tuning.ChaseDodgeFrames : Tuning.DodgeFrames;
                        float t = P.stateTime / (float)frames;
                        float spd = (P.chase ? Tuning.ChaseDodgeSpeed : Tuning.DodgeSpeed) * Math.Max(0f, 1f - t * t);
                        if (P.dvx == 0 && P.dvy == 0) { P.vx *= 0.8f; P.vy *= 0.8f; }
                        else { P.vx = P.dvx * spd; P.vy = P.dvy * spd; if (P.onGround && P.vy > 0) P.vy = 0; }
                        int actionable = P.chase ? 4 : Tuning.DodgeActionableFrame;
                        if (P.stateTime >= actionable)
                        {
                            bool ok = P.onGround ? TryGround(s, P, dx, up, down, held, true) : TryAir(s, P, dx, up, down, held, true);
                            if (ok) break;
                        }
                        if (P.stateTime >= frames) { P.vx *= 0.6f; SetState(P, P.onGround ? St.Idle : St.Air); }
                        break;
                    }

                case St.Hitstun:
                    P.hitstun--;
                    if (P.onGround) P.vx = Approach(P.vx, 0, Tuning.GroundDecel * 0.6f);
                    else
                    {
                        P.vx *= Tuning.KnockbackDrag;
                        if (P.vy < 0) P.vy *= Tuning.KnockbackDrag;
                        if (dx != 0) P.vx += dx * 0.15f;
                    }
                    if (P.hitstun <= 0) { P.tumble = false; SetState(P, P.onGround ? St.Idle : St.Air); }
                    break;

                case St.Parried:
                case St.Clank:
                    P.vx = Approach(P.vx, 0, P.onGround ? Tuning.GroundDecel * 0.5f : Tuning.AirDecel);
                    if (P.stateTime >= (P.state == St.Parried ? Tuning.ParryStunFrames : 16)) SetState(P, P.onGround ? St.Idle : St.Air);
                    break;

                case St.Broken:
                    P.vx = Approach(P.vx, 0, Tuning.GroundDecel);
                    if (P.stateTime >= Tuning.ShieldBreakFrames) { P.shieldHp = Tuning.ShieldMax * 0.6f; SetState(P, P.onGround ? St.Idle : St.Air); }
                    break;

                case St.Landing:
                    P.vx = Approach(P.vx, 0, Tuning.GroundDecel);
                    if (P.stateTime >= P.landLag) SetState(P, St.Idle);
                    break;

                case St.Grabbing:
                    {
                        PlayerState V = s.p[1 - P.slot];
                        if (V.state != St.Grabbed) { SetState(P, P.onGround ? St.Idle : St.Air); break; }
                        P.vx = Approach(P.vx, 0, Tuning.GroundDecel);
                        if (!P.onGround) P.vy = 0;
                        V.x = P.x + P.facing * 58;
                        V.y = P.y;
                        V.vx = V.vy = 0;
                        V.onGround = P.onGround;
                        bool choose = dx != 0 || up || down || P.bufLight > 0 || P.bufHeavy > 0 || P.bufGrab > 0;
                        if ((P.stateTime >= 6 && choose) || P.stateTime >= Tuning.GrabHoldMax)
                        {
                            int kind = up ? 2 : down ? 3 : (dx == -P.facing ? 1 : 0);
                            P.bufLight = P.bufHeavy = P.bufGrab = 0;
                            Throw(P, V, kind);
                        }
                        break;
                    }

                case St.Grabbed:
                    {
                        PlayerState G = s.p[1 - P.slot];
                        if (G.state != St.Grabbing || P.stateTime > Tuning.GrabHoldMax + 20) SetState(P, P.onGround ? St.Idle : St.Air);
                        break;
                    }

                case St.Respawn:
                    P.vx = 0; P.vy = 0;
                    P.invuln = Tuning.RespawnInvuln;
                    if (P.stateTime > 100 || (P.stateTime > 25 && held != 0))
                    {
                        SetState(P, St.Air);
                        P.airJumps = Tuning.AirJumps;
                    }
                    break;
            }

            if (P.onGround) P.runPhase += Math.Abs(P.vx) * 0.042f;
            Physics(P, (held & Btn.Down) != 0);
            P.prevHeld = held;
        }

        static void GroundMove(PlayerState P, int dx)
        {
            if (dx != 0)
            {
                if (dx != P.facing) { P.facing = dx; P.sprint = false; }
                float target = (P.sprint ? Tuning.SprintSpeed : Tuning.RunSpeed) * C(P).Speed * dx;
                float acc = Tuning.GroundAccel * C(P).Speed * (Math.Sign(P.vx) == -dx ? 2.2f : 1f);
                if (Math.Abs(P.vx) > Math.Abs(target) && Math.Sign(P.vx) == dx) acc = Tuning.GroundDecel * 0.5f;
                P.vx = Approach(P.vx, target, acc);
                if (P.state != St.Run) SetState(P, St.Run);
            }
            else
            {
                P.vx = Approach(P.vx, 0, Tuning.GroundDecel);
                P.sprint = false;
                if (P.state != St.Idle) SetState(P, St.Idle);
            }
        }

        static void AirMove(PlayerState P, int dx, int held)
        {
            if (dx != 0)
            {
                P.facing = dx;
                float am = Tuning.AirMaxSpeed * C(P).Speed;
                float target = dx * am;
                if (Math.Abs(P.vx) > am && Math.Sign(P.vx) == dx) P.vx = Approach(P.vx, target, Tuning.AirDecel);
                else P.vx = Approach(P.vx, target, Tuning.AirAccel * C(P).Speed);
            }
            else P.vx = Approach(P.vx, 0, Tuning.AirDecel);

            if ((held & Btn.Jump) == 0 && P.vy < 0 && !P.jumpCut)
            {
                P.vy *= Tuning.JumpCutMultiplier;
                P.jumpCut = true;
            }
        }

        static bool CanShield(PlayerState P) { return P.shieldCd <= 0 && P.shieldHp > 5; }

        static void EnterShield(PlayerState P, bool fresh)
        {
            SetState(P, St.Shield);
            P.parryOk = fresh;
            P.bufShield = 0;
            P.sprint = false;
            if (!fresh) P.stateTime = Tuning.ParryWindow + 3;
        }

        static void GroundJump(PlayerState P)
        {
            P.vy = -Tuning.JumpForce * C(P).Jump;
            P.onGround = false;
            P.plat = -1;
            P.jumpCut = false;
            P.fastFall = false;
            P.groundJumpAvail = false;
            P.sprint = false;
            SetState(P, St.Air);
            Emit(Ev.Jump, P.slot, P.x, P.y, 0);
        }

        // Verfolgungs-Dodge: nur direkt nach einem Treffer, jagt dem Gegner hinterher (verbraucht keinen Luft-Dodge)
        static bool ChaseDodge(GameState s, PlayerState P, int dx, bool up, bool down)
        {
            P.bufDodge = 0;
            PlayerState V = s.p[1 - P.slot];
            float ddx = dx, ddy = up ? -1 : down ? 1 : 0;
            float scale = 1f;
            if (ddx == 0 && ddy == 0)
            {
                // ohne Richtung: zum Gegner, aber ca. 70 px vor ihm anhalten
                ddx = V.x - P.x; ddy = V.y - P.y;
                if (P.onGround && ddy > -50) ddy = 0;
                float dist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                scale = Math.Max(0.15f, Math.Min(1f, (dist - 70) / (0.67f * Tuning.ChaseDodgeFrames * Tuning.ChaseDodgeSpeed)));
            }
            if (P.onGround && ddy > 0) ddy = 0;
            float len = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
            if (len < 0.01f) { ddx = P.facing; ddy = 0; len = 1; }
            SetState(P, St.AirDodge);
            P.chase = true;
            P.dvx = ddx / len * scale; P.dvy = ddy / len * scale;
            if (P.dvx != 0) P.facing = P.dvx > 0 ? 1 : -1;
            P.invuln = 5;
            P.fastFall = false;
            P.dodgeCd = Tuning.ChaseDodgeCooldown;
            Emit(Ev.Dodge, P.slot, P.x, P.y - 46, 2);
            return true;
        }

        // Aktionen am Boden. onlyBuffered: nur gepufferte Tasten zählen (für Cancels)
        static bool TryGround(GameState s, PlayerState P, int dx, bool up, bool down, int held, bool onlyBuffered)
        {
            bool comboCancel = onlyBuffered && P.state == St.Attack && P.atkHit;
            if (P.bufShield > 0 && CanShield(P)) { EnterShield(P, true); return true; }
            if (comboCancel && P.bufDodge > 0) return ChaseDodge(s, P, dx, up, down);
            if (P.bufDodge > 0 && P.dodgeCd <= 0)
            {
                P.bufDodge = 0;
                if (dx != 0 && !down)
                {
                    SetState(P, St.Dash);
                    P.dashDir = dx;
                    P.facing = dx;
                    P.vx = dx * Tuning.DashSpeed * C(P).Speed;
                    P.dodgeCd = Tuning.DashCooldown;
                    Emit(Ev.Dash, P.slot, P.x, P.y, dx);
                }
                else
                {
                    SetState(P, St.Dodge);
                    P.invuln = Tuning.DodgeInvulnFrames;
                    P.dodgeCd = Tuning.SpotDodgeCooldown;
                    Emit(Ev.Dodge, P.slot, P.x, P.y - 46, 0);
                }
                return true;
            }
            if (P.bufGrab > 0)
            {
                P.bufGrab = 0;
                if (dx != 0) P.facing = dx;
                StartAttack(P, Attacks.Grab);
                return true;
            }
            if (P.bufHeavy > 0)
            {
                P.bufHeavy = 0;
                int a = down ? Attacks.DHeavy : (dx != 0 ? Attacks.SHeavy : Attacks.NHeavy);
                if (dx != 0) P.facing = dx;
                if (onlyBuffered && P.state == St.Attack && P.atk == a) return false;
                StartAttack(P, a);
                return true;
            }
            if (P.bufLight > 0)
            {
                P.bufLight = 0;
                int a = up ? Attacks.ULight : down ? Attacks.DLight : dx != 0 ? Attacks.SLight : Attacks.NLight;
                if (dx != 0) P.facing = dx;
                if (onlyBuffered && P.state == St.Attack && P.atk == a) return false;
                StartAttack(P, a);
                return true;
            }
            if (P.bufJump > 0)
            {
                P.bufJump = 0;
                GroundJump(P);
                if (perfectCancel) P.jumpCut = true;
                AimJump(P);
                return true;
            }
            if (!onlyBuffered && (held & Btn.Shield) != 0 && CanShield(P)) { EnterShield(P, false); return true; }
            return false;
        }

        static bool TryAir(GameState s, PlayerState P, int dx, bool up, bool down, int held, bool onlyBuffered)
        {
            bool comboCancel = onlyBuffered && P.state == St.Attack && P.atkHit;
            if (P.bufShield > 0 && CanShield(P)) { EnterShield(P, true); return true; }
            if (comboCancel && P.bufDodge > 0) return ChaseDodge(s, P, dx, up, down);
            if (P.bufDodge > 0 && P.dodgeCd <= 0 && !P.airDodgeUsed)
            {
                P.bufDodge = 0;
                float ddx = dx, ddy = up ? -1 : down ? 1 : 0;
                float len = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                if (len > 0) { ddx /= len; ddy /= len; }
                SetState(P, St.AirDodge);
                P.chase = false;
                P.dvx = ddx; P.dvy = ddy;
                P.invuln = Tuning.DodgeInvulnFrames;
                P.airDodgeUsed = true;
                P.fastFall = false;
                P.dodgeCd = len > 0 ? Tuning.DodgeCooldown : Tuning.SpotDodgeCooldown;
                Emit(Ev.Dodge, P.slot, P.x, P.y - 46, 1);
                return true;
            }
            if (P.bufGrab > 0)
            {
                P.bufGrab = 0;
                if (dx != 0) P.facing = dx;
                StartAttack(P, Attacks.Grab);
                return true;
            }
            if (P.bufHeavy > 0)
            {
                P.bufHeavy = 0;
                int a = up ? Attacks.UAirHeavy : dx != 0 ? Attacks.SAirHeavy : Attacks.DAirHeavy;
                if (a == Attacks.UAirHeavy && P.recoveryUsed) a = -1;
                if (a >= 0 && !(onlyBuffered && P.state == St.Attack && P.atk == a))
                {
                    if (dx != 0) P.facing = dx;
                    StartAttack(P, a);
                    return true;
                }
            }
            if (P.bufLight > 0)
            {
                int a = up ? Attacks.UAir : down ? Attacks.DAir : dx != 0 ? Attacks.SAir : Attacks.NAir;
                if (!(onlyBuffered && P.state == St.Attack && P.atk == a))
                {
                    P.bufLight = 0;
                    if (dx != 0) P.facing = dx;
                    StartAttack(P, a);
                    return true;
                }
            }
            if (P.bufJump > 0)
            {
                if (P.groundJumpAvail && P.airTime <= Tuning.CoyoteFrames)
                {
                    P.bufJump = 0;
                    GroundJump(P);
                    return true;
                }
                // Perfekter Jump-Cancel kostet keinen Luftsprung -> perfekte Ketten reißen nicht ab
                if (P.airJumps > 0 || perfectCancel)
                {
                    P.bufJump = 0;
                    if (!perfectCancel) P.airJumps--;
                    P.vy = -Tuning.AirJumpForce * C(P).Jump;
                    P.jumpCut = perfectCancel; // perfekter Sprung ist immer ein voller Sprung
                    P.fastFall = false;
                    P.airJumpTime = 0;
                    if (dx != 0) { P.vx = dx * Math.Max(Math.Abs(P.vx) * 0.5f, Tuning.AirMaxSpeed * 0.9f); P.facing = dx; }
                    AimJump(P);
                    SetState(P, St.Air);
                    Emit(Ev.AirJump, P.slot, P.x, P.y, 0);
                    return true;
                }
            }
            return false;
        }

        static void StartAttack(PlayerState P, int id)
        {
            AttackDef d = Attacks.SetOf(P.charId)[id];
            SetState(P, St.Attack);
            P.atk = id;
            P.atkFrame = 0;
            P.charge = 0;
            P.chargeFull = false;
            P.atkHit = false;
            P.sinceHit = 0;
            P.pressMark = -1;
            P.sprint = false;
            if (!d.Air && P.onGround) P.vx *= 0.55f;
            if (id == Attacks.UAirHeavy) { P.recoveryUsed = true; P.fastFall = false; }
        }

        static void UpdateAttack(GameState s, PlayerState P, int dx, bool up, bool down, int held)
        {
            AttackDef d = Attacks.Of(P);
            if (d.ChargeMax > 0 && P.atkFrame == d.ChargeFrame && (held & Btn.Heavy) != 0 && P.charge < d.ChargeMax)
            {
                P.charge++;
                if (P.charge == d.ChargeMax) { P.chargeFull = true; Emit(Ev.ChargeFull, P.slot, P.x, P.y - 60, 0); }
            }
            else
            {
                P.atkFrame++;
                if (P.atkFrame == d.Startup && P.atk != Attacks.Throw && d.ProjKind < 0) Emit(Ev.Swing, P.slot, P.x, P.y - 50, d.Heavy ? 1 : 0);
                if (P.atkFrame == d.Startup && d.ProjKind >= 0) Shoot(P, d);
                if (P.atk == Attacks.DHeavy && P.atkFrame == d.Startup) Emit(Ev.Shockwave, P.slot, P.x, P.y, 1);
            }

            // Bewegung während des Angriffs
            if (P.onGround) P.vx = Approach(P.vx, 0, Tuning.GroundDecel * 0.6f);
            else if (dx != 0) P.vx = Approach(P.vx, dx * Tuning.AirMaxSpeed, Tuning.AirAccel * 0.45f);
            int f = P.atkFrame, st = d.Startup, ae = d.Startup + d.Active;
            switch (P.atk)
            {
                case Attacks.SAirHeavy:
                    if (f < st) { P.vy = Math.Min(P.vy, 1.5f) * 0.7f; P.vx *= 0.92f; P.fastFall = false; }
                    break;
                case Attacks.DAirHeavy:
                    if (f < st) { P.vx *= 0.85f; P.vy = -1.4f; P.fastFall = false; }
                    else if (f < ae) { P.vy = d.PlungeSpeed; P.vx = P.facing * 1.5f; }
                    break;
                case Attacks.UAirHeavy:
                    if (f >= st && f < ae) { float t = (f - st) / (float)d.Active; P.vy = -d.RiseSpeed * (1 - t) - 2f; P.vx = P.facing * 4f; }
                    break;
                case Attacks.NAir:
                case Attacks.UAir:
                    if (f < st && P.vy > 2) P.vy *= 0.9f;
                    break;
                case Attacks.Grab:
                    if (!P.onGround && f < ae) P.vy = Math.Min(P.vy, 2f);
                    break;
            }

            if (d.Lunge != 0 && f < st + 2 && P.onGround) P.vx = P.facing * d.Lunge;
            if (d.ActiveDash != 0 && f >= st && f < ae && (d.Air || P.onGround)) P.vx = P.facing * d.ActiveDash;

            // Combo-Cancel nur nach einem echten Treffer
            if (P.atkHit)
            {
                P.sinceHit++;
                if (P.sinceHit >= Tuning.ComboCancelDelay && P.sinceHit <= Tuning.ComboCancelDelay + Tuning.ComboWindow)
                {
                    bool perfect = P.pressMark >= 1 && P.pressMark <= Tuning.ComboCancelDelay + Tuning.PerfectWindow;
                    PlayerState V = s.p[1 - P.slot];
                    bool anyBuf = P.bufLight > 0 || P.bufHeavy > 0 || P.bufJump > 0 || P.bufDodge > 0 || P.bufGrab > 0;
                    bool boost = perfect && anyBuf && V.state == St.Hitstun;
                    if (boost)
                    {
                        // PERFEKT: Gegner bleibt länger betäubt und driftet weniger weg
                        V.hitstun = Math.Min(V.hitstun + Tuning.PerfectHitstunBonus, 75);
                        V.vx *= 0.6f;
                        if (V.vy > 0) V.vy *= 0.3f;
                    }
                    perfectCancel = perfect;
                    perfectTarget = boost ? V : null;
                    bool ok = P.onGround ? TryGround(s, P, dx, up, down, held, true) : TryAir(s, P, dx, up, down, held, true);
                    perfectCancel = false;
                    perfectTarget = null;
                    if (ok)
                    {
                        if (boost)
                        {
                            P.perfects++;
                            P.comboTimer = 100;
                            Emit(Ev.Perfect, P.slot, V.x, V.y - 120, P.perfects);
                        }
                        return;
                    }
                }
            }

            if (P.atkFrame >= d.Total) SetState(P, P.onGround ? St.Idle : St.Air);
        }

        static void BreakShield(PlayerState P)
        {
            P.shieldHp = 0;
            SetState(P, St.Broken);
            if (P.onGround) P.vy = -6;
            Emit(Ev.ShieldBreak, P.slot, P.x, P.y - 50, 0);
        }

        static void Respawn(GameState s, PlayerState P)
        {
            PlayerState o = s.p[1 - P.slot];
            P.x = o.state == St.Respawn ? (P.slot == 0 ? -140 : 140) : 0;
            P.y = -620;
            P.vx = P.vy = 0;
            P.damage = 0;
            P.shieldHp = Tuning.ShieldMax;
            P.airJumps = Tuning.AirJumps;
            P.wallJumps = Tuning.WallJumps;
            P.recoveryUsed = false; P.airDodgeUsed = false; P.fastFall = false; P.tumble = false;
            P.hitstun = 0; P.hitlag = 0; P.dodgeCd = 0; P.shieldCd = 0;
            P.onGround = false;
            P.facing = P.x <= 0 ? 1 : -1;
            SetState(P, St.Respawn);
            P.invuln = Tuning.RespawnInvuln;
            Emit(Ev.Respawn, P.slot, P.x, P.y, 0);
        }

        // ------------------------------------------------------------------ Physik

        static void Physics(PlayerState P, bool holdingDown)
        {
            if (P.state == St.Respawn || P.state == St.KO || P.state == St.Grabbed) return;
            bool wasGround = P.onGround;
            float hw = Arena.HalfW, h = Arena.Height;

            if (!P.onGround && P.state != St.AirDodge && P.state != St.Grabbing)
            {
                P.vy += Tuning.Gravity * C(P).Grav * (P.state == St.Hitstun ? Tuning.HitstunGravity : 1f);
                float maxF = Tuning.MaxFallSpeed * C(P).Grav;
                if (P.state == St.Hitstun) maxF = 24f;
                else if (P.state == St.Wall) maxF = Tuning.WallSlideSpeed;
                else if (P.atk == Attacks.DAirHeavy) maxF = 21f;
                else if (P.fastFall) { maxF = Tuning.FastFallSpeed * C(P).Grav; P.vy = Math.Max(P.vy, maxF); }
                if (P.vy > maxF) P.vy = maxF;
            }

            Box M = Arena.Main;
            // horizontal
            P.x += P.vx;
            if (P.x + hw > M.L && P.x - hw < M.R && P.y > M.T + 0.5f && P.y - h < M.B)
            {
                // Kantenhilfe: knapp unter der Oberkante wird man auf die Bühne gezogen
                if (P.y <= M.T + Tuning.LedgeAssist && P.vy > -8f && P.state != St.Hitstun && !P.onGround)
                {
                    P.y = M.T;
                    P.vy = 0;
                }
                else
                {
                    if (P.x < M.CX) P.x = M.L - hw; else P.x = M.R + hw;
                    if (P.state == St.Hitstun && Math.Abs(P.vx) > 7) { P.vx = -P.vx * 0.4f; Emit(Ev.Bounce, P.slot, P.x, P.y - 46, 1); }
                    else P.vx = 0;
                }
            }

            // vertikal
            float prevY = P.y;
            float impact = P.vy;
            P.y += P.vy;
            P.onGround = false;
            int landPlat = -1;
            if (P.x + hw > M.L && P.x - hw < M.R)
            {
                if (P.vy >= 0 && prevY <= M.T + 0.5f && P.y >= M.T) { P.y = M.T; landPlat = 0; }
                else if (P.y > M.T + 0.5f && P.y - h < M.B)
                {
                    if (P.vy < 0) { P.y = M.B + h; P.vy = 0; }
                    else { P.y = M.T; landPlat = 0; }
                }
            }
            bool passSoft = P.dropTimer > 0 || (holdingDown && (P.state == St.Air || P.state == St.AirDodge || (P.state == St.Attack && !wasGround)));
            if (landPlat < 0 && P.vy >= 0 && !passSoft)
            {
                for (int i = 0; i < Arena.Soft.Length; i++)
                {
                    SoftPlat sp = Arena.Soft[i];
                    if (P.x + hw * 0.5f > sp.L && P.x - hw * 0.5f < sp.R && prevY <= sp.Y + 0.5f && P.y >= sp.Y)
                    {
                        P.y = sp.Y;
                        landPlat = i + 1;
                        break;
                    }
                }
            }
            if (landPlat >= 0)
            {
                P.onGround = true;
                P.plat = landPlat;
                P.vy = 0;
                if (!wasGround) OnLand(P, impact);
            }
            else P.plat = -1;

            if (P.onGround) P.airTime = 0; else P.airTime++;

            // Wandkontakt (Seitenwände der Hauptplattform)
            P.wallDir = 0;
            if (!P.onGround && P.y - h < M.B - 10 && P.y > M.T + 20)
            {
                if (Math.Abs(P.x + hw - M.L) < 1.5f) P.wallDir = 1;
                else if (Math.Abs(P.x - hw - M.R) < 1.5f) P.wallDir = -1;
            }
        }

        static void OnLand(PlayerState P, float impact)
        {
            P.airJumps = Tuning.AirJumps;
            P.wallJumps = Tuning.WallJumps;
            P.recoveryUsed = false;
            P.airDodgeUsed = false;
            P.fastFall = false;
            P.groundJumpAvail = true;
            switch (P.state)
            {
                case St.Air:
                case St.Wall:
                    P.landAnim = 7;
                    SetState(P, Math.Abs(P.vx) > 0.5f ? St.Run : St.Idle);
                    if (impact > 5) Emit(Ev.Land, P.slot, P.x, P.y, impact);
                    break;
                case St.AirDodge:
                    P.landAnim = 5;
                    SetState(P, St.Idle);
                    break;
                case St.Attack:
                    {
                        AttackDef d = Attacks.Of(P);
                        if (P.atk == Attacks.DAirHeavy && P.atkFrame >= d.Startup)
                        {
                            StartAttack(P, Attacks.Impact);
                            P.vx = 0;
                            Emit(Ev.Shockwave, P.slot, P.x, P.y, 2);
                        }
                        else if (d.Air && !P.atkHit)
                        {
                            int lag = Math.Min(d.Total - P.atkFrame, d.Heavy ? Tuning.LandingLagHeavy : Tuning.LandingLagLight);
                            if (lag > 0) { SetState(P, St.Landing); P.landLag = lag; }
                            else SetState(P, St.Idle);
                            P.landAnim = 6;
                        }
                        break;
                    }
                case St.Hitstun:
                    if (P.tumble && impact > 7)
                    {
                        P.vy = -impact * 0.5f;
                        P.onGround = false;
                        P.y -= 0.6f;
                        Emit(Ev.Bounce, P.slot, P.x, P.y, impact);
                    }
                    break;
            }
        }

        static void Separate(PlayerState a, PlayerState b)
        {
            if (!a.Alive || !b.Alive || a.state == St.Respawn || b.state == St.Respawn) return;
            if (a.state == St.Grabbed || b.state == St.Grabbed) return;
            float d = b.x - a.x;
            if (Math.Abs(d) < 30 && Math.Abs(a.y - b.y) < 60 && a.onGround && b.onGround)
            {
                float push = (30 - Math.Abs(d)) * 0.12f;
                int s = d >= 0 ? 1 : -1;
                if (d == 0) s = a.slot == 0 ? 1 : -1;
                a.x -= s * push;
                b.x += s * push;
            }
        }

        // ------------------------------------------------------------------ Kampf (nur autoritativ)

        public static bool HitboxActive(PlayerState P, out Box hb, out AttackDef d)
        {
            hb = new Box();
            d = null;
            if (P.state != St.Attack || P.atk < 0) return false;
            d = Attacks.Of(P);
            if (d.HW <= 0 || P.atkFrame < d.Startup || P.atkFrame >= d.Startup + d.Active) return false;
            hb = WorldBox(P, d);
            return true;
        }

        public static Box WorldBox(PlayerState P, AttackDef d)
        {
            float l = P.facing == 1 ? P.x + d.HX : P.x - d.HX - d.HW;
            return new Box(l, P.y + d.HY, l + d.HW, P.y + d.HY + d.HH);
        }

        static void Combat(GameState s)
        {
            Box[] hb = new Box[2];
            AttackDef[] def = new AttackDef[2];
            bool[] act = new bool[2];
            bool[] hits = new bool[2];
            for (int i = 0; i < 2; i++)
            {
                PlayerState A = s.p[i];
                act[i] = !A.atkHit && A.hitlag == 0 && HitboxActive(A, out hb[i], out def[i]);
            }

            // Klingen treffen aufeinander -> Clank (Greifen ausgenommen)
            if (act[0] && act[1] && !def[0].Grab && !def[1].Grab && hb[0].Hits(hb[1]))
            {
                for (int i = 0; i < 2; i++)
                {
                    PlayerState A = s.p[i];
                    A.atkHit = true;
                    SetState(A, St.Clank);
                    A.vx = (A.x < s.p[1 - i].x ? -1 : 1) * 5f;
                    A.hitlag = 5;
                }
                Emit(Ev.Clank, 0, (hb[0].CX + hb[1].CX) * 0.5f, (hb[0].CY + hb[1].CY) * 0.5f, 0);
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (!act[i]) continue;
                PlayerState B = s.p[1 - i];
                if (B.state == St.KO || B.state == St.Respawn || B.state == St.Grabbed || B.state == St.Grabbing || B.invuln > 0) continue;
                if (hb[i].Hits(B.Hurt)) hits[i] = true;
            }
            // Greifen gewinnt nicht gegen einen gleichzeitigen Treffer
            for (int i = 0; i < 2; i++)
                if (hits[i] && def[i].Grab && hits[1 - i] && !def[1 - i].Grab) hits[i] = false;
            // gleichzeitige Treffer werden beide gewertet (Trade)
            PlayerState[] snap = { s.p[0].Clone(), s.p[1].Clone() };
            for (int i = 0; i < 2; i++)
                if (hits[i]) ResolveHit(s, s.p[i], s.p[1 - i], snap[1 - i], def[i], hb[i]);
        }

        static void ResolveHit(GameState s, PlayerState A, PlayerState B, PlayerState Bsnap, AttackDef d, Box hb)
        {
            A.atkHit = true;
            A.sinceHit = 0;
            A.pressMark = -1;
            Box hurt = Bsnap.Hurt;
            float cx = (Math.Max(hb.L, hurt.L) + Math.Min(hb.R, hurt.R)) * 0.5f;
            float cy = (Math.Max(hb.T, hurt.T) + Math.Min(hb.B, hurt.B)) * 0.5f;

            // Sensenhaken: packt den Gegner – geht durch Block und Parade
            if (d.Grab)
            {
                A.grabCombo = Bsnap.state == St.Hitstun;
                SetState(A, St.Grabbing);
                SetState(B, St.Grabbed);
                B.vx = B.vy = 0;
                B.fastFall = false;
                B.blockstun = 0;
                B.hurtFlash = 6;
                B.facing = -A.facing;
                A.hitlag = B.hitlag = 5;
                Emit(Ev.Grab, B.slot, cx, cy, 0);
                return;
            }

            float chargeT = d.ChargeMax > 0 ? A.charge / (float)d.ChargeMax : 0f;
            float dmg = d.Damage * (1f + Tuning.ChargeDamageBonus * d.ChargeScale * chargeT);
            int towardA = Math.Sign(A.x - Bsnap.x);
            bool front = towardA == Bsnap.facing || Math.Abs(A.x - Bsnap.x) < 4;

            if (Bsnap.state == St.Shield)
            {
                if (Bsnap.parryOk && Bsnap.stateTime <= Tuning.ParryWindow)
                {
                    // Perfekte Parade
                    SetState(A, St.Parried);
                    A.vx = -A.facing * 4f;
                    if (!A.onGround) A.vy = Math.Min(A.vy, -2f);
                    A.hitlag = 7;
                    B.hitlag = 7;
                    SetState(B, B.onGround ? St.Idle : St.Air);
                    B.invuln = Tuning.ParryInvulnFrames;
                    B.shieldCd = 0;
                    B.shieldHp = Math.Min(Tuning.ShieldMax, B.shieldHp + 20);
                    B.parryFlash = 24;
                    B.statParries++;
                    Emit(Ev.Parry, B.slot, cx, cy, dmg);
                    return;
                }
                if (front)
                {
                    float sd = dmg * Tuning.ShieldDamageMult * (d.Heavy ? Tuning.HeavyShieldMult : 1f);
                    B.shieldHp -= sd;
                    B.damage += dmg * Tuning.BlockChipMult;
                    B.blockstun = (int)(Tuning.BlockstunBase + dmg * 0.6f);
                    int away = B.x >= A.x ? 1 : -1;
                    B.vx = away * (d.Heavy ? 7f : 4f);
                    A.vx -= away * 2f;
                    A.hitlag = 4;
                    B.hitlag = 4;
                    if (B.shieldHp <= 0) BreakShield(B);
                    else Emit(Ev.Block, B.slot, cx, cy, dmg);
                    return;
                }
            }

            if (Bsnap.state == St.Attack && Bsnap.charge > 0) dmg *= Tuning.ChargeVulnerability;

            int dir = d.Away ? (Bsnap.x >= A.x ? 1 : -1) : A.facing;
            float ang = d.Angle;
            // Trefferposition beeinflusst den Winkel leicht
            float rel = ((Bsnap.y - 47) - hb.CY) / (Math.Max(20f, hb.B - hb.T) * 0.5f);
            rel = Math.Max(-1f, Math.Min(1f, rel));
            ang += rel * 8f;
            double rad = ang * Math.PI / 180.0;
            float kx = (float)Math.Cos(rad) * dir, ky = (float)Math.Sin(rad);
            if (d.Radial)
            {
                float rx = Bsnap.x - hb.CX, ry = (Bsnap.y - 47) - hb.CY;
                float rl = (float)Math.Sqrt(rx * rx + ry * ry);
                if (rl > 1)
                {
                    kx = kx * 0.5f + rx / rl * 0.5f;
                    ky = ky * 0.5f + Math.Min(0.2f, ry / rl) * 0.5f;
                    float n = (float)Math.Sqrt(kx * kx + ky * ky);
                    kx /= n; ky /= n;
                }
            }
            ApplyHit(A, B, Bsnap.state == St.Hitstun, dmg, d.KbBase, d.KbGrowth, kx, ky, 1f + 0.35f * chargeT, d.Heavy, cx, cy);
        }

        // Schaden + Knockback + Hitstun auf B anwenden (Angriffe und Würfe)
        static void ApplyHit(PlayerState A, PlayerState B, bool comboHit, float dmg, float kbBase, float kbGrowth, float kx, float ky, float kbMult, bool heavy, float cx, float cy, bool ranged = false)
        {
            B.damage = Math.Min(999f, B.damage + dmg);
            // Super-Rüstung: Schaden ja, aber keine Unterbrechung
            if (B.state == St.Attack && B.atk >= 0 && B.atkFrame < Attacks.Of(B).Startup && Attacks.Of(B).Armor)
            {
                B.hurtFlash = 6;
                B.hitlag = 3;
                if (!ranged) A.hitlag = 3;
                A.statHits++;
                A.statDamage += dmg;
                Emit(Ev.Hit, B.slot, cx, cy, 2000);
                return;
            }
            float kb = (kbBase + kbGrowth * B.damage) * Tuning.KnockbackScale * kbMult * C(B).Weight;
            if (B.onGround && ky > 0) ky = -ky * 0.6f;

            SetState(B, St.Hitstun);
            B.vx = kx * kb;
            B.vy = ky * kb;
            if (B.vy < 0) B.onGround = false;
            B.hitstun = (int)Math.Min(Tuning.HitstunMax, Tuning.HitstunBase + kb * Tuning.HitstunPerKnockback);
            B.tumble = kb > Tuning.TumbleThreshold;
            B.fastFall = false;
            B.blockstun = 0;
            // getroffen werden gibt alle Sprünge, Recovery und Luft-Dodge zurück -> Rettung nach dem Rauswurf möglich
            B.airJumps = Tuning.AirJumps;
            B.wallJumps = Tuning.WallJumps;
            B.recoveryUsed = false;
            B.airDodgeUsed = false;
            B.dodgeCd = Math.Min(B.dodgeCd, 20);
            if (kx != 0) B.facing = kx > 0 ? -1 : 1;
            B.hurtFlash = 8;

            int lag = (int)(Tuning.HitlagBase + dmg * Tuning.HitlagPerDamage);
            if (!ranged) A.hitlag = lag;
            B.hitlag = lag;
            A.combo = comboHit ? A.combo + 1 : 1;
            if (!comboHit) A.perfects = 0;
            A.comboTimer = 100;
            A.statHits++;
            A.statDamage += dmg;
            A.statBestCombo = Math.Max(A.statBestCombo, A.combo);
            A.pressMark = -1;
            Emit(Ev.Hit, B.slot, cx, cy, kb + (heavy ? 1000 : 0));
        }

        // Wurf aus dem Sensenhaken: 0 vorne, 1 hinten, 2 oben, 3 unten
        static void Throw(PlayerState P, PlayerState V, int kind)
        {
            if (kind == 1) P.facing = -P.facing;
            float ang, dmg, kbB, kbG;
            switch (kind)
            {
                case 2: ang = -88; dmg = 7; kbB = 7.5f; kbG = 0.06f; break;
                case 3: ang = 80; dmg = 6; kbB = 6f; kbG = 0.025f; break;
                default: ang = -38; dmg = 8; kbB = 7.5f; kbG = 0.075f; break;
            }
            double rad = ang * Math.PI / 180.0;
            float kx = (float)Math.Cos(rad) * P.facing, ky = (float)Math.Sin(rad);
            V.x = P.x + P.facing * 40;
            bool combo = P.grabCombo;
            StartAttack(P, Attacks.Throw);
            P.atkHit = true;
            P.sinceHit = 0;
            SetState(V, St.Hitstun);
            ApplyHit(P, V, combo, dmg, kbB, kbG, kx, ky, 1f, kind != 3, V.x, V.y - 50);
            Emit(Ev.Throw, P.slot, V.x, V.y - 50, kind);
        }

        // ------------------------------------------------------------------ Projektile

        static void Shoot(PlayerState P, AttackDef d)
        {
            if (d.ProjKind == 2 && !P.onGround) return; // Bodenwelle nur am Boden
            float ct = d.ChargeMax > 0 ? P.charge / (float)d.ChargeMax : 0f;
            double a = d.ProjAngle * Math.PI / 180.0;
            float spd = d.ProjSpeed * (1f + 0.35f * ct);
            P.projOn = true;
            P.projKind = d.ProjKind;
            P.projLife = d.ProjLife;
            P.projDir = P.facing;
            P.projVx = (float)Math.Cos(a) * spd * P.facing;
            P.projVy = (float)Math.Sin(a) * spd;
            P.projX = P.x + P.facing * 34;
            P.projY = d.ProjKind == 2 ? P.y : P.y - 58;
            P.projDmg = d.ProjDmg * (1f + Tuning.ChargeDamageBonus * ct);
            P.projKbB = d.ProjKbB * (1f + 0.3f * ct);
            P.projKbG = d.ProjKbG;
            P.projAng = d.ProjKbAng;
            if (d.Recoil != 0) P.vx = -P.facing * d.Recoil;
            Emit(Ev.Shoot, P.slot, P.projX, P.projY, d.ProjKind);
        }

        static bool GroundAt(float x, float y)
        {
            Box M = Arena.Main;
            if (Math.Abs(y - M.T) < 1 && x > M.L && x < M.R) return true;
            foreach (var sp in Arena.Soft) if (Math.Abs(y - sp.Y) < 1 && x > sp.L && x < sp.R) return true;
            return false;
        }

        static void MoveProjectile(PlayerState P)
        {
            if (!P.projOn) return;
            P.projLife--;
            if (P.projKind == 2)
            {
                P.projX += P.projVx;
                if (!GroundAt(P.projX, P.projY)) P.projOn = false;
            }
            else
            {
                P.projVy += 0.1f;
                P.projX += P.projVx;
                P.projY += P.projVy;
                Box M = Arena.Main;
                if (P.projX > M.L && P.projX < M.R && P.projY > M.T && P.projY < M.B) { P.projOn = false; Emit(Ev.ProjBreak, P.slot, P.projX, P.projY, 0); }
            }
            if (P.projLife <= 0 || P.projX < Arena.BlastL || P.projX > Arena.BlastR || P.projY < Arena.BlastT || P.projY > Arena.BlastB)
            {
                if (P.projOn && P.projKind == 0) Emit(Ev.ProjBreak, P.slot, P.projX, P.projY, 0);
                P.projOn = false;
            }
        }

        public static Box ProjBox(PlayerState P)
        {
            if (P.projKind == 2) return new Box(P.projX - 30, P.projY - 48, P.projX + 30, P.projY);
            return new Box(P.projX - 20, P.projY - 9, P.projX + 20, P.projY + 9);
        }

        static void ProjectileHits(GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerState A = s.p[i], B = s.p[1 - i];
                if (!A.projOn) continue;
                if (B.state == St.KO || B.state == St.Respawn || B.state == St.Grabbed || B.invuln > 0) continue;
                Box pb = ProjBox(A);
                if (!pb.Hits(B.Hurt)) continue;
                A.projOn = false;
                float cx = A.projX, cy = A.projKind == 2 ? A.projY - 24 : A.projY;
                if (B.state == St.Shield)
                {
                    if (B.parryOk && B.stateTime <= Tuning.ParryWindow)
                    {
                        B.parryFlash = 24;
                        B.statParries++;
                        B.invuln = Tuning.ParryInvulnFrames;
                        Emit(Ev.Parry, B.slot, cx, cy, A.projDmg);
                        continue;
                    }
                    if (B.facing == -A.projDir)
                    {
                        B.shieldHp -= A.projDmg * Tuning.ShieldDamageMult;
                        B.blockstun = (int)(Tuning.BlockstunBase + A.projDmg * 0.4f);
                        B.vx = A.projDir * 3f;
                        if (B.shieldHp <= 0) BreakShield(B); else Emit(Ev.Block, B.slot, cx, cy, A.projDmg);
                        continue;
                    }
                }
                double rad = A.projAng * Math.PI / 180.0;
                float kx = (float)Math.Cos(rad) * A.projDir, ky = (float)Math.Sin(rad);
                // trifft der Pfeil, darf der Schütze aus seinem Angriff weiter-comboen
                if (A.state == St.Attack) { A.atkHit = true; A.sinceHit = 0; A.pressMark = -1; }
                ApplyHit(A, B, B.state == St.Hitstun, A.projDmg, A.projKbB, A.projKbG, kx, ky, 1f, false, cx, cy, true);
            }
        }

        static void CheckBlast(GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerState P = s.p[i];
                if (P.state == St.KO || P.state == St.Respawn) continue;
                if (P.x < Arena.BlastL || P.x > Arena.BlastR || P.y < Arena.BlastT || P.y > Arena.BlastB)
                {
                    float ex = Math.Max(Arena.BlastL, Math.Min(Arena.BlastR, P.x));
                    float ey = Math.Max(Arena.BlastT, Math.Min(Arena.BlastB, P.y - 46));
                    if (s.phase == GameState.Fight) P.stocks--;
                    SetState(P, St.KO);
                    P.koTimer = Tuning.RespawnDelay;
                    P.vx = P.vy = 0;
                    P.hitlag = 0;
                    s.p[1 - i].statKOs++;
                    Emit(Ev.KO, i, ex, ey, P.stocks);
                    if (P.stocks <= 0 && s.phase == GameState.Fight)
                    {
                        s.phase = GameState.Over;
                        s.phaseTimer = 0;
                        s.winner = 1 - i;
                        Emit(Ev.GameOver, 1 - i, 0, 0, 0);
                    }
                }
            }
        }
    }
}
