# Programmi digitali, pedale PTT e tasto CW

Queste funzioni riguardano il **client** (il PC dell'operatore).

## Programmi digitali (WSJT-X, JTDX, JS8Call, fldigi…)

Il client offre ai programmi digitali due cose:

- **un rigctld locale** su `127.0.0.1:4532`: ogni comando CAT viaggia cifrato fino al rigctld del server. I comandi di PTT non vengono inoltrati: diventano il PTT di IP-TRX-CTRL, con le stesse sicurezze del pulsante PTT;
- **due percorsi audio** su cavi virtuali: la ricezione della radio va verso il programma, e durante la trasmissione chiesta dal programma l'audio del programma va alla radio al posto del microfono.

Si attiva da Impostazioni › **Programmi digitali** › «Usa programmi digitali».

### Impostazioni nel programma digitale

| Voce | Valore |
|---|---|
| Radio | Hamlib **NET rigctl** |
| Indirizzo | `127.0.0.1:4532` (la porta si cambia nelle impostazioni di IP-TRX-CTRL) |
| PTT | CAT |
| Ingresso audio | il cavo che IP-TRX-CTRL usa come «verso il programma» |
| Uscita audio | il cavo che IP-TRX-CTRL usa come «dal programma» |

### Cavi audio virtuali per sistema

| Sistema | Cavo | Note |
|---|---|---|
| Windows 64 bit | **VB-CABLE** (VB-Audio, donationware) | L'installatore lo propone come attività facoltativa, lo scarica dal sito del produttore e lo salta se è già installato. Dopo l'installazione serve un riavvio. |
| Windows 32 bit | — | Funzione non disponibile. |
| macOS | **BlackHole 2ch** (MIT) | `brew install blackhole-2ch`, poi riavvia IP-TRX-CTRL. |
| Linux / Raspberry Pi | creati dal programma | Due «null sink» `IPTRX_RX` e `IPTRX_TX` con `pactl` (PipeWire o PulseAudio, pacchetto `pulseaudio-utils`). Nel programma digitale: ingresso «Monitor of IP-TRX-CTRL_RX», uscita «IP-TRX-CTRL_TX». |

**Un solo cavo** (VB-CABLE gratuito): IP-TRX-CTRL scrive la ricezione su «CABLE Input» e legge da «CABLE Output»; il programma digitale fa lo stesso. Funziona in **semi-duplex**: mentre il programma trasmette, IP-TRX-CTRL smette di mandargli la ricezione. **Due cavi** (per esempio VB-CABLE A+B): uno per direzione; il pulsante «Scegli i cavi in automatico» li assegna.

## Pedale PTT e tasto CW meccanico

Impostazioni › **Pedale PTT e tasto CW**. Si usa una porta seriale del client (anche un adattatore USB-seriale) come ingresso:

| Contatto | Collegamento | Ingresso da scegliere |
|---|---|---|
| Pedale PTT | tra **RTS** (pin 7 del DB9) e **CTS** (pin 8) | CTS |
| Tasto verticale | tra **DTR** (pin 4) e **DSR** (pin 6) | DSR |
| in alternativa | tra DTR (pin 4) e **DCD** (pin 1) | DCD |

Il programma alza RTS e DTR e legge gli ingressi ogni millisecondo. Il pedale agisce come il pulsante PTT. Il tasto manda al server ogni pressione e rilascio con il suo istante; il server li ripete sulla porta CW della radio con un **ritardo fisso** (250 ms di serie, regolabile) che assorbe le variazioni di ritardo di internet, così le durate di punti e linee restano esatte. Il PTT della radio si accende prima del primo segno e si rilascia dopo 800 ms senza segni; un tasto rimasto premuto per più di 10 secondi viene rilasciato per sicurezza.

**Porta virtuale di un altro programma.** Al posto dell'hardware si può usare la porta virtuale di un programma che comanda PTT o CW con RTS e DTR. Su Windows serve una coppia di porte virtuali **com0com**: il programma usa una porta della coppia, IP-TRX-CTRL l'altra (RTS arriva su CTS, DTR su DSR). Su Linux e macOS le pseudo-porte (pty) non hanno le linee di controllo: serve un adattatore reale.
