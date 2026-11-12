// IP-TRX-CTRL · pagina web della stazione (dalla 0.3.1).
// Servita dal server in HTTPS. Accesso con il codice fisso (sfida e risposta: il codice non passa in rete),
// poi gli stessi messaggi binari del programma client sul WebSocket «/ws»: telemetria, comandi, audio 8 kHz.
'use strict';

(() => {
  // ================================================================ messaggi (come Net/Messages.cs)
  const M = {
    Audio: 0x01, Hello: 0x10, Ping: 0x11, Pong: 0x12, PeerInfo: 0x13, Telemetry: 0x20, SetFreqMode: 0x21, Ptt: 0x22,
    RadioStatus: 0x23, SetVfoB: 0x24, SetSplit: 0x25, VfoOp: 0x26, SetPower: 0x27, Busy: 0x33, ServerNotice: 0x34,
    SetRadioCtl: 0x35, SpectrumOn: 0x39, Spectrum: 0x3A
  };
  const enc = new TextEncoder(), dec = new TextDecoder();

  class W {
    constructor(type) { this.a = [type]; }
    u8(v) { this.a.push(v & 255); return this; }
    bool(v) { return this.u8(v ? 1 : 0); }
    num(setter, size, v) { const d = new DataView(new ArrayBuffer(size)); d[setter](0, v, true); for (let i = 0; i < size; i++) this.a.push(d.getUint8(i)); return this; }
    i32(v) { return this.num('setInt32', 4, v); }
    u32(v) { return this.num('setUint32', 4, v >>> 0); }
    f32(v) { return this.num('setFloat32', 4, v); }
    i64(v) { return this.num('setBigInt64', 8, BigInt(Math.round(v))); }
    str(s) {
      const b = enc.encode(s || '');
      let n = b.length;
      do { let x = n & 127; n >>>= 7; if (n) x |= 128; this.a.push(x); } while (n);
      for (const x of b) this.a.push(x);
      return this;
    }
    raw(b) { for (const x of b) this.a.push(x); return this; }
    build() { return new Uint8Array(this.a); }
  }

  class R {
    constructor(u) { this.u = u; this.v = new DataView(u.buffer, u.byteOffset, u.byteLength); this.p = 1; this.type = u[0]; }
    get end() { return this.p >= this.u.length; }
    u8() { return this.v.getUint8(this.p++); }
    bool() { return this.u8() === 1; }
    i32() { const x = this.v.getInt32(this.p, true); this.p += 4; return x; }
    u32() { const x = this.v.getUint32(this.p, true); this.p += 4; return x; }
    f32() { const x = this.v.getFloat32(this.p, true); this.p += 4; return x; }
    i64() { const x = this.v.getBigInt64(this.p, true); this.p += 8; return Number(x); }
    str() {
      let n = 0, shift = 0, b;
      do { b = this.u8(); n |= (b & 127) << shift; shift += 7; } while (b & 128);
      const s = dec.decode(this.u.subarray(this.p, this.p + n)); this.p += n; return s;
    }
    bytes(n) { const a = this.u.subarray(this.p, this.p + n); this.p += n; return a; }
    rest() { return this.u.subarray(this.p); }
  }

  // stesso ordine di RigTelemetry.Encode
  function decodeTelemetry(r) {
    const t = {};
    t.freq = r.i64(); t.mode = r.str(); t.freqB = r.i64(); t.split = r.bool();
    t.sDb = r.f32(); t.powerW = r.f32(); t.powerRel = r.f32(); t.swr = r.f32(); t.txGain = r.f32();
    t.mute = r.i32(); t.tx = r.bool(); t.catOk = r.bool();
    t.powerSetRel = r.f32(); t.powerSetW = r.f32(); t.powerMaxW = r.f32();
    t.pitch = r.i32(); t.keySpeed = r.i32(); t.rig = r.str(); t.pttInfo = r.str(); t.pttDelay = r.i32(); t.status = r.str();
    t.passband = r.i32();
    t.swrProtect = r.bool(); t.swrWarn = r.f32(); t.swrTrip = r.f32(); t.txBlocked = r.bool(); t.txBlockedSwr = r.f32();
    t.nr = r.i32(); t.nb = r.i32(); t.anf = r.i32(); t.comp = r.i32(); t.att = r.i32(); t.pre = r.i32(); t.agc = r.i32(); t.sql = r.f32(); t.alc = r.f32();
    t.txSec = r.i32(); t.txLimit = r.i32(); t.txTimedOut = r.bool(); t.localLock = r.i32(); t.qsyLock = r.bool(); t.txAllowed = r.bool(); t.txDenied = r.str();
    t.panAvail = r.bool(); t.panStatus = r.str();
    return t;
  }

  // ================================================================ chiavi (come Core/Crypto.cs e Net/WebServer.cs)
  async function fixedSecret(digits) {
    const base = await crypto.subtle.importKey('raw', enc.encode(digits), 'PBKDF2', false, ['deriveBits']);
    const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt: enc.encode('IP-TRX-CTRL codice fisso del server v3'), iterations: 600000 }, base, 256);
    return new Uint8Array(bits);
  }
  async function hkdf(secret, info, len) {
    const k = await crypto.subtle.importKey('raw', secret, 'HKDF', false, ['deriveBits']);
    return new Uint8Array(await crypto.subtle.deriveBits({ name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(0), info: enc.encode(info) }, k, len * 8));
  }
  async function hmac(key, text) {
    const k = await crypto.subtle.importKey('raw', key, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    return new Uint8Array(await crypto.subtle.sign('HMAC', k, enc.encode(text)));
  }
  const b64 = (u) => btoa(String.fromCharCode(...u));

  // ================================================================ elementi
  const $ = (id) => document.getElementById(id);
  const isPc = () => window.matchMedia('(min-width: 900px)').matches;

  const BANDS = [
    ['160', 1810000, 2000000, 1840000, 'LSB'], ['80', 3500000, 4000000, 3700000, 'LSB'], ['60', 5250000, 5450000, 5354000, 'USB'],
    ['40', 7000000, 7300000, 7100000, 'LSB'], ['30', 10100000, 10150000, 10120000, 'CW'], ['20', 14000000, 14350000, 14200000, 'USB'],
    ['17', 18068000, 18168000, 18130000, 'USB'], ['15', 21000000, 21450000, 21250000, 'USB'], ['12', 24890000, 24990000, 24940000, 'USB'],
    ['10', 28000000, 29700000, 28500000, 'USB'], ['6', 50000000, 54000000, 50150000, 'USB'], ['2', 144000000, 148000000, 144300000, 'USB'],
    ['70cm', 430000000, 440000000, 432200000, 'USB']
  ];
  const PHONE_BANDS = ['80', '40', '20', '17', '15', '10'];
  const MODES = ['USB', 'LSB', 'CW', 'CWR', 'AM', 'FM', 'PKTUSB', 'PKTLSB', 'RTTY'];
  const STEPS = [10, 50, 100, 500, 1000, 2500, 5000, 10000, 25000];
  const AGC = { 0: 'OFF', 1: 'S.FAST', 2: 'FAST', 3: 'SLOW', 4: 'USER', 5: 'MID', 6: 'AUTO', 7: 'LONG', 8: 'ON' };

  const store = {
    get(k, d) { try { const v = localStorage.getItem('iptrx.' + k); return v === null ? d : v; } catch { return d; } },
    set(k, v) { try { localStorage.setItem('iptrx.' + k, v); } catch { } },
    del(k) { try { localStorage.removeItem('iptrx.' + k); } catch { } }
  };

  let step = Number(store.get('step', '100')) || 100;
  let tel = null;
  let ws = null, loginKey = null, opName = '', serverCall = '', serverTag = '';
  let connected = false, wantConnection = false, reconnectTimer = 0;
  let latency = NaN, lastPingAt = 0;

  // ================================================================ accesso
  function logLine(cls, sym, text) {
    const d = document.createElement('span');
    const s = document.createElement('span'); s.className = cls; s.textContent = sym + ' ';
    d.appendChild(s); d.appendChild(document.createTextNode(text));
    $('loginLog').appendChild(d);
  }

  function initLogin() {
    // dalla pagina «Trova il server»: #c=codice&n=nome&r=1 (poi si toglie dalla barra degli indirizzi)
    const h = new URLSearchParams(location.hash.slice(1));
    if (h.get('c')) {
      $('code').value = h.get('c');
      if (h.get('r') === '1') store.set('code', h.get('c'));
      history.replaceState(null, '', location.pathname);
    } else $('code').value = store.get('code', '');
    $('opname').value = h.get('n') || store.get('name', '');
    $('remember').checked = store.get('code', '') !== '' || h.get('r') === '1' || !h.get('c');
    $('loginForm').addEventListener('submit', (e) => { e.preventDefault(); enter(); });
    if (!window.isSecureContext || !crypto.subtle) {
      logLine('e', '✗', 'Il browser non permette la crittografia su questa pagina: apri il server con https://');
    }
  }

  async function enter() {
    const digits = ($('code').value || '').replace(/\D/g, '');
    if (digits.length !== 12) { logLine('e', '✗', 'Servono le 12 cifre del codice fisso del server.'); return; }
    opName = ($('opname').value || '').trim().slice(0, 20) || 'Browser';
    $('btnEnter').disabled = true;
    $('loginLog').textContent = '';
    try {
      await startAudio();   // il tocco su «Entra» permette audio e microfono
      logLine('s', '▶', 'chiave dal codice (PBKDF2) …');
      const secret = await fixedSecret(digits);
      loginKey = await hkdf(secret, 'iptrx-web-login', 32);
      logLine('s', '▶', 'ok · collegamento al server …');
      if ($('remember').checked) { store.set('code', digits); store.set('name', opName); } else { store.del('code'); store.set('name', opName); }
      wantConnection = true;
      connect();
    } catch (e) {
      logLine('e', '✗', 'Errore: ' + (e && e.message ? e.message : e));
      $('btnEnter').disabled = false;
    }
  }

  function connect() {
    clearTimeout(reconnectTimer);
    if (!wantConnection) return;
    let s;
    try { s = new WebSocket(`wss://${location.host}/ws`); } catch (e) { scheduleReconnect(); return; }
    s.binaryType = 'arraybuffer';
    ws = s;
    let authed = false;
    s.onmessage = async (ev) => {
      if (typeof ev.data === 'string') {
        let m; try { m = JSON.parse(ev.data); } catch { return; }
        if (m.t === 'challenge') {
          serverCall = m.call || ''; serverTag = m.tag || '';
          const mac = await hmac(loginKey, 'IP-TRX-CTRL web login|' + m.nonce);
          s.send(JSON.stringify({ t: 'login', mac: b64(mac), name: opName }));
        } else if (m.t === 'ok') {
          authed = true;
          onConnected();
        } else if (m.t === 'denied') {
          wantConnection = false;
          showLogin();
          logLine('e', '✗', m.why || 'Accesso rifiutato.');
        }
        return;
      }
      if (authed) onBinary(new Uint8Array(ev.data));
    };
    s.onclose = () => {
      if (ws !== s) return;
      ws = null;
      const was = connected;
      connected = false;
      pttRelease();
      refreshConn();
      if (!authed && !was && $('station').hidden) { logLine('e', '✗', 'Il server non risponde.'); $('btnEnter').disabled = false; }
      scheduleReconnect();
    };
  }

  function scheduleReconnect() {
    if (!wantConnection) return;
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connect, 3000);
  }

  function send(bytes) { if (ws && ws.readyState === 1 && connected) ws.send(bytes); }

  function onConnected() {
    connected = true;
    $('login').hidden = true;
    $('station').hidden = false;
    const ver = (serverTag.split(' ')[1]) || '';
    send(new W(M.Hello).str(opName).str('browser').str(serverTag).build());
    $('call').textContent = serverCall || '—';
    $('pcSub').textContent = `${serverCall} · server ${ver} · audio cifrato · nessuna installazione`;
    spectrumAsked = null;
    refreshConn();
    showNotice('');
  }

  function showLogin() {
    $('station').hidden = true;
    $('login').hidden = false;
    $('btnEnter').disabled = false;
  }

  function logout(forget) {
    wantConnection = false;
    if (forget) store.del('code');
    pttRelease();
    try { ws && ws.close(); } catch { }
    ws = null; connected = false;
    $('dlg').open && $('dlg').close();
    showLogin();
    $('loginLog').textContent = '';
    logLine('r', '✓', 'Uscito.');
  }

  // ================================================================ messaggi dal server
  let noticeTimer = 0;
  function showNotice(text, ms) {
    clearTimeout(noticeTimer);
    $('notice').textContent = text;
    $('notice').hidden = !text;
    if (text && ms) noticeTimer = setTimeout(() => showNotice(''), ms);
  }

  function onBinary(u) {
    if (u.length === 0) return;
    const r = new R(u);
    switch (r.type) {
      case M.Audio: onAudio(r); break;
      case M.Telemetry: tel = decodeTelemetry(r); render(); break;
      case M.Ping: send(new W(M.Pong).raw(r.bytes(8)).build()); break;
      case M.Pong: latency = performance.now() - r.i64(); refreshConn(); break;
      case M.Busy: showNotice(`Il server è occupato: è collegato ${r.str()}. Riprovo da solo.`); break;
      case M.ServerNotice: showNotice(r.str(), 12000); break;
      case M.Spectrum: onSpectrum(r); break;
      case M.PeerInfo: $('kvAddr').textContent = location.hostname; break;
      default: break;
    }
  }

  setInterval(() => {
    if (!connected) return;
    lastPingAt = performance.now();
    send(new W(M.Ping).i64(Math.round(lastPingAt)).build());
  }, 2000);

  function netKind(h) {
    if (/^100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\./.test(h)) return 'VPN';
    if (/^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(h)) return 'LAN';
    return 'internet';
  }

  function refreshConn() {
    const ms = Number.isFinite(latency) ? `${Math.round(latency)} ms` : '— ms';
    $('dot').className = 'dot' + (connected ? (tel && tel.tx ? ' tx' : '') : ' off');
    $('net').textContent = connected ? `${netKind(location.hostname)} · ${ms}` : 'non collegato';
    const c = $('pcConn');
    c.textContent = connected ? `Collegato · ${ms}` : 'Non collegato';
    c.className = 'chip ' + (connected ? 'ok' : 'off');
    $('kvAddr').textContent = `${netKind(location.hostname)} ${location.hostname}`;
  }

  // ================================================================ disegno della telemetria
  const fmtQrg = (hz) => { hz = Math.max(0, Math.round(hz)); const m = Math.floor(hz / 1e6), k = Math.floor(hz / 1000) % 1000, h = hz % 1000; return `${m}.${String(k).padStart(3, '0')}.${String(h).padStart(3, '0')}`; };
  const khz = (hz) => (hz / 1000).toFixed(1).replace('.', ',');
  const stepName = (s) => s < 1000 ? `${s} Hz` : `${String(s / 1000).replace('.', ',')} kHz`;
  const setBar = (id, frac) => { $(id).style.width = `${Math.max(0, Math.min(1, frac || 0)) * 100}%`; };

  function sText(db) {
    if (!Number.isFinite(db)) return '—';
    if (db >= 0) return db < 1 ? 'S9' : `S9+${Math.round(db)}`;
    return 'S' + Math.max(0, Math.min(9, Math.round(9 + db / 6)));
  }

  function render() {
    const t = tel;
    if (!t) return;
    if (!freqEditing) {
      const f = pendingFreq && performance.now() - pendingAt < 1500 ? pendingFreq : t.freq;
      $('freq').textContent = t.freq > 0 ? fmtQrg(f) : '--.---.---';
    }
    $('freq').classList.toggle('tx', t.tx);
    const pb = t.passband > 0 ? `${khz(t.passband)} kHz` : '—';
    $('modePb').textContent = `${t.mode || '—'} · ${pb}`;
    $('unit').textContent = `MHz · ${t.mode || '—'}`;
    $('vfoBShort').textContent = t.freqB > 0 ? `B ${fmtQrg(t.freqB)}` : 'B —';
    $('vfoB').textContent = t.freqB > 0 ? fmtQrg(t.freqB) : '—';
    $('btnSplit').classList.toggle('on', t.split);
    $('rigTitle').textContent = `RADIO REMOTA${t.rig ? ' · ' + t.rig.toUpperCase() : ''}`;
    for (const sel of [$('modeSel'), $('modeSelPc')]) {
      if (document.activeElement !== sel && t.mode && sel.value !== t.mode) {
        if (![...sel.options].some((o) => o.value === t.mode)) { const o = document.createElement('option'); o.value = o.textContent = t.mode; sel.appendChild(o); }
        sel.value = t.mode;
      }
    }
    // banda in uso
    for (const b of BANDS) $('band' + b[0]).classList.toggle('sel', t.freq >= b[1] && t.freq <= b[2]);

    // strumenti
    setBar('bS', Number.isFinite(t.sDb) ? (t.sDb + 54) / 114 : 0);
    $('vS').textContent = sText(t.sDb);
    let po = NaN, poTxt = '—';
    if (t.tx && Number.isFinite(t.powerW) && Number.isFinite(t.powerMaxW) && t.powerMaxW > 0) { po = t.powerW / t.powerMaxW; poTxt = `${Math.round(t.powerW)} W`; }
    else if (t.tx && Number.isFinite(t.powerRel)) { po = t.powerRel; poTxt = `${Math.round(t.powerRel * 100)}%`; }
    setBar('bPo', po); $('vPo').textContent = poTxt;
    const swrOk = t.tx && Number.isFinite(t.swr) && t.swr >= 1;
    setBar('bSwr', swrOk ? (t.swr - 1) / 2 : 0);
    $('vSwr').textContent = swrOk ? t.swr.toFixed(1).replace('.', ',') : '—';
    $('barSwr').classList.toggle('warn', swrOk && t.swrProtect && t.swr >= t.swrWarn && t.swr < t.swrTrip);
    $('barSwr').classList.toggle('bad', swrOk && t.swrProtect && t.swr >= t.swrTrip);
    setBar('bAlc', Number.isFinite(t.alc) ? t.alc : 0);
    $('vAlc').textContent = Number.isFinite(t.alc) ? `${Math.round(t.alc * 100)}%` : '—';

    // comandi della radio (un clic resta visibile finché il server non lo conferma)
    const hold = performance.now() - ctlClickedAt < 5000;
    const tog = (id, v) => { const b = $(id); b.disabled = t.catOk && v < 0; if (!hold && v >= 0) b.classList.toggle('on', v === 1); };
    tog('cNR', t.nr); tog('cNB', t.nb); tog('cANF', t.anf); tog('cCOMP', t.comp);
    $('cATT').textContent = 'ATT ' + (t.att < 0 ? '—' : t.att === 0 ? 'OFF' : t.att);
    $('cPRE').textContent = 'PRE ' + (t.pre < 0 ? '—' : t.pre === 0 ? 'OFF' : t.pre);
    $('cAGC').textContent = 'AGC ' + (AGC[t.agc] || '—');
    $('cATT').classList.toggle('on', t.att > 0); $('cPRE').classList.toggle('on', t.pre > 0);
    $('cATT').disabled = t.catOk && t.att < 0; $('cPRE').disabled = t.catOk && t.pre < 0; $('cAGC').disabled = t.catOk && t.agc < 0;

    // potenza impostata
    if (document.activeElement !== $('power') && performance.now() - powerAt > 2000) {
      if (Number.isFinite(t.powerSetRel)) $('power').value = Math.round(t.powerSetRel * 100);
    }
    $('powerTxt').textContent = Number.isFinite(t.powerSetW) ? `${Math.round(t.powerSetW)} W` : Number.isFinite(t.powerSetRel) ? `${Math.round(t.powerSetRel * 100)}%` : '—';

    // PTT e riga della trasmissione
    const mm = (s) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
    let line = '', alert = false;
    if (!t.txAllowed) { line = 'Trasmissione non permessa: ' + t.txDenied; alert = true; }
    else if (t.txBlocked) { line = 'Trasmissione bloccata dal server: ROS alto'; alert = true; }
    else if (t.txTimedOut) { line = 'Tempo massimo raggiunto: rilascia il PTT'; alert = true; }
    else if (t.txSec > 0) { line = t.txLimit > 0 ? `In trasmissione da ${mm(t.txSec)} · massimo ${mm(t.txLimit)}` : `In trasmissione da ${mm(t.txSec)}`; alert = t.txLimit > 0 && t.txLimit - t.txSec <= 15; }
    else {
      const max = t.txLimit > 0 ? `massimo ${mm(t.txLimit)}` : 'nessun tempo massimo';
      line = isPc() ? `Anche con la barra spaziatrice · ${max}` : `Tempo massimo ${t.txLimit > 0 ? mm(t.txLimit) : '—'} · ${micReady ? 'microfono del telefono pronto' : 'microfono non disponibile: solo ascolto'}`;
    }
    $('txline').textContent = line;
    $('txline').classList.toggle('alert', alert);
    const denied = !t.txAllowed || t.txBlocked;
    $('ptt').classList.toggle('denied', denied);
    $('ptt').classList.toggle('active', pttOn || t.tx);
    $('ptt').textContent = denied ? 'PTT NON PERMESSO' : isPc() ? 'PTT (PREMI E TIENI)' : 'PTT · PREMI E TIENI';
    refreshConn();

    // panadapter
    refreshPan();
  }

  // ================================================================ sintonia
  let pendingFreq = 0, pendingAt = 0, freqEditing = false, ctlClickedAt = -1e9, powerAt = -1e9;

  function setFreq(hz, mode) {
    hz = Math.max(1000, Math.round(hz));
    pendingFreq = hz; pendingAt = performance.now();
    $('freq').textContent = fmtQrg(hz);
    send(new W(M.SetFreqMode).i64(hz).str(mode || '').build());
  }

  function stepTune(n) {
    if (!tel || !tel.freq) return;
    const cur = pendingFreq && performance.now() - pendingAt < 1500 ? pendingFreq : tel.freq;
    const base = n > 0 ? Math.floor(cur / step) * step : Math.ceil(cur / step) * step;
    let nf = base + n * step;
    if (nf === cur) nf += n * step;
    setFreq(nf, '');
  }

  function goBand(b) {
    if (!tel) return;
    const cur = BANDS.find((x) => tel.freq >= x[1] && tel.freq <= x[2]);
    if (cur) { store.set('band.' + cur[0], `${tel.freq}|${tel.mode}`); if (cur[0] === b[0]) return; }
    const last = (store.get('band.' + b[0], '') || '').split('|');
    const f = Number(last[0]);
    if (f >= b[1] && f <= b[2]) setFreq(f, last[1] || ''); else setFreq(b[3], b[4]);
  }

  function editFreq() {
    if (!tel || freqEditing) return;
    freqEditing = true;
    const inp = $('freqIn');
    inp.value = (tel.freq / 1000).toFixed(3).replace('.', ',');
    $('freq').hidden = true; inp.hidden = false; inp.focus(); inp.select();
    const done = (ok) => {
      if (!freqEditing) return;
      freqEditing = false;
      inp.hidden = true; $('freq').hidden = false;
      if (!ok) return;
      const v = Number(String(inp.value).replace(/\s/g, '').replace(',', '.'));
      if (!Number.isFinite(v) || v <= 0) return;
      setFreq(v >= 100000 ? v : v * 1000, '');   // in kHz, oppure in Hz se il numero è grande
    };
    inp.onkeydown = (e) => { if (e.key === 'Enter') done(true); if (e.key === 'Escape') done(false); };
    inp.onblur = () => done(true);
  }

  function ctlToggle(name, id) {
    ctlClickedAt = performance.now();
    const b = $(id); b.classList.toggle('on');
    send(new W(M.SetRadioCtl).str(name).f32(b.classList.contains('on') ? 1 : 0).build());
  }
  function ctlStep(name) {
    ctlClickedAt = performance.now();
    send(new W(M.SetRadioCtl).str(name).f32(-1).build());
  }

  function buildControls() {
    const bands = $('bands');
    for (const b of BANDS) {
      const btn = document.createElement('button');
      btn.type = 'button'; btn.className = 'btn' + (PHONE_BANDS.includes(b[0]) ? '' : ' more'); btn.id = 'band' + b[0];
      btn.textContent = b[0];
      btn.title = `${b[0]}${b[0].endsWith('cm') ? '' : ' m'}: torna all'ultima frequenza usata sulla banda`;
      btn.addEventListener('click', () => goBand(b));
      bands.appendChild(btn);
    }
    for (const sel of [$('modeSel'), $('modeSelPc')]) {
      for (const m of MODES) { const o = document.createElement('option'); o.value = o.textContent = m; sel.appendChild(o); }
      sel.addEventListener('change', () => { if (tel) send(new W(M.SetFreqMode).i64(0).str(sel.value).build()); });
    }
    const ss = $('stepSel');
    for (const s of STEPS) { const o = document.createElement('option'); o.value = String(s); o.textContent = stepName(s); ss.appendChild(o); }
    if (!STEPS.includes(step)) step = 100;
    ss.value = String(step);
    $('stepShort').textContent = `passo ${stepName(step)}`;
    ss.addEventListener('change', () => { step = Number(ss.value); store.set('step', String(step)); $('stepShort').textContent = `passo ${stepName(step)}`; });
    $('stepDown').addEventListener('click', () => stepTune(-1));
    $('stepUp').addEventListener('click', () => stepTune(1));
    $('stepDownPc').addEventListener('click', () => stepTune(-1));
    $('stepUpPc').addEventListener('click', () => stepTune(1));
    $('qrg').addEventListener('click', (e) => { if (e.target !== $('freqIn')) editFreq(); });
    $('qrg').addEventListener('wheel', (e) => { e.preventDefault(); stepTune(e.deltaY < 0 ? 1 : -1); }, { passive: false });
    $('btnAB').addEventListener('click', () => send(new W(M.VfoOp).str('XCHG').build()));
    $('btnSplit').addEventListener('click', () => send(new W(M.SetSplit).u8(tel && tel.split ? 0 : 1).build()));
    $('cNR').addEventListener('click', () => ctlToggle('NR', 'cNR'));
    $('cNB').addEventListener('click', () => ctlToggle('NB', 'cNB'));
    $('cANF').addEventListener('click', () => ctlToggle('ANF', 'cANF'));
    $('cCOMP').addEventListener('click', () => ctlToggle('COMP', 'cCOMP'));
    $('cATT').addEventListener('click', () => ctlStep('ATT'));
    $('cPRE').addEventListener('click', () => ctlStep('PREAMP'));
    $('cAGC').addEventListener('click', () => ctlStep('AGC'));
    let powerTimer = 0;
    $('power').addEventListener('input', () => {
      powerAt = performance.now();
      const v = Number($('power').value) / 100;
      $('powerTxt').textContent = tel && Number.isFinite(tel.powerMaxW) && tel.powerMaxW > 0 ? `${Math.round(v * tel.powerMaxW)} W` : `${Math.round(v * 100)}%`;
      clearTimeout(powerTimer);
      powerTimer = setTimeout(() => send(new W(M.SetPower).f32(v).bool(false).build()), 250);
    });
    // volume (uguale sul telefono e sul PC)
    const vol = Number(store.get('vol', '70'));
    for (const [id, txt] of [['vol', 'volTxt'], ['volPc', 'volPcTxt']]) {
      $(id).value = vol; $(txt).textContent = `${vol}%`;
      $(id).addEventListener('input', () => {
        const v = Number($(id).value);
        for (const [i2, t2] of [['vol', 'volTxt'], ['volPc', 'volPcTxt']]) { $(i2).value = v; $(t2).textContent = `${v}%`; }
        store.set('vol', String(v));
        if (gain) gain.gain.value = v / 100;
      });
    }
    // impostazioni ed uscita
    const openDlg = () => { fillDevices(); $('dlgInfo').textContent = `Server ${serverCall} · ${location.host} · operatore ${opName}`; $('dlg').showModal(); };
    $('who').addEventListener('click', openDlg);
    $('btnSettings').addEventListener('click', openDlg);
    $('btnClose').addEventListener('click', () => $('dlg').close());
    $('btnLogout').addEventListener('click', () => logout(false));
    $('btnLogout2').addEventListener('click', () => logout(false));
    $('btnForget').addEventListener('click', () => logout(true));
  }

  // ================================================================ PTT (premi e tieni; il server toglie tutto se smettiamo di ripeterlo)
  let pttOn = false, pttTimer = 0;
  function pttPress() {
    if (!connected || pttOn) return;
    if (tel && (!tel.txAllowed || tel.txBlocked)) { showNotice('Trasmissione non permessa: ' + (tel.txDenied || 'bloccata dal server') + '.', 6000); return; }
    if (!micReady) showNotice('Microfono non disponibile: il server trasmetterebbe senza la tua voce.', 6000);
    pttOn = true;
    send(new W(M.Ptt).u8(1).build());
    pttTimer = setInterval(() => send(new W(M.Ptt).u8(1).build()), 250);
    if (tel) render();
  }
  function pttRelease() {
    if (!pttOn) return;
    pttOn = false;
    clearInterval(pttTimer);
    send(new W(M.Ptt).u8(0).build());
    if (tel) render();
  }
  function bindPtt() {
    const b = $('ptt');
    b.addEventListener('pointerdown', (e) => { e.preventDefault(); try { b.setPointerCapture(e.pointerId); } catch { } pttPress(); });
    for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) b.addEventListener(ev, pttRelease);
    b.addEventListener('contextmenu', (e) => e.preventDefault());
    const typing = () => { const a = document.activeElement; return a && (a.tagName === 'INPUT' || a.tagName === 'SELECT' || a.tagName === 'TEXTAREA'); };
    document.addEventListener('keydown', (e) => { if (e.code === 'Space' && !e.repeat && !typing() && !$('station').hidden) { e.preventDefault(); pttPress(); } });
    document.addEventListener('keyup', (e) => { if (e.code === 'Space') pttRelease(); });
    window.addEventListener('blur', pttRelease);
    document.addEventListener('visibilitychange', () => { if (document.hidden) pttRelease(); });
  }

  // ================================================================ audio (8 kHz, 16 bit, blocchi da 40 ms)
  let actx = null, rxNode = null, txNode = null, gain = null, micStream = null, micReady = false, txSeq = 0;
  let lastSeq = 0, lost = 0, expected = 0, lastArr = 0, jitter = 0;

  async function startAudio() {
    if (actx) { if (actx.state === 'suspended') await actx.resume(); return; }
    actx = new (window.AudioContext || window.webkitAudioContext)({ latencyHint: 'interactive' });
    await actx.audioWorklet.addModule('audio-worklet.js');
    rxNode = new AudioWorkletNode(actx, 'rx-player', { numberOfInputs: 0, outputChannelCount: [1] });
    gain = actx.createGain();
    gain.gain.value = Number(store.get('vol', '70')) / 100;
    rxNode.connect(gain).connect(actx.destination);
    await openMic(store.get('mic', ''));
    const spk = store.get('spk', '');
    if (spk && actx.setSinkId) { try { await actx.setSinkId(spk); } catch { } }
    fillDevices();
  }

  async function openMic(deviceId) {
    try {
      if (micStream) { micStream.getTracks().forEach((t) => t.stop()); micStream = null; }
      if (txNode) { txNode.disconnect(); txNode = null; }
      const c = { echoCancellation: false, noiseSuppression: false, autoGainControl: true, channelCount: 1 };
      if (deviceId) c.deviceId = { exact: deviceId };
      micStream = await navigator.mediaDevices.getUserMedia({ audio: c });
      const src = actx.createMediaStreamSource(micStream);
      txNode = new AudioWorkletNode(actx, 'tx-capture', { numberOfOutputs: 1, outputChannelCount: [1] });
      txNode.port.onmessage = (e) => onMicFrame(e.data);
      const mute = actx.createGain(); mute.gain.value = 0;   // tiene vivo il nodo senza sentirsi
      src.connect(txNode).connect(mute).connect(actx.destination);
      micReady = true;
    } catch (e) {
      micReady = false;
      showNotice('Microfono non disponibile (' + (e && e.name ? e.name : 'permesso negato') + '): solo ascolto.', 10000);
    }
  }

  function onMicFrame(pcm) {
    // pcm: Int16Array di 320 campioni a 8 kHz; parte solo con il PTT premuto
    if (!pttOn || !connected) return;
    const b = new Uint8Array(1 + 4 + pcm.length * 2);
    const d = new DataView(b.buffer);
    b[0] = M.Audio; d.setUint32(1, ++txSeq >>> 0, true);
    for (let i = 0; i < pcm.length; i++) d.setInt16(5 + i * 2, pcm[i], true);
    send(b);
  }

  function onAudio(r) {
    const seq = r.u32();
    const bytes = r.rest();
    const n = bytes.length >> 1;
    const f = new Float32Array(n);
    const d = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (let i = 0; i < n; i++) f[i] = d.getInt16(i * 2, true) / 32768;
    if (rxNode) rxNode.port.postMessage(f, [f.buffer]);
    const now = performance.now();
    if (lastArr) jitter += (Math.abs(now - lastArr - 40) - jitter) / 16;
    lastArr = now;
    if (lastSeq) { const diff = seq - lastSeq; if (diff > 1) lost += diff - 1; if (diff > 0) expected += diff; } else expected = 1;
    lastSeq = seq;
  }
  setInterval(() => {
    $('kvJit').textContent = lastArr ? `${Math.round(jitter)} ms` : '—';
    $('kvLoss').textContent = expected ? `${(100 * lost / expected).toFixed(1).replace('.', ',')} %` : '—';
  }, 1000);

  async function fillDevices() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;
    let list = [];
    try { list = await navigator.mediaDevices.enumerateDevices(); } catch { return; }
    const fill = (sel, kind, key) => {
      const cur = store.get(key, '');
      sel.textContent = '';
      const def = document.createElement('option'); def.value = ''; def.textContent = 'Predefinito'; sel.appendChild(def);
      for (const d of list.filter((x) => x.kind === kind)) {
        const o = document.createElement('option'); o.value = d.deviceId; o.textContent = d.label || (kind === 'audioinput' ? 'Microfono' : 'Altoparlante'); sel.appendChild(o);
      }
      sel.value = cur;
    };
    for (const id of ['micSel', 'micSel2']) fill($(id), 'audioinput', 'mic');
    for (const id of ['spkSel', 'spkSel2']) { fill($(id), 'audiooutput', 'spk'); $(id).disabled = !(actx && actx.setSinkId); }
  }
  function bindDevices() {
    for (const id of ['micSel', 'micSel2']) $(id).addEventListener('change', async () => { store.set('mic', $(id).value); if (actx) await openMic($(id).value); fillDevices(); });
    for (const id of ['spkSel', 'spkSel2']) $(id).addEventListener('change', async () => {
      store.set('spk', $(id).value);
      if (actx && actx.setSinkId) { try { await actx.setSinkId($(id).value); } catch { } }
      fillDevices();
    });
  }

  // ================================================================ panadapter
  let spectrumAsked = null, pan = null;
  const PAN_ROWS = 120;

  function refreshPan() {
    const show = !!(tel && tel.panAvail) && isPc();
    $('pan').hidden = !show;
    if (spectrumAsked !== show && connected) { spectrumAsked = show; send(new W(M.SpectrumOn).bool(show).build()); }
    if (tel) $('panInfo').textContent = tel.panStatus ? `${tel.panStatus} · clic = sintonia` : 'clic = sintonia';
  }

  function onSpectrum(r) {
    const start = r.i64(), end = r.i64(), out = r.bool(), n = Math.min(4096, r.i32());
    const bins = r.bytes(n);
    if (!pan) pan = { rows: [], start: 0, end: 0, smooth: null };
    if (out || n === 0) { pan.out = true; drawPan(); return; }
    pan.out = false;
    if (pan.start !== start || pan.end !== end || !pan.smooth || pan.smooth.length !== n) { pan.rows = []; pan.smooth = new Float32Array(n); }
    pan.start = start; pan.end = end;
    for (let i = 0; i < n; i++) pan.smooth[i] = pan.smooth[i] * 0.4 + bins[i] * 0.6;
    // riga della cascata già colorata (fondo = mediana della riga)
    const med = Uint8Array.from(bins).sort()[n >> 1];
    const rgba = new Uint8ClampedArray(n * 4);
    for (let x = 0; x < n; x++) {
      const [rr, gg, bb] = color(Math.max(0, Math.min(255, (bins[x] - med) * 2.6 + 40)));
      rgba[x * 4] = rr; rgba[x * 4 + 1] = gg; rgba[x * 4 + 2] = bb; rgba[x * 4 + 3] = 255;
    }
    pan.rows.unshift(rgba);
    if (pan.rows.length > PAN_ROWS) pan.rows.length = PAN_ROWS;
    drawPan();
  }

  function color(v) {
    const stops = [[0, 4, 14, 40], [0.35, 10, 60, 140], [0.6, 30, 170, 200], [0.8, 250, 220, 60], [1, 255, 60, 30]];
    const x = v / 255;
    let k = 0; while (k < stops.length - 2 && x > stops[k + 1][0]) k++;
    const a = stops[k], b = stops[k + 1], t = Math.min(1, Math.max(0, (x - a[0]) / (b[0] - a[0])));
    return [a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3] + (b[3] - a[3]) * t];
  }

  let panPending = false;
  function drawPan() {
    if (panPending) return;
    panPending = true;
    requestAnimationFrame(() => {
      panPending = false;
      const c = $('panCanvas');
      if ($('pan').hidden) return;
      const dpr = window.devicePixelRatio || 1;
      const w = Math.max(100, Math.round(c.clientWidth * dpr)), h = Math.max(80, Math.round(c.clientHeight * dpr));
      if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
      const g = c.getContext('2d');
      g.fillStyle = '#04102A'; g.fillRect(0, 0, w, h);
      if (!pan || pan.out || !pan.smooth) {
        g.fillStyle = '#C9D4E5'; g.font = `${12 * dpr}px Inter, sans-serif`; g.textAlign = 'center';
        g.fillText(pan && pan.out ? 'Frequenza fuori dal campo dello scopio della radio' : 'In attesa dei dati del panadapter…', w / 2, h / 2);
        return;
      }
      const n = pan.smooth.length, specH = Math.round(h * 0.38), wfH = h - specH;
      // cascata
      const rows = pan.rows.length;
      if (rows > 0) {
        const img = g.createImageData(n, rows);
        for (let y = 0; y < rows; y++) if (pan.rows[y].length === n * 4) img.data.set(pan.rows[y], y * n * 4);
        const tmp = document.createElement('canvas'); tmp.width = n; tmp.height = rows;
        tmp.getContext('2d').putImageData(img, 0, 0);
        g.imageSmoothingEnabled = false;
        g.drawImage(tmp, 0, specH, w, wfH * rows / PAN_ROWS);   // la cascata si riempie dall'alto
      }
      // spettro
      g.fillStyle = 'rgba(10,42,92,0.6)'; g.fillRect(0, 0, w, specH);
      g.beginPath(); g.moveTo(0, specH);
      for (let x = 0; x < n; x++) g.lineTo(x / (n - 1) * w, specH - pan.smooth[x] / 255 * (specH - 4));
      g.lineTo(w, specH); g.closePath();
      g.fillStyle = 'rgba(120,200,255,0.28)'; g.fill();
      g.strokeStyle = '#96DCFF'; g.lineWidth = dpr; g.stroke();
      // filtro e frequenza di ricezione
      const xOf = (hz) => (hz - pan.start) / (pan.end - pan.start) * w;
      if (tel && tel.freq) {
        const pb = tel.passband || 0, m = (tel.mode || '').toUpperCase();
        let lo = tel.freq, hi = tel.freq;
        if (m.startsWith('LSB') || m === 'PKTLSB' || m === 'CWR') lo -= pb; else if (m.startsWith('CW') || m.startsWith('AM') || m.startsWith('FM')) { lo -= pb / 2; hi += pb / 2; } else hi += pb;
        if (pb > 0) { g.fillStyle = 'rgba(255,255,255,0.12)'; g.fillRect(xOf(lo), 0, Math.max(dpr, xOf(hi) - xOf(lo)), h); }
        g.fillStyle = '#FF4D4D'; g.fillRect(xOf(tel.freq) - dpr, 0, 2 * dpr, h);
      }
    });
  }

  function bindPan() {
    const c = $('panCanvas');
    c.addEventListener('click', (e) => {
      if (!pan || !pan.end) return;
      const rect = c.getBoundingClientRect();
      const hz = pan.start + (e.clientX - rect.left) / rect.width * (pan.end - pan.start);
      const r = Math.min(Math.max(step, 10), 1000);
      setFreq(Math.round(hz / r) * r, '');
    });
    c.addEventListener('wheel', (e) => { e.preventDefault(); stepTune(e.deltaY < 0 ? 1 : -1); }, { passive: false });
  }

  // ================================================================ avvio
  initLogin();
  buildControls();
  bindPtt();
  bindDevices();
  bindPan();
  window.matchMedia('(min-width: 900px)').addEventListener('change', () => { if (tel) render(); });
})();
