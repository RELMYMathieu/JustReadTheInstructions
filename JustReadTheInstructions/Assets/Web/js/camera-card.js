import {
    SNAPSHOT_REFRESH_MS,
    LOS_DELAY_MS,
    RECORDER_LOS_DELAY_MS,
    LOS_OVERLAY_HTML,
    WAITING_OVERLAY_HTML,
    API,
} from './config.js';
import { copyToClipboard } from './clipboard.js';
import { checkStatus } from './api.js';
import { CameraRecorder, isRecordingSupported } from './stream-recorder.js';

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

function makeButton({ label, className = 'btn', role, title, onClick }) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = className;
    btn.textContent = label;
    if (role) btn.dataset.role = role;
    if (title) btn.title = title;
    if (onClick) btn.addEventListener('click', onClick);
    return btn;
}

function makeCopyButton({ label, title, getText }) {
    const btn = makeButton({ label, title });
    btn.addEventListener('click', async () => {
        const ok = await copyToClipboard(getText());
        btn.textContent = ok ? 'Copied!' : 'Manual Copy';
        setTimeout(() => { btn.textContent = label; }, 1500);
    });
    return btn;
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

export class CameraCard {
    constructor(cam) {
        this.id = cam.id;
        this.name = cam.name;
        this.streamUrl = cam.streamUrl;
        this.snapshotBaseUrl = cam.snapshotUrl;
        this.streaming = cam.streaming;

        this.offlineSince = 0;
        this.loadingSnapshot = false;
        this.snapshotTimer = null;
        this.durationTimer = null;
        this.livenessTimer = null;
        this.recorder = null;
        this.destroyed = false;
        this._snapshotJitterTimer = null;
        this._lastRecordingBytes = 0;

        this.el = this._buildDom();
        this._startSnapshotLoop();
    }

    update(cam) {
        this.streaming = cam.streaming;
        this.name = cam.name;

        const watchBtn = this.el.querySelector('[data-role="watch"]');
        if (watchBtn) watchBtn.className = this.streaming ? 'btn watch' : 'btn watch-disabled';

        const nameEl = this.el.querySelector('.camera-name');
        if (nameEl) nameEl.textContent = this.name;
    }

    dispose() {
        this._stopSnapshotLoop();
        clearInterval(this.durationTimer);
        clearInterval(this.livenessTimer);
        this.durationTimer = null;
        this.livenessTimer = null;
        const img = this._getSnapshotImg();
        if (img?.src.startsWith('blob:')) URL.revokeObjectURL(img.src);
        this.recorder?.stop();
        this.el.remove();
    }

    emergencyFinalize() {
        this.recorder?.emergencyFinalize();
    }

    revive(cam) {
        this.destroyed = false;
        this.el.classList.remove('destroyed');
        this.update(cam);
        this._startSnapshotLoop();
    }

    markDestroyed() {
        this.destroyed = true;
        this._stopSnapshotLoop();
        this.el.classList.add('offline', 'destroyed');
        const overlay = this.el.querySelector('.offline-overlay');
        if (overlay) overlay.innerHTML = LOS_OVERLAY_HTML;
        const watchBtn = this.el.querySelector('[data-role="watch"]');
        if (watchBtn) watchBtn.className = 'btn watch-disabled';
    }

    _buildDom() {
        const card = document.createElement('div');
        card.className = 'camera-card offline';
        card.dataset.id = this.id;
        card.append(this._buildPreview(), this._buildInfo(), this._buildFooter());
        return card;
    }

    _buildPreview() {
        const preview = document.createElement('div');
        preview.className = 'preview';

        const img = document.createElement('img');
        img.alt = this.name;
        img.crossOrigin = 'anonymous';

        const offlineOverlay = document.createElement('div');
        offlineOverlay.className = 'offline-overlay';
        offlineOverlay.innerHTML = WAITING_OVERLAY_HTML;

        const recBadge = document.createElement('div');
        recBadge.className = 'rec-badge';
        recBadge.innerHTML = '<span class="rec-dot"></span><span data-role="rec-label">REC</span>';

        preview.append(img, offlineOverlay, recBadge);
        return preview;
    }

    _buildInfo() {
        const info = document.createElement('div');
        info.className = 'camera-info';

        const name = document.createElement('span');
        name.className = 'camera-name';
        name.textContent = this.name;

        const actions = document.createElement('div');
        actions.className = 'camera-actions';

        const watchBtn = document.createElement('a');
        watchBtn.className = this.streaming ? 'btn watch' : 'btn watch-disabled';
        watchBtn.href = this.streamUrl;
        watchBtn.target = '_blank';
        watchBtn.textContent = 'Watch';
        watchBtn.dataset.role = 'watch';

        const recBtn = makeButton({
            label: '● Record',
            className: 'btn rec',
            role: 'record',
            onClick: () => this._toggleRecording(),
        });
        if (!isRecordingSupported()) {
            recBtn.disabled = true;
            recBtn.classList.add('btn-unsupported');
            recBtn.title = 'Recording not supported in this browser';
        }

        const pauseBtn = makeButton({
            label: '⏸ Pause',
            role: 'pause',
            onClick: () => this._togglePause(),
        });
        pauseBtn.hidden = true;

        const copyBtn = makeCopyButton({
            label: 'Copy URL',
            getText: () => location.origin + this.streamUrl,
        });

        const rawCopyBtn = makeCopyButton({
            label: 'Copy Raw',
            title: 'For OBS and other external feeds',
            getText: () => location.origin + API.stream(this.id),
        });

        actions.append(watchBtn, recBtn, pauseBtn, copyBtn, rawCopyBtn);
        info.append(name, actions);
        return info;
    }

    _buildFooter() {
        const footer = document.createElement('div');
        footer.className = 'camera-footer';
        footer.innerHTML = '<span class="rec-status" data-role="rec-status">Idle</span>' +
            '<span class="rec-size" data-role="rec-size"></span>';
        return footer;
    }

    _getSnapshotImg() {
        return this.el.querySelector('.preview img');
    }

    _startSnapshotLoop() {
        this._refreshSnapshot();
        this._snapshotJitterTimer = setTimeout(() => {
            this._snapshotJitterTimer = null;
            this.snapshotTimer = setInterval(() => this._refreshSnapshot(), SNAPSHOT_REFRESH_MS);
        }, Math.random() * SNAPSHOT_REFRESH_MS);
    }

    _stopSnapshotLoop() {
        clearTimeout(this._snapshotJitterTimer);
        this._snapshotJitterTimer = null;
        clearInterval(this.snapshotTimer);
        this.snapshotTimer = null;
    }

    async _refreshSnapshot() {
        if (this.loadingSnapshot) return;
        if (this.recorder && this.recorder.state !== 'idle') return;

        const img = this._getSnapshotImg();
        if (!img) return;

        this.loadingSnapshot = true;
        try {
            const res = await fetch(`${this.snapshotBaseUrl}?t=${Date.now()}`);
            if (!res.ok) throw new Error();
            const blob = await res.blob();
            const prev = img.src;
            img.src = URL.createObjectURL(blob);
            if (prev.startsWith('blob:')) URL.revokeObjectURL(prev);
            this._markOnline();
        } catch {
            this._markOffline();
        } finally {
            this.loadingSnapshot = false;
        }
    }

    _markOnline() {
        this.el.classList.remove('offline');
        this.offlineSince = 0;
        this._losSignaled = false;
        const overlay = this.el.querySelector('.offline-overlay');
        if (overlay) overlay.innerHTML = WAITING_OVERLAY_HTML;
        this.recorder?.handleSignalRestored();
    }

    _markOffline() {
        if (!this.offlineSince) this.offlineSince = Date.now();
        this.el.classList.add('offline');
        const overlay = this.el.querySelector('.offline-overlay');
        if (overlay && Date.now() - this.offlineSince >= LOS_DELAY_MS) {
            overlay.innerHTML = LOS_OVERLAY_HTML;
        }
        if (!this._losSignaled && Date.now() - this.offlineSince >= RECORDER_LOS_DELAY_MS) {
            this._losSignaled = true;
            this.recorder?.handleSignalLost();
        }
    }

    _togglePause() {
        if (this.recorder?.state === 'recording') this.recorder.pause();
        else if (this.recorder?.state === 'paused') this.recorder.resume();
    }

    _toggleRecording() {
        if (this.recorder?.isActive) {
            this.recorder.stop();
            return;
        }
        this._startRecording();
    }

    _startRecording() {
        if (this.recorder && this.recorder.state !== 'idle') {
            const old = this.recorder;
            this.recorder = null;
            old.abandon();
        }

        const isLocal = location.hostname === 'localhost' || location.hostname === '127.0.0.1';

        this.recorder = new CameraRecorder({
            cameraId: this.id,
            cameraName: this.name,
            streamUrl: `/camera/${this.id}/stream`,
            isLocal,
            onStateChange: (s) => this._onRecorderState(s),
            onCanvasReady: (canvas) => this._mountRecorderCanvas(canvas),
        });

        this.recorder.start();
        this._startLivenessPolling();
    }

    _startLivenessPolling() {
        if (this.livenessTimer) return;
        this.livenessTimer = setInterval(async () => {
            if (!this.recorder?.isActive) return;
            const { ok, status } = await checkStatus(this.id);
            if (status === 404 || !ok) this._markOffline();
            else this._markOnline();
        }, SNAPSHOT_REFRESH_MS);
    }

    _stopLivenessPolling() {
        if (!this.livenessTimer) return;
        clearInterval(this.livenessTimer);
        this.livenessTimer = null;
    }

    _onRecorderState({ state, bytesUploaded, startedAt }) {
        const spec = REC_STATES[state] ?? REC_STATES.idle;
        const card = this.el;
        const recBtn = card.querySelector('[data-role="record"]');
        const pauseBtn = card.querySelector('[data-role="pause"]');
        const statusEl = card.querySelector('[data-role="rec-status"]');
        const sizeEl = card.querySelector('[data-role="rec-size"]');

        card.classList.toggle('recording', spec.cardClass);
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
            this._stopLivenessPolling();
            this._unmountRecorderCanvas();
            this._getSnapshotImg().src = `${this.snapshotBaseUrl}?t=${Date.now()}`;
        }

        this._updateSizeDisplay(sizeEl, state, bytesUploaded);
    }

    _updateSizeDisplay(sizeEl, state, bytesUploaded) {
        if (!sizeEl) return;

        const active = state === 'recording' || state === 'paused' || state === 'finalizing';
        if (active) this._lastRecordingBytes = bytesUploaded;

        const bytes = active ? bytesUploaded : this._lastRecordingBytes;
        sizeEl.textContent = bytes > 0 ? `LAST RECORDING SIZE = ${formatBytes(bytes)}` : '';
    }

    _ensureDurationTimer(startedAt) {
        if (this.durationTimer) return;
        const statusEl = this.el.querySelector('[data-role="rec-status"]');
        this.durationTimer = setInterval(() => {
            if (!this.recorder?.isActive) return;
            if (this.recorder.state !== 'recording') return;
            statusEl.textContent = `● Recording ${formatDuration(Date.now() - startedAt)}`;
        }, 1000);
    }

    _mountRecorderCanvas(canvas) {
        canvas.className = 'rec-live-preview';
        const preview = this._getSnapshotImg().closest('.preview');
        preview.querySelector('.offline-overlay').style.display = 'none';
        this._getSnapshotImg().hidden = true;
        preview.appendChild(canvas);
    }

    _unmountRecorderCanvas() {
        const canvas = this.el.querySelector('.rec-live-preview');
        if (!canvas) return;
        canvas.remove();
        const preview = this._getSnapshotImg().closest('.preview');
        preview.querySelector('.offline-overlay').style.display = '';
        this._getSnapshotImg().hidden = false;
    }

    _clearDurationTimer() {
        if (!this.durationTimer) return;
        clearInterval(this.durationTimer);
        this.durationTimer = null;
    }
}