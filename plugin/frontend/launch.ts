import { callable, DialogButton, Menu, MenuItem, MenuSeparator, ModalRoot, showContextMenu, showModal, ToggleField } from '@steambrew/client';
import { h } from './panels';
import { applyStyle } from './style';
import { appName, cx, isBigPicture, libraryWindow, playButton, sel } from './steam';

const TAG = '[shortcut-studio]';
const getLaunch = callable<[{ payload: string }], string>('GetLaunch');
const setChoice = callable<[{ payload: string }], string>('SetLaunchChoice');

interface Option {
	name: string;
	exe: string;
	startDir: string;
	args: string;
}
interface LaunchMenu {
	options: Option[];
	base: Option | null;
	choice: number | null;
}

const quote = (p: string) => (p ? `"${p.replace(/^"|"$/g, '')}"` : '');

/** A shortcut's appid from a Steam game id ((appid << 32) | 0x02000000), or null for real games. */
function shortcutAppId(gameId: unknown): number | null {
	try {
		const id = BigInt(String(gameId));
		if ((id & 0xffffffn) !== 0n || ((id >> 24n) & 0xffn) !== 2n) return null; // type 2 = shortcut
		return Number(id >> 32n);
	} catch {
		return null;
	}
}

async function loadMenu(appid: number): Promise<LaunchMenu | null> {
	try {
		const raw = JSON.parse(await getLaunch({ payload: JSON.stringify({ appid: String(appid) }) }));
		if (!raw || typeof raw !== 'object') return null;
		const list = (v: any) => (Array.isArray(v) ? v : v && typeof v === 'object' ? Object.values(v) : []);
		const opt = (o: any): Option => ({ name: String(o?.name ?? ''), exe: String(o?.exe ?? ''), startDir: String(o?.startDir ?? ''), args: String(o?.args ?? '') });
		const options = list(raw.options).map(opt);
		if (options.length < 2) return null;
		return { options, base: raw.base ? opt(raw.base) : null, choice: typeof raw.choice === 'number' ? raw.choice : null };
	} catch (e) {
		console.error(TAG, 'loading launch menu failed', e);
		return null;
	}
}

const CSS = `
.ss-lo-backdrop { position: fixed; inset: 0; z-index: 10001; -webkit-app-region: no-drag; background: rgba(0,0,0,.55); display: flex; align-items: center; justify-content: center; }
.ss-lo { width: 490px; background: var(--ss-picker); box-shadow: 0 10px 40px rgba(0,0,0,.65); color: var(--ss-text); border-radius: 2px; }
.ss-lo-bar { display: flex; align-items: center; justify-content: space-between; padding: 8px 10px 0 18px; font-size: 13px; color: var(--ss-text-2); }
.ss-lo-x { cursor: pointer; color: var(--ss-dim); font-size: 18px; line-height: 18px; padding: 2px 4px; }
.ss-lo-x:hover { color: #fff; }
.ss-lo-body { padding: 14px 18px 18px; }
.ss-lo-h { font-size: 14px; font-weight: 700; letter-spacing: .02em; text-transform: uppercase; color: #fff; margin-bottom: 10px; }
.ss-lo-list { background: var(--ss-picker-list); border-radius: 2px; padding: 10px 12px; min-height: 160px; box-shadow: inset 0 1px 3px rgba(0,0,0,.4); }
.ss-lo-row { display: flex; align-items: center; gap: 10px; padding: 9px 12px; margin-bottom: 6px; background: var(--ss-picker-row); border-radius: 2px; cursor: pointer; font-size: 14px; color: var(--ss-text); }
.ss-lo-row:hover { background: var(--ss-picker-row-hover); }
.ss-lo-radio { width: 12px; height: 12px; border-radius: 50%; background: #fff; flex-shrink: 0; box-shadow: inset 0 0 0 1px var(--ss-dim); }
.ss-lo-row.ss-on .ss-lo-radio { background: #fff; box-shadow: inset 0 0 0 3px #1a9fff; }
.ss-lo-check { display: flex; align-items: center; gap: 10px; margin: 14px 0 10px 6px; font-size: 14px; cursor: pointer; user-select: none; }
.ss-lo-box { width: 22px; height: 22px; border-radius: 2px; background: var(--ss-raised); display: flex; align-items: center; justify-content: center; color: #fff; font-size: 16px; }
.ss-lo-check.ss-on .ss-lo-box { background: #1a9fff; }
.ss-lo-note { font-size: 13px; color: var(--ss-text-2); line-height: 1.45; margin-bottom: 16px; }
.ss-lo-btns { display: flex; justify-content: flex-end; gap: 18px; }
.ss-lo-btn { min-width: 104px; padding: 12px 18px; border-radius: 2px; text-align: center; font-size: 16px; cursor: pointer; user-select: none; }
.ss-lo-play { background: linear-gradient(90deg, #1a9fff, #1a8fe8); color: #fff; }
.ss-lo-play:hover { background: #47b2ff; }
.ss-lo-cancel { background: var(--ss-raised); color: var(--ss-text); }
.ss-lo-cancel:hover { background: var(--ss-raised-hover); }
`;

