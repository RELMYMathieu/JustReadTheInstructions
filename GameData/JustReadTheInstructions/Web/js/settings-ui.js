import { LOS_BEHAVIORS, RECORDERS, VIDEO_CODECS } from './config.js';
import {
    getSettings,
    updateSettings,
    isInGameRecordingAvailable,
    usesGameRecorder,
    getGameCodecs,
    selectedGameCodec,
} from './recorder-settings.js';
import { Sheet } from './ui.js';

const LOS_LABELS = {
    [LOS_BEHAVIORS.AUTO_SAVE]: 'Save what was recorded so far',
    [LOS_BEHAVIORS.PAUSE]: 'Pause until the signal returns',
    [LOS_BEHAVIORS.DISCARD]: 'Discard the recording',
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

function renderOptions(select, labels, selected) {
    select.replaceChildren(...Object.entries(labels).map(([value, label]) => {
        const opt = new Option(label, value);
        opt.selected = value === selected;
        return opt;
    }));
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
    const sheetEl = document.getElementById('settings-sheet');
    const controls = {
        los: document.getElementById('settings-los'),
        recorder: document.getElementById('settings-recorder'),
        codec: document.getElementById('settings-codec'),
    };

    if (!openBtn || !sheetEl || Object.values(controls).some((el) => !el)) return;

    const sheet = new Sheet(sheetEl, { onOpen: () => render(controls) });
    render(controls);

    openBtn.addEventListener('click', () => sheet.toggle(openBtn));

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
}
