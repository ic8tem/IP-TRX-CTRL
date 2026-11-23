# Novedades de la 0.3.6

## Audio Opus

Entre dos programas 0.3.6 o posteriores el audio viaja comprimido con **Opus** (16 kbit/s, voz a 8 kHz) en lugar de PCM: menos ancho de banda, corrección de errores (FEC) y relleno de huecos cuando se pierde un paquete. Con versiones anteriores y con la página web el audio sigue en PCM a 8 kHz. No hay nada que configurar.

## Más oyentes en el mismo servidor

**Impostazioni › Sicurezza in trasmissione (server) › «Altri client e pagine web possono collegarsi in solo ascolto»** (Ajustes › Seguridad en transmisión (servidor) › «Otros clientes y páginas web pueden conectarse solo para escuchar»; activado de serie). Mientras un operador usa la radio, hasta **8** clientes emparejados o páginas web más pueden conectarse: oyen el audio y ven frecuencia e instrumentos, pero no transmiten ni cambian nada. En su ventana aparece «tu sei in solo ascolto: trasmette X» (solo escuchas: transmite X); en el servidor el recuadro del otro PC muestra cuántos escuchan. Cuando el operador se desconecta, los oyentes se desconectan y el primero que vuelve a conectarse pasa a ser operador.

## Manipulador CW USB y MIDI

**Impostazioni › Tasto CW USB e MIDI** (Ajustes › Manipulador CW USB y MIDI, cliente):

- **Modo teclado** (VBand, Vail y similares): Ctrl izquierdo = punto, Ctrl derecho = raya.
- Manipulador **yámbico modo A o B**, o **manipulador vertical**; velocidad 5–60 WPM; intercambio punto/raya; tono de escucha en el PC al pitch CW.
- **MIDI** (Vail en modo MIDI, TinyMIDI): Do = vertical, Do# = punto, Re = raya. Windows y Linux (el usuario debe estar en el grupo `audio`), no en Mac.

El manipulador funciona en el cliente y envía al servidor los tiempos de la llave, que el servidor repite con el retardo fijo de la llave CW (como la llave mecánica por puerto serie, ver [Modos digitales y pedal](ES-Modos-digitales-y-pedal)).

La **página web** también tiene la llave: Ajustes › Tasto CW, con modo teclado (Ctrl o `[` y `]`) y MIDI con Chrome o Edge.

## Registro de QSO

Botón **«Registro»** arriba en la ventana principal: indicativo, RST enviado y recibido, locator, nombre, QTH y notas. Frecuencia, banda, modo y horas vienen de la radio.

- **Registra** guarda el QSO en `registro.adi`, en la carpeta de ajustes, y si está activo lo envía a los programas de log con el **protocolo UDP de WSJT-X** (mensajes «QSO Logged» y «Logged ADIF», puerto 2237 de serie): GridTracker, JTAlert, Log4OM, N1MM, HRD y otros lo registran como un QSO de WSJT-X.
- **Esporta ADIF…** guarda un archivo para **LoTW** (TQSL), QRZ y Club Log.
- **Impostazioni › Registro dei QSO**: dirección, puerto y tu locator. Si WSJT-X está abierto en el mismo PC, añade un segundo puerto en el programa de log o usa el reenvío multicast.

## Radios Icom en red

Para IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905 y otras Icom con red: el servidor habla con la radio como RS-BA1 y wfview, **sin cables USB y sin un PC junto a la radio**.

1. En la radio: Set › Network › **Network Control ON** y un usuario en Network User1 (nombre y contraseña, hasta 16 caracteres).
2. **Impostazioni › Radio Icom in rete (server)**: dirección de la radio, puerto 50001, usuario y contraseña (queda cifrada en ese PC).
3. En **Radio (server)**: el modelo Hamlib de la radio y PTT «CAT».
4. Guarda, vuelve a abrir los Ajustes y elige **«Icom LAN · audio della radio»** como tarjeta de sonido del servidor (recepción y transmisión a 8 kHz).

El CAT pasa por rigctld, conectado a un puente interno hacia el flujo CI-V de la radio. El protocolo está reescrito en C# siguiendo [wfview](https://gitlab.com/eliggett/wfview) (GPL 3) y la documentación de [kappanhang](https://github.com/nonoo/kappanhang); ver [Licencias](ES-Licencias).

> Función nueva, probada hasta ahora sin una radio real: si hay problemas adjunta el informe de problema (líneas ICOM de `sessioni.log`).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Funzioni-nuove-della-0.3.6) · [English](EN-New-in-0.3.6) · [Français](FR-Nouveautés-de-la-0.3.6) · [Deutsch](DE-Neu-in-0.3.6)
