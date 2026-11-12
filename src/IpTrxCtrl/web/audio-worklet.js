// IP-TRX-CTRL · audio della pagina web (dalla 0.3.1)
// rx-player: audio della radio a 8 kHz → frequenza della scheda del browser, con riserva di 160 ms.
// tx-capture: microfono → 8 kHz, blocchi da 320 campioni (40 ms) come il programma client.
'use strict';

const RATE = 8000;

class RxPlayer extends AudioWorkletProcessor {
  constructor() {
    super();
    this.buf = new Float32Array(RATE * 2);   // 2 s di anello
    this.w = 0; this.r = 0; this.n = 0;
    this.pos = 0;                             // posizione frazionaria tra due campioni a 8 kHz
    this.started = false;
    this.step = RATE / sampleRate;
    this.port.onmessage = (e) => this.push(e.data);
  }

  push(f) {
    for (let i = 0; i < f.length; i++) {
      this.buf[this.w] = f[i];
      this.w = (this.w + 1) % this.buf.length;
      if (this.n < this.buf.length) this.n++; else this.r = (this.r + 1) % this.buf.length;
    }
    // troppo ritardo accumulato (rete a raffiche): si torna a 200 ms
    if (this.n > RATE * 0.6) { const drop = this.n - RATE * 0.2; this.r = (this.r + drop) % this.buf.length; this.n -= drop; }
  }

  process(inputs, outputs) {
    const out = outputs[0][0];
    if (!this.started) { if (this.n >= RATE * 0.16) this.started = true; else { out.fill(0); return true; } }
    for (let i = 0; i < out.length; i++) {
      if (this.n < 2) { out[i] = 0; this.started = false; continue; }   // riserva finita: si riempie di nuovo
      const a = this.buf[this.r], b = this.buf[(this.r + 1) % this.buf.length];
      out[i] = a + (b - a) * this.pos;
      this.pos += this.step;
      while (this.pos >= 1) { this.pos -= 1; this.r = (this.r + 1) % this.buf.length; this.n--; }
    }
    return true;
  }
}

class TxCapture extends AudioWorkletProcessor {
  constructor() {
    super();
    this.ratio = sampleRate / RATE;   // es. 6 a 48 kHz, 5,5125 a 44,1 kHz
    this.acc = 0; this.cnt = 0; this.pos = 0;
    this.frame = new Int16Array(320); this.fi = 0;
  }

  process(inputs) {
    const inp = inputs[0] && inputs[0][0];
    if (!inp) return true;
    for (let i = 0; i < inp.length; i++) {
      // media dei campioni di ogni intervallo da 1/8000 s: filtro passa basso semplice prima di scendere a 8 kHz
      this.acc += inp[i]; this.cnt++; this.pos += 1;
      if (this.pos >= this.ratio) {
        this.pos -= this.ratio;
        const v = Math.max(-1, Math.min(1, this.acc / this.cnt));
        this.frame[this.fi++] = Math.round(v * 32767);
        this.acc = 0; this.cnt = 0;
        if (this.fi === this.frame.length) {
          this.port.postMessage(this.frame);
          this.frame = new Int16Array(320); this.fi = 0;
        }
      }
    }
    return true;
  }
}

registerProcessor('rx-player', RxPlayer);
registerProcessor('tx-capture', TxCapture);
