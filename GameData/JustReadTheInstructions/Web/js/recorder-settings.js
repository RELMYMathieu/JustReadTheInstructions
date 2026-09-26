import { LOS_BEHAVIORS, DEFAULT_LOS_BEHAVIOR, RECORDERS } from './config.js';

const STORAGE_KEY = 'jrti.recorder.settings.v1';

const DEFAULTS = Object.freeze({
    losBehavior: DEFAULT_LOS_BEHAVIOR,
    recorder: RECORDERS.GAME,
});

function isValidBehavior(value) {
    return Object.values(LOS_BEHAVIORS).includes(value);
}

function isValidRecorder(value) {
    return Object.values(RECORDERS).includes(value);
}

function load() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return { ...DEFAULTS };
        const parsed = JSON.parse(raw);
        return {
            losBehavior: isValidBehavior(parsed.losBehavior) ? parsed.losBehavior : DEFAULTS.losBehavior,
            recorder: isValidRecorder(parsed.recorder) ? parsed.recorder : DEFAULTS.recorder,
        };
    } catch {
        return { ...DEFAULTS };
    }
}

function save(state) {
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
    }
}

let cached = load();
let inGameRecordingAvailable = false;
const listeners = new Set();

export function getSettings() {
    return { ...cached };
}

export function updateSettings(patch) {
    cached = { ...cached, ...patch };
    save(cached);
    for (const listener of listeners) listener(cached);
}

export function onChange(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
}

export function setInGameRecordingAvailable(available) {
    inGameRecordingAvailable = available;
}

export function isInGameRecordingAvailable() {
    return inGameRecordingAvailable;
}

export function usesGameRecorder() {
    return inGameRecordingAvailable && cached.recorder === RECORDERS.GAME;
}
