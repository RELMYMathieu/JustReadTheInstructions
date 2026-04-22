export class MjpegStreamReader {
    constructor(streamUrl, onFrame) {
        this._streamUrl = streamUrl;
        this._onFrame = onFrame;
        this._fetchAbort = null;
        this._retryTimer = null;
    }

    start() {
        this._pump();
    }

    stop() {
        this._fetchAbort?.abort();
        this._fetchAbort = null;
        clearTimeout(this._retryTimer);
        this._retryTimer = null;
    }

    async _pump() {
        this._fetchAbort = new AbortController();
        try {
            const response = await fetch(`${this._streamUrl}?r=${Date.now()}`, {
                signal: this._fetchAbort.signal,
            });

            const reader = response.body.getReader();
            let buf = new Uint8Array(0);

            const flush = async () => {
                while (true) {
                    let soi = -1;
                    for (let i = 0; i < buf.length - 1; i++) {
                        if (buf[i] === 0xFF && buf[i + 1] === 0xD8) { soi = i; break; }
                    }
                    if (soi === -1) break;

                    let eoi = -1;
                    for (let i = soi + 2; i < buf.length - 1; i++) {
                        if (buf[i] === 0xFF && buf[i + 1] === 0xD9) { eoi = i; break; }
                    }
                    if (eoi === -1) break;

                    const frame = buf.slice(soi, eoi + 2);
                    buf = buf.slice(eoi + 2);

                    const shouldContinue = await this._onFrame(frame);
                    if (shouldContinue === false) break;
                }
            };

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                const next = new Uint8Array(buf.length + value.length);
                next.set(buf);
                next.set(value, buf.length);
                buf = next;
                await flush();
            }
        } catch (e) {
            if (e.name === 'AbortError') return;
            this._retryTimer = setTimeout(() => this._pump(), 1000);
        }
    }
}
