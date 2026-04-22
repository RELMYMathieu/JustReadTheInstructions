import {
    SNAPSHOT_REFRESH_MS,
    LOS_DELAY_MS,
    RECORDER_LOS_DELAY_MS,
    LOS_OVERLAY_HTML,
    WAITING_OVERLAY_HTML,
} from './config.js';

export class CameraSnapshot {
    constructor(snapshotBaseUrl, cardEl, { getRecorder, isLivePreviewActive }) {
        this._snapshotBaseUrl = snapshotBaseUrl;
        this._cardEl = cardEl;
        this._getRecorder = getRecorder;
        this._isLivePreviewActive = isLivePreviewActive;

        this._loading = false;
        this._timer = null;
        this._jitterTimer = null;
        this._offlineSince = 0;
        this._losSignaled = false;
    }

    start() {
        this._refresh();
        this._jitterTimer = setTimeout(() => {
            this._jitterTimer = null;
            this._timer = setInterval(() => this._refresh(), SNAPSHOT_REFRESH_MS);
        }, Math.random() * SNAPSHOT_REFRESH_MS);
    }

    stop() {
        clearTimeout(this._jitterTimer);
        this._jitterTimer = null;
        clearInterval(this._timer);
        this._timer = null;
    }

    markOnline() {
        this._offlineSince = 0;
        this._losSignaled = false;
        this._cardEl.classList.remove('offline');
        const overlay = this._cardEl.querySelector('.offline-overlay');
        if (overlay) overlay.innerHTML = WAITING_OVERLAY_HTML;
        this._getRecorder()?.handleSignalRestored();
    }

    markOffline() {
        if (!this._offlineSince) this._offlineSince = Date.now();
        this._cardEl.classList.add('offline');
        const overlay = this._cardEl.querySelector('.offline-overlay');
        if (overlay && Date.now() - this._offlineSince >= LOS_DELAY_MS) {
            overlay.innerHTML = LOS_OVERLAY_HTML;
        }
        if (!this._losSignaled && Date.now() - this._offlineSince >= RECORDER_LOS_DELAY_MS) {
            this._losSignaled = true;
            this._getRecorder()?.handleSignalLost();
        }
    }

    refresh() {
        return this._refresh();
    }

    async _refresh() {
        if (this._loading) return;
        const recorder = this._getRecorder();
        if (recorder && recorder.state !== 'idle') return;
        if (this._isLivePreviewActive()) return;

        const img = this._cardEl.querySelector('.preview img');
        if (!img) return;

        this._loading = true;
        try {
            const res = await fetch(`${this._snapshotBaseUrl}?t=${Date.now()}`);
            if (!res.ok) throw new Error();
            const blob = await res.blob();
            const prev = img.src;
            img.src = URL.createObjectURL(blob);
            if (prev.startsWith('blob:')) URL.revokeObjectURL(prev);
            this.markOnline();
        } catch {
            this.markOffline();
        } finally {
            this._loading = false;
        }
    }
}
