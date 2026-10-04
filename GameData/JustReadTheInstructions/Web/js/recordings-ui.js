import { API, RECORDINGS_REFRESH_MS } from './config.js';
import { fetchRecordings } from './api.js';
import { h } from './dom.js';
import { Sheet, formatBytes } from './ui.js';

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
const CAMERA_FILE = /^(.+?)__cam(\d+)__/;

function actionLink(label, title, href, extra) {
    return h('a', { class: 'btn', href, title, ...extra }, label);
}

function renderTitle(file) {
    const match = CAMERA_FILE.exec(file);
    const name = match ? match[1].replaceAll('_', ' ') : file;
    return h('span', { class: 'recording-title' },
        match ? h('span', { class: 'camera-id' }, match[2]) : null,
        h('span', { class: 'recording-name', title: name }, name));
}

function renderRow(recording) {
    const meta = h('div', { class: 'recording-meta' },
        recording.recording ? h('span', { class: 'live-tag' }, 'Recording now') : null,
        h('span', {}, dateFormat.format(new Date(recording.modified))),
        h('span', {}, formatBytes(recording.bytes)));

    return h('li', { class: 'recording-row' },
        renderTitle(recording.file),
        meta,
        CAMERA_FILE.test(recording.file) ? h('span', { class: 'recording-file', title: recording.file }, recording.file) : null,
        h('div', { class: 'recording-actions' },
            actionLink('Play', `Play ${recording.file} in a new tab`, API.recording(recording.file), { target: '_blank', rel: 'noopener' }),
            actionLink('Download', `Download ${recording.file}`, API.recording(recording.file, true), { download: recording.file })));
}

let showSheet = null;

export function showRecordings() {
    showSheet?.();
}

export function mountRecordingsUI() {
    const openBtn = document.getElementById('recordings-btn');
    const sheetEl = document.getElementById('recordings-sheet');
    const list = document.getElementById('recordings-list');
    if (!openBtn || !sheetEl || !list) return;

    let timer = null;
    let lastKey = null;

    const load = async () => {
        let recordings;
        try {
            recordings = await fetchRecordings();
        } catch {
            list.replaceChildren(h('li', { class: 'list-empty' }, 'Could not reach the game to list recordings.'));
            lastKey = null;
            return;
        }
        const key = JSON.stringify(recordings);
        if (key === lastKey) return;
        lastKey = key;
        list.replaceChildren(...(recordings.length > 0
            ? recordings.map(renderRow)
            : [h('li', { class: 'list-empty' }, 'No recordings yet. Press Record on a camera to make one.')]));
    };

    const sheet = new Sheet(sheetEl, {
        onOpen: () => {
            load();
            timer = setInterval(load, RECORDINGS_REFRESH_MS);
        },
        onClose: () => clearInterval(timer),
    });

    openBtn.addEventListener('click', () => sheet.toggle(openBtn));
    showSheet = () => { if (!sheet.isOpen) sheet.open(openBtn); };
}
