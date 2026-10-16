import { getCameraSettings, setCameraSettings } from './api.js';
import { formatFov } from './camera-controls.js';
import { TRACKS } from './config.js';
import { h } from './dom.js';
import { isTyping, toast } from './ui.js';

const RATE_RESEND_MS = 200;
const POLL_MS = 500;
const NUDGE_MS = 250;
const DIRECTIONS = Object.freeze({ left: [-1, 0], right: [1, 0], up: [0, 1], down: [0, -1] });
const KEY_DIRECTIONS = Object.freeze({ ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down' });
const ZOOM_KEYS = Object.freeze({ '+': 1, '=': 1, '-': -1, '_': -1 });
const PRESS_KEYS = new Set([' ', 'Enter']);

let panel = null;

export function isPanOpen() {
    return panel != null && !panel.hidden;
}

function holdable(btn, start, stop) {
    btn.addEventListener('pointerdown', (e) => {
        btn.setPointerCapture(e.pointerId);
        start();
    });
    for (const type of ['pointerup', 'pointercancel', 'lostpointercapture', 'blur']) btn.addEventListener(type, stop);
    btn.addEventListener('keydown', (e) => {
        if (!PRESS_KEYS.has(e.key)) return;
        e.preventDefault();
        if (!e.repeat) start();
    });
    btn.addEventListener('keyup', (e) => {
        if (!PRESS_KEYS.has(e.key)) return;
        e.preventDefault();
        stop();
    });
    btn.addEventListener('click', (e) => {
        if (e.detail !== 0) return;
        start();
        setTimeout(stop, NUDGE_MS);
    });
}

export function initPanControls(cameraId, { onFov } = {}) {
    const toggle = document.getElementById('pan-toggle');
    const toggleText = document.getElementById('pan-toggle-text');
    panel = document.getElementById('pan-panel');
    const yawEl = document.getElementById('pan-yaw');
    const pitchEl = document.getElementById('pan-pitch');
    const fovEl = document.getElementById('pan-fov');
    const pitchRow = document.getElementById('pan-pitch-row');
    const dirButtons = [...panel.querySelectorAll('[data-dir]')];
    const zoomButtons = [...panel.querySelectorAll('[data-zoom]')];
    const trackGroup = document.getElementById('pan-track');
    const trackNote = document.getElementById('pan-track-note');

    let available = false;
    let pollTimer = null;
    let holdTimer = null;
    let zoomTimer = null;
    let zoomPollTimer = null;
    let zoomKey = null;
    const held = new Set();

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
        const tracking = id !== 'off';
        trackNote.textContent = tracking ? `${hint} Panning by hand stops it.` : hint;
        toggle.classList.toggle('tracking', tracking);
        toggleText.textContent = tracking ? 'Tracking' : 'Pan';
    }

    function showPan(pan) {
        yawEl.textContent = `${Math.round(pan.yaw)}°`;
        pitchEl.textContent = `${Math.round(pan.pitch)}°`;
        const canTilt = pan.pitchMax > pan.pitchMin;
        panel.classList.toggle('no-tilt', !canTilt);
        pitchRow.hidden = !canTilt;
        for (const btn of dirButtons) if (btn.dataset.dir === 'up' || btn.dataset.dir === 'down') btn.hidden = !canTilt;
        if (holdTimer === null) showTrack(pan.track);
    }

    async function refresh() {
        try {
            const settings = await getCameraSettings(cameraId);
            if (settings.pan) showPan(settings.pan);
            if (settings.fov != null) {
                fovEl.textContent = formatFov(settings.fov);
                onFov?.(settings.fov);
            }
        } catch { }
    }

    function zoom(rate) {
        clearInterval(zoomTimer);
        for (const btn of zoomButtons) btn.classList.toggle('held', +btn.dataset.zoom === rate);
        const tick = () => send({ zoomRate: rate });
        tick();
        zoomTimer = setInterval(tick, RATE_RESEND_MS);
        zoomPollTimer ??= setInterval(refresh, POLL_MS);
    }

    function stopZoom() {
        if (zoomTimer === null) return;
        clearInterval(zoomTimer);
        clearInterval(zoomPollTimer);
        zoomTimer = null;
        zoomPollTimer = null;
        for (const btn of zoomButtons) btn.classList.remove('held');
        send({ zoomRate: 0 }).then(refresh);
    }

    function applyHeld() {
        for (const btn of dirButtons) btn.classList.toggle('held', held.has(btn.dataset.dir));
        clearInterval(holdTimer);
        if (held.size === 0) {
            if (holdTimer === null) return;
            holdTimer = null;
            send({ panYawRate: 0, panPitchRate: 0 });
            return;
        }
        let yaw = 0;
        let pitch = 0;
        for (const dir of held) {
            yaw += DIRECTIONS[dir][0];
            pitch += DIRECTIONS[dir][1];
        }
        showTrack('off');
        const tick = () => send({ panYawRate: yaw, panPitchRate: pitch });
        tick();
        holdTimer = setInterval(tick, RATE_RESEND_MS);
    }

    function press(dir) {
        held.add(dir);
        applyHeld();
    }

    function lift(dir) {
        if (held.delete(dir)) applyHeld();
    }

    function releaseAll() {
        held.clear();
        applyHeld();
        zoomKey = null;
        stopZoom();
    }

    function setOpen(open) {
        panel.hidden = !open;
        document.body.classList.toggle('pan-open', open);
        toggle.setAttribute('aria-expanded', String(open));
        clearInterval(pollTimer);
        pollTimer = null;
        if (!open) return;
        refresh();
        pollTimer = setInterval(refresh, POLL_MS);
    }

    new ResizeObserver(() => document.documentElement.style.setProperty('--pan-h', `${panel.offsetHeight}px`)).observe(panel);
    toggle.addEventListener('click', () => setOpen(panel.hidden));
    document.getElementById('controls-toggle')?.addEventListener('click', () => setOpen(false));

    for (const btn of dirButtons) holdable(btn, () => press(btn.dataset.dir), () => lift(btn.dataset.dir));
    for (const btn of zoomButtons) holdable(btn, () => zoom(+btn.dataset.zoom), stopZoom);

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
        const dir = KEY_DIRECTIONS[e.key];
        if (!dir || e.target.closest?.('#controls-panel, input[type="range"]')) return;
        e.preventDefault();
        if (!e.repeat) press(dir);
    });

    document.addEventListener('keyup', (e) => {
        if (e.key === zoomKey || (zoomKey && e.key in ZOOM_KEYS)) {
            zoomKey = null;
            stopZoom();
        }
        const dir = KEY_DIRECTIONS[e.key];
        if (dir) lift(dir);
    });

    window.addEventListener('blur', releaseAll);

    return {
        get available() {
            return available;
        },
        setAvailable(canPan, track) {
            available = canPan;
            toggle.hidden = !canPan;
            if (!canPan) {
                setOpen(false);
                held.clear();
                applyHeld();
            } else if (holdTimer === null) {
                showTrack(track ?? 'off');
            }
        },
        openOnLoad() {
            if (available) setOpen(true);
        },
    };
}
