import { SNAPSHOT_REFRESH_MS, WAITING_OVERLAY_HTML, API, MICS } from './config.js';
import { checkStatus, setCameraSettings } from './api.js';
import { CameraRecorder, isRecordingSupported } from './stream-recorder.js';
import { GameRecorder } from './game-recorder.js';
import { usesGameRecorder, isInGameRecordingAvailable } from './recorder-settings.js';
import { CameraSnapshot } from './camera-snapshot.js';
import { CameraRecordingUI } from './camera-recording-ui.js';
import { StreamHub } from './stream-hub.js';
import { FeedCanvas } from './feed-canvas.js';
import { h, icon, button } from './dom.js';
import { Menu, menuItem, menuNote, menuSeparator, copyWithToast, toast } from './ui.js';
import { getSession, lanUrl } from './session.js';
import { showRecordings } from './recordings-ui.js';

const previewHub = new StreamHub({ preview: true });
const MIC_SETTLE_MS = 1000;

export class CameraCard {
    constructor(cam) {
        this.id = cam.id;
        this.name = cam.name;
        this.streamUrl = cam.streamUrl;
        this.snapshotBaseUrl = cam.snapshotUrl;
        this.streaming = cam.streaming;
        this.groupItems = null;
        this.onRecordingChange = null;
        this.onForget = null;

        this.livenessTimer = null;
        this.recorder = null;
        this.destroyed = false;
        this._viewerCount = 0;
        this._livePreview = null;
        this._startingRecording = false;
        this._recState = 'idle';
        this._mic = null;
        this._micHeldUntil = 0;

        this.el = this._buildDom();

        this._snapshot = new CameraSnapshot(this.snapshotBaseUrl, this.el, {
            getRecorder: () => this.recorder,
            isLivePreviewActive: () => !!this._livePreview,
        });

        this._recordingUI = new CameraRecordingUI(this.el, {
            getRecorder: () => this.recorder,
            getSnapshotImg: () => this._getSnapshotImg(),
            onIdle: () => {
                this._stopLivenessPolling();
                this._renderViewerState();
                this._updateLivePreview();
                this._snapshot.refresh();
            },
        });

        this._snapshot.start();
        this._syncRecordButton();

        this._viewerCount = cam.viewerCount ?? 0;
        this._onViewerCountChange();
        this._syncGameRecording(cam.recording);
        this._syncMic(cam.mic);
    }

    get key() {
        return this.name;
    }

    update(cam) {
        this.streaming = cam.streaming;
        if (cam.name !== this.name) {
            this.name = cam.name;
            this._nameEl.textContent = this.name;
            this._nameEl.title = this.name;
            this.el.dataset.key = this.name;
        }

        const viewerCount = cam.viewerCount ?? 0;
        if (viewerCount !== this._viewerCount) {
            this._viewerCount = viewerCount;
            this._onViewerCountChange();
        }
        this._syncGameRecording(cam.recording);
        this._syncMic(cam.mic);
    }

    dispose() {
        this._snapshot.stop();
        this._stopLivePreview();
        this._recordingUI.dispose();
        this._moreMenu.dispose();
        this._groupMenu.dispose();
        this._micMenu.dispose();
        this._stopLivenessPolling();
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
        this._renderViewerState();
        this._snapshot.start();
        this._syncRecordButton();
    }

    markDestroyed() {
        this.destroyed = true;
        if (this.recorder?.inGame) this.recorder.sync(null);
        this._stopLivePreview();
        this._snapshot.stop();
        this._viewerCount = 0;
        this.el.classList.add('destroyed');
        this._renderViewerState();
        this._snapshot.showLost();
        this._syncRecordButton();
    }

    setGroup(label) {
        this._groupBtn.classList.toggle('assigned', label != null);
        this._groupBtn.querySelector('span').textContent = label ?? 'Group';
    }

    startRecording() {
        return this._startRecording();
    }

