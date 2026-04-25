import { API } from './config.js';

export async function fetchCameras() {
    const res = await fetch(API.cameras);
    if (!res.ok) throw new Error(`cameras fetch failed: ${res.status}`);
    return res.json();
}

export async function checkStatus(cameraId) {
    try {
        const res = await fetch(API.status(cameraId));
        return { ok: res.ok, status: res.status };
    } catch {
        return { ok: false, status: 0 };
    }
}

export async function uploadRecordingChunk(sessionId, filename, blob, mimeType) {
    const res = await fetch(API.recordingAppend(sessionId, filename), {
        method: 'POST',
        headers: { 'Content-Type': mimeType },
        body: blob,
    });
    if (!res.ok) throw new Error(`upload chunk failed: ${res.status}`);
}

export function heartbeatRecording(sessionId, filename) {
    return fetch(API.recordingAppend(sessionId, filename), {
        method: 'POST',
        body: '',
    }).catch(() => { });
}

export async function finalizeRecording(sessionId, filename) {
    const res = await fetch(API.recordingFinalize(sessionId, filename), {
        method: 'POST',
        keepalive: true,
    });
    if (!res.ok) throw new Error(`finalize failed: ${res.status}`);
}

export function finalizeRecordingBeacon(sessionId, filename) {
    try {
        fetch(API.recordingFinalize(sessionId, filename), {
            method: 'POST',
            keepalive: true,
        }).catch(() => { });
    } catch { }
}

export async function getCameraSettings(cameraId) {
    const res = await fetch(API.settings(cameraId));
    if (!res.ok) throw new Error(`settings fetch failed: ${res.status}`);
    return res.json();
}

export async function setCameraSettings(cameraId, settings) {
    await fetch(API.settings(cameraId), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings),
    });
}

export function abortRecording(sessionId, filename) {
    return fetch(`/recordings/${sessionId}/abort?name=${encodeURIComponent(filename)}`, {
        method: 'POST',
        keepalive: true,
    }).catch(() => { });
}