import { VIDEO_CODECS } from './config.js';

const STORAGE_KEY = 'jrti.recorder.settings.v1';

const HOST_NAMES = ['localhost', '127.0.0.1', '[::1]'];

export const IS_HANDHELD = window.matchMedia('(pointer: coarse)').matches;

const DEFAULTS = Object.freeze({
    codec: VIDEO_CODECS.H264,
    saveToDevice: !HOST_NAMES.includes(location.hostname),
});

function isValidCodec(value) {
    return Object.values(VIDEO_CODECS).includes(value);
}

function load() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return { ...DEFAULTS };
        const parsed = JSON.parse(raw);
        return {
            codec: isValidCodec(parsed.codec) ? parsed.codec : DEFAULTS.codec,
            saveToDevice: typeof parsed.saveToDevice === 'boolean' ? parsed.saveToDevice : DEFAULTS.saveToDevice,
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

export function updateSettings(patch) {
    cached = { ...cached, ...patch };
    save(cached);
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

export function savesRecordingsToDevice() {
    return cached.saveToDevice;
}

export function isInGameRecordingAvailable() {
    return inGameRecordingAvailable;
}
