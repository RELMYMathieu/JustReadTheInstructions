import { CAMERA_SYNC_MS, LAYOUT_SIGNAL_CHECK_MS, LAYOUT_CHROME_HIDE_MS } from './config.js';
import { fetchCameras, fetchLayouts, deleteLayout, fetchProgram, takeProgram, clearProgram } from './api.js';
import { StreamHub } from './stream-hub.js';
import { layoutRects } from './layout-grid.js';
import { LayoutTile } from './layout-tile.js';
import { CameraTray } from './layout-tray.js';
import { storeForLocation, savedLayoutStore, saveLayoutAs, LABEL_MODES } from './layout-store.js';
import { beginDrag, isDragging } from './layout-drag.js';
import { onServerEvent } from './events.js';
import { h, button } from './dom.js';
import { Menu, menuItem, menuSeparator, menuHeading, copyWithToast, isMenuOpen, isTyping, toast } from './ui.js';
import { getSession, lanUrl } from './session.js';

const TILE_GAP = 6;
const DEFAULT_FRAME_ASPECT = 16 / 9;
const RESIZE_SETTLE_MS = 200;
const FLASH_MS = 700;
const NOTICE_MS = 8000;
const UNDO_LIMIT = 30;
const SHORTCUT_LAYOUTS = 9;
const LAYOUT_NAME_PATTERN = /^[\p{L}\p{N} _-]{1,64}$/u;
const DIGIT_CODE = /^(?:Digit|Numpad)([0-9])$/;
const LABEL_TEXT = { auto: 'Names: when idle', always: 'Names: on', never: 'Names: off' };
const EMPTY_LAYOUT = Object.freeze({ tiles: [], spotlight: null, fill: false, labels: 'auto' });
const SAVE_STATES = {
    missing: { text: 'New saved layout: your first change creates it' },
    saving: { text: 'Saving...' },
    saved: { text: 'Saved in the game' },
    retrying: { text: 'Not saved, retrying', error: true },
};
const SHORTCUTS = [
    ['1 - 9', 'Spotlight that tile, again to go back'],
    ['0', 'Back to the grid'],
    ['Shift 1 - 9', 'Open saved layout 1 to 9 and put it on air'],
    ['C', 'Camera list'],
    ['T', 'Add an empty tile'],
    ['G', 'Fill the window edge to edge'],
    ['N', 'Camera names: when idle, on, off'],
    ['F', 'Fullscreen'],
    ['Ctrl Z', 'Undo the last change'],
    ['?', 'This list'],
    ['Double-click', 'Spotlight a tile'],
    ['Drag', 'Drag a tile onto another to swap them'],
];

let store = storeForLocation(new URLSearchParams(location.search));
const programMode = store.kind === 'program';

const grid = document.getElementById('layout-grid');
const emptyEl = document.getElementById('layout-empty');
const statusEl = document.getElementById('layout-status');
const fillBtn = document.getElementById('layout-fill');
const labelsBtn = document.getElementById('layout-labels');
const fullscreenBtn = document.getElementById('layout-fullscreen');
const switcherBtn = document.getElementById('layout-switcher');
const takeBtn = document.getElementById('layout-take');
const programHint = document.getElementById('program-hint');

const hub = new StreamHub();
const tiles = [];
let spotlightIndex = null;
let fill = false;
let labels = 'auto';
let frameAspect = DEFAULT_FRAME_ASPECT;
let stripSide = 'right';
let cameras = [];
let savedNames = [];
let onAirName = null;
let chromeTimer = null;
let resizeTimer = null;
let notice = null;
let noticeTimer = null;
let connection = null;
let saveState = null;
const undoStack = [];

const tray = new CameraTray({
    sheetEl: document.getElementById('layout-tray'),
    listEl: document.getElementById('tray-list'),
    openerEl: document.getElementById('layout-tray-btn'),
    isInLayout: (cameraId) => tiles.some((tile) => tile.camera?.id === cameraId),
    onPointerDown: (camera, e) => grabCamera(camera, e),
});

