using System;

namespace Sensensturm
{
    // Skelett-Pose. Winkel in Grad, absolut im Charakterraum (Blick nach rechts):
    // 0 = vorne, 90 = unten, -90 = oben, 180 = hinten. F = vorderer Arm/Bein (Sense), B = hinterer Arm (Schild)/Bein.
    public class Pose
    {
        public float Lean, Crouch, Head, AF1, AF2, AB1, AB2, LF1, LF2, LB1, LB2, Scy, Spin;

        public Pose(float lean, float crouch, float head, float aF1, float aF2, float aB1, float aB2,
                    float lF1, float lF2, float lB1, float lB2, float scy, float spin = 0)
        {
            Lean = lean; Crouch = crouch; Head = head; AF1 = aF1; AF2 = aF2; AB1 = aB1; AB2 = aB2;
            LF1 = lF1; LF2 = lF2; LB1 = lB1; LB2 = lB2; Scy = scy; Spin = spin;
        }

        public Pose Copy() { return (Pose)MemberwiseClone(); }

        static float L(float a, float b, float t) { return a + (b - a) * t; }

        public static Pose Lerp(Pose a, Pose b, float t)
        {
            return new Pose(L(a.Lean, b.Lean, t), L(a.Crouch, b.Crouch, t), L(a.Head, b.Head, t),
                L(a.AF1, b.AF1, t), L(a.AF2, b.AF2, t), L(a.AB1, b.AB1, t), L(a.AB2, b.AB2, t),
                L(a.LF1, b.LF1, t), L(a.LF2, b.LF2, t), L(a.LB1, b.LB1, t), L(a.LB2, b.LB2, t),
                L(a.Scy, b.Scy, t), L(a.Spin, b.Spin, t));
        }

        public Pose WithLegs(float lF1, float lF2, float lB1, float lB2) { var p = Copy(); p.LF1 = lF1; p.LF2 = lF2; p.LB1 = lB1; p.LB2 = lB2; return p; }
    }

    public class AttackDef
    {
        public string Name;
        public int Startup, Active, Recovery;
        public float HX, HY, HW, HH;          // Hitbox relativ zu den Füßen, Blick nach rechts
        public float Damage, KbBase, KbGrowth, Angle;
        public bool Heavy, Air, Away, Radial, Grab;
        public int ChargeFrame, ChargeMax;
        public float ChargeScale = 1f;
        public Pose Wind, Strike, Follow;
        // Bewegung
        public float Lunge, ActiveDash, PlungeSpeed = 19.5f, RiseSpeed = 16f;
        // Super-Rüstung während der Vorlaufzeit (wird nicht unterbrochen)
        public bool Armor;
        // Projektil (-1 = keins; 0 = Pfeil, 2 = Bodenwelle)
        public int ProjKind = -1, ProjLife = 50;
        public float ProjSpeed, ProjAngle, ProjDmg, ProjKbB, ProjKbG, ProjKbAng, Recoil;
        // Spur am Fuß statt an der Waffe (Tritte)
        public bool FootTrail;
        public int Total { get { return Startup + Active + Recovery; } }
    }

    public static class Attacks
    {
        public const int NLight = 0, SLight = 1, ULight = 2, DLight = 3, NAir = 4, SAir = 5, UAir = 6, DAir = 7,
            SHeavy = 8, NHeavy = 9, DHeavy = 10, SAirHeavy = 11, DAirHeavy = 12, UAirHeavy = 13, Impact = 14, Grab = 15, Throw = 16,
            UHeavy = 17, DashAtk = 18, Ult = 19;

        public const int Count = 20;
        public static readonly AttackDef[][] Sets = new AttackDef[Weapon.Count][];
        static AttackDef[] cur;
        public static AttackDef[] All { get { return Sets[0]; } }
        public static AttackDef[] SetOf(int charId) { return Sets[CharDef.All[charId].Weapon]; }
        public static AttackDef Of(PlayerState p) { return Sets[CharDef.All[p.charId].Weapon][p.atk]; }

        const int H = 1, A = 2, AW = 4, RA = 8, GR = 16;

        static AttackDef Mk(int id, string name, int su, int ac, int rc, float hx, float hy, float hw, float hh,
                             float dmg, float kbB, float kbG, float ang, int flags)
        {
            var d = new AttackDef
            {
                Name = name, Startup = su, Active = ac, Recovery = rc, HX = hx, HY = hy, HW = hw, HH = hh,
                Damage = dmg, KbBase = kbB, KbGrowth = kbG, Angle = ang,
                Heavy = (flags & H) != 0, Air = (flags & A) != 0, Away = (flags & AW) != 0, Radial = (flags & RA) != 0, Grab = (flags & GR) != 0
            };
            cur[id] = d;
            return d;
        }

        // Beinhaltungen
        static Pose G(float lean, float cr, float head, float af1, float af2, float ab1, float ab2, float scy, float spin = 0)
        { return new Pose(lean, cr, head, af1, af2, ab1, ab2, 62, 94, 120, 100, scy, spin); }
        static Pose Ai(float lean, float cr, float head, float af1, float af2, float ab1, float ab2, float scy, float spin = 0)
        { return new Pose(lean, cr, head, af1, af2, ab1, ab2, 55, 108, 108, 140, scy, spin); }

        static Attacks()
        {
            cur = new AttackDef[Count];
            BuildScythe();
            Sets[0] = cur;
            cur = new AttackDef[Count];
            BuildHammer();
            Sets[1] = cur;
            cur = new AttackDef[Count];
            BuildBow();
            Sets[2] = cur;
            cur = new AttackDef[Count];
            BuildDaggers();
            Sets[3] = cur;
            cur = new AttackDef[Count];
            BuildStaff();
            Sets[4] = cur;
        }

