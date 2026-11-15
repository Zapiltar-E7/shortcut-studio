import { definePlugin } from '@steambrew/client';
import { GameData, loadCapsule, loadGame, loadStamp, pollUnlocks, setUnlocked } from './data';
import { achievementStat, achievementsSection, ensureStyles, gameInfoPanel, hasInfo, openAchievementList, showUnlockToast } from './panels';
import { startLaunchMenu, syncLaunchArrow } from './launch';
import { startPlaytime } from './playtime';
import { onLiveApplied, startLive } from './live';
import { appName, columns, currentAppId, isShortcut, libraryWindow, playBar, section, sel } from './steam';

const TAG = '[shortcut-studio]';
const POLL_MS = 400;
const STAMP_MS = 1500;

interface State {
	appid: number;
	data: GameData | null;
	loading: boolean;
	/** Bumped whenever data changes, so mounted panels know to redraw. */
	version: number;
}

let state: State | null = null;
/** The open "View My Achievements" list, so live unlocks can redraw it. */
let modal: { appid: number; data: GameData; render: () => void } | null = null;

function changed() {
	if (state) state.version++;
}

async function refresh(appid: number) {
	if (!state || state.appid !== appid) return;
	state.loading = true;
	try {
		let data = await loadGame(appid);
		// A read can land while the app is replacing games.json; try again before showing nothing.
		for (let i = 0; !data && i < 2 && state?.appid === appid; i++) {
			await new Promise((r) => setTimeout(r, 800));
			data = await loadGame(appid);
		}
		if (state?.appid === appid) {
			state.data = data;
			changed();
		}
		if (data && modal?.appid === appid) {
			// The open list keeps its object; swap everything in it (names, sets, order, new achievements,
			// progress, unlocks) and redraw. Filters, search and revealed hidden ones stay as they were.
			if (libraryWindow()?.document.querySelector('.ss-modal')) {
				Object.assign(modal.data, data);
				modal.render();
			} else modal = null;
		}
	} catch (e) {
		console.error(TAG, 'loading game data failed', e);
	} finally {
		if (state?.appid === appid) state.loading = false;
	}
}

function removeAll(doc: Document) {
	doc.querySelectorAll('[data-ss]').forEach((el) => el.remove());
	doc.querySelectorAll<HTMLElement>('[data-ss-hidden]').forEach((el) => {
		el.style.display = '';
		delete el.dataset.ssHidden;
	});
	doc.querySelector('.ss-modal')?.remove();
}

/** Puts `el` in place unless an up-to-date copy is already there. */
function mount(doc: Document, key: string, version: number, build: () => HTMLElement | null, place: (el: HTMLElement) => boolean) {
	const existing = doc.querySelector<HTMLElement>(`[data-ss="${key}"]`);
	if (existing && existing.dataset.ssVersion === String(version) && existing.isConnected) return;
	const el = build();
	existing?.remove();
	if (!el) return;
	el.dataset.ss = key;
	el.dataset.ssVersion = String(version);
	if (!place(el)) el.remove();
}

async function toggle(appid: number, doc: Document, a: GameData['achievements'][number]) {
	const unlocking = a.unlockedAt == null;
	const result = await setUnlocked(appid, a.id, unlocking);
	if (result === undefined) return;
	a.unlockedAt = result;
	changed();
	if (unlocking) showUnlockToast(doc, a);
}

/**
 * For shortcuts Steam fills the left column with a single text-only section ("Some detailed information … is
 * unavailable"). Hide it while our info is shown; removeAll() brings it back.
 */
function hideNotice(left: HTMLElement) {
	const bodySel = sel(section.Body);
	for (const child of Array.from(left.children) as HTMLElement[]) {
		if (child.dataset.ss || child.dataset.ssHidden) continue;
		const body = bodySel ? child.querySelector(bodySel) : null;
		if (body && body.children.length === 0 && (body.textContent ?? '').trim().length > 0) {
			child.dataset.ssHidden = '1';
			child.style.display = 'none';
		}
	}
}

function tick() {
	const win = libraryWindow();
	if (!win) return;
	const doc = win.document;
	const appid = currentAppId();

	syncLaunchArrow(doc, appid != null && isShortcut(appid) ? appid : null);
	if (appid == null || !isShortcut(appid)) {
		if (state) removeAll(doc);
		state = null;
		return;
	}
	if (!state || state.appid !== appid) {
		removeAll(doc);
		state = { appid, data: null, loading: false, version: 0 };
		refresh(appid);
		return;
	}
	const { data, version } = state;
	if (!data) return;
	ensureStyles(doc);

	// "ACHIEVEMENTS 7/63" in the play bar, exactly as Steam shows it for real games.
	// (Steam shows Last Played and Play Time for shortcuts by itself.)
	mount(
		doc,
		'stats',
		version,
		() => (data.achievements.length ? achievementStat(doc, playBar, data.achievements.filter((a) => a.unlockedAt != null).length, data.achievements.length) : null),
		(el) => {
			// The in-page play bar, not the sticky copy Steam keeps at the top of the page.
			const inPage = sel(playBar.InPage) ? doc.querySelector(sel(playBar.InPage)!) : null;
			const target = inPage && sel(playBar.GameStatsSection) ? inPage.querySelector(sel(playBar.GameStatsSection)!) : null;
			if (!target) return false;
			target.append(el);
			return true;
		},
	);

	// Game info goes at the top of the left column, replacing Steam's "detailed information is unavailable" notice.
	const left = sel(columns.LeftColumn) ? doc.querySelector<HTMLElement>(sel(columns.LeftColumn)!) : null;
	if (left && hasInfo(data)) hideNotice(left);
	mount(
		doc,
		'info',
		version,
		() => (hasInfo(data) ? gameInfoPanel(doc, data) : null),
		(el) => {
			if (!left) return false;
			left.prepend(el);
			return true;
		},
	);

	// Achievements at the top of the right column, where Steam puts them for real games.
	mount(
		doc,
		'achievements',
		version,
		() =>
			data.achievements.length
				? achievementsSection(doc, data, async () => {
						const capsule = await loadCapsule(appid);
							const render = openAchievementList(doc, appid, appName(appid), data, (a) => toggle(appid, doc, a), capsule);
						modal = { appid, data, render };
					})
				: null,
		(el) => {
			const column = sel(columns.RightColumn) && doc.querySelector(sel(columns.RightColumn)!);
			if (!column) return false;
			column.prepend(el);
			return true;
		},
	);
}

