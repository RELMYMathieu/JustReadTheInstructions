import {
    VIEWER_STATUS_POLL_MS,
    VIEWER_RETRY_MS,
    VIEWER_LOS_DELAY_MS,
    LOS_IMAGE_URL,
    LOS_FALLBACK_IMAGE_URL,
    API,
} from './config.js';
import { checkStatus } from './api.js';

function getCameraId() {
    const params = new URLSearchParams(location.search);
    const id = params.get('id');
    if (!id) return null;
    const n = Number(id);
    return Number.isFinite(n) ? n : null;
}

function main() {
    const cameraId = getCameraId();
    const img = document.getElementById('viewer-img');
    if (!img || cameraId === null) {
        document.title = 'JRTI Stream - Invalid';
        return;
    }

    const base = API.stream(cameraId);
    img.src = base;
    document.title = `Camera ${cameraId} - JRTI Stream`;

    let offAt = 0;
    let losMode = false;

    const setLosImage = () => {
        if (losMode) return;
        losMode = true;
        img.onerror = null;
        img.src = LOS_IMAGE_URL;
        img.onerror = () => {
            img.onerror = null;
            img.src = LOS_FALLBACK_IMAGE_URL;
        };
    };

    const onError = () => {
        if (losMode) return;
        if (!offAt) offAt = Date.now();
        if (Date.now() - offAt >= VIEWER_LOS_DELAY_MS) {
            setLosImage();
        } else {
            setTimeout(() => { img.src = `${base}?r=${Date.now()}`; }, VIEWER_RETRY_MS);
        }
    };

    const onLoad = () => {
        if (losMode) return;
        if (img.src.includes(base)) {
            offAt = 0;
        } else if (offAt) {
            setTimeout(() => { img.src = `${base}?r=${Date.now()}`; }, VIEWER_RETRY_MS);
        }
    };

    img.addEventListener('error', onError);
    img.addEventListener('load', onLoad);

    setInterval(async () => {
        if (losMode) {
            const s = await checkStatus(cameraId);
            if (s.ok) location.reload();
            return;
        }
        const s = await checkStatus(cameraId);
        if (s.status === 404) {
            if (!offAt) offAt = Date.now();
            if (Date.now() - offAt >= VIEWER_LOS_DELAY_MS) setLosImage();
        } else if (s.ok) {
            offAt = 0;
        }
    }, VIEWER_STATUS_POLL_MS);
}

main();
