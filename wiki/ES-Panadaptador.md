# Panadaptador de la radio

Desde la 0.3.0 la ventana puede mostrar el **espectro real de la radio** (panadaptador), no el del audio como la cascada CW: arriba el espectro, debajo la cascada, al fondo la escala en kHz. La **línea roja** es la frecuencia de recepción, la **franja clara** el filtro.

- **Clic** en un punto: sintonía allí (redondeada al paso de sintonía, como máximo 1 kHz).
- **Rueda**: un paso de sintonía.
- **Intensità** (intensidad): contraste de la cascada.

## Activación

Se activa **de forma opcional**, en dos sitios:

1. **En el servidor**: *Impostazioni › Panadapter della radio (server)* › casilla **«Panadapter della radio per i client e per questa finestra»** (panadaptador para los clientes y para esta ventana; apagada tras la instalación).
2. **En cada PC**: *Impostazioni › Finestra principale* › **«Panadapter della radio»** (encendido de serie: el recuadro aparece solo si el servidor lo ofrece).

El servidor envía el espectro al cliente solo mientras el cliente lo muestra: como máximo 10 líneas por segundo, unos 5 kB/s.

## De dónde llega el espectro

| Radio | Fuente | Qué hace falta |
|---|---|---|
| **SDR con CAT por TCI** (ExpertSDR, Thetis…) | flujo IQ del receptor 0 (`iq_start`, 48 000 muestras/s), convertido en espectro con una FFT de 2048 puntos alrededor del centro del panorama (`dds`) | nada: basta el CAT por TCI |
| **Icom** con osciloscopio (IC-7300, IC-705, IC-9700, IC-7610…) | datos de la cascada de la radio, comando CI-V `27 00` | un puerto CI-V **que Hamlib no use**: por ejemplo el segundo puerto USB de las radios que lo ofrecen como CI-V (hay que ajustarlo en el menú de la radio), o una interfaz CI-V aparte. En los ajustes: puerto, velocidad y **dirección CI-V** de la radio en hexadecimal (IC-7300 `94`, IC-705 `A4`, IC-9700 `A2`, IC-7610 `98`: compruébalo en el menú de la radio) |

Con el puerto Icom el programa enciende el osciloscopio (`27 10 01`) y la salida de datos de la cascada (`27 11 01`), y los apaga al salir. El osciloscopio en modo **centro** muestra centro ± ancho; en modo **fijo** los dos extremos. Si la frecuencia está fuera del campo del osciloscopio fijo, el recuadro lo indica.

Con otras marcas (Yaesu, Kenwood, Elecraft…) por ahora el panadaptador solo existe con TCI.

**Función nueva, aún no probada con todas las radios**: si el recuadro se queda en «In attesa dei dati» (esperando datos), la línea de estado junto al título y `sessioni.log` (líneas `PANADAPTER`) dicen qué falta (puerto, velocidad, dirección).

## Fuentes

- Icom, *CI-V Reference Guide* IC-9700 e IC-7300MK2: comando `27 00` (datos de la cascada: osciloscopio principal o secundario, división y número de divisiones, modo centro o fijo, frecuencias, fuera de campo, niveles de 0 a 160), `27 10` (osciloscopio encendido/apagado), `27 11` (salida de datos). Comparadas entre sí: coinciden en el formato; la guía dice que por USB los datos llegan divididos en 11 partes y por LAN todos juntos.
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
- Biblioteca TCI `github.com/ftl/tci` (Go): comandos `iq_start`, `iq_stop`, `iq_samplerate` (48, 96, 192 kHz), `dds`, flujo binario de tipo 0 = IQ.
  https://pkg.go.dev/github.com/ftl/tci/client


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Panadapter) · [English](EN-Panadapter) · [Français](FR-Panadapter) · [Deutsch](DE-Panadapter)
