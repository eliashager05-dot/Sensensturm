# Sensensturm

2D-Plattform-Kampfspiel (Brawlhalla-ähnliches Spielprinzip) mit 4 Kämpfern und 3 Waffentypen:

| Kämpfer | Typ | Stil |
|---|---|---|
| Lila Skelett / Blaues Skelett | **Sense** | Allrounder, große Reichweite, flüssige Luft-Combos |
| **Brokk, der Steinwächter** (Golem) | **Hammer** | langsam und schwer, extrem harte Treffer, **Super-Rüstung** beim Ausholen schwerer Angriffe (wird nicht unterbrochen), **Bebenwelle** rollt über den Boden |
| **Lyra, die Sternenschützin** (Jägerin) | **Bogen** | schnell und leicht, Tritt-Combos, **aufladbare Sternenpfeile** (vorwärts, schräg nach oben, aus der Luft mit Rückstoß), **Rankenseil** greift aus großer Entfernung |

Jeder Typ hat ein eigenes Moveset (14 Angriffe + Griff/Wurf) und eigene Werte für Tempo, Sprungkraft, Fallgeschwindigkeit und Gewicht. Die Auswahl erfolgt unter „Lokal spielen“ bzw. in der LAN-Lobby (beide Spieler wählen selbst). Combos für jeden Typ stehen unter *Einstellungen > Combos lernen* (Links/Rechts wechselt die Waffe).
Alle Grafiken werden im Code gezeichnet, alle Sounds werden synthetisiert. Es gibt keine fremden Assets.

## Arenen
Unter „Lokal spielen“ bzw. in der Host-Lobby wählt man unter **Arena** eine von vier Arenen. Der Client übernimmt automatisch die Arena des Hosts.

| Arena | Aufbau |
|---|---|
| **Mondfriedhof** | Original: Nacht, Vollmond, drei Schwebeplattformen |
| **Kirschblütental** | Kirschbäume, Pagoden-Burg, Torii im See; zwei hohe Plattformen weit außen |
| **Herbsttempel** | Sonnenuntergang, Buddha-Statue, roter Ahorn; zwei Plattformen |
| **Sternenhain** | Sternennacht, leuchtender Weltenbaum, Ruinentor; zwei Plattformen |

Die drei neuen Arenen sind gemalte Bilder (`assets/stage_*.jpg`, beim Bauen in die EXE eingebettet). Die Plattformen jeder Arena (`Arena.Layouts` in `src/Sim.cs`) sind so ausgemessen, dass sie genau auf den gemalten Plattformen liegen.

## Grafik und Leistung
*Einstellungen > Grafik*: **Automatisch** (Standard), Hoch, Mittel oder Niedrig. Gezeichnet wird in höchstens 1080p (Mittel 75 %, Niedrig 50 %) und dann aufs Fenster gestreckt. Bei „Automatisch“ senkt das Spiel die Stufe selbst, wenn es dauerhaft unter etwa 45 FPS läuft. Oben rechts stehen FPS, Zeichenzeit pro Bild und die Zeichenauflösung (abschaltbar über „FPS / Ping anzeigen“).

## Starten
`Sensensturm.exe` doppelklicken. Benötigt wird nur Windows 10/11 (das .NET Framework 4.8 ist dort bereits enthalten).
Für den zweiten PC einfach den ganzen Ordner kopieren (mindestens die `Sensensturm.exe`).

