const SVG_NS = 'http://www.w3.org/2000/svg';
const ICON_SPRITE = '/images/icons.svg';

export function icon(name, className = 'icon') {
    const svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('class', className);
    svg.setAttribute('aria-hidden', 'true');
    const use = document.createElementNS(SVG_NS, 'use');
    use.setAttribute('href', `${ICON_SPRITE}#i-${name}`);
    svg.append(use);
    return svg;
}

export function h(tag, props = {}, ...children) {
    const el = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
        if (value == null || value === false) continue;
        if (key === 'class') el.className = value;
        else if (key === 'dataset') Object.assign(el.dataset, value);
        else if (key === 'style') Object.assign(el.style, value);
        else if (key.startsWith('on')) el.addEventListener(key.slice(2).toLowerCase(), value);
        else el.setAttribute(key, value === true ? '' : value);
    }
    el.append(...children.flat().filter((child) => child != null && child !== false));
    return el;
}

export function button({ icon: iconName, label, className = 'btn', title, role, onClick, hideLabel = false, pressed }) {
    const btn = h('button', {
        type: 'button',
        class: iconName && !label ? `${className} btn-icon` : className,
        title: title ?? (hideLabel ? label : null),
        'aria-label': hideLabel || !label ? (title ?? label) : null,
        'aria-pressed': pressed == null ? null : String(pressed),
        dataset: role ? { role } : null,
        onClick,
    });
    if (iconName) btn.append(icon(iconName));
    if (label) btn.append(h('span', { class: hideLabel ? 'visually-hidden' : 'btn-text' }, label));
    return btn;
}

export function setButtonLabel(btn, label) {
    btn.querySelector('span').textContent = label;
}

