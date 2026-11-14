import { callable } from '@steambrew/client';
import { h } from './panels';
import { appName, libraryWindow } from './steam';

const TAG = '[shortcut-studio]';
const getLaunch = callable<[{ payload: string }], string>('GetLaunch');
const setChoice = callable<[{ payload: string }], string>('SetLaunchChoice');

interface Option {
	name: string;
	exe: string;
	startDir: string;
	args: string;
}
interface Menu {
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

async function loadMenu(appid: number): Promise<Menu | null> {
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
.ss-lo-backdrop { position: fixed; inset: 0; z-index: 10001; background: rgba(0,0,0,.55); display: flex; align-items: center; justify-content: center; }
.ss-lo { width: 490px; background: linear-gradient(180deg, #2a2f38 0%, #1e2229 100%); box-shadow: 0 10px 40px rgba(0,0,0,.65); color: #dcdedf; border-radius: 2px; }
.ss-lo-bar { display: flex; align-items: center; justify-content: space-between; padding: 8px 10px 0 18px; font-size: 13px; color: #b8bcbf; }
.ss-lo-x { cursor: pointer; color: #8b929a; font-size: 18px; line-height: 18px; padding: 2px 4px; }
.ss-lo-x:hover { color: #fff; }
.ss-lo-body { padding: 14px 18px 18px; }
.ss-lo-h { font-size: 14px; font-weight: 700; letter-spacing: .02em; text-transform: uppercase; color: #fff; margin-bottom: 10px; }
.ss-lo-list { background: #191c22; border-radius: 2px; padding: 10px 12px; min-height: 160px; box-shadow: inset 0 1px 3px rgba(0,0,0,.4); }
.ss-lo-row { display: flex; align-items: center; gap: 10px; padding: 9px 12px; margin-bottom: 6px; background: #2a2e36; border-radius: 2px; cursor: pointer; font-size: 14px; color: #dcdedf; }
.ss-lo-row:hover { background: #343a44; }
.ss-lo-radio { width: 12px; height: 12px; border-radius: 50%; background: #fff; flex-shrink: 0; box-shadow: inset 0 0 0 1px #8b929a; }
.ss-lo-row.ss-on .ss-lo-radio { background: #fff; box-shadow: inset 0 0 0 3px #1a9fff; }
.ss-lo-check { display: flex; align-items: center; gap: 10px; margin: 14px 0 10px 6px; font-size: 14px; cursor: pointer; user-select: none; }
.ss-lo-box { width: 22px; height: 22px; border-radius: 2px; background: #3d4450; display: flex; align-items: center; justify-content: center; color: #fff; font-size: 16px; }
.ss-lo-check.ss-on .ss-lo-box { background: #1a9fff; }
.ss-lo-note { font-size: 13px; color: #b8bcbf; line-height: 1.45; margin-bottom: 16px; }
.ss-lo-btns { display: flex; justify-content: flex-end; gap: 18px; }
.ss-lo-btn { min-width: 104px; padding: 12px 18px; border-radius: 2px; text-align: center; font-size: 16px; cursor: pointer; user-select: none; }
.ss-lo-play { background: linear-gradient(90deg, #1a9fff, #1a8fe8); color: #fff; }
.ss-lo-play:hover { background: #47b2ff; }
.ss-lo-cancel { background: #3d4450; color: #dcdedf; }
.ss-lo-cancel:hover { background: #4f5866; }
`;

/** Steam's "Select launch option" dialog. Resolves with the chosen index and whether to remember it, or null. */
function pickOption(doc: Document, title: string, options: Option[]): Promise<{ index: number; remember: boolean } | null> {
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

/** Points the shortcut at one option through Steam's API (the same calls as editing its Properties). */
function applyOption(appid: number, menu: Menu, o: Option) {
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
				const doc = libraryWindow()?.document ?? document;
				const pick = await pickOption(doc, appName(appid) || 'Launch', menu.options);
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
