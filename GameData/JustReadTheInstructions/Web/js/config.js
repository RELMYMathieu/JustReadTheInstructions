export const SNAPSHOT_REFRESH_MS = 10000;
export const CAMERA_SYNC_MS = 5000;
export const LOS_DELAY_MS = 3000;
export const VIEWER_STATUS_POLL_MS = 1000;
export const VIEWER_RETRY_MS = 2000;
export const VIEWER_LOS_DELAY_MS = 5000;

export const RECORDER_CHUNK_MS = 2000;
export const RECORDER_CAPTURE_FPS = 24;
export const RECORDER_VIDEO_BPS = 3_500_000;
export const RECORDER_HEARTBEAT_MS = 5000;
export const RECORDER_LOS_DELAY_MS = 5000;

export const LOS_IMAGE_URL = '/images/customlos.png';
export const LOS_FALLBACK_IMAGE_URL = '/images/los.png';
export const LOS_OVERLAY_HTML =
    `<img src="${LOS_IMAGE_URL}" onerror="if(this.dataset.fallbackDone)return;this.dataset.fallbackDone='1';this.src='${LOS_FALLBACK_IMAGE_URL}';" alt="Loss of Signal" style="max-width:100%;max-height:100%;object-fit:contain;">`;
export const WAITING_OVERLAY_HTML =
    '<span style="font-size:1.8rem;line-height:1">&#x25CE;</span><span>Waiting for frames...</span>';

export const API = Object.freeze({
    cameras: '/cameras',
    snapshot: (id) => `/camera/${id}/snapshot?t=${Date.now()}`,
    stream: (id) => `/camera/${id}/stream`,
    status: (id) => `/camera/${id}/status`,
    settings: (id) => `/camera/${id}/settings`,
    viewer: (id) => `/viewer.html?id=${id}`,
    recordingAppend: (sessionId, filename) =>
        `/recordings/${sessionId}/append?name=${encodeURIComponent(filename)}`,
    recordingFinalize: (sessionId, filename) =>
        `/recordings/${sessionId}/finalize?name=${encodeURIComponent(filename)}`,
});

export const LOS_BEHAVIORS = Object.freeze({
    AUTO_SAVE: 'auto_save',
    PAUSE: 'pause',
    DISCARD: 'discard',
});

export const DEFAULT_LOS_BEHAVIOR = LOS_BEHAVIORS.AUTO_SAVE;