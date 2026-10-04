import { CAMERA_SYNC_MS } from './config.js';
import { fetchCameras } from './api.js';
import { CameraCard } from './camera-card.js';
import { mountSettingsUI } from './settings-ui.js';
import { mountRecordingsUI } from './recordings-ui.js';
import { enableDragOrder } from './drag-order.js';
import { RecordingGroups } from './recording-groups.js';
import { setInGameRecordingAvailable } from './recorder-settings.js';
import { getSession } from './session.js';
import { copyWithToast } from './ui.js';

const KNOWN_CAMERAS_KEY = 'jrti-known-cameras';
const LAUNCH_ID_KEY = 'jrti-launch-id';
const ORDER_KEY = 'jrti-camera-order-by-name';

const cards = new Map();
const groups = new RecordingGroups(() => cards);

const liveContainer = document.getElementById('cameras-live');
const offlineSection = document.getElementById('cameras-offline-section');
const offlineContainer = document.getElementById('cameras-offline');
const errorEl = document.getElementById('error');
const emptyEl = document.getElementById('empty');
const linkStatus = document.getElementById('link-status');
const linkStatusText = document.getElementById('link-status-text');

let savedOrder = { live: [], offline: [] };

function setError(message) {
    errorEl.textContent = message ?? '';
    errorEl.classList.toggle('visible', Boolean(message));
}

function setLinkStatus(state, text) {
    linkStatus.dataset.state = state;
    linkStatusText.textContent = text;
}

function loadOrder() {
    try {
        savedOrder = JSON.parse(localStorage.getItem(ORDER_KEY)) ?? { live: [], offline: [] };
    } catch {
        savedOrder = { live: [], offline: [] };
    }
}

function cardKeys(container) {
    return [...container.querySelectorAll('.camera-card')].map((el) => el.dataset.key);
}

function saveOrder() {
    savedOrder = { live: cardKeys(liveContainer), offline: cardKeys(offlineContainer) };
    try { localStorage.setItem(ORDER_KEY, JSON.stringify(savedOrder)); } catch { }
}

function insertOrdered(container, el, order) {
    const pos = order.indexOf(el.dataset.key);
    if (pos === -1) { container.appendChild(el); return; }
    const existing = [...container.querySelectorAll('.camera-card')];
    for (let i = pos + 1; i < order.length; i++) {
        const after = existing.find((c) => c.dataset.key === order[i]);
        if (after) { after.before(el); return; }
    }
    container.appendChild(el);
}

function syncOfflineSection() {
    offlineSection.hidden = ![...cards.values()].some((c) => c.destroyed);
}

function forgetCard(card) {
    cards.delete(card.id);
    card.dispose();
    persistKnownCameras();
    saveOrder();
    syncOfflineSection();
}

function addCard(card) {
    cards.set(card.id, card);
    card.onForget = forgetCard;
    groups.syncCard(card);
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
            addCard(card);
            insertOrdered(offlineContainer, card.el, savedOrder.offline);
        }
        syncOfflineSection();
    } catch { }
}

async function applySession() {
    const session = await getSession();
    setInGameRecordingAvailable(session.inGameRecording === true, session.codecs);
    if (session.launchId && localStorage.getItem(LAUNCH_ID_KEY) !== session.launchId) {
        try {
            localStorage.removeItem(KNOWN_CAMERAS_KEY);
            localStorage.setItem(LAUNCH_ID_KEY, session.launchId);
        } catch { }
    }
    showLanAddress(session.lanUrls[0]);
    if (session.version) document.getElementById('version').textContent = `JRTI ${session.version}`;
}

function showLanAddress(url) {
    const chip = document.getElementById('lan-chip');
    if (!url || !chip) return;
    document.getElementById('lan-chip-text').textContent = new URL(url).host;
    chip.hidden = false;
    chip.addEventListener('click', () => copyWithToast(url, 'Network address'));
}

async function sync() {
    let cameras;
    try {
        cameras = await fetchCameras();
        setError(null);
    } catch {
        setLinkStatus('offline', 'Not connected to KSP');
        setError('Could not reach KSP. Is the game running, in a flight, with the web server on?');
        emptyEl.classList.remove('visible');
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
            groups.syncCard(existing);
        } else {
            const card = new CameraCard(cam);
            addCard(card);
            insertOrdered(liveContainer, card.el, savedOrder.live);
        }
    }

    persistKnownCameras();
    groups.refresh();
    syncOfflineSection();
    emptyEl.classList.toggle('visible', cameras.length === 0);
    setLinkStatus('online', cameras.length === 1 ? 'Connected · 1 camera' : `Connected · ${cameras.length} cameras`);
}

function trackStatusLineHeight() {
    const statusline = document.querySelector('.statusline');
    new ResizeObserver(() => {
        document.documentElement.style.setProperty('--status-h', `${statusline.offsetHeight}px`);
    }).observe(statusline);
}

async function main() {
    mountSettingsUI();
    mountRecordingsUI();
    groups.mount(document.getElementById('groups-bar'));
    trackStatusLineHeight();
    await applySession();
    loadOrder();
    restoreKnownCameras();
    enableDragOrder(liveContainer, saveOrder);
    enableDragOrder(offlineContainer, saveOrder);
    sync();
    setInterval(sync, CAMERA_SYNC_MS);
}

main();
