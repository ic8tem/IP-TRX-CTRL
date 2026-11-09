"""
Prototipo del trasporto UDP di IP-TRX-CTRL (da tradurre in C#).

1. Indirizzo pubblico con STUN da due server diversi: se le porte viste differiscono il NAT è
   "simmetrico" e la perforazione diretta probabilmente non riuscirà.
2. Candidati: indirizzo locale + indirizzo pubblico (STUN). Scambio tramite il canale di segnalazione
   (nel programma: il broker MQTT, cifrato).
3. Perforazione: ogni 50 ms un PUNCH cifrato verso tutti i candidati; chi riceve un PUNCH valido
   aggiunge il mittente ai candidati (anche se non era nell'elenco: "peer reflexive") e risponde PUNCH_ACK.
4. Canale: datagrammi AES-256-GCM. AUDIO senza ritrasmissione (seq per perdite e riordino),
   CTRL affidabile (ACK, ritrasmissione con attesa crescente), KEEPALIVE ogni 15 s per tenere aperto il NAT.
"""
import asyncio, os, struct, time, random
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF
from cryptography.hazmat.primitives import hashes
from nat import stun_request, stun_parse_response, is_stun

MARK = 0xA5
PUNCH, PUNCH_ACK, AUDIO, CTRL, ACK, KEEPALIVE = 1, 2, 0x10, 0x20, 0x21, 0x30

def hkdf(key, info, n=32):
    return HKDF(algorithm=hashes.SHA256(), length=n, salt=b"", info=info.encode()).derive(key)


