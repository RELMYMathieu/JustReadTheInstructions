import { getCameraSettings, setCameraSettings } from './api.js';
import { formatFov } from './camera-controls.js';
import { TRACKS } from './config.js';
import { h } from './dom.js';
import { isTyping, toast } from './ui.js';

const RATE_RESEND_MS = 200;
const POLL_MS = 500;
const KEY_DIRECTIONS = Object.freeze({
    ArrowLeft: [-1, 0],
    ArrowRight: [1, 0],
    ArrowUp: [0, 1],
    ArrowDown: [0, -1],
});
const ZOOM_KEYS = Object.freeze({ '+': 1, '=': 1, '-': -1, '_': -1 });

let panel = null;

export function isPanOpen() {
    return panel != null && !panel.hidden;
}

export function initPanControls(cameraId) {
    const toggle = document.getElementById('pan-toggle');
    panel = document.getElementById('pan-panel');
    const yawEl = document.getElementById('pan-yaw');
    const pitchEl = document.getElementById('pan-pitch');
    const fovEl = document.getElementById('pan-fov');
    const pitchRow = document.getElementById('pan-pitch-row');
    const pitchButtons = [...panel.querySelectorAll('[data-dir="up"], [data-dir="down"]')];
    const trackGroup = document.getElementById('pan-track');
    const trackNote = document.getElementById('pan-track-note');

    let available = false;
    let pollTimer = null;
    let holdTimer = null;
    let zoomTimer = null;
    let zoomKey = null;
    const heldKeys = new Set();

    const send = (body) => setCameraSettings(cameraId, body)
        .catch(() => toast('Could not move the camera. Is KSP still in a flight?'));

    const trackButtons = TRACKS.map((track) => h('button', {
        type: 'button',
        class: 'btn',
        'aria-pressed': 'false',
        dataset: { track: track.id },
        onClick: () => {
            showTrack(track.id);
            send({ track: track.id });
        },
    }, track.label));
    trackGroup.replaceChildren(...trackButtons);

    function showTrack(id) {
        for (const btn of trackButtons) btn.setAttribute('aria-pressed', String(btn.dataset.track === id));
        const hint = TRACKS.find((t) => t.id === id)?.hint ?? '';
        trackNote.textContent = id === 'off' ? hint : `${hint} Panning by hand stops it.`;
        toggle.classList.toggle('tracking', id !== 'off');
    }

    function showPan(pan) {
        yawEl.textContent = `${Math.round(pan.yaw)}°`;
        pitchEl.textContent = `${Math.round(pan.pitch)}°`;
        const canTilt = pan.pitchMax > pan.pitchMin;
        panel.classList.toggle('no-tilt', !canTilt);
        pitchRow.hidden = !canTilt;
        for (const btn of pitchButtons) btn.hidden = !canTilt;
        if (holdTimer === null) showTrack(pan.track);
    }

    async function refresh() {
        try {
            const settings = await getCameraSettings(cameraId);
            if (settings.pan) showPan(settings.pan);
            if (settings.fov != null) fovEl.textContent = formatFov(settings.fov);
        } catch { }
    }

    function zoom(rate) {
        clearInterval(zoomTimer);
        const tick = () => send({ zoomRate: rate });
        tick();
        zoomTimer = setInterval(tick, RATE_RESEND_MS);
    }

    function stopZoom() {
        if (zoomTimer === null) return;
        clearInterval(zoomTimer);
        zoomTimer = null;
        send({ zoomRate: 0 });
    }

    function hold(yawRate, pitchRate) {
        clearInterval(holdTimer);
        showTrack('off');
        const tick = () => send({ panYawRate: yawRate, panPitchRate: pitchRate });
        tick();
        holdTimer = setInterval(tick, RATE_RESEND_MS);
    }

    function release() {
        if (holdTimer === null) return;
        clearInterval(holdTimer);
        holdTimer = null;
        send({ panYawRate: 0, panPitchRate: 0 });
    }

    function holdKeys() {
        if (heldKeys.size === 0) {
            release();
            return;
        }
        let yaw = 0;
        let pitch = 0;
        for (const key of heldKeys) {
            yaw += KEY_DIRECTIONS[key][0];
            pitch += KEY_DIRECTIONS[key][1];
        }
        hold(yaw, pitch);
    }

    function setOpen(open) {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        clearInterval(pollTimer);
        pollTimer = null;
        if (!open) return;
        refresh();
        pollTimer = setInterval(refresh, POLL_MS);
    }

    toggle.addEventListener('click', () => setOpen(panel.hidden));
    document.getElementById('controls-toggle')?.addEventListener('click', () => setOpen(false));

    for (const btn of panel.querySelectorAll('[data-dir]')) {
        const [yaw, pitch] = { left: [-1, 0], right: [1, 0], up: [0, 1], down: [0, -1] }[btn.dataset.dir];
        btn.addEventListener('pointerdown', (e) => {
            btn.setPointerCapture(e.pointerId);
            hold(yaw, pitch);
        });
        for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) btn.addEventListener(type, release);
    }

    for (const btn of panel.querySelectorAll('[data-zoom]')) {
        btn.addEventListener('pointerdown', (e) => {
            btn.setPointerCapture(e.pointerId);
            zoom(+btn.dataset.zoom);
        });
        for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) btn.addEventListener(type, stopZoom);
    }

    document.getElementById('pan-center').addEventListener('click', () => {
        showTrack('off');
        send({ panYaw: 0, panPitch: 0 });
    });

    document.addEventListener('keydown', (e) => {
        if (isTyping(e) || e.ctrlKey || e.metaKey || e.altKey) return;
        if (e.key in ZOOM_KEYS) {
            e.preventDefault();
            if (e.repeat || zoomKey === e.key) return;
            zoomKey = e.key;
            zoom(ZOOM_KEYS[e.key]);
            return;
        }
        if (!available) return;
        if (e.key === 'Escape' && !panel.hidden) {
            setOpen(false);
            toggle.focus();
            return;
        }
        if (e.key.toLowerCase() === 'p') {
            setOpen(panel.hidden);
            return;
        }
        if (!(e.key in KEY_DIRECTIONS) || e.target.closest?.('input[type="range"]')) return;
        e.preventDefault();
        if (e.repeat || heldKeys.has(e.key)) return;
        heldKeys.add(e.key);
        holdKeys();
    });

    document.addEventListener('keyup', (e) => {
        if (e.key === zoomKey || (zoomKey && e.key in ZOOM_KEYS)) {
            zoomKey = null;
            stopZoom();
        }
        if (!heldKeys.delete(e.key)) return;
        holdKeys();
    });

    window.addEventListener('blur', () => {
        heldKeys.clear();
        release();
        zoomKey = null;
        stopZoom();
    });

    return {
        setAvailable(canPan, track) {
            available = canPan;
            toggle.hidden = !canPan;
            if (!canPan) {
                setOpen(false);
                release();
            } else if (holdTimer === null) {
                showTrack(track ?? 'off');
            }
        },
        openOnLoad() {
            if (available) setOpen(true);
        },
    };
}
