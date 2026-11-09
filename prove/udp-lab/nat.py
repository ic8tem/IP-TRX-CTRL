"""
Laboratorio locale: router NAT simulati e server STUN, tutto su indirizzi di loopback.

Ogni PC simulato non parla direttamente con "internet": incapsula i datagrammi verso il proprio
router (intestazione = indirizzo di destinazione), e il router li fa uscire da una porta pubblica
creata secondo il suo comportamento:
  - mappatura: indipendente dalla destinazione (EIM, i router di casa) o dipendente (simmetrico);
  - filtro: nessuno (cono pieno), per indirizzo (ristretto), per indirizzo e porta (porta ristretta).
I pacchetti in arrivo passano solo se il filtro lo consente. Si possono aggiungere perdita e ritardo
e un tempo di scadenza delle mappature.
"""
import asyncio, random, socket, struct, os, time

def pack_ep(ep):
    return socket.inet_aton(ep[0]) + struct.pack("!H", ep[1])

def unpack_ep(b):
    return (socket.inet_ntoa(b[:4]), struct.unpack("!H", b[4:6])[0])


class _Proto(asyncio.DatagramProtocol):
    def __init__(self, on_dgram):
        self.on_dgram = on_dgram
    def connection_made(self, t):
        self.t = t
    def datagram_received(self, data, addr):
        self.on_dgram(data, addr)


async def udp_bind(addr, on_dgram):
    loop = asyncio.get_running_loop()
    t, p = await loop.create_datagram_endpoint(lambda: _Proto(on_dgram), local_addr=addr)
    return t


class Nat:
    def __init__(self, name, public_ip, inside_ep, mapping="eim", filtering="port",
                 loss=0.0, delay_ms=(0, 0), timeout_s=60.0, log=None):
        self.name, self.public_ip, self.inside_ep = name, public_ip, inside_ep
        self.mapping, self.filtering = mapping, filtering
        self.loss, self.delay_ms, self.timeout_s = loss, delay_ms, timeout_s
        self.maps = {}          # chiave -> dict(transport, port, host_ep, sent_to=set, last)
        self.inside = None
        self.next_port = 40000 + random.randint(0, 2000)
        self.log = log or (lambda *a: None)
        self.dropped_filter = 0
        self.forwards = {}      # host_ep -> mappatura fissa aperta "con UPnP"

    async def add_forward(self, public_port, host_ep):
        """Porta inoltrata come farebbe UPnP: accetta da chiunque, e il PC esce dalla stessa porta."""
        m = {"port": public_port, "host_ep": host_ep, "sent_to": set(), "last": float("inf"), "transport": None, "forward": True}
        self.forwards[host_ep] = m
        await self._open(m, None)

    async def start(self):
        self.inside = await udp_bind(self.inside_ep, self._from_inside)
        asyncio.get_running_loop().create_task(self._expire())

    async def _expire(self):
        while True:
            await asyncio.sleep(1)
            now = time.monotonic()
            for k, m in list(self.maps.items()):
                if now - m["last"] > self.timeout_s:
                    m["transport"].close()
                    del self.maps[k]
                    self.log(f"{self.name}: mappatura {self.public_ip}:{m['port']} scaduta")

    def _key(self, host_ep, dest):
        return (host_ep,) if self.mapping == "eim" else (host_ep, dest)

    def _from_inside(self, data, host_ep):
        dest = unpack_ep(data[:6]); payload = data[6:]
        if host_ep in self.forwards:
            self._send_out(self.forwards[host_ep], dest, payload); return
        key = self._key(host_ep, dest)
        m = self.maps.get(key)
        if m is None:
            port = self.next_port; self.next_port += 1 + (random.randint(0, 3) if self.mapping != "eim" else 0)
            m = {"port": port, "host_ep": host_ep, "sent_to": set(), "last": time.monotonic(), "transport": None}
            self.maps[key] = m
            asyncio.get_running_loop().create_task(self._open(m, key))
            m["pending"] = [(dest, payload)]
            return
        if m["transport"] is None:
            m["pending"].append((dest, payload)); return
        self._send_out(m, dest, payload)

    async def _open(self, m, key):
        def on_out(data, src, m=m):
            self._from_outside(m, data, src)
        m["transport"] = await udp_bind((self.public_ip, m["port"]), on_out)
        for dest, payload in m.pop("pending", []):
            self._send_out(m, dest, payload)

    def _send_out(self, m, dest, payload):
        m["sent_to"].add(dest); m["last"] = time.monotonic()
        self._deliver(lambda: m["transport"].sendto(payload, dest))

    def _deliver(self, fn):
        if random.random() < self.loss:
            return
        lo, hi = self.delay_ms
        d = random.uniform(lo, hi) / 1000.0
        if d <= 0: fn()
        else: asyncio.get_running_loop().call_later(d, fn)

    def _from_outside(self, m, data, src):
        ok = (m.get("forward") or self.filtering == "none"
              or (self.filtering == "addr" and any(s[0] == src[0] for s in m["sent_to"]))
              or (self.filtering == "port" and src in m["sent_to"]))
        if not ok:
            self.dropped_filter += 1
            return
        pkt = pack_ep(src) + data
        self._deliver(lambda: self.inside.sendto(pkt, m["host_ep"]))


