"""Scenari di prova del trasporto UDP nel laboratorio NAT."""
import asyncio, os, sys, time, random
from nat import Nat, HostSocket, StunServer
from peer import Peer

STUN1, STUN2 = ("127.10.0.1", 3478), ("127.10.0.2", 3478)
quiet = lambda m: None

_n = [0]
async def scenario(title, nat_a, nat_b, audio_s=4.0, loss=0.0, delay=(0, 0), timeout_s=60.0, idle_s=0.0, verbose=False,
                   predict=False, upnp_udp=False):
    random.seed(7)
    _n[0] += 1; k = _n[0]
    log = print if verbose else quiet
    port_base = 20000
    na = Nat("NAT-A", f"127.20.{k}.1", (f"127.21.{k}.254", port_base), *nat_a, loss=loss, delay_ms=delay, timeout_s=timeout_s, log=log)
    nb = Nat("NAT-B", f"127.30.{k}.1", (f"127.31.{k}.254", port_base), *nat_b, loss=loss, delay_ms=delay, timeout_s=timeout_s, log=log)
    await na.start(); await nb.start()
    sa = HostSocket((f"127.21.{k}.1", port_base + 1), na.inside_ep); sb = HostSocket((f"127.31.{k}.1", port_base + 1), nb.inside_ep)
    await sa.start(); await sb.start()
    master = os.urandom(32)
    A = Peer("server", sa, master, [STUN1, STUN2], log=log)
    B = Peer("client", sb, master, [STUN1, STUN2], log=log)
    tasks = [asyncio.create_task(x) for x in (A.run(), B.run(), A.maintain(), B.maintain())]
    if upnp_udp:
        await na.add_forward(55000, sa.local_ep)       # il server apre una porta UDP con UPnP
    pa, pb = await asyncio.gather(A.discover(), B.discover())
    # segnalazione (nel programma: broker MQTT): candidati locali + pubblici (+ porta UPnP del server)
    ca = [p for p in (pa, sa.local_ep) if p]; cb = [p for p in (pb, sb.local_ep) if p]
    if upnp_udp:
        ca.insert(0, (na.public_ip, 55000))
    da = A.port_delta if predict else 0; db = B.port_delta if predict else 0
    ok = all(await asyncio.gather(A.punch(cb, remote_delta=db), B.punch(ca, remote_delta=da)))
    res = {"titolo": title, "NAT server": A.nat_type, "NAT client": B.nat_type, "perforazione": "OK" if ok else "NO"}
    if ok:
        if idle_s:
            await asyncio.sleep(idle_s)          # silenzio: i keepalive devono tenere aperta la mappatura
        # 40 ms di audio a 8 kHz = 640 byte, nei due versi + 50 comandi di controllo affidabili
        frames = int(audio_s / 0.04)
        for i in range(frames):
            A.send_audio(os.urandom(640)); B.send_audio(os.urandom(640))
            if i % (frames // 50 or 1) == 0: B.send_ctrl(f"cmd{i}".encode())
            await asyncio.sleep(0.04)
        await asyncio.sleep(3)
        ra, rb = A.audio_rx, B.audio_rx
        res["audio ricevuto"] = f"{ra['count']}/{frames} ({100 * ra['count'] / frames:.1f} %), riordinati {ra['reorder']}"
        res["controllo"] = f"{len(A.ctrl_received)}/{B.ctrl_seq} consegnati, {B.stats['retransmits']} ritrasmissioni"
        res["scartati dal filtro"] = f"A {na.dropped_filter} · B {nb.dropped_filter}"
    for t in tasks: t.cancel()
    return res

async def main():
    s1, s2 = StunServer(STUN1), StunServer(STUN2)
    await s1.start(); await s2.start()
    verbose = "-v" in sys.argv
    out = []
    out.append(await scenario("1. due router di casa (cono, porta ristretta)", ("eim", "port"), ("eim", "port"), verbose=verbose))
    out.append(await scenario("2. come 1 con 5 % di perdita e 20-80 ms di ritardo", ("eim", "port"), ("eim", "port"), loss=0.05, delay=(20, 80), verbose=verbose))
    out.append(await scenario("3. server cono pieno, client simmetrico", ("eim", "none"), ("sym", "port"), verbose=verbose))
    out.append(await scenario("4. server cono ristretto (indirizzo), client simmetrico", ("eim", "addr"), ("sym", "port"), verbose=verbose))
    out.append(await scenario("5. server porta ristretta, client simmetrico", ("eim", "port"), ("sym", "port"), verbose=verbose))
    out.append(await scenario("6. entrambi simmetrici", ("sym", "port"), ("sym", "port"), verbose=verbose))
    out.append(await scenario("5b. come 5 con previsione della porta", ("eim", "port"), ("sym", "port"), predict=True, verbose=verbose))
    out.append(await scenario("6b. entrambi simmetrici con previsione della porta", ("sym", "port"), ("sym", "port"), predict=True, verbose=verbose))
    out.append(await scenario("6c. entrambi simmetrici, server con porta UDP aperta via UPnP", ("sym", "port"), ("sym", "port"), upnp_udp=True, verbose=verbose))
    out.append(await scenario("7. mappature che scadono dopo 20 s, 25 s di silenzio", ("eim", "port"), ("eim", "port"), timeout_s=20, idle_s=25, audio_s=2, verbose=verbose))
    for r in out:
        print("\n" + r.pop("titolo"))
        for k, v in r.items(): print(f"   {k:20} {v}")

asyncio.run(main())