        static void BuildScythe()
        {
            AttackDef d;
            // ---------------- Leichte Bodenangriffe ----------------
            d = Mk(NLight, "Schneller Hieb", 5, 3, 14, 10, -100, 112, 76, 6f, 3.6f, 0.034f, -58, 0);
            d.Wind = G(-8, 4, 0, 175, -140, 50, 10, -140);
            d.Strike = G(14, 6, 5, 15, -5, 130, 150, 0);
            d.Follow = G(18, 6, 5, 45, 50, 135, 160, 80);

            d = Mk(SLight, "Weiter Hieb", 8, 4, 18, 22, -96, 140, 66, 8f, 4.6f, 0.05f, -28, 0);
            d.Lunge = 6.5f;
            d.Wind = new Pose(-12, 6, 0, -150, -120, 40, 20, 50, 90, 125, 105, -120);
            d.Strike = new Pose(25, 10, 8, 0, 0, 140, 160, 38, 86, 138, 112, 20);
            d.Follow = new Pose(28, 10, 8, 30, 45, 145, 165, 38, 86, 138, 112, 100);

            d = Mk(ULight, "Aufwärtssichel", 7, 5, 18, -50, -195, 145, 125, 7f, 11f, 0.035f, -90, 0);
            d.Wind = G(12, 12, 10, 60, 95, 120, 140, 125);
            d.Strike = G(-5, 0, -15, -60, -80, 100, 60, -60);
            d.Follow = G(-15, 0, -20, -110, -125, 110, 70, -155);

            d = Mk(DLight, "Bodenschnitt", 6, 4, 16, 12, -42, 135, 75, 6f, 9f, 0.03f, -72, 0);
            d.Wind = new Pose(25, 16, 10, 160, 175, 60, 30, 25, 100, 140, 80, -175);
            d.Strike = new Pose(38, 22, 15, 45, 60, 150, 170, 20, 100, 145, 80, 15);
            d.Follow = new Pose(34, 22, 15, 60, 82, 150, 170, 20, 100, 145, 80, 60);

            // ---------------- Leichte Luftangriffe ----------------
            d = Mk(NAir, "Wirbel", 6, 8, 14, -78, -135, 156, 140, 7f, 4.6f, 0.04f, -50, A | RA);
            d.Wind = Ai(-10, 0, 0, 120, 150, -60, -80, 160);
            d.Strike = Ai(0, 0, 0, 0, 0, 180, 180, 0, 0);
            d.Follow = Ai(0, 0, 0, 0, 0, 180, 180, 0, 360);

            d = Mk(SAir, "Luftsichel", 7, 4, 16, 18, -110, 135, 78, 8f, 4.6f, 0.05f, -25, A);
            d.Wind = Ai(-12, 0, 0, -140, -110, 60, 30, -110);
            d.Strike = Ai(15, 0, 5, 5, 10, 140, 160, 30);
            d.Follow = Ai(20, 0, 5, 40, 62, 145, 165, 110);

            d = Mk(UAir, "Himmelsbogen", 6, 5, 16, -65, -210, 140, 115, 7f, 12f, 0.04f, -90, A);
            d.Wind = Ai(10, 0, 10, 140, 160, 60, 80, 170);
            d.Strike = Ai(-10, 0, -15, -62, -72, 110, 80, -82);
            d.Follow = Ai(-20, 0, -25, -122, -132, 120, 90, -172);

            d = Mk(DAir, "Sturzhaken", 8, 5, 18, -42, -30, 115, 92, 8f, 5f, 0.046f, 74, A);
            d.Wind = Ai(-10, 0, -10, -100, -120, 100, 60, -110);
            d.Strike = new Pose(20, 0, 15, 60, 80, 140, 130, 70, 100, 100, 120, 80);
            d.Follow = new Pose(25, 0, 15, 80, 100, 140, 130, 70, 100, 100, 120, 125);

            // ---------------- Schwere Angriffe ----------------
            d = Mk(SHeavy, "Schwerer Sensenhieb", 18, 5, 28, 22, -125, 158, 120, 16f, 7.6f, 0.108f, -35, H);
            d.ActiveDash = 10f;
            d.ChargeFrame = 10; d.ChargeMax = 40; d.ChargeScale = 0.6f;
            d.Wind = new Pose(-22, 10, -5, -165, -150, 30, 0, 50, 92, 130, 108, -100);
            d.Strike = new Pose(26, 12, 8, 10, 20, 150, 170, 35, 86, 140, 112, 30);
            d.Follow = new Pose(32, 14, 10, 50, 80, 150, 170, 35, 86, 140, 112, 122);

            d = Mk(NHeavy, "Seelenernte", 16, 7, 38, -115, -160, 270, 160, 14f, 7.2f, 0.106f, -60, H | AW);
            d.ChargeFrame = 8; d.ChargeMax = 60; d.ChargeScale = 1.25f;
            d.Wind = G(0, 14, -10, -100, -92, -80, -95, -90);
            d.Strike = G(0, 8, 0, 0, 0, 180, 180, 0, 0);
            d.Follow = G(0, 8, 0, 0, 0, 180, 180, 0, 360);

            d = Mk(DHeavy, "Erdspalter", 16, 6, 30, -145, -55, 290, 60, 14f, 7f, 0.092f, -45, H | AW);
            d.ChargeFrame = 8; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            d.Wind = G(-10, 6, -10, -112, -100, -60, -80, -95);
            d.Strike = new Pose(40, 24, 20, 70, 85, 150, 160, 25, 100, 150, 80, 88);
            d.Follow = new Pose(38, 24, 20, 75, 90, 150, 160, 25, 100, 150, 80, 100);

            d = Mk(SAirHeavy, "Todesschwung", 16, 6, 26, 18, -145, 158, 140, 15f, 7.6f, 0.1f, -30, H | A);
            d.ActiveDash = 7.5f;
            d.Wind = Ai(-25, 0, -10, -170, -160, -40, -60, -140);
            d.Strike = Ai(20, 0, 10, 10, 20, 150, 170, 40);
            d.Follow = Ai(30, 0, 10, 60, 92, 150, 170, 132);

            d = Mk(DAirHeavy, "Fallbeil", 12, 50, 20, -40, -55, 80, 90, 13f, 6f, 0.07f, 62, H | A);
            d.Wind = new Pose(-5, 0, -10, -90, -100, -95, -105, 40, 120, 115, 150, -92);
            d.Strike = new Pose(0, 0, 10, 82, 90, 98, 95, 85, 92, 95, 95, 90);
            d.Follow = d.Strike.Copy();

            d = Mk(UAirHeavy, "Aufschwung", 6, 14, 22, -32, -175, 125, 145, 11f, 6f, 0.07f, -80, H | A);
            d.Wind = Ai(10, 0, 10, 100, 120, 150, 170, 150);
            d.Strike = Ai(-10, 0, -15, -70, -80, 120, 100, -80);
            d.Follow = Ai(-15, 0, -20, -100, -132, 130, 110, -165);

            d = Mk(Impact, "Fallbeil-Aufprall", 1, 4, 18, -115, -50, 230, 52, 10f, 6.5f, 0.08f, -50, H | AW);
            d.Wind = new Pose(30, 25, 20, 70, 90, 150, 160, 20, 105, 150, 80, 95);
            d.Strike = d.Wind.Copy();
            d.Follow = d.Wind.Copy();

            // ---------------- Sensenhaken (Greifen) und Wurf ----------------
            d = Mk(Grab, "Sensenhaken", 6, 5, 24, 8, -100, 112, 92, 0f, 0f, 0f, 0, GR);
            d.Wind = G(-12, 6, -5, 150, 175, 60, 30, -150);
            d.Strike = G(18, 10, 8, 5, -5, 120, 140, 35);
            d.Follow = G(10, 10, 5, 40, 80, 120, 140, 95);

            d = Mk(Throw, "Wurf", 1, 1, 20, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0);
            d.Wind = G(22, 10, 10, -40, -70, 150, 170, -130);
            d.Strike = d.Wind.Copy();
            d.Follow = G(20, 10, 5, 40, 60, 140, 160, 60);

            // ---------------- Neue Angriffe ----------------
            // Oben + Schwer am Boden: aufladbarer Hochschlag gegen Gegner über einem
            d = Mk(UHeavy, "Mondaufgang", 14, 6, 26, -70, -235, 175, 175, 13f, 8.5f, 0.095f, -86, H);
            d.ChargeFrame = 8; d.ChargeMax = 40; d.ChargeScale = 0.8f;
            d.Wind = G(16, 18, 12, 75, 115, 120, 140, 145);
            d.Strike = G(-8, 0, -20, -78, -92, 110, 60, -78);
            d.Follow = G(-18, 0, -25, -118, -132, 115, 70, -165);

            // Leicht aus dem Dash bzw. Sprint: Angriff aus dem Lauf
            d = Mk(DashAtk, "Schnitterlauf", 6, 8, 20, 10, -102, 150, 96, 10f, 6.5f, 0.07f, -35, 0);
            d.ActiveDash = 14f;
            d.Wind = new Pose(-10, 8, 0, -150, -120, 40, 20, 40, 100, 140, 110, -120);
            d.Strike = new Pose(36, 16, 10, 0, 0, 140, 160, 20, 82, 150, 120, 12);
            d.Follow = new Pose(32, 14, 10, 35, 50, 145, 165, 25, 84, 145, 115, 95);

            // ULTIMATE: Zeitstopp, dann ein gewaltiger Sturmschnitt nach vorne
            d = Mk(Ult, "Seelensturm", 30, 10, 34, -60, -200, 330, 210, 32f, 11f, 0.15f, -42, H);
            d.ActiveDash = 16f;
            d.Wind = G(0, 16, -12, -100, -92, -80, -95, -95);
            d.Strike = new Pose(30, 12, 8, 10, 20, 150, 170, 35, 86, 140, 112, 30);
            d.Follow = new Pose(30, 12, 8, 10, 20, 150, 170, 35, 86, 140, 112, 122, 360);
        }

