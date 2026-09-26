import { CAMERA_SYNC_MS } from './config.js';
import { fetchCameras } from './api.js';
import { CameraCard } from './camera-card.js';
import { mountSettingsUI } from './settings-ui.js';
import { enableDragOrder } from './drag-order.js';
import { RecordingGroups } from './recording-groups.js';
import { setInGameRecordingAvailable } from './recorder-settings.js';

const KNOWN_CAMERAS_KEY = 'jrti-known-cameras';
const LAUNCH_ID_KEY = 'jrti-launch-id';
const ORDER_KEY = 'jrti-camera-order';

const cards = new Map();
const groups = new RecordingGroups(() => cards);

const liveContainer = document.getElementById('cameras-live');
const offlineSection = document.getElementById('cameras-offline-section');
const offlineContainer = document.getElementById('cameras-offline');

let savedOrder = { live: [], offline: [] };

function setStatus(id, message) {
    const el = document.getElementById(id);
    if (!el) return;
    if (message) { el.textContent = message; el.classList.add('visible'); }
    else { el.classList.remove('visible'); }
}

function loadOrder() {
    try {
        savedOrder = JSON.parse(localStorage.getItem(ORDER_KEY)) ?? { live: [], offline: [] };
    } catch {
        savedOrder = { live: [], offline: [] };
    }
}

function saveOrder() {
    const live = [...liveContainer.querySelectorAll('.camera-card')].map(el => el.dataset.id);
    const offline = [...offlineContainer.querySelectorAll('.camera-card')].map(el => el.dataset.id);
    savedOrder = { live, offline };
    try { localStorage.setItem(ORDER_KEY, JSON.stringify(savedOrder)); } catch { }
}

function insertOrdered(container, el, order) {
    const pos = order.indexOf(el.dataset.id);
    if (pos === -1) { container.appendChild(el); return; }
    const existing = [...container.querySelectorAll('.camera-card')];
    for (let i = pos + 1; i < order.length; i++) {
        const after = existing.find(c => c.dataset.id === order[i]);
        if (after) { after.before(el); return; }
    }
    container.appendChild(el);
}

function persistKnownCameras() {
    try {
        localStorage.setItem(KNOWN_CAMERAS_KEY, JSON.stringify(
            [...cards.values()].map(({ id, name }) => ({ id, name }))
        ));
    } catch { }
}

function restoreKnownCameras() {
    try {
        const stored = JSON.parse(localStorage.getItem(KNOWN_CAMERAS_KEY) || '[]');
        for (const { id, name } of stored) {
            if (cards.has(id)) continue;
            const card = new CameraCard({
                id,
                name,
                streaming: false,
                snapshotUrl: `/camera/${id}/snapshot`,
                streamUrl: `/viewer.html?id=${id}`,
            });
            card.markDestroyed();
            cards.set(id, card);
            groups.syncCard(card);
            insertOrdered(offlineContainer, card.el, savedOrder.offline);
        }
        offlineSection.hidden = stored.length === 0;
    } catch { }
}

async function checkLaunchId() {
    try {
        const res = await fetch('/session');
        if (!res.ok) return;
        const { launchId, inGameRecording, codecs } = await res.json();
        setInGameRecordingAvailable(inGameRecording === true, codecs);
        const stored = localStorage.getItem(LAUNCH_ID_KEY);
        if (stored !== launchId) {
            localStorage.removeItem(KNOWN_CAMERAS_KEY);
            localStorage.removeItem(ORDER_KEY);
            localStorage.setItem(LAUNCH_ID_KEY, launchId);
        }
    } catch { }
}

async function sync() {
    let cameras;
    try {
        cameras = await fetchCameras();
        setStatus('error', null);
    } catch {
        setStatus('error', 'Could not connect to KSP. Is the game running?');
        setStatus('empty', null);
        return;
    }

    const incomingIds = new Set(cameras.map((c) => c.id));

    for (const [id, card] of cards) {
        if (!incomingIds.has(id) && !card.destroyed) {
            card.markDestroyed();
            insertOrdered(offlineContainer, card.el, savedOrder.offline);
        }
    }

    for (const cam of cameras) {
        const existing = cards.get(cam.id);
        if (existing) {
            if (existing.destroyed) {
                existing.revive(cam);
                insertOrdered(liveContainer, existing.el, savedOrder.live);
            } else {
                existing.update(cam);
            }
        } else {
            const card = new CameraCard(cam);
            cards.set(cam.id, card);
            groups.syncCard(card);
            insertOrdered(liveContainer, card.el, savedOrder.live);
        }
    }

    persistKnownCameras();
    groups.refresh();

    const hasOffline = [...cards.values()].some(c => c.destroyed);
    offlineSection.hidden = !hasOffline;

    setStatus('empty', cameras.length === 0 && !hasOffline
        ? 'No cameras open. Open or stream a hull camera in KSP first.'
        : null);
}

function wireLifecycle() {
    const finalize = () => {
        for (const card of cards.values()) card.emergencyFinalize();
    };
    window.addEventListener('pagehide', finalize);
    window.addEventListener('beforeunload', finalize);
}

async function main() {
    mountSettingsUI();
    groups.mount(document.getElementById('groups-bar'));
    wireLifecycle();
    await checkLaunchId();
    loadOrder();
    restoreKnownCameras();
    enableDragOrder(liveContainer, saveOrder);
    enableDragOrder(offlineContainer, saveOrder);
    sync();
    setInterval(sync, CAMERA_SYNC_MS);
}

main();
