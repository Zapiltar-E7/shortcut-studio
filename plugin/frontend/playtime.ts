import { beforePatch, callable } from '@steambrew/client';
import { g, isShortcut } from './steam';

/**
 * Steam records playtime for non-Steam games but only shows it on the game page. The library's own
 * widgets (Recent Games hover card, "time played" sorting) read the app overview, which stays at 0 for
 * shortcuts. This copies Steam's recorded numbers (read from localconfig.vdf by the backend) onto them.
 */
const TAG = '[shortcut-studio]';
const getRpc = callable<[{ payload: string }], string>('GetSteamPlaytimes');

interface Playtime {
	minutesForever: number;
	minutesLastTwoWeeks: number;
	lastPlayed: number;
}
const cache = new Map<number, Playtime>();

function apply(o: any, p: Playtime) {
	if (!o) return;
	// Never lower what Steam already shows (e.g. while a session is still being counted).
	if (!(o.minutes_playtime_forever > p.minutesForever)) o.minutes_playtime_forever = p.minutesForever;
	if (!(o.minutes_playtime_last_two_weeks > p.minutesLastTwoWeeks)) o.minutes_playtime_last_two_weeks = p.minutesLastTwoWeeks;
	if (p.lastPlayed && !(o.rt_last_time_played > p.lastPlayed)) o.rt_last_time_played = p.lastPlayed;
}

function shortcutIds(): number[] {
	let apps: any[] = [];
	try {
		apps = g.appStore?.allApps ?? Array.from(g.appStore?.m_mapApps?.values?.() ?? []);
	} catch {
		apps = [];
	}
	return apps.map((a) => a?.appid).filter((id) => typeof id === 'number' && isShortcut(id));
}

async function refresh() {
	const ids = shortcutIds();
	if (!ids.length) return false;
	try {
		const raw = JSON.parse(await getRpc({ payload: JSON.stringify({ appids: ids.map(String) }) })) ?? {};
		for (const [id, p] of Object.entries(raw) as [string, any][]) {
			const pt = { minutesForever: Number(p?.minutesForever) || 0, minutesLastTwoWeeks: Number(p?.minutesLastTwoWeeks) || 0, lastPlayed: Number(p?.lastPlayed) || 0 };
			cache.set(Number(id), pt);
			apply(g.appStore?.GetAppOverviewByAppID?.(Number(id)), pt);
		}
	} catch (e) {
		console.error(TAG, 'reading Steam playtimes failed', e);
	}
	return true;
}

export function startPlaytime() {
	// Steam rebuilds overviews from its own data now and then; put the numbers back each time.
	try {
		if (g.collectionStore?.OnAppOverviewChange) {
			beforePatch(g.collectionStore, 'OnAppOverviewChange', ([apps]: any[]) => {
				for (const o of apps ?? []) {
					const p = typeof o?.appid === 'number' ? cache.get(o.appid) : undefined;
					if (p) apply(o, p);
				}
			});
		}
	} catch (e) {
		console.warn(TAG, 'could not patch app overviews', e);
	}

	// The library may still be loading when plugins start.
	const first = async (attempt = 0) => {
		if (!(await refresh()) && attempt < 20) setTimeout(() => first(attempt + 1), 1500);
	};
	first();

	// Re-read after a non-Steam game closes (Steam saves its time then), and every few minutes.
	let wasRunning = new Set<number>();
	setInterval(() => {
		let now = new Set<number>();
		try {
			now = new Set((Array.from(g.SteamUIStore?.RunningApps ?? []) as any[]).map((a) => a?.appid).filter((id) => typeof id === 'number' && isShortcut(id)));
		} catch {}
		const stopped = [...wasRunning].some((id) => !now.has(id));
		wasRunning = now;
		if (stopped) setTimeout(refresh, 5000);
	}, 5000);
	setInterval(refresh, 5 * 60 * 1000);
}
