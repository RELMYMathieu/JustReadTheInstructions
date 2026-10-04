import { VIDEO_CODECS } from './config.js';

const STORAGE_KEY = 'jrti.recorder.settings.v1';

const DEFAULTS = Object.freeze({
    codec: VIDEO_CODECS.H264,
});

function isValidCodec(value) {
    return Object.values(VIDEO_CODECS).includes(value);
}

function load() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return { ...DEFAULTS };
        const parsed = JSON.parse(raw);
        return { codec: isValidCodec(parsed.codec) ? parsed.codec : DEFAULTS.codec };
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

export function isInGameRecordingAvailable() {
    return inGameRecordingAvailable;
}
