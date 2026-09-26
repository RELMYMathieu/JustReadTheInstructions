import { LOS_BEHAVIORS, RECORDERS, VIDEO_CODECS } from './config.js';
import {
    getSettings,
    updateSettings,
    isInGameRecordingAvailable,
    usesGameRecorder,
    getGameCodecs,
    selectedGameCodec,
} from './recorder-settings.js';

const LOS_LABELS = {
    [LOS_BEHAVIORS.AUTO_SAVE]: 'Auto-save recording',
    [LOS_BEHAVIORS.PAUSE]: 'Pause until signal returns',
    [LOS_BEHAVIORS.DISCARD]: 'Discard recording',
};

const RECORDER_LABELS = {
    [RECORDERS.GAME]: 'The game (graphics card, MP4)',
    [RECORDERS.BROWSER]: 'This browser (legacy, removed in v3.0.0)',
};

const CODEC_LABELS = {
    [VIDEO_CODECS.H264]: 'H.264 (default, plays everywhere)',
    [VIDEO_CODECS.AV1]: 'AV1 (advanced, limited support)',
};

const CODEC_WARNING =
    'Only change the codec if you know your video tools support it.\n\n' +
    'H.264 opens in every editor, player, phone and OBS setup. AV1 keeps more detail at the same file size, ' +
    'but many editors, older players and phones cannot open it. Without a graphics card that encodes AV1, ' +
    'the CPU does the work and recordings may drop frames.\n\n' +
    'Switch anyway?';

function show() {
    document.getElementById('settings-modal')?.classList.add('visible');
}

function hide() {
    const el = document.getElementById('settings-modal');
    if (!el) return;
    el.classList.add('modal-backdrop--closing');
    setTimeout(() => el.classList.remove('visible', 'modal-backdrop--closing'), 180);
}

function renderOptions(select, labels, selected) {
    select.innerHTML = '';
    for (const [value, label] of Object.entries(labels)) {
        const opt = document.createElement('option');
        opt.value = value;
        opt.textContent = label;
        if (value === selected) opt.selected = true;
        select.appendChild(opt);
    }
}

function availableCodecLabels() {
    return Object.fromEntries(getGameCodecs().map((codec) => [codec, CODEC_LABELS[codec]]));
}

function render(controls) {
    const { losBehavior, recorder } = getSettings();
    const codec = selectedGameCodec();
    renderOptions(controls.los, LOS_LABELS, losBehavior);
    renderOptions(controls.recorder, RECORDER_LABELS, recorder);
    renderOptions(controls.codec, availableCodecLabels(), codec);
    document.getElementById('settings-recorder-row').hidden = !isInGameRecordingAvailable();
    document.getElementById('settings-codec-row').hidden = !usesGameRecorder();
    document.getElementById('settings-codec-warning').hidden = !usesGameRecorder() || codec === VIDEO_CODECS.H264;
}

export function mountSettingsUI() {
    const openBtn = document.getElementById('settings-btn');
    const modal = document.getElementById('settings-modal');
    const closeBtn = document.getElementById('settings-close');
    const controls = {
        los: document.getElementById('settings-los'),
        recorder: document.getElementById('settings-recorder'),
        codec: document.getElementById('settings-codec'),
    };

    if (!openBtn || !modal || !closeBtn || Object.values(controls).some((el) => !el)) return;

    render(controls);

    openBtn.addEventListener('click', () => { render(controls); show(); });
    closeBtn.addEventListener('click', hide);

    modal.addEventListener('click', (e) => { if (e.target === modal) hide(); });

    controls.los.addEventListener('change', () => {
        updateSettings({ losBehavior: controls.los.value });
    });

    controls.recorder.addEventListener('change', () => {
        updateSettings({ recorder: controls.recorder.value });
        render(controls);
    });

    controls.codec.addEventListener('change', () => {
        const codec = controls.codec.value;
        if (codec !== VIDEO_CODECS.H264 && !confirm(CODEC_WARNING)) {
            controls.codec.value = selectedGameCodec();
            return;
        }
        updateSettings({ codec });
        render(controls);
    });

    document.addEventListener('keydown', (e) => {
        if (modal.classList.contains('visible') && e.key === 'Escape') hide();
    });
}
