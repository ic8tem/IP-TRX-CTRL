# Ventana principal

Desde la 0.3.0 la ventana es más pequeña: se abre a **1080 × 760** puntos (antes 1240 × 816) y se puede estrechar hasta 900 × 520; si el contenido no cabe aparece la barra de desplazamiento. Está organizada así:

1. **Cabecera** en una línea: nombre del programa, estado de la conexión, LED UPnP, papel (CLIENT o SERVER), colores, **Impostazioni** (ajustes), **Inverti ruoli** (invertir papeles), **Help**. Debajo, cuando la hay, la línea de la **versión nueva** con **Aggiorna adesso** (actualizar ahora) (ver *Registros y solución de problemas*).
2. **Tres columnas**: radio · instrumentos y PTT · el otro PC.
3. **Panadaptador de la radio**, si está activado (ver *Panadaptador*).
4. **Pestañas** abajo: **Keyer**, **CW** (solo en modo CW), **Audio**, **Collegamento** (conexión).
5. Línea de **avisos** (servidor ocupado, transmisión no permitida, tiempo máximo…).

Las partes nuevas se ocultan desde **Impostazioni › Finestra principale** (ajustes › ventana principal): botones de bandas y paso, mandos de la radio, memorias, medidor ALC, panadaptador, control de versión nueva y actualizaciones automáticas. Así la ventana queda aún más baja.

## Radio

- **VFO A** en grande, con el modo. **Rueda sobre las cifras**: con el ratón sobre una cifra, la rueda la sube o baja, con acarreo (vale también para el VFO B).
- Casilla **Nuova QRG** (nueva frecuencia) en Hz, modo, **Imposta** (fijar).
- **Paso de sintonía** (de 1 Hz a 25 kHz) con los botones ◀ ▶ (también con la rueda sobre los botones).
- **Bandas** de 160 m a 70 cm: un clic lleva a la última frecuencia y al último modo usados en esa banda (la primera vez a una frecuencia típica). La banda en uso aparece resaltada.
- **VFO B** en una línea con **A/B**, **A=B**, **SPLIT** y el desplazamiento (−, valor, +). La explicación del split está en la ayuda del botón.
- **Potenza** (potencia) TX.
- **Mandos de la radio** (ver *Radio, split y potencia*): NR, NB, NOTCH, COMP, ATT, PRE, AGC, SQL.
- **Memorie** (memorias) (ver *Radio, split y potencia*).

## Instrumentos y PTT

- **S**, **Po** (potencia de salida), **ROS** (ROE), **ALC** (en transmisión), **Gain** (ganancia TX de la radio). La barra de la ROE parpadea con la protección ROE.
- Indicador **RX/TX**.
- **PTT** de «pulsar y mantener», también con la barra espaciadora. Bajo el PTT una línea dice cuánto dura la transmisión y el **tiempo máximo**, o por qué la transmisión **no está permitida** (solo escucha, operador en la radio). Cuando no está permitida el botón aparece atenuado y dice «PTT NON PERMESSO».
- En el cliente: método PTT y retardo configurados en el servidor.

## El otro PC

- Cliente: lista de **servidores emparejados** y **Abbina…** (emparejar).
- Indicativo, IP, hora de conexión, latencia, nivel del audio recibido.
- Recuadros plegables: **Reti dei due PC** (redes de los dos PC: IPv4/IPv6, recorrido del audio, versión del otro PC), **Metodo PTT e CW** (método PTT y CW, solo servidor), **Schede audio del server** (tarjetas de sonido del servidor).

## Pestañas

- **Keyer**: F1…F8 (WAV en fonía, macros en CW). Ver *Keyer, mensajes WAV y macros CW*.
- **CW**: aparece en modo CW y se abre sola; audio RX en una línea, pitch, ancho, velocidad, umbral, filtro de la radio, cascada y decodificador. F1…F8 funcionan también desde aquí. Ver *Modo CW*.
- **Audio**: micrófono y altavoz, volúmenes, refuerzo y niveles.
- **Collegamento** (conexión): estado, latencia, jitter, paquetes perdidos, búfer, broker MQTT y, en el servidor, el código de reserva.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Finestra-principale) · [English](EN-Main-window) · [Français](FR-Fenêtre-principale) · [Deutsch](DE-Hauptfenster)
