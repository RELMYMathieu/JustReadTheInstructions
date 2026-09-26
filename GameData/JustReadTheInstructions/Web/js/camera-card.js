import {
    SNAPSHOT_REFRESH_MS,
    LOS_OVERLAY_HTML,
    WAITING_OVERLAY_HTML,
    API,
} from './config.js';
import { copyToClipboard } from './clipboard.js';
import { checkStatus } from './api.js';
import { CameraRecorder, isRecordingSupported } from './stream-recorder.js';
import { GameRecorder } from './game-recorder.js';
import { usesGameRecorder } from './recorder-settings.js';
import { CameraSnapshot } from './camera-snapshot.js';
import { CameraRecordingUI } from './camera-recording-ui.js';
import { StreamHub } from './stream-hub.js';
import { FeedCanvas } from './feed-canvas.js';

const previewHub = new StreamHub({ preview: true });

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

let _overflowCloseListenerAdded = false;

function makeOverflowMenu(items) {
    if (!_overflowCloseListenerAdded) {
        _overflowCloseListenerAdded = true;
        document.addEventListener('pointerdown', (e) => {
            if (!e.target.closest('.overflow-wrapper') && !e.target.closest('.overflow-menu'))
                document.querySelectorAll('.overflow-menu--open').forEach(m => m.classList.remove('overflow-menu--open'));
        }, true);
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'overflow-wrapper';

    const btn = makeButton({ label: '···', className: 'btn btn-overflow', title: 'More options' });

    const menu = document.createElement('div');
    menu.className = 'overflow-menu';
    document.body.appendChild(menu);

    const reposition = () => {
        const r = btn.getBoundingClientRect();
        menu.style.right = `${window.innerWidth - r.right}px`;
        menu.style.bottom = `${window.innerHeight - r.top + 4}px`;
    };

    for (const { label, getText } of items) {
        const item = document.createElement('button');
        item.type = 'button';
        item.className = 'overflow-item';
        item.textContent = label;
        item.addEventListener('click', async (e) => {
            e.stopPropagation();
            const ok = await copyToClipboard(getText());
            item.textContent = ok ? '✓ Copied' : 'Manual Copy';
            setTimeout(() => { item.textContent = label; menu.classList.remove('overflow-menu--open'); }, 1200);
        });
        menu.appendChild(item);
    }

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        document.querySelectorAll('.overflow-menu--open').forEach(m => { if (m !== menu) m.classList.remove('overflow-menu--open'); });
        const isOpen = menu.classList.contains('overflow-menu--open');
        if (!isOpen) reposition();
        menu.classList.toggle('overflow-menu--open');
    });

    wrapper.appendChild(btn);
    return wrapper;
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
        this._livePreview = null;
        this._startingRecording = false;

        this.el = this._buildDom();

        this._snapshot = new CameraSnapshot(this.snapshotBaseUrl, this.el, {
            getRecorder: () => this.recorder,
            isLivePreviewActive: () => !!this._livePreview,
        });

        this._recordingUI = new CameraRecordingUI(this.el, {
            getRecorder: () => this.recorder,
            getSnapshotImg: () => this._getSnapshotImg(),
            onIdle: (statusEl) => {
                this._stopLivenessPolling();
                if (this._viewerCount > 0) statusEl.textContent = 'Watching';
                this._updateLivePreview();
                this._snapshot.refresh();
            },
        });

        this._snapshot.start();

        const initialViewerCount = cam.viewerCount ?? 0;
        if (initialViewerCount > 0) {
            this._viewerCount = initialViewerCount;
            this._onViewerCountChange();
        }
        this._syncGameRecording(cam.recording);
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
        this._syncGameRecording(cam.recording);
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
        if (this.recorder?.inGame) this.recorder.sync(null);
        this._stopLivePreview();
        this._snapshot.stop();
        this.el.classList.add('offline', 'destroyed');
        const overlay = this.el.querySelector('.offline-overlay');
        if (overlay) overlay.innerHTML = LOS_OVERLAY_HTML;
        const watchBtn = this.el.querySelector('[data-role="watch"]');
        if (watchBtn) watchBtn.className = 'btn watch';
    }

    _buildDom() {
        const card = document.createElement('div');
        card.className = 'camera-card offline';
        card.dataset.id = this.id;

        this.groupStripEl = document.createElement('div');
        this.groupStripEl.className = 'group-strip';
        this.groupStripEl.title = 'Click to assign recording group (cycles G1 → G2 → G3 → G4 → none)';

        card.append(this.groupStripEl, this._buildPreview(), this._buildInfo(), this._buildFooter());
        return card;
    }

    setGroupStrip(groupId, color) {
        this.groupStripEl.style.background = color ?? '';
        this.groupStripEl.classList.toggle('group-strip--assigned', groupId !== null);

        const label = groupId !== null ? `G${groupId + 1}` : 'Grp';
        this.groupBtnEl.textContent = label;
        this.groupBtnEl.style.borderColor = color ?? '';
        this.groupBtnEl.style.color = color ?? '';
    }

    startRecording() {
        return this._startRecording();
    }

    _buildPreview() {
        const preview = document.createElement('div');
        preview.className = 'preview';

        const img = document.createElement('img');
        img.alt = this.name;
        img.crossOrigin = 'anonymous';
        img.draggable = false;

        const offlineOverlay = document.createElement('div');
        offlineOverlay.className = 'offline-overlay';
        offlineOverlay.innerHTML = WAITING_OVERLAY_HTML;

        const recBadge = document.createElement('div');
        recBadge.className = 'rec-badge';
        recBadge.innerHTML = '<span class="rec-dot"></span><span data-role="rec-label">REC</span>';

        const dragHandle = document.createElement('div');
        dragHandle.className = 'drag-handle';
        dragHandle.title = 'Drag to reorder';
        dragHandle.innerHTML = '<i class="fa-solid fa-hand"></i>';

        preview.append(img, offlineOverlay, recBadge, dragHandle);
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

        this.groupBtnEl = makeButton({ label: 'Grp', className: 'btn group-assign-btn', title: 'Click to assign a recording group (G1–G4). Cameras in the same group can be started/stopped together.' });

        const moreMenu = makeOverflowMenu([
            { label: 'Copy Viewer URL', getText: () => location.origin + this.streamUrl },
            { label: 'Copy Stream URL', getText: () => location.origin + API.stream(this.id) },
        ]);

        actions.append(watchBtn, recBtn, pauseBtn, this.groupBtnEl, moreMenu);
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

    async _startRecording() {
        if (this._startingRecording) return;
        this._startingRecording = true;
        try {
            if (this.recorder && this.recorder.state !== 'idle') {
                const old = this.recorder;
                this.recorder = null;
                old.abandon();
            }
            if (usesGameRecorder() && await this._tryStartGameRecording()) return;
            this._startBrowserRecording();
        } finally {
            this._startingRecording = false;
        }
    }

    _createGameRecorder() {
        return new GameRecorder({
            cameraId: this.id,
            onStateChange: (s) => this._recordingUI.onStateChange(s),
        });
    }

    async _tryStartGameRecording() {
        const recorder = this._createGameRecorder();
        try {
            await recorder.start();
        } catch (err) {
            console.warn('[JRTI] in-game recording unavailable, recording in the browser instead', err);
            return false;
        }
        this.recorder = recorder;
        this._updateLivePreview();
        this._startLivenessPolling();
        return true;
    }

    _syncGameRecording(info) {
        if (this.recorder?.isActive) {
            if (this.recorder.inGame) this.recorder.sync(info);
            return;
        }
        if (!info || this.recorder?.state === 'finalizing') return;
        if (this.recorder?.inGame && this.recorder.filename === info.file) return;
        this.recorder = this._createGameRecorder();
        this.recorder.adopt(info);
        this._updateLivePreview();
        this._startLivenessPolling();
    }

    _startBrowserRecording() {
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
        this._updateLivePreview();
        if (!recActive) {
            const statusEl = this.el.querySelector('[data-role="rec-status"]');
            if (statusEl) statusEl.textContent = this._viewerCount > 0 ? 'Watching' : 'Idle';
        }
    }

    _updateLivePreview() {
        const active = this.recorder?.isActive;
        const browserRecording = active && !this.recorder.inGame;
        if (!browserRecording && (this._viewerCount > 0 || active)) this._startLivePreview();
        else this._stopLivePreview();
    }

    _startLivePreview() {
        if (this._livePreview) return;
        const feed = new FeedCanvas('live-preview-feed');
        const snapshotImg = this._getSnapshotImg();
        const preview = snapshotImg.closest('.preview');
        snapshotImg.hidden = true;
        preview.querySelector('.offline-overlay').style.display = 'none';
        preview.appendChild(feed.el);
        this._livePreview = {
            el: feed.el,
            unsubscribe: previewHub.subscribe(this.id, (frame) => feed.push(frame)),
        };
    }

    _stopLivePreview() {
        if (!this._livePreview) return;
        this._livePreview.unsubscribe();
        this._livePreview.el.remove();
        this._livePreview = null;
        const snapshotImg = this._getSnapshotImg();
        snapshotImg.hidden = false;
        const preview = snapshotImg.closest('.preview');
        preview.querySelector('.offline-overlay').style.display = '';
        this._snapshot.refresh();
    }
}
