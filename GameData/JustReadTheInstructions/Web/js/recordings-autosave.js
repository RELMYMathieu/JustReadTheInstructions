import { API, RECORDINGS_REFRESH_MS } from './config.js';
import { fetchRecordings } from './api.js';
import { IS_HANDHELD, savesRecordingsToDevice, updateSettings } from './recorder-settings.js';
import { h } from './dom.js';
import { toast } from './ui.js';

const SAVED_KEY = 'jrti.recordings.saved.v1';
const SAVED_LIMIT = 100;

let timer = null;
let seen = null;

function claim(file) {
    try {
        const saved = JSON.parse(localStorage.getItem(SAVED_KEY)) ?? [];
        if (saved.includes(file)) return false;
        localStorage.setItem(SAVED_KEY, JSON.stringify([...saved, file].slice(-SAVED_LIMIT)));
    } catch {
    }
    return true;
}

function download(file) {
    const link = h('a', { href: API.recording(file, true), download: file, hidden: true });
    document.body.append(link);
    link.click();
    link.remove();
    if (IS_HANDHELD) toast(`Saving ${file} to this phone`, { label: 'Turn off', onAction: turnOff });
    else toast(`Saving ${file} to this device`);
}

function turnOff() {
    updateSettings({ saveToDevice: false });
    syncRecordingsAutoSave();
    toast('Recordings stay on the KSP computer. Turn this back on in Settings');
}

async function poll() {
    let recordings;
    try {
        recordings = await fetchRecordings();
    } catch {
        return;
    }
    const finished = recordings.filter((r) => !r.recording).map((r) => r.file);
    if (!seen) {
        seen = new Set(finished);
        return;
    }
    for (const file of finished.filter((f) => !seen.has(f))) {
        seen.add(file);
        if (claim(file)) download(file);
    }
}

export function syncRecordingsAutoSave() {
    clearInterval(timer);
    timer = null;
    seen = null;
    if (!savesRecordingsToDevice()) return;
    poll();
    timer = setInterval(poll, RECORDINGS_REFRESH_MS);
}
