import { getCameraSettings, setCameraSettings } from './api.js';

const DEFAULTS = { brightness: 0, contrast: 1, gamma: 1, soundGain: 0 };
const POST_DELAY_MS = 300;
const MIC_HINTS = {
    game: 'What the player would hear standing where this camera is.',
    external: 'Outside mic: sound crosses the air and arrives late from far away. Silent in vacuum, except its own vessel through the hull.',
    onboard: 'Inside mic: its own vessel through the structure, other vessels muffled by the hull.',
};

let panel = null;

export function isControlsOpen() {
    return panel != null && !panel.hidden;
}

export function initControls(cameraId) {
    const toggle = document.getElementById('controls-toggle');
    panel = document.getElementById('controls-panel');
    if (!toggle || !panel) return;

    const setOpen = (open) => {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        toggle.classList.toggle('active', open);
    };

    toggle.addEventListener('click', (e) => {
        e.stopPropagation();
        setOpen(panel.hidden);
    });

    document.addEventListener('click', (e) => {
        if (!panel.hidden && !panel.contains(e.target) && !toggle.contains(e.target)) setOpen(false);
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !panel.hidden) {
            setOpen(false);
            toggle.focus();
        }
    });

    const controls = {
        brightness: { slider: document.getElementById('ctrl-brightness'), display: document.getElementById('val-brightness'), fmt: v => (+v).toFixed(2) },
        contrast: { slider: document.getElementById('ctrl-contrast'), display: document.getElementById('val-contrast'), fmt: v => (+v).toFixed(2) },
        gamma: { slider: document.getElementById('ctrl-gamma'), display: document.getElementById('val-gamma'), fmt: v => (+v).toFixed(2) },
        fov: { slider: document.getElementById('ctrl-fov'), display: document.getElementById('val-fov'), fmt: v => `${Math.round(+v)}°` },
        soundGain: { slider: document.getElementById('ctrl-sound-gain'), display: document.getElementById('val-sound-gain'), fmt: v => `${+v > 0 ? '+' : ''}${Math.round(+v)} dB` },
    };
    const mic = document.getElementById('ctrl-mic');

    let debounce;
    const schedulePost = () => {
        clearTimeout(debounce);
        debounce = setTimeout(() => postSettings(cameraId, controls), POST_DELAY_MS);
    };

    mic?.addEventListener('change', () => {
        showMicHint(mic.value);
        schedulePost();
    });

    for (const ctrl of Object.values(controls)) {
        if (!ctrl.slider) continue;
        ctrl.slider.addEventListener('input', () => {
            ctrl.display.textContent = ctrl.fmt(ctrl.slider.value);
            schedulePost();
        });
    }

    document.querySelectorAll('[data-reset]').forEach(btn => {
        btn.addEventListener('click', () => {
            const key = btn.dataset.reset;
            const ctrl = controls[key];
            if (!ctrl?.slider) return;
            const def = btn.dataset.default !== undefined ? +btn.dataset.default : DEFAULTS[key];
            if (def !== undefined && !isNaN(def)) {
                ctrl.slider.value = def;
                ctrl.display.textContent = ctrl.fmt(def);
                schedulePost();
            }
        });
    });

    loadSettings(cameraId, controls, mic);
}

function showMicHint(value) {
    const hint = document.getElementById('mic-hint');
    if (hint) hint.textContent = MIC_HINTS[value] ?? '';
}

function showSoundRows(visible) {
    for (const id of ['mic-row', 'mic-hint', 'sound-gain-row']) {
        const row = document.getElementById(id);
        if (row) row.hidden = !visible;
    }
}

async function loadSettings(cameraId, controls, mic) {
    try {
        const s = await getCameraSettings(cameraId);
        setSlider(controls.brightness, s.brightness ?? 0);
        setSlider(controls.contrast, s.contrast ?? 1);
        setSlider(controls.gamma, s.gamma ?? 1);

        const hasSound = typeof s.mic === 'string' && mic != null;
        showSoundRows(hasSound);
        if (hasSound) {
            mic.value = s.mic;
            showMicHint(s.mic);
            setSlider(controls.soundGain, s.soundGain ?? 0);
        }

        const fovRow = document.getElementById('fov-row');
        if (s.fov != null && s.fovMax > s.fovMin) {
            const c = controls.fov;
            if (!c?.slider) return;
            c.slider.min = s.fovMin;
            c.slider.max = s.fovMax;
            document.querySelector('[data-reset="fov"]').dataset.default = s.fov;
            setSlider(c, s.fov);
            if (fovRow) fovRow.hidden = false;
        } else if (fovRow) {
            fovRow.hidden = true;
        }
    } catch (err) {
        console.warn('[JRTI] Failed to load camera settings:', err);
    }
}

function setSlider(ctrl, value) {
    if (!ctrl.slider || !ctrl.display) return;
    ctrl.slider.value = value;
    ctrl.display.textContent = ctrl.fmt(value);
}

async function postSettings(cameraId, controls) {
    try {
        const payload = {
            brightness: +controls.brightness.slider.value,
            contrast: +controls.contrast.slider.value,
            gamma: +controls.gamma.slider.value,
        };
        const fovRow = document.getElementById('fov-row');
        if (!fovRow?.hidden && controls.fov?.slider)
            payload.fov = +controls.fov.slider.value;
        if (!document.getElementById('mic-row')?.hidden) {
            payload.mic = document.getElementById('ctrl-mic').value;
            payload.soundGain = +controls.soundGain.slider.value;
        }
        await setCameraSettings(cameraId, payload);
    } catch { }
}
