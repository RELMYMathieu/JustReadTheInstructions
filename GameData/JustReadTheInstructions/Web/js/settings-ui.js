import { VIDEO_CODECS } from './config.js';
import { updateSettings, isInGameRecordingAvailable, getGameCodecs, selectedGameCodec } from './recorder-settings.js';
import { Sheet } from './ui.js';

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
    const codec = selectedGameCodec();
    renderOptions(controls.codec, availableCodecLabels(), codec);
    document.getElementById('settings-codec-row').hidden = !isInGameRecordingAvailable();
    document.getElementById('settings-unavailable').hidden = isInGameRecordingAvailable();
    document.getElementById('settings-codec-warning').hidden = codec === VIDEO_CODECS.H264;
}

export function mountSettingsUI() {
    const openBtn = document.getElementById('settings-btn');
    const sheetEl = document.getElementById('settings-sheet');
    const controls = {
        codec: document.getElementById('settings-codec'),
    };

    if (!openBtn || !sheetEl || Object.values(controls).some((el) => !el)) return;

    const sheet = new Sheet(sheetEl, { onOpen: () => render(controls) });
    render(controls);

    openBtn.addEventListener('click', () => sheet.toggle(openBtn));

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