class HostSocket:
    """Socket UDP di un PC dietro un NAT simulato: sendto/recv come un socket vero."""
    def __init__(self, local_ep, nat_inside_ep):
        self.local_ep, self.nat = local_ep, nat_inside_ep
        self.queue = asyncio.Queue()
        self.t = None
    async def start(self):
        self.t = await udp_bind(self.local_ep, lambda d, a: self.queue.put_nowait((d[6:], unpack_ep(d[:6]))))
    def sendto(self, data, dest):
        self.t.sendto(pack_ep(dest) + data, self.nat)
    async def recv(self, timeout=None):
        return await asyncio.wait_for(self.queue.get(), timeout)


# ------------------------------------------------------------------ STUN (RFC 5389, solo Binding)
MAGIC = 0x2112A442

def stun_request():
    tid = os.urandom(12)
    return struct.pack("!HHI", 0x0001, 0, MAGIC) + tid, tid

def stun_parse_response(data, tid):
    if len(data) < 20: return None
    t, ln, magic = struct.unpack("!HHI", data[:8])
    if t != 0x0101 or magic != MAGIC or data[8:20] != tid: return None
    pos = 20
    while pos + 4 <= 20 + ln:
        at, al = struct.unpack("!HH", data[pos:pos + 4]); v = data[pos + 4:pos + 4 + al]
        if at == 0x0020 and al >= 8:        # XOR-MAPPED-ADDRESS
            port = struct.unpack("!H", v[2:4])[0] ^ (MAGIC >> 16)
            ip = struct.unpack("!I", v[4:8])[0] ^ MAGIC
            return (socket.inet_ntoa(struct.pack("!I", ip)), port)
        pos += 4 + al + ((4 - al % 4) % 4)
    return None

def is_stun(data):
    return len(data) >= 20 and data[0] < 2 and struct.unpack("!I", data[4:8])[0] == MAGIC

class StunServer:
    def __init__(self, ep):
        self.ep = ep
    async def start(self):
        self.t = await udp_bind(self.ep, self._on)
    def _on(self, data, src):
        if len(data) < 20: return
        t, ln, magic = struct.unpack("!HHI", data[:8])
        if t != 0x0001 or magic != MAGIC: return
        tid = data[8:20]
        ip = struct.unpack("!I", socket.inet_aton(src[0]))[0] ^ MAGIC
        attr = struct.pack("!HHBBHI", 0x0020, 8, 0, 1, src[1] ^ (MAGIC >> 16), ip)
        self.t.sendto(struct.pack("!HHI", 0x0101, len(attr), MAGIC) + tid + attr, src)
