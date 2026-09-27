import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(HERE, '..');
const WEB = path.join(ROOT, 'GameData', 'JustReadTheInstructions', 'Web');
const DOCS = path.join(ROOT, 'docs');
const STATE = path.join(HERE, '.mock');
const PORT = Number(process.env.PORT || 8099);
const FPS = 30;
const LOOP_FRAMES = 90;
const BOUNDARY = 'jrtiboundary';
const AUDIO_RATE = 48000;
const AUDIO_TICK_MS = 20;

const FEEDS = [
    [10, 'screenshot-3.png', 'crop=2320:1305:160:100'],
    [11, 'screenshot-1.png', 'crop=1646:926:1700:0'],
    [12, 'screenshot-4.png', 'crop=1280:720:0:0'],
    [13, 'screenshot-2.png', 'crop=1280:720:0:0'],
    [14, 'screenshot-1.png', 'crop=1100:619:60:80,hflip'],
];

function renderFrames(id, source, crop) {
    const dir = path.join(STATE, 'frames', String(id));
    if (fs.existsSync(dir) && fs.readdirSync(dir).length > 0) return dir;
    fs.mkdirSync(dir, { recursive: true });
    const pan = `${crop},scale=1408:792,crop=1280:720:'64+64*sin(2*PI*n/${LOOP_FRAMES})':'36+36*cos(2*PI*n/${LOOP_FRAMES})'`;
    const result = spawnSync('ffmpeg', ['-v', 'error', '-y', '-loop', '1', '-i', path.join(DOCS, source), '-frames:v', String(LOOP_FRAMES), '-vf', pan, '-q:v', '6', path.join(dir, '%03d.jpg')]);
    if (result.status !== 0) fs.copyFileSync(path.join(DOCS, source), path.join(dir, 'still.png'));
    return dir;
}

const frames = new Map();
for (const [id, source, crop] of FEEDS) {
    const dir = renderFrames(id, source, crop);
    frames.set(id, fs.readdirSync(dir).sort().map((f) => fs.readFileSync(path.join(dir, f))));
}

const cameras = new Map([
    [10, { name: 'Atlantis.Aerocam Booster' }],
    [11, { name: 'Sally-Hut 1.Aerocam180' }],
    [12, { name: 'Sally-Hut 1.Nadir' }],
    [13, { name: 'Kerbal X.Booster Sep' }],
    [14, { name: 'Kerbal X.Docking Port Cam with a rather long name for overflow' }],
]);
for (const [id, cam] of cameras) Object.assign(cam, { id, online: true, streamClients: 0, previewClients: 0, recording: null, settings: { brightness: 0, contrast: 1, gamma: 1, fov: 60, fovMin: 20, fovMax: 90 } });

const launchId = 'mock' + Date.now().toString(16);
const layouts = new Map();
const eventClients = new Set();
const program = { layout: null, version: 0 };

function eventMessage(event, data) {
    return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

function broadcast(event, data) {
    for (const res of eventClients) res.write(eventMessage(event, data));
}

function setProgram(layout) {
    program.layout = layout;
    program.version++;
    broadcast('program', program);
}

function events(req, res) {
    res.writeHead(200, { 'Content-Type': 'text/event-stream; charset=utf-8', 'Cache-Control': 'no-cache' });
    res.write(`retry: 2000\n\n${eventMessage('program', program)}`);
    eventClients.add(res);
    const ping = setInterval(() => res.write(': ping\n\n'), 15000);
    req.on('close', () => { clearInterval(ping); eventClients.delete(res); });
}

const recordingsDir = path.join(STATE, 'recordings');
fs.mkdirSync(recordingsDir, { recursive: true });
if (fs.readdirSync(recordingsDir).length === 0) {
    fs.writeFileSync(path.join(recordingsDir, 'Atlantis.Aerocam_Booster__cam10__2026-09-25_210455.mp4'), Buffer.alloc(1024 * 1024 * 4));
    fs.writeFileSync(path.join(recordingsDir, 'Sally-Hut_1.Nadir__cam12__2026-09-26_101002.mp4'), Buffer.alloc(1024 * 1024 * 3));
}

const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'application/javascript', '.png': 'image/png', '.svg': 'image/svg+xml', '.json': 'application/json', '.mp4': 'video/mp4', '.otf': 'font/otf' };

function frameFor(id) {
    const list = frames.get(id) ?? frames.get(10);
    return list[Math.floor(Date.now() / (1000 / FPS)) % list.length];
}

