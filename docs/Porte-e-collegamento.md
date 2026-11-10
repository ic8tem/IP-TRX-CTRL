# Porte e collegamento

Il server e il client si collegano direttamente, senza un server intermedio per l'audio. Perché questo funzioni almeno uno dei due PC deve avere porte raggiungibili da internet. IP-TRX-CTRL le apre da solo con **UPnP** e controlla che funzionino davvero.

## UPnP all'avvio e LED

All'avvio, sia sul server sia sul client, il programma cerca il router con **UPnP** e con **NAT-PMP** (tre tentativi da 10 secondi) e mostra una finestra con il risultato:

- **UPnP attivo**: il router può aprire le porte da solo; la finestra indica il protocollo e l'IP esterno dichiarato dal router;
- **UPnP non trovato**: la finestra indica la causa più probabile e cosa fare:
  - rete di Windows impostata come «Pubblica» (Windows blocca le risposte del router: va messa «Privata»);
  - VPN o altre schede di rete virtuali attive;
  - UPnP spento sul router: va attivato dalla pagina del router (sul FRITZ!Box anche per il singolo PC).

In alto nella finestra principale il **LED «UPnP»** resta **verde lampeggiante** finché il router risponde (controllo ogni 60 secondi), è **rosso fisso** se UPnP manca e **grigio** durante la ricerca. Se UPnP era attivo e il router smette di rispondere compare di nuovo la finestra. Un clic sul LED la riapre in qualsiasi momento.

## Il server

All'avvio il server:

1. cerca il router con UPnP;
2. apre una porta casuale per il **controllo** (TCP) e una per l'**audio** (TCP, e la stessa porta in UDP);
3. si mette in ascolto e chiede a **due servizi esterni indipendenti** di collegarsi a quella porta: **ifconfig.co** e **portchecker.io** (entrambi open source). Basta che uno dei due entri perché la porta sia buona; se nessuno dei due entra il server chiude la porta e ne prova un'altra (fino a 6 volte per porta). Se nessuno dei due servizi risponde la porta viene tenuta, e il registro lo dice;
4. le **prime due porte utili** vengono pubblicate, cifrate, sul broker MQTT insieme all'indirizzo IP pubblico.

**Router senza UPnP.** Il server usa comunque due porte locali, le verifica dall'esterno e le pubblica: funzionano se sul router c'è una DMZ o un inoltro manuale verso il PC del server, e in ogni caso dai PC della **stessa rete locale**. Se nemmeno così il client entra, il server resta in attesa delle porte proposte dal client (collegamento inverso).

Quando le porte del server non sono raggiungibili da internet compare anche una finestra di avviso **«Da fuori casa questo server non è raggiungibile»**, con i numeri delle porte, l'indirizzo del PC e i tre rimedi (UPnP, DMZ, inoltro manuale). Il programma continua a funzionare per la rete locale e per il collegamento inverso.

**Stessa rete locale.** Il server pubblica anche il suo indirizzo di rete locale (per esempio 192.168.1.20). Molti router non permettono a un PC di casa di rientrare dall'indirizzo pubblico della casa stessa: in quel caso il client prova da solo l'indirizzo locale, sia nell'abbinamento sia nei collegamenti successivi.

## Il client e il ciclo delle porte

Il client legge sul broker MQTT l'indirizzo e le porte del server e prova a collegarsi. Se non ci riesce comincia il **ciclo delle porte**:

1. il client apre con UPnP due porte sul **suo** router, le verifica dall'esterno come il server e le pubblica sul broker MQTT, con il numero del giro e la porta del server che non ha raggiunto;
2. il server legge la proposta e prova a collegarsi alle porte del client (**collegamento inverso**: il server apre la connessione, ma la cifratura e l'autenticazione restano le stesse);
3. se nemmeno questo riesce, il server chiude le sue porte, ne apre e verifica due nuove e le ripubblica;
4. il client riprova con le porte nuove del server, e così via.

Il ciclo dura **20 giri**. Dopo il ventesimo il client mostra la finestra **«Nessuna porta è apribile»** con il pulsante **Exit**, che chiude il programma.

Ogni passaggio è scritto nel registro `sessioni.log` con il prefisso `PORTE` (Impostazioni › Apri cartella dati), per esempio:

```
PORTE giro 3/20: il client non raggiunge il server (93.45.x.x:41234)
PORTE IP-TRX-CTRL client controllo: porta TCP 52310 aperta sul router (tentativo 1)
PORTE verifica esterna TCP 151.x.x.x:52310: raggiungibile
PORTE giro 3/20: il client propone 151.x.x.x porte 52310/38877 sul broker MQTT (consegnato)
PORTE collegamento inverso riuscito: il server si è collegato alle porte del client 151.x.x.x:52310
```

## Audio su UDP

A collegamento fatto i due PC provano a passare l'audio su **UDP**, che soffre meno dei ritardi di TCP:

- ognuno chiede il proprio indirizzo pubblico a due server **STUN** dallo stesso socket UDP (aperto sulla porta audio, che UPnP ha aperto anche in UDP);
- gli indirizzi candidati vengono scambiati sul canale di controllo cifrato;
- i due PC «bussano» l'uno verso l'altro (perforazione del NAT); con un NAT simmetrico si provano anche le porte successive;
- solo quando **entrambi** confermano di ricevere, l'audio passa su UDP (cifrato AES-256-GCM).

Se UDP non passa, o se il client non riceve più pacchetti UDP per 4 secondi, l'audio resta (o torna) su TCP. Il percorso in uso compare nella riga della rete dell'altro PC: «audio UDP» o «audio TCP».

## Cosa fare se non si collega

- Attiva **UPnP** sul router di almeno uno dei due PC (meglio su tutti e due).
- Se l'operatore usa il **CGNAT** (IP condiviso, frequente su rete mobile e FWA) quel PC non può ricevere collegamenti: deve essere l'altro ad aprire le porte.
- Se entrambi i PC sono dietro CGNAT o senza UPnP, il collegamento diretto non è possibile.
