import { findClassModule } from '@steambrew/client';

// Steam's CSS-module class maps, found by keys only that module has (checked against this Steam build's sp.js).
// Using Steam's own classes makes the injected panels pick up the client's (and the active theme's) styling.
type Classes = Record<string, string>;
const find = (filter: (m: any) => boolean): Classes => {
	try {
		return (findClassModule(filter) as Classes | undefined) ?? {};
	} catch {
		return {};
	}
};

export const playBar = find((m) => m.AppButtonsContainer && m.GameInfoButton && m.InPage);
export const columns = find((m) => m.ColumnContainer && m.RightColumn && m.SeekTarget);
export const section = find((m) => m.AppDetailsSectionContainer && m.RightColumnSection);
export const header = find((m) => m.SectionHeader && m.PadLeft && m.LabelText);
export const ach = find((m) => m.RightAchievementSection && m.UnachievedSection);
export const achIcon = find((m) => m.AchievementIconWrapper && m.RareAchievementIconGlow);
export const playButton = find((m) => m.PlayButtonContainer && m.StreamingSelector && m.ShowingStreaming);
export const appButton =find((m) => m.AppDetailsButton && m.BottomRight);
export const desc =find((m) => m.GameDescription && m.AssociationList);

/** Joins class names, skipping lookups that came back undefined. */
export const cx = (...names: unknown[]) => names.filter((n) => typeof n === 'string' && n).join(' ');

/** First class of a lookup as a CSS selector, or null if Steam didn't have it. */
export const sel = (cls?: string) => (cls ? '.' + cls.split(' ')[0] : null);

export const g = globalThis as any;

/** The appid in the library route (/library/app/123), or null elsewhere. */
export function currentAppId(): number | null {
	const path: string | undefined = g.MainWindowBrowserManager?.m_lastLocation?.pathname;
	const m = path?.match(/^\/library\/app\/(\d+)/);
	return m ? Number(m[1]) : null;
}

// Shortcuts get appids with the high bit set, and app_type 0x40000000.
export function isShortcut(appid: number): boolean {
	return appid >= 0x80000000 || g.appStore?.GetAppOverviewByAppID?.(appid)?.app_type === 0x40000000;
}

export function appName(appid: number): string {
	return g.appStore?.GetAppOverviewByAppID?.(appid)?.display_name ?? '';
}

/** The main library window (where game pages render), if open. */
export function libraryWindow(): Window | null {
	return g.g_PopupManager?.GetExistingPopup?.('SP Desktop_uid0')?.window ?? null;
}

export function formatDate(ms: number): string {
	return new Date(ms).toLocaleDateString(navigator.languages as string[], { year: 'numeric', month: 'short', day: 'numeric' });
}