function renderStatus() {
    const shown = notice ?? connection ?? saveState;
    statusEl.textContent = shown?.text ?? '';
    statusEl.classList.toggle('error', shown?.error === true);
}

function setNotice(text, error = false) {
    notice = { text, error };
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => {
        notice = null;
        renderStatus();
    }, NOTICE_MS);
    renderStatus();
}

function setConnection(text, error = false) {
    connection = text ? { text, error } : null;
    renderStatus();
}

function currentLayout() {
    return { tiles: tiles.map((tile) => tile.stored), spotlight: spotlightIndex, fill, labels };
}

function persist() {
    store.save(currentLayout());
    tray.refreshMarks();
}

function onSaveState(state) {
    if (state === 'retrying' && saveState !== SAVE_STATES.retrying) toast('Layout not saved in the game, retrying');
    saveState = SAVE_STATES[state];
    renderStatus();
}

function change(mutate, undoMessage) {
    undoStack.push(currentLayout());
    if (undoStack.length > UNDO_LIMIT) undoStack.shift();
    mutate();
    layoutTiles();
    persist();
    if (undoMessage) toast(undoMessage, { label: 'Undo', onAction: undo });
}

function undo() {
    const previous = undoStack.pop();
    if (!previous) {
        toast('Nothing to undo');
        return;
    }
    applyLayout(previous);
    persist();
    toast('Undone');
}

function applyRemoteLayout(layout) {
    undoStack.length = 0;
    applyLayout(layout);
}

function applyProgramLayout(layout) {
    applyLayout(layout);
    programHint.hidden = store.name !== null;
}

function layoutTiles() {
    const gap = fill ? 0 : TILE_GAP;
    const box = { x: gap, y: gap, width: grid.clientWidth - 2 * gap, height: grid.clientHeight - 2 * gap };
    const placed = layoutRects(tiles.length, spotlightIndex, box, { gap, aspect: frameAspect, fill, stripSide });
    stripSide = placed.stripSide;
    placed.rects.forEach((rect, i) => tiles[i].place(rect));
    tiles.forEach((tile, i) => tile.setSpotlit(i === spotlightIndex));
    emptyEl.hidden = tiles.length > 0;
}

function onFrameSize(width, height) {
    const aspect = width / height;
    if (Math.abs(aspect - frameAspect) < 0.01) return;
    frameAspect = aspect;
    layoutTiles();
}

function claimCamera(claimed, match) {
    const camera = cameras.find((c) => !claimed.has(c.id) && match(c));
    if (camera) claimed.add(camera.id);
    return camera ?? null;
}

function resolveTiles() {
    const claimed = new Set();
    const resolved = new Map();
    for (const tile of tiles) {
        const camera = tile.bound ? cameras.find((c) => c.id === tile.id) : null;
        if (!camera) continue;
        claimed.add(camera.id);
        resolved.set(tile, camera);
    }
    for (const tile of tiles) {
        if (!resolved.get(tile) && tile.name) resolved.set(tile, claimCamera(claimed, (c) => c.name === tile.name));
    }
    for (const tile of tiles) {
        if (!resolved.get(tile) && !tile.bound && tile.id !== null) resolved.set(tile, claimCamera(claimed, (c) => c.id === tile.id));
    }
    for (const tile of tiles) {
        tile.setCameraList(cameras);
        tile.setCamera(resolved.get(tile) ?? null);
    }
    tray.setCameras(cameras);
}

async function sync() {
    try {
        cameras = await fetchCameras();
        setConnection(cameras.length > 0 ? '' : 'No cameras open in KSP yet.');
        resolveTiles();
    } catch {
        setConnection('Not connected to the game.', true);
    } finally {
        setTimeout(sync, CAMERA_SYNC_MS);
    }
}

