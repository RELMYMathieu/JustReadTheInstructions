import {
    SNAPSHOT_REFRESH_MS,
    LOS_DELAY_MS,
    LOS_OVERLAY_HTML,
    WAITING_OVERLAY_HTML,
} from './config.js';

const snapshots = new Set();

document.addEventListener('visibilitychange', () => {
    if (document.hidden) return;
    for (const snapshot of snapshots) snapshot.refresh();
});

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
        this._online = false;
    }

    start() {
        snapshots.add(this);
        this._refresh();
        this._jitterTimer = setTimeout(() => {
            this._jitterTimer = null;
            this._timer = setInterval(() => this._refresh(), SNAPSHOT_REFRESH_MS);
        }, Math.random() * SNAPSHOT_REFRESH_MS);
    }

    stop() {
        snapshots.delete(this);
        clearTimeout(this._jitterTimer);
        this._jitterTimer = null;
        clearInterval(this._timer);
        this._timer = null;
    }

    markOnline() {
        this._offlineSince = 0;
        if (this._online) return;
        this._online = true;
        this._cardEl.classList.remove('offline');
        this._setOverlay(WAITING_OVERLAY_HTML);
    }

    markOffline() {
        this._online = false;
        if (!this._offlineSince) this._offlineSince = Date.now();
        this._cardEl.classList.add('offline');
        if (Date.now() - this._offlineSince >= LOS_DELAY_MS) this._setOverlay(LOS_OVERLAY_HTML);
    }

    showLost() {
        this._online = false;
        this._cardEl.classList.add('offline');
        this._setOverlay(LOS_OVERLAY_HTML);
    }

    refresh() {
        return this._refresh();
    }

    _setOverlay(html) {
        const overlay = this._cardEl.querySelector('.offline-overlay');
        if (overlay && overlay.dataset.html !== html) {
            overlay.innerHTML = html;
            overlay.dataset.html = html;
        }
    }

    async _refresh() {
        if (this._loading || document.hidden) return;
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