// ---------- Live emulator trophies ----------

const TROPHY_POLL_MS = 3000;
let seen: Record<string, Record<string, number>> | null = null;
let polling = false;

/**
 * Every few seconds, ask the backend for the unlocks of every emulator-synced game (it reads RPCS3 / shadPS4's
 * own files). Anything new since last time gets Steam's "Achievement Unlocked" popup and a page refresh.
 */
async function watchTrophies() {
	if (polling) return;
	polling = true;
	try {
		// Running shortcuts, so the backend only asks RetroAchievements' web API about games being played.
		let running: number[] = [];
		try {
			running = (Array.from((globalThis as any).SteamUIStore?.RunningApps ?? []) as any[]).map((a) => a?.appid).filter((id) => typeof id === 'number' && isShortcut(id));
		} catch {
			running = [];
		}
		const now = await pollUnlocks(running);
		const before = seen;
		seen = now;
		if (!before) return; // first look: just remember what's already unlocked

		for (const [appidText, unlocks] of Object.entries(now)) {
			const fresh = Object.keys(unlocks).filter((id) => !(before[appidText] && id in before[appidText]));
			if (!fresh.length) continue;
			const appid = Number(appidText);
			const data = await loadGame(appid);
			const doc = libraryWindow()?.document;
			if (data && doc) {
				ensureStyles(doc);
				for (const id of fresh) {
					const a = data.achievements.find((x) => x.id === id);
					if (a) showUnlockToast(doc, a, appName(appid));
				}
			}
			console.log(TAG, `${fresh.length} new trophy unlock(s) for ${appid}`);
			if (state?.appid === appid) refresh(appid);
		}
	} catch (e) {
		console.error(TAG, 'trophy poll failed', e);
	} finally {
		polling = false;
	}
}

export default definePlugin(() => {
	setInterval(watchTrophies, TROPHY_POLL_MS);
	try {
		startLaunchMenu();
	} catch (e) {
		console.error(TAG, 'launch menu failed to start', e);
	}
	try {
		startPlaytime();
	} catch (e) {
		console.error(TAG, 'playtime sync failed to start', e);
	}
	// Saves from the app arrive here and are applied through Steam's API, so Steam never needs a restart.
	try {
		startLive();
		onLiveApplied((appids) => {
			if (state && appids.includes(state.appid)) refresh(state.appid);
		});
	} catch (e) {
		console.error(TAG, 'live saving failed to start', e);
	}
	console.log(TAG, 'loaded', { columns: !!columns.RightColumn, sections: !!section.AppDetailsSectionContainer });
	// Data changes when the app saves; reload it whenever the game page is opened again.
	let lastPath = '';
	setInterval(() => {
		try {
			const path = (globalThis as any).MainWindowBrowserManager?.m_lastLocation?.pathname ?? '';
			if (path !== lastPath) {
				lastPath = path;
				if (state && currentAppId() === state.appid) refresh(state.appid);
			}
			tick();
		} catch (e) {
			console.error(TAG, 'tick failed', e);
		}
	}, POLL_MS);

	// Live page: while a non-Steam game's page is shown, check its files' fingerprint and reload when anything
	// changes (an app save, an unlock, a game's autosave moving a progress bar). No navigating away and back.
	let stampFor = -1;
	let stamp = '';
	let checking = false;
	setInterval(async () => {
		if (checking || !state) return;
		const appid = state.appid;
		checking = true;
		try {
			const now = await loadStamp(appid);
			if (!now || state?.appid !== appid) return;
			if (stampFor !== appid) {
				stampFor = appid;
				stamp = now; // the page just loaded this game: remember, don't reload
			} else if (now !== stamp) {
				stamp = now;
				if (!state.loading) refresh(appid);
				else stamp = ''; // a load is already running; compare again next time
			}
		} finally {
			checking = false;
		}
	}, STAMP_MS);

	return {
		title: 'Shortcut Studio',
		icon: <span style={{ fontWeight: 700 }}>SS</span>,
		content: (
			<div style={{ padding: 16, lineHeight: 1.5 }}>
				<p>Descriptions, details and achievements for non-Steam games are edited in the Shortcut Studio app.</p>
				<p>Open a non-Steam game in your library to see its details, and use View My Achievements to lock or unlock achievements.</p>
			</div>
		),
	};
});
