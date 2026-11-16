# Modos digitales, pedal PTT y manipulador CW

Estas funciones afectan al **cliente** (el PC del operador).

## Programas digitales (WSJT-X, JTDX, JS8Call, fldigi…)

El cliente ofrece a los programas digitales dos cosas:

- **un rigctld local** en `127.0.0.1:4532`: cada comando CAT viaja cifrado hasta el rigctld del servidor. Los comandos de PTT no se reenvían: se convierten en el PTT de IP-TRX-CTRL, con las mismas seguridades que el botón PTT;
- **dos recorridos de audio** por cables virtuales: la recepción de la radio va hacia el programa, y durante la transmisión pedida por el programa el audio del programa va a la radio en lugar del micrófono.

Se activa en Impostazioni › **Programmi digitali** (programas digitales) › «Usa programmi digitali» (usar programas digitales).

### Ajustes en el programa digital

| Elemento | Valor |
|---|---|
| Radio | Hamlib **NET rigctl** |
| Dirección | `127.0.0.1:4532` (el puerto se cambia en los ajustes de IP-TRX-CTRL) |
| PTT | CAT |
| Entrada de audio | el cable que IP-TRX-CTRL usa como «verso il programma» (hacia el programa) |
| Salida de audio | el cable que IP-TRX-CTRL usa como «dal programma» (desde el programa) |

### Cables de audio virtuales por sistema

| Sistema | Cable | Notas |
|---|---|---|
| Windows 64 bits | **VB-CABLE** (VB-Audio, donationware) | El instalador lo propone como tarea opcional, lo descarga del sitio del fabricante y lo omite si ya está instalado. Tras la instalación hace falta reiniciar. |
| Windows 32 bits | — | Función no disponible. |
| macOS | **BlackHole 2ch** (MIT) | `brew install blackhole-2ch`, luego reinicia IP-TRX-CTRL. |
| Linux / Raspberry Pi | creados por el programa | Dos «null sink» `IPTRX_RX` e `IPTRX_TX` con `pactl` (PipeWire o PulseAudio, paquete `pulseaudio-utils`). En el programa digital: entrada «Monitor of IP-TRX-CTRL_RX», salida «IP-TRX-CTRL_TX». |

**Un solo cable** (VB-CABLE gratuito): IP-TRX-CTRL escribe la recepción en «CABLE Input» y lee de «CABLE Output»; el programa digital hace lo mismo. Funciona en **semidúplex**: mientras el programa transmite, IP-TRX-CTRL deja de enviarle la recepción. **Dos cables** (por ejemplo VB-CABLE A+B): uno por dirección; el botón «Scegli i cavi in automatico» (elegir los cables automáticamente) los asigna.

## Pedal PTT y manipulador CW mecánico

Impostazioni › **Pedale PTT e tasto CW** (pedal PTT y manipulador CW). Se usa un puerto serie del cliente (también un adaptador USB-serie) como entrada:

| Contacto | Conexión | Entrada que elegir |
|---|---|---|
| Pedal PTT | entre **RTS** (pin 7 del DB9) y **CTS** (pin 8) | CTS |
| Manipulador vertical | entre **DTR** (pin 4) y **DSR** (pin 6) | DSR |
| como alternativa | entre DTR (pin 4) y **DCD** (pin 1) | DCD |

El programa activa RTS y DTR y lee las entradas cada milisegundo. El pedal actúa como el botón PTT. El manipulador envía al servidor cada pulsación y suelta con su instante; el servidor los repite en el puerto CW de la radio con un **retardo fijo** (250 ms de serie, ajustable) que absorbe las variaciones de retardo de internet, así la duración de puntos y rayas sigue siendo exacta. El PTT de la radio se activa antes del primer signo y se suelta tras 800 ms sin signos; un manipulador que queda pulsado más de 10 segundos se suelta por seguridad.

**Puerto virtual de otro programa.** En lugar del hardware se puede usar el puerto virtual de un programa que maneja PTT o CW con RTS y DTR. En Windows hace falta un par de puertos virtuales **com0com**: el programa usa un puerto del par, IP-TRX-CTRL el otro (RTS llega a CTS, DTR a DSR). En Linux y macOS los pseudopuertos (pty) no tienen líneas de control: hace falta un adaptador real.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Programmi-digitali-e-pedale) · [English](EN-Digital-modes-and-pedal) · [Français](FR-Modes-numériques-et-pédale) · [Deutsch](DE-Digitalbetrieb-und-Pedal)
