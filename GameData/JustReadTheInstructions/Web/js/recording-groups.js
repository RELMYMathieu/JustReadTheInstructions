import { h } from './dom.js';
import { menuItem, menuNote, menuSeparator } from './ui.js';

const STORAGE_KEY = 'jrti-recording-groups-by-name';
const GROUP_LABELS = ['G1', 'G2', 'G3', 'G4'];

function cameraCount(n) {
    return n === 1 ? '1 camera' : `${n} cameras`;
}

export class RecordingGroups {
    #assignments = {};
    #getCards;
    #barEl = null;
    #buttons = [];

    constructor(getCards) {
        this.#getCards = getCards;
        try {
            this.#assignments = JSON.parse(localStorage.getItem(STORAGE_KEY)) ?? {};
        } catch {
            this.#assignments = {};
        }
    }

    mount(barEl) {
        this.#barEl = barEl;
        barEl.setAttribute('aria-label', 'Record groups');
        GROUP_LABELS.forEach((label, i) => {
            const btn = h('button', {
                type: 'button',
                class: 'btn group-btn',
                hidden: true,
                onClick: () => this.#toggleGroup(i),
            }, label);
            this.#buttons.push(btn);
            barEl.append(btn);
        });
    }

    syncCard(card) {
        this.#paintCard(card, this.#groupId(card));
        card.groupItems = () => this.#menuItems(card);
        card.onRecordingChange = () => this.refresh();
    }

    refresh() {
        let anyVisible = false;

        GROUP_LABELS.forEach((label, i) => {
            const members = this.#members(i);
            const online = members.filter((c) => !c.destroyed);
            const recording = members.filter((c) => c.recorder?.isActive);
            const btn = this.#buttons[i];
            btn.hidden = online.length === 0 && recording.length === 0;
            if (btn.hidden) return;

            anyVisible = true;
            const active = recording.length > 0;
            btn.classList.toggle('group-btn--active', active);
            btn.textContent = active
                ? `Stop ${label} · ${cameraCount(recording.length)}`
                : `Record ${label} · ${cameraCount(online.length)}`;
            btn.title = active
                ? `Stop the ${cameraCount(recording.length)} recording in ${label}`
                : `Start recording the ${cameraCount(online.length)} in ${label} together`;
        });

        if (this.#barEl) this.#barEl.hidden = !anyVisible;
    }

    #members(groupId) {
        return [...this.#getCards().values()].filter((c) => this.#groupId(c) === groupId);
    }

    #groupId(card) {
        const v = this.#assignments[card.key];
        return Number.isInteger(v) && v >= 0 && v < GROUP_LABELS.length ? v : null;
    }

    #paintCard(card, groupId) {
        card.setGroup(groupId === null ? null : GROUP_LABELS[groupId]);
    }

    #menuItems(card) {
        const current = this.#groupId(card);
        return [
            menuNote('Cameras in the same group start and stop recording together, with the group\'s button at the bottom of the page. Groups are kept in this browser.'),
            ...GROUP_LABELS.map((label, i) => {
                const size = this.#members(i).length;
                return menuItem({
                    label,
                    detail: size === 0 ? 'empty' : cameraCount(size),
                    checked: current === i,
                    onSelect: () => this.#assign(card, i),
                });
            }),
            menuSeparator(),
            menuItem({ label: 'No group', checked: current === null, onSelect: () => this.#assign(card, null) }),
        ];
    }

    #assign(card, groupId) {
        if (groupId === null) delete this.#assignments[card.key];
        else this.#assignments[card.key] = groupId;
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(this.#assignments)); } catch { }
        this.#paintCard(card, groupId);
        this.refresh();
    }

    #toggleGroup(i) {
        const members = this.#members(i);
        const anyActive = members.some((c) => c.recorder?.isActive);
        const pending = anyActive
            ? members.filter((c) => c.recorder?.isActive).map((c) => c.recorder.stop())
            : members.filter((c) => !c.destroyed).map((c) => c.startRecording());
        Promise.allSettled(pending).then(() => this.refresh());
    }
}
