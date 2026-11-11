# Abbinamento e ritrovo tramite MQTT

L'abbinamento si fa una sola volta per ogni server: dopo, chiavi, porte e riconnessioni sono automatiche.

## Come si abbina

1. Sul **server** si apre la finestra *Abbinamento*: compare un numero di 10 cifre (es. `482 913 0571`, dalla 0.3.0; prima era un codice lungo come `R4VGZ-TRGSF-…`). In alternativa il client può usare il codice fisso del server di 12 cifre, se è attivo (vedi sotto).
2. Sul **client** si scrive quel numero e si preme **Abbina** (vale 10 minuti).
3. I due programmi si scambiano una chiave segreta (ECDH P-256) e da quel momento tutto il traffico è cifrato con AES-256-GCM.

## Il ruolo del broker MQTT

Il server non ha un indirizzo fisso. Per farsi trovare lascia il proprio indirizzo IP e le porte, **cifrati**, su un canale di un broker MQTT pubblico. Il nome del canale è ricavato dalla chiave segreta: solo chi ha abbinato i due PC sa dove guardare e sa leggere il contenuto. Se un broker non risponde, il programma passa al successivo.

Anche la fase di abbinamento passa dal broker: il client annuncia la propria presenza sul canale ricavato dal numero, il server risponde con l'indirizzo aggiornato (cifrato), poi il client apre il collegamento diretto. Dalla 0.3.0 il numero non contiene l'indirizzo: arriva solo da qui.

## Il registro del dialogo

La finestra di abbinamento mostra, riga per riga, il dialogo con il broker e con il server:

```
20:01:02.104  ▶  test.mosquitto.org:1883  CONNECT  id=aal-3f9a21
20:01:10.106  ✗  nessuna risposta in 8 s → broker successivo
20:01:10.110  ▶  broker.hivemq.com:1883  CONNECT  id=aal-3f9a21
20:01:10.296  ◀  CONNACK  accettato
20:01:10.298  ▶  SUBSCRIBE  aal/pair/7c1e…d4/srv  QoS 1
20:01:10.417  ◀  SUBACK  QoS 1
20:01:10.419  ▶  PUBLISH  aal/pair/7c1e…d4/cli  HELLO cifrato 88 B
20:01:11.020  ◀  PUBLISH  …/srv  server presente · IP 93.41.127.18
                    porte 51234 / 51235 · UPnP ok · versione 3.1
20:01:11.022  ▶  TCP 93.41.127.18:51234  apertura canale di controllo
20:01:11.140  ◀  handshake ECDH P-256 ok · chiave AES-256 derivata
20:01:11.143  ✓  Abbinamento completato
```

`▶` = inviato, `◀` = ricevuto, `✗` = errore, `✓` = completato. Con **Copia registro** il testo va negli appunti, utile per capire dove si ferma un abbinamento non riuscito. Il registro può essere salvato anche nel log di sessione.

## Problemi tipici

| Cosa si vede nel registro | Causa probabile |
|---|---|
| Nessun broker risponde | Connessione internet assente o porta 1883 bloccata dalla rete |
| Il broker risponde ma il server no | Il programma server è chiuso, oppure non è ancora in attesa di abbinamento |
| Il server risponde ma il TCP non si apre | Porta non aperta sul router (UPnP disattivato o rete del provider con IP condiviso): dalla 0.2.7 parte il ciclo delle porte |
| «nessuna risposta» in rete locale con lo stesso IP pubblico | Il firewall di Windows del server scarta le connessioni: sul server Impostazioni › Rete e audio › «Consenti nel firewall di Windows» |
| «connessione rifiutata» | Il programma server è chiuso o ascolta su un'altra porta |
| «le porte del server NON sono raggiungibili» e «CGNAT» | Il server non può ricevere collegamenti da internet: serve una porta aperta sul router del client (UPnP, DMZ o inoltro), che il ciclo delle porte usa per il collegamento inverso |

## Ciclo delle porte nell'abbinamento (dalla 0.2.7)

Se il collegamento diretto non riesce, l'abbinamento non si ferma: partono fino a **20 giri** di scambio di porte, sempre attraverso il broker MQTT e cifrati con la chiave ricavata dal numero di abbinamento.

1. Il client apre una porta **nuova** sul suo router (a giri alterni una porta comune che spesso passa i filtri, come 443, 80, 8080, 8443, oppure una a caso), la verifica da internet e la propone al server.
2. Il server prova a collegarsi a quella porta: è il **collegamento inverso**, l'unico possibile quando il server è dietro CGNAT.
3. Se non riesce, il server apre anche lui una porta **nuova**, la verifica da internet e comunica al client numero ed esito.
4. Il client prova subito la porta nuova del server (anche in rete locale).

Nessuna porta viene provata due volte, né sul client né sul server. Se la rete del client lascia uscire solo alcune porte, il server prova prima quelle. Una porta del server che risulta raggiungibile diventa la sua porta di controllo. Alla fine, se nulla ha funzionato, l'ultima riga del registro dice perché (per esempio: server dietro CGNAT e router del client senza UPnP).

