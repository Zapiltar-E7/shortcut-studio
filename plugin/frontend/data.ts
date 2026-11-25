import { callable } from '@steambrew/client';

export interface Achievement {
	id: string;
	name: string;
	description: string;
	hidden: boolean;
	percent: number | null;
	/** PlayStation grade for emulator trophies: P, G, S, B, or "". */
	grade: string;
	/** RetroAchievements points, or null. */
	points: number | null;
	/** Achievement set: "" for the base game, otherwise the DLC's name (Steam's "DLC: …" sets). */
	dlc: string;
	/** data: URI or "" */
	icon: string;
	iconGray: string;
	/** Unix ms, or null while locked. */
	unlockedAt: number | null;
	/** Steam-style progress toward a locked achievement ("12 / 900"), or null. */
	progress: { current: number; max: number } | null;
}

export interface GameData {
	shortDescription: string;
	description: string;
	developer: string;
	publisher: string;
	releaseDate: string;
	genres: string[];
	sourceAppId: number;
	/** Set when unlocks come live from an emulator; then they can't be toggled by hand. */
	trophySource: { emulator: string; title: string; npCommId: string } | null;
	achievements: Achievement[];
}

export const emulatorName = (e?: string) => (e === 'rpcs3' ? 'RPCS3' : e === 'retroachievements' ? 'RetroAchievements' : e === 'ps3recomp' ? 'the PC port' : e === 'savefile' ? 'your save files' : e === 'dusklight' ? 'Dusklight' : 'shadPS4');

export interface AchievementSet {
	/** "" for the base game. */
	dlc: string;
	label: string;
	achievements: Achievement[];
	done: number;
	total: number;
}

/** Steam's achievement sets: the base game first, then each DLC in the order it first appears. */
export function achievementSets(data: GameData): AchievementSet[] {
	const byName = new Map<string, Achievement[]>([['', []]]);
	for (const a of data.achievements) {
		if (!byName.has(a.dlc)) byName.set(a.dlc, []);
		byName.get(a.dlc)!.push(a);
	}
	const out: AchievementSet[] = [];
	for (const [dlc, achievements] of byName) {
		if (!achievements.length) continue;
		out.push({ dlc, label: dlc ? `DLC: ${dlc}` : 'Base Game', achievements, done: achievements.filter((a) => a.unlockedAt != null).length, total: achievements.length });
	}
	return out;
}

// The Lua side takes and returns one JSON string (see backend/main.lua).
const getGame = callable<[{ payload: string }], string>('GetGame');
const setUnlockedRpc = callable<[{ payload: string }], string>('SetUnlocked');
const pollRpc = callable<[{ payload: string }], string>('PollUnlocks');
const capsuleRpc = callable<[{ payload: string }], string>('GetCapsule');
const stampRpc = callable<[{ payload: string }], string>('GetStamp');

const listRpc = callable<[{ payload: string }], string>('ListGames');

/**
 * A backend reply that is plain text (not JSON). Millennium JSON-encodes plain strings on the way back, so they
 * arrive wrapped in quotes ("\"data:…\""); unwrap those. Replies that are JSON objects come through as-is.
 */
export function text(reply: unknown): string {
	if (typeof reply !== 'string') return '';
	if (reply.length >= 2 && reply.startsWith('"') && reply.endsWith('"')) {
		try {
			const s = JSON.parse(reply);
			if (typeof s === 'string') return s;
		} catch {
			/* not JSON after all: use it as it is */
		}
	}
	return reply;
}

/** The appids of every game the app has details for. */
export async function listGames(): Promise<number[]> {
	try {
		const raw = JSON.parse(await listRpc({ payload: '{}' }));
		return (Array.isArray(raw) ? raw : raw && typeof raw === 'object' ? Object.values(raw) : []).map(Number).filter((n) => Number.isFinite(n) && n > 0);
	} catch {
		return [];
	}
}

/** Changes whenever anything the game's page shows changes on disk; "" if it couldn't be read. */
export async function loadStamp(appid: number): Promise<string> {
	try {
		const s = text(await stampRpc({ payload: JSON.stringify({ appid: String(appid) }) }));
		return s;
	} catch {
		return '';
	}
}

/** The shortcut's portrait library capsule as a data: URI, or "" if it has none. */
export async function loadCapsule(appid: number): Promise<string> {
	try {
		const uri = text(await capsuleRpc({ payload: JSON.stringify({ appid: String(appid) }) }));
		return uri.startsWith('data:') ? uri : '';
	} catch {
		return '';
	}
}

