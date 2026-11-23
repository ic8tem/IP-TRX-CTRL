# Versiones compatibles

Servidor y cliente deben tener versiones del mismo **protocolo**. El protocolo cambia solo cuando los dos PC deben actualizarse a la vez; dentro del mismo protocolo las versiones se entienden.

| Versiones | Protocolo | Compatibles con |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | solo las 0.1.x |
| 0.2.0 – 0.2.8 | 2 | todas las 0.2.x |
| 0.3.0 – 0.3.6 | 3 | solo las 0.3.x (número de 10 cifras, código fijo del servidor, varios clientes en el servidor, seguridad en transmisión, panadaptador; desde la 0.3.1 acceso desde el navegador; desde la 0.3.2 actualizaciones automáticas) |

Una 0.1.x y una 0.2.x **no** deben usarse juntas, ni una 0.2.x y una 0.3.x: actualiza los dos PC. La 0.3.0 cambia también el formato de la telemetría y añade mensajes nuevos, por eso no se entiende con las versiones anteriores. La 0.3.0 cambia el emparejamiento (número de 10 cifras en lugar del código largo); las claves ya intercambiadas siguen siendo válidas y pasan solas a la lista de servidores del cliente y a la lista de clientes del servidor.

## Funciones que requieren una versión mínima en ambos PC

| Función | Servidor y cliente al menos |
|---|---|
| Programas digitales, pedal/manipulador CW, ciclo de puertos, audio UDP | 0.2.0 |
| Elección de las tarjetas de sonido del servidor desde el cliente | 0.2.1 |
| Servidor sin UPnP, conexión en la red local | 0.2.2 |
| Intercambio de la versión y control automático de compatibilidad | 0.2.3 |
| El cliente comunica al servidor los puertos que salen de su red (redes filtradas) | 0.2.4 |
| Protección ROE: barra intermitente, bloqueo de la transmisión y ventana en el cliente | 0.2.4 |
| CAT y audio del servidor por TCI (ExpertSDR, Thetis y otros SDR) | 0.2.5 (basta en el servidor) |
| Ciclo de puertos también en el emparejamiento (20 vueltas, puertos siempre nuevos, conexión inversa) | 0.2.7 en ambos PC |
| Número de emparejamiento de 10 cifras, lista de servidores en el cliente | 0.3.0 en ambos PC |
| Varios clientes en el servidor (uno conectado a la vez, «servidor ocupado»), código fijo del servidor | 0.3.0 en ambos PC |
| Seguridad en transmisión (tiempo máximo, sin QSY en transmisión, prioridad a quien está en la radio, solo escucha), mandos de la radio, memorias del servidor, panadaptador | 0.3.0 en ambos PC |
| Acceso desde el navegador (página web HTTPS, página «Trova il server») | 0.3.1 en el servidor; el cliente 0.3.0 sigue funcionando |
| Actualizaciones automáticas | 0.3.2 en cada PC: cada uno se actualiza por sí mismo (no con los paquetes .deb, .rpm, Arch y Slackware) |

## Actualizaciones automáticas (desde la 0.3.2)

Desde la 0.3.2 cada PC se actualiza solo a la última release, al arrancar o en menos de 6 horas, y solo cuando no hay una sesión en curso: si un día sale una versión con un protocolo nuevo, servidor y cliente se alinean solos sin quedarse mucho tiempo en protocolos distintos. La excepción son los paquetes de sistema (.deb, .rpm, Arch, Slackware), que hay que actualizar a mano en los dos PC. Detalles en el [README](https://github.com/ic8tem/IP-TRX-CTRL#aggiornamenti-automatici).

## Control automático (desde la 0.2.3)

Los dos PC se intercambian la versión en tres momentos:

- **emparejamiento**: el servidor la envía, cifrada, en el broker MQTT junto con dirección y puertos; el cliente la compara antes de conectarse. Si el protocolo es distinto, el emparejamiento se detiene y lo dice en el registro de la ventana;
- **dirección en el broker MQTT**: el servidor la publica junto con su dirección, en cada actualización;
- **inicio de cada conexión**: en el saludo por el canal cifrado.

Si el otro PC tiene un protocolo distinto aparece la ventana **«Versioni non compatibili»** (versiones no compatibles); si tiene una versión más antigua del mismo protocolo aparece un aviso que dice qué funciones faltan. Las versiones hasta la 0.2.2 no envían su versión: aparecen como «0.2.2 o precedente» (0.2.2 o anterior).

La versión del otro PC se ve en la línea de su red en la ventana principal; cada comparación se escribe en `sessioni.log` con el prefijo `VERSIONE`. La versión de este PC está en la ventana «Informazioni e licenze» (información y licencias).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Versioni-compatibili) · [English](EN-Compatible-versions) · [Français](FR-Versions-compatibles) · [Deutsch](DE-Kompatible-Versionen)
