# Lizenzen der Fremdkomponenten

IP-TRX-CTRL enthält oder verwendet die unten aufgeführten Komponenten. Das Programm hat ein Fenster **Informazioni e licenze** (Info und Lizenzen) mit den vollständigen Texten; vor jeder Weitergabe sind Rechteinhaber und Texte anhand der tatsächlich verwendeten Pakete zu prüfen.

| Komponente | Lizenz | Website |
|---|---|---|
| Hamlib (Bibliothek) | GNU LGPL 2.1 oder neuer | https://hamlib.github.io |
| Hamlib `rigctld`, `rigctl` | GNU GPL 2 oder neuer (nur in der Windows-Version enthalten) | https://github.com/Hamlib/Hamlib |
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (Schrift) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| MQTTnet | MIT | https://github.com/dotnet/MQTTnet |
| Open.NAT | MIT | https://github.com/lontivero/Open.NAT |
| .NET Runtime, System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Hinweise:

- `rigctld` und `rigctl` werden als eigenständige, unveränderte Programme verteilt, im Hintergrund gestartet und über TCP genutzt. Die Quellen der enthaltenen Version liegen auf der Releases-Seite des Hamlib-Repositorys.
- Audio und globale Tasten nutzen direkt die Funktionen des Betriebssystems (winmm/Core Audio, ALSA/libX11, CoreAudio/Carbon): keine zusätzliche Bibliothek.
- Der Keyer-Code stammt aus WavPTT vom selben Autor und bringt keine Auflagen mit.
- Der CW-Decoder ist von Grund auf neu geschrieben.
- Die Namen der öffentlichen MQTT-Broker dienen nur als Treffpunkt; es werden keine Daten im Klartext veröffentlicht.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Licenze) · [English](EN-Licenses) · [Español](ES-Licencias) · [Français](FR-Licences)