        static void CopyPoses(AttackDef d, int fromId, int fromSet = 0)
        {
            AttackDef s = Sets[fromSet][fromId];
            d.Wind = s.Wind.Copy(); d.Strike = s.Strike.Copy(); d.Follow = s.Follow.Copy();
        }

        // ================= HAMMER (Brokk): langsam, wuchtig, Super-Rüstung =================
        static void BuildHammer()
        {
            AttackDef d;
            d = Mk(NLight, "Steinschlag", 7, 4, 16, 10, -112, 112, 100, 8f, 4.2f, 0.045f, -58, 0); CopyPoses(d, NLight);
            d = Mk(SLight, "Seitwärtsschwung", 9, 4, 20, 20, -104, 145, 84, 10f, 5.4f, 0.06f, -30, 0); CopyPoses(d, SLight); d.Lunge = 5f;
            d = Mk(ULight, "Aufwärtshieb", 9, 5, 20, -50, -205, 150, 135, 9f, 11.5f, 0.035f, -90, 0); CopyPoses(d, ULight);
            d = Mk(DLight, "Bodenstampfer", 8, 5, 18, 10, -48, 145, 80, 8f, 9.5f, 0.03f, -74, 0); CopyPoses(d, DLight);
            d = Mk(NAir, "Hammerwirbel", 8, 10, 16, -88, -142, 176, 154, 9f, 5.4f, 0.045f, -55, A | RA); CopyPoses(d, NAir);
            d = Mk(SAir, "Luftschmetterer", 9, 4, 20, 18, -124, 145, 96, 10f, 5.4f, 0.055f, -25, A); CopyPoses(d, SAir);
            d = Mk(UAir, "Himmelshammer", 8, 5, 18, -70, -218, 150, 124, 9f, 12.5f, 0.04f, -90, A); CopyPoses(d, UAir);
            d = Mk(DAir, "Meteorhammer", 9, 5, 22, -45, -34, 112, 104, 11f, 6f, 0.06f, 80, A); CopyPoses(d, DAir);
            d = Mk(SHeavy, "Felsbrecher", 22, 6, 32, 24, -145, 172, 145, 21f, 9f, 0.125f, -38, H); CopyPoses(d, SHeavy);
            d.ChargeFrame = 12; d.ChargeMax = 40; d.ChargeScale = 0.6f; d.Armor = true; d.ActiveDash = 7f;
            d = Mk(NHeavy, "Erdbeben", 20, 8, 36, -155, -64, 310, 70, 17f, 8f, 0.11f, -58, H | AW); CopyPoses(d, DHeavy);
            d.ChargeFrame = 10; d.ChargeMax = 50; d.ChargeScale = 1f; d.Armor = true;
            d = Mk(DHeavy, "Bebenwelle", 18, 4, 30, 10, -44, 92, 48, 12f, 7f, 0.08f, -62, H); CopyPoses(d, DHeavy);
            d.ProjKind = 2; d.ProjSpeed = 9f; d.ProjLife = 55; d.ProjDmg = 10f; d.ProjKbB = 7.5f; d.ProjKbG = 0.07f; d.ProjKbAng = -66;
            d = Mk(SAirHeavy, "Kometenhammer", 18, 6, 28, 15, -152, 165, 152, 18f, 8.5f, 0.11f, -30, H | A); CopyPoses(d, SAirHeavy);
            d.Armor = true; d.ActiveDash = 6f;
            d = Mk(DAirHeavy, "Bergsturz", 14, 50, 24, -46, -62, 92, 98, 15f, 7f, 0.08f, 65, H | A); CopyPoses(d, DAirHeavy); d.PlungeSpeed = 22f;
            d = Mk(UAirHeavy, "Gipfelsturm", 8, 14, 26, -62, -182, 124, 172, 13f, 7f, 0.08f, -82, H | A); CopyPoses(d, UAirHeavy); d.RiseSpeed = 15f;
            d.Follow.Spin = 360;
            d = Mk(Impact, "Bergsturz-Aufprall", 1, 5, 20, -155, -58, 310, 62, 12f, 7f, 0.09f, -50, H | AW); CopyPoses(d, Impact);
            d = Mk(Grab, "Steingriff", 7, 5, 26, 8, -102, 104, 96, 0f, 0f, 0f, 0, GR); CopyPoses(d, Grab);
            d = Mk(Throw, "Wurf", 1, 1, 22, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0); CopyPoses(d, Throw);
            // Neue Angriffe
            d = Mk(UHeavy, "Vulkanhieb", 20, 7, 32, -60, -250, 180, 200, 17f, 9f, 0.11f, -84, H); CopyPoses(d, UHeavy);
            d.ChargeFrame = 12; d.ChargeMax = 40; d.ChargeScale = 0.6f; d.Armor = true;
            d = Mk(DashAtk, "Rammbock", 9, 10, 24, 5, -108, 120, 104, 12f, 7f, 0.08f, -32, 0); CopyPoses(d, DashAtk);
            d.ActiveDash = 12f; d.Armor = true;
            // ULTIMATE: Zeitstopp, dann zerreißt ein Hammerschlag die Bühne zu beiden Seiten
            d = Mk(Ult, "Weltenbrecher", 34, 8, 40, -330, -150, 660, 160, 34f, 12f, 0.15f, -70, H | AW); CopyPoses(d, DHeavy);
            d.Armor = true;
        }

