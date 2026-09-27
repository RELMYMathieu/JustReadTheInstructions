import { API, STREAM_RETRY_MIN_MS } from './config.js';

const SAMPLE_RATE = 48000;
const CHANNELS = 2;
const BYTES_PER_FRAME = CHANNELS * 2;
const WAV_HEADER_BYTES = 44;
const TARGET_LATENCY_S = 0.15;
const MAX_LATENCY_S = 0.6;

function concat(a, b) {
    if (a.length === 0) return b;
    const joined = new Uint8Array(a.length + b.length);
    joined.set(a);
    joined.set(b, a.length);
    return joined;
}

export class CameraAudio {
    constructor(cameraId) {
        this._cameraId = cameraId;
        this._context = null;
        this._abort = null;
        this._retry = null;
        this._nextTime = 0;
    }

    get playing() {
        return this._abort !== null;
    }

    start() {
        if (this._abort) return;
        this._context ??= new AudioContext();
        this._resumeWhenAllowed();
        this._abort = new AbortController();
        this._read(this._abort.signal);
    }

    stop() {
        clearTimeout(this._retry);
        this._abort?.abort();
        this._abort = null;
        this._context?.suspend();
    }

    toggle() {
        if (this.playing) this.stop();
        else this.start();
    }

    _resumeWhenAllowed() {
        this._context.resume();
        if (this._context.state !== 'suspended') return;
        document.addEventListener('pointerdown', () => { if (this.playing) this._context.resume(); }, { once: true });
    }

    async _read(signal) {
        try {
            const res = await fetch(API.audio(this._cameraId), { signal, cache: 'no-store' });
            if (!res.ok || !res.body) throw new Error(`audio stream failed: ${res.status}`);
            const reader = res.body.getReader();
            let headerLeft = WAV_HEADER_BYTES;
            let pending = new Uint8Array(0);

            for (;;) {
                const { done, value } = await reader.read();
                if (done) break;
                const skipped = Math.min(headerLeft, value.length);
                headerLeft -= skipped;
                pending = concat(pending, value.subarray(skipped));

                const whole = pending.length - (pending.length % BYTES_PER_FRAME);
                if (whole === 0) continue;
                this._schedule(pending.subarray(0, whole));
                pending = pending.slice(whole);
            }
        } catch {
            if (signal.aborted) return;
        }
        if (!signal.aborted) this._retry = setTimeout(() => this._read(signal), STREAM_RETRY_MIN_MS);
    }

    _schedule(bytes) {
        const context = this._context;
        const now = context.currentTime;
        if (this._nextTime <= now) this._nextTime = now + TARGET_LATENCY_S;
        if (this._nextTime > now + MAX_LATENCY_S) return;

        const frames = bytes.length / BYTES_PER_FRAME;
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.length);
        const buffer = context.createBuffer(CHANNELS, frames, SAMPLE_RATE);
        for (let channel = 0; channel < CHANNELS; channel++) {
            const data = buffer.getChannelData(channel);
            for (let i = 0; i < frames; i++) data[i] = view.getInt16((i * CHANNELS + channel) * 2, true) / 32768;
        }

        const node = context.createBufferSource();
        node.buffer = buffer;
        node.connect(context.destination);
        node.start(this._nextTime);
        this._nextTime += buffer.duration;
    }
}
