using System;
using System.Drawing;

namespace Sensensturm
{
    public static class Weapon
    {
        public const int Scythe = 0, Hammer = 1, Bow = 2, Daggers = 3, Staff = 4;
        public const int Count = 5;
        public static readonly string[] Names = { "Sense", "Hammer", "Bogen", "Dolche", "Flammenstab" };
    }

    public static class Body
    {
        public const int Skeleton = 0, Golem = 1, Huntress = 2;
    }

    // Spielbare Charaktere. Werte sind Multiplikatoren auf die zentralen Werte aus tuning.ini.
    public class CharDef
    {
        public string Name, Title, Desc, Special;
        public int Weapon, Body;
        public float Speed = 1f, Jump = 1f, Grav = 1f, Weight = 1f; // Weight = Faktor auf erhaltenen Knockback (kleiner = schwerer)
        public int StatSpeed, StatPower, StatWeight, StatRange;       // 1..5, nur für die Anzeige
        public Color Bone, Shade, Outline, Glow, Eye, Shaft, Blade, Edge, ShieldFace, Rim, Skin;
        // Kleidung und Ausrüstung (Skelett: Umhang, Lendentuch, Gürtel, Wickel; Jägerin: Kapuze, Weste, Stiefel, Haare)
        public Color Cloth, ClothDark, Leather, LeatherDark, Hair, Trim, EyeAura;

        public string TypeName { get { return Sensensturm.Weapon.Names[Weapon]; } }