        // ================= BOGEN (Lyra): schnell, Tritte, aufladbare Pfeile =================
        static Pose K(float lean, float cr, float head, float af1, float af2, float ab1, float ab2, float lf1, float lf2, float lb1, float lb2, float scy, float spin = 0)
        { return new Pose(lean, cr, head, af1, af2, ab1, ab2, lf1, lf2, lb1, lb2, scy, spin); }

        static void BuildBow()
        {
            AttackDef d;
            d = Mk(NLight, "Bogenstich", 4, 3, 12, 10, -98, 92, 64, 5f, 3.4f, 0.03f, -58, 0);
            d.Wind = G(-6, 4, 0, 120, 150, 120, 150, 60); d.Strike = G(14, 6, 5, -5, -5, 150, 170, 0); d.Follow = G(10, 6, 5, 10, 20, 150, 170, 10);
            d = Mk(SLight, "Wirbeltritt", 6, 4, 14, 12, -92, 112, 64, 6f, 4.3f, 0.045f, -30, 0); d.Lunge = 7f; d.FootTrail = true;
            d.Wind = K(-12, 6, -5, 70, 30, 140, 110, 25, 115, 105, 95, 30);
            d.Strike = K(-22, 4, -10, 80, 40, 160, 140, -8, -5, 100, 92, 40);
            d.Follow = K(-15, 4, -5, 75, 35, 150, 120, 20, 40, 100, 92, 35);
            d = Mk(ULight, "Mondsicheltritt", 6, 5, 15, -48, -195, 140, 125, 6f, 10.5f, 0.033f, -90, 0); d.FootTrail = true;
            d.Wind = G(15, 12, 10, 60, 40, 120, 100, 30);
            d.Strike = K(-30, 0, -20, 120, 150, 140, 170, -95, -100, 110, 100, 40);
            d.Follow = K(-35, 0, -25, 130, 160, 150, 170, -130, -150, 100, 100, 40);
            d = Mk(DLight, "Gleittritt", 5, 7, 15, 10, -44, 122, 78, 5f, 8.5f, 0.028f, -74, 0); d.ActiveDash = 9f; d.FootTrail = true;
            d.Wind = K(-10, 18, 0, 60, 40, 140, 120, 30, 110, 130, 60, 40);
            d.Strike = K(-30, 30, -10, 60, 40, 160, 170, 5, 10, 140, 40, 40);
            d.Follow = d.Strike.Copy();
            d = Mk(NAir, "Sternenrad", 5, 8, 12, -70, -128, 140, 136, 6f, 4.3f, 0.035f, -52, A | RA); d.FootTrail = true;
            d.Wind = Ai(0, 0, 0, 60, 20, 110, 80, 20);
            d.Strike = K(0, 0, 0, 0, 0, 180, 180, 20, 10, 160, 170, 0, 0);
            d.Follow = K(0, 0, 0, 0, 0, 180, 180, 20, 10, 160, 170, 0, 360);
            d = Mk(SAir, "Doppeltritt", 6, 5, 14, 12, -112, 118, 76, 7f, 4.3f, 0.045f, -25, A); d.FootTrail = true;
            d.Wind = K(-10, 0, -5, 70, 30, 140, 110, 25, 115, 60, 140, 30);
            d.Strike = K(-20, 0, -10, 80, 40, 160, 140, -8, -5, 100, 130, 40);
            d.Follow = K(-14, 0, -5, 75, 35, 150, 120, 20, 40, 100, 130, 35);
            d = Mk(UAir, "Himmelskick", 5, 5, 14, -60, -212, 132, 118, 6f, 11.5f, 0.038f, -90, A); d.FootTrail = true;
            d.Wind = K(15, 0, 10, 60, 40, 120, 100, 60, 140, 110, 150, 30);
            d.Strike = K(-30, 0, -20, 120, 150, 140, 170, -95, -100, 110, 130, 40);
            d.Follow = K(-35, 0, -25, 130, 160, 150, 170, -140, -160, 100, 130, 40);
            d = Mk(DAir, "Sturztritt", 8, 4, 16, -36, -24, 84, 82, 7f, 5f, 0.04f, 76, A); d.FootTrail = true;
            d.Wind = K(0, 0, 0, 60, 20, 110, 80, 20, 140, 40, 150, 30);
            d.Strike = K(10, 0, 15, 60, 30, 120, 100, 84, 88, 96, 92, 30);
            d.Follow = d.Strike.Copy();
            // Fernkampf
            d = Mk(SHeavy, "Sternenpfeil", 14, 2, 22, 0, 0, 0, 0, 0f, 0f, 0f, 0, H);
            d.ChargeFrame = 8; d.ChargeMax = 40; d.ProjKind = 0; d.ProjSpeed = 17f; d.ProjLife = 60; d.ProjDmg = 9f; d.ProjKbB = 6f; d.ProjKbG = 0.085f; d.ProjKbAng = -22;
            d.Wind = G(0, 6, 0, 0, 0, 175, 10, 0); d.Strike = G(-6, 6, 0, 0, 0, 160, 40, 0); d.Follow = G(-5, 6, 0, 10, 10, 150, 40, 5);
            d = Mk(NHeavy, "Mondbogen", 14, 2, 24, 0, 0, 0, 0, 0f, 0f, 0f, 0, H);
            d.ChargeFrame = 8; d.ChargeMax = 40; d.ProjKind = 0; d.ProjSpeed = 15f; d.ProjAngle = -55; d.ProjLife = 60; d.ProjDmg = 8f; d.ProjKbB = 7f; d.ProjKbG = 0.08f; d.ProjKbAng = -70;
            d.Wind = G(-10, 8, -15, -48, -48, 135, -40, -48); d.Strike = G(-14, 8, -15, -48, -48, 130, -10, -48); d.Follow = G(-8, 8, -10, -30, -30, 130, 0, -30);
            d = Mk(DHeavy, "Rankenfalle", 12, 5, 24, -125, -36, 250, 42, 9f, 8f, 0.075f, -70, H | AW); d.FootTrail = true;
            d.ChargeFrame = 6; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            d.Wind = K(10, 20, 0, 120, 150, 60, 30, 40, 120, 130, 60, 30);
            d.Strike = K(0, 28, 0, 120, 150, 60, 30, 0, 0, 180, 180, 30, 0);
            d.Follow = K(0, 28, 0, 120, 150, 60, 30, 0, 0, 180, 180, 30, 360);
            d = Mk(SAirHeavy, "Luftschuss", 12, 2, 20, 0, 0, 0, 0, 0f, 0f, 0f, 0, H | A);
            d.ProjKind = 0; d.ProjSpeed = 17f; d.ProjAngle = 12; d.ProjLife = 50; d.ProjDmg = 8f; d.ProjKbB = 6f; d.ProjKbG = 0.075f; d.ProjKbAng = -25; d.Recoil = 5f;
            d.Wind = Ai(0, 0, 0, 10, 10, 175, 15, 10); d.Strike = Ai(-8, 0, 0, 10, 10, 160, 40, 10); d.Follow = Ai(-10, 0, 0, 20, 20, 150, 40, 15);
            d = Mk(DAirHeavy, "Sternensturz", 10, 50, 18, -34, -40, 68, 80, 10f, 6f, 0.06f, 65, H | A); d.PlungeSpeed = 21f; d.FootTrail = true;
            d.Wind = K(0, 0, 0, 60, 20, 110, 80, 20, 140, 40, 150, 30);
            d.Strike = K(-20, 0, -10, -120, -140, -60, -80, 80, 80, 60, 90, 20);
            d.Follow = d.Strike.Copy();
            d = Mk(UAirHeavy, "Sternensprung", 5, 14, 20, -46, -160, 92, 160, 8f, 6f, 0.06f, -85, H | A); d.RiseSpeed = 17.5f; d.FootTrail = true;
            d.Wind = Ai(10, 0, 10, 100, 120, 150, 170, 40);
            d.Strike = K(-10, 0, -15, -100, -110, -80, -95, -80, -90, 110, 130, -90);
            d.Follow = K(-10, 0, -15, -100, -110, -80, -95, -80, -90, 110, 130, -90, -360);
            d = Mk(Impact, "Sternenaufprall", 1, 4, 16, -92, -44, 184, 46, 7f, 6f, 0.07f, -55, H | AW); CopyPoses(d, Impact);
            d = Mk(Grab, "Rankenseil", 9, 6, 28, 18, -96, 195, 66, 0f, 0f, 0f, 0, GR);
            d.Wind = G(-10, 4, 0, 170, 170, 120, 140, 40); d.Strike = G(12, 6, 5, -5, -5, 150, 170, 0); d.Follow = G(5, 6, 0, 30, 40, 140, 160, 20);
            d = Mk(Throw, "Wurf", 1, 1, 18, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0); CopyPoses(d, Throw);
            // Neue Angriffe
            d = Mk(UHeavy, "Sternensalto", 9, 6, 22, -40, -205, 130, 150, 10f, 9f, 0.08f, -88, H); d.FootTrail = true;
            d.ChargeFrame = 6; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            d.Wind = G(15, 14, 10, 60, 40, 120, 100, 30);
            d.Strike = K(-30, 0, -20, 120, 150, 140, 170, -95, -100, 110, 100, 40);
            d.Follow = K(-35, 0, -25, 130, 160, 150, 170, -130, -150, 100, 100, 40, -360);
            d = Mk(DashAtk, "Kometentritt", 5, 9, 18, 8, -95, 110, 70, 8f, 6f, 0.06f, -30, 0); d.ActiveDash = 15f; d.FootTrail = true;
            d.Wind = K(-12, 6, -5, 70, 30, 140, 110, 25, 115, 105, 95, 30);
            d.Strike = K(-26, 2, -10, 80, 40, 160, 140, -10, -6, 100, 92, 40);
            d.Follow = K(-18, 4, -5, 75, 35, 150, 120, 20, 40, 100, 92, 35);
            // ULTIMATE: Zeitstopp, dann ein riesiger Sternenpfeil, der Schilde zerbricht
            d = Mk(Ult, "Sternenflut", 28, 2, 30, 0, 0, 0, 0, 0f, 0f, 0f, 0, H);
            d.ProjKind = 3; d.ProjSpeed = 24f; d.ProjLife = 70; d.ProjDmg = 30f; d.ProjKbB = 11f; d.ProjKbG = 0.14f; d.ProjKbAng = -30; d.Recoil = 7f;
            d.Wind = G(-8, 10, 0, 0, 0, 175, 10, 0); d.Strike = G(-14, 10, 0, 0, 0, 160, 40, 0); d.Follow = G(-10, 8, 0, 10, 10, 150, 40, 5);
        }

