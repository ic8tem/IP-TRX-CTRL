# IP-TRX-CTRL

IP-TRX-CTRL serve a usare una radio HF a distanza, via internet, come se fosse sul proprio tavolo. Un programma unico gira su due PC:

- **server**: il PC collegato alla radio (audio, CAT tramite Hamlib/rigctld, PTT);
- **client**: il PC dell'operatore, che riceve l'audio della radio, trasmette con il proprio microfono e comanda la radio.

I ruoli si scelgono al primo avvio e si possono invertire in qualsiasi momento.

> **Versione attuale: 0.3.8.** Le immagini sono schermate vere del programma (client collegato a un server con una radio simulata da Hamlib).

## In breve

- Audio bidirezionale a 8 kHz, cifrato AES-256: **Opus** tra programmi 0.3.6, PCM 16 bit mono con le versioni precedenti e con il browser.
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
- Dalla 0.3.8: collegamento cifrato (TLS) ai broker MQTT con ritorno automatico alla porta 1883, chiavi effimere per ogni sessione (segretezza futura) e limite su tutti i tentativi di codice.
- Dalla 0.3.7: riduzione del rumore neurale HamNoise, VOX con il pulsante accanto al PTT, squelch vocale, note tra operatore e ascoltatori, registrazione dei QSO, IPv6 diretto con PCP, server senza finestra ed emulazione WinKeyer: vedi [Funzioni nuove della 0.3.7](IT-Funzioni-nuove-della-0.3.7).
- Dalla 0.3.6: audio Opus, più ascoltatori sullo stesso server, tasto CW USB e MIDI, registro dei QSO con invio UDP ai programmi di log ed esportazione ADIF, radio Icom in rete senza PC accanto: vedi [Funzioni nuove della 0.3.6](IT-Funzioni-nuove-della-0.3.6). Licenza GNU AGPL 3.0 o successiva, con il collegamento al codice sorgente nella pagina web.
- Dalla 0.3.5: pacchetti per Windows 7 SP1, 8 e 8.1 (`-win7-x64-`, `-win7-x86-`, con .NET 6); in cima a ogni release la tabella con il collegamento a ogni pacchetto.
- Dalla 0.3.4: porte scelte da te in Impostazioni (con l'elenco delle due porte da aprire), riconoscimento del doppio NAT e dell'UPnP di un apparecchio che non è il router.
- Dalla 0.3.3: su Linux, Raspberry Pi e Mac prima configurazione, Impostazioni e licenze non si bloccano più; etichette ROS e Gain visibili.
- Sei temi di colore: Chiaro, Notte, Nero, Blu, Viola, Verde: vedi [Temi di colore](IT-Temi-di-colore).
- Un unico codice sorgente per **Windows 64 bit, 32 bit e ARM, macOS, Linux (anche Alpine) e Raspberry Pi**.

Prima di aggiornare uno dei due PC guarda le [Versioni compatibili](IT-Versioni-compatibili).

![Client collegato, tema Chiaro](https://raw.githubusercontent.com/ic8tem/IP-TRX-CTRL/video-corso/wiki-images/main.png)

## Pagine

- [Video corso](IT-Video-corso)
- [Funzioni nuove della 0.3.7](IT-Funzioni-nuove-della-0.3.7)
- [Funzioni nuove della 0.3.6](IT-Funzioni-nuove-della-0.3.6)
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
