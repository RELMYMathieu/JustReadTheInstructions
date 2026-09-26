import {
    CAMERA_SYNC_MS,
    LAYOUT_SIGNAL_CHECK_MS,
    LAYOUT_CHROME_HIDE_MS,
} from './config.js';
import { fetchCameras } from './api.js';
import { copyToClipboard } from './clipboard.js';
import { StreamHub } from './stream-hub.js';
import { layoutRects } from './layout-grid.js';
import { LayoutTile } from './layout-tile.js';

const STORAGE_KEY = 'jrti-layout';
const TILE_GAP = 6;
const DEFAULT_FRAME_ASPECT = 16 / 9;
const RESIZE_SETTLE_MS = 200;
const COPY_FEEDBACK_MS = 1200;
const COPY_LABEL = 'Copy link';

const params = new URLSearchParams(location.search);
const urlMode = params.has('cams');

const grid = document.getElementById('layout-grid');
const emptyEl = document.getElementById('layout-empty');
const statusEl = document.getElementById('layout-status');
const copyBtn = document.getElementById('layout-copy');
const copyLabel = copyBtn.querySelector('span');
const fillBtn = document.getElementById('layout-fill');
const fullscreenBtn = document.getElementById('layout-fullscreen');

const hub = new StreamHub();
const tiles = [];
let spotlightIndex = null;
let fill = false;
let frameAspect = DEFAULT_FRAME_ASPECT;
let cameras = [];
let chromeTimer = null;
let resizeTimer = null;

function parseWholeNumber(text) {
    const trimmed = text?.trim() ?? '';
    return /^\d+$/.test(trimmed) ? Number(trimmed) : null;
}

function loadFromUrl() {
    const cams = params.get('cams');
    return {
        saved: cams ? cams.split(',').map((token) => ({ id: parseWholeNumber(token) })) : [],
        spotlight: parseWholeNumber(params.get('spotlight')),
        fill: params.get('fill') === '1',
    };
}

function loadFromStorage() {
    try {
        const stored = JSON.parse(localStorage.getItem(STORAGE_KEY));
        const names = Array.isArray(stored?.tiles) ? stored.tiles : [];
        return {
            saved: names.map((name) => ({ name: typeof name === 'string' ? name : null })),
            spotlight: Number.isInteger(stored?.spotlight) ? stored.spotlight : null,
            fill: stored?.fill === true,
        };
    } catch {
        return { saved: [], spotlight: null, fill: false };
    }
}

function layoutQuery() {
    let query = `cams=${tiles.map((tile) => tile.id ?? '').join(',')}`;
    if (spotlightIndex !== null) query += `&spotlight=${spotlightIndex}`;
    if (fill) query += '&fill=1';
    return query;
}

function saveLayout() {
    if (urlMode) {
        history.replaceState(null, '', `?${layoutQuery()}`);
        return;
    }
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
            tiles: tiles.map((tile) => tile.name),
            spotlight: spotlightIndex,
            fill,
        }));
    } catch { }
}

function setStatus(message) {
    statusEl.textContent = message;
}

function layoutTiles() {
    const gap = fill ? 0 : TILE_GAP;
    const box = {
        x: gap,
        y: gap,
        width: grid.clientWidth - 2 * gap,
        height: grid.clientHeight - 2 * gap,
    };
    const rects = layoutRects(tiles.length, spotlightIndex, box, { gap, aspect: frameAspect, fill });
    rects.forEach((rect, i) => tiles[i].place(rect));
    tiles.forEach((tile, i) => tile.setSpotlit(i === spotlightIndex));
    emptyEl.hidden = tiles.length > 0;
}

function onFrameSize(width, height) {
    const aspect = width / height;
    if (Math.abs(aspect - frameAspect) < 0.01) return;
    frameAspect = aspect;
    layoutTiles();
}

function findUnclaimedByName(name, claimedIds) {
    return cameras.find((camera) => camera.name === name && !claimedIds.has(camera.id));
}

function resolveTiles() {
    const liveById = new Map(cameras.map((camera) => [camera.id, camera]));
    const claimedIds = new Set(tiles.map((tile) => tile.id).filter((id) => liveById.has(id)));

    for (const tile of tiles) {
        const camera = liveById.get(tile.id) ?? findUnclaimedByName(tile.name, claimedIds);
        if (camera) claimedIds.add(camera.id);
        tile.setCameraList(cameras);
        tile.setCamera(camera ?? null);
    }
}