        // ================= DOLCHE (Nyx): sehr schnell, kurze Reichweite, Wurfdolch =================
        static void BuildDaggers()
        {
            AttackDef d;
            d = Mk(NLight, "Doppelstich", 3, 3, 11, 10, -98, 84, 64, 4.5f, 3.4f, 0.028f, -58, 0); CopyPoses(d, NLight);
            d = Mk(SLight, "Schattenschnitt", 5, 4, 14, 12, -96, 108, 64, 6f, 4.4f, 0.045f, -30, 0); CopyPoses(d, SLight); d.Lunge = 7.5f;
            d = Mk(ULight, "Klingenwirbel", 5, 5, 15, -46, -190, 124, 118, 5.5f, 10.5f, 0.03f, -90, 0); CopyPoses(d, ULight);
            d = Mk(DLight, "Fersenschnitt", 4, 6, 14, 10, -44, 112, 76, 5f, 8.5f, 0.026f, -74, 0); CopyPoses(d, DLight, Weapon.Bow);
            d.ActiveDash = 8f; d.FootTrail = true;
            d = Mk(NAir, "Klingenrad", 5, 8, 12, -66, -128, 132, 130, 5.5f, 4.3f, 0.035f, -52, A | RA); CopyPoses(d, NAir);
            d = Mk(SAir, "Luftschnitt", 5, 4, 13, 14, -108, 112, 74, 6.5f, 4.3f, 0.045f, -25, A); CopyPoses(d, SAir);
            d = Mk(UAir, "Aufwärtsstich", 5, 4, 13, -56, -205, 124, 112, 5.5f, 11.5f, 0.036f, -90, A); CopyPoses(d, UAir);
            d = Mk(DAir, "Sturzstich", 7, 4, 15, -36, -26, 92, 86, 6.5f, 5f, 0.042f, 76, A); CopyPoses(d, DAir);
            // Wurfdolch: fliegt schnell und flach, aufladbar
            d = Mk(SHeavy, "Wurfdolch", 10, 2, 18, 0, 0, 0, 0, 0f, 0f, 0f, 0, H);
            d.ChargeFrame = 6; d.ChargeMax = 30; d.ProjKind = 4; d.ProjSpeed = 21f; d.ProjLife = 40; d.ProjDmg = 7f; d.ProjKbB = 5.5f; d.ProjKbG = 0.075f; d.ProjKbAng = -25;
            d.Wind = G(-12, 6, 0, -160, -140, 40, 20, -150); d.Strike = G(18, 8, 5, 5, 0, 140, 160, 0); d.Follow = G(14, 8, 5, 30, 40, 140, 160, 40);
            d = Mk(NHeavy, "Schattenwirbel", 12, 8, 30, -108, -150, 216, 150, 11f, 6.8f, 0.1f, -60, H | AW); CopyPoses(d, NHeavy);
            d.ChargeFrame = 6; d.ChargeMax = 40; d.ChargeScale = 1f;
            d = Mk(DHeavy, "Fußfeger", 10, 5, 26, -120, -46, 240, 50, 10f, 7f, 0.085f, -48, H | AW); CopyPoses(d, DHeavy);
            d.ChargeFrame = 6; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            d = Mk(SAirHeavy, "Schattensturz", 12, 6, 22, 14, -132, 132, 124, 11f, 7f, 0.095f, -30, H | A); CopyPoses(d, SAirHeavy);
            d.ActiveDash = 9f;
            d = Mk(DAirHeavy, "Klingenregen", 10, 50, 18, -34, -48, 70, 84, 10f, 6f, 0.06f, 62, H | A); CopyPoses(d, DAirHeavy); d.PlungeSpeed = 21f;
            d = Mk(UAirHeavy, "Schattensprung", 5, 14, 20, -42, -165, 100, 150, 8.5f, 6f, 0.06f, -82, H | A); CopyPoses(d, UAirHeavy); d.RiseSpeed = 17f;
            d = Mk(Impact, "Klingenaufprall", 1, 4, 15, -95, -46, 190, 48, 7f, 6f, 0.07f, -50, H | AW); CopyPoses(d, Impact);
            d = Mk(Grab, "Schattengriff", 5, 4, 22, 8, -98, 100, 90, 0f, 0f, 0f, 0, GR); CopyPoses(d, Grab);
            d = Mk(Throw, "Wurf", 1, 1, 18, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0); CopyPoses(d, Throw);
            d = Mk(UHeavy, "Mondklinge", 9, 6, 22, -46, -215, 140, 160, 10f, 8.5f, 0.085f, -88, H); CopyPoses(d, UHeavy);
            d.ChargeFrame = 6; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            d = Mk(DashAtk, "Schattenschritt", 4, 8, 18, 6, -100, 120, 90, 7f, 5.5f, 0.055f, -32, 0); CopyPoses(d, DashAtk); d.ActiveDash = 18f;
            // ULTIMATE: Zeitstopp, dann ein Wirbel aus Klingen quer über die Bühne
            d = Mk(Ult, "Tausend Klingen", 24, 14, 30, -60, -200, 360, 210, 30f, 11f, 0.15f, -40, H); CopyPoses(d, Ult);
            d.ActiveDash = 20f;
        }