function createTile(binding) {
    const tile = new LayoutTile(binding, {
        subscribe: (cameraId, onFrame) => hub.subscribe(cameraId, onFrame),
        onFrameSize,
        onPick: pickCamera,
        onSpotlight: (t) => toggleSpotlight(tiles.indexOf(t)),
        onRemove: removeTile,
        onGrab: grabTile,
    });
    tile.setCameraList(cameras);
    tiles.push(tile);
    grid.append(tile.el);
    return tile;
}

function sameBinding(tile, binding) {
    return binding.name ? tile.name === binding.name : binding.id !== null && tile.id === binding.id;
}

function applyLayout(layout) {
    const pool = tiles.splice(0);
    for (const binding of layout.tiles) {
        const reuse = pool.findIndex((tile) => sameBinding(tile, binding));
        if (reuse >= 0) tiles.push(...pool.splice(reuse, 1));
        else createTile(binding);
    }
    for (const tile of pool) tile.retire();
    spotlightIndex = layout.spotlight;
    fill = layout.fill;
    labels = layout.labels;
    applyFill();
    applyLabels();
    resolveTiles();
    layoutTiles();
}

function flashTile(tile) {
    tile.el.classList.add('drop-target');
    setTimeout(() => tile.el.classList.remove('drop-target'), FLASH_MS);
}

function addTile(camera = null) {
    change(() => createTile({}).assign(camera));
}

function putCamera(camera) {
    const empty = tiles.find((tile) => !tile.hasBinding);
    const tile = empty ?? createTile({});
    tile.assign(camera);
    return tile;
}

function placeCamera(camera) {
    const existing = tiles.find((tile) => tile.camera?.id === camera.id);
    if (existing) {
        flashTile(existing);
        return;
    }
    let placed = null;
    change(() => { placed = putCamera(camera); });
    flashTile(placed);
}

function addAllCameras() {
    const missing = cameras.filter((camera) => !tiles.some((tile) => tile.camera?.id === camera.id));
    if (cameras.length === 0) toast('No cameras open in KSP yet');
    else if (missing.length === 0) toast('Every camera is already in the layout');
    else change(() => missing.forEach(putCamera), `Added ${missing.length} camera${missing.length === 1 ? '' : 's'}`);
}

function clearLayout() {
    if (tiles.length === 0) return;
    const scope = store.kind === 'saved' ? `"${store.name}" for every screen showing it` : 'this layout';
    if (!confirm(`Remove every tile from ${scope}?`)) return;
    change(() => {
        for (const tile of tiles.splice(0)) tile.retire();
        spotlightIndex = null;
    }, 'Layout cleared');
}

function pickCamera(tile, cameraId) {
    change(() => tile.assign(cameras.find((camera) => camera.id === cameraId) ?? null));
}

function toggleSpotlight(index) {
    if (index < 0 || index >= tiles.length) return;
    change(() => { spotlightIndex = spotlightIndex === index ? null : index; });
}

function removeTile(tile) {
    change(() => {
        const index = tiles.indexOf(tile);
        tiles.splice(index, 1);
        tile.retire();
        if (spotlightIndex === index) spotlightIndex = null;
        else if (spotlightIndex !== null && spotlightIndex > index) spotlightIndex--;
    }, `Removed ${tile.label || 'an empty tile'}`);
}

function swapTiles(a, b) {
    change(() => {
        const i = tiles.indexOf(a);
        const j = tiles.indexOf(b);
        [tiles[i], tiles[j]] = [tiles[j], tiles[i]];
    }, 'Tiles swapped');
}

function findTile(el) {
    return tiles.find((tile) => tile.el === el) ?? null;
}

function grabTile(tile, e) {
    if (programMode) return;
    beginDrag(e, {
        source: tile,
        label: tile.label || 'Empty tile',
        findTile,
        onDrop: (target) => { if (target) swapTiles(tile, target); },
    });
}

function grabCamera(camera, e) {
    beginDrag(e, {
        label: camera.name,
        findTile,
        onClick: () => placeCamera(camera),
        onDrop: (target) => {
            if (!target) {
                addTile(camera);
                return;
            }
            const replaced = target.camera && target.camera.id !== camera.id ? target.label : null;
            change(() => target.assign(camera), replaced ? `Replaced ${replaced}` : null);
            flashTile(target);
        },
    });
}

