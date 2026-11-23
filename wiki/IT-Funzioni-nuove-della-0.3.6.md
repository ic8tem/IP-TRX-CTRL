# Funzioni nuove della 0.3.6

## Audio Opus

Tra due programmi 0.3.6 o successivi l'audio viaggia compresso con **Opus** (16 kbit/s, voce a 8 kHz) invece che in PCM: meno banda, correzione degli errori (FEC) e riempimento dei buchi quando un pacchetto si perde. Con le versioni precedenti e con la pagina web l'audio resta PCM a 8 kHz. Non c'è niente da impostare.

## Più ascoltatori sullo stesso server

**Impostazioni › Sicurezza in trasmissione (server) › «Altri client e pagine web possono collegarsi in solo ascolto»** (attiva di serie). Mentre un operatore usa la radio, fino a **8** altri client abbinati o pagine web si collegano lo stesso: sentono l'audio della radio e vedono frequenza e strumenti, ma non trasmettono né cambiano nulla. Nella loro finestra compare «tu sei in solo ascolto: trasmette X»; sul server il riquadro dell'altro PC mostra quanti ascoltano. Quando l'operatore si scollega, gli ascoltatori vengono scollegati e il primo che si ricollega diventa operatore.

## Tasto CW USB e MIDI

**Impostazioni › Tasto CW USB e MIDI** (client):

- **Modo tastiera** (VBand, Vail e simili): Ctrl sinistro = punto, Ctrl destro = linea.
- Manipolatore **iambico modo A o B**, oppure **tasto verticale**; velocità 5–60 WPM; scambio punto/linea; tono di ascolto sul PC al pitch CW.
- **MIDI** (Vail in modo MIDI, TinyMIDI): Do = tasto verticale, Do# = punto, Re = linea. Windows e Linux (l'utente deve essere nel gruppo `audio`), non sul Mac.

Il manipolatore gira sul client e manda al server i tempi del tasto, che il server ripete con il ritardo fisso del tasto CW (come il tasto meccanico su porta seriale, vedi [Programmi digitali e pedale](IT-Programmi-digitali-e-pedale)).

Anche la **pagina web** ha il tasto: Impostazioni › Tasto CW, con modo tastiera (Ctrl oppure `[` e `]`) e MIDI con Chrome o Edge.

## Registro dei QSO

Pulsante **«Registro»** in alto nella finestra principale: nominativo, RST dato e ricevuto, locatore, nome, QTH e note. Frequenza, banda, modo e orari arrivano dalla radio.

- **Registra** salva il QSO in `registro.adi`, nella cartella delle impostazioni, e se attivo lo manda ai programmi di log con il **protocollo UDP di WSJT-X** (messaggi «QSO Logged» e «Logged ADIF», porta 2237 di serie): GridTracker, JTAlert, Log4OM, N1MM, HRD e altri lo registrano come un QSO di WSJT-X.
- **Esporta ADIF…** salva un file per **LoTW** (TQSL), QRZ e Club Log.
- **Impostazioni › Registro dei QSO**: indirizzo, porta e il tuo locatore. Se WSJT-X è aperto sullo stesso PC, nel programma di log aggiungi una seconda porta o usa l'inoltro multicast.

## Radio Icom in rete

Per IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905 e le altre Icom con la rete: il server parla con la radio come RS-BA1 e wfview, **senza cavi USB e senza un PC accanto alla radio**.

1. Sulla radio: Set › Network › **Network Control ON** e un utente in Network User1 (nome e password, al massimo 16 caratteri).
2. **Impostazioni › Radio Icom in rete (server)**: indirizzo della radio, porta 50001, utente e password (resta cifrata su quel PC).
3. In **Radio (server)**: il modello Hamlib della radio e PTT «CAT».
4. Salva, riapri le Impostazioni e come scheda audio del server scegli **«Icom LAN · audio della radio»** (ricezione e trasmissione a 8 kHz).

Il CAT passa da rigctld, collegato a un ponte interno verso il flusso CI-V della radio. Il protocollo è riscritto in C# seguendo [wfview](https://gitlab.com/eliggett/wfview) (GPL 3) e la documentazione di [kappanhang](https://github.com/nonoo/kappanhang); vedi [Licenze](IT-Licenze).

> Funzione nuova, provata finora senza una radio vera: in caso di problemi allega il «Rapporto per un problema» (righe ICOM di `sessioni.log`).


---

[Indice](Italiano) · Altre lingue: [English](EN-New-in-0.3.6) · [Español](ES-Novedades-de-la-0.3.6) · [Français](FR-Nouveautés-de-la-0.3.6) · [Deutsch](DE-Neu-in-0.3.6)
