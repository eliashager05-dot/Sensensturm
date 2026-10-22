using System.Drawing;

namespace Sensensturm
{
    public static class Weapon
    {
        public const int Scythe = 0, Hammer = 1, Bow = 2;
        public static readonly string[] Names = { "Sense", "Hammer", "Bogen" };
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

        public string TypeName { get { return Sensensturm.Weapon.Names[Weapon]; } }

        public static readonly CharDef[] All =
        {
            new CharDef
            {
                Name = "Lila Skelett", Title = "Der Seelenschnitter", Weapon = Sensensturm.Weapon.Scythe, Body = Sensensturm.Body.Skeleton,
                Desc = "Allrounder mit großer Reichweite und flüssigen Luft-Combos.",
                Special = "Aufladbare Seelenernte, Fallbeil-Sturz",
                StatSpeed = 3, StatPower = 3, StatWeight = 3, StatRange = 4,
                Bone = Color.FromArgb(200, 145, 255), Shade = Color.FromArgb(140, 88, 212), Outline = Color.FromArgb(32, 8, 58),
                Glow = Color.FromArgb(222, 150, 255), Eye = Color.FromArgb(255, 232, 255), Shaft = Color.FromArgb(78, 42, 98),
                Blade = Color.FromArgb(222, 218, 236), Edge = Color.FromArgb(205, 120, 255), ShieldFace = Color.FromArgb(98, 44, 152), Rim = Color.FromArgb(236, 205, 255),
                Skin = Color.FromArgb(200, 145, 255)
            },
            new CharDef
            {
                Name = "Blaues Skelett", Title = "Der Frostschnitter", Weapon = Sensensturm.Weapon.Scythe, Body = Sensensturm.Body.Skeleton,
                Desc = "Allrounder mit großer Reichweite und flüssigen Luft-Combos.",
                Special = "Aufladbare Seelenernte, Fallbeil-Sturz",
                StatSpeed = 3, StatPower = 3, StatWeight = 3, StatRange = 4,
                Bone = Color.FromArgb(118, 182, 255), Shade = Color.FromArgb(66, 120, 214), Outline = Color.FromArgb(6, 22, 58),
                Glow = Color.FromArgb(110, 225, 255), Eye = Color.FromArgb(225, 252, 255), Shaft = Color.FromArgb(34, 58, 100),
                Blade = Color.FromArgb(214, 226, 238), Edge = Color.FromArgb(80, 200, 255), ShieldFace = Color.FromArgb(30, 78, 162), Rim = Color.FromArgb(198, 232, 255),
                Skin = Color.FromArgb(118, 182, 255)
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
                Bone = Color.FromArgb(70, 190, 160), Shade = Color.FromArgb(36, 112, 100), Outline = Color.FromArgb(6, 28, 32),
                Glow = Color.FromArgb(140, 255, 214), Eye = Color.FromArgb(235, 255, 248), Shaft = Color.FromArgb(132, 88, 52),
                Blade = Color.FromArgb(220, 245, 238), Edge = Color.FromArgb(255, 120, 110), ShieldFace = Color.FromArgb(40, 110, 96), Rim = Color.FromArgb(200, 255, 235),
                Skin = Color.FromArgb(242, 210, 186)
            },
        };

        public static int Count { get { return All.Length; } }

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