/** For every emulator-synced game: { appid: { achievementId: unlockMs } }. */
export async function pollUnlocks(running: number[] = []): Promise<Record<string, Record<string, number>>> {
	const raw = JSON.parse(await pollRpc({ payload: JSON.stringify({ running: running.map(String) }) }));
	const out: Record<string, Record<string, number>> = {};
	if (raw && typeof raw === 'object') {
		for (const [appid, map] of Object.entries(raw)) {
			out[appid] = {};
			if (map && typeof map === 'object') for (const [id, t] of Object.entries(map as object)) if (typeof t === 'number') out[appid][id] = t;
		}
	}
	return out;
}

// Lua's JSON encoder writes empty arrays as {} and null as missing, so coerce everything.
const list = (v: unknown): any[] => (Array.isArray(v) ? v : v && typeof v === 'object' ? Object.values(v) : []);
const str = (v: unknown) => (typeof v === 'string' ? v : '');

const iconsRpc = callable<[{ payload: string }], string>('GetIcons');

/**
 * Fills in the icons a batch at a time. Millennium refuses backend replies over 8 MB, so the backend sends as many
 * as fit in a few MB and says how many ids it got through; this asks again from there. An icon whose batch fails
 * stays on the plain trophy icon.
 */
async function loadIcons(appid: number, achievements: Achievement[]) {
	const byId = new Map(achievements.map((a) => [a.id, a]));
	const ids = achievements.map((a) => a.id);
	let i = 0;
	while (i < ids.length) {
		let done = 0;
		try {
			const raw = JSON.parse(await iconsRpc({ payload: JSON.stringify({ appid: String(appid), ids: ids.slice(i) }) }));
			const icons = raw && typeof raw.icons === 'object' ? raw.icons : {};
			for (const [id, r] of Object.entries(icons as Record<string, any>)) {
				const a = byId.get(id);
				if (a && r && typeof r === 'object') {
					a.icon = str(r.icon);
					a.iconGray = str(r.iconGray);
				}
			}
			done = Number(raw?.done) || 0;
		} catch (e) {
			console.error('[Shortcut Studio] loading achievement icons failed', e);
		}
		// Never stall: a failed or empty reply skips one achievement.
		i += Math.max(1, done);
	}
}

/** quiet: a background preload, so the backend skips the RetroAchievements refresh a page visit does. */
export async function loadGame(appid: number, quiet = false): Promise<GameData | null> {
	// The icons come separately, in batches: all of them in one reply was too big for games with hundreds of
	// achievements, and the page then showed nothing at all (issue #1).
	const raw = JSON.parse(await getGame({ payload: JSON.stringify({ appid: String(appid), quiet, noIcons: true }) }));
	if (!raw || typeof raw !== 'object') return null;
	const data = toGameData(raw);
	await loadIcons(appid, data.achievements);
	return data;
}

function toGameData(raw: any): GameData {
	return {
		shortDescription: str(raw.shortDescription),
		description: str(raw.description),
		developer: str(raw.developer),
		publisher: str(raw.publisher),
		releaseDate: str(raw.releaseDate),
		genres: list(raw.genres).map(str).filter(Boolean),
		sourceAppId: Number(raw.sourceAppId) || 0,
		trophySource: raw.trophySource && typeof raw.trophySource === 'object' ? { emulator: str(raw.trophySource.emulator), title: str(raw.trophySource.title), npCommId: str(raw.trophySource.npCommId) } : null,
		achievements: list(raw.achievements).map((a) => ({
			id: str(a?.id),
			name: str(a?.name),
			description: str(a?.description),
			hidden: Boolean(a?.hidden),
			percent: typeof a?.percent === 'number' ? a.percent : null,
			grade: str(a?.grade),
			points: typeof a?.points === 'number' ? a.points : null,
			dlc: str(a?.dlc),
			icon: str(a?.icon),
			iconGray: str(a?.iconGray),
			unlockedAt: typeof a?.unlockedAt === 'number' ? a.unlockedAt : null,
			progress: typeof a?.progressMax === 'number' && a.progressMax > 0 ? { current: Math.max(0, Number(a.progressCurrent) || 0), max: a.progressMax } : null,
		})),
	};
}

/** Returns the stored unlock time (or null when locked), or undefined if saving failed. */
export async function setUnlocked(appid: number, id: string, unlocked: boolean): Promise<number | null | undefined> {
	const res = JSON.parse(await setUnlockedRpc({ payload: JSON.stringify({ appid: String(appid), id, unlocked }) }));
	if (!res || typeof res !== 'object') return undefined;
	return typeof res.unlockedAt === 'number' ? res.unlockedAt : null;
}
