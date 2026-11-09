# Licenze dei componenti di terze parti

IP-TRX-CTRL include o usa i componenti elencati qui sotto. Il programma contiene una finestra **Informazioni e licenze** con i testi completi; prima di ogni distribuzione titolari e testi vanno verificati sui pacchetti effettivamente usati.

| Componente | Licenza | Sito |
|---|---|---|
| Hamlib (libreria) | GNU LGPL 2.1 o successiva | https://hamlib.github.io |
| Hamlib `rigctld`, `rigctl` | GNU GPL 2 o successiva (inclusi solo nella versione Windows) | https://github.com/Hamlib/Hamlib |
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (carattere) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| MQTTnet | MIT | https://github.com/dotnet/MQTTnet |
| Open.NAT | MIT | https://github.com/lontivero/Open.NAT |
| .NET Runtime, System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Note:

- `rigctld` e `rigctl` sono distribuiti come programmi separati, non modificati, avviati in background e usati via TCP. I sorgenti della versione inclusa si trovano nella pagina Releases del repository Hamlib.
- Audio e tasti globali usano direttamente le funzioni del sistema operativo (winmm/Core Audio, ALSA/libX11, CoreAudio/Carbon): nessuna libreria aggiuntiva.
- Il codice del keyer deriva da WavPTT, dello stesso autore, e non aggiunge vincoli.
- Il decodificatore CW è scritto da zero.
- I nomi dei broker MQTT pubblici sono usati solo come punto d'incontro; nessun dato in chiaro viene pubblicato.