    _buildDom() {
        this._nameEl = h('span', { class: 'camera-name', title: this.name }, this.name);
        this._stateEl = h('span', { dataset: { role: 'rec-status' } }, 'Idle');

        const preview = h('div', { class: 'preview' },
            h('img', { alt: '', crossorigin: 'anonymous', draggable: 'false' }),
            h('div', { class: 'offline-overlay' }),
            h('div', { class: 'drag-handle', title: 'Drag to reorder' }, icon('grip')));
        preview.querySelector('.offline-overlay').innerHTML = WAITING_OVERLAY_HTML;

        const watchBtn = h('a', {
            class: 'btn',
            href: this.streamUrl,
            target: '_blank',
            rel: 'noopener',
            title: 'Open this camera in a new tab, with its controls',
            dataset: { role: 'watch' },
        }, h('span', { class: 'btn-text' }, 'Watch'));

        const recBtn = button({ label: 'Record', className: 'btn btn-rec', role: 'record', onClick: () => this._toggleRecording() });

        const pauseBtn = button({ label: 'Pause', role: 'pause', onClick: () => this._togglePause() });
        pauseBtn.hidden = true;

        this._groupBtn = button({
            label: 'Group',
            className: 'btn btn-quiet group-assign-btn',
            title: 'Record group: cameras in a group start and stop recording together',
        });
        this._groupMenu = new Menu(this._groupBtn, () => this.groupItems?.() ?? []);

        const moreBtn = button({ label: 'More', className: 'btn btn-quiet', title: 'Layout, links for OBS and other options' });
        this._moreMenu = new Menu(moreBtn, () => this._moreItems());

        this._micValue = h('span', { class: 'pane-mic-value' });
        this._micEl = h('button', { type: 'button', class: 'pane-mic', title: 'What this camera hears, in every viewer and recording', hidden: true },
            h('span', { class: 'pane-mic-key' }, 'Mic'), this._micValue, icon('chevron'));
        this._micMenu = new Menu(this._micEl, () => this._micItems(), { align: 'start' });

        return h('article', { class: 'camera-card offline', dataset: { id: this.id, key: this.name } },
            h('div', { class: 'pane-head' },
                h('div', { class: 'pane-title' }, h('span', { class: 'camera-id' }, String(this.id)), this._nameEl),
                h('div', { class: 'pane-state' }, h('span', { class: 'lamp' }), this._stateEl)),
            preview,
            h('div', { class: 'camera-actions' },
                watchBtn, recBtn, pauseBtn,
                h('div', { class: 'actions-end' }, this._groupBtn, moreBtn)),
            this._micEl,
            h('div', { class: 'pane-foot', dataset: { role: 'rec-size' } }));
    }

    _syncMic(mic) {
        this._micEl.hidden = typeof mic !== 'string';
        if (!this._micEl.hidden && performance.now() >= this._micHeldUntil) this._showMic(mic);
    }

    _showMic(mic) {
        this._mic = mic;
        this._micValue.textContent = MICS.find((m) => m.id === mic)?.label ?? mic;
    }

    _micItems() {
        return [
            menuNote('What this camera hears, for everyone listening and in its recordings.'),
            ...MICS.map((mic) => menuItem({
                label: mic.label,
                description: mic.hint,
                checked: mic.id === this._mic,
                onSelect: () => this._setMic(mic.id),
            })),
        ];
    }

    async _setMic(mic) {
        this._micHeldUntil = Infinity;
        this._showMic(mic);
        try {
            await setCameraSettings(this.id, { mic });
        } catch {
            toast(`Could not change the mic of ${this.name}`);
        }
        this._micHeldUntil = performance.now() + MIC_SETTLE_MS;
    }