async function sync() {
    try {
        cameras = await fetchCameras();
        setStatus(cameras.length > 0 ? '' : 'No cameras open. Open or stream a hull camera in KSP first.');
        resolveTiles();
    } catch {
        setStatus('Not connected to the game.');
    } finally {
        setTimeout(sync, CAMERA_SYNC_MS);
    }
}

function checkSignals() {
    const now = Date.now();
    for (const tile of tiles) tile.updateSignal(now);
}

function createTile(binding) {
    const tile = new LayoutTile(binding, {
        subscribe: (cameraId, onFrame) => hub.subscribe(cameraId, onFrame),
        onFrameSize,
        onPick: pickCamera,
        onSpotlight: toggleSpotlight,
        onRemove: removeTile,
    });
    tile.setCameraList(cameras);
    tiles.push(tile);
    grid.append(tile.el);
    return tile;
}

function addTile() {
    const unused = cameras.find((camera) => !tiles.some((tile) => tile.id === camera.id));
    createTile({}).assign(unused ?? null);
    layoutTiles();
    saveLayout();
}

function pickCamera(tile, cameraId) {
    tile.assign(cameras.find((camera) => camera.id === cameraId) ?? null);
    saveLayout();
}

function toggleSpotlight(tile) {
    const index = tiles.indexOf(tile);
    spotlightIndex = spotlightIndex === index ? null : index;
    layoutTiles();
    saveLayout();
}

function removeTile(tile) {
    const index = tiles.indexOf(tile);
    tiles.splice(index, 1);
    tile.dispose();

    if (spotlightIndex === index) spotlightIndex = null;
    else if (spotlightIndex !== null && spotlightIndex > index) spotlightIndex--;

    layoutTiles();
    saveLayout();
}

function applyFill() {
    document.body.classList.toggle('layout-fill', fill);
    fillBtn.classList.toggle('active', fill);
    fillBtn.setAttribute('aria-pressed', String(fill));
    layoutTiles();
}

function toggleFill() {
    fill = !fill;
    applyFill();
    saveLayout();
}

async function copyLink() {
    const copied = await copyToClipboard(`${location.origin}${location.pathname}?${layoutQuery()}`);
    if (!copied) return;
    copyLabel.textContent = 'Copied';
    setTimeout(() => { copyLabel.textContent = COPY_LABEL; }, COPY_FEEDBACK_MS);
}

function toggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen().catch(() => { });
}

function chromeInUse() {
    return tiles.length === 0
        || document.activeElement?.tagName === 'SELECT'
        || document.querySelector('.layout-chrome:hover') !== null;
}

function hideChromeWhenIdle() {
    if (chromeInUse()) {
        chromeTimer = setTimeout(hideChromeWhenIdle, LAYOUT_CHROME_HIDE_MS);
        return;
    }
    document.body.classList.add('idle');
}

function showChrome() {
    document.body.classList.remove('idle');
    clearTimeout(chromeTimer);
    chromeTimer = setTimeout(hideChromeWhenIdle, LAYOUT_CHROME_HIDE_MS);
}

function onResize() {
    grid.classList.add('layout-resizing');
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => grid.classList.remove('layout-resizing'), RESIZE_SETTLE_MS);
    layoutTiles();
}

function wireControls() {
    document.getElementById('layout-add').addEventListener('click', addTile);
    copyBtn.addEventListener('click', copyLink);
    fillBtn.addEventListener('click', toggleFill);
    fullscreenBtn.hidden = !document.fullscreenEnabled;
    fullscreenBtn.addEventListener('click', toggleFullscreen);

    document.addEventListener('fullscreenchange', () => {
        document.body.classList.toggle('layout-fullscreen', Boolean(document.fullscreenElement));
    });
    for (const type of ['pointermove', 'pointerdown', 'keydown']) {
        window.addEventListener(type, showChrome, { passive: true });
    }
    window.addEventListener('resize', onResize);
}

function main() {
    const loaded = urlMode ? loadFromUrl() : loadFromStorage();
    for (const binding of loaded.saved) createTile(binding);
    spotlightIndex = loaded.spotlight !== null && loaded.spotlight < tiles.length ? loaded.spotlight : null;
    fill = loaded.fill;

    wireControls();
    applyFill();
    showChrome();
    sync();
    setInterval(checkSignals, LAYOUT_SIGNAL_CHECK_MS);
}

main();