Neu bauen: `build.bat` (verwendet den in Windows eingebauten C#-Compiler).

## Online-Spiel (über das Internet, verschiedene Netzwerke)
1. Host: **Online-Spiel erstellen**. Es erscheint ein 6-stelliger **Raum-Code** (mit „Raum-Code kopieren“ in die Zwischenablage).
2. Mitspieler: **Online-Spiel beitreten**, Code eingeben, **Verbinden**.
3. Beide wählen ihren Charakter, dann startet der Host das Match.

Keine Portfreigabe nötig: Beide PCs treffen sich über einen öffentlichen Vermittlungsserver (MQTT, ausgehender TCP-Port 1883). Zuerst wird eine **Direktverbindung** per UDP-Hole-Punching aufgebaut (STUN). Klappt das nicht, etwa bei strenger Firewall oder symmetrischem NAT in Schul- und Firmennetzen, läuft das Spiel automatisch **über das Relay** (etwas höhere Verzögerung, 30 statt 60 Updates/s). Die Verbindungsart wird oben rechts angezeigt.
Hinweis: Der Vermittlungsserver ist ein öffentlicher Testdienst (broker.hivemq.com, Ersatz: broker.emqx.io, test.mosquitto.org). Er ist meist zuverlässig, aber ohne Garantie.

## LAN-Spiel (zwei PCs im selben WLAN/LAN)
1. PC 1: **LAN-Spiel erstellen**. IP-Adresse und Port (Standard 7777) werden angezeigt.
2. PC 2: **LAN-Spiel beitreten**. Die Lobby erscheint meist automatisch unter „Gefunden“, sonst die IP eintippen und **Verbinden** wählen.
3. PC 1 wählt **Match starten**. Über Esc beendet der Host das Match wieder (zurück zur Lobby).

Beim ersten Hosten fragt die Windows-Firewall nach. Dort **„Private Netzwerke“ erlauben**. Wenn der Beitritt fehlschlägt, liegt es fast immer an der Firewall (UDP-Port 7777) oder daran, dass ein Gerät in einem Gäste-WLAN mit Client-Isolation hängt.

Technik: Der Host rechnet das Spiel autoritativ und entscheidet allein über Treffer, Block, Parade und KO. Der Client sendet 60× pro Sekunde seine Eingaben (redundant gegen Paketverlust) und sagt seine eigene Bewegung voraus (Client-Side Prediction mit Abgleich). Der Host schickt 60× pro Sekunde den vollständigen Spielzustand samt Ereignissen.

## Steuerung (änderbar unter Einstellungen)
| Aktion | Spieler 1 / LAN | Spieler 2 (lokal) | Controller |
|---|---|---|---|
| Bewegen / Zielen | A D W S | Pfeiltasten | Stick / D-Pad |
| Springen (gedrückt halten = höher) | Leertaste oder W | Num 0 oder Pfeil hoch | A |
| Leichter Angriff | J oder **linke Maustaste** | Num 1 oder . | X |
| Schwerer Angriff (halten = aufladen) | K oder mittlere Maustaste | Num 2 oder - | B |
| Dodge (Boden + Richtung = Dash) | L oder Shift | Num 3 | RT / RB |
| Schild / Parieren | H oder **rechte Maustaste** | Num 5 | LT / LB |
| Greifen / Werfen (Sensenhaken) | E | Num 4 | Y |

Esc/Start = Pause · F11 = Vollbild · F3 = Hitboxen anzeigen

## Spielmechanik
- **Bewegung:** sofortige Beschleunigung, variable Sprunghöhe, 3 Luftsprünge, Coyote-Time, Schnellfallen (Unten in der Luft), Dash und Sprint, Luft-Dodge in 8 Richtungen (einmal pro Luftphase, mit Abklingzeit), Wandrutschen und Wandsprung an den Seiten der Hauptplattform, Schwebeplattformen (Unten + Springen zum Durchfallen).
- **Sense, leichte Angriffe:** neutral, vorne, oben, unten (Boden) sowie neutral, vorne, oben, unten (Luft).
- **Sense, schwere Angriffe:** schwerer Seitenhieb (aufladbar), *Seelenernte* (lange aufladbar, viel Schaden, aber großes Risiko: Treffer beim Aufladen machen +25 % Schaden), Erdspalter, schwerer Luftschwung, Fallbeil (Sturz mit Aufprall-Schockwelle) und Aufschwung (Recovery nach oben, einmal pro Luftphase).
- **Combos:** Nur nach einem echten Treffer gibt es ein kurzes Cancel-Fenster in einen *anderen* Angriff, einen Sprung, einen Dodge oder einen Griff. Verfehlte Angriffe lassen sich nicht abbrechen. Alle Combos zum Lernen stehen unter *Einstellungen > Combos lernen*. Üben kannst du sie gegen die *Trainingspuppe* (Lokal spielen > Gegner).
- **PERFEKT-Timing:** Drückst du die nächste Taste kurz *nach* dem Trefferstopp (nicht schon vorher), erscheint PERFEKT. Der Gegner bleibt länger betäubt, ein perfekter Sprung kostet keinen Luftsprung und zielt automatisch auf den Gegner. Perfekte Ketten reißen deshalb nicht ab (z. B. die Himmelsleiter bis zum K.O.).
- **Verfolgungs-Dodge:** Dodge direkt nach einem Treffer jagt dem Gegner hinterher.
- **Sensenhaken:** Greifen geht durch Schild und Parade. Danach wirfst du per Richtung nach vorne, hinten, oben oder unten. Ein Wurf nach unten eignet sich als Combo-Starter.
- **Rettung:** Getroffen werden gibt alle Sprünge, den Luft-Dodge und den Aufschwung zurück. Es gibt 3 Luftsprünge, Wandsprünge an den tiefen Seitenwänden und eine Kantenhilfe, die dich knapp unter der Kante auf die Bühne zieht.
- **Schild:** Die ersten 8 Frames nach dem Drücken sind eine **perfekte Parade**. Sie wehrt alles ab und betäubt den Angreifer, dazu gibt es Lichtblitz, Glockenklang und „PARIERT!“. Danach blockt der Schild nur nach vorne, verliert Energie und kann von schweren Angriffen gebrochen werden. Wer den Schild zu früh loslässt, bekommt eine Abklingzeit.
- **Knockback** steigt mit dem Schaden (Farbe der Prozentanzeige: weiß → gelb → orange → rot). KO gibt es beim Verlassen der Arena. Standard sind 3 Leben.

## Feintuning
Alle Bewegungs- und Kampfwerte stehen kommentiert in `tuning.ini` (wird beim ersten Start erzeugt).
Unter *Einstellungen > Spielwerte neu laden* werden Änderungen ohne Neustart übernommen. Im LAN-Spiel gelten automatisch die Werte des Hosts.