    _moreItems() {
        const streamUrl = API.stream(this.id);
        const lanItem = menuItem({
            label: 'Copy network stream URL',
            description: 'The same video, at the address other devices on your network can open',
            onSelect: async () => {
                const url = lanUrl(await getSession(), streamUrl);
                if (url) copyWithToast(url, 'Network stream URL');
            },
        });
        lanItem.hidden = true;
        getSession().then((session) => { lanItem.hidden = !lanUrl(session, '/'); });

        return [
            menuItem({ label: 'Open in a layout', description: 'A new layout tab with this camera, to put others next to it', href: `/layout.html?cams=${this.id}`, target: '_blank' }),
            menuSeparator(),
            menuItem({ label: 'Copy viewer link', description: 'This camera\'s page with its controls, what Watch opens', onSelect: () => copyWithToast(location.origin + this.streamUrl) }),
            menuItem({ label: 'Copy stream URL', description: 'The video alone, for an OBS media source or VLC', onSelect: () => copyWithToast(location.origin + streamUrl, 'Stream URL') }),
            lanItem,
            ...(this.destroyed ? [
                menuSeparator(),
                menuItem({ label: 'Forget this camera', description: 'Take it off this page. It comes back if it streams again', onSelect: () => this.onForget?.(this) }),
            ] : []),
        ];
    }

    _onRecordingState(state) {
        if (this._recState === 'finalizing' && state.state === 'idle') {
            toast('Recording saved on the KSP computer', { label: 'Show', onAction: showRecordings });
        }
        this._recState = state.state;
        this._recordingUI.onStateChange(state);
        this._syncRecordButton();
        this.onRecordingChange?.(this);
    }

    _syncRecordButton() {
        if (this.recorder?.isActive || this.recorder?.state === 'finalizing') return;
        const recBtn = this.el.querySelector('[data-role="record"]');
        const reason = this.destroyed ? 'This camera is offline'
            : !this._canRecord() ? 'Recording is not available: in-game recording is off and this browser cannot record'
            : null;
        recBtn.disabled = reason !== null;
        if (reason) recBtn.title = reason;
        else recBtn.removeAttribute('title');
    }

    _canRecord() {
        return isInGameRecordingAvailable() || isRecordingSupported();
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
        if (this._startingRecording || this.destroyed) return;
        this._startingRecording = true;
        try {
            if (this.recorder && this.recorder.state !== 'idle') {
                const old = this.recorder;
                this.recorder = null;
                old.abandon();
            }
            const preferGame = usesGameRecorder() || !isRecordingSupported();
            if (preferGame && await this._tryStartGameRecording()) return;
            if (!isRecordingSupported()) {
                toast(`Could not start recording ${this.name} in the game. Is KSP still in a flight?`);
                return;
            }
            if (preferGame) toast(`The game could not record ${this.name}, so this browser records it: keep this tab open`);
            this._startBrowserRecording();
        } finally {
            this._startingRecording = false;
        }
    }

    _createGameRecorder() {
        return new GameRecorder({
            cameraId: this.id,
            onStateChange: (s) => this._onRecordingState(s),
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
        this.onRecordingChange?.(this);
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
            streamUrl: API.stream(this.id),
            isLocal,
            onStateChange: (s) => this._onRecordingState(s),
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
        this._updateLivePreview();
        this._renderViewerState();
    }

    _renderViewerState() {
        const watched = this._viewerCount > 0;
        this.el.classList.toggle('live', watched);
        if (this.recorder?.isActive || this.recorder?.state === 'finalizing') return;
        if (this.destroyed) this._stateEl.textContent = 'Offline';
        else if (watched) this._stateEl.textContent = this._viewerCount === 1 ? 'Live · 1 viewer' : `Live · ${this._viewerCount} viewers`;
        else this._stateEl.textContent = 'Idle';
    }

    _updateLivePreview() {
        const active = this.recorder?.isActive;
        const browserRecording = active && !this.recorder.inGame;
        if (!this.destroyed && !browserRecording && (this._viewerCount > 0 || active)) this._startLivePreview();
        else this._stopLivePreview();
    }

    _startLivePreview() {
        if (this._livePreview) return;
        const feed = new FeedCanvas('live-preview-feed', { onDraw: () => this._snapshot.markOnline() });
        const snapshotImg = this._getSnapshotImg();
        const preview = snapshotImg.closest('.preview');
        snapshotImg.hidden = true;
        preview.insertBefore(feed.el, preview.querySelector('.offline-overlay'));
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
        this._getSnapshotImg().hidden = false;
        this._snapshot.refresh();
    }
}
