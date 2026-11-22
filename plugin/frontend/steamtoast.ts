/**
 * Steam's own "Achievement Unlocked" toast. Steam's notification store shows client notifications of type 5
 * (an achievement) with its achievement sound, and while a game is running it draws them in that game's
 * overlay. The data is Steam's CAchievementNotification protobuf, which is small enough to encode here:
 *   1 achievement_id (string), 2 appid (uint32), 3 name, 4 description, 5 image_url (strings),
 *   6 achieved (bool), 7 rtime_unlocked (uint32), 8-11 min/current/max progress and global % (floats).
 * Returns false when Steam's store isn't there, so the caller can fall back to our own popup.
 */
const ACHIEVEMENT = 5;
let nextId = 0x5c000000; // well away from the ids Steam numbers its own notifications with

function varint(out: number[], v: number) {
	v >>>= 0;
	while (v > 0x7f) {
		out.push((v & 0x7f) | 0x80);
		v >>>= 7;
	}
	out.push(v);
}

function encode(fields: { appid: number; id: string; name: string; description: string; image: string; unlockedAt: number }) {
	const out: number[] = [];
	const text = new TextEncoder();
	const str = (n: number, s: string) => {
		if (!s) return;
		const b = text.encode(s);
		varint(out, (n << 3) | 2);
		varint(out, b.length);
		for (const x of b) out.push(x);
	};
	const uint = (n: number, v: number) => {
		varint(out, n << 3);
		varint(out, v);
	};
	const float = (n: number, v: number) => {
		varint(out, (n << 3) | 5);
		const b = new Uint8Array(new Float32Array([v]).buffer);
		for (const x of b) out.push(x);
	};
	str(1, fields.id);
	uint(2, fields.appid);
	str(3, fields.name);
	str(4, fields.description);
	str(5, fields.image);
	uint(6, 1);
	uint(7, Math.floor(fields.unlockedAt / 1000));
	float(11, 0);
	return new Uint8Array(out);
}

export function showSteamAchievementToast(appid: number, a: { id: string; name: string; description: string; icon: string; unlockedAt: number | null }): boolean {
	const store = (window as any).NotificationStore;
	if (typeof store?.OnNotification !== 'function') return false;
	try {
		const bytes = encode({ appid, id: a.id, name: a.name, description: a.description, image: a.icon, unlockedAt: a.unlockedAt ?? Date.now() });
		store.OnNotification(nextId++, ACHIEVEMENT, bytes);
		return true;
	} catch (e) {
		console.warn('[Shortcut Studio] Steam achievement toast failed', e);
		return false;
	}
}