function json(res, value, status = 200) {
    const body = JSON.stringify(value);
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': Buffer.byteLength(body) });
    res.end(body);
}

function text(res, status, message) {
    res.writeHead(status, { 'Content-Type': 'text/plain' });
    res.end(message);
}

function readBody(req) {
    return new Promise((resolve) => {
        const chunks = [];
        req.on('data', (c) => chunks.push(c));
        req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    });
}

function recordingInfo(cam) {
    if (!cam.recording) return null;
    const r = cam.recording;
    const elapsed = (r.paused ? r.pausedAt : Date.now()) - r.startedAt;
    return { file: r.file, paused: r.paused, elapsedMs: elapsed, bytes: Math.floor(elapsed * 120), framesWritten: Math.floor(elapsed / 33), framesDropped: 0, error: null };
}

function cameraList() {
    return [...cameras.values()].filter((c) => c.online).map((c) => ({
        id: c.id, name: c.name, streaming: true, viewerCount: c.streamClients,
        snapshotUrl: `/camera/${c.id}/snapshot`, streamUrl: `/viewer.html?id=${c.id}`, recording: recordingInfo(c),
    }));
}

function writePart(res, jpeg, id) {
    res.write(`--${BOUNDARY}\r\nContent-Type: image/jpeg\r\nContent-Length: ${jpeg.length}\r\n${id != null ? `X-Camera-Id: ${id}\r\n` : ''}\r\n`);
    res.write(jpeg);
    res.write('\r\n');
}

function stream(req, res, ids, preview) {
    const cams = ids.map((id) => cameras.get(id)).filter((c) => c?.online);
    if (cams.length === 0) return text(res, 404, 'No matching cameras');
    res.writeHead(200, { 'Content-Type': `multipart/x-mixed-replace; boundary=${BOUNDARY}`, 'Cache-Control': 'no-cache' });
    const key = preview ? 'previewClients' : 'streamClients';
    for (const c of cams) c[key]++;
    const multi = ids.length > 1 || req.url.startsWith('/streams');
    const timer = setInterval(() => {
        let any = false;
        for (const c of cams) {
            if (!c.online) continue;
            any = true;
            writePart(res, frameFor(c.id), multi ? c.id : null);
        }
        if (!any) { clearInterval(timer); res.end(); }
    }, 1000 / FPS);
    const done = () => { clearInterval(timer); for (const c of cams) c[key]--; };
    req.on('close', done);
}

function wavStreamHeader() {
    const header = Buffer.alloc(44);
    header.write('RIFF', 0);
    header.writeUInt32LE(0xffffffff, 4);
    header.write('WAVEfmt ', 8);
    header.writeUInt32LE(16, 16);
    header.writeUInt16LE(1, 20);
    header.writeUInt16LE(2, 22);
    header.writeUInt32LE(AUDIO_RATE, 24);
    header.writeUInt32LE(AUDIO_RATE * 4, 28);
    header.writeUInt16LE(4, 32);
    header.writeUInt16LE(16, 34);
    header.write('data', 36);
    header.writeUInt32LE(0xffffffff, 40);
    return header;
}

function audio(req, res, cam) {
    res.writeHead(200, { 'Content-Type': 'audio/wav', 'Cache-Control': 'no-cache' });
    res.write(wavStreamHeader());
    const pitch = 40 + (cam.id % 5) * 12;
    const startedAt = performance.now();
    let frame = 0;
    let rumble = 0;
    const timer = setInterval(() => {
        const due = Math.floor((performance.now() - startedAt) * AUDIO_RATE / 1000);
        const frames = due - frame;
        if (frames <= 0) return;
        const block = Buffer.alloc(frames * 4);
        for (let i = 0; i < frames; i++, frame++) {
            const t = frame / AUDIO_RATE;
            rumble = rumble * 0.985 + (Math.random() * 2 - 1) * 0.015;
            const sample = Math.sin(2 * Math.PI * pitch * t) * 0.15 + rumble * 2;
            const pan = Math.sin(2 * Math.PI * 0.1 * t);
            block.writeInt16LE(Math.round(sample * (1 - pan) * 0.5 * 32767), i * 4);
            block.writeInt16LE(Math.round(sample * (1 + pan) * 0.5 * 32767), i * 4 + 2);
        }
        res.write(block);
    }, AUDIO_TICK_MS);
    req.on('close', () => clearInterval(timer));
}

