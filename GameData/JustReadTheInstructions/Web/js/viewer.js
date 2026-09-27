import { API, CAMERA_SYNC_MS, VIEWER_LOS_DELAY_MS, LOS_OVERLAY_HTML, WAITING_OVERLAY_HTML } from './config.js';
import { fetchCameras } from './api.js';
import { initControls, isControlsOpen } from './camera-controls.js';
import { StreamHub } from './stream-hub.js';
import { FeedCanvas } from './feed-canvas.js';
import { copyWithToast, formatTimecode, isTyping } from './ui.js';

const HUD_HIDE_MS = 3000;
const SIGNAL_CHECK_MS = 1000;

function getCameraId() {
    const id = new URLSearchParams(location.search).get('id');
    return id !== null && /^\d+$/.test(id) ? Number(id) : null;
}

function wireIdleChrome() {
    let timer = null;
    const hide = () => {
        if (isControlsOpen() || document.querySelector('.viewer-chrome:hover')) {
            timer = setTimeout(hide, HUD_HIDE_MS);
            return;
        }
        document.body.classList.add('idle');
    };
    const show = () => {
        document.body.classList.remove('idle');
        clearTimeout(timer);
        timer = setTimeout(hide, HUD_HIDE_MS);
    };
    for (const type of ['pointermove', 'pointerdown', 'keydown', 'touchstart']) {
        document.addEventListener(type, show, { passive: true });
    }
    show();
}

function toggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen().catch(() => { });
}

function main() {
    const cameraId = getCameraId();
    const viewer = document.getElementById('viewer');
    const overlay = document.getElementById('viewer-overlay');
    const nameEl = document.getElementById('viewer-name');
    const tally = document.getElementById('viewer-tally');
    const tallyText = document.getElementById('viewer-tally-text');

    if (cameraId === null) {
        document.title = 'JRTI Stream - no camera';
        overlay.textContent = 'No camera in this link. Open a camera from the main page.';
        return;
    }

    document.title = `Camera ${cameraId} - JRTI`;
    document.getElementById('viewer-id').textContent = String(cameraId);
    nameEl.textContent = `Camera ${cameraId}`;

    let signal = null;
    let lastFrameAt = 0;
    let recording = null;

    const setSignal = (next) => {
        if (next === signal) return;
        signal = next;
        viewer.classList.toggle('offline', next !== 'live');
        overlay.innerHTML = next === 'lost' ? LOS_OVERLAY_HTML : next === 'waiting' ? WAITING_OVERLAY_HTML : '';
    };

    const renderTally = () => {
        const recordingNow = Boolean(recording && !recording.paused);
        tally.classList.toggle('is-rec', recordingNow);
        tally.classList.toggle('is-live', !recordingNow && signal === 'live');
        tallyText.textContent = recordingNow ? `REC ${formatTimecode(recording.elapsedMs)}` : signal === 'live' ? 'Live' : 'No signal';
    };

    const feed = new FeedCanvas('viewer-feed', {
        onDraw: () => {
            lastFrameAt = Date.now();
            setSignal('live');
        },
    });
    viewer.prepend(feed.el);
    new StreamHub().subscribe(cameraId, (frame) => feed.push(frame));
    setSignal('waiting');

    const openedAt = Date.now();
    setInterval(() => {
        const since = lastFrameAt || openedAt;
        if (Date.now() - since >= VIEWER_LOS_DELAY_MS) setSignal('lost');
        if (recording && !recording.paused) recording.elapsedMs += SIGNAL_CHECK_MS;
        renderTally();
    }, SIGNAL_CHECK_MS);

    const syncCamera = async () => {
        try {
            const camera = (await fetchCameras()).find((c) => c.id === cameraId);
            recording = camera?.recording ?? null;
            if (camera) {
                nameEl.textContent = camera.name;
                document.title = `${camera.name} - JRTI`;
            }
        } catch { }
        renderTally();
    };
    syncCamera();
    setInterval(syncCamera, CAMERA_SYNC_MS);

    initControls(cameraId);
    wireIdleChrome();

    const streamUrl = location.origin + API.stream(cameraId);
    document.getElementById('viewer-copy').addEventListener('click', () => copyWithToast(streamUrl, 'Stream URL'));
    const fullscreenBtn = document.getElementById('viewer-fullscreen');
    fullscreenBtn.hidden = !document.fullscreenEnabled;
    fullscreenBtn.addEventListener('click', toggleFullscreen);
    document.addEventListener('keydown', (e) => {
        if (e.key.toLowerCase() === 'f' && !isTyping(e) && !e.ctrlKey && !e.metaKey && !e.altKey) toggleFullscreen();
    });
}

main();
