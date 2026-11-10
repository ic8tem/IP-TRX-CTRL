# Finestra principale (client)

La finestra del client è divisa in riquadri: in alto **radio** e **audio** a sinistra, **server e PTT** a destra; sotto, a tutta larghezza, cascata CW, keyer e collegamento. Dalla 0.2.5 la finestra si apre alta 816 punti (prima 960): se lo schermo è più basso del contenuto compare la barra di scorrimento. Il programma si chiama **IP-TRX-CTRL** e il nome compare in alto a sinistra; accanto ci sono lo stato del collegamento, il ruolo (CLIENT o SERVER), la scelta dei colori, **Impostazioni**, **Inverti ruoli** e **Help**.

## Radio remota

- **VFO A** in grande, con frequenza e modo.
- **VFO B** con i pulsanti **A/B** (scambio), **A=B** (copia) e **SPLIT**. Con lo split attivo si riceve su A e si trasmette su B. Vedi *Radio, split e potenza*.
- Casella per impostare una nuova frequenza in Hz e il modo.
- Indicatori: S-meter, potenza RF in uscita, SWR. Con la **protezione ROS** la barra del SWR lampeggia in rosso sopra la soglia di avviso; se la trasmissione è bloccata, un clic sulla barra riapre la finestra di sblocco (vedi *Radio, split e potenza*).
- **Potenza TX**: cursore da 1 a 100 W, oppure da 1 a 100 % per le radio che non la esprimono in watt.
- In modo CW, l'audio di ricezione (altoparlante, volume, boost, livello) si sposta qui su una sola riga, per lasciare spazio alla cascata.

## Server (stazione radio)

- Nominativo del server, **IP del server con le porte**, **IP del client** (pubblico e di rete locale), ora di collegamento, latenza.
- **Rete** di questo PC e dell'altro: se c'è IPv4 e/o IPv6 verso internet (con «IPv6 solo locale» quando la scheda ha un indirizzo IPv6 ma internet non risponde in IPv6).
- Livello dell'audio che arriva dal server.
- **Spia RX/TX** in alto a destra: grigia in ricezione, **rossa lampeggiante** quando la radio è in trasmissione (anche durante un messaggio F1…F8).
- **PTT**: pulsante "premi e tieni", anche con la barra spaziatrice. Sotto è indicato il metodo PTT impostato sul server (RTS, DTR, CAT o TCI) e il ritardo.

## Audio (modi fonia)

Sotto la radio, nella colonna di sinistra (dalla 0.2.5, per non lasciare spazio vuoto accanto al riquadro del server). Scelta di microfono e altoparlante, volume e boost per RX e TX, indicatori di livello. In modo CW questo riquadro si nasconde e sotto i riquadri compaiono cascata e decodificatore (vedi *Modo CW*).

## Keyer

Tasti **F1…F8**. In SSB sono messaggi WAV, in CW sono macro di testo. Vedi *Keyer, messaggi WAV e macro CW*.

## Collegamento

Stato, latenza, jitter, pacchetti persi, buffer e stato dei broker MQTT.

## Schede audio del server

Il client vede se le schede audio del server sono silenziate (mute) dal sistema operativo e può togliere il mute a distanza. Vedi *Schede audio del server*.