## Numero di abbinamento a 10 cifre (dalla 0.3.0)

Il server mostra un **numero di 10 cifre** (per esempio `482 913 0571`) al posto del vecchio codice lungo.

- Vale **10 minuti** e **una sola volta**; scaduto, si preme «Riprova» sul server per averne uno nuovo.
- **Non contiene l'indirizzo** del server: il client lo riceve **cifrato** (AES-256-GCM) dal broker MQTT. Senza almeno un broker raggiungibile l'abbinamento non si può fare.
- Dal numero si ricava la chiave con **PBKDF2-SHA256** lento (200 000 passaggi) usando come sale la finestra di 10 minuti in cui il numero è nato. Provare tutti i 10 miliardi di numeri richiederebbe troppo tempo, e una tabella calcolata in anticipo non serve perché ogni finestra ha il suo sale. Il client prova la finestra attuale, le due precedenti e la successiva, per l'attesa e per orologi un po' diversi (qualche secondo di calcolo).
- Il resto dell'abbinamento è come prima: scambio ECDH P-256 autenticato con la chiave ricavata dal numero, chiave a 256 bit per le sessioni.

## Server abbinati (client, dalla 0.3.0)

Il client tiene un **elenco di server**, ognuno con la sua chiave a 256 bit custodita come prima (DPAPI su Windows, portachiavi su macOS, file cifrato su Linux).

- Nel riquadro del server della finestra principale si sceglie il server: il client è collegato a **uno solo per volta**, e scegliendone un altro il collegamento in corso si chiude.
- **Abbina nuovo…** aggiunge un server con il suo numero; i precedenti restano.
- In **Impostazioni › Abbinamento e registri** si rinomina un server o lo si toglie dall'elenco (la sua chiave viene cancellata).
- Il nome iniziale è il nominativo del server, appena il server lo comunica.
- L'abbinamento fatto con una 0.2.x diventa da solo il primo server dell'elenco: non serve abbinare di nuovo.

## Client abbinati (server, dalla 0.3.0)

Anche il server tiene un **elenco di client**, ognuno con la sua chiave: un abbinamento nuovo **non** toglie l'accesso agli altri.

- Si collega **un client per volta** (la radio è una). Se un secondo client prova a entrare mentre un altro opera, riceve «Il server è occupato: è collegato …» con il nominativo di chi opera, e riprova da solo ogni 30 secondi.
- Lo stesso client che si ricollega (per esempio dopo un cambio di rete) riprende il suo posto senza attese.
- In **Impostazioni › Abbinamento e registri** si vede l'elenco, con il nominativo, il modo dell'abbinamento (numero temporaneo, codice fisso, scambio dei ruoli, abbinamento precedente) e l'ultimo uso. Si rinomina un client o gli si **toglie l'accesso**: la sua chiave viene cancellata e, se è collegato, viene scollegato subito.
- **Abbina un nuovo client** apre la finestra con un numero temporaneo; i client già abbinati restano.
- L'abbinamento fatto con una 0.2.x diventa da solo il primo client dell'elenco.
- «Esporta chiavi per le autorità» esporta le chiavi di tutti i client.

## Codice fisso del server (dalla 0.3.0)

Oltre al numero temporaneo, il server può avere un **codice fisso di 12 cifre** (per esempio `4829 1305 7712`), come certi programmi di controllo remoto. Si attiva in **Impostazioni › Abbinamento e registri** con «Accetta nuovi client con il codice fisso del server»; all'installazione è **disattivato**.

- Il codice vale **sempre**, finché non lo cambi («Cambia codice») o non disattivi l'opzione. Chi lo riceve si abbina quando vuole, anche dopo giorni, scrivendolo nella stessa casella del numero; poi entra nell'elenco dei client e non serve più.
- Funziona solo con il programma server avviato e almeno un broker MQTT raggiungibile, come il numero temporaneo; anche qui l'indirizzo arriva al client solo cifrato.
- La chiave si ricava dal codice con PBKDF2-SHA256 da 600 000 passaggi (tre volte il numero temporaneo) e il codice ha due cifre in più: provare tutti i codici richiederebbe anni di calcolo.
- **Sicurezza:** è comodo ma meno sicuro del numero temporaneo, perché chi scopre il codice può abbinarsi in qualunque momento. Dallo solo a persone fidate, cambialo se è circolato troppo e togli dall'elenco i client che non devono più entrare. Cambiare il codice non toglie l'accesso ai client già abbinati.
- Le cifre del codice sono custodite come le chiavi (DPAPI, portachiavi di macOS, file cifrato su Linux), non nel file delle impostazioni.

## Indirizzi cifrati

L'indirizzo IP del server non passa mai in chiaro: sui broker MQTT (abbinamento, ritrovo dopo un cambio di IP) è cifrato con AES-256-GCM, e i nomi dei canali MQTT sono impronte ricavate dalla chiave, che non rivelano né l'indirizzo né il nominativo. Anche il codice di riserva per quando i broker non rispondono contiene l'indirizzo cifrato.