        // ================= FLAMMENSTAB (Pyros): Feuerbälle auf Distanz, Flammenring =================
        static void BuildStaff()
        {
            AttackDef d;
            d = Mk(NLight, "Stabstoß", 5, 3, 14, 10, -104, 116, 70, 6f, 3.6f, 0.034f, -58, 0); CopyPoses(d, NLight);
            d = Mk(SLight, "Glutschwung", 8, 4, 18, 22, -98, 136, 68, 8f, 4.6f, 0.05f, -28, 0); CopyPoses(d, SLight); d.Lunge = 5f;
            d = Mk(ULight, "Funkenbogen", 7, 5, 18, -50, -195, 142, 125, 7f, 11f, 0.035f, -90, 0); CopyPoses(d, ULight);
            d = Mk(DLight, "Ascheschlag", 6, 4, 16, 12, -42, 132, 75, 6f, 9f, 0.03f, -72, 0); CopyPoses(d, DLight);
            d = Mk(NAir, "Flammenrad", 6, 8, 14, -76, -134, 152, 140, 7f, 4.6f, 0.04f, -50, A | RA); CopyPoses(d, NAir);
            d = Mk(SAir, "Glutstoß", 7, 4, 16, 18, -110, 132, 78, 8f, 4.6f, 0.05f, -25, A); CopyPoses(d, SAir);
            d = Mk(UAir, "Funkenregen", 6, 5, 16, -65, -210, 140, 115, 7f, 12f, 0.04f, -90, A); CopyPoses(d, UAir);
            d = Mk(DAir, "Aschesturz", 8, 5, 18, -42, -30, 112, 92, 8f, 5f, 0.046f, 74, A); CopyPoses(d, DAir);
            // Feuerball: fliegt gerade und langsam, aufladbar
            d = Mk(SHeavy, "Feuerball", 14, 2, 22, 0, 0, 0, 0, 0f, 0f, 0f, 0, H);
            d.ChargeFrame = 8; d.ChargeMax = 40; d.ProjKind = 5; d.ProjSpeed = 11f; d.ProjLife = 75; d.ProjDmg = 10f; d.ProjKbB = 6.5f; d.ProjKbG = 0.09f; d.ProjKbAng = -30;
            d.Wind = G(-14, 8, -5, -150, -130, 40, 20, -120); d.Strike = G(16, 8, 5, 0, -5, 150, 170, -10); d.Follow = G(12, 8, 5, 10, 10, 150, 170, 0);
            d = Mk(NHeavy, "Flammenring", 16, 7, 38, -125, -165, 250, 170, 14f, 7.2f, 0.106f, -60, H | AW); CopyPoses(d, NHeavy);
            d.ChargeFrame = 8; d.ChargeMax = 60; d.ChargeScale = 1.25f;
            d = Mk(DHeavy, "Glutteppich", 16, 6, 30, -150, -52, 300, 56, 13f, 7f, 0.09f, -50, H | AW); CopyPoses(d, DHeavy);
            d.ChargeFrame = 8; d.ChargeMax = 30; d.ChargeScale = 0.6f;
            // Feuerball schräg nach unten aus der Luft
            d = Mk(SAirHeavy, "Feuerregen", 12, 2, 22, 0, 0, 0, 0, 0f, 0f, 0f, 0, H | A);
            d.ProjKind = 5; d.ProjSpeed = 12f; d.ProjAngle = 22; d.ProjLife = 60; d.ProjDmg = 9f; d.ProjKbB = 6f; d.ProjKbG = 0.08f; d.ProjKbAng = -30; d.Recoil = 4f;
            d.Wind = Ai(-12, 0, -5, -150, -130, 40, 20, -120); d.Strike = Ai(14, 0, 10, 30, 25, 150, 170, 25); d.Follow = Ai(10, 0, 8, 35, 30, 150, 170, 30);
            d = Mk(DAirHeavy, "Meteorsturz", 12, 50, 20, -40, -55, 80, 90, 13f, 6f, 0.07f, 62, H | A); CopyPoses(d, DAirHeavy); d.PlungeSpeed = 20f;
            d = Mk(UAirHeavy, "Phönixaufstieg", 6, 14, 22, -32, -175, 125, 145, 11f, 6f, 0.07f, -80, H | A); CopyPoses(d, UAirHeavy); d.RiseSpeed = 17f;
            d = Mk(Impact, "Glutaufprall", 1, 4, 18, -130, -52, 260, 54, 11f, 6.5f, 0.08f, -50, H | AW); CopyPoses(d, Impact);
            d = Mk(Grab, "Flammengriff", 6, 5, 24, 8, -100, 112, 92, 0f, 0f, 0f, 0, GR); CopyPoses(d, Grab);
            d = Mk(Throw, "Wurf", 1, 1, 20, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0); CopyPoses(d, Throw);
            d = Mk(UHeavy, "Sonnenbogen", 14, 6, 26, -70, -235, 175, 175, 13f, 8.5f, 0.095f, -86, H); CopyPoses(d, UHeavy);
            d.ChargeFrame = 8; d.ChargeMax = 40; d.ChargeScale = 0.8f;
            d = Mk(DashAtk, "Glutsprint", 7, 8, 20, 10, -102, 140, 96, 9f, 6.5f, 0.07f, -35, 0); CopyPoses(d, DashAtk); d.ActiveDash = 13f;
            // ULTIMATE: Zeitstopp, dann eine Feuerexplosion rund um Pyros
            d = Mk(Ult, "Inferno", 30, 10, 38, -290, -280, 580, 300, 32f, 11.5f, 0.15f, -60, H | AW); CopyPoses(d, NHeavy);
        }
    }

