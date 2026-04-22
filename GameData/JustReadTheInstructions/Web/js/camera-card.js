import {
    SNAPSHOT_REFRESH_MS,
    LOS_OVERLAY_HTML,
    WAITING_OVERLAY_HTML,
    API,
} from './config.js';
import { copyToClipboard } from './clipboard.js';
import { checkStatus } from './api.js';
import { CameraRecorder, isRecordingSupported } from './stream-recorder.js';
import { CameraSnapshot } from './camera-snapshot.js';
import { CameraRecordingUI } from './camera-recording-ui.js';

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

export class CameraCard {
    constructor(cam) {
        this.id = cam.id;
        this.name = cam.name;
        this.streamUrl = cam.streamUrl;
        this.snapshotBaseUrl = cam.snapshotUrl;
        this.streaming = cam.streaming;

        this.livenessTimer = null;
        this.recorder = null;
        this.destroyed = false;
        this._viewerCount = 0;
        this._livePreviewEl = null;

        this.el = this._buildDom();

        this._snapshot = new CameraSnapshot(this.snapshotBaseUrl, this.el, {
            getRecorder: () => this.recorder,
            isLivePreviewActive: () => !!this._livePreviewEl,
        });

        this._recordingUI = new CameraRecordingUI(this.el, {
            getRecorder: () => this.recorder,
            getSnapshotImg: () => this._getSnapshotImg(),
            onIdle: (statusEl) => {
                this._stopLivenessPolling();
                if (this._viewerCount > 0) {
                    statusEl.textContent = 'Watching';
                    this._startLivePreview();
                } else {
                    this._getSnapshotImg().src = `${this.snapshotBaseUrl}?t=${Date.now()}`;
                }
            },
        });

        this._snapshot.start();

        const initialViewerCount = cam.viewerCount ?? 0;
        if (initialViewerCount > 0) {
            this._viewerCount = initialViewerCount;
            this._onViewerCountChange();
        }
    }

    update(cam) {
        this.streaming = cam.streaming;
        this.name = cam.name;

        const watchBtn = this.el.querySelector('[data-role="watch"]');
        if (watchBtn) watchBtn.className = this.streaming ? 'btn watch' : 'btn watch-disabled';

        const nameEl = this.el.querySelector('.camera-name');
        if (nameEl) nameEl.textContent = this.name;

        const newViewerCount = cam.viewerCount ?? 0;
        if (newViewerCount !== this._viewerCount) {
            this._viewerCount = newViewerCount;
            this._onViewerCountChange();
        }
    }

    dispose() {
        this._snapshot.stop();
        this._stopLivePreview();
        this._recordingUI.dispose();
        clearInterval(this.livenessTimer);
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
        this._snapshot.start();
    }

    markDestroyed() {
        this.destroyed = true;
        this._stopLivePreview();
        this._snapshot.stop();
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
        this._stopLivePreview();

        const isLocal = location.hostname === 'localhost' || location.hostname === '127.0.0.1';

        this.recorder = new CameraRecorder({
            cameraId: this.id,
            cameraName: this.name,
            streamUrl: `/camera/${this.id}/stream`,
            isLocal,
            onStateChange: (s) => this._recordingUI.onStateChange(s),
            onCanvasReady: (canvas) => this._recordingUI.mountCanvas(canvas),
        });

        this.recorder.start();
        this._startLivenessPolling();
    }

    _startLivenessPolling() {
        if (this.livenessTimer) return;
        this.livenessTimer = setInterval(async () => {
            if (!this.recorder?.isActive) return;
            const { ok, status } = await checkStatus(this.id);
            if (status === 404 || !ok) this._snapshot.markOffline();
            else this._snapshot.markOnline();
        }, SNAPSHOT_REFRESH_MS);
    }

    _stopLivenessPolling() {
        if (!this.livenessTimer) return;
        clearInterval(this.livenessTimer);
        this.livenessTimer = null;
    }

    _onViewerCountChange() {
        const recActive = this.recorder?.isActive;
        if (this._viewerCount > 0 && !recActive) {
            this._startLivePreview();
        } else if (this._viewerCount === 0) {
            this._stopLivePreview();
        }
        if (!recActive) {
            const statusEl = this.el.querySelector('[data-role="rec-status"]');
            if (statusEl) statusEl.textContent = this._viewerCount > 0 ? 'Watching' : 'Idle';
        }
    }

    _startLivePreview() {
        if (this._livePreviewEl) return;
        const img = document.createElement('img');
        img.className = 'live-preview-feed';
        img.src = `/camera/${this.id}/preview`;
        const snapshotImg = this._getSnapshotImg();
        const preview = snapshotImg.closest('.preview');
        snapshotImg.hidden = true;
        preview.querySelector('.offline-overlay').style.display = 'none';
        preview.appendChild(img);
        this._livePreviewEl = img;
    }

    _stopLivePreview() {
        if (!this._livePreviewEl) return;
        this._livePreviewEl.src = '';
        this._livePreviewEl.remove();
        this._livePreviewEl = null;
        const snapshotImg = this._getSnapshotImg();
        snapshotImg.hidden = false;
        const preview = snapshotImg.closest('.preview');
        preview.querySelector('.offline-overlay').style.display = '';
    }
}
