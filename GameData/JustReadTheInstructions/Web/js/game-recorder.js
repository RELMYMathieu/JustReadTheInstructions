import { gameRecording } from './api.js';
import { selectedGameCodec } from './recorder-settings.js';

export class GameRecorder {
    constructor({ cameraId, onStateChange }) {
        this.cameraId = cameraId;
        this.onStateChange = onStateChange || (() => { });
        this.inGame = true;

        this.state = 'idle';
        this.bytesUploaded = 0;
        this.startedAt = null;
        this._filename = null;
    }

    get isActive() {
        return this.state === 'recording' || this.state === 'paused';
    }

    get filename() {
        return this._filename;
    }

    async start() {
        this.adopt(await gameRecording(this.cameraId, 'start', selectedGameCodec()));
    }

    adopt(info) {
        this.startedAt = Date.now() - (info.elapsedMs ?? 0);
        this._filename = info.file;
        this.bytesUploaded = info.bytes ?? 0;
        this._setState(info.paused ? 'paused' : 'recording');
    }

    sync(info) {
        if (!this.isActive) return;
        if (!info) {
            this._setState('idle');
            return;
        }
        if (info.error) console.error('[JRTI] in-game recording error', info.error);
        this.bytesUploaded = info.bytes;
        this._setState(info.paused ? 'paused' : 'recording');
    }

    async stop() {
        if (!this.isActive) return;
        this._setState('finalizing');
        try { await gameRecording(this.cameraId, 'stop'); } catch { }
        this._setState('idle');
    }

    async pause() {
        if (this.state !== 'recording') return;
        await this._send('pause');
    }

    async resume() {
        if (this.state !== 'paused') return;
        await this._send('resume');
    }

    handleSignalLost() { }

    handleSignalRestored() { }

    emergencyFinalize() { }

    abandon() {
        this.onStateChange = () => { };
        if (this.isActive) this.stop();
    }

    async _send(action) {
        try {
            this.sync(await gameRecording(this.cameraId, action));
        } catch (err) {
            console.error(`[JRTI] in-game recording ${action} failed`, err);
        }
    }

    _setState(state) {
        this.state = state;
        this.onStateChange({
            state: this.state,
            bytesUploaded: this.bytesUploaded,
            startedAt: this.startedAt,
            filename: this._filename,
        });
    }
}
