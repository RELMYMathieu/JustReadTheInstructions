function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function formatDuration(ms) {
    const s = Math.floor(ms / 1000);
    const mm = String(Math.floor(s / 60)).padStart(2, '0');
    const ss = String(s % 60).padStart(2, '0');
    return `${mm}:${ss}`;
}

const REC_STATES = {
    recording: {
        cardClass: true,
        statusClass: 'recording',
        recBtn: { text: '■ Stop', active: true, disabled: false },
        pauseBtn: { hidden: false, text: '⏸ Pause' },
    },
    paused: {
        cardClass: true,
        statusClass: 'paused',
        status: '⏸ Paused',
        recBtn: { text: '■ Stop', active: true, disabled: false },
        pauseBtn: { hidden: false, text: '▶ Resume' },
    },
    finalizing: {
        cardClass: false,
        status: 'Saving...',
        recBtn: { text: 'Saving...', active: false, disabled: true },
        pauseBtn: { hidden: true },
    },
    idle: {
        cardClass: false,
        status: 'Idle',
        recBtn: { text: '● Record', active: false, disabled: false },
        pauseBtn: { hidden: true },
    },
};

export class CameraRecordingUI {
    constructor(cardEl, { getRecorder, getSnapshotImg, onIdle }) {
        this._el = cardEl;
        this._getRecorder = getRecorder;
        this._getSnapshotImg = getSnapshotImg;
        this._onIdle = onIdle;

        this._durationTimer = null;
        this._lastBytes = 0;
    }

    onStateChange({ state, bytesUploaded, startedAt }) {
        const spec = REC_STATES[state] ?? REC_STATES.idle;
        const recBtn = this._el.querySelector('[data-role="record"]');
        const pauseBtn = this._el.querySelector('[data-role="pause"]');
        const statusEl = this._el.querySelector('[data-role="rec-status"]');
        const sizeEl = this._el.querySelector('[data-role="rec-size"]');

        this._el.classList.toggle('recording', spec.cardClass);
        statusEl?.classList.remove('recording', 'paused');
        if (spec.statusClass) statusEl?.classList.add(spec.statusClass);

        recBtn.textContent = spec.recBtn.text;
        recBtn.classList.toggle('active', spec.recBtn.active);
        recBtn.disabled = spec.recBtn.disabled;

        pauseBtn.hidden = spec.pauseBtn.hidden;
        if (!spec.pauseBtn.hidden) pauseBtn.textContent = spec.pauseBtn.text;

        if (state === 'recording') {
            statusEl.textContent = `● Recording ${formatDuration(Date.now() - startedAt)}`;
            this._ensureDurationTimer(startedAt);
        } else {
            statusEl.textContent = spec.status;
            this._clearDurationTimer();
        }

        if (state === 'idle') {
            this._unmountCanvas();
            this._onIdle(statusEl);
        }

        this._updateSizeDisplay(sizeEl, state, bytesUploaded);
    }

    mountCanvas(canvas) {
        canvas.className = 'rec-live-preview';
        const snapshotImg = this._getSnapshotImg();
        const preview = snapshotImg.closest('.preview');
        preview.querySelector('.offline-overlay').style.display = 'none';
        snapshotImg.hidden = true;
        preview.appendChild(canvas);
    }

    dispose() {
        this._clearDurationTimer();
    }

    _unmountCanvas() {
        const canvas = this._el.querySelector('.rec-live-preview');
        if (!canvas) return;
        canvas.remove();
        const snapshotImg = this._getSnapshotImg();
        const preview = snapshotImg.closest('.preview');
        preview.querySelector('.offline-overlay').style.display = '';
        snapshotImg.hidden = false;
    }

    _updateSizeDisplay(sizeEl, state, bytesUploaded) {
        if (!sizeEl) return;
        const active = state === 'recording' || state === 'paused' || state === 'finalizing';
        if (active) this._lastBytes = bytesUploaded;
        const bytes = active ? bytesUploaded : this._lastBytes;
        sizeEl.textContent = bytes > 0 ? `LAST RECORDING SIZE = ${formatBytes(bytes)}` : '';
    }

    _ensureDurationTimer(startedAt) {
        if (this._durationTimer) return;
        this._durationTimer = setInterval(() => {
            const recorder = this._getRecorder();
            if (!recorder?.isActive || recorder.state !== 'recording') return;
            const statusEl = this._el.querySelector('[data-role="rec-status"]');
            if (statusEl) statusEl.textContent = `● Recording ${formatDuration(Date.now() - startedAt)}`;
        }, 1000);
    }

    _clearDurationTimer() {
        if (!this._durationTimer) return;
        clearInterval(this._durationTimer);
        this._durationTimer = null;
    }
}
