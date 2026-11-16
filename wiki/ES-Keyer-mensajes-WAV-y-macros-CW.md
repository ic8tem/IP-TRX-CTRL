# Keyer: mensajes WAV y macros CW

El keyer funciona **solo en el cliente**. Retoma el programa WavPTT, reescrito en C# dentro de IP-TRX-CTRL. Las mismas teclas F1…F8 cambian de función según el modo de la radio.

Mandos comunes: **F1…F8** en la ventana, **Ctrl+Alt+F1…F8** también desde otros programas, **Esc** interrumpe.

## Modos de fonía (SSB): mensajes WAV

- Cada tecla reproduce un archivo WAV; el audio va a la radio remota a través del servidor, como si se hablara al micrófono, y el PTT se gestiona automáticamente.
- Los archivos se convierten a 8 kHz al cargarlos.
- Cadena de audio: conversión a 8 kHz → **normalización** (pico a −1 dB del fondo de escala, así todos los archivos parten del mismo nivel) → compresor (apagado o de 3 a 13 dB) → filtro (ninguno, 300–2700, 500–2400, 500–2100 Hz) → refuerzo del cursor VOLUME → limitador.
- Refuerzo del cursor VOLUME (como WavPTT): ganancia = 1000^(volumen/100).

| Cursor | Ganancia |
|---|---|
| 0 | silencio |
| 33 | ×10 (+20 dB) |
| 50 | ×31,6 (+30 dB) |
| 66 | ×100 (+40 dB) |
| 100 | ×1000 (+60 dB) |

- El micrófono no pasa por esta cadena: TX gain y «Boost oltre soglia» (refuerzo por encima del umbral) valen solo para la voz.
- Varias teclas pulsadas seguidas se ponen en cola; el PTT se suelta al final del último mensaje.
- El botón **File WAV…** abre la ventana **Memorie WAV (F1 – F8)** (memorias WAV), retomada de WavPTT:
  - **…** elige el archivo para esa tecla;
  - **Play** / **Stop** escucha de prueba en la salida de audio local, sin PTT;
  - **Salva** (guardar) copia los archivos en la carpeta WAV junto al programa; **Annulla** (cancelar) cierra sin guardar.

## Modo CW: macros de texto

- Al pulsar F1…F8 el cliente sustituye las variables y **envía al servidor el texto de la macro como cadena**, junto con la velocidad elegida.
- El servidor lo transmite de una de dos formas, elegida en el recuadro PTT del servidor en **Invio CW** (envío CW):
  - **CAT**: el texto va al keyer interno de la radio (comando Hamlib `send_morse`, velocidad con `KEYSPD`);
  - **manipulador en la línea DTR o RTS** de un puerto serie: el servidor activa el PTT en el puerto indicado, espera el retardo PTT,
    manipula el CW en la línea elegida con los tiempos Morse a la velocidad recibida, espera 3 puntos y suelta el PTT.
    Varias macros seguidas quedan bajo el mismo PTT. Esc interrumpe enseguida: manipulador arriba y PTT suelto.
- El botón **Macro…** abre la ventana **Macro CW (F1 – F8)**: para cada tecla, el nombre que aparece en la tecla y el texto que se transmite. **Prova** (probar) reproduce el texto en local, sin transmitir.
- Variables utilizables en los textos:

| Variable | Significado |
|---|---|
| `{MYCALL}` | mi indicativo |
| `{CALL}` | el indicativo del corresponsal |
| `{NR}` | número progresivo |
| `{RST}` | informe |

- Bajo las teclas se ajustan la velocidad de transmisión, mi indicativo, el indicativo del corresponsal y el número.

### Puertos serie para PTT y CW (servidor)

| Ajuste | Efecto |
|---|---|
| PTT en RTS o DTR, puerto X | el servidor maneja la línea del puerto X |
| CW en DTR o RTS, puerto Y (o «mismo puerto que el PTT») | el servidor manipula el CW en la línea del puerto Y |
| PTT y CW en el **mismo puerto** | usan líneas opuestas: si el CW está en DTR el PTT va en RTS, y viceversa |
| puerto PTT igual al puerto del CAT | el PTT lo maneja rigctld (el puerto es suyo) |
| puerto CW igual al puerto del CAT | no se puede manipular el CW ahí: el CW pasa por CAT. Usa el segundo puerto serie de la radio (por ejemplo el puerto «Standard» de las Yaesu) o un adaptador USB-serie |

Las elecciones efectivas y los posibles cambios de línea se muestran bajo los ajustes y se escriben en `sessioni.log` (líneas `PTT/CW`).

## En el servidor

El servidor no tiene teclas F ni archivos WAV: guarda el método PTT hacia la radio (RTS, DTR, CAT, VOX o TCI), el retardo PTT y el modo de envío del CW (CAT o manipulador en DTR/RTS).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Keyer-messaggi-WAV-e-macro-CW) · [English](EN-Keyer-WAV-messages-and-CW-macros) · [Français](FR-Keyer-messages-WAV-et-macros-CW) · [Deutsch](DE-Keyer-WAV-Meldungen-und-CW-Makros)
