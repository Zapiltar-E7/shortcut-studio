import { callable } from '@steambrew/client';

const TAG = '[shortcut-studio]';
const tickRpc = callable<[{ payload: string }], string>('LiveTick');
const doneRpc = callable<[{ payload: string }], string>('LiveDone');
const imageRpc = callable<[{ payload: string }], string>('ReadImage');

/** Steam's ELibraryAssetType for each of our art slots (the icon is set by path instead). */
const ASSET: Record<string, number> = { capsule: 0, hero: 1, logo: 2, wide: 3 };

interface Op {
	op: 'add' | 'update' | 'remove' | 'art' | 'artClear' | 'logo' | 'icon' | 'hidden';
	appid?: number;
	name?: string;
	exe?: string;
	startDir?: string;
	launchOptions?: string;
	kind?: string;
	path?: string;
	pinned?: string;
	widthPct?: number;
	heightPct?: number;
	hidden?: boolean;
}

interface Result {
	op: string;
	appid?: number;
	newAppid?: number;
	error?: string;
}

const g = globalThis as any;
let busy = false;
const listeners = new Set<(appids: number[]) => void>();
export const onLiveApplied = (fn: (appids: number[]) => void) => listeners.add(fn);

async function apply(op: Op): Promise<Result> {
	const apps = g.SteamClient?.Apps;
	if (!apps) throw new Error('SteamClient.Apps is not available');
	const id = op.appid ?? 0;
	switch (op.op) {
		case 'add': {
			const newAppid: number = await apps.AddShortcut(op.name ?? '', op.exe ?? '', op.startDir ?? '', op.launchOptions ?? '');
			// Steam names new shortcuts after the exe; set ours explicitly.
			if (op.name) apps.SetShortcutName(newAppid, op.name);
			return { op: op.op, newAppid };
		}
		case 'update':
			if (op.name !== undefined) apps.SetShortcutName(id, op.name);
			if (op.exe !== undefined) apps.SetShortcutExe(id, op.exe);
			if (op.startDir !== undefined) apps.SetShortcutStartDir(id, op.startDir);
			if (op.launchOptions !== undefined) apps.SetShortcutLaunchOptions(id, op.launchOptions);
			return { op: op.op, appid: id };
		case 'icon':
			apps.SetShortcutIcon(id, op.path ?? '');
			return { op: op.op, appid: id };
		case 'hidden': {
			const cs = g.collectionStore;
			if (!cs?.SetAppsAsHidden) throw new Error('Hiding apps is not supported by this Steam version');
			cs.SetAppsAsHidden([id], !!op.hidden);
			return { op: op.op, appid: id };
		}
		case 'remove':
			apps.RemoveShortcut(id);
			return { op: op.op, appid: id };
		case 'art': {
			const type = ASSET[op.kind ?? ''];
			if (type === undefined) throw new Error(`Unknown art kind ${op.kind}`);
			const b64 = await imageRpc({ payload: JSON.stringify({ path: op.path }) });
			if (!b64) throw new Error(`Couldn't read ${op.path}`);
			const ext = /\.jpe?g$/i.test(op.path ?? '') ? 'jpg' : 'png';
			await apps.SetCustomArtworkForApp(id, b64, ext, type);
			return { op: op.op, appid: id };
		}
		case 'artClear': {
			const type = ASSET[op.kind ?? ''];
			if (type !== undefined) await apps.ClearCustomArtworkForApp(id, type);
			return { op: op.op, appid: id };
		}
		case 'logo':
			await apps.SetCustomLogoPositionForApp(
				id,
				JSON.stringify({ nVersion: 1, logoPosition: { pinnedPosition: op.pinned ?? 'BottomLeft', nWidthPct: op.widthPct ?? 50, nHeightPct: op.heightPct ?? 50 } }),
			);
			return { op: op.op, appid: id };
	}
	throw new Error(`Unknown op ${(op as Op).op}`);
}

/** Takes one request from the app (if any), applies every op through Steam's API, and reports back. */
async function tick() {
	if (busy) return;
	busy = true;
	try {
		const raw = await tickRpc({ payload: '{}' });
		const req = raw && raw !== 'null' ? JSON.parse(raw) : null;
		if (!req?.id) return;
		const results: Result[] = [];
		for (const op of (req.ops ?? []) as Op[]) {
			try {
				results.push(await apply(op));
			} catch (e: any) {
				results.push({ op: op.op, appid: op.appid, error: String(e?.message ?? e) });
			}
		}
		await doneRpc({ payload: JSON.stringify({ id: req.id, result: { ok: results.every((r) => !r.error), results } }) });
		console.log(TAG, 'applied live changes', req.id, results);
		const touched = results.map((r) => r.newAppid ?? r.appid).filter((n): n is number => typeof n === 'number');
		listeners.forEach((fn) => fn(touched));
	} catch (e) {
		console.error(TAG, 'live tick failed', e);
	} finally {
		busy = false;
	}
}

export function startLive() {
	setInterval(tick, 1500);
}
