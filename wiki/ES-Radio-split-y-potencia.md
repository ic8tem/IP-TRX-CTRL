# Radio, split y potencia

Todos los comandos hacia la radio pasan por el servidor, que usa **Hamlib** (`rigctld`) o, desde la 0.2.5, el **TCI** de los programas SDR (ver *Conexión TCI*). Al iniciar la conexión el servidor reconoce siempre el modelo de radio y qué comandos admite.

## VFO A, VFO B y split

| Comando | Efecto |
|---|---|
| **A/B** | intercambia los dos VFO |
| **A=B** | copia el VFO A en el VFO B |
| **SPLIT** | recepción en A, transmisión en B |

El split se ajusta como **desplazamiento** del VFO B respecto al VFO A:

- margen: de **−50 kHz** a **+50 kHz**;
- paso de **1 kHz** en los modos de fonía (SSB);
- paso de **0,1 kHz** en CW;
- botones − y +, casilla del valor; la rueda del ratón mueve un paso (desde la 0.3.0 ya no hay cursor, para una ventana más pequeña).

## Potencia TX

- Cursor de **1 a 100 W**.
- Si la radio, o Hamlib para ese modelo, no admite la conversión a vatios, el cursor pasa automáticamente a **1–100 %**.
- El comando (nivel `RFPOWER`) sale al soltar el cursor; tras cada cambio el valor se vuelve a leer de la radio y se muestra al lado, junto con el modelo y la unidad usada (p. ej. «IC-7300 · watt»).

## Protección ROE

Desde la 0.2.4 el servidor vigila la **ROE** (SWR) leída de la radio mientras transmite. La protección está **activa desde la instalación**.

| ROE | Qué ocurre |
|---|---|
| desde **1,9** | aviso: la barra de la ROE parpadea en rojo, en el servidor y en el cliente |
| desde **2,5** durante dos lecturas seguidas (un segundo aprox.) | bloqueo: el servidor quita el PTT, detiene mensajes WAV, texto y manipulador CW y el audio hacia la radio (también con PTT por VOX); en el cliente aparece una ventana |

La transmisión queda bloqueada hasta que el operador pulsa **«Sblocca la trasmissione»** (desbloquear la transmisión) en la ventana. Si la cerró con «Lascia bloccata» (dejar bloqueada), un clic en la barra de la ROE la vuelve a abrir. Tras desbloquear hay que pulsar de nuevo el PTT: aunque el pedal o el botón sigan pulsados, la radio no vuelve sola a transmitir.

Antes de desbloquear: revisar antena, cable y conectores, acoplador, y que la frecuencia esté en la banda de la antena.

**Impostazioni › Radio (server)**:

- casilla **Protezione ROS** (protección ROE) para activarla o desactivarla (al desactivarla el programa pide confirmación);
- **Avviso da ROS** (aviso desde ROE, por defecto 1,9) y **Blocco da ROS** (bloqueo desde ROE, por defecto 2,5); el bloqueo va de 1,2 a 10, el aviso queda por debajo del bloqueo.

Los ajustes valen en el servidor, que mide la ROE. Desde el cliente se pueden cambiar cuando está conectado a un servidor 0.2.4 o posterior: se envían al servidor al guardar. Cada bloqueo, desbloqueo y cambio de ajuste se escribe en `sessioni.log` con el prefijo `ROS`.

La protección solo funciona si la radio da la ROE a Hamlib (nivel `SWR`); si no la da, la barra se queda en «—» y `sessioni.log` lo indica una vez. No sustituye las protecciones internas de la radio y del amplificador.

## Seguridad en transmisión (desde la 0.3.0)

Decide el servidor, para todos los clientes. **Impostazioni › Sicurezza in trasmissione (server)** (seguridad en transmisión):

| Opción | Por defecto | Qué hace |
|---|---|---|
| **Tempo massimo di una trasmissione** (tiempo máximo de una transmisión) | activa, 180 s | cuenta las transmisiones mandadas por el programa (PTT, mensajes WAV, manipulador CW por línea serie, programas digitales). 15 segundos antes del final la línea bajo el PTT se pone roja; al vencer, el PTT se quita y **sigue quitado hasta que el operador lo suelta**. De 30 a 600 s |
| **Niente cambi di frequenza in trasmissione** (sin cambios de frecuencia en transmisión) | activa | mientras la radio transmite el servidor rechaza cambios de frecuencia, modo, filtro, VFO B, split y A/B; en el cliente aparece el aviso |
| **Precedenza a chi è alla radio** (prioridad a quien está en la radio) | activa, 3 minutos | si la radio transmite sin que el programa lo haya mandado (micrófono o PTT de la radio), o si se pulsa el PTT del programa en el servidor, el cliente no puede transmitir durante los minutos elegidos (de 1 a 30). El bloqueo se alarga mientras sigue la transmisión local; en el servidor un clic en la línea bajo el PTT lo quita antes |
| **Codice fisso in solo ascolto** (código fijo solo escucha) | activa | quien se empareja con el código fijo del servidor entra en **solo escucha**; lo habilita el servidor |