function sample() {
    const cams = [...cameras.values()].filter((c) => c.online);
    return {
        utc: new Date().toISOString(), fps: 58 + Math.random() * 4, frame_ms_avg: 16.8 + Math.random(), frame_ms_max: 24 + Math.random() * 6,
        jrti_ms_avg: 2.1 + Math.random(), jrti_ms_max: 5 + Math.random() * 3, gc_per_s: 0.2, gc_frame_ms_max: 31, heap_mb: 1840 + Math.random() * 20,
        pool_busy: 3, pool_min: 12, pool_io_busy: 1, stream_clients: cams.reduce((s, c) => s + c.streamClients, 0),
        preview_clients: cams.reduce((s, c) => s + c.previewClients, 0), recordings: cams.filter((c) => c.recording).length,
        recording_kbps: 0, spread: 1, max_fps: FPS, camera_count: cams.length,
        cameras: cams.map((c) => ({
            camera_id: c.id, camera: c.name, mode: c.id % 2 ? 'window' : 'stream', renders_per_s: 29.5 + Math.random(), render_ms_avg: 3 + Math.random() * 2, render_ms_max: 7 + Math.random() * 4,
            stream_fps: c.streamClients + c.previewClients > 0 ? 28 + Math.random() * 2 : 0, capture_ms_avg: 0.05, capture_ms_max: 0.1, readback_ms_avg: 21, readback_ms_max: 38,
            readback_copy_ms_avg: 0.4, encode_wait_ms_avg: 0.3, encode_wait_ms_max: 2, encode_ms_avg: 9.5, encode_ms_max: 14, jpeg_kb_avg: 96, deferred_per_s: 0.4,
            stream_clients: c.streamClients, preview_clients: c.previewClients,
        })),
    };
}

function listRecordings() {
    const active = new Set([...cameras.values()].filter((c) => c.recording).map((c) => c.recording.file));
    return fs.readdirSync(recordingsDir).filter((f) => /\.(mp4|webm|mkv)$/i.test(f)).map((file) => {
        const stat = fs.statSync(path.join(recordingsDir, file));
        return { file, bytes: stat.size, modified: stat.mtime.toISOString(), recording: active.has(file) };
    }).sort((a, b) => b.modified.localeCompare(a.modified));
}

function gameRecording(res, cam, action, codec) {
    if (action === 'start' && !cam.recording) {
        const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '_').slice(0, 15);
        const file = `${cam.name.replace(/[\\/:*?"<>|\s]+/g, '_')}__cam${cam.id}__${stamp}.mp4`;
        cam.recording = { file, startedAt: Date.now(), paused: false, codec };
        fs.writeFileSync(path.join(recordingsDir, file), Buffer.alloc(1024));
    } else if (action === 'stop' || action === 'discard') {
        if (cam.recording && action === 'discard') fs.rmSync(path.join(recordingsDir, cam.recording.file), { force: true });
        cam.recording = null;
    } else if (action === 'pause' && cam.recording && !cam.recording.paused) {
        Object.assign(cam.recording, { paused: true, pausedAt: Date.now() });
    } else if (action === 'resume' && cam.recording?.paused) {
        cam.recording.startedAt += Date.now() - cam.recording.pausedAt;
        cam.recording.paused = false;
    }
    json(res, recordingInfo(cam));
}

function serveFile(req, res, file, type, download) {
    const stat = fs.statSync(file);
    const headers = { 'Content-Type': type, 'Accept-Ranges': 'bytes', 'Cache-Control': 'no-cache' };
    if (download) headers['Content-Disposition'] = `attachment; filename="${path.basename(file)}"`;
    const range = /bytes=(\d*)-(\d*)/.exec(req.headers.range ?? '');
    if (range) {
        const start = range[1] ? Number(range[1]) : 0;
        const end = range[2] ? Number(range[2]) : stat.size - 1;
        res.writeHead(206, { ...headers, 'Content-Range': `bytes ${start}-${end}/${stat.size}`, 'Content-Length': end - start + 1 });
        fs.createReadStream(file, { start, end }).pipe(res);
        return;
    }
    res.writeHead(200, { ...headers, 'Content-Length': stat.size });
    fs.createReadStream(file).pipe(res);
}

