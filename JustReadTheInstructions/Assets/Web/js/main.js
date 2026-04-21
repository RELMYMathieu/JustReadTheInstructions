import { CAMERA_SYNC_MS } from './config.js';
import { fetchCameras } from './api.js';
import { CameraCard } from './camera-card.js';
import { mountSettingsUI } from './settings-ui.js';

const KNOWN_CAMERAS_KEY = 'jrti-known-cameras';
const LAUNCH_ID_KEY = 'jrti-launch-id';

const cards = new Map();

const liveContainer = document.getElementById('cameras-live');
const offlineSection = document.getElementById('cameras-offline-section');
const offlineContainer = document.getElementById('cameras-offline');

function setStatus(id, message) {
    const el = document.getElementById(id);
    if (!el) return;
    if (message) { el.textContent = message; el.classList.add('visible'); }
    else { el.classList.remove('visible'); }
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
            offlineContainer.appendChild(card.el);
        }
        offlineSection.hidden = stored.length === 0;
    } catch { }
}

async function checkLaunchId() {
    try {
        const res = await fetch('/session');
        if (!res.ok) return;
        const { launchId } = await res.json();
        const stored = localStorage.getItem(LAUNCH_ID_KEY);
        if (stored !== launchId) {
            localStorage.removeItem(KNOWN_CAMERAS_KEY);
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
            offlineContainer.appendChild(card.el);
        }
    }

    for (const cam of cameras) {
        const existing = cards.get(cam.id);
        if (existing) {
            if (existing.destroyed) {
                existing.revive(cam);
                liveContainer.appendChild(existing.el);
            } else {
                existing.update(cam);
            }
        } else {
            const card = new CameraCard(cam);
            cards.set(cam.id, card);
            liveContainer.appendChild(card.el);
        }
    }

    persistKnownCameras();

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
    wireLifecycle();
    await checkLaunchId();
    restoreKnownCameras();
    sync();
    setInterval(sync, CAMERA_SYNC_MS);
}

main();
