import { LOS_BEHAVIORS, RECORDERS } from './config.js';
import { getSettings, updateSettings, isInGameRecordingAvailable } from './recorder-settings.js';

const LOS_LABELS = {
    [LOS_BEHAVIORS.AUTO_SAVE]: 'Auto-save recording',
    [LOS_BEHAVIORS.PAUSE]: 'Pause until signal returns',
    [LOS_BEHAVIORS.DISCARD]: 'Discard recording',
};

const RECORDER_LABELS = {
    [RECORDERS.GAME]: 'The game (graphics card, MP4)',
    [RECORDERS.BROWSER]: 'This browser (legacy, removed in v3.0.0)',
};

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

function render(losSelect, recorderSelect) {
    const { losBehavior, recorder } = getSettings();
    renderOptions(losSelect, LOS_LABELS, losBehavior);
    renderOptions(recorderSelect, RECORDER_LABELS, recorder);
    document.getElementById('settings-recorder-row').hidden = !isInGameRecordingAvailable();
}

export function mountSettingsUI() {
    const openBtn = document.getElementById('settings-btn');
    const modal = document.getElementById('settings-modal');
    const closeBtn = document.getElementById('settings-close');
    const losSelect = document.getElementById('settings-los');
    const recorderSelect = document.getElementById('settings-recorder');

    if (!openBtn || !modal || !closeBtn || !losSelect || !recorderSelect) return;

    render(losSelect, recorderSelect);

    openBtn.addEventListener('click', () => { render(losSelect, recorderSelect); show(); });
    closeBtn.addEventListener('click', hide);

    modal.addEventListener('click', (e) => { if (e.target === modal) hide(); });

    losSelect.addEventListener('change', () => {
        updateSettings({ losBehavior: losSelect.value });
    });

    recorderSelect.addEventListener('change', () => {
        updateSettings({ recorder: recorderSelect.value });
    });

    document.addEventListener('keydown', (e) => {
        if (modal.classList.contains('visible') && e.key === 'Escape') hide();
    });
}