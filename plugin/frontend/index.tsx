import { definePlugin, DropdownItem } from '@steambrew/client';
import { currentStyle, lastPalette, onStyleChange, setStyle, startStyle, Style, STYLES } from './style';
import { GameData, listGames, loadCapsule, loadGame, loadStamp, pollUnlocks, setUnlocked } from './data';
import { achievementStat, achievementsSection, ensureStyles, gameInfoPanel, hasInfo, openAchievementList, showUnlockToast } from './panels';
import { makeFocusable, pruneFocusable } from './bpnav';
import { showSteamAchievementToast } from './steamtoast';
import { startLaunchMenu, syncLaunchArrow } from './launch';
import { startPlaytime } from './playtime';
import { onLiveApplied, startLive } from './live';
import { appName, bpGameInfo, bpMedia, columns, currentAppId, desc, isBigPicture, isShortcut, libraryWindow, playBar, section, sel } from './steam';

const TAG = '[shortcut-studio]';
const POLL_MS = 400;
const STAMP_MS = 1500;

interface State {
	appid: number;
	data: GameData | null;
	loading: boolean;
	/** Bumped whenever data changes, so mounted panels know to redraw. */
	version: number;
	/** The page document the panels are drawn in (desktop library or Big Picture). */
	doc: Document;
}

let state: State | null = null;
/** The open "View My Achievements" list, so live unlocks can redraw it. */
let modal: { appid: number; data: GameData; render: () => void } | null = null;

/**
 * Every game's page data, with the file fingerprint it was read at. A page opens straight from here, so Steam's
 * plain non-Steam page never shows; the fingerprint is then checked and the data re-read only if it moved on.
 */
const cache = new Map<number, { data: GameData | null; stamp: string }>();

function changed() {
	if (state) state.version++;
}

/** Reads one game's data into the cache (the stamp first, so a change during the read is caught next time). */
async function fetchGame(appid: number, retry: boolean): Promise<GameData | null> {
	const stamp = await loadStamp(appid);
	let data = await loadGame(appid, !retry);
	// A read can land while the app is replacing games.json; try again before showing nothing.
	for (let i = 0; retry && !data && i < 2 && state?.appid === appid; i++) {
		await new Promise((r) => setTimeout(r, 800));
		data = await loadGame(appid);
	}
	if (stamp) cache.set(appid, { data, stamp });
	return data;
}

