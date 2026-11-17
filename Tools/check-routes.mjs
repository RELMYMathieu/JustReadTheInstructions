import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const OPENAPI_FILE = path.join(HERE, '..', 'docs', 'openapi.json');
const PORT = Number(process.env.CHECK_PORT || 8199);
const BASE = `http://localhost:${PORT}/api/v1`;
const START_TIMEOUT_MS = 60000;
const REQUEST_TIMEOUT_MS = 5000;
const UNKNOWN_ROUTE = 'Unknown route';

const SAMPLES = { id: '10', layout: 'RouteCheck', file: 'route-check.mp4', action: 'stop' };
const BODIES = { saveLayout: '{}', changeCameraSettings: '{}' };

function startMock() {
    const mock = spawn(process.execPath, [path.join(HERE, 'mock-server.mjs')], { env: { ...process.env, PORT: String(PORT), MOCK_KEY: '' } });
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('mock server did not start')), START_TIMEOUT_MS);
        mock.stdout.on('data', (chunk) => {
            if (!String(chunk).includes('mock JRTI on')) return;
            clearTimeout(timer);
            resolve(mock);
        });
        mock.on('exit', (code) => reject(new Error(`mock server exited with ${code}`)));
    });
}

function concretePath(template) {
    return template.replace(/\{(\w+)\}/g, (_, name) => encodeURIComponent(SAMPLES[name] ?? 'x'));
}

async function call(op) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
    try {
        const res = await fetch(BASE + concretePath(op.template).replace(/\/$/, ''), {
            method: op.method,
            body: BODIES[op.id],
            headers: BODIES[op.id] ? { 'Content-Type': 'application/json' } : undefined,
            signal: controller.signal,
        });
        const streaming = /multipart|event-stream|audio/.test(res.headers.get('content-type') ?? '');
        const body = streaming ? '' : await res.text();
        controller.abort();
        return { status: res.status, body };
    } finally {
        clearTimeout(timer);
    }
}

const spec = JSON.parse(fs.readFileSync(OPENAPI_FILE, 'utf8'));
const ops = Object.entries(spec.paths).flatMap(([template, methods]) =>
    Object.entries(methods).map(([method, op]) => ({ id: op.operationId, method: method.toUpperCase(), template })));

const mock = await startMock();
const missing = [];
try {
    for (const op of ops) {
        const { status, body } = await call(op);
        const known = !(status === 404 && body.startsWith(UNKNOWN_ROUTE));
        console.log(`${known ? 'ok     ' : 'MISSING'} ${status} ${op.method} ${op.template} (${op.id})`);
        if (!known) missing.push(op.id);
    }
} finally {
    mock.kill();
}

if (missing.length) {
    console.error(`\nThe mock server has no route for: ${missing.join(', ')}`);
    process.exit(1);
}
console.log(`\nAll ${ops.length} API operations answer in the mock server`);