        public static readonly CharDef[] All =
        {
            new CharDef
            {
                Name = "Lila Skelett", Title = "Der Seelenschnitter", Weapon = Sensensturm.Weapon.Scythe, Body = Sensensturm.Body.Skeleton,
                Desc = "Allrounder mit großer Reichweite und flüssigen Luft-Combos.",
                Special = "Aufladbare Seelenernte, Fallbeil-Sturz",
                StatSpeed = 3, StatPower = 3, StatWeight = 3, StatRange = 4,
                Bone = Color.FromArgb(238, 230, 210), Shade = Color.FromArgb(184, 172, 150), Outline = Color.FromArgb(28, 20, 38),
                Glow = Color.FromArgb(200, 150, 255), Eye = Color.FromArgb(232, 250, 255), Shaft = Color.FromArgb(74, 54, 40),
                Blade = Color.FromArgb(150, 146, 140), Edge = Color.FromArgb(228, 226, 218), ShieldFace = Color.FromArgb(84, 60, 128), Rim = Color.FromArgb(214, 204, 182),
                Skin = Color.FromArgb(238, 230, 210),
                Cloth = Color.FromArgb(96, 66, 146), ClothDark = Color.FromArgb(56, 44, 78), Leather = Color.FromArgb(122, 82, 52),
                LeatherDark = Color.FromArgb(34, 30, 40), Trim = Color.FromArgb(196, 168, 120), EyeAura = Color.FromArgb(70, 190, 255)
            },
            new CharDef
            {
                Name = "Blaues Skelett", Title = "Der Frostschnitter", Weapon = Sensensturm.Weapon.Scythe, Body = Sensensturm.Body.Skeleton,
                Desc = "Allrounder mit großer Reichweite und flüssigen Luft-Combos.",
                Special = "Aufladbare Seelenernte, Fallbeil-Sturz",
                StatSpeed = 3, StatPower = 3, StatWeight = 3, StatRange = 4,
                Bone = Color.FromArgb(230, 232, 228), Shade = Color.FromArgb(170, 176, 178), Outline = Color.FromArgb(14, 22, 40),
                Glow = Color.FromArgb(110, 225, 255), Eye = Color.FromArgb(230, 255, 255), Shaft = Color.FromArgb(58, 50, 46),
                Blade = Color.FromArgb(146, 152, 160), Edge = Color.FromArgb(226, 236, 242), ShieldFace = Color.FromArgb(36, 76, 140), Rim = Color.FromArgb(200, 210, 214),
                Skin = Color.FromArgb(230, 232, 228),
                Cloth = Color.FromArgb(40, 86, 150), ClothDark = Color.FromArgb(34, 46, 70), Leather = Color.FromArgb(104, 78, 60),
                LeatherDark = Color.FromArgb(28, 32, 42), Trim = Color.FromArgb(180, 190, 196), EyeAura = Color.FromArgb(110, 240, 255)
            },
            new CharDef
            {
                Name = "Brokk", Title = "Der Steinwächter", Weapon = Sensensturm.Weapon.Hammer, Body = Sensensturm.Body.Golem,
                Desc = "Langsamer, schwerer Golem. Wenige Treffer reichen für ein K.O.",
                Special = "Super-Rüstung beim Ausholen, Bebenwelle über den Boden",
                Speed = 0.86f, Jump = 0.93f, Grav = 1.12f, Weight = 0.82f,
                StatSpeed = 2, StatPower = 5, StatWeight = 5, StatRange = 3,
                Bone = Color.FromArgb(150, 140, 132), Shade = Color.FromArgb(104, 96, 90), Outline = Color.FromArgb(30, 22, 20),
                Glow = Color.FromArgb(255, 150, 60), Eye = Color.FromArgb(255, 236, 170), Shaft = Color.FromArgb(96, 64, 42),
                Blade = Color.FromArgb(128, 132, 146), Edge = Color.FromArgb(255, 140, 50), ShieldFace = Color.FromArgb(112, 76, 50), Rim = Color.FromArgb(255, 196, 120),
                Skin = Color.FromArgb(150, 140, 132)
            },
            new CharDef
            {
                Name = "Lyra", Title = "Die Sternenschützin", Weapon = Sensensturm.Weapon.Bow, Body = Sensensturm.Body.Huntress,
                Desc = "Schnelle, leichte Jägerin. Kontrolliert den Kampf auf Distanz.",
                Special = "Aufladbare Sternenpfeile, Tritt-Combos, Rankenseil mit großer Reichweite",
                Speed = 1.1f, Jump = 1.07f, Grav = 0.92f, Weight = 1.12f,
                StatSpeed = 5, StatPower = 2, StatWeight = 1, StatRange = 5,
                Bone = Color.FromArgb(72, 136, 58), Shade = Color.FromArgb(44, 94, 42), Outline = Color.FromArgb(26, 20, 16),
                Glow = Color.FromArgb(150, 240, 80), Eye = Color.FromArgb(255, 255, 250), Shaft = Color.FromArgb(112, 72, 40),
                Blade = Color.FromArgb(214, 186, 140), Edge = Color.FromArgb(150, 240, 80), ShieldFace = Color.FromArgb(92, 64, 40), Rim = Color.FromArgb(214, 180, 110),
                Skin = Color.FromArgb(246, 208, 172),
                Cloth = Color.FromArgb(70, 134, 56), ClothDark = Color.FromArgb(40, 84, 38), Leather = Color.FromArgb(200, 168, 116),
                LeatherDark = Color.FromArgb(106, 66, 38), Hair = Color.FromArgb(112, 68, 36), Trim = Color.FromArgb(222, 186, 96)
            },
            new CharDef
            {
                Name = "Nyx", Title = "Die Schattenklinge", Weapon = Sensensturm.Weapon.Daggers, Body = Sensensturm.Body.Huntress,
                Desc = "Blitzschnelle Nahkämpferin. Kurze Reichweite, aber endlose Combos.",
                Special = "Wurfdolch, Schattenschritt aus dem Lauf, sehr schnelle leichte Angriffe",
                Speed = 1.14f, Jump = 1.1f, Grav = 1f, Weight = 1.16f,
                StatSpeed = 5, StatPower = 2, StatWeight = 1, StatRange = 1,
                Bone = Color.FromArgb(58, 48, 78), Shade = Color.FromArgb(36, 30, 52), Outline = Color.FromArgb(14, 10, 20),
                Glow = Color.FromArgb(255, 70, 110), Eye = Color.FromArgb(255, 240, 245), Shaft = Color.FromArgb(40, 32, 44),
                Blade = Color.FromArgb(196, 200, 214), Edge = Color.FromArgb(255, 110, 140), ShieldFace = Color.FromArgb(48, 36, 60), Rim = Color.FromArgb(190, 60, 96),
                Skin = Color.FromArgb(232, 206, 196),
                Cloth = Color.FromArgb(54, 44, 74), ClothDark = Color.FromArgb(30, 24, 42), Leather = Color.FromArgb(92, 64, 72),
                LeatherDark = Color.FromArgb(40, 28, 36), Hair = Color.FromArgb(236, 236, 244), Trim = Color.FromArgb(200, 60, 96)
            },
            new CharDef
            {
                Name = "Pyros", Title = "Der Aschenmagier", Weapon = Sensensturm.Weapon.Staff, Body = Sensensturm.Body.Skeleton,
                Desc = "Feuermagier, der den Gegner mit Feuerbällen auf Abstand hält.",
                Special = "Fliegende Feuerbälle, Flammenring, Inferno-Ultimate rund um sich",
                Speed = 0.94f, Jump = 0.97f, Grav = 0.94f, Weight = 0.98f,
                StatSpeed = 2, StatPower = 4, StatWeight = 3, StatRange = 5,
                Bone = Color.FromArgb(222, 206, 184), Shade = Color.FromArgb(156, 134, 114), Outline = Color.FromArgb(30, 14, 10),
                Glow = Color.FromArgb(255, 140, 40), Eye = Color.FromArgb(255, 244, 200), Shaft = Color.FromArgb(70, 40, 30),
                Blade = Color.FromArgb(176, 128, 84), Edge = Color.FromArgb(255, 210, 90), ShieldFace = Color.FromArgb(110, 36, 24), Rim = Color.FromArgb(230, 170, 90),
                Skin = Color.FromArgb(222, 206, 184),
                Cloth = Color.FromArgb(140, 34, 24), ClothDark = Color.FromArgb(66, 20, 18), Leather = Color.FromArgb(92, 56, 36),
                LeatherDark = Color.FromArgb(34, 22, 20), Trim = Color.FromArgb(236, 180, 80), EyeAura = Color.FromArgb(255, 120, 30)
            },
        };

