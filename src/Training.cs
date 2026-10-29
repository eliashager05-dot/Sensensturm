using System;
using System.Collections.Generic;

namespace Sensensturm
{
    // Combo-Aufgabe im Training: die Angriffe in dieser Reihenfolge als eine einzige Combo auf die Puppe bringen.
    // Je Schritt sind mehrere Angriffe erlaubt (z. B. Boden- oder Luftvariante nach einem Verfolgungs-Dodge).
    public class ComboTrial
    {
        public string Name;   // wie in der Combo-Übersicht (dort steht auch die Tastenfolge)
        public int Weapon;
        public int[][] Steps; // Angriffs-Indizes (Attacks.*), Attacks.Throw = Wurf aus dem Griff

        static ComboTrial T(int w, string name, params int[][] steps) { return new ComboTrial { Weapon = w, Name = name, Steps = steps }; }
        static int[] S(params int[] a) { return a; }

        // Reihenfolge nicht ändern: der Index ist das Bit in Settings.TrainTrials (geschaffte Aufgaben)
        public static readonly ComboTrial[] All =
        {
            T(Sensensturm.Weapon.Scythe, "Erster Schnitt", S(Attacks.NLight), S(Attacks.SLight)),
            T(Sensensturm.Weapon.Scythe, "Aufwärtsspirale", S(Attacks.NLight), S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Scythe, "Bodenschnitt-Kette", S(Attacks.DLight), S(Attacks.SAir), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Scythe, "Haken & Wurf", S(Attacks.Throw), S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Scythe, "Schnitter-Jagd", S(Attacks.SLight), S(Attacks.SLight, Attacks.SAir), S(Attacks.SHeavy, Attacks.SAirHeavy)),
            T(Sensensturm.Weapon.Scythe, "Himmelsleiter", S(Attacks.ULight), S(Attacks.UAir), S(Attacks.UAir), S(Attacks.UAir), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Hammer, "Steinschlag-Kette", S(Attacks.NLight), S(Attacks.SLight)),
            T(Sensensturm.Weapon.Hammer, "Gipfelstürmer", S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Hammer, "Steingriff-Wurf", S(Attacks.Throw), S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Hammer, "Felsbrecher", S(Attacks.NLight), S(Attacks.SHeavy)),
            T(Sensensturm.Weapon.Bow, "Trittwirbel", S(Attacks.NLight), S(Attacks.SLight)),
            T(Sensensturm.Weapon.Bow, "Mondaufstieg", S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Bow, "Gleitfalle", S(Attacks.DLight), S(Attacks.SAir)),
            T(Sensensturm.Weapon.Bow, "Rankenwurf", S(Attacks.Throw), S(Attacks.ULight), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Bow, "Pfeilhagel", S(Attacks.SLight), S(Attacks.SHeavy)),
            T(Sensensturm.Weapon.Hammer, "Himmelsleiter", S(Attacks.ULight), S(Attacks.UAir), S(Attacks.UAir), S(Attacks.UAir)),
            T(Sensensturm.Weapon.Bow, "Himmelsleiter", S(Attacks.ULight), S(Attacks.UAir), S(Attacks.UAir), S(Attacks.UAir), S(Attacks.UAir)),
        };

        public static List<ComboTrial> For(int weapon) { return new List<ComboTrial>(Array.FindAll(All, t => t.Weapon == weapon)); }
        public int Bit { get { return 1 << Array.IndexOf(All, this); } }

        // Anzeigename eines Schritts für diese Waffe ("Wurf" für den Griff)
        public string StepName(int i)
        {
            var n = new List<string>();
            foreach (int a in Steps[i]) n.Add(a == Attacks.Throw ? "Wurf" : Attacks.Sets[Weapon][a].Name);
            return string.Join(" / ", n.ToArray());
        }
    }

    // Auswertung für den Trainingsmodus (Spieler = Platz 0, Puppe = Platz 1), nach jedem Simulationsschritt aufgerufen:
    // letzter Treffer mit Bilddaten, gemessener Frame-Vorteil, Combo-Schaden, Fortschritt der Combo-Aufgabe.
    public class TrainingTracker
    {
        public bool HasHit;
        public string LastName = "";
        public int LastStartup, LastActive, LastTotal; // 0 = keine Bilddaten (Wurf)
        public float LastDamage;
        // Frame-Vorteil: wie viele Frames der Spieler nach dem Treffer früher handeln kann als die Puppe (gemessen, nicht geschätzt)
        public int Advantage;
        public bool AdvKnown, AdvPending;
        public int ComboHits, BestHits;
        public float ComboDamage, BestDamage;
        public ComboTrial Trial;
        public int TrialStep;
        public bool JustCompleted; // nur im Schritt, in dem die Aufgabe geschafft wurde
        public int CompleteFlash;  // Anzeige "geschafft" (Frames)