function applyFill() {
    document.body.classList.toggle('layout-fill', fill);
    fillBtn.setAttribute('aria-pressed', String(fill));
    layoutTiles();
}

function toggleFill() {
    change(() => {
        fill = !fill;
        applyFill();
    });
}

function applyLabels() {
    for (const mode of LABEL_MODES) document.body.classList.toggle(`labels-${mode}`, labels === mode);
    document.getElementById('layout-labels-text').textContent = LABEL_TEXT[labels];
    labelsBtn.setAttribute('aria-label', LABEL_TEXT[labels]);
}

function cycleLabels() {
    change(() => {
        labels = LABEL_MODES[(LABEL_MODES.indexOf(labels) + 1) % LABEL_MODES.length];
        applyLabels();
    });
}

function pageLink(query, origin = location.origin) {
    return `${origin}${location.pathname}?${query}`;
}

function shareLink(origin) {
    return pageLink(store.shareQuery(currentLayout()), origin);
}

async function lanOrigin() {
    return new URL(lanUrl(await getSession(), '/')).origin;
}

function toggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen().catch(() => { });
}

function isBusy() {
    return isDragging() || document.activeElement?.tagName === 'SELECT';
}

function watchStore() {
    store.onSaveState = onSaveState;
    store.watch(programMode ? applyProgramLayout : applyRemoteLayout, programMode ? () => false : isBusy);
}

async function openSavedLayout(name) {
    const next = savedLayoutStore(name);
    let layout;
    try {
        layout = await next.load();
    } catch {
        setNotice(`Could not load ${name} from the game.`, true);
        return false;
    }
    store.stop();
    store = next;
    watchStore();
    undoStack.length = 0;
    history.replaceState(null, '', `${location.pathname}?layout=${encodeURIComponent(name)}`);
    describeStore();
    saveState = next.missing ? SAVE_STATES.missing : null;
    renderStatus();
    applyLayout(layout);
    renderTake();
    return true;
}

function setOnAir(name) {
    onAirName = name;
    renderTake();
    switcherMenu.rerender();
}

async function takeOnAir(name) {
    try {
        setOnAir((await takeProgram(name)).layout ?? null);
        toast(`${name} is on air`);
    } catch {
        setNotice(`Could not put ${name} on air. Is it saved in the game?`, true);
    }
}

async function takeOffAir() {
    try {
        setOnAir((await clearProgram()).layout ?? null);
        toast('Nothing on air: the clean feed is black');
    } catch {
        setNotice('Could not take the layout off air.', true);
    }
}

async function takeSavedLayout(index) {
    const name = savedNames[index];
    if (!name) {
        toast(`No saved layout ${index + 1} yet`);
        return;
    }
    const isOpen = store.kind === 'saved' && store.name === name;
    if (isOpen || await openSavedLayout(name)) await takeOnAir(name);
}

function renderTake() {
    const saved = store.kind === 'saved';
    takeBtn.hidden = !saved;
    if (!saved) return;
    const live = onAirName === store.name;
    takeBtn.classList.toggle('on-air', live);
    takeBtn.textContent = live ? 'On air' : 'Take on air';
    takeBtn.title = live
        ? 'This layout is on the clean feed (layout.html?program)'
        : 'Show this layout on the clean feed (layout.html?program)';
}

async function refreshSavedNames() {
    try {
        savedNames = (await fetchLayouts()).map((entry) => entry.name);
    } catch {
        savedNames = [];
    }
    switcherMenu.rerender();
}

