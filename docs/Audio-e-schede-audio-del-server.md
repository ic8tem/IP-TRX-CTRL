# Audio e schede audio del server

## Audio sempre a 8 kHz

Tutto l'audio è trattato a **8 kHz, 16 bit mono**:

- il sistema operativo apre microfono e altoparlante in modalità condivisa alla frequenza della scheda; il programma converte a 8 kHz appena il segnale arriva e torna alla frequenza della scheda solo per l'uscita;
- filtri, compressore, mixaggio microfono/messaggi, rete e decodificatore CW lavorano a 8 kHz;
- la banda utile arriva a circa 4 kHz, sufficiente per la SSB e per la cascata CW 0–3000 Hz.

## Scegliere le schede audio del server dal client

Nel riquadro **Schede audio del server** le righe *Ingresso* e *Uscita* sono due elenchi con le schede presenti sul PC server:

- **Ingresso**: la scheda da cui il server prende l'audio della radio (ricezione);
- **Uscita**: la scheda con cui il server manda l'audio alla radio (trasmissione).

Scegliendo una scheda dal client, il server riavvia il proprio audio sulla scheda nuova e salva la scelta. Se la scheda non si apre (scollegata, occupata, permesso negato) il server torna subito alle schede di prima e il client vede un avviso; nel registro `sessioni.log` del server resta una riga `AUDIO`. L'elenco delle schede del server si aggiorna da solo ogni 30 secondi, per esempio dopo aver collegato un'interfaccia USB.

Lo stesso elenco funziona anche sul server: cambiare la scheda lì equivale a cambiarla nel riquadro dell'audio locale.

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
