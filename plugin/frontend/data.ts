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

/** Changes whenever anything the game's page shows changes on disk; "" if it couldn't be read. */
export async function loadStamp(appid: number): Promise<string> {
	try {
		const s = await stampRpc({ payload: JSON.stringify({ appid: String(appid) }) });
		return typeof s === 'string' ? s : '';
	} catch {
		return '';
	}
}

/** The shortcut's portrait library capsule as a data: URI, or "" if it has none. */
export async function loadCapsule(appid: number): Promise<string> {
	try {
		const uri = await capsuleRpc({ payload: JSON.stringify({ appid: String(appid) }) });
		return typeof uri === 'string' && uri.startsWith('data:') ? uri : '';
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

export async function loadGame(appid: number): Promise<GameData | null> {
	const raw = JSON.parse(await getGame({ payload: JSON.stringify({ appid: String(appid) }) }));
	if (!raw || typeof raw !== 'object') return null;
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
