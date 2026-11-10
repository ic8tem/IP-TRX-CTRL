# Collegamento TCI

Dalla 0.2.5 il server può comandare la radio con il **TCI** (Transceiver Control Interface) invece che con Hamlib. Il TCI è il protocollo di Expert Electronics (ExpertSDR2, ExpertSDR3) usato anche da Thetis e da altri programmi SDR: il programma della radio fa da server WebSocket, di solito sullo stesso PC, porta **40001**.

## Impostazione

**Impostazioni › Radio (server) › Collegamento CAT** → **TCI (ExpertSDR, Thetis e altri SDR)**:

- **Indirizzo** del programma della radio (di serie `127.0.0.1`) e **porta TCI** (di serie `40001`);
- **Audio della radio via TCI**: l'audio di ricezione e di trasmissione passa dal TCI, senza schede audio né cavi virtuali sul server.

Nel programma della radio va attivato il server TCI (ExpertSDR: Opzioni › TCI; Thetis: Setup › Network › TCI).

## Cosa passa dal TCI

| Funzione | Comandi TCI |
|---|---|
| frequenza VFO A e VFO B | `vfo` |
| modo (USB, LSB, CW, CW-R, AM, FM, DIGU, DIGL…) | `modulation` |
| larghezza del filtro (filtro largo in CW) | `rx_filter_band` |
| split | `split_enable` |
| potenza TX in % | `drive` |
| PTT («CAT» o «TCI») | `trx` (con l'audio TCI: `trx:0,true,tci`) |
| S-meter | `rx_smeter` / `rx_channel_sensors` |
| potenza in uscita e **ROS** (anche per la protezione ROS) | `tx_sensors` |
| testo CW e velocità | `cw_macros`, `cw_macros_stop`, `cw_macros_speed` |
| audio | flussi binari: ricezione, trasmissione su richiesta (`TX_CHRONO`), a 8 kHz mono quando il programma lo accetta, altrimenti convertito |

Il server crea un piccolo «rigctld» interno che traduce i comandi Hamlib nel TCI: per questo il resto del programma funziona come con Hamlib, compresi i **programmi digitali** del client (WSJT-X, JTDX, fldigi…). Per i comandi che non riguardano la radio (`\dump_state`, `\chk_vfo`) il server avvia un `rigctld` di prova (modello 1, «Dummy»); se Hamlib non è installato il TCI funziona lo stesso, ma i programmi digitali potrebbero non collegarsi.

## Limiti

- Il **pitch CW** non arriva dal TCI: va regolato uguale nel programma e nella radio.
- La potenza si regola in **percentuale** (il TCI non dà la potenza massima in watt).
- Il ROS e la potenza in uscita arrivano solo se il programma della radio manda `tx_sensors` (ExpertSDR con TCI 1.9 o successivo); senza, la protezione ROS non può intervenire.
- Con l'audio TCI le schede del server sono entrambe «TCI · audio della radio»: non si può avere una direzione su TCI e l'altra su una scheda.
- Nel registro `sessioni.log` le righe `TCI` dicono quando il collegamento si apre, è pronto o cade.