    // Liefert die aktuelle Pose eines Kämpfers aus seinem Zustand (rein visuell, identisch auf beiden PCs).
    public static class Anim
    {
        static float S(float a) { return (float)Math.Sin(a); }
        static float C(float a) { return (float)Math.Cos(a); }
        static float EaseOut(float t) { return 1 - (1 - t) * (1 - t); }
        static float Smooth(float t) { return t * t * (3 - 2 * t); }

        public static Pose Idle(PlayerState p)
        {
            float b = S(p.anim * 0.07f);
            var pose = new Pose(4 + b, 2 + b * 1.5f, b * 2, 62, -18 + b * 3, 105, 72, 80, 97, 102, 92, -72 + b * 3);
            if (p.landAnim > 0) pose.Crouch += p.landAnim * 2.2f;
            return pose;
        }

        public static Pose AirPose(PlayerState p)
        {
            Pose up = new Pose(0, 0, -6, 35, -35, 125, 70, 58, 112, 115, 135, -62);
            Pose down = new Pose(-4, 0, 6, 20, -30, 150, 110, 82, 102, 100, 118, -80);
            float t = Math.Max(0f, Math.Min(1f, (p.vy + 6f) / 12f));
            Pose r = Pose.Lerp(up, down, t);
            if (p.airJumpTime < 22)
            {
                float k = p.airJumpTime / 22f;
                r.Spin = Smooth(k) * 360f;
                Pose tuck = new Pose(10, 0, 10, 60, 10, 80, 30, 20, 140, 60, 160, -40);
                r = Pose.Lerp(r, tuck, S(k * 3.14159f));
                r.Spin = Smooth(k) * 360f;
            }
            return r;
        }

