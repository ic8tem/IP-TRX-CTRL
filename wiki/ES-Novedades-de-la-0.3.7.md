# Novedades de la 0.3.7

La 0.3.7 recoge las mejores soluciones de otros programas de control remoto: remoteAudio (DH1TW), PiRO (VK3TR), RustyRig, RigControl (K6BP) y CAESAR (RA0SMS). Lo que IP-TRX-CTRL ya tenía (Opus, varios oyentes, STUN y UPnP, puerto rigctl, panadaptador, macros CW, Icom en red) no se ha duplicado.

## VOX en el cliente (de remoteAudio)

Botón **VOX** junto al PTT del cliente: es la única forma de encenderlo, y en cada arranque está apagado. El PTT se activa solo cuando hablas y se suelta tras el tiempo de retención. Umbral (dBFS) y retención (ms) en **Impostazioni › VOX, squelch vocale e registrazione**. Usa auriculares: el programa reconoce e ignora el audio de la radio que vuelve al micrófono, pero con altavoz el VOX es poco fiable. Valen las seguridades del PTT (tiempo máximo, ROE, prioridad a quien está en la radio).

## Bitrate Opus (de remoteAudio)

En los Ajustes se elige el ancho del audio comprimido: 8, 12, **16** (de serie), 24 o 32 kbit/s.

## Notas entre quien opera y quien escucha (de PiRO y RustyRig)

Pestaña **Note**: mensajes breves («en 40 m FT8 hasta las 16:30», «¿la cojo yo después del net?»), también desde la página web. El servidor los guarda una hora y los envía a quien se conecta después.

## Los oyentes oyen también al operador (de PiRO)

Con oyentes conectados, la voz del operador que transmite les llega también, como en un net. No en modos de datos ni en CW. Se quita en **Impostazioni › Sicurezza in trasmissione (server)**.

## Reducción de ruido neuronal (HamNoise, de PiRO)

**Audio › NR neurale** (cliente): la red neuronal [HamNoise](https://github.com/e04/HamNoise) de e04, entrenada con señales HF, la misma del botón «HamNoise» de PiRO. Un modelo para fonía (SSB, AM, FM) y otro para CW, elegidos según el modo de la radio; en los modos de datos no actúa. El valor al lado (25-100 %) indica cuánto filtrar: con señales débiles un valor más bajo deja pasar más señal. Funciona en el PC del cliente, añade unos 60 ms de retardo y muy poca carga.

## Squelch de voz (de PiRO)

**Audio › Squelch vocale** (cliente): en SSB, AM y FM quita el soplido entre intervenciones. Abre cuando reconoce la periodicidad de una voz, no con portadoras, tonos CW o descargas: no hay umbral que ajustar. Retención ajustable.

## Grabación del audio (de RustyRig)

**Audio › ● Registra**: un archivo WAV a 8 kHz con recepción y transmisión, en la carpeta `registrazioni` junto a los ajustes (botón «Cartella»). Con **«Registra l'audio di ogni QSO»** la grabación empieza sola al escribir un indicativo en el [registro](ES-Novedades-de-la-0.3.6) y termina con «Registra», con el indicativo en el nombre del archivo.

## Conexión directa IPv6 con PCP (de RigControl)

Con IPv6 no hay NAT ni CGNAT: si servidor y cliente tienen IPv6 pública, el servidor escucha también ahí con los mismos puertos y el cliente lo prueba primero. El servidor pide al router que deje pasar los dos puertos con **PCP** (RFC 6887). Si el router no tiene PCP, hay que permitir los puertos en el cortafuegos IPv6 del router para el PC servidor; si IPv6 no responde, el cliente vuelve a IPv4 y no reintenta durante media hora. Se apaga en **Impostazioni › Porte aperte sul router**. Ver también [Puertos y conexión](ES-Puertos-y-conexión).

## Servidor sin ventana (de CAESAR)

Para una Raspberry Pi o un PC sin pantalla junto a la radio: se configura todo una vez con la ventana (papel de servidor, radio, audio, emparejamiento o código fijo) y luego:

```
IP-TRX-CTRL --senza-finestra
```

El paquete `.deb` instala también un servicio de systemd que arranca al encender y se reinicia solo:

```
sudo systemctl enable --now ip-trx-ctrl-server@USUARIO
journalctl -u ip-trx-ctrl-server@USUARIO -f
```

## WinKeyer para los programas de log (de CAESAR)

**Impostazioni › WinKeyer per i programmi di log (client)**. N1MM, DXLog, TR4W, Win-Test, QLog y otros ven un WinKeyer: el CW que envían sale de la radio a la velocidad que piden, y la tecla Esc del programa lo para.

- **Windows**: hace falta un par de puertos virtuales com0com (por ejemplo COM10 y COM11): uno en IP-TRX-CTRL, el otro en el programa de log.
- **Linux y macOS**: con el puerto **«virtuale»** el programa crea un puerto serie virtual; en el programa de log se indica la ruta `winkeyer` en la carpeta de ajustes.

> Funciones nuevas, compiladas pero aún no probadas con una radio real: si hay problemas adjunta el informe de problema.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Funzioni-nuove-della-0.3.7) · [English](EN-New-in-0.3.7) · [Français](FR-Nouveautés-de-la-0.3.7) · [Deutsch](DE-Neu-in-0.3.7)
