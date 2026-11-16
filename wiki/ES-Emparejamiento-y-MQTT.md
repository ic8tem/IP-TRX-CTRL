# Emparejamiento y encuentro mediante MQTT

El emparejamiento se hace una sola vez por cada servidor: después, claves, puertos y reconexiones son automáticos.

## Cómo emparejar

1. En el **servidor** se abre la ventana *Abbinamento* (emparejamiento): aparece un número de 10 cifras (p. ej. `482 913 0571`, desde la 0.3.0; antes era un código largo como `R4VGZ-TRGSF-…`). Como alternativa, el cliente puede usar el código fijo del servidor de 12 cifras, si está activado (ver más abajo).
2. En el **cliente** se escribe ese número y se pulsa **Abbina** (emparejar). Vale 10 minutos.
3. Los dos programas intercambian una clave secreta (ECDH P-256) y desde ese momento todo el tráfico va cifrado con AES-256-GCM.

## El papel del broker MQTT

El servidor no tiene una dirección fija. Para que lo encuentren deja su dirección IP y sus puertos, **cifrados**, en un canal de un broker MQTT público. El nombre del canal se obtiene de la clave secreta: solo quien emparejó los dos PC sabe dónde mirar y puede leer el contenido. Si un broker no responde, el programa pasa al siguiente.

También el emparejamiento pasa por el broker: el cliente anuncia su presencia en el canal obtenido del número, el servidor responde con su dirección actual (cifrada) y luego el cliente abre la conexión directa. Desde la 0.3.0 el número no contiene la dirección: solo llega por aquí.

## El registro del diálogo

La ventana de emparejamiento muestra, línea a línea, el diálogo con el broker y con el servidor:

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

`▶` = enviado, `◀` = recibido, `✗` = error, `✓` = completado. Con **Copia registro** (copiar registro) el texto pasa al portapapeles, útil para ver dónde se detiene un emparejamiento fallido. El registro también puede guardarse en el log de la sesión.

## Problemas típicos

| Qué se ve en el registro | Causa probable |
|---|---|
| Ningún broker responde | Sin conexión a internet o puerto 1883 bloqueado por la red |
| El broker responde pero el servidor no | El programa servidor está cerrado o todavía no espera el emparejamiento |
| El servidor responde pero el TCP no se abre | Puerto no abierto en el router (UPnP desactivado o red del proveedor con IP compartida): desde la 0.2.7 arranca el ciclo de puertos |
| «nessuna risposta» (sin respuesta) en la red local con la misma IP pública | El cortafuegos de Windows del servidor descarta las conexiones: en el servidor Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (permitir en el cortafuegos de Windows) |
| «connessione rifiutata» (conexión rechazada) | El programa servidor está cerrado o escucha en otro puerto |
| «le porte del server NON sono raggiungibili» y «CGNAT» | El servidor no puede recibir conexiones desde internet: hace falta un puerto abierto en el router del cliente (UPnP, DMZ o redirección), que el ciclo de puertos usa para la conexión inversa |

## Ciclo de puertos en el emparejamiento (desde la 0.2.7)

Si la conexión directa falla, el emparejamiento no se detiene: arrancan hasta **20 vueltas** de intercambio de puertos, siempre a través del broker MQTT y cifradas con la clave obtenida del número de emparejamiento.

1. El cliente abre un puerto **nuevo** en su router (en vueltas alternas un puerto común que suele pasar los filtros, como 443, 80, 8080, 8443, o uno al azar), lo comprueba desde internet y lo propone al servidor.
2. El servidor intenta conectarse a ese puerto: es la **conexión inversa**, la única posible cuando el servidor está detrás de CGNAT.
3. Si no lo consigue, el servidor abre también un puerto **nuevo**, lo comprueba desde internet y comunica al cliente el número y el resultado.
4. El cliente prueba enseguida el puerto nuevo del servidor (también en la red local).

Ningún puerto se prueba dos veces, ni en el cliente ni en el servidor. Si la red del cliente solo deja salir algunos puertos, el servidor prueba primero esos. Un puerto del servidor que resulta alcanzable pasa a ser su puerto de control. Al final, si nada ha funcionado, la última línea del registro dice por qué (por ejemplo: servidor detrás de CGNAT y router del cliente sin UPnP).

## Número de emparejamiento de 10 cifras (desde la 0.3.0)

El servidor muestra un **número de 10 cifras** (por ejemplo `482 913 0571`) en lugar del antiguo código largo.

- Vale **10 minutos** y **una sola vez**; caducado, se pulsa «Riprova» (reintentar) en el servidor para obtener uno nuevo.
- **No contiene la dirección** del servidor: el cliente la recibe **cifrada** (AES-256-GCM) desde el broker MQTT. Sin al menos un broker alcanzable no se puede emparejar.
- La clave se obtiene del número con un **PBKDF2-SHA256** lento (200 000 iteraciones), usando como sal la ventana de 10 minutos en la que nació el número. Probar los 10 000 millones de números llevaría demasiado tiempo, y una tabla precalculada no sirve porque cada ventana tiene su propia sal. El cliente prueba la ventana actual, las dos anteriores y la siguiente, por la espera y por relojes algo distintos (unos segundos de cálculo).
- El resto del emparejamiento es como antes: intercambio ECDH P-256 autenticado con la clave obtenida del número y clave de 256 bits para las sesiones.

## Servidores emparejados (cliente, desde la 0.3.0)

El cliente guarda una **lista de servidores**, cada uno con su clave de 256 bits custodiada como antes (DPAPI en Windows, llavero en macOS, archivo cifrado en Linux).

- En el recuadro del servidor de la ventana principal se elige el servidor: el cliente está conectado a **uno solo a la vez**, y al elegir otro se cierra la conexión en curso.
- **Abbina nuovo…** (emparejar nuevo) añade un servidor con su número; los anteriores se quedan.
- En **Impostazioni › Abbinamento e registri** (ajustes › emparejamiento y registros) se renombra un servidor o se quita de la lista (su clave se borra).
- El nombre inicial es el indicativo del servidor, en cuanto el servidor lo comunica.
- Un emparejamiento hecho con una 0.2.x pasa a ser por sí solo el primer servidor de la lista: no hace falta emparejar de nuevo.

## Clientes emparejados (servidor, desde la 0.3.0)

También el servidor guarda una **lista de clientes**, cada uno con su clave: un emparejamiento nuevo **no** quita el acceso a los demás.

- Se conecta **un cliente a la vez** (la radio es una). Si un segundo cliente intenta entrar mientras otro opera, recibe «Il server è occupato: è collegato …» (el servidor está ocupado: está conectado …) con el indicativo de quien opera, y lo reintenta solo cada 30 segundos.
- El mismo cliente que se reconecta (por ejemplo tras un cambio de red) recupera su sitio sin esperas.
- En **Impostazioni › Abbinamento e registri** se ve la lista, con el indicativo, el modo de emparejamiento (número temporal, código fijo, intercambio de papeles, emparejamiento anterior) y el último uso. Se puede renombrar un cliente o **quitarle el acceso** («Togli accesso»): su clave se borra y, si está conectado, se desconecta enseguida.
- **Abbina un nuovo client** (emparejar un cliente nuevo) abre la ventana con un número temporal; los clientes ya emparejados se quedan.
- Un emparejamiento hecho con una 0.2.x pasa a ser por sí solo el primer cliente de la lista.
- «Esporta chiavi per le autorità» (exportar claves para las autoridades) exporta las claves de todos los clientes.

## Código fijo del servidor (desde la 0.3.0)

Además del número temporal, el servidor puede tener un **código fijo de 12 cifras** (por ejemplo `4829 1305 7712`), como algunos programas de control remoto. Se activa en **Impostazioni › Abbinamento e registri** con «Accetta nuovi client con il codice fisso del server» (aceptar clientes nuevos con el código fijo del servidor); tras la instalación está **desactivado**.

- El código vale **siempre**, hasta que lo cambies («Cambia codice») o desactives la opción. Quien lo recibe se empareja cuando quiere, incluso días después, escribiéndolo en la misma casilla del número; luego entra en la lista de clientes y ya no lo necesita.
- Solo funciona con el programa servidor en marcha y al menos un broker MQTT alcanzable, como el número temporal; también aquí la dirección llega al cliente solo cifrada.
- La clave se obtiene del código con PBKDF2-SHA256 de 600 000 iteraciones (tres veces el número temporal) y el código tiene dos cifras más: probar todos los códigos llevaría años de cálculo.
- **Seguridad:** es cómodo pero menos seguro que el número temporal, porque quien descubre el código puede emparejarse en cualquier momento. Dalo solo a personas de confianza, cámbialo si ha circulado demasiado y quita de la lista los clientes que ya no deben entrar. Cambiar el código no quita el acceso a los clientes ya emparejados.
- Las cifras del código se custodian como las claves (DPAPI, llavero de macOS, archivo cifrado en Linux), no en el archivo de ajustes.

## Direcciones cifradas

La dirección IP del servidor nunca viaja en claro: en los brokers MQTT (emparejamiento, búsqueda tras un cambio de IP) va cifrada con AES-256-GCM, y los nombres de los canales MQTT son huellas obtenidas de la clave, que no revelan ni la dirección ni el indicativo. También el código de reserva para cuando los brokers no responden contiene la dirección cifrada.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Abbinamento-e-MQTT) · [English](EN-Pairing-and-MQTT) · [Français](FR-Appairage-et-MQTT) · [Deutsch](DE-Kopplung-und-MQTT)
