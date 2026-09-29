import { STREAM_RETRY_MIN_MS, STREAM_RETRY_MAX_MS, STREAM_STALL_MS } from './config.js';

const headerDecoder = new TextDecoder();

function withCacheBuster(url) {
    return `${url}${url.includes('?') ? '&' : '?'}r=${Date.now()}`;
}

function concat(a, b) {
    const joined = new Uint8Array(a.length + b.length);
    joined.set(a);
    joined.set(b, a.length);
    return joined;
}

function indexOfHeaderEnd(buf) {
    for (let i = 0; i + 3 < buf.length; i++) {
        if (buf[i] === 13 && buf[i + 1] === 10 && buf[i + 2] === 13 && buf[i + 3] === 10) return i;
    }
    return -1;
}

function parsePartHeaders(bytes) {
    const headers = new Map();
    for (const line of headerDecoder.decode(bytes).split('\r\n')) {
        const colon = line.indexOf(':');
        if (colon > 0) headers.set(line.slice(0, colon).trim().toLowerCase(), line.slice(colon + 1).trim());
    }
    return {
        length: Number(headers.get('content-length')),
        cameraId: headers.has('x-camera-id') ? Number(headers.get('x-camera-id')) : null,
    };
}

export class MjpegStreamReader {
    constructor(streamUrl, onFrame) {
        this._streamUrl = streamUrl;
        this._onFrame = onFrame;
        this._fetchAbort = null;
        this._retryTimer = null;
        this._stallTimer = null;
        this._retryMs = STREAM_RETRY_MIN_MS;
        this._stopped = false;
    }

    start() {
        this._stopped = false;
        this._pump();
    }

    stop() {
        this._stopped = true;
        this._fetchAbort?.abort();
        this._fetchAbort = null;
        clearTimeout(this._retryTimer);
        clearTimeout(this._stallTimer);
        this._retryTimer = null;
    }

    async _pump() {
        const controller = new AbortController();
        this._fetchAbort = controller;
        try {
            this._armStallWatchdog(controller);
            const response = await fetch(withCacheBuster(this._streamUrl), { signal: controller.signal, cache: 'no-store' });
            if (!response.ok) throw new Error(`stream request failed: ${response.status}`);
            await this._readParts(response.body.getReader(), controller);
        } catch { }
        clearTimeout(this._stallTimer);
        if (this._stopped || this._fetchAbort !== controller) return;
        this._retryTimer = setTimeout(() => this._pump(), this._retryMs);
        this._retryMs = Math.min(this._retryMs * 2, STREAM_RETRY_MAX_MS);
    }

    _armStallWatchdog(controller) {
        clearTimeout(this._stallTimer);
        this._stallTimer = setTimeout(() => controller.abort(), STREAM_STALL_MS);
    }

    async _readParts(reader, controller) {
        let buf = new Uint8Array(0);
        let part = null;

        while (true) {
            const { done, value } = await reader.read();
            if (done) return;
            this._armStallWatchdog(controller);
            buf = concat(buf, value);

            while (true) {
                if (!part) {
                    const headerEnd = indexOfHeaderEnd(buf);
                    if (headerEnd === -1) break;
                    part = parsePartHeaders(buf.subarray(0, headerEnd));
                    buf = buf.subarray(headerEnd + 4);
                }
                if (buf.length < part.length) break;

                const frame = buf.slice(0, part.length);
                const { cameraId } = part;
                buf = buf.subarray(part.length);
                part = null;
                this._retryMs = STREAM_RETRY_MIN_MS;

                const shouldContinue = await this._onFrame(frame, cameraId);
                if (shouldContinue === false) break;
            }
        }
    }
}
