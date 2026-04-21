let backdropEl = null;
let inputEl = null;
let initialized = false;

function ensure() {
    if (initialized) return;
    initialized = true;

    backdropEl = document.getElementById('copy-fallback');
    inputEl = document.getElementById('copy-input');
    if (!backdropEl || !inputEl) return;

    const closeBtn = document.getElementById('copy-close-btn');
    const selectBtn = document.getElementById('copy-select-btn');

    closeBtn?.addEventListener('click', (e) => { e.preventDefault(); hide(); });
    selectBtn?.addEventListener('click', (e) => { e.preventDefault(); inputEl.focus(); inputEl.select(); });

    backdropEl.addEventListener('click', (e) => {
        if (e.target === backdropEl) hide();
    });

    document.addEventListener('keydown', (e) => {
        if (!isVisible()) return;
        if (e.key === 'Escape' || e.key === 'Enter') hide();
    });
}

function isVisible() {
    return backdropEl?.classList.contains('visible') ?? false;
}

function show(text) {
    ensure();
    if (!backdropEl || !inputEl) return;
    inputEl.value = text;
    backdropEl.classList.add('visible');
    inputEl.focus();
    inputEl.select();
}

function hide() {
    backdropEl?.classList.remove('visible');
}

async function tryNativeApi(text) {
    if (!navigator.clipboard?.writeText) return false;
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

function tryExecCommand(text) {
    try {
        const el = document.createElement('textarea');
        el.value = text;
        el.setAttribute('readonly', '');
        el.style.position = 'absolute';
        el.style.left = '-9999px';
        document.body.appendChild(el);
        el.select();
        const ok = document.execCommand('copy');
        document.body.removeChild(el);
        return ok;
    } catch {
        return false;
    }
}

export async function copyToClipboard(text) {
    if (await tryNativeApi(text)) return true;
    if (tryExecCommand(text)) return true;
    show(text);
    return false;
}