# Puertos y conexión

Servidor y cliente se conectan directamente, sin un servidor intermedio para el audio. Para que funcione, al menos uno de los dos PC debe tener puertos alcanzables desde internet. IP-TRX-CTRL los abre solo con **UPnP** y comprueba que funcionen de verdad.

## UPnP al arrancar y el LED

Al arrancar, tanto en el servidor como en el cliente, el programa busca el router con **UPnP** y con **NAT-PMP** (tres intentos de 10 segundos) y muestra una ventana con el resultado:

- **UPnP activo**: el router puede abrir los puertos solo; la ventana indica el protocolo y la IP externa declarada por el router;
- **UPnP no encontrado**: la ventana indica la causa más probable y qué hacer:
  - red de Windows configurada como «Pública» (Windows bloquea las respuestas del router: hay que ponerla «Privada»);
  - VPN u otras tarjetas de red virtuales activas;
  - UPnP apagado en el router: hay que activarlo desde la página del router (en el FRITZ!Box también para el PC concreto).

Arriba en la ventana principal el **LED «UPnP»** queda **verde intermitente** mientras el router responde (comprobación cada 60 segundos), **rojo fijo** si falta UPnP y **gris** durante la búsqueda. Si UPnP estaba activo y el router deja de responder, la ventana vuelve a aparecer. Un clic en el LED la reabre en cualquier momento.

## UPnP y NAT-PMP (desde la 0.2.8)

El programa busca el router **primero con UPnP** (dos intentos de 8 segundos) y solo si UPnP no responde, con **NAT-PMP**. Antes de la 0.2.8 valía el primer protocolo que respondía: algunos routers responden a NAT-PMP pero luego rechazan todos los puertos, y en el registro aparecían «Failed to create portmap» y «Arithmetic operation resulted in an overflow» (un límite de la biblioteca Open.NAT con NAT-PMP en los puertos por encima de 32767).

Si el router rechaza 5 puertos seguidos, el programa prueba el otro protocolo. Si también ese rechaza, usa un puerto del PC: funciona en la red local, con DMZ o redirección manual, y en el emparejamiento se sigue con el ciclo de puertos y la conexión inversa. Con NAT-PMP los puertos elegidos quedan por debajo de 32768.

## El servidor

Al arrancar el servidor:

1. busca el router con UPnP;
2. abre un puerto al azar para el **control** (TCP) y otro para el **audio** (TCP, y el mismo puerto en UDP);
3. se pone a la escucha y pide a **dos servicios externos independientes** que se conecten a ese puerto: **ifconfig.co** y **portchecker.io** (ambos de código abierto). Basta con que uno de los dos entre para que el puerto sea bueno; si ninguno entra, el servidor cierra el puerto y prueba otro (hasta 6 veces por puerto). Si ninguno de los servicios responde, el puerto se mantiene y el registro lo dice;
4. los **dos primeros puertos útiles** se publican, cifrados, en el broker MQTT junto con la dirección IP pública.

**Router sin UPnP.** El servidor usa igualmente dos puertos locales, los comprueba desde fuera y los publica: funcionan si en el router hay una DMZ o una redirección manual hacia el PC del servidor, y en todo caso desde los PC de la **misma red local**. Si ni así entra el cliente, el servidor espera los puertos que proponga el cliente (conexión inversa).

Cuando los puertos del servidor no son alcanzables desde internet aparece también una ventana de aviso **«Da fuori casa questo server non è raggiungibile»** (desde fuera de casa este servidor no es alcanzable), con los números de puerto, la dirección del PC y los tres remedios (UPnP, DMZ, redirección manual). El programa sigue funcionando para la red local y para la conexión inversa.

**Misma red local.** El servidor publica también su dirección de red local (por ejemplo 192.168.1.20). Muchos routers no permiten que un PC de casa vuelva a entrar por la dirección pública de la propia casa: en ese caso el cliente prueba solo la dirección local, tanto en el emparejamiento como en las conexiones siguientes.

## El cliente y el ciclo de puertos

El cliente lee en el broker MQTT la dirección y los puertos del servidor e intenta conectarse. Si no lo consigue empieza el **ciclo de puertos**:

1. el cliente abre con UPnP dos puertos en **su** router, los comprueba desde fuera como el servidor y los publica en el broker MQTT, con el número de vuelta y el puerto del servidor que no alcanzó;
2. el servidor lee la propuesta e intenta conectarse a los puertos del cliente (**conexión inversa**: el servidor abre la conexión, pero el cifrado y la autenticación siguen siendo los mismos);
3. si tampoco esto funciona, el servidor cierra sus puertos, abre y comprueba dos nuevos y los vuelve a publicar;
4. el cliente reintenta con los puertos nuevos del servidor, y así sucesivamente.

El ciclo dura **20 vueltas**. Tras la vigésima el cliente muestra la ventana **«Nessuna porta è apribile»** (ningún puerto se puede abrir) con el botón **Exit**, que cierra el programa.

Cada paso queda escrito en el registro `sessioni.log` con el prefijo `PORTE` (Impostazioni › Apri cartella dati, abrir carpeta de datos), por ejemplo:

```
PORTE giro 3/20: il client non raggiunge il server (93.45.x.x:41234)
PORTE IP-TRX-CTRL client controllo: porta TCP 52310 aperta sul router (tentativo 1)
PORTE verifica esterna TCP 151.x.x.x:52310: raggiungibile
PORTE giro 3/20: il client propone 151.x.x.x porte 52310/38877 sul broker MQTT (consegnato)
PORTE collegamento inverso riuscito: il server si è collegato alle porte del client 151.x.x.x:52310
```

## Redes que filtran los puertos de salida (desde la 0.2.4)

Algunas redes (empresas, hoteles, wifi públicas, ciertas redes móviles) solo dejan **salir** unos pocos puertos, normalmente 443 y 80. Desde ahí el cliente no alcanza los puertos aleatorios del servidor, aunque estén abiertos.

Al arrancar, el cliente prueba qué puertos salen de su red conectándose a **portquiz.net**, un servicio que responde en todos los puertos TCP: 443, 80, 8443, 8080, 993, 587, 465, 1935 y dos puertos altos. El puerto cuenta como bueno solo si llega de verdad la página de portquiz.net (un portal o un proxy que intercepta no basta).

- Si salen también los puertos altos, la red no filtra y no cambia nada.
- Si la red filtra, el cliente envía **enseguida** al servidor, cifrada en el broker MQTT, la lista de los puertos que pasan; la repite en cada vuelta del ciclo de puertos.
- El servidor pide al router por UPnP **precisamente esos puertos como puertos externos** (por ejemplo 443 para el control y 80 para el audio), redirigidos a puertos internos libres del PC, los comprueba desde fuera y los publica. Si el router rechaza uno (ya usado por otro PC o por el propio router) prueba el siguiente; si no consigue abrir dos, vuelve a los puertos aleatorios.

Hacen falta **dos** puertos de salida: si la red del cliente solo deja pasar uno, la conexión desde esa red no es posible y el registro lo dice. Hace falta UPnP en el router del servidor (o una redirección manual de esos puertos). Todo queda escrito en `sessioni.log` con el prefijo `PORTE`.

## Audio por UDP

Una vez conectados, los dos PC intentan pasar el audio por **UDP**, que sufre menos los retardos de TCP:

- cada uno pide su dirección pública a dos servidores **STUN** desde el mismo socket UDP (abierto en el puerto de audio, que UPnP abrió también en UDP);
- las direcciones candidatas se intercambian por el canal de control cifrado;
- los dos PC «llaman» el uno hacia el otro (perforación del NAT); con un NAT simétrico se prueban también los puertos siguientes;
- solo cuando **ambos** confirman que reciben, el audio pasa por UDP (cifrado AES-256-GCM).

Si UDP no pasa, o si el cliente deja de recibir paquetes UDP durante 4 segundos, el audio se queda (o vuelve) en TCP. El recorrido en uso aparece en la línea de la red del otro PC: «audio UDP» o «audio TCP».

## Qué hacer si no conecta

- Activa **UPnP** en el router de al menos uno de los dos PC (mejor en los dos).
- Si el operador usa **CGNAT** (IP compartida, frecuente en red móvil y FWA), ese PC no puede recibir conexiones: debe ser el otro quien abra los puertos.
- Si ambos PC están detrás de CGNAT o sin UPnP, la conexión directa no es posible.
- **Cortafuegos de Windows**: aun con los puertos abiertos en el router, el cortafuegos del PC puede descartar las conexiones («nessuna risposta» incluso desde la red local). Desde la 0.2.7 el instalador permite el programa en todas las redes; con las versiones portátiles está el botón Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (permitir en el cortafuegos de Windows).
- En el emparejamiento, desde la 0.2.7, el ciclo de puertos arranca solo (ver *Emparejamiento y encuentro mediante MQTT*).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Porte-e-collegamento) · [English](EN-Ports-and-connection) · [Français](FR-Ports-et-liaison) · [Deutsch](DE-Ports-und-Verbindung)