function saveAsForm(menu) {
    const input = h('input', {
        class: 'input',
        type: 'text',
        maxlength: '64',
        placeholder: 'New layout name',
        'aria-label': 'New layout name',
    });
    const form = h('form', { class: 'menu-form' }, input, button({ label: 'Save', className: 'btn btn-primary' }));
    form.querySelector('button').type = 'submit';
    input.addEventListener('input', () => input.setCustomValidity(''));
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const name = input.value.trim();
        if (!LAYOUT_NAME_PATTERN.test(name)) {
            input.setCustomValidity('Use letters, digits, spaces, - and _ (64 at most).');
            input.reportValidity();
            return;
        }
        if (savedNames.includes(name) && !confirm(`Replace the saved layout "${name}"?`)) return;
        try {
            await saveLayoutAs(name, currentLayout());
            menu.close();
            await openSavedLayout(name);
            refreshSavedNames();
            toast(`Saved as ${name}`);
        } catch {
            setNotice('Could not save this layout in the game.', true);
        }
    });
    return form;
}

async function deleteCurrentLayout() {
    if (!confirm(`Delete the saved layout "${store.name}"? It is taken off air if it is on air.`)) return;
    try {
        await deleteLayout(store.name);
        location.assign(location.pathname);
    } catch {
        setNotice('Could not delete this layout.', true);
    }
}

function lanMenuItem(label, what, link) {
    const item = menuItem({ label, onSelect: async () => copyWithToast(link(await lanOrigin()), what) });
    item.hidden = true;
    getSession().then((session) => { item.hidden = !lanUrl(session, '/'); });
    return item;
}

function savedLayoutItems() {
    if (savedNames.length === 0) return [h('div', { class: 'menu-heading' }, 'None yet: save this one below.')];
    return savedNames.map((name, i) => menuItem({
        label: name,
        detail: i < SHORTCUT_LAYOUTS ? `Shift ${i + 1}` : null,
        className: [store.kind === 'saved' && store.name === name ? 'current' : '', name === onAirName ? 'is-on-air' : ''].join(' '),
        onSelect: () => openSavedLayout(name),
    }));
}

const switcherMenu = new Menu(switcherBtn, (menu) => {
    const items = [menuHeading('Saved in the game (shared by every device)'), ...savedLayoutItems(), saveAsForm(menu)];
    items.push(menuSeparator(), menuHeading('Clean feed for OBS (shows the layout on air)'));
    items.push(menuItem({ label: 'Open the clean feed', href: pageLink('program'), target: '_blank' }));
    items.push(menuItem({ label: 'Copy clean feed link', onSelect: () => copyWithToast(pageLink('program'), 'Clean feed link') }));
    items.push(lanMenuItem('Copy clean feed link for other devices', 'Network clean feed link', (origin) => pageLink('program', origin)));
    if (onAirName) items.push(menuItem({ label: `Take ${onAirName} off air`, onSelect: takeOffAir }));
    items.push(menuSeparator(), menuHeading('This layout'));
    items.push(menuItem({
        label: "This browser's own layout",
        href: location.pathname,
        className: store.kind === 'local' ? 'current' : '',
    }));
    items.push(menuItem({ label: 'Copy link', onSelect: () => copyWithToast(shareLink()) }));
    items.push(lanMenuItem('Copy link for other devices', 'Network link', (origin) => shareLink(origin)));
    if (store.kind === 'saved') items.push(menuItem({ label: 'Delete this saved layout', className: 'danger', onSelect: deleteCurrentLayout }));
    return items;
});

const helpMenu = new Menu(document.getElementById('layout-help'), () => [
    menuHeading('Keyboard and mouse'),
    h('div', { class: 'shortcuts' },
        ...SHORTCUTS.flatMap(([key, text]) => [h('kbd', {}, key), h('span', {}, text)])),
]);

