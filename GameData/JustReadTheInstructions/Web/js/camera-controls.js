import { getCameraSettings, setCameraSettings } from './api.js';
import { MICS } from './config.js';
import { toast } from './ui.js';

const DEFAULTS = { brightness: 0, contrast: 1, gamma: 1, soundGain: 0 };
const POST_DELAY_MS = 300;
const SOUND_TOGGLES = ['autoGain', 'mastering'];

let panel = null;
let partFov = null;

const widerThanPart = (v) => partFov != null && +v > partFov.max + 0.5;

export function formatFov(fov) {
    return `${(+fov).toFixed(fov < 1 ? 2 : fov < 10 ? 1 : 0)}°`;
}

export function isControlsOpen() {
    return panel != null && !panel.hidden;
}

export function initControls(cameraId) {
    const toggle = document.getElementById('controls-toggle');
    panel = document.getElementById('controls-panel');
    if (!toggle || !panel) return;

    const controls = {
        brightness: { slider: document.getElementById('ctrl-brightness'), display: document.getElementById('val-brightness'), fmt: v => (+v).toFixed(2) },
        contrast: { slider: document.getElementById('ctrl-contrast'), display: document.getElementById('val-contrast'), fmt: v => (+v).toFixed(2) },
        gamma: { slider: document.getElementById('ctrl-gamma'), display: document.getElementById('val-gamma'), fmt: v => (+v).toFixed(2) },
        fov: { slider: document.getElementById('ctrl-fov'), display: document.getElementById('val-fov'), fmt: v => `${formatFov(v)}${widerThanPart(v) ? ' !' : ''}`, warn: widerThanPart, toSlider: Math.log, fromSlider: Math.exp },
        soundGain: { slider: document.getElementById('ctrl-sound-gain'), display: document.getElementById('val-sound-gain'), fmt: v => `${+v > 0 ? '+' : ''}${Math.round(+v)} dB` },
    };
    const sound = {
        group: document.getElementById('sound-group'),
        mic: document.getElementById('ctrl-mic'),
        toggles: [...document.querySelectorAll('[data-toggle]')],
    };
    sound.mic.append(...MICS.map((mic) => new Option(mic.label, mic.id)));
    const sent = {};
    const load = () => loadSettings(cameraId, controls, sound, sent);

    const setOpen = (open) => {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
        toggle.classList.toggle('active', open);
        if (open) load();
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

    let debounce;
    const schedulePost = () => {
        clearTimeout(debounce);
        debounce = setTimeout(() => postChanges(cameraId, readValues(controls, sound), sent), POST_DELAY_MS);
    };

    sound.mic.addEventListener('change', () => {
        showMicHint(sound.mic.value);
        schedulePost();
    });

    for (const btn of sound.toggles) {
        btn.addEventListener('click', () => {
            setToggle(sound, btn.dataset.toggle, btn.dataset.on === 'true');
            schedulePost();
        });
    }

    for (const ctrl of Object.values(controls)) {
        if (!ctrl.slider) continue;
        ctrl.slider.addEventListener('input', () => {
            showValue(ctrl, sliderValue(ctrl));
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
                setSlider(ctrl, def);
                schedulePost();
            }
        });
    });

    load();
}

function showMicHint(value) {
    const hint = document.getElementById('mic-hint');
    if (hint) hint.textContent = MICS.find((mic) => mic.id === value)?.hint ?? '';
}

function setToggle(sound, key, on) {
    for (const btn of sound.toggles)
        if (btn.dataset.toggle === key) btn.setAttribute('aria-pressed', String((btn.dataset.on === 'true') === on));
}

function isToggleOn(sound, key) {
    return sound.toggles.some((btn) => btn.dataset.toggle === key && btn.dataset.on === 'true' && btn.getAttribute('aria-pressed') === 'true');
}

async function loadSettings(cameraId, controls, sound, sent) {
    try {
        const s = await getCameraSettings(cameraId);
        setSlider(controls.brightness, s.brightness ?? 0);
        setSlider(controls.contrast, s.contrast ?? 1);
        setSlider(controls.gamma, s.gamma ?? 1);

        const hasSound = typeof s.mic === 'string';
        sound.group.hidden = !hasSound;
        if (hasSound) {
            sound.mic.value = s.mic;
            showMicHint(s.mic);
            setSlider(controls.soundGain, s.soundGain ?? 0);
            for (const key of SOUND_TOGGLES) setToggle(sound, key, s[key] === true);
        }

        const fovRow = document.getElementById('fov-row');
        if (s.fov != null && controls.fov?.slider) {
            const c = controls.fov;
            partFov = { min: s.fovMin, max: s.fovMax };
            c.slider.min = Math.log(s.fovLimitMin ?? s.fovMin);
            c.slider.max = Math.log(s.fovLimitMax ?? s.fovMax);
            c.slider.step = 'any';
            c.display.title = `This camera part opens up to ${Math.round(s.fovMax)}°. Wider than that, the picture looks stretched.`;
            const fovReset = document.querySelector('[data-reset="fov"]');
            fovReset.dataset.default ??= s.fov;
            setSlider(c, s.fov);
            if (fovRow) fovRow.hidden = false;
        } else if (fovRow) {
            fovRow.hidden = true;
        }

        for (const key of Object.keys(sent)) delete sent[key];
        Object.assign(sent, readValues(controls, sound));
    } catch (err) {
        console.warn('[JRTI] Failed to load camera settings:', err);
        toast('Could not load the settings of this camera from the game');
    }
}

function setSlider(ctrl, value) {
    if (!ctrl.slider || !ctrl.display) return;
    ctrl.slider.value = ctrl.toSlider ? ctrl.toSlider(value) : value;
    showValue(ctrl, value);
}

function sliderValue(ctrl) {
    return ctrl.fromSlider ? ctrl.fromSlider(+ctrl.slider.value) : +ctrl.slider.value;
}

function showValue(ctrl, value) {
    ctrl.display.textContent = ctrl.fmt(value);
    ctrl.display.classList.toggle('ctrl-warn', ctrl.warn?.(value) === true);
}

function readValues(controls, sound) {
    const values = {
        brightness: +controls.brightness.slider.value,
        contrast: +controls.contrast.slider.value,
        gamma: +controls.gamma.slider.value,
    };
    if (!document.getElementById('fov-row')?.hidden && controls.fov?.slider)
        values.fov = Math.round(sliderValue(controls.fov) * 100) / 100;
    if (!sound.group.hidden) {
        values.mic = sound.mic.value;
        values.soundGain = +controls.soundGain.slider.value;
        for (const key of SOUND_TOGGLES) values[key] = isToggleOn(sound, key);
    }
    return values;
}

async function postChanges(cameraId, values, sent) {
    const changes = Object.fromEntries(Object.entries(values).filter(([key, value]) => sent[key] !== value));
    if (Object.keys(changes).length === 0) return;
    Object.assign(sent, changes);
    try {
        await setCameraSettings(cameraId, changes);
    } catch {
        for (const key of Object.keys(changes)) delete sent[key];
        toast('Could not apply the change in the game. Is KSP still in a flight?');
    }
}
