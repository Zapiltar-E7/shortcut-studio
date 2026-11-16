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
// Big Picture's game page: tabs (Activity / Your stuff / Community / Game Info) under the play section.
export const bpMedia = find((m) => m.ScreenshotsSection && m.NoRecent);
export const bpGameInfo = find((m) => m.GameInfoContainer && m.GameInfoQuickLinks);
export const bpTabs = find((m) => m.TabRowTabs && m.TabContentsScroll && m.GamepadTabbedPage);

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

const DESKTOP = 'SP Desktop_uid0';
const BIG_PICTURE = 'SP BPM_uid0';
const popupWindow = (name: string): Window | null => g.g_PopupManager?.GetExistingPopup?.(name)?.window ?? null;

/**
 * The window game pages render in: Big Picture's while it is open and showing, otherwise the desktop library.
 * (Steam closes the desktop window when Big Picture opens, and reopens it when Big Picture exits.)
 */
export function libraryWindow(): Window | null {
	const bp = popupWindow(BIG_PICTURE);
	const desktop = popupWindow(DESKTOP);
	if (bp && (!desktop || bp.document.visibilityState === 'visible')) return bp;
	return desktop ?? bp;
}

/** True when `win` is Big Picture's window (controller UI). */
export function isBigPicture(win: Window | null | undefined): boolean {
	return !!win && win === popupWindow(BIG_PICTURE);
}

export function formatDate(ms: number): string {
	return new Date(ms).toLocaleDateString(navigator.languages as string[], { year: 'numeric', month: 'short', day: 'numeric' });
}
