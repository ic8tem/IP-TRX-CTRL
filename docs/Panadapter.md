# Panadapter della radio

Dalla 0.3.0 la finestra può mostrare lo **spettro vero della radio** (panadapter), non quello dell'audio come la cascata CW: in alto lo spettro, sotto la cascata, in fondo la scala in kHz. La **linea rossa** è la frequenza di ricezione, la **fascia chiara** il filtro.

- **Clic** su un punto: sintonia lì (arrotondata al passo di sintonia, al massimo 1 kHz).
- **Rotella**: un passo di sintonia.
- **Intensità**: contrasto della cascata.

## Attivazione

Si attiva **a scelta**, in due punti:

1. **Sul server**: *Impostazioni › Panadapter della radio (server)* › casella **«Panadapter della radio per i client e per questa finestra»** (spenta all'installazione).
2. **Su ogni PC**: *Impostazioni › Finestra principale* › **«Panadapter della radio»** (acceso di serie: il riquadro compare solo se il server lo fornisce).

Il server manda lo spettro al client solo mentre il client lo mostra: al massimo 10 righe al secondo, circa 5 kB/s.

## Da dove arriva lo spettro

| Radio | Sorgente | Cosa serve |
|---|---|---|
| **SDR con CAT via TCI** (ExpertSDR, Thetis…) | flusso IQ del ricevitore 0 (`iq_start`, 48 000 campioni/s), trasformato in spettro con una FFT di 2048 punti attorno al centro del panorama (`dds`) | niente: basta il CAT via TCI |
| **Icom** con scopio (IC-7300, IC-705, IC-9700, IC-7610…) | dati della cascata della radio, comando CI-V `27 00` | una porta CI-V **che Hamlib non usa**: per esempio la seconda porta USB delle radio che la offrono come CI-V (va impostata nel menu della radio), oppure un'interfaccia CI-V separata. Nelle Impostazioni: porta, velocità e **indirizzo CI-V** della radio in esadecimale (IC-7300 `94`, IC-705 `A4`, IC-9700 `A2`, IC-7610 `98`: controlla nel menu della radio) |

Con la porta Icom il programma accende lo scopio (`27 10 01`) e l'uscita dei dati della cascata (`27 11 01`), e la spegne all'uscita. Lo scopio in modo **centro** mostra centro ± ampiezza; in modo **fisso** i due estremi. Se la frequenza è fuori dal campo dello scopio fisso, il riquadro lo dice.

Con altre marche (Yaesu, Kenwood, Elecraft…) per ora il panadapter c'è solo con il TCI.

**Funzione nuova, non ancora provata con tutte le radio**: se il riquadro resta su «In attesa dei dati», la riga di stato accanto al titolo e `sessioni.log` (righe `PANADAPTER`) dicono cosa manca (porta, velocità, indirizzo).

## Fonti

- Icom, *CI-V Reference Guide* IC-9700 e IC-7300MK2: comando `27 00` (dati della cascata: scopio principale o secondario, divisione e numero di divisioni, modo centro o fisso, frequenze, fuori campo, livelli da 0 a 160), `27 10` (scopio acceso/spento), `27 11` (uscita dei dati). Confrontate tra loro: concordano sul formato; la guida dice che via USB i dati arrivano divisi in 11 parti e via LAN tutti insieme.
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
- Libreria TCI `github.com/ftl/tci` (Go): comandi `iq_start`, `iq_stop`, `iq_samplerate` (48, 96, 192 kHz), `dds`, flusso binario di tipo 0 = IQ.
  https://pkg.go.dev/github.com/ftl/tci/client