        public static int Count { get { return All.Length; } }

        CharDef With(string variant, Action<CharDef> edit)
        {
            var c = (CharDef)MemberwiseClone();
            c.Name = Name + " (" + variant + ")";
            edit(c);
            return c;
        }

        // Zweite Farbpalette je Kämpfer: bekommt Spieler 2, wenn beide denselben Kämpfer spielen
        public static readonly CharDef[] Alt =
        {
            All[0].With("Glut", c =>
            {
                c.Bone = Color.FromArgb(242, 228, 204); c.Shade = Color.FromArgb(190, 168, 140);
                c.Glow = Color.FromArgb(255, 130, 80); c.EyeAura = Color.FromArgb(255, 170, 60); c.Eye = Color.FromArgb(255, 245, 220);
                c.Cloth = Color.FromArgb(150, 52, 40); c.ClothDark = Color.FromArgb(78, 30, 30); c.ShieldFace = Color.FromArgb(128, 40, 30);
                c.Leather = Color.FromArgb(96, 62, 48); c.Trim = Color.FromArgb(232, 172, 90);
            }),
            All[1].With("Gift", c =>
            {
                c.Bone = Color.FromArgb(226, 232, 212); c.Shade = Color.FromArgb(168, 178, 156);
                c.Glow = Color.FromArgb(150, 255, 120); c.EyeAura = Color.FromArgb(120, 255, 140); c.Eye = Color.FromArgb(235, 255, 230);
                c.Cloth = Color.FromArgb(52, 120, 62); c.ClothDark = Color.FromArgb(30, 62, 40); c.ShieldFace = Color.FromArgb(40, 100, 52);
                c.Trim = Color.FromArgb(190, 210, 150);
            }),
            All[2].With("Frost", c =>
            {
                c.Bone = Color.FromArgb(150, 162, 184); c.Shade = Color.FromArgb(100, 112, 138); c.Skin = c.Bone;
                c.Glow = Color.FromArgb(110, 210, 255); c.Eye = Color.FromArgb(225, 250, 255); c.Edge = Color.FromArgb(110, 210, 255);
                c.Blade = Color.FromArgb(150, 172, 204); c.ShieldFace = Color.FromArgb(60, 82, 122); c.Rim = Color.FromArgb(180, 230, 255);
            }),
            All[3].With("Mondjägerin", c =>
            {
                c.Bone = Color.FromArgb(112, 82, 162); c.Shade = Color.FromArgb(72, 52, 112);
                c.Glow = Color.FromArgb(205, 150, 255); c.Edge = Color.FromArgb(205, 150, 255);
                c.Cloth = Color.FromArgb(112, 72, 162); c.ClothDark = Color.FromArgb(62, 42, 102); c.Hair = Color.FromArgb(222, 222, 236);
                c.Trim = Color.FromArgb(205, 205, 232); c.ShieldFace = Color.FromArgb(70, 50, 100);
            }),
            All[4].With("Mitternacht", c =>
            {
                c.Bone = Color.FromArgb(40, 60, 92); c.Shade = Color.FromArgb(26, 40, 64);
                c.Glow = Color.FromArgb(90, 220, 255); c.Edge = Color.FromArgb(120, 230, 255);
                c.Cloth = Color.FromArgb(30, 52, 84); c.ClothDark = Color.FromArgb(18, 28, 48); c.Hair = Color.FromArgb(34, 34, 46);
                c.Trim = Color.FromArgb(90, 200, 240); c.Rim = c.Trim; c.ShieldFace = Color.FromArgb(26, 44, 70);
            }),
            All[5].With("Blaufeuer", c =>
            {
                c.Glow = Color.FromArgb(90, 170, 255); c.EyeAura = Color.FromArgb(80, 150, 255); c.Eye = Color.FromArgb(225, 240, 255);
                c.Edge = Color.FromArgb(150, 210, 255); c.Blade = Color.FromArgb(150, 160, 180);
                c.Cloth = Color.FromArgb(36, 58, 128); c.ClothDark = Color.FromArgb(22, 30, 64); c.ShieldFace = Color.FromArgb(30, 50, 110);
                c.Trim = Color.FromArgb(180, 210, 240); c.Rim = c.Trim;
            }),
        };

        // Aussehen eines Kämpfers: skin 0 = Standard, 1 = Variante
        public static CharDef Look(int ch, int skin) { return skin == 1 ? Alt[ch] : All[ch]; }

        // nächster Charakter in Richtung d, der nicht "blocked" ist
        public static int Next(int cur, int d, int blocked)
        {
            int n = All.Length;
            int c = cur;
            for (int i = 0; i < n; i++)
            {
                c = ((c + d) % n + n) % n;
                if (c != blocked) return c;
            }
            return cur;
        }
    }
}
