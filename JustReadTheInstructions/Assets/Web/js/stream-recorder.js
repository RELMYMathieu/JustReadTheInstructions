import {
    RECORDER_CHUNK_MS,
    RECORDER_CAPTURE_FPS,
    RECORDER_VIDEO_BPS,
    RECORDER_HEARTBEAT_MS,
    LOS_BEHAVIORS,
} from './config.js';
import {
    uploadRecordingChunk,
    finalizeRecording,
    finalizeRecordingBeacon,
    heartbeatRecording,
    abortRecording,
} from './api.js';
import { getSettings } from './recorder-settings.js';

const MIME_CANDIDATES = [
    'video/mp4;codecs=avc1',
    'video/mp4',
    'video/x-matroska;codecs=avc1',
    'video/webm;codecs=vp9',
    'video/webm;codecs=vp8',
    'video/webm',
];

function pickMimeType() {
    if (typeof MediaRecorder === 'undefined') return null;
    for (const mime of MIME_CANDIDATES) {
        if (MediaRecorder.isTypeSupported(mime)) return mime;
    }
    return null;
}

function sanitizeForFilename(raw) {
    return (raw || 'camera')
        .replace(/[\\/:*?"<>|\s]+/g, '_')
        .replace(/_+/g, '_')
        .replace(/^_+|_+$/g, '')
        .slice(0, 80) || 'camera';
}

function timestampForFilename() {
    const d = new Date();
    const pad = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` +
        `_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

function getExtensionForMime(mime) {
    if (!mime) return 'webm';
    const lower = mime.toLowerCase();
    if (lower.includes('mp4')) return 'mp4';
    if (lower.includes('matroska')) return 'mkv';
    return 'webm';
}

function buildFilename(cameraName, cameraId, mimeType) {
    const safe = sanitizeForFilename(cameraName);
    const ext = getExtensionForMime(mimeType);
    return `${safe}__cam${cameraId}__${timestampForFilename()}.${ext}`;
}

function buildSessionId(cameraId) {
    const rand = Math.random().toString(36).slice(2, 10);
    return `cam${cameraId}-${Date.now().toString(36)}-${rand}`;
}

export function isRecordingSupported() {
    return pickMimeType() !== null && typeof HTMLCanvasElement.prototype.captureStream === 'function';
}

export class CameraRecorder {
    constructor({ cameraId, cameraName, streamUrl, onStateChange }) {
        this.cameraId = cameraId;
        this.cameraName = cameraName;
        this.streamUrl = streamUrl;
        this.onStateChange = onStateChange || (() => { });

        this.state = 'idle';
        this.bytesUploaded = 0;
        this.startedAt = null;
        this.pausedAt = null;

        this._mimeType = null;
        this._mediaRecorder = null;
        this._canvas = null;
        this._ctx = null;
        this._rafId = null;
        this._streamImg = null;
        this._streamRetryTimer = null;
        this._stream = null;
        this._sessionId = null;
        this._filename = null;
        this._pendingUploads = Promise.resolve();
        this._finalizePromise = null;
        this._aborted = false;
        this._heartbeatTimer = null;
    }

    get isActive() {
        return this.state === 'recording' || this.state === 'paused';
    }

    async start() {
        if (this.isActive) return;

        this._mimeType = pickMimeType();
        if (!this._mimeType) {
            this._setState('idle', { error: 'MediaRecorder not supported' });
            return;
        }

        this._sessionId = buildSessionId(this.cameraId);
        this._filename = buildFilename(this.cameraName, this.cameraId, this._mimeType);
        this.bytesUploaded = 0;
        this._aborted = false;
        this.startedAt = Date.now();
        this._setState('recording');
        this._startCanvasPump();
    }

    pause() {
        if (this.state !== 'recording' || !this._mediaRecorder) return;
        try {
            this._mediaRecorder.pause();
        } catch {
            return;
        }
        this.pausedAt = Date.now();
        this._setState('paused');
        this._startHeartbeat();
    }

    resume() {
        if (this.state !== 'paused' || !this._mediaRecorder) return;
        try {
            this._mediaRecorder.resume();
        } catch {
            return;
        }
        this.pausedAt = null;
        this._stopHeartbeat();
        this._setState('recording');
    }

    async stop() {
        if (!this.isActive) return;
        await this._finalize();
    }

    handleSignalLost() {
        if (!this.isActive) return;
        const { losBehavior } = getSettings();

        if (losBehavior === LOS_BEHAVIORS.PAUSE) {
            this.pause();
        } else if (losBehavior === LOS_BEHAVIORS.DISCARD) {
            this._discard();
        } else {
            this.stop();
        }
    }

    handleSignalRestored() {
        if (this.state === 'paused') this.resume();
    }

    _startCanvasPump() {
        const startRecorder = () => {
            if (this._canvas || !this._streamImg) return;

            const width = this._streamImg.naturalWidth || 1280;
            const height = this._streamImg.naturalHeight || 720;
            this._canvas = document.createElement('canvas');
            this._canvas.width = width;
            this._canvas.height = height;
            this._ctx = this._canvas.getContext('2d');

            this._stream = this._canvas.captureStream(RECORDER_CAPTURE_FPS);
            this._mediaRecorder = new MediaRecorder(this._stream, {
                mimeType: this._mimeType,
                videoBitsPerSecond: RECORDER_VIDEO_BPS,
            });

            this._mediaRecorder.addEventListener('dataavailable', (e) => this._onChunk(e));
            this._mediaRecorder.addEventListener('error', () => this._emergencyStop());
            this._mediaRecorder.start(RECORDER_CHUNK_MS);
        };

        const drawLoop = () => {
            if (this.state === 'idle' || this.state === 'finalizing') {
                this._rafId = null;
                return;
            }

            if (this.state === 'recording' && this._ctx && this._streamImg) {
                try {
                    this._ctx.drawImage(this._streamImg, 0, 0, this._canvas.width, this._canvas.height);
                } catch { }
            }

            this._rafId = requestAnimationFrame(drawLoop);
        };

        const reconnect = () => {
            if (this.state === 'idle' || this.state === 'finalizing' || !this._streamImg) return;
            this._streamImg.src = `${this.streamUrl}?r=${Date.now()}`;
        };

        this._streamImg = new Image();
        this._streamImg.crossOrigin = 'anonymous';

        this._streamImg.onload = () => {
            if (this.state === 'idle' || this.state === 'finalizing') return;
            startRecorder();
            if (this._rafId === null) {
                this._rafId = requestAnimationFrame(drawLoop);
            }
        };

        this._streamImg.onerror = () => {
            if (this._streamRetryTimer !== null) clearTimeout(this._streamRetryTimer);
            this._streamRetryTimer = setTimeout(reconnect, 1000);
        };

        reconnect();
    }

    _stopCanvasPump() {
        if (this._rafId !== null) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
        if (this._streamRetryTimer !== null) {
            clearTimeout(this._streamRetryTimer);
            this._streamRetryTimer = null;
        }
        if (this._streamImg) {
            this._streamImg.onload = null;
            this._streamImg.onerror = null;
            this._streamImg.src = '';
            this._streamImg = null;
        }
    }

    _onChunk(event) {
        const blob = event.data;
        if (!blob || blob.size === 0) return;
        if (this._aborted) return;

        const sessionId = this._sessionId;
        const filename = this._filename;

        this._pendingUploads = this._pendingUploads.then(async () => {
            if (this._aborted) return;
            try {
                await uploadRecordingChunk(sessionId, filename, blob);
                this.bytesUploaded += blob.size;
                this._notify();
            } catch (err) {
                console.error('[JRTI] chunk upload failed', err);
            }
        });
    }

    async _finalize() {
        if (this.state === 'finalizing' || this.state === 'idle') return this._finalizePromise;
        this._setState('finalizing');

        const sessionId = this._sessionId;
        const filename = this._filename;

        this._finalizePromise = (async () => {
            try {
                if (this._mediaRecorder && this._mediaRecorder.state !== 'inactive') {
                    await new Promise((resolve) => {
                        this._mediaRecorder.addEventListener('stop', resolve, { once: true });
                        try { this._mediaRecorder.stop(); } catch { resolve(); }
                    });
                }

                await this._pendingUploads;

                try {
                    await finalizeRecording(sessionId, filename);
                } catch (err) {
                    console.error('[JRTI] finalize failed', err);
                }
            } finally {
                this._cleanup();
                this._setState('idle');
            }
        })();

        return this._finalizePromise;
    }

    _discard() {
        const sessionId = this._sessionId;
        const filename = this._filename;
        this._aborted = true;
        this._setState('finalizing');

        try {
            if (this._mediaRecorder && this._mediaRecorder.state !== 'inactive') {
                this._mediaRecorder.stop();
            }
        } catch { }

        abortRecording(sessionId, filename);

        this._cleanup();
        this._setState('idle');
    }

    _emergencyStop() {
        if (!this.isActive) return;
        this.stop();
    }

    emergencyFinalize() {
        if (this.state === 'idle' || !this._sessionId) return;
        this._aborted = true;
        this._stopHeartbeat();
        try {
            if (this._mediaRecorder && this._mediaRecorder.state !== 'inactive') {
                this._mediaRecorder.stop();
            }
        } catch { }
        finalizeRecordingBeacon(this._sessionId, this._filename);
    }

    abandon() {
        this.onStateChange = () => { };
        if (this.isActive) this.stop();
    }

    _startHeartbeat() {
        if (this._heartbeatTimer) return;
        const sessionId = this._sessionId;
        const filename = this._filename;
        this._heartbeatTimer = setInterval(() => {
            if (this.state !== 'paused' || this._aborted) return;
            heartbeatRecording(sessionId, filename);
        }, RECORDER_HEARTBEAT_MS);
    }

    _stopHeartbeat() {
        if (!this._heartbeatTimer) return;
        clearInterval(this._heartbeatTimer);
        this._heartbeatTimer = null;
    }

    _cleanup() {
        this._stopCanvasPump();
        this._stopHeartbeat();

        if (this._stream) {
            for (const track of this._stream.getTracks()) {
                try { track.stop(); } catch { }
            }
            this._stream = null;
        }

        this._mediaRecorder = null;
        this._canvas = null;
        this._ctx = null;
    }

    _setState(state, extras = {}) {
        this.state = state;
        this._notify(extras);
    }

    _notify(extras = {}) {
        this.onStateChange({
            state: this.state,
            bytesUploaded: this.bytesUploaded,
            startedAt: this.startedAt,
            filename: this._filename,
            ...extras,
        });
    }
}