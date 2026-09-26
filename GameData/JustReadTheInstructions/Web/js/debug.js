import { API, DEBUG_POLL_MS, DEBUG_HISTORY_SAMPLES, DEBUG_REQUEST_TIMEOUT_MS } from './config.js';

const CAMERA_COLORS = ['#58a6ff', '#3fb950', '#d29922', '#f85149', '#bc8cff', '#39c5cf', '#ff9bce', '#e3b341'];
const FRAME_SERIES = [
    { label: 'Frame avg', key: 'frame_ms_avg', color: '#58a6ff' },
    { label: 'Frame max', key: 'frame_ms_max', color: '#6e7681' },
    { label: 'JRTI main thread avg', key: 'jrti_ms_avg', color: '#d29922' },
];

const fmt = (value, digits = 1) => Number(value).toFixed(digits);
const streamFrameMs = (s) => 1000 / Math.max(1, s.max_fps);
const hasViewers = (c) => c.stream_clients + c.preview_clients > 0;

const TILES = [
    {
        label: 'Game FPS',
        value: s => fmt(s.fps),
        detail: s => `frame ${fmt(s.frame_ms_avg)} ms, max ${fmt(s.frame_ms_max)}`,
    },
    {
        label: 'JRTI main thread',
        value: s => `${fmt(s.jrti_ms_avg, 2)} ms`,
        detail: s => `per frame, max ${fmt(s.jrti_ms_max, 2)}`,
    },
    {
        label: 'Garbage collections',
        value: s => `${fmt(s.gc_per_s)}/s`,
        detail: s => `worst GC frame ${fmt(s.gc_frame_ms_max, 0)} ms`,
        warn: s => s.gc_per_s > 0 && s.gc_frame_ms_max > 2 * s.frame_ms_avg
            ? 'A garbage collection caused a frame over twice the average frame time'
            : null,
    },
    {
        label: 'Managed memory',
        value: s => `${fmt(s.heap_mb, 0)} MB`,
    },
    {
        label: 'Worker threads busy',
        value: s => `${s.pool_busy}`,
        detail: s => `min ${s.pool_min}, IO ${s.pool_io_busy}`,
        warn: s => s.pool_busy >= s.pool_min
            ? 'Every ready worker thread is busy: new encodes and requests wait for the pool to add threads'
            : null,
    },
    {
        label: 'Clients',
        value: s => `${s.stream_clients} + ${s.preview_clients}`,
        detail: () => 'stream + preview',
    },
    {
        label: 'Recordings',
        value: s => `${s.recordings}`,
        detail: s => `${fmt(s.recording_kbps, 0)} KB/s received`,
    },
    {
        label: 'Capture',
        value: s => `${s.max_fps} FPS`,
        detail: s => (s.spread ? 'spread across frames' : 'not spread'),
    },
];

const COLUMNS = [
    { label: 'Camera', value: c => c.camera, className: 'debug-name' },
    { label: 'Mode', value: c => c.mode },
    { label: 'Renders/s', value: c => fmt(c.renders_per_s, 0) },
    {
        label: 'Render ms',
        title: 'Average / max main-thread time per render, capture included',
        value: c => `${fmt(c.render_ms_avg)} / ${fmt(c.render_ms_max)}`,
    },
    {
        label: 'Stream FPS',
        value: c => fmt(c.stream_fps, 0),
        warn: (c, s) => hasViewers(c) && c.stream_fps < s.max_fps * 0.9 ? 'Delivering under 90% of Max FPS' : null,
    },
    { label: 'Capture ms', title: 'Main-thread time to start a capture', value: c => fmt(c.capture_ms_avg, 2) },
    {
        label: 'Readback ms',
        title: 'Average / max time for the GPU to hand the frame back',
        value: c => `${fmt(c.readback_ms_avg, 0)} / ${fmt(c.readback_ms_max, 0)}`,
    },
    { label: 'Copy ms', title: 'Main-thread time to copy the frame out of the readback', value: c => fmt(c.readback_copy_ms_avg, 2) },
    {
        label: 'Encode wait ms',
        title: 'Average / max time a frame waits for a free worker thread before encoding',
        value: c => `${fmt(c.encode_wait_ms_avg)} / ${fmt(c.encode_wait_ms_max)}`,
        warn: (c, s) => c.encode_wait_ms_avg > streamFrameMs(s)
            ? 'Frames wait longer than one stream frame for a free worker thread'
            : null,
    },
    {
        label: 'Encode ms',
        value: c => `${fmt(c.encode_ms_avg)} / ${fmt(c.encode_ms_max)}`,
        warn: (c, s) => c.encode_ms_avg > streamFrameMs(s) ? 'Encoding a frame takes longer than one stream frame' : null,
    },
    { label: 'Frame KB', value: c => fmt(c.jpeg_kb_avg, 0) },
    { label: 'Deferred/s', title: 'Captures pushed to a later frame by capture spreading', value: c => fmt(c.deferred_per_s) },
    { label: 'Clients', title: 'Stream + preview', value: c => `${c.stream_clients} + ${c.preview_clients}` },
];

const history = [];
const cameraColors = new Map();
let lastUtc = null;

function setStatus(message) {
    const el = document.getElementById('debug-status');
    el.textContent = message ?? '';
    el.classList.toggle('visible', Boolean(message));
    document.getElementById('debug-content').hidden = Boolean(message) && history.length === 0;
}

function cameraColor(id) {
    if (!cameraColors.has(id))
        cameraColors.set(id, CAMERA_COLORS[cameraColors.size % CAMERA_COLORS.length]);
    return cameraColors.get(id);
}

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
}

function markWarning(node, reason) {
    if (!reason) return;
    node.classList.add('debug-warn');
    node.title = reason;
}

function renderTiles(s) {
    const container = document.getElementById('debug-tiles');
    container.replaceChildren(...TILES.map(tile => {
        const node = el('div', 'debug-tile');
        node.append(el('div', 'debug-tile-label', tile.label), el('div', 'debug-tile-value', tile.value(s)));
        if (tile.detail) node.append(el('div', 'debug-tile-detail', tile.detail(s)));
        markWarning(node, tile.warn?.(s));
        return node;
    }));
}

function renderTable(s) {
    const table = document.getElementById('debug-table');
    const head = el('tr');
    for (const column of COLUMNS) {
        const th = el('th', null, column.label);
        if (column.title) th.title = column.title;
        head.append(th);
    }

    const rows = s.cameras.map(c => {
        const row = el('tr');
        for (const column of COLUMNS) {
            const td = el('td', column.className, column.value(c));
            markWarning(td, column.warn?.(c, s));
            row.append(td);
        }
        row.firstChild.style.borderLeftColor = cameraColor(c.camera_id);
        return row;
    });

    if (rows.length === 0) {
        const empty = el('tr');
        const td = el('td', 'debug-empty', 'No open cameras');
        td.colSpan = COLUMNS.length;
        empty.append(td);
        rows.push(empty);
    }

    table.replaceChildren(el('thead'), el('tbody'));
    table.tHead.append(head);
    table.tBodies[0].append(...rows);
}

function niceCeiling(value) {
    const magnitude = 10 ** Math.floor(Math.log10(value));
    const step = [1, 2, 5, 10].find(m => m * magnitude >= value);
    return step * magnitude;
}

function drawChart(canvas, series, reference) {
    const dpr = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);

    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);

    const values = series.flatMap(s => s.values.filter(v => v != null));
    if (reference != null) values.push(reference);
    const yMax = niceCeiling(Math.max(1, ...values));

    const pad = { left: 34, right: 6, top: 6, bottom: 6 };
    const plotW = width - pad.left - pad.right;
    const plotH = height - pad.top - pad.bottom;
    const x = i => pad.left + (i / (DEBUG_HISTORY_SAMPLES - 1)) * plotW;
    const y = v => pad.top + plotH - (v / yMax) * plotH;

    const styles = getComputedStyle(document.documentElement);
    ctx.font = '10px ui-monospace, Consolas, monospace';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.lineWidth = 1;
    for (const tick of [0, yMax / 2, yMax]) {
        ctx.strokeStyle = styles.getPropertyValue('--border');
        ctx.beginPath();
        ctx.moveTo(pad.left, y(tick));
        ctx.lineTo(width - pad.right, y(tick));
        ctx.stroke();
        ctx.fillStyle = styles.getPropertyValue('--text-muted');
        ctx.fillText(String(Math.round(tick * 10) / 10), pad.left - 4, y(tick));
    }

    if (reference != null) {
        ctx.setLineDash([4, 4]);
        ctx.strokeStyle = styles.getPropertyValue('--text-dim');
        ctx.beginPath();
        ctx.moveTo(pad.left, y(reference));
        ctx.lineTo(width - pad.right, y(reference));
        ctx.stroke();
        ctx.setLineDash([]);
    }

    ctx.lineWidth = 1.5;
    for (const s of series) {
        const offset = DEBUG_HISTORY_SAMPLES - s.values.length;
        ctx.strokeStyle = s.color;
        ctx.beginPath();
        let drawing = false;
        s.values.forEach((v, i) => {
            if (v == null) { drawing = false; return; }
            if (drawing) ctx.lineTo(x(offset + i), y(v));
            else ctx.moveTo(x(offset + i), y(v));
            drawing = true;
        });
        ctx.stroke();
    }
}

function renderLegend(id, series) {
    document.getElementById(id).replaceChildren(...series.map(s => {
        const item = el('span', 'debug-legend-item', s.label);
        item.style.setProperty('--swatch', s.color);
        return item;
    }));
}

function cameraSeries() {
    const latestNames = new Map();
    for (const sample of history)
        for (const c of sample.cameras) latestNames.set(c.camera_id, c.camera);

    return [...latestNames].map(([id, name]) => ({
        label: name,
        color: cameraColor(id),
        values: history.map(sample => sample.cameras.find(c => c.camera_id === id)?.stream_fps ?? null),
    }));
}

function renderCharts() {
    if (history.length === 0) return;
    const latest = history[history.length - 1];

    const frameSeries = FRAME_SERIES.map(s => ({ ...s, values: history.map(sample => sample[s.key]) }));
    drawChart(document.getElementById('chart-frame'), frameSeries, null);
    renderLegend('legend-frame', frameSeries);

    const fpsSeries = cameraSeries();
    drawChart(document.getElementById('chart-fps'), fpsSeries, latest.max_fps);
    renderLegend('legend-fps', [...fpsSeries, { label: 'Max FPS', color: 'var(--text-dim)' }]);
}

function render(s) {
    if (s.utc !== lastUtc) {
        lastUtc = s.utc;
        history.push(s);
        if (history.length > DEBUG_HISTORY_SAMPLES) history.shift();
    }

    renderTiles(s);
    renderTable(s);
    renderCharts();
    document.getElementById('debug-updated').textContent = `Updated ${new Date(s.utc).toLocaleTimeString()}`;
}

async function poll() {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), DEBUG_REQUEST_TIMEOUT_MS);
    try {
        const res = await fetch(API.debugStats, { cache: 'no-store', signal: controller.signal });
        if (res.status === 503) {
            setStatus('Waiting for the first sample. Stats are only collected in the flight scene.');
            return;
        }
        if (!res.ok) throw new Error(`stats fetch failed: ${res.status}`);
        render(await res.json());
        setStatus(null);
    } catch {
        setStatus('Not connected to the game.');
    } finally {
        clearTimeout(timeout);
        setTimeout(poll, DEBUG_POLL_MS);
    }
}

window.addEventListener('resize', renderCharts);
poll();