/** Steam's "Select launch option" dialog. Resolves with the chosen index and whether to remember it, or null. */
function pickOption(doc: Document, title: string, options: Option[]): Promise<{ index: number; remember: boolean } | null> {
	applyStyle(doc);
	if (!doc.getElementById('shortcut-studio-launch-css')) doc.head.append(h(doc, 'style', { id: 'shortcut-studio-launch-css' }, CSS));
	return new Promise((resolve) => {
		let selected = 0;
		let remember = false;
		const done = (v: { index: number; remember: boolean } | null) => {
			backdrop.remove();
			doc.removeEventListener('keydown', onKey, true);
			resolve(v);
		};
		const onKey = (e: KeyboardEvent) => {
			if (e.key === 'Escape') done(null);
			else if (e.key === 'Enter') done({ index: selected, remember });
			else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
				selected = (selected + (e.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length;
				render();
			} else return;
			e.preventDefault();
			e.stopPropagation();
		};

		const list = h(doc, 'div', { class: 'ss-lo-list' });
		const render = () => {
			list.replaceChildren(
				...options.map((o, i) => {
					const row = h(doc, 'div', { class: 'ss-lo-row' + (i === selected ? ' ss-on' : '') }, h(doc, 'div', { class: 'ss-lo-radio' }), o.name || `Option ${i + 1}`);
					row.addEventListener('click', () => {
						selected = i;
						render();
					});
					row.addEventListener('dblclick', () => done({ index: i, remember }));
					return row;
				}),
			);
		};
		render();

		const check = h(doc, 'div', { class: 'ss-lo-check' }, h(doc, 'div', { class: 'ss-lo-box' }), 'Always use this option');
		check.addEventListener('click', () => {
			remember = !remember;
			check.classList.toggle('ss-on', remember);
			(check.firstChild as HTMLElement).textContent = remember ? '✓' : '';
		});
		const play = h(doc, 'div', { class: 'ss-lo-btn ss-lo-play' }, 'Play');
		play.addEventListener('click', () => done({ index: selected, remember }));
		const cancel = h(doc, 'div', { class: 'ss-lo-btn ss-lo-cancel' }, 'Cancel');
		cancel.addEventListener('click', () => done(null));
		const x = h(doc, 'div', { class: 'ss-lo-x', title: 'Close' }, '✕');
		x.addEventListener('click', () => done(null));

		const dialog = h(
			doc,
			'div',
			{ class: 'ss-lo' },
			h(doc, 'div', { class: 'ss-lo-bar' }, h(doc, 'span', {}, title), x),
			h(
				doc,
				'div',
				{ class: 'ss-lo-body' },
				h(doc, 'div', { class: 'ss-lo-h' }, `Select launch option (${options.length})`),
				list,
				check,
				h(doc, 'div', { class: 'ss-lo-note' }, "You can edit these options, and undo \"Always use this option\", on the game's Details tab in Shortcut Studio."),
				h(doc, 'div', { class: 'ss-lo-btns' }, play, cancel),
			),
		);
		const backdrop = h(doc, 'div', { class: 'ss-lo-backdrop' }, dialog);
		backdrop.addEventListener('click', (e) => e.target === backdrop && done(null));
		doc.addEventListener('keydown', onKey, true);
		doc.body.append(backdrop);
	});
}

type Pick = { index: number; remember: boolean } | null;

/**
 * Big Picture's version of the picker: a real Steam modal built from Steam's own dialog components, so the
 * controller moves between options (A launches the focused one, B cancels) exactly as in Steam's own dialogs.
 * Falls back to the first option if Steam's components can't be found, so Play never silently does nothing.
 */
function pickOptionGamepad(win: Window, title: string, options: Option[]): Promise<Pick> {
	const R = (globalThis as any).SP_REACT;
	if (!R || !ModalRoot || !DialogButton) {
		console.warn(TAG, "Steam's dialog components weren't found; launching the first option");
		return Promise.resolve({ index: 0, remember: false });
	}
	return new Promise((resolve) => {
		let settled = false;
		let remember = false;
		let modal: { Close: () => void } | null = null;
		const done = (v: Pick) => {
			if (settled) return;
			settled = true;
			modal?.Close();
			resolve(v);
		};
		const Picker = () => {
			const [on, setOn] = R.useState(false);
			return R.createElement(
				ModalRoot,
				{ onCancel: () => done(null), closeModal: () => done(null), bAllowFullSize: false },
				R.createElement('div', { style: { fontSize: '24px', fontWeight: 700, marginBottom: '4px' } }, title),
				R.createElement('div', { style: { fontSize: '14px', opacity: 0.7, marginBottom: '16px', textTransform: 'uppercase', letterSpacing: '.04em' } }, `Select launch option (${options.length})`),
				...options.map((o, i) =>
					R.createElement(DialogButton, { key: i, style: { marginBottom: '8px', textAlign: 'left' }, onClick: () => done({ index: i, remember }) }, o.name || `Option ${i + 1}`),
				),
				ToggleField
					? R.createElement(ToggleField, {
							label: 'Always use this option',
							description: "Change or undo this on the game's Details tab in Shortcut Studio.",
							checked: on,
							onChange: (v: boolean) => {
								remember = v;
								setOn(v);
							},
						})
					: null,
				R.createElement(DialogButton, { style: { marginTop: '12px' }, onClick: () => done(null) }, 'Cancel'),
			);
		};
		try {
			modal = showModal(R.createElement(Picker), win, { strTitle: 'Select launch option', bHideMainWindowForPopouts: false, bNeverPopOut: true });
		} catch (e) {
			console.error(TAG, "couldn't show the launch picker in Big Picture; launching the first option", e);
			done({ index: 0, remember: false });
		}
	});
}

/** Points the shortcut at one option through Steam's API (the same calls as editing its Properties). */
function applyOption(appid: number, menu: LaunchMenu, o: Option) {
	const apps = (globalThis as any).SteamClient.Apps;
	const exe = o.exe || menu.base?.exe || '';
	const dir = o.startDir || menu.base?.startDir || '';
	if (exe) apps.SetShortcutExe(appid, quote(exe));
	if (dir) apps.SetShortcutStartDir(appid, quote(dir));
	apps.SetShortcutLaunchOptions(appid, o.args ?? '');
}

/**
 * Wraps SteamClient.Apps.RunGame, which every Play button and menu in Steam goes through. For shortcuts with
 * a launch menu it asks which option to start (unless one was remembered), applies it, then launches as normal.
 */
export function startLaunchMenu() {
	const apps = (globalThis as any).SteamClient?.Apps;
	if (!apps?.RunGame || apps.RunGame.__ssWrapped) return;
	const original = apps.RunGame.bind(apps);

	const wrapped = (gameId: string, ...rest: any[]) => {
		const appid = shortcutAppId(gameId);
		if (appid == null) return original(gameId, ...rest);
		(async () => {
			const menu = await loadMenu(appid);
			if (!menu) return original(gameId, ...rest);
			let index = menu.choice != null && menu.choice >= 0 && menu.choice < menu.options.length ? menu.choice : -1;
			if (index < 0) {
				const win = libraryWindow();
				const title = appName(appid) || 'Launch';
				const pick = isBigPicture(win) ? await pickOptionGamepad(win!, title, menu.options) : await pickOption(win?.document ?? document, title, menu.options);
				if (!pick) return; // cancelled
				index = pick.index;
				if (pick.remember) await setChoice({ payload: JSON.stringify({ appid: String(appid), index }) });
			}
			applyOption(appid, menu, menu.options[index]);
			// Give Steam a moment to take the new target before it builds the command line.
			await new Promise((r) => setTimeout(r, 400));
			console.log(TAG, `launching ${appid} with "${menu.options[index].name}"`);
			original(gameId, ...rest);
		})().catch((e) => {
			console.error(TAG, 'launch menu failed; launching normally', e);
			original(gameId, ...rest);
		});
	};
	wrapped.__ssWrapped = true;
	try {
		apps.RunGame = wrapped;
	} catch (e) {
		console.error(TAG, "couldn't hook RunGame; launch menus won't show", e);
	}
	if (apps.RunGame !== wrapped) console.warn(TAG, 'SteamClient.Apps.RunGame is read-only; launch menus are unavailable');
}

// ---------- Play button arrow ----------
// Shortcuts with a launch menu get Steam's split arrow on the Play button (the one real games use for "Play from").
// It opens a menu to pick the option Play starts without asking, or to have Play ask every time.

const ARROW_SVG =
	'<svg xmlns="http://www.w3.org/2000/svg" class="SVGIcon_Button SVGIcon_FlatArrow" width="256px" height="256px" viewBox="0 0 256 256" style="transform: rotate(180deg);"><polygon points="13.913,141 128,59 242.087,141" fill="#ffffff"></polygon></svg>';
const MENU_REFRESH_MS = 2000;
let arrowMenu: { appid: number; menu: LaunchMenu | null; at: number; loading: boolean } | null = null;

function removeArrows(doc: Document) {
	doc.querySelectorAll('[data-ss="launch-arrow"]').forEach((el) => el.remove());
	doc.querySelectorAll<HTMLElement>('[data-ss-arrow]').forEach((el) => {
		if (playButton.ShowingStreaming) el.classList.remove(playButton.ShowingStreaming);
		delete el.dataset.ssArrow;
	});
}

async function chooseDefault(appid: number, index: number | null) {
	await setChoice({ payload: JSON.stringify({ appid: String(appid), index }) });
	if (arrowMenu?.appid === appid) arrowMenu.at = 0; // re-read now
}

function openArrowMenu(appid: number, menu: LaunchMenu, anchor: HTMLElement) {
	const R = (globalThis as any).SP_REACT;
	const item = (label: string, checked: boolean, onClick: () => void) =>
		R.createElement(
			MenuItem,
			{ onClick, selected: checked },
			R.createElement('div', { className: cx(playButton.StreamingContextMenuItem), style: { display: 'flex', alignItems: 'center', gap: '8px' } },
				R.createElement('div', { className: cx(playButton.CheckContainer), style: { width: '14px', flexShrink: 0, color: '#1a9fff' } }, checked ? '✓' : ''),
				R.createElement('div', { className: cx(playButton.StreamingTargetLabel) }, label)),
		);
	showContextMenu(
		R.createElement(
			Menu,
			{ label: 'Launch options' },
			...menu.options.map((o, i) => item(o.name || `Option ${i + 1}`, menu.choice === i, () => chooseDefault(appid, i))),
			R.createElement(MenuSeparator),
			item('Ask every time', menu.choice == null, () => chooseDefault(appid, null)),
		),
		anchor,
		{ bOverlapHorizontal: true },
	);
}

/** Called on every page tick: keeps the arrow on the Play buttons of shortcuts that have a launch menu. */
export function syncLaunchArrow(doc: Document, appid: number | null) {
	const containerSel = sel(playButton.PlayButtonContainer);
	if (appid == null || !containerSel || !playButton.StreamingSelector) {
		arrowMenu = null;
		removeArrows(doc);
		return;
	}
	if (!arrowMenu || arrowMenu.appid !== appid) arrowMenu = { appid, menu: null, at: 0, loading: false };
	const state = arrowMenu;
	if (!state.loading && Date.now() - state.at > MENU_REFRESH_MS) {
		state.loading = true;
		loadMenu(appid)
			.then((m) => {
				state.menu = m;
				state.at = Date.now();
			})
			.finally(() => (state.loading = false));
	}
	const menu = state.menu;
	if (!menu) return removeArrows(doc);

	// Steam keeps two Play buttons: the one in the page and the copy in the header that sticks while scrolling.
	doc.querySelectorAll<HTMLElement>(containerSel).forEach((container) => {
		if (playButton.ShowingStreaming && !container.classList.contains(playButton.ShowingStreaming)) container.classList.add(playButton.ShowingStreaming);
		container.dataset.ssArrow = '1';
		if (container.querySelector('[data-ss="launch-arrow"]')) return;
		if (sel(playButton.StreamingSelector) && container.querySelector(sel(playButton.StreamingSelector)!)) return; // Steam has its own
		const arrow = h(doc, 'div', {
			class: cx(playButton.StreamingSelector, playButton.ButtonChild, 'Focusable'),
			'aria-label': 'Launch options',
			title: 'Launch options',
			tabindex: '0',
			role: 'button',
		});
		arrow.dataset.ss = 'launch-arrow';
		arrow.innerHTML = ARROW_SVG;
		arrow.addEventListener('click', (e) => {
			e.preventDefault();
			e.stopPropagation();
			if (arrowMenu?.appid === appid && arrowMenu.menu) openArrowMenu(appid, arrowMenu.menu, container); // drops down from the Play button's left edge, like Steam's
		});
		container.append(arrow);
	});
}
