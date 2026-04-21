import { LOS_BEHAVIORS } from './config.js';
import { getSettings, updateSettings } from './recorder-settings.js';

const LOS_LABELS = {
    [LOS_BEHAVIORS.AUTO_SAVE]: 'Auto-save recording',
    [LOS_BEHAVIORS.PAUSE]: 'Pause until signal returns',
    [LOS_BEHAVIORS.DISCARD]: 'Discard recording',
};

function show() {
    document.getElementById('settings-modal')?.classList.add('visible');
}

function hide() {
    document.getElementById('settings-modal')?.classList.remove('visible');
}

function renderOptions(select) {
    const { losBehavior } = getSettings();
    select.innerHTML = '';
    for (const [value, label] of Object.entries(LOS_LABELS)) {
        const opt = document.createElement('option');
        opt.value = value;
        opt.textContent = label;
        if (value === losBehavior) opt.selected = true;
        select.appendChild(opt);
    }
}

export function mountSettingsUI() {
    const openBtn = document.getElementById('settings-btn');
    const modal = document.getElementById('settings-modal');
    const closeBtn = document.getElementById('settings-close');
    const losSelect = document.getElementById('settings-los');

    if (!openBtn || !modal || !closeBtn || !losSelect) return;

    renderOptions(losSelect);

    openBtn.addEventListener('click', () => { renderOptions(losSelect); show(); });
    closeBtn.addEventListener('click', hide);

    modal.addEventListener('click', (e) => { if (e.target === modal) hide(); });

    losSelect.addEventListener('change', () => {
        updateSettings({ losBehavior: losSelect.value });
    });

    document.addEventListener('keydown', (e) => {
        if (modal.classList.contains('visible') && e.key === 'Escape') hide();
    });
}