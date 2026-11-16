import { callable } from '@steambrew/client';
import { text } from './data';

// The plugin's own windows (achievement list, launch picker, unlock popup, icon tiles) draw their colours from
// these variables. Everything built from Steam's own classes follows the active Steam theme by itself.
//   stock        stock Steam's blue-grey (what the plugin always used)
//   simply-dark  the Simply Dark Millennium theme's neutral greys (its own variables first, so its colour
//                settings carry over)
//   auto         Simply Dark when that theme is active in the window, otherwise stock

export type Style = 'auto' | 'stock' | 'simply-dark';
export const STYLES: { data: Style; label: string }[] = [
	{ data: 'auto', label: 'Auto (match the Steam theme)' },
	{ data: 'stock', label: 'Stock' },
	{ data: 'simply-dark', label: 'Simply Dark' },
];

const PALETTES: Record<Exclude<Style, 'auto'>, Record<string, string>> = {
	stock: {
		'--ss-dialog': '#171a21',
		'--ss-dialog-0': 'rgba(23, 26, 33, 0)',
		'--ss-panel': 'rgba(35, 41, 52, .85)',
		'--ss-tile': '#10141a',
		'--ss-tile-muted': '#23262e',
		'--ss-track': '#0e1116',
		'--ss-line': '#2a2f38',
		'--ss-input': '#2a3039',
		'--ss-raised': '#3d4450',
		'--ss-raised-hover': '#4f5866',
		'--ss-sep': '#4c5560',
		'--ss-close': 'rgba(61, 68, 80, .9)',
		'--ss-close-hover': '#67707b',
		'--ss-art-fallback': '#1b2838',
		'--ss-toast': 'linear-gradient(135deg, #2a3343, #1b2029)',
		'--ss-picker': 'linear-gradient(180deg, #2a2f38 0%, #1e2229 100%)',
		'--ss-picker-list': '#191c22',
		'--ss-picker-row': '#2a2e36',
		'--ss-picker-row-hover': '#343a44',
		'--ss-text': '#dcdedf',
		'--ss-info': '#c6d4df',
		'--ss-text-2': '#acb2b8',
		'--ss-dim': '#8b929a',
		'--ss-faint': '#67707b',
	},
	'simply-dark': {
		'--ss-dialog': 'var(--color-darker, #191919)',
		'--ss-dialog-0': 'rgba(25, 25, 25, 0)',
		'--ss-panel': 'var(--color-dark, #212121)',
		'--ss-tile': 'var(--color-darkest, #141414)',
		'--ss-tile-muted': 'var(--darkgrey, #1e1e1f)',
		'--ss-track': 'var(--darkergrey, #151515)',
		'--ss-line': 'var(--littlegrey, #282828)',
		'--ss-input': 'var(--grey, #2e2e2f)',
		'--ss-raised': 'var(--lightgrey, #3e3e3f)',
		'--ss-raised-hover': 'var(--grey-ish, #454545)',
		'--ss-sep': 'var(--grey-ish, #454545)',
		'--ss-close': 'rgba(46, 46, 47, .92)',
		'--ss-close-hover': 'var(--grey-ish, #454545)',
		'--ss-art-fallback': 'var(--color-darker, #191919)',
		'--ss-toast': 'linear-gradient(135deg, var(--begrey, #262627), var(--blacker, #1c1c1c))',
		'--ss-picker': 'linear-gradient(180deg, var(--begrey, #262627) 0%, var(--blacker, #1c1c1c) 100%)',
		'--ss-picker-list': 'var(--darkergrey, #151515)',
		'--ss-picker-row': 'var(--grey, #2e2e2f)',
		'--ss-picker-row-hover': 'var(--lightergrey, #353535)',
		'--ss-text': 'var(--darkwhite, #d9dadd)',
		'--ss-info': 'var(--darkwhite, #d9dadd)',
		'--ss-text-2': 'var(--darkerwhite, #b8b6b4)',
		'--ss-dim': '#8f8f8f',
		'--ss-faint': '#6b6b6b',
	},
};

const getStyle = callable<[], string>('GetStyle');
const setStyleRpc = callable<[{ payload: string }], string>('SetStyle');

let chosen: Style = 'auto';
let applied: Exclude<Style, 'auto'> = 'stock';
/** The palette most recently put on a page (for things drawn as images, like the placeholder trophy). */
export const lastPalette = () => applied;
const listeners = new Set<(s: Style) => void>();

export const currentStyle = () => chosen;
export const onStyleChange = (fn: (s: Style) => void) => {
	listeners.add(fn);
	return () => listeners.delete(fn);
};

const valid = (s: unknown): s is Style => s === 'auto' || s === 'stock' || s === 'simply-dark';

/** Whether the Simply Dark Millennium theme is loaded in this window. */
function hasSimplyDark(doc: Document): boolean {
	return Array.from(doc.querySelectorAll<HTMLLinkElement>('link[href*="/themes/"]')).some((l) => /\/themes\/simply-dark\//i.test(l.href));
}

/** The palette a window should use right now. */
export function resolvedStyle(doc: Document): Exclude<Style, 'auto'> {
	if (chosen !== 'auto') return chosen;
	return hasSimplyDark(doc) ? 'simply-dark' : 'stock';
}

/** Puts the palette's variables in `doc` (once per change; cheap to call on every tick). */
export function applyStyle(doc: Document) {
	const palette = resolvedStyle(doc);
	applied = palette;
	let el = doc.getElementById('shortcut-studio-palette');
	if (el?.dataset.palette === palette) return;
	const css = `:root {\n${Object.entries(PALETTES[palette])
		.map(([k, v]) => `\t${k}: ${v};`)
		.join('\n')}\n}`;
	if (!el) {
		el = doc.createElement('style');
		el.id = 'shortcut-studio-palette';
		doc.head.prepend(el);
	}
	el.textContent = css;
	el.dataset.palette = palette;
}

async function load() {
	try {
		const s = text(await getStyle());
		if (valid(s) && s !== chosen) {
			chosen = s;
			listeners.forEach((fn) => fn(s));
		}
	} catch {
		/* keep the last one */
	}
}

/** Saves a new choice (the app's Settings window shows and changes the same one). */
export async function setStyle(s: Style) {
	chosen = s;
	listeners.forEach((fn) => fn(s));
	try {
		await setStyleRpc({ payload: JSON.stringify({ style: s }) });
	} catch (e) {
		console.error('[shortcut-studio] saving the style failed', e);
	}
}

/** Reads the choice now and every few seconds, so a change in the app shows without restarting Steam. */
export function startStyle() {
	load();
	setInterval(load, 3000);
}
