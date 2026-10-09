import { API } from './config.js';

async function getJson(url, what) {
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) throw new Error(`${what} fetch failed: ${res.status}`);
    return res.json();
}

async function send(url, method, body, what) {
    const res = await fetch(url, {
        method,
        headers: body == null ? undefined : { 'Content-Type': 'application/json' },
        body: body == null ? undefined : JSON.stringify(body),
    });
    if (!res.ok) throw new Error(`${what} failed: ${res.status} ${await res.text()}`);
    return res;
}

export function fetchSession() {
    return getJson(API.session, 'session');
}

export function fetchCameras() {
    return getJson(API.cameras, 'cameras');
}

export async function checkStatus(cameraId) {
    try {
        const res = await fetch(API.status(cameraId));
        return { ok: res.ok, status: res.status };
    } catch {
        return { ok: false, status: 0 };
    }
}

export async function gameRecording(cameraId, action, codec) {
    const res = await send(API.gameRecording(cameraId, action, codec), 'POST', null, `in-game recording ${action}`);
    return res.json();
}

export function getCameraSettings(cameraId) {
    return getJson(API.settings(cameraId), 'settings');
}

export async function setCameraSettings(cameraId, settings) {
    await send(API.settings(cameraId), 'POST', settings, 'settings update');
}

export function fetchRecordings() {
    return getJson(API.recordings, 'recordings');
}

export function fetchProgram() {
    return getJson(API.program, 'program');
}

export async function takeProgram(name) {
    const res = await send(API.programTake(name), 'POST', null, 'take on air');
    return res.json();
}

export async function clearProgram() {
    const res = await send(API.programClear, 'POST', null, 'take off air');
    return res.json();
}

export function fetchLayouts() {
    return getJson(API.layouts, 'layouts');
}

export async function fetchLayout(name) {
    const res = await fetch(API.layout(name), { cache: 'no-store' });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`layout fetch failed: ${res.status}`);
    return res.text();
}

export async function saveLayout(name, layout) {
    await send(API.layout(name), 'PUT', layout, 'layout save');
}

export async function deleteLayout(name) {
    await send(API.layout(name), 'DELETE', null, 'layout delete');
}
