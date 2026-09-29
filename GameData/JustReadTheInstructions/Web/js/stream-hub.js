import { API } from './config.js';
import { MjpegStreamReader } from './mjpeg-stream-reader.js';

export class StreamHub {
    constructor({ preview = false } = {}) {
        this._preview = preview;
        this._listeners = new Map();
        this._reader = null;
        this._streamKey = '';
        this._syncQueued = false;
    }

    subscribe(cameraId, onFrame) {
        if (!this._listeners.has(cameraId)) this._listeners.set(cameraId, new Set());
        this._listeners.get(cameraId).add(onFrame);
        this._queueSync();

        return () => {
            const listeners = this._listeners.get(cameraId);
            if (!listeners?.delete(onFrame)) return;
            if (listeners.size === 0) this._listeners.delete(cameraId);
            this._queueSync();
        };
    }

    _queueSync() {
        if (this._syncQueued) return;
        this._syncQueued = true;
        queueMicrotask(() => {
            this._syncQueued = false;
            this._sync();
        });
    }

    _sync() {
        const ids = [...this._listeners.keys()].sort((a, b) => a - b);
        const key = ids.join(',');
        if (key === this._streamKey) return;

        this._streamKey = key;
        this._reader?.stop();
        this._reader = ids.length > 0
            ? new MjpegStreamReader(API.streams(ids, this._preview), (frame, cameraId) => this._dispatch(frame, cameraId))
            : null;
        this._reader?.start();
    }

    _dispatch(frame, cameraId) {
        for (const onFrame of this._listeners.get(cameraId) ?? []) onFrame(frame);
    }
}