**Solo escucha**: en **Impostazioni › Abbinamento e registri** el servidor elige para cada cliente **«Può trasmettere»** (puede transmitir) o no. Un cliente en solo escucha oye la radio, cambia de frecuencia y usa el panadaptador, pero no transmite (PTT, mensajes, CW, programas digitales), no desbloquea la protección ROE, no cambia las memorias del servidor ni pide el intercambio de papeles.

Notas:

- La prioridad funciona con las radios que dicen por CAT cuándo transmiten (comando Hamlib `t`). El CW enviado al keyer de la radio cuenta como transmisión del programa durante la duración estimada del texto.
- Con el PTT por VOX el tiempo máximo quita el PTT del programa, pero es la radio la que decide cuándo deja de transmitir.
- El tiempo máximo no afecta a las transmisiones hechas por la propia radio (operador en la radio).
- Cada evento se escribe en `sessioni.log` (líneas `TEMPO MASSIMO`, `QSY rifiutato`, `PRECEDENZA`, `TRASMISSIONE remota rifiutata`).

## Mandos de la radio (desde la 0.3.0)

Fila de botones bajo la potencia (se oculta desde *Impostazioni › Finestra principale*). Son los comandos estándar de Hamlib, así que funcionan con las radios para las que Hamlib los admite:

| Botón | Comando Hamlib | Función |
|---|---|---|
| **NR** | función `NR` | reducción de ruido |
| **NB** | función `NB` | supresor de ruido (noise blanker) |
| **NOTCH** | función `ANF` | notch automático |
| **COMP** | función `COMP` | compresor del micrófono de la radio |
| **ATT** | nivel `ATT` | atenuador: cada clic pasa al valor siguiente que acepta la radio, luego apagado |
| **PRE** | nivel `PREAMP` | preamplificador, como el atenuador |
| **AGC** | nivel `AGC` | rápido, medio, lento |
| **SQL** | nivel `SQL` | squelch, 0–100 % |

Un botón gris significa que la radio (o Hamlib para ese modelo) no ofrece ese comando. El servidor lee estos valores cada 4 segundos y justo después de cada comando. Con el **CAT por TCI** funcionan NR, NB, NOTCH y SQL (comandos TCI `rx_nr_enable`, `rx_nb_enable`, `rx_anf_enable`, `sql_enable` y `sql_level`).

## Bandas, paso y memorias (desde la 0.3.0)

- **Bandas** de 160 m a 70 cm: un clic lleva a la última frecuencia y al último modo usados en esa banda, la primera vez a una frecuencia típica (p. ej. 14,200 MHz USB). Las últimas frecuencias se guardan en este PC.
- **Paso de sintonía** de 1 Hz a 25 kHz con los botones ◀ ▶ y la rueda sobre los botones; vale también para la rueda en el panadaptador.
- **Memorie** (memorias): «Salva…» (guardar) pide el nombre y dónde guardarla: **in questo PC** (en este PC, solo para ti) o **sul server** (en el servidor), común a todos los clientes. En la lista las memorias llevan la marca «PC» o «Server»; **Vai** (ir, o doble clic) las recupera, **Elimina** (eliminar) las quita. En el servidor, las memorias del servidor se gestionan directamente. Solo un cliente que puede transmitir cambia las memorias del servidor.

Fuente de los nombres de los comandos Hamlib: manual de `rigctl`/`rigctld` (funciones `NR`, `NB`, `ANF`, `COMP`; niveles `ATT`, `PREAMP`, `AGC`, `SQL`, `ALC`), https://hamlib.sourceforge.net/html/rigctl.1.html


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Radio-split-e-potenza) · [English](EN-Radio-split-and-power) · [Français](FR-Radio-split-et-puissance) · [Deutsch](DE-Funkgerät-Split-und-Leistung)
