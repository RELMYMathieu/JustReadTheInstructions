import { LOS_BEHAVIORS, DEFAULT_LOS_BEHAVIOR, RECORDERS, VIDEO_CODECS } from './config.js';

const STORAGE_KEY = 'jrti.recorder.settings.v1';

const DEFAULTS = Object.freeze({
    losBehavior: DEFAULT_LOS_BEHAVIOR,
    recorder: RECORDERS.GAME,
    codec: VIDEO_CODECS.H264,
});

function isValidBehavior(value) {
    return Object.values(LOS_BEHAVIORS).includes(value);
}

function isValidRecorder(value) {
    return Object.values(RECORDERS).includes(value);
}

function isValidCodec(value) {
    return Object.values(VIDEO_CODECS).includes(value);
}

function load() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return { ...DEFAULTS };
        const parsed = JSON.parse(raw);
        return {
            losBehavior: isValidBehavior(parsed.losBehavior) ? parsed.losBehavior : DEFAULTS.losBehavior,
            recorder: isValidRecorder(parsed.recorder) ? parsed.recorder : DEFAULTS.recorder,
            codec: isValidCodec(parsed.codec) ? parsed.codec : DEFAULTS.codec,
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
let gameCodecs = [DEFAULTS.codec];
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

export function setInGameRecordingAvailable(available, codecs) {
    inGameRecordingAvailable = available;
    if (Array.isArray(codecs) && codecs.length > 0) gameCodecs = codecs.filter(isValidCodec);
}

export function getGameCodecs() {
    return [...gameCodecs];
}

export function selectedGameCodec() {
    return gameCodecs.includes(cached.codec) ? cached.codec : DEFAULTS.codec;
}

export function isInGameRecordingAvailable() {
    return inGameRecordingAvailable;
}

export function usesGameRecorder() {
    return inGameRecordingAvailable && cached.recorder === RECORDERS.GAME;
}
