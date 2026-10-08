import {mkdir, writeFile} from 'node:fs/promises';

// Original 64-beat score: every scene change lands on the 128 BPM grid.
const rate = 48000;
const seconds = 30;
const beat = 60 / 128;
const length = rate * seconds;
const left = new Float64Array(length);
const right = new Float64Array(length);
let seed = 9137;
const noise = () => {seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2147483648 - 1;};
const tau = Math.PI * 2;
function add(start, duration, fn, gain = 1, pan = 0) {
  const begin = Math.round(start * rate);
  for (let i = 0; i < duration * rate && begin + i < length; i++) {
    const t = i / rate;
    const v = fn(t, i) * gain;
    left[begin + i] += v * Math.sqrt((1 - pan) / 2);
    right[begin + i] += v * Math.sqrt((1 + pan) / 2);
  }
}
const kick = t => Math.sin(tau * (48 * t + 105 * .022 * (1 - Math.exp(-t / .022)))) * Math.exp(-t * 14);
const snare = t => (noise() * .8 + Math.sin(tau * 180 * t) * .25) * Math.exp(-t * 24);
const hat = t => noise() * Math.exp(-t * 100);
const notes = [41.2034, 41.2034, 48.9994, 36.7081];
for (let b = 0; b < 64; b++) {
  const start = b * beat;
  const active = b >= 8 && b < 56;
  if (active || b % 4 === 0) add(start, .5, kick, active ? .8 : .7);
  if (active && b % 4 === 2) add(start, .3, snare, .32);
  if (active) {
    for (let h = 0; h < 2; h++) add(start + h * beat / 2, .08, hat, h ? .095 : .065, h ? .35 : -.35);
    const frequency = notes[Math.floor(b / 8) % 4];
    add(start, beat * .86, t => {
      const env = Math.min(1, t * 90) * Math.exp(-t * 7);
      return Math.tanh((Math.sin(tau * frequency * t) + .27 * Math.sin(tau * frequency * 2 * t)) * 2) * env;
    }, .25);
    const arp = [0, 7, 12, 10, 7, 3, 12, 15][b % 8];
    const f = 164.8138 * 2 ** (arp / 12);
    for (let echo = 0; echo < 3; echo++) add(start + echo * beat * .75, .36,
      t => (Math.sin(tau * f * t) + .18 * Math.sin(tau * f * 3 * t)) * Math.min(1, t * 120) * Math.exp(-t * 11),
      .09 * .4 ** echo, echo % 2 ? -.4 : .4);
  }
}
for (let bar = 0; bar < 8; bar++) {
  const f = notes[bar % 4] * 4;
  add(bar * beat * 8, beat * 8, t => {
    const env = Math.sin(Math.PI * t / (beat * 8)) ** 2;
    return (Math.sin(tau * f * t) + Math.sin(tau * f * 1.498 * t) * .5 + Math.sin(tau * f * 2.003 * t) * .25) * env;
  }, .065, bar % 2 ? -.2 : .2);
}
for (const b of [8, 16, 28, 40, 48, 56]) {
  const cut = b * beat;
  add(Math.max(0, cut - .65), .65, t => noise() * (t / .65) ** 3, .15);
  add(cut, 1.3, t => (Math.sin(tau * (37 * t + 35 * .08 * (1 - Math.exp(-t / .08)))) + noise() * .12) * Math.exp(-t * 4), .7);
  add(cut, .65, t => Math.sin(tau * (900 * t - 500 * t * t)) * Math.exp(-t * 12), .09, .3);
}
let peak = 0;
for (let i = 0; i < length; i++) peak = Math.max(peak, Math.abs(left[i]), Math.abs(right[i]));
const buffer = Buffer.alloc(44 + length * 4);
buffer.write('RIFF', 0); buffer.writeUInt32LE(buffer.length - 8, 4); buffer.write('WAVEfmt ', 8);
buffer.writeUInt32LE(16, 16); buffer.writeUInt16LE(1, 20); buffer.writeUInt16LE(2, 22);
buffer.writeUInt32LE(rate, 24); buffer.writeUInt32LE(rate * 4, 28); buffer.writeUInt16LE(4, 32);
buffer.writeUInt16LE(16, 34); buffer.write('data', 36); buffer.writeUInt32LE(length * 4, 40);
for (let i = 0; i < length; i++) {
  const t = i / rate;
  const fade = Math.min(1, t / .04, (seconds - t) / .7);
  buffer.writeInt16LE(Math.round(left[i] / peak * .88 * fade * 32767), 44 + i * 4);
  buffer.writeInt16LE(Math.round(right[i] / peak * .88 * fade * 32767), 46 + i * 4);
}
await mkdir(new URL('../public/', import.meta.url), {recursive: true});
await writeFile(new URL('../public/soundtrack.wav', import.meta.url), buffer);
console.log('Original stereo score: 30 s / 128 BPM / 48 kHz');
