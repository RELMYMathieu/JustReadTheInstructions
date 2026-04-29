import { getCameraSettings, setCameraSettings } from './api.js';

const DEFAULTS = { brightness: 0, contrast: 1, gamma: 1 };

export function initControls(cameraId) {
    const toggle = document.getElementById('controls-toggle');
    const panel = document.getElementById('controls-panel');
    if (!toggle || !panel) return;

    toggle.addEventListener('click', (e) => {
        e.stopPropagation();
        panel.hidden = !panel.hidden;
    });

    document.addEventListener('click', (e) => {
        if (!panel.hidden && !panel.contains(e.target) && e.target !== toggle)
            panel.hidden = true;
    });

    const controls = {
        brightness: { slider: document.getElementById('ctrl-brightness'), display: document.getElementById('val-brightness'), fmt: v => (+v).toFixed(2) },
        contrast:   { slider: document.getElementById('ctrl-contrast'),   display: document.getElementById('val-contrast'),   fmt: v => (+v).toFixed(2) },
        gamma:      { slider: document.getElementById('ctrl-gamma'),      display: document.getElementById('val-gamma'),      fmt: v => (+v).toFixed(2) },
        fov:        { slider: document.getElementById('ctrl-fov'),        display: document.getElementById('val-fov'),        fmt: v => `${Math.round(+v)}°` },
    };

    let debounce;
    const schedulePost = () => {
        clearTimeout(debounce);
        debounce = setTimeout(() => postSettings(cameraId, controls), 300);
    };

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

    loadSettings(cameraId, controls);
}

async function loadSettings(cameraId, controls) {
    try {
        const s = await getCameraSettings(cameraId);
        setSlider(controls.brightness, s.brightness ?? 0);
        setSlider(controls.contrast, s.contrast ?? 1);
        setSlider(controls.gamma, s.gamma ?? 1);

        const fovRow = document.getElementById('fov-row');
        if (s.fov != null && s.fovMax > s.fovMin) {
            const c = controls.fov;
            if (!c?.slider) return;
            c.slider.min = s.fovMin;
            c.slider.max = s.fovMax;
            document.querySelector('[data-reset="fov"]').dataset.default = s.fov;
            setSlider(c, s.fov);
            if (fovRow) fovRow.hidden = false;
        } else {
            if (fovRow) fovRow.hidden = true;
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
            contrast:   +controls.contrast.slider.value,
            gamma:      +controls.gamma.slider.value,
        };
        const fovRow = document.getElementById('fov-row');
        if (!fovRow?.hidden && controls.fov?.slider)
            payload.fov = +controls.fov.slider.value;
        await setCameraSettings(cameraId, payload);
    } catch { }
}