async function refresh(appid: number) {
	if (!state || state.appid !== appid) return;
	state.loading = true;
	try {
		const data = await fetchGame(appid, true);
		if (state?.appid === appid) {
			state.data = data;
			changed();
			tick(); // draw now, not at the next poll
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

/** Re-reads the open game only if its files changed since it was cached. */
async function revalidate(appid: number) {
	const cached = cache.get(appid);
	if (!cached) return refresh(appid);
	const now = await loadStamp(appid);
	if (now && now !== cached.stamp && state?.appid === appid && !state.loading) refresh(appid);
}

/** Reads every game the app knows into the cache in the background, so even a first visit opens at once. */
async function preload() {
	for (const appid of await listGames()) {
		if (cache.has(appid)) continue;
		try {
			await fetchGame(appid, false);
		} catch {
			/* the page will load it when opened */
		}
	}
}

function removeAll(doc: Document) {
	pruneFocusable(true);
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
	if (unlocking && !showSteamAchievementToast(appid, a)) showUnlockToast(doc, a);
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
	if (isBigPicture(win)) {
		// Our buttons become stops for the controller (see bpnav.ts).
		pruneFocusable();
		makeFocusable(win, doc.querySelector<HTMLElement>('[data-ss="launch-arrow"]'));
		makeFocusable(win, doc.querySelector<HTMLElement>('[data-ss="achievements"] .ss-view'));
	}
	if (appid == null || !isShortcut(appid)) {
		if (state) removeAll(doc);
		state = null;
		return;
	}
	if (!state || state.appid !== appid || state.doc !== doc) {
		if (state && state.doc !== doc) {
			try {
				removeAll(state.doc);
			} catch {
				/* that window is gone */
			}
		}
		const sameGame = state?.appid === appid;
		removeAll(doc);
		const cached = cache.get(appid);
		state = { appid, data: sameGame ? state!.data : (cached?.data ?? null), loading: false, version: sameGame ? state!.version + 1 : 0, doc };
		if (!cached) {
			refresh(appid);
			return;
		}
		// Show the cached copy now. RetroAchievements games always re-read (a visit fetches fresh unlocks);
		// others only if their files changed since.
		if (cached.data?.trophySource?.emulator === 'retroachievements') refresh(appid);
		else revalidate(appid);
	}
	const { data } = state;
	if (!data) return;
	const palette = lastPalette();
	ensureStyles(doc);
	if (lastPalette() !== palette) changed(); // redraw the placeholder trophies in the new palette
	const { version } = state;
	if (isBigPicture(win)) return tickBigPicture(doc, appid, data, version);

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
				? achievementsSection(doc, data, () => openList(doc, appid, data))
				: null,
		(el) => {
			const column = sel(columns.RightColumn) && doc.querySelector(sel(columns.RightColumn)!);
			if (!column) return false;
			column.prepend(el);
			return true;
		},
	);
}

/** Opens the full achievement list over the page. */
async function openList(doc: Document, appid: number, data: GameData) {
	const capsule = await loadCapsule(appid);
	const render = openAchievementList(doc, appid, appName(appid), data, (a) => toggle(appid, doc, a), capsule);
	modal = { appid, data, render };
}

/**
 * Big Picture's game page has the play section, then tabs (Activity / Your stuff / Community / Game Info).
 * The achievement count goes in the play section's stats, the Achievements section at the top of Your stuff
 * (where Steam lists them for real games), and the game's details in Game Info in place of Steam's
 * "detailed information is unavailable" notice.
 */
function tickBigPicture(doc: Document, appid: number, data: GameData, version: number) {
	mount(
		doc,
		'stats',
		version,
		() => (data.achievements.length ? achievementStat(doc, playBar, data.achievements.filter((a) => a.unlockedAt != null).length, data.achievements.length) : null),
		(el) => {
			const target = sel(playBar.GameStatsSection) ? doc.querySelector(sel(playBar.GameStatsSection)!) : null;
			if (!target) return false;
			target.append(el);
			return true;
		},
	);

	// The open tab's content. Your stuff is the one with the Media section; Game Info has its own container.
	const seek = sel(columns.SeekTarget) ? doc.querySelector<HTMLElement>(sel(columns.SeekTarget)!) : null;
	const media = seek && sel(bpMedia.ScreenshotsSection) ? seek.querySelector(sel(bpMedia.ScreenshotsSection)!) : null;
	const gameInfo = seek && sel(bpGameInfo.GameInfoContainer) ? seek.querySelector<HTMLElement>(sel(bpGameInfo.GameInfoContainer)!) : null;

	mount(
		doc,
		'achievements',
		version,
		() => (data.achievements.length && media ? achievementsSection(doc, data, () => openList(doc, appid, data)) : null),
		(el) => {
			if (!seek || !media) return false;
			el.classList.add('ss-bp');
			seek.prepend(el);
			return true;
		},
	);

	if (gameInfo && hasInfo(data)) {
		const notice = sel(desc.Shortcut) ? gameInfo.querySelector<HTMLElement>(sel(desc.Shortcut)!) : null;
		if (notice && !notice.dataset.ssHidden) {
			notice.dataset.ssHidden = '1';
			notice.style.display = 'none';
		}
	}
	mount(
		doc,
		'info',
		version,
		() => (gameInfo && hasInfo(data) ? gameInfoPanel(doc, data) : null),
		(el) => {
			if (!gameInfo) return false;
			el.classList.add('ss-bp');
			gameInfo.prepend(el);
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
					// Steam's own toast (in the game's overlay while it runs, with Steam's sound); ours if that fails.
					if (a && !showSteamAchievementToast(appid, { ...a, unlockedAt: unlocks[id] ?? null })) showUnlockToast(doc, a, appName(appid));
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

/** The plugin's page in Millennium's settings: the look of the plugin's own windows. */
function SettingsPage() {
	const R = (globalThis as any).SP_REACT;
	const [style, set] = R.useState(currentStyle());
	R.useEffect(() => onStyleChange((s: Style) => set(s)), []);
	return (
		<div style={{ padding: 16, lineHeight: 1.5 }}>
			<p>Descriptions, details and achievements for non-Steam games are edited in the Shortcut Studio app.</p>
			<p>Open a non-Steam game in your library to see its details, and use View My Achievements to lock or unlock achievements.</p>
			<DropdownItem
				label="Style"
				description="Colours of the plugin's own windows: the achievement list, launch-option dialog, unlock popup and achievement tiles. Auto uses Simply Dark when that Millennium theme is active. Also in the app's Settings."
				rgOptions={STYLES.map((o) => ({ data: o.data, label: o.label }))}
				selectedOption={style}
				onChange={(o) => setStyle(o.data as Style)}
			/>
		</div>
	);
}

export default definePlugin(() => {
	try {
		startStyle();
		onStyleChange(() => changed());
	} catch (e) {
		console.error(TAG, 'style setting failed to start', e);
	}
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
				if (state && currentAppId() === state.appid) revalidate(state.appid);
			}
			tick();
		} catch (e) {
			console.error(TAG, 'tick failed', e);
		}
	}, POLL_MS);

	// Live page: while a non-Steam game's page is shown, check its files' fingerprint and reload when anything
	// changes (an app save, an unlock, a game's autosave moving a progress bar). No navigating away and back.
	let checking = false;
	setInterval(async () => {
		if (checking || !state || state.loading) return;
		checking = true;
		try {
			await revalidate(state.appid);
		} finally {
			checking = false;
		}
	}, STAMP_MS);

	// Steam renders a game page a moment after navigating; draw onto it as soon as it appears instead of
	// waiting for the next poll. Mutation callbacks run before the browser paints, so Steam's plain page never
	// shows. Our own changes trigger one more call, which finds everything already in place.
	let observed: Document | null = null;
	let ticking = false;
	const observer = new MutationObserver(() => {
		if (ticking) return;
		ticking = true;
		try {
			tick();
		} catch (e) {
			console.error(TAG, 'tick failed', e);
		} finally {
			ticking = false;
		}
	});
	setInterval(() => {
		const doc = libraryWindow()?.document ?? null;
		if (doc === observed) return;
		observer.disconnect();
		observed = doc;
		if (doc?.body) observer.observe(doc.body, { childList: true, subtree: true });
	}, 1000);

	setTimeout(() => preload().catch((e) => console.error(TAG, 'preloading game data failed', e)), 3000);

	return {
		title: 'Shortcut Studio',
		icon: <span style={{ fontWeight: 700 }}>SS</span>,
		content: <SettingsPage />,
	};
});