        // Waffenhaltung je Typ außerhalb von Angriffen
        static Pose Weaponize(PlayerState p, Pose r)
        {
            int w = CharDef.All[p.charId].Weapon;
            if (w == Weapon.Hammer) r.Scy -= 32;
            else if (w == Weapon.Bow) r.Scy = r.AF2 + 15;
            else if (w == Weapon.Daggers) r.Scy = r.AF2 - 20; // Klinge zeigt nach vorne
            else if (w == Weapon.Staff) r.Scy -= 20;
            return r;
        }

        public static Pose Get(PlayerState p)
        {
            Pose r = GetBase(p);
            return p.state == St.Attack ? r : Weaponize(p, r);
        }

        static Pose GetBase(PlayerState p)
        {
            switch (p.state)
            {
                case St.Idle:
                    return Idle(p);
                case St.Run:
                    {
                        float ph = p.runPhase;
                        float sp = Math.Min(1f, Math.Abs(p.vx) / Tuning.RunSpeed);
                        float sprint = p.sprint ? 1f : 0f;
                        float lean = 10 + 8 * sp + 8 * sprint;
                        float th = 34 * sp + 8 * sprint;
                        var r = new Pose(lean, 3 + 2 * Math.Abs(S(ph * 2)), 0,
                            65 + 10 * S(ph), 10 - 10 * S(ph),
                            95 - 38 * S(ph) * sp, 60 - 30 * S(ph) * sp,
                            90 + th * S(ph), 90 + th * S(ph) + 15 + 40 * Math.Max(0, -S(ph + 0.9f)) * sp,
                            90 - th * S(ph), 90 - th * S(ph) + 15 + 40 * Math.Max(0, S(ph + 0.9f)) * sp,
                            -150 + 8 * S(ph));
                        if (sp < 0.15f) r = Pose.Lerp(Idle(p), r, sp / 0.15f);
                        return r;
                    }
                case St.Dash:
                    return new Pose(28, 8, 8, 120, 150, 140, 170, 30, 70, 150, 120, -165);
                case St.Air:
                    return AirPose(p);
                case St.Wall:
                    return new Pose(-12, 4, 5, 40, -30, 175, 175, 60, 130, 150, 100, -60);
                case St.Shield:
                    {
                        bool parry = p.parryOk && p.stateTime <= Tuning.ParryWindow;
                        float push = parry ? 1f - p.stateTime / (float)Tuning.ParryWindow : 0f;
                        return new Pose(-2 + 6 * push, 9, -4, 115, 75, 12 - 8 * push, -15, 60, 96, 122, 100, -125);
                    }
                case St.Dodge:
                    return new Pose(-18, 10, -8, 100, 60, 120, 80, 70, 105, 125, 110, -130);
                case St.AirDodge:
                    return new Pose(12, 0, 12, 80, 20, 100, 40, 40, 130, 80, 150, -50);
                case St.Hitstun:
                    {
                        var r = new Pose(-26, 0, -22, -120, -150, -60, -95, 42, 72, 130, 160, -150);
                        if (p.tumble && !p.onGround) r.Spin = -p.anim * 22f;
                        return r;
                    }
                case St.Parried:
                    {
                        float w = S(p.stateTime * 0.5f) * 6;
                        return new Pose(-30 + w, 4, -25, -130, -160, -70, -110, 70, 95, 115, 100, -170 + w);
                    }
                case St.Broken:
                    {
                        float w = S(p.stateTime * 0.12f) * 14;
                        return new Pose(20 + w, 14, 30 + w, 100, 110, 85, 95, 75, 100, 105, 95, 60);
                    }
                case St.Clank:
                    return new Pose(-22, 6, -12, -140, -150, 140, 120, 60, 95, 120, 100, -155);
                case St.Landing:
                    {
                        var r = Idle(p);
                        r.Crouch += 14;
                        r.Lean += 10;
                        return r;
                    }
                case St.Respawn:
                    {
                        float b = S(p.anim * 0.08f) * 4;
                        return new Pose(0, 0, -5, -20 + b, -40, 170, 140, 80, 110, 100, 120, -90 + b);
                    }
                case St.Grabbing:
                    return new Pose(-10, 10, -6, 25, 55, 120, 140, 55, 96, 125, 100, 75);
                case St.Grabbed:
                    {
                        float w = S(p.anim * 0.9f) * 5;
                        return new Pose(20 + w, 4, 25, 110, 140, 100, 130, 70, 110, 110, 120, 100);
                    }
                case St.Attack:
                    return AttackPose(p);
            }
            return Idle(p);
        }

        public static void AttackPhase(PlayerState p, out int phase, out float t)
        {
            AttackDef d = Attacks.Of(p);
            int f = p.atkFrame;
            if (f < d.Startup) { phase = 0; t = f / (float)Math.Max(1, d.Startup); }
            else if (f < d.Startup + d.Active) { phase = 1; t = (f - d.Startup + 1) / (float)d.Active; }
            else { phase = 2; t = (f - d.Startup - d.Active) / (float)Math.Max(1, d.Recovery); }
        }

        static Pose AttackPose(PlayerState p)
        {
            AttackDef d = Attacks.Of(p);
            Pose rest = Weaponize(p, p.onGround ? Idle(p) : AirPose(p));
            rest.Spin = 0;
            int ph; float t;
            AttackPhase(p, out ph, out t);
            if (ph == 0)
            {
                Pose r = Pose.Lerp(rest, d.Wind, EaseOut(Math.Min(1f, t * 1.3f)));
                if (p.charge > 0)
                {
                    float sh = p.chargeFull ? 2.5f : 1.2f;
                    r.Lean += S(p.anim * 1.7f) * sh;
                    r.Scy += S(p.anim * 2.3f) * sh * 2;
                }
                return r;
            }
            if (ph == 1)
            {
                if (p.atk == Attacks.DAirHeavy) return d.Strike;
                return Pose.Lerp(d.Strike, d.Follow, EaseOut(t));
            }
            Pose f = d.Follow.Copy();
            f.Spin = f.Spin % 360f;
            return Pose.Lerp(f, rest, Smooth(t));
        }
    }
}
