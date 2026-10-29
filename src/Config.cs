using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Sensensturm
{
    [AttributeUsage(AttributeTargets.Field)]
    public class DescAttribute : Attribute
    {
        public string Text;
        public DescAttribute(string text) { Text = text; }
    }

    // Zentrale Spielwerte für Bewegung und Kampf. Einheiten: Pixel bzw. Frames (60 Ticks pro Sekunde).
    // Beim ersten Start wird tuning.ini neben der .exe erzeugt; dort lassen sich alle Werte feinjustieren.
    // Im LAN-Spiel übernimmt der Client automatisch die Werte des Hosts.
    public static class Tuning
    {
        // ---------- Bewegung ----------
        [Desc("Maximale Laufgeschwindigkeit am Boden")] public static float RunSpeed = 9.4f;
        [Desc("Beschleunigung am Boden pro Frame")] public static float GroundAccel = 2.8f;
        [Desc("Abbremsen am Boden pro Frame ohne Eingabe")] public static float GroundDecel = 2.3f;
        [Desc("Sprintgeschwindigkeit nach einem Dash")] public static float SprintSpeed = 11.8f;
        [Desc("Startgeschwindigkeit des Dashs")] public static float DashSpeed = 19.5f;
        [Desc("Dauer des Dashs in Frames")] public static int DashFrames = 8;
        [Desc("Abklingzeit zwischen zwei Dashs")] public static int DashCooldown = 14;
        [Desc("Schwerkraft pro Frame")] public static float Gravity = 0.66f;
        [Desc("Sprungkraft vom Boden")] public static float JumpForce = 17.4f;
        [Desc("Sprungkraft der Luftsprünge")] public static float AirJumpForce = 15.6f;
        [Desc("Faktor auf die Steiggeschwindigkeit, wenn Springen früh losgelassen wird (variable Sprunghöhe)")] public static float JumpCutMultiplier = 0.45f;
        [Desc("Anzahl Luftsprünge")] public static int AirJumps = 3;
        [Desc("Frames nach Verlassen einer Kante, in denen noch ein Bodensprung möglich ist")] public static int CoyoteFrames = 5;
        [Desc("Luftkontrolle: Beschleunigung in der Luft")] public static float AirAccel = 1.45f;
        [Desc("Maximale horizontale Luftgeschwindigkeit")] public static float AirMaxSpeed = 9f;
        [Desc("Abbremsen in der Luft ohne Eingabe")] public static float AirDecel = 0.38f;
        [Desc("Maximale Fallgeschwindigkeit")] public static float MaxFallSpeed = 14.5f;
        [Desc("Fallgeschwindigkeit beim Schnellfallen")] public static float FastFallSpeed = 23f;
        [Desc("Rutschgeschwindigkeit an der Wand")] public static float WallSlideSpeed = 2.2f;
        [Desc("Horizontale Kraft des Wandsprungs")] public static float WallJumpX = 9.5f;
        [Desc("Vertikale Kraft des Wandsprungs")] public static float WallJumpY = 15.5f;
        [Desc("Wandsprünge pro Luftphase")] public static int WallJumps = 3;
        [Desc("Geschwindigkeit des Dodges")] public static float DodgeSpeed = 13.5f;
        [Desc("Dauer des Dodges in Frames")] public static int DodgeFrames = 18;
        [Desc("Unverwundbare Frames während des Dodges")] public static int DodgeInvulnFrames = 15;
        [Desc("Abklingzeit nach gerichtetem Luft-Dodge")] public static int DodgeCooldown = 50;
        [Desc("Abklingzeit nach neutralem Dodge bzw. Boden-Dodge")] public static int SpotDodgeCooldown = 30;
        [Desc("Ab diesem Frame darf ein Dodge mit einer Aktion abgebrochen werden")] public static int DodgeActionableFrame = 13;
        [Desc("Landeverzögerung nach leichtem Luftangriff")] public static int LandingLagLight = 5;
        [Desc("Landeverzögerung nach schwerem Luftangriff")] public static int LandingLagHeavy = 14;
        [Desc("Eingabepuffer in Frames")] public static int InputBufferFrames = 6;

        // ---------- Kampf ----------
        [Desc("Globaler Knockback-Multiplikator")] public static float KnockbackScale = 1f;
        [Desc("Luftwiderstand im Knockback pro Frame (1 = keiner)")] public static float KnockbackDrag = 0.982f;
        [Desc("Schwerkraft-Faktor im Hitstun (kleiner = schwebender, längere Luft-Combos)")] public static float HitstunGravity = 0.55f;
        [Desc("Hitstun Grundwert")] public static float HitstunBase = 7f;
        [Desc("Hitstun pro Knockback-Einheit")] public static float HitstunPerKnockback = 1.75f;
        [Desc("Maximaler Hitstun")] public static int HitstunMax = 55;
        [Desc("Ab diesem Knockback wirbelt der Getroffene (Tumble)")] public static float TumbleThreshold = 11f;
        [Desc("Trefferstopp Grundwert")] public static float HitlagBase = 3f;
        [Desc("Trefferstopp pro Schadenspunkt")] public static float HitlagPerDamage = 0.3f;
        [Desc("Frames nach einem Treffer, ab denen gecancelt werden darf")] public static int ComboCancelDelay = 3;
        [Desc("Länge des Combo-Eingabefensters nach einem Treffer")] public static int ComboWindow = 18;
        [Desc("Perfektes Paradefenster in Frames")] public static int ParryWindow = 8;
        [Desc("Betäubung des Angreifers nach erfolgreicher Parade")] public static int ParryStunFrames = 44;
        [Desc("Unverwundbarkeit des Verteidigers nach Parade")] public static int ParryInvulnFrames = 10;
        [Desc("Abklingzeit nach verfehlter Parade (Schild gleich wieder losgelassen)")] public static int ParryWhiffCooldown = 40;
        [Desc("Abklingzeit nach normalem Blocken")] public static int ShieldCooldown = 22;
        [Desc("Maximale Schildenergie")] public static float ShieldMax = 100f;
        [Desc("Schildregeneration pro Frame")] public static float ShieldRegen = 0.3f;
        [Desc("Schildverbrauch pro Frame beim Halten")] public static float ShieldDrain = 0.22f;
        [Desc("Schildschaden pro Angriffsschaden")] public static float ShieldDamageMult = 2.2f;
        [Desc("Zusätzlicher Schildschaden-Faktor schwerer Angriffe")] public static float HeavyShieldMult = 1.8f;
        [Desc("Anteil des Schadens, der trotz Block durchkommt")] public static float BlockChipMult = 0.15f;
        [Desc("Blockstun Grundwert")] public static float BlockstunBase = 6f;
        [Desc("Betäubung nach Schildbruch")] public static int ShieldBreakFrames = 100;
        [Desc("Maximale Dauer des Schildes in der Luft")] public static int AirShieldMax = 30;
        [Desc("Zusatzschaden bei voll aufgeladenem Angriff (1 = +100%)")] public static float ChargeDamageBonus = 0.8f;
        [Desc("Schadensfaktor, wenn man beim Aufladen getroffen wird (Risiko)")] public static float ChargeVulnerability = 1.25f;
        [Desc("Perfektes Combo-Fenster: so viele Frames nach dem Cancel-Start zählt ein Tastendruck als PERFEKT")] public static int PerfectWindow = 6;
        [Desc("Zusätzlicher Hitstun bei perfektem Combo-Timing")] public static int PerfectHitstunBonus = 12;
        [Desc("Dauer des Verfolgungs-Dodges nach einem Treffer")] public static int ChaseDodgeFrames = 12;
        [Desc("Geschwindigkeit des Verfolgungs-Dodges")] public static float ChaseDodgeSpeed = 18f;
        [Desc("Abklingzeit des Verfolgungs-Dodges")] public static int ChaseDodgeCooldown = 24;
        [Desc("Maximale Haltezeit beim Greifen, danach automatischer Wurf")] public static int GrabHoldMax = 40;
        [Desc("Kantenhilfe: so viele Pixel unter der Oberkante wird man auf die Bühne gezogen")] public static float LedgeAssist = 34f;
        [Desc("Ultimate: benötigte Leistenpunkte")] public static float UltMax = 100f;
        [Desc("Ultimate: Leistenpunkte pro verursachtem Schadenspunkt")] public static float UltGainDeal = 1f;
        [Desc("Ultimate: Leistenpunkte pro erlittenem Schadenspunkt")] public static float UltGainTake = 0.6f;
        [Desc("Ultimate: so viele Frames wird der Gegner beim Auslösen eingefroren (Zeitstopp)")] public static int UltFreeze = 20;
        [Desc("Version dieser Datei (nicht ändern)")] public static int TuningVersion = CurrentVersion;
        public const int CurrentVersion = 2;
        [Desc("Frames bis zum Respawn nach einem KO")] public static int RespawnDelay = 70;
        [Desc("Unverwundbarkeit nach dem Respawn")] public static int RespawnInvuln = 110;

        static FieldInfo[] fields;
        public static FieldInfo[] Fields
        {
            get
            {
                if (fields == null)
                {
                    // ohne Konstanten (CurrentVersion): die lassen sich nicht setzen und brachen früher Laden/Übertragen mittendrin ab
                    fields = Array.FindAll(typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Static), f => !f.IsLiteral);
                    Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                }
                return fields;
            }
        }

        public static string FilePath { get { return Path.Combine(Application.StartupPath, "tuning.ini"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { Save(); return; }
                // Alte Datei mit veralteten Werten -> durch neue Standardwerte ersetzen
                if (!File.ReadAllText(FilePath).Contains("TuningVersion = " + CurrentVersion)) { Save(); return; }
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    foreach (FieldInfo f in Fields)
                    {
                        if (f.Name != key) continue;
                        if (f.FieldType == typeof(float))
                        {
                            float v;
                            if (float.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) f.SetValue(null, v);
                        }
                        else if (f.FieldType == typeof(int))
                        {
                            int v;
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) f.SetValue(null, v);
                        }
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Sensensturm - zentrale Bewegungs- und Kampfwerte");
                sb.AppendLine("# Einheiten: Pixel pro Frame bzw. Frames (60 pro Sekunde).");
                sb.AppendLine("# Datei löschen, um die Standardwerte wiederherzustellen.");
                sb.AppendLine("# Im Menü 'Einstellungen > Spielwerte neu laden' werden Änderungen ohne Neustart übernommen.");
                sb.AppendLine();
                FieldInfo[] ordered = Array.FindAll(typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Static), f => !f.IsLiteral);
                Array.Sort(ordered, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (FieldInfo f in ordered)
                {
                    var d = (DescAttribute)Attribute.GetCustomAttribute(f, typeof(DescAttribute));
                    if (d != null) sb.AppendLine("# " + d.Text);
                    object v = f.GetValue(null);
                    string s = v is float ? ((float)v).ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
                    sb.AppendLine(f.Name + " = " + s);
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        public static byte[] ToBytes()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            foreach (FieldInfo f in Fields)
            {
                if (f.FieldType == typeof(float)) w.Write((float)f.GetValue(null));
                else w.Write((int)f.GetValue(null));
            }
            return ms.ToArray();
        }

        public static void FromBytes(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            foreach (FieldInfo f in Fields)
            {
                if (f.FieldType == typeof(float)) f.SetValue(null, r.ReadSingle());
                else f.SetValue(null, r.ReadInt32());
            }
        }
    }

    public static class Settings
    {
        public static int Volume = 8;
        public static bool ScreenShake = true;
        public static bool Fullscreen = false;
        public static bool ShowStats = true;
        public static bool ShowHitboxes = false;
        public static string PlayerName = "Spieler";
        public static int Port = 7777;
        public static string LastIp = "";
        public static int Stocks = 3;
        public static int MatchMinutes = 5;
        public static int P1Char = 0;
        public static int P2Char = 1;
        public static int Stage = 0;    // Arena, siehe StageDef
        public static int Opponent = 0; // 0 = Spieler 2, 1..3 = CPU leicht/mittel/schwer
        public static int BindVersion = 2;
        // Training: Kämpfer der Puppe, Verhalten (Game.DummyNames), Startschaden, geschaffte Combo-Aufgaben (Bits, ComboTrial.All)
        public static int TrainChar = 1, TrainDummy = 0, TrainDamage = 0, TrainTrials = 0;
        public static bool TrainReset = true, TrainUlt = false;

        // Bind[spieler, aktion, slot]
        public static Keys[,,] Bind = new Keys[2, Btn.Count, 2];

        public static string FilePath { get { return Path.Combine(Application.StartupPath, "settings.ini"); } }

        public static void DefaultBindings(int p)
        {
            Keys[] a, b;
            if (p == 0)
            {
                a = new Keys[] { Keys.A, Keys.D, Keys.W, Keys.S, Keys.Space, Keys.J, Keys.K, Keys.L, Keys.H, Keys.E, Keys.Q };
                b = new Keys[] { Keys.None, Keys.None, Keys.None, Keys.None, Keys.W, Keys.LButton, Keys.MButton, Keys.ShiftKey, Keys.RButton, Keys.None, Keys.None };
            }
            else
            {
                a = new Keys[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad5, Keys.NumPad4, Keys.NumPad6 };
                b = new Keys[] { Keys.None, Keys.None, Keys.None, Keys.None, Keys.Up, Keys.OemPeriod, Keys.OemMinus, Keys.End, Keys.PageDown, Keys.Oemcomma, Keys.None };
            }
            for (int i = 0; i < Btn.Count; i++) { Bind[p, i, 0] = a[i]; Bind[p, i, 1] = b[i]; }
        }

        public static void Load()
        {
            DefaultBindings(0);
            DefaultBindings(1);
            try
            {
                string n = Environment.UserName;
                if (!string.IsNullOrEmpty(n)) PlayerName = n.Length > 14 ? n.Substring(0, 14) : n;
            }
            catch { }
            int fileBindVersion = 0;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (raw.StartsWith("BindVersion")) { int.TryParse(raw.Substring(eq + 1).Trim(), out fileBindVersion); continue; }
                    if (eq < 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    if (key.StartsWith("Bind."))
                    {
                        string[] parts = key.Split('.');
                        if (parts.Length != 3) continue;
                        int p = parts[1] == "P2" ? 1 : 0;
                        int act = Array.IndexOf(Btn.Ids, parts[2]);
                        if (act < 0) continue;
                        string[] ks = val.Split(',');
                        for (int s = 0; s < 2 && s < ks.Length; s++)
                        {
                            Keys k;
                            if (Enum.TryParse(ks[s].Trim(), out k)) Bind[p, act, s] = k;
                        }
                        continue;
                    }
                    FieldInfo f = typeof(Settings).GetField(key, BindingFlags.Public | BindingFlags.Static);
                    if (f == null) continue;
                    if (f.FieldType == typeof(int)) { int v; if (int.TryParse(val, out v)) f.SetValue(null, v); }
                    else if (f.FieldType == typeof(bool)) { bool v; if (bool.TryParse(val, out v)) f.SetValue(null, v); }
                    else if (f.FieldType == typeof(string)) f.SetValue(null, val);
                }
            }
            catch { }
            // ältere Tastenbelegung (ohne W-Sprung, Maus, Greifen) -> Standard laden
            if (fileBindVersion < 2) { DefaultBindings(0); DefaultBindings(1); }
            BindVersion = 2;
            if (Opponent < 0 || Opponent > 3) Opponent = 0; // 4 war früher die Trainingspuppe (jetzt eigener Modus)
            if (TrainChar < 0 || TrainChar >= CharDef.Count) TrainChar = 1;
            TrainDummy = Math.Max(0, Math.Min(6, TrainDummy));
            TrainDamage = Math.Max(0, Math.Min(300, TrainDamage));
            Stage = StageDef.Valid(Stage);
            if (P1Char < 0 || P1Char >= CharDef.Count) P1Char = 0;
            if (P2Char < 0 || P2Char >= CharDef.Count) P2Char = CharDef.Next(P1Char, 1, P1Char);
            Volume = Math.Max(0, Math.Min(10, Volume));
            Stocks = Math.Max(1, Math.Min(9, Stocks));
            if (Port < 1024 || Port > 65535) Port = 7777;
        }

        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType == typeof(string))
                        sb.AppendLine(f.Name + " = " + Convert.ToString(f.GetValue(null), CultureInfo.InvariantCulture));
                }
                for (int p = 0; p < 2; p++)
                    for (int i = 0; i < Btn.Count; i++)
                        sb.AppendLine("Bind." + (p == 0 ? "P1" : "P2") + "." + Btn.Ids[i] + " = " + Bind[p, i, 0] + ", " + Bind[p, i, 1]);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