        int curAtk = -1, lastAtkFrame = int.MaxValue;
        float prevDamage;
        int advT, advA, advB;

        public void Clear()
        {
            HasHit = AdvKnown = AdvPending = false;
            BestHits = 0; BestDamage = 0;
            ComboHits = 0; ComboDamage = 0;
            TrialStep = 0; CompleteFlash = 0;
            curAtk = -1; lastAtkFrame = int.MaxValue;
        }

        // nach einem Neustart der Positionen oder wenn das Spiel den Schaden der Puppe von außen setzt
        public void Sync(GameState s) { prevDamage = s.p[1].damage; }

        public void SetTrial(ComboTrial t) { Trial = t; TrialStep = 0; CompleteFlash = 0; }

        // nach Treffer/Combo-Ende wieder handlungsfähig (kein Angriff, kein Hitstun, keine Landeverzögerung)
        static bool Free(PlayerState p)
        {
            return p.hitlag == 0 && (p.state == St.Idle || p.state == St.Run || p.state == St.Air || p.state == St.Wall);
        }

        public void Step(GameState s, List<GameEvent> evs)
        {
            PlayerState A = s.p[0], B = s.p[1];
            JustCompleted = false;
            if (CompleteFlash > 0) CompleteFlash--;

            // zuletzt begonnenen Angriff merken (Projektile treffen erst später, zählen aber für diesen Angriff)
            if (A.state == St.Attack && A.atk >= 0)
            {
                if (A.atk != curAtk || A.atkFrame < lastAtkFrame) curAtk = A.atk;
                lastAtkFrame = A.atkFrame;
            }
            else lastAtkFrame = int.MaxValue;

            bool thrown = false;
            int hits = 0;
            if (evs != null)
                foreach (GameEvent e in evs)
                {
                    if (e.Type == Ev.Throw && e.Player == 0) thrown = true;
                    if (e.Type == Ev.Hit && e.Player == 1) hits++;
                }
            float dmg = Math.Max(0, B.damage - prevDamage);
            prevDamage = B.damage;

            if (hits > 0)
            {
                int src = thrown ? Attacks.Throw : curAtk;
                AttackDef d = !thrown && src >= 0 ? Attacks.SetOf(A.charId)[src] : null;
                HasHit = true;
                LastName = thrown ? "Wurf" : d != null ? d.Name : "?";
                LastStartup = d != null ? d.Startup : 0;
                LastActive = d != null ? d.Active : 0;
                LastTotal = d != null ? d.Total : 0;
                LastDamage = dmg;
                bool fresh = A.combo <= 1;
                ComboHits = Math.Max(1, A.combo);
                ComboDamage = fresh ? dmg : ComboDamage + dmg;
                if (ComboHits > BestHits || (ComboHits == BestHits && ComboDamage > BestDamage)) { BestHits = ComboHits; BestDamage = ComboDamage; }
                AdvPending = true; AdvKnown = false; advT = 0; advA = advB = -1;
                TrialHit(src, fresh);
            }
            else if (AdvPending)
            {
                advT++;
                if (advA < 0 && Free(A)) advA = advT;
                if (advB < 0 && Free(B)) advB = advT;
                if (B.state == St.KO || B.state == St.Respawn || advT > 600) AdvPending = false;
                else if (advA >= 0 && advB >= 0) { Advantage = advB - advA; AdvKnown = true; AdvPending = false; }
            }

            // Combo vorbei (Puppe wieder frei), bevor die Aufgabe fertig war -> von vorne
            if (Trial != null && TrialStep > 0 && TrialStep < Trial.Steps.Length && hits == 0 &&
                B.hitlag == 0 && B.state != St.Hitstun && B.state != St.Grabbed)
                TrialStep = 0;
        }

        void TrialHit(int src, bool fresh)
        {
            if (Trial == null) return;
            int n = Trial.Steps.Length;
            if (fresh) TrialStep = 0;
            else if (TrialStep >= n) return; // schon geschafft, weitere Treffer derselben Combo zählen nicht
            if (Array.IndexOf(Trial.Steps[TrialStep], src) >= 0) TrialStep++;
            else TrialStep = Array.IndexOf(Trial.Steps[0], src) >= 0 ? 1 : 0;
            if (TrialStep >= n) { JustCompleted = true; CompleteFlash = 150; }
        }
    }
}