class Peer:
    def __init__(self, name, sock, master, stun_servers, log=print):
        self.name, self.sock = name, sock
        self.aes = AESGCM(hkdf(master, "iptrx-udp"))
        self.id = os.urandom(8)
        self.stun_servers = stun_servers
        self.log = lambda m: log(f"[{self.name}] {m}")
        self.stun_waits = {}
        self.candidates = set()
        self.selected = None
        self.connected = asyncio.Event()
        self.nat_type = "?"
        self.public = None
        self.port_delta = 0           # passo con cui il NAT simmetrico assegna le porte
        # canale
        self.tx_seq = 0
        self.ctrl_seq = 0
        self.pending = {}             # seq -> (data, tries, next_time)
        self.ctrl_seen = set()
        self.ctrl_received = []
        self.audio_rx = {"count": 0, "lost": 0, "reorder": 0, "last": None}
        self.stats = {"retransmits": 0, "punch_sent": 0}
        self.last_rx = time.monotonic()

    # ---------------------------------------------------------------- invio cifrato
    def _seal(self, typ, payload):
        nonce = os.urandom(12)
        return bytes([MARK]) + nonce + self.aes.encrypt(nonce, bytes([typ]) + payload, None)

    def _open(self, data):
        if len(data) < 1 + 12 + 16 + 1 or data[0] != MARK: return None
        try:
            p = self.aes.decrypt(data[1:13], data[13:], None)
            return p[0], p[1:]
        except Exception:
            return None

    def send(self, typ, payload, ep=None):
        ep = ep or self.selected
        if ep: self.sock.sendto(self._seal(typ, payload), ep)

    # ---------------------------------------------------------------- ricezione
    async def run(self):
        while True:
            data, src = await self.sock.recv()
            if is_stun(data):
                for tid, fut in list(self.stun_waits.items()):
                    r = stun_parse_response(data, tid)
                    if r and not fut.done(): fut.set_result(r)
                continue
            m = self._open(data)
            if m is None: continue           # pacchetto estraneo o falsificato: scartato
            typ, payload = m
            self.last_rx = time.monotonic()
            if typ == PUNCH:
                if src not in self.candidates:
                    self.log(f"PUNCH da indirizzo nuovo {src}: aggiunto ai candidati")
                    self.candidates.add(src)
                self.send(PUNCH_ACK, payload[:8] + self.id, src)
                if self.selected is None: self._select(src, "PUNCH ricevuto")
            elif typ == PUNCH_ACK:
                if payload[:8] == self.id and self.selected is None: self._select(src, "PUNCH_ACK ricevuto")
            elif typ == AUDIO:
                seq = struct.unpack("!I", payload[:4])[0]
                a = self.audio_rx; a["count"] += 1
                if a["last"] is not None:
                    if seq > a["last"] + 1: a["lost"] += seq - a["last"] - 1
                    elif seq <= a["last"]: a["reorder"] += 1; a["lost"] -= 1
                a["last"] = max(seq, a["last"] or 0)
            elif typ == CTRL:
                seq = struct.unpack("!I", payload[:4])[0]
                self.send(ACK, payload[:4], src)
                if seq not in self.ctrl_seen:
                    self.ctrl_seen.add(seq); self.ctrl_received.append(payload[4:])
            elif typ == ACK:
                self.pending.pop(struct.unpack("!I", payload[:4])[0], None)

    def _select(self, ep, why):
        self.selected = ep
        self.log(f"collegato a {ep} ({why})")
        self.connected.set()

    # ---------------------------------------------------------------- STUN
    async def stun(self, server, timeout=1.0, tries=3):
        for _ in range(tries):
            req, tid = stun_request()
            fut = asyncio.get_running_loop().create_future()
            self.stun_waits[tid] = fut
            self.sock.sendto(req, server)
            try:
                return await asyncio.wait_for(fut, timeout)
            except asyncio.TimeoutError:
                pass
            finally:
                self.stun_waits.pop(tid, None)
        return None

    async def discover(self):
        a = await self.stun(self.stun_servers[0])
        b = await self.stun(self.stun_servers[1])
        self.public = a
        if a is None: self.nat_type = "UDP bloccato"
        elif b is None: self.nat_type = "sconosciuto (un solo STUN)"
        elif a == b: self.nat_type = "cono (mappatura stabile)"
        else:
            self.nat_type = f"SIMMETRICO (porte {a[1]} / {b[1]})"
            self.port_delta = b[1] - a[1]
            self.public = b                 # la porta più recente è la base della previsione
        self.log(f"indirizzo pubblico {a} · NAT {self.nat_type}")
        return a

    # ---------------------------------------------------------------- perforazione
    async def punch(self, remote_candidates, timeout=10.0, remote_delta=0, predict=32):
        self.candidates |= set(remote_candidates)
        # previsione della porta: se l'altro NAT è simmetrico con passo regolare, prova le porte successive
        if remote_delta and remote_candidates:
            ip, port = remote_candidates[0]
            step = remote_delta if abs(remote_delta) < 16 else 1
            for k in range(1, predict + 1):
                self.candidates.add((ip, port + step * k))
            self.log(f"previsione porta: provo {predict} porte da {port + step} con passo {step}")
        t0 = time.monotonic()
        while not self.connected.is_set() and time.monotonic() - t0 < timeout:
            for ep in list(self.candidates):
                self.send(PUNCH, self.id, ep); self.stats["punch_sent"] += 1
            await asyncio.sleep(0.05)
        if self.connected.is_set():
            self.log(f"perforazione riuscita in {time.monotonic() - t0:.2f} s")
            return True
        self.log(f"perforazione NON riuscita dopo {timeout:.0f} s")
        return False

    # ---------------------------------------------------------------- controllo affidabile
    def send_ctrl(self, payload):
        self.ctrl_seq += 1
        data = struct.pack("!I", self.ctrl_seq) + payload
        self.pending[self.ctrl_seq] = [data, 0, time.monotonic() + 0.2]
        self.send(CTRL, data)

    async def maintain(self):
        """Ritrasmissioni del controllo e keepalive."""
        last_ka = time.monotonic()
        while True:
            await asyncio.sleep(0.02)
            now = time.monotonic()
            for seq, p in list(self.pending.items()):
                if now >= p[2]:
                    if p[1] >= 12:
                        self.log(f"CTRL {seq} perso dopo 12 tentativi"); self.pending.pop(seq); continue
                    p[1] += 1; p[2] = now + 0.2 * (1.5 ** p[1])
                    self.stats["retransmits"] += 1
                    self.send(CTRL, p[0])
            if self.selected and now - last_ka > 15:
                last_ka = now; self.send(KEEPALIVE, b"")

    def send_audio(self, pcm):
        self.tx_seq += 1
        self.send(AUDIO, struct.pack("!I", self.tx_seq) + pcm)
