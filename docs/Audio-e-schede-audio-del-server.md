# Audio e schede audio del server

## Audio sempre a 8 kHz

Tutto l'audio è trattato a **8 kHz, 16 bit mono**:

- il sistema operativo apre microfono e altoparlante in modalità condivisa alla frequenza della scheda; il programma converte a 8 kHz appena il segnale arriva e torna alla frequenza della scheda solo per l'uscita;
- filtri, compressore, mixaggio microfono/messaggi, rete e decodificatore CW lavorano a 8 kHz;
- la banda utile arriva a circa 4 kHz, sufficiente per la SSB e per la cascata CW 0–3000 Hz.

## Mute delle schede audio del server

Il client vede se le due schede audio del server collegate alla radio (ricezione e trasmissione) sono silenziate dal sistema operativo, insieme al loro volume di sistema, e può:

- togliere il mute a distanza;
- regolare il volume di sistema;
- attivare **Togli il mute da solo**: il server lo disattiva appena qualcuno lo attiva e lo segnala al client.

Come viene fatto su ciascun sistema:

| Sistema | Interfaccia |
|---|---|
| Windows | Core Audio |
| macOS | CoreAudio |
| Linux / Raspberry Pi | PipeWire o PulseAudio; ALSA se mancano |

Limiti:

- il programma server deve girare nella sessione dell'utente, non come servizio;
- se il sistema nega l'accesso al microfono per privacy, il programma lo segnala ma non lo cambia: va sbloccato a mano sul server una volta;
- un eventuale mute interno al driver USB della radio non è visibile al sistema operativo.
