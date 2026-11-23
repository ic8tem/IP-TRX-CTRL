# Funzioni nuove della 0.3.7

La 0.3.7 riprende le soluzioni migliori di altri programmi per il controllo remoto della radio: remoteAudio (DH1TW), PiRO (VK3TR), RustyRig, RigControl (K6BP) e CAESAR (RA0SMS), più la riduzione del rumore neurale HamNoise (e04). Le funzioni che IP-TRX-CTRL aveva già (Opus, più ascoltatori, STUN e UPnP, porta rigctl, panadapter, macro CW, Icom in rete) non sono state duplicate.

## VOX sul client (da remoteAudio)

Pulsante **VOX** accanto al PTT del client: è l'unico modo di accenderlo, e a ogni avvio è spento. Il PTT parte da solo quando parli e si rilascia dopo il tempo di tenuta. Soglia (dBFS) e tenuta (ms) in **Impostazioni › VOX, squelch vocale e registrazione**. Serve la cuffia: il programma riconosce e ignora l'audio della radio che rientra nel microfono, ma con l'altoparlante il VOX resta poco affidabile. Valgono le stesse sicurezze del PTT (tempo massimo, ROS, precedenza a chi è alla radio).

## Bitrate Opus (da remoteAudio)

In Impostazioni si sceglie la banda dell'audio compresso: 8, 12, **16** (di serie), 24 o 32 kbit/s.

## Note tra chi opera e chi ascolta (da PiRO e RustyRig)

Scheda **Note**: brevi messaggi («sono sui 40 m in FT8 fino alle 16:30», «dopo il net posso prenderla io?»), anche dalla pagina web. Il server li tiene per un'ora e li manda a chi si collega dopo.

## Chi ascolta sente anche chi trasmette (da PiRO)

Con più ascoltatori collegati, la voce dell'operatore che trasmette arriva anche a loro, come in un net. Non nei modi dati e in CW. Si toglie in **Impostazioni › Sicurezza in trasmissione (server)**.

## Riduzione del rumore neurale (HamNoise, da PiRO)

Scheda **Audio › NR neurale** (client): la rete neurale [HamNoise](https://github.com/e04/HamNoise) di e04, addestrata su segnali HF, la stessa del pulsante «HamNoise» di PiRO. Un modello per la fonia (SSB, AM, FM) e uno per il CW, scelti in base al modo della radio; nei modi dati non agisce. Il valore accanto (25-100 %) dice quanto filtrare: con i segnali deboli un valore più basso lascia passare più segnale. Gira sul PC del client, aggiunge circa 60 ms di ritardo e pochissimo carico.

## Squelch vocale (da PiRO)

Scheda **Audio › Squelch vocale** (client): in SSB, AM e FM toglie il fruscio tra un intervento e l'altro. Apre quando riconosce la periodicità della voce, non con portanti, toni CW o scariche: non c'è una soglia da regolare. Tenuta regolabile.

## Registrazione dell'audio (da RustyRig)

Scheda **Audio › ● Registra**: un file WAV a 8 kHz con ricezione e trasmissione, nella cartella `registrazioni` accanto alle impostazioni (pulsante «Cartella»). Con **«Registra l'audio di ogni QSO»** la registrazione parte da sola quando scrivi un nominativo nel [registro](IT-Funzioni-nuove-della-0.3.6) e si chiude con «Registra», con il nominativo nel nome del file.

## Collegamento diretto IPv6 con PCP (da RigControl)

Con IPv6 non c'è NAT e nemmeno CGNAT: se server e client hanno un indirizzo IPv6 pubblico, il server ascolta anche lì con le stesse porte e il client lo prova per primo. Il server chiede al router di lasciar passare le due porte con **PCP** (RFC 6887). Se il router non ha PCP, le porte vanno permesse nel firewall IPv6 del router per il PC server; se IPv6 non risponde, il client torna su IPv4 e non riprova per mezz'ora. Si spegne in **Impostazioni › Porte aperte sul router**. Vedi anche [Porte e collegamento](IT-Porte-e-collegamento).

## Server senza finestra (da CAESAR)

Per un Raspberry Pi o un PC senza schermo accanto alla radio: si imposta tutto una volta con la finestra (ruolo server, radio, audio, abbinamento o codice fisso), poi:

```
IP-TRX-CTRL --senza-finestra
```

Con il pacchetto `.deb` c'è anche il servizio di systemd, che parte all'accensione e riparte da solo:

```
sudo systemctl enable --now ip-trx-ctrl-server@NOMEUTENTE
journalctl -u ip-trx-ctrl-server@NOMEUTENTE -f
```

## WinKeyer per i programmi di log (da CAESAR)

**Impostazioni › WinKeyer per i programmi di log (client)**. N1MM, DXLog, TR4W, Win-Test, QLog e gli altri vedono un WinKeyer: il CW che mandano parte dalla radio, alla velocità che chiedono, e il tasto «Esc» del programma lo ferma.

- **Windows**: serve una coppia di porte virtuali com0com (per esempio COM10 e COM11). In IP-TRX-CTRL una, nel programma di log l'altra.
- **Linux e macOS**: con la porta **«virtuale»** il programma crea una porta seriale finta; nel programma di log si indica il percorso `winkeyer` nella cartella delle impostazioni.

> Funzioni nuove, provate in compilazione ma non ancora con una radio vera: in caso di problemi allega il «Rapporto per un problema».


---

[Indice](Italiano) · Altre lingue: [English](EN-New-in-0.3.7) · [Español](ES-Novedades-de-la-0.3.7) · [Français](FR-Nouveautés-de-la-0.3.7) · [Deutsch](DE-Neu-in-0.3.7)
