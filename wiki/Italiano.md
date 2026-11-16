# IP-TRX-CTRL

IP-TRX-CTRL serve a usare una radio HF a distanza, via internet, come se fosse sul proprio tavolo. Un programma unico gira su due PC:

- **server**: il PC collegato alla radio (audio, CAT tramite Hamlib/rigctld, PTT);
- **client**: il PC dell'operatore, che riceve l'audio della radio, trasmette con il proprio microfono e comanda la radio.

I ruoli si scelgono al primo avvio e si possono invertire in qualsiasi momento.

> **Versione attuale: 0.3.3.** Le immagini sono schermate vere del programma (client collegato a un server con una radio simulata da Hamlib).

## In breve

- Audio bidirezionale sempre a **8 kHz, 16 bit mono**, cifrato AES-256.
- Porte, chiavi e riconnessioni gestite in automatico; il server viene ritrovato anche quando cambia indirizzo IP, tramite un broker MQTT pubblico.
- Controllo CAT della radio: frequenza, modo, VFO A/B con split, potenza TX, letture di S-meter, potenza e SWR, protezione ROS con blocco della trasmissione.
- **Keyer** integrato sul client (deriva dal programma WavPTT): in SSB otto messaggi WAV su F1…F8, in CW otto macro di testo trasmesse dalla radio.
- In modo CW: cascata 0–2000 Hz con la QRG reale dei segnali, decodificatore sul pitch e finestre di decodifica aggiuntive per gli altri segnali.
- Programmi digitali sul client (WSJT-X, JTDX, fldigi…) tramite un rigctld locale e cavi audio virtuali: vedi [Programmi digitali, pedale PTT e tasto CW](IT-Programmi-digitali-e-pedale).
- Pedale PTT e tasto CW meccanico su una porta seriale del client.
- Ciclo delle porte tra server e client (UPnP verificato dall'esterno, collegamento inverso, 20 giri) e audio su UDP quando possibile: vedi [Porte e collegamento](IT-Porte-e-collegamento).
- Dalla 0.3.0: sicurezza in trasmissione (tempo massimo, niente cambi di frequenza in trasmissione, precedenza a chi è alla radio, client in solo ascolto), comandi della radio (NR, NB, Notch, ATT, preamplificatore, AGC, compressore, squelch, ALC), bande, passo e memorie, [panadapter della radio](IT-Panadapter), rapporto per segnalare un problema e avviso di versione nuova. Vedi [Radio, split e potenza](IT-Radio-split-e-potenza) e [Finestra principale](IT-Finestra-principale).
- Dalla 0.3.1: [accesso dal browser](IT-Accesso-dal-browser) di telefono, tablet e PC, con la pagina «Trova il server».
- Dalla 0.3.2: aggiornamenti automatici (non per i pacchetti .deb, .rpm, Arch e Slackware) e pacchetti per Windows ARM, Fedora/openSUSE, Arch, Slackware, AppImage e Alpine: vedi [Registri e risoluzione dei problemi](IT-Registri-e-risoluzione-dei-problemi).
- Dalla 0.3.3: su Linux, Raspberry Pi e Mac prima configurazione, Impostazioni e licenze non si bloccano più; etichette ROS e Gain visibili.
- Sei temi di colore: Chiaro, Notte, Nero, Blu, Viola, Verde: vedi [Temi di colore](IT-Temi-di-colore).
- Un unico codice sorgente per **Windows 64 bit, 32 bit e ARM, macOS, Linux (anche Alpine) e Raspberry Pi**.

Prima di aggiornare uno dei due PC guarda le [Versioni compatibili](IT-Versioni-compatibili).

![Client collegato, tema Chiaro](https://raw.githubusercontent.com/ic8tem/IP-TRX-CTRL/video-corso/wiki-images/main.png)

## Pagine

- [Video corso](IT-Video-corso)
- [Finestra principale](IT-Finestra-principale)
- [Abbinamento e MQTT](IT-Abbinamento-e-MQTT)
- [Porte e collegamento](IT-Porte-e-collegamento)
- [Radio, split e potenza](IT-Radio-split-e-potenza)
- [Audio e schede audio del server](IT-Audio-e-schede-audio-del-server)
- [Keyer, messaggi WAV e macro CW](IT-Keyer-messaggi-WAV-e-macro-CW)
- [Modo CW](IT-Modo-CW)
- [Panadapter](IT-Panadapter)
- [Programmi digitali e pedale](IT-Programmi-digitali-e-pedale)
- [Collegamento TCI](IT-Collegamento-TCI)
- [Accesso dal browser](IT-Accesso-dal-browser)
- [Temi di colore](IT-Temi-di-colore)
- [Registri e risoluzione dei problemi](IT-Registri-e-risoluzione-dei-problemi)
- [Versioni compatibili](IT-Versioni-compatibili)
- [Piattaforme e compilazione](IT-Piattaforme-e-compilazione)
- [Licenze](IT-Licenze)


---

[Indice](Italiano) · Altre lingue: [English](English) · [Español](Español) · [Français](Français) · [Deutsch](Deutsch)