const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://${req.headers.host}`);
    const p = url.pathname.replace(/\/+$/, '') || '/';
    const parts = p.split('/').filter(Boolean);
    try {
        if (p === '/session') return json(res, { launchId, inGameRecording: true, codecs: ['h264', 'av1'], version: '2.5.0', lanUrls: [`http://192.168.1.42:${PORT}/`] });
        if (p === '/cameras') return json(res, cameraList());
        if (p === '/debug/stats') return json(res, sample());
        if (p === '/events') return events(req, res);
        if (parts[0] === 'program') {
            if (parts.length === 1) return json(res, program);
            if (parts[1] === 'clear') { setProgram(null); return json(res, program); }
            if (parts[1] === 'take' && parts[2]) {
                const name = decodeURIComponent(parts[2]);
                if (!layouts.has(name)) return text(res, 404, `No saved layout named ${name}`);
                setProgram(name);
                return json(res, program);
            }
            return text(res, 404, 'Use GET /program, /program/take/<layout> or /program/clear');
        }
        if (p === '/streams') return stream(req, res, (url.searchParams.get('ids') ?? '').split(',').map(Number), url.searchParams.get('preview') === '1');
        if (parts[0] === 'mock' && parts[1] === 'toggle') {
            const cam = cameras.get(Number(parts[2]));
            if (cam) cam.online = !cam.online;
            return json(res, { online: cam?.online });
        }
        if (parts[0] === 'layouts') {
            if (parts.length === 1) return json(res, [...layouts].map(([name, v]) => ({ name, updated: v.updated })).sort((a, b) => a.name.localeCompare(b.name)));
            const name = decodeURIComponent(parts[1]);
            if (!/^[\p{L}\p{N} _-]{1,64}$/u.test(name)) return text(res, 400, 'Invalid layout name');
            if (req.method === 'GET') {
                const layout = layouts.get(name);
                if (!layout) return text(res, 404, 'Layout not found');
                res.writeHead(200, { 'Content-Type': 'application/json' });
                return res.end(layout.body);
            }
            if (req.method === 'PUT' || req.method === 'POST') {
                const body = await readBody(req);
                if (!body.trim().startsWith('{') || body.length > 65536) return text(res, 400, 'Expected a JSON object');
                layouts.set(name, { body, updated: new Date().toISOString() });
                broadcast('layout', { name });
                res.writeHead(204); return res.end();
            }
            if (req.method === 'DELETE') {
                layouts.delete(name);
                broadcast('layout', { name });
                if (program.layout === name) setProgram(null);
                res.writeHead(204); return res.end();
            }
            return text(res, 405, 'Method not allowed');
        }
        if (parts[0] === 'recordings') {
            if (parts.length === 1) return json(res, listRecordings());
            if (parts.length === 2) {
                const file = decodeURIComponent(parts[1]);
                const full = path.join(recordingsDir, path.basename(file));
                if (!fs.existsSync(full)) return text(res, 404, 'Recording not found');
                return serveFile(req, res, full, 'video/mp4', url.searchParams.get('download') === '1');
            }
            res.writeHead(200); return res.end();
        }
        if (parts[0] === 'camera') {
            const cam = cameras.get(Number(parts[1]));
            if (!cam || !cam.online) return text(res, 404, 'Camera not found');
            switch (parts[2]) {
                case 'snapshot': { const jpeg = frameFor(cam.id); res.writeHead(200, { 'Content-Type': 'image/jpeg', 'Content-Length': jpeg.length }); return res.end(jpeg); }
                case 'stream': return stream(req, res, [cam.id], false);
                case 'preview': return stream(req, res, [cam.id], true);
                case 'audio': return audio(req, res, cam);
                case 'status': return text(res, 200, 'ok');
                case 'settings':
                    if (req.method === 'POST') { Object.assign(cam.settings, JSON.parse(await readBody(req))); res.writeHead(200); return res.end(); }
                    return json(res, cam.settings);
                case 'recording': return gameRecording(res, cam, parts[3], url.searchParams.get('codec'));
                default: return text(res, 404, 'Unknown action');
            }
        }
        const rel = p === '/' ? 'index.html' : decodeURIComponent(p.slice(1));
        const full = path.join(WEB, rel);
        if (!full.startsWith(path.normalize(WEB)) || !fs.existsSync(full) || fs.statSync(full).isDirectory()) return text(res, 404, 'Not found');
        res.writeHead(200, { 'Content-Type': types[path.extname(full)] ?? 'application/octet-stream', 'Cache-Control': 'no-cache' });
        fs.createReadStream(full).pipe(res);
    } catch (err) {
        console.error(err);
        if (!res.headersSent) text(res, 500, String(err));
    }
});

server.listen(PORT, () => console.log(`mock JRTI on http://localhost:${PORT}/`));
