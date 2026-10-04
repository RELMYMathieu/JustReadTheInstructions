import { API, STREAM_RETRY_MIN_MS } from './config.js';

const SAMPLE_RATE = 48000;
const CHANNELS = 2;
const BYTES_PER_FRAME = CHANNELS * 2;
const WAV_HEADER_BYTES = 44;
const TARGET_LATENCY_S = 0.15;
const MAX_LATENCY_S = 0.6;
const FADE_S = 0.08;

function concat(a, b) {
    if (a.length === 0) return b;
    const joined = new Uint8Array(a.length + b.length);
    joined.set(a);
    joined.set(b, a.length);
    return joined;
}

export class CameraAudio {
    constructor(cameraId = null) {
        this._cameraId = cameraId;
        this._context = null;
        this._stream = null;
        this._retry = null;
        this._on = false;
    }

    get playing() {
        return this._on;
    }

    start() {
        if (this._on) return;
        this._on = true;
        this._context ??= new AudioContext();
        this._resumeWhenAllowed();
        this._connect();
    }

    stop() {
        if (!this._on) return;
        this._on = false;
        this._disconnect(0);
        this._context.suspend();
    }

    toggle() {
        if (this.playing) this.stop();
        else this.start();
    }

    setCamera(cameraId) {
        if (cameraId === this._cameraId) return;
        this._cameraId = cameraId;
        if (!this._on) return;
        this._disconnect(FADE_S);
        this._connect();
    }

    _resumeWhenAllowed() {
        this._context.resume();
        if (this._context.state !== 'suspended') return;
        document.addEventListener('pointerdown', () => { if (this._on) this._context.resume(); }, { once: true });
    }

    _connect() {
        if (this._cameraId === null) return;
        const gain = this._context.createGain();
        gain.gain.value = 0;
        gain.connect(this._context.destination);
        const stream = { abort: new AbortController(), gain, nextTime: 0, heard: false };
        this._stream = stream;
        this._read(this._cameraId, stream);
    }

    _disconnect(fadeSeconds) {
        clearTimeout(this._retry);
        const stream = this._stream;
        this._stream = null;
        if (!stream) return;

        stream.abort.abort();
        const level = stream.gain.gain;
        const now = this._context.currentTime;
        level.cancelScheduledValues(now);
        level.setValueAtTime(level.value, now);
        level.linearRampToValueAtTime(0, now + fadeSeconds);
        setTimeout(() => stream.gain.disconnect(), (fadeSeconds + MAX_LATENCY_S) * 1000);
    }

    async _read(cameraId, stream) {
        try {
            const res = await fetch(API.audio(cameraId), { signal: stream.abort.signal, cache: 'no-store' });
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
                this._schedule(stream, pending.subarray(0, whole));
                pending = pending.slice(whole);
            }
        } catch {
            if (stream.abort.signal.aborted) return;
        }
        if (this._stream === stream) this._retry = setTimeout(() => this._read(cameraId, stream), STREAM_RETRY_MIN_MS);
    }

    _schedule(stream, bytes) {
        const context = this._context;
        const now = context.currentTime;
        if (stream.nextTime <= now) stream.nextTime = now + TARGET_LATENCY_S;
        if (stream.nextTime > now + MAX_LATENCY_S) return;

        const frames = bytes.length / BYTES_PER_FRAME;
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.length);
        const buffer = context.createBuffer(CHANNELS, frames, SAMPLE_RATE);
        for (let channel = 0; channel < CHANNELS; channel++) {
            const data = buffer.getChannelData(channel);
            for (let i = 0; i < frames; i++) data[i] = view.getInt16((i * CHANNELS + channel) * 2, true) / 32768;
        }

        if (!stream.heard) {
            stream.heard = true;
            stream.gain.gain.setValueAtTime(0, stream.nextTime);
            stream.gain.gain.linearRampToValueAtTime(1, stream.nextTime + FADE_S);
        }

        const node = context.createBufferSource();
        node.buffer = buffer;
        node.connect(stream.gain);
        node.start(stream.nextTime);
        stream.nextTime += buffer.duration;
    }
}