function chromeInUse() {
    return tiles.length === 0
        || tray.isOpen
        || isMenuOpen()
        || isDragging()
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

const KEY_ACTIONS = {
    c: () => tray.toggle(),
    t: () => addTile(),
    g: toggleFill,
    n: cycleLabels,
    f: toggleFullscreen,
    '?': () => helpMenu.open(),
};

function onDigit(digit, shift) {
    if (shift) {
        if (digit > 0) takeSavedLayout(digit - 1);
    } else if (digit === 0) {
        if (spotlightIndex !== null) toggleSpotlight(spotlightIndex);
    } else {
        toggleSpotlight(digit - 1);
    }
}

function onKeyDown(e) {
    if (isTyping(e)) return;
    const key = e.key.toLowerCase();
    if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && key === 'z') {
        e.preventDefault();
        undo();
        return;
    }
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    const digit = DIGIT_CODE.exec(e.code)?.[1];
    if (digit !== undefined) {
        e.preventDefault();
        onDigit(Number(digit), e.shiftKey);
        return;
    }
    const action = KEY_ACTIONS[key];
    if (!action) return;
    e.preventDefault();
    action();
}

function describeStore() {
    const kinds = { saved: 'Saved', program: 'Clean feed' };
    document.getElementById('layout-kind').textContent = kinds[store.kind] ?? 'Layout';
    document.getElementById('layout-name').textContent = store.title;
    document.title = store.kind === 'saved' ? `${store.name} - JRTI Layout` : `${programMode ? 'Clean feed' : 'Layout'} - JRTI`;
}

function wireCommon() {
    document.addEventListener('fullscreenchange', () => {
        const full = Boolean(document.fullscreenElement);
        document.body.classList.toggle('layout-fullscreen', full);
        if (full) tray.close();
    });
    window.addEventListener('resize', onResize);
}

function wireProgram() {
    document.body.classList.add('program', 'idle');
    document.addEventListener('keydown', (e) => {
        if (e.key.toLowerCase() === 'f' && !e.ctrlKey && !e.metaKey && !e.altKey) toggleFullscreen();
    });
}

function wireControls() {
    document.getElementById('layout-add').addEventListener('click', () => addTile());
    document.getElementById('layout-copy').addEventListener('click', () => copyWithToast(shareLink()));
    document.getElementById('layout-tray-btn').addEventListener('click', () => tray.toggle());
    document.getElementById('layout-empty-tray').addEventListener('click', () => tray.open());
    document.getElementById('layout-empty-all').addEventListener('click', addAllCameras);
    document.getElementById('tray-add-all').addEventListener('click', addAllCameras);
    document.getElementById('tray-clear').addEventListener('click', clearLayout);
    takeBtn.addEventListener('click', () => { if (store.kind === 'saved' && onAirName !== store.name) takeOnAir(store.name); });
    fillBtn.addEventListener('click', toggleFill);
    labelsBtn.addEventListener('click', cycleLabels);
    fullscreenBtn.hidden = !document.fullscreenEnabled;
    fullscreenBtn.addEventListener('click', toggleFullscreen);
    switcherBtn.addEventListener('pointerdown', refreshSavedNames);

    for (const type of ['pointermove', 'pointerdown', 'keydown']) {
        window.addEventListener(type, showChrome, { passive: true });
    }
    document.addEventListener('keydown', onKeyDown);
    onServerEvent('program', (program) => setOnAir(program.layout ?? null));
    onServerEvent('layout', refreshSavedNames);
}

async function loadInitialLayout() {
    try {
        const layout = await store.load();
        if (store.missing) onSaveState('missing');
        return layout;
    } catch {
        setNotice('Could not load this layout from the game.', true);
        return EMPTY_LAYOUT;
    }
}

async function main() {
    describeStore();
    wireCommon();
    if (programMode) wireProgram();
    else wireControls();

    const layout = await loadInitialLayout();
    if (programMode) applyProgramLayout(layout);
    else applyLayout(layout);
    watchStore();

    if (!programMode) {
        showChrome();
        refreshSavedNames();
        renderTake();
        fetchProgram().then((program) => setOnAir(program.layout ?? null)).catch(() => { });
    }
    sync();
    setInterval(() => {
        const now = Date.now();
        for (const tile of tiles) tile.updateSignal(now);
    }, LAYOUT_SIGNAL_CHECK_MS);
}

main();
