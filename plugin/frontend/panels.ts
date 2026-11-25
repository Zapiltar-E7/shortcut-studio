import { achievementSets, emulatorName, type Achievement, type AchievementSet, type GameData } from './data';
import { applyStyle, lastPalette } from './style';
import { ach, achIcon, appButton, cx, desc, header, isBigPicture, section } from './steam';
import { FocusTree, refocus } from './bpnav';

/** Tiny DOM builder: h(doc, 'div', { class: 'x' }, child, 'text'). */
export function h(doc: Document, tag: string, attrs: Record<string, string | undefined> = {}, ...children: (Node | string | null | undefined | false)[]): HTMLElement {
	const el = doc.createElement(tag);
	for (const [k, v] of Object.entries(attrs)) if (v !== undefined && v !== '') el.setAttribute(k, v);
	for (const c of children) if (c !== null && c !== undefined && c !== false) el.append(typeof c === 'string' ? doc.createTextNode(c) : c);
	return el;
}

export const CSS = `
.ss-section { margin-bottom: 16px; }
/* The achievements section uses Steam's own classes; these fill in Steam's generic layout boxes (Box/Grid). */
.ss-pbar { display: grid; height: 10px; border-radius: 4px; overflow: hidden; background: var(--ss-raised); }
.ss-pbar > .ss-done { background: #1a9fff; }
.ss-icons { display: grid; grid-template-columns: repeat(auto-fill, 48px); grid-template-rows: 48px; grid-auto-rows: 0; row-gap: 0; column-gap: 4px; overflow: hidden; }
.ss-icon { width: 48px; height: 48px; background: var(--ss-tile) center/cover no-repeat; cursor: pointer; position: relative; }
.ss-icon.ss-more { box-sizing: border-box; display: flex; align-items: center; justify-content: center; border: 1px solid var(--ss-sep); background: none; font-size: 14px; font-weight: 700; color: #fff; }
.ss-icon.ss-locked { filter: grayscale(1) brightness(.75); }
.ss-icon.ss-hidden-lock { display: flex; align-items: center; justify-content: center; font-size: 22px; color: var(--ss-faint); background: var(--ss-tile-muted); }
.ss-feat-icon { width: 48px; height: 48px; flex-shrink: 0; background: var(--ss-tile) center/cover no-repeat; }
.ss-view { align-self: flex-end; }
/* Big Picture: controller focus on View My Achievements. Steam's own focused style for this button only shrinks the
   text, so match its other focused buttons instead: white, with normal-weight grey text at the same size. */
.ss-section.ss-bp button.DialogButton.ss-view.gpfocus { background: #fff; color: var(--ss-raised); font-weight: 400; font-size: 16px; line-height: 20px; padding: 10px 24px; }
.ss-info { padding: 14px 16px 18px; display: flex; gap: 28px; color: var(--ss-info); }
.ss-info-main { flex: 1; min-width: 0; font-size: 13px; line-height: 1.55; }
.ss-info-main .ss-short { font-size: 15px; color: var(--ss-text); margin-bottom: 12px; }
.ss-info-main .ss-about { max-height: 360px; overflow-y: auto; padding-right: 8px; }
.ss-info-main .ss-about h2 { font-size: 14px; color: #fff; text-transform: uppercase; letter-spacing: .04em; margin: 14px 0 6px; }
.ss-info-main .ss-about ul { padding-left: 18px; margin: 6px 0; }
.ss-info-main .ss-about a { color: #67c1f5; }
.ss-info-side { width: 260px; flex-shrink: 0; font-size: 12px; }
.ss-info-row { margin-bottom: 10px; }
.ss-info-row .ss-k { color: var(--ss-dim); font-size: 11px; letter-spacing: .06em; text-transform: uppercase; }
.ss-info-row .ss-v { color: #67c1f5; font-size: 13px; }
/* Steam's title bar is a window drag area: Windows takes clicks there before the page sees them, so in a short
   window the close X (which pokes above the dialog) only worked on its lower edge. Opt the modal out of dragging. */
.ss-modal { position: fixed; inset: 0; z-index: 9999; -webkit-app-region: no-drag; background: rgba(0, 0, 0, .72); display: flex; align-items: center; justify-content: center; }
.ss-dialog { position: relative; width: min(1200px, 94vw); height: min(900px, 92vh); display: flex; flex-direction: column; background: var(--ss-dialog); box-shadow: 0 0 48px rgba(0,0,0,.75); color: var(--ss-text); border-radius: 4px; }
.ss-scroll { flex: 1; overflow-y: auto; border-radius: 4px; }
.ss-top { position: relative; padding: 40px 36px 24px 16px; overflow: hidden; }
.ss-top-bg { position: absolute; inset: -30px; background: var(--ss-art-fallback) center/cover no-repeat; filter: blur(22px) brightness(.38) saturate(1.05); }
.ss-top::after { content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 60%; background: linear-gradient(180deg, var(--ss-dialog-0) 0%, var(--ss-dialog) 100%); }
.ss-top > :not(.ss-top-bg) { position: relative; z-index: 1; }
.ss-head { display: flex; gap: 16px; align-items: center; }
.ss-capsule { width: 146px; height: 219px; flex-shrink: 0; background: var(--ss-tile) center/cover no-repeat; box-shadow: 0 4px 16px rgba(0,0,0,.6); }
.ss-capsule.ss-none { display: flex; align-items: center; justify-content: center; text-align: center; padding: 10px; font-size: 13px; color: var(--ss-dim); }
.ss-head-main { flex: 1; min-width: 0; }
.ss-title { font-size: 26px; font-weight: 500; color: #fff; line-height: 1.2; margin-bottom: 4px; }
.ss-subline { font-size: 12px; color: var(--ss-text-2); margin-bottom: 18px; }
.ss-subline .ss-sep { display: inline-block; width: 1px; height: 12px; background: var(--ss-sep); margin: 0 9px; vertical-align: -1px; }
.ss-earned { background: var(--ss-panel); padding: 16px 16px 18px; }
.ss-earned-row { font-size: 15px; color: var(--ss-text); margin-bottom: 10px; }
.ss-earned-row .ss-pct { color: var(--ss-dim); }
.ss-earned-bar { height: 10px; border-radius: 5px; background: var(--ss-track); overflow: hidden; }
.ss-earned-bar > div { height: 100%; border-radius: 5px; background: #1a9fff; }
.ss-sync-note { font-size: 12px; color: #8fd08f; margin-top: 12px; }
.ss-controls { display: flex; align-items: center; gap: 22px; padding: 18px 36px 14px 16px; position: sticky; top: 0; z-index: 2; background: var(--ss-dialog); }
.ss-switch { display: flex; align-items: center; gap: 10px; font-size: 14px; color: var(--ss-text); cursor: pointer; user-select: none; }
.ss-switch-track { position: relative; width: 38px; height: 22px; border-radius: 11px; background: var(--ss-raised); transition: background .15s; }
.ss-switch-track::after { content: ''; position: absolute; top: 2px; left: 2px; width: 18px; height: 18px; border-radius: 50%; background: #fff; transition: transform .15s; }
.ss-switch.ss-on .ss-switch-track { background: #1a9fff; }
.ss-switch.ss-on .ss-switch-track::after { transform: translateX(16px); }
.ss-spacer { flex: 1; }
.ss-filter { position: relative; font-size: 14px; color: var(--ss-text); cursor: pointer; user-select: none; padding: 8px 4px; }
.ss-filter::after { content: ''; display: inline-block; width: 6px; height: 6px; margin-left: 10px; border-right: 1.5px solid var(--ss-text); border-bottom: 1.5px solid var(--ss-text); transform: rotate(45deg); vertical-align: 3px; }
.ss-menu { position: absolute; right: 0; top: 100%; min-width: 180px; background: var(--ss-raised); box-shadow: 0 4px 16px rgba(0,0,0,.5); z-index: 3; padding: 4px 0; }
.ss-menu > div { padding: 8px 14px; font-size: 14px; color: var(--ss-text); }
.ss-menu > div:hover { background: var(--ss-text); color: var(--ss-dialog); }
.ss-searchbox { position: relative; }
.ss-search { width: 198px; background: var(--ss-input); border: 1px solid transparent; color: var(--ss-text); padding: 9px 34px 9px 10px; font-size: 14px; font-style: italic; border-radius: 2px; outline: none; }
.ss-search:focus { border-color: #1a9fff; font-style: normal; }
.ss-search::placeholder { color: var(--ss-dim); }
.ss-searchbox::after { content: ''; position: absolute; right: 12px; top: 50%; width: 9px; height: 9px; border: 2px solid var(--ss-text); border-radius: 50%; transform: translateY(-65%); pointer-events: none; }
.ss-searchbox::before { content: ''; position: absolute; right: 9px; top: 58%; width: 6px; height: 2px; background: var(--ss-text); transform: rotate(45deg); pointer-events: none; }
.ss-list { padding: 0 36px 28px 16px; }
.ss-group { font-size: 13px; letter-spacing: .04em; text-transform: uppercase; color: var(--ss-dim); padding: 22px 0 8px 8px; }
.ss-set { margin-top: 26px; padding: 14px 16px 16px; background: var(--ss-panel); border-radius: 2px; }
.ss-set-head { display: flex; align-items: baseline; gap: 12px; margin-bottom: 10px; }
.ss-set-name { flex: 1; min-width: 0; font-size: 17px; color: #fff; }
.ss-set-count { font-size: 13px; color: var(--ss-dim); white-space: nowrap; }
.ss-set-bar { height: 6px; border-radius: 3px; background: var(--ss-track); overflow: hidden; }
.ss-set-bar > div { height: 100%; border-radius: 3px; background: #1a9fff; }
.ss-set.ss-complete .ss-set-bar > div { background: #e5b143; }
.ss-dlc-tag { display: inline-block; font-size: 10px; font-weight: 700; letter-spacing: .08em; padding: 2px 6px; margin-right: 8px; border-radius: 2px; vertical-align: 2px; color: var(--ss-dialog); background: #b8a3ff; }
.ss-menu .ss-dlc-tag { font-size: 9px; }
.ss-sets { display: flex; gap: 14px; font-size: 12px; color: var(--ss-dim); margin: 8px 0 2px; }
.ss-ach { display: flex; align-items: center; gap: 8px; padding: 8px 30px 8px 8px; min-height: 79px; box-sizing: border-box; border-bottom: 1px solid var(--ss-line); }
.ss-ach-icon { width: 54px; height: 54px; flex-shrink: 0; background: var(--ss-tile) center/cover no-repeat; }
.ss-ach-text { flex: 1; min-width: 0; padding-left: 4px; }
.ss-ach-name { font-size: 15px; font-weight: 600; color: #fff; line-height: 1.35; }
.ss-ach-desc { font-size: 13px; color: var(--ss-text-2); line-height: 1.4; }
.ss-ach-side { font-size: 13px; color: var(--ss-dim); text-align: right; white-space: nowrap; }
.ss-prog { width: 200px; margin-left: auto; }
.ss-prog-text { font-size: 13px; color: var(--ss-text-2); text-align: right; padding-right: 2px; margin-bottom: 5px; }
.ss-prog-bar { height: 8px; border-radius: 4px; background: var(--ss-track); overflow: hidden; }
.ss-prog-bar > div { height: 100%; border-radius: 4px; background: #1a9fff; }
.ss-ach-global {display: flex; align-items: center; gap: 10px; justify-content: flex-end; margin-top: 4px; font-size: 12px; color: var(--ss-dim); }
.ss-ach-bar { width: 120px; height: 6px; border-radius: 3px; background: var(--ss-line); overflow: hidden; }
.ss-ach-bar > div { height: 100%; background: var(--ss-faint); }
.ss-ach.ss-locked .ss-ach-icon { filter: grayscale(1) brightness(.7); }
.ss-hiddenrow { cursor: pointer; }
.ss-hiddenrow .ss-ach-icon { display: flex; align-items: center; justify-content: center; font-size: 24px; color: var(--ss-faint); background: var(--ss-tile-muted); }
.ss-hiddenrow .ss-ach-desc { color: #67c1f5; }
.ss-hiddenrow:hover .ss-ach-name { text-decoration: underline; }
/* Revealed hidden ones, like Steam's: listed after the other locked ones, blurred until hovered or clicked. */
.ss-secret { cursor: pointer; }
.ss-secret .ss-ach-name, .ss-secret .ss-ach-desc, .ss-secret .ss-ach-icon { filter: blur(6px); transition: filter .15s; }
.ss-secret.ss-locked .ss-ach-icon { filter: grayscale(1) brightness(.7) blur(6px); }
.ss-secret:hover .ss-ach-name, .ss-secret:hover .ss-ach-desc, .ss-secret:hover .ss-ach-icon,
.ss-secret.ss-peek .ss-ach-name, .ss-secret.ss-peek .ss-ach-desc, .ss-secret.ss-peek .ss-ach-icon { filter: none; }
.ss-secret.ss-locked:hover .ss-ach-icon, .ss-secret.ss-locked.ss-peek .ss-ach-icon { filter: grayscale(1) brightness(.7); }
.ss-ach-toggle { font-size: 12px; color: #67c1f5; cursor: pointer; margin-top: 4px; opacity: 0; transition: opacity .12s; }
.ss-ach:hover .ss-ach-toggle { opacity: 1; }
.ss-ach-toggle:hover { text-decoration: underline; }
.ss-empty { text-align: center; color: var(--ss-dim); padding: 40px 0; }
.ss-grade { display: inline-block; font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; padding: 1px 6px; border-radius: 2px; margin-left: 8px; vertical-align: 2px; background: rgba(255,255,255,.08); }
.ss-grade-pts { color: #e5b143; }
.ss-grade-P { color: #b8d8ff; } .ss-grade-G { color: #e8c35a; } .ss-grade-S { color: #c9d0d6; } .ss-grade-B { color: #d4925a; }
.ss-x { position: absolute; top: -12px; right: -14px; width: 50px; height: 50px; border-radius: 50%; background: var(--ss-close); border: 2px solid var(--ss-dim); cursor: pointer; z-index: 4; box-shadow: 0 2px 8px rgba(0,0,0,.5); -webkit-app-region: no-drag; }
.ss-x:hover { background: var(--ss-faint); border-color: var(--ss-text); }
/* Big Picture: the stop the controller is on, in Steam's focused style (white, or a lighter row). */
.ss-x.gpfocus { background: #fff; border-color: #fff; }
.ss-x.gpfocus::before, .ss-x.gpfocus::after { background: var(--ss-raised); }
.ss-switch.gpfocus, .ss-filter.gpfocus { background: #fff; color: var(--ss-raised); border-radius: 2px; }
.ss-switch.gpfocus { padding: 6px 8px; margin: -6px -8px; }
.ss-filter.gpfocus::after { border-color: var(--ss-raised); }
.ss-search.gpfocus { border-color: #fff; background: #fff; color: var(--ss-raised); }
.ss-ach.gpfocus { background: var(--ss-raised); }
.ss-ach.gpfocus .ss-ach-toggle { opacity: 1; }
.ss-x::before, .ss-x::after { content: ''; position: absolute; left: 50%; top: 50%; width: 22px; height: 2px; background: var(--ss-text); }
.ss-x::before { transform: translate(-50%, -50%) rotate(45deg); }
.ss-x::after { transform: translate(-50%, -50%) rotate(-45deg); }
.ss-toast { position: fixed; right: 18px; bottom: 18px; z-index: 10000; width: 300px; display: flex; gap: 12px; align-items: center; padding: 12px; border-radius: 4px; background: var(--ss-toast); box-shadow: 0 8px 32px rgba(0,0,0,.6); color: var(--ss-text); transform: translateY(140%); transition: transform .35s ease-out; }
.ss-toast.ss-in { transform: translateY(0); }
.ss-toast .ss-row-icon { width: 64px; height: 64px; border-radius: 2px; background: var(--ss-tile) center/cover no-repeat; flex-shrink: 0; }
.ss-toast .ss-t1 { font-size: 13px; color: #fff; font-weight: 600; }
.ss-toast .ss-t2 { font-size: 13px; color: var(--ss-dim); }
`;

// Steam's own achievements rosette, copied from a real game's play bar.
const ACHIEVEMENT_SVG =
	'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 36 36" fill="none"><g fill="currentColor">' +
	'<path d="M9.64304 9.49988L6.39294 12.8055L9.64304 16.1112V20.8333H14.2858L18.0001 24.6111L21.7143 20.8333H26.3573V16.111L29.6072 12.8055L26.3573 9.50012V4.77777H21.7143L18.0001 1L14.2858 4.77777H9.64304V9.49988ZM22.6432 12.8056C22.6432 15.4136 20.5645 17.5278 18.0004 17.5278C15.4362 17.5278 13.3575 15.4136 13.3575 12.8056C13.3575 10.1976 15.4362 8.08334 18.0004 8.08334C20.5645 8.08334 22.6432 10.1976 22.6432 12.8056Z"></path>' +
	'<path d="M5 30.2778L8.25 24.6111H12.4286L15.6786 27.9167L11.5 35L9.17857 30.2778H5Z"></path>' +
	'<path d="M30.9999 30.2778L27.7499 24.6111H23.5713L20.3213 27.9167L24.4999 35L26.8213 30.2778H30.9999Z"></path></g></svg>';

/**
 * The "ACHIEVEMENTS 7/63" play-bar stat, built with exactly the markup and classes Steam uses for real games
 * (GameStat MiniAchievements > GameStatIcon AchievementSVG + GameStatRight > PlayBarLabel + AchievementProgressRow).
 * Steam draws Last Played and Play Time for shortcuts itself.
 */
export function achievementStat(doc: Document, pb: Record<string, string>, done: number, total: number): HTMLElement {
	const pct = total ? Math.floor((100 * done) / total) : 0;
	const icon = h(doc, 'div', { class: cx(pb.GameStatIcon, 'GameStatIcon', pb.AchievementSVG, 'AchievementSVG') });
	icon.innerHTML = ACHIEVEMENT_SVG;
	const right = h(
		doc,
		'div',
		{ class: cx(pb.GameStatRight, 'GameStatRight') },
		h(doc, 'div', { class: cx(pb.PlayBarLabel, 'PlayBarLabel') }, 'Achievements'),
		h(
			doc,
			'div',
			{ class: cx(pb.AchievementProgressRow, 'AchievementProgressRow') },
			h(doc, 'div', { class: cx(pb.PlayBarDetailLabel, 'PlayBarDetailLabel', pb.AchievementCountLabel, 'AchievementCountLabel') }, `${done}/${total}`),
			h(doc, 'div', { role: 'progressbar', 'aria-valuenow': String(pct), class: cx(pb.DetailsProgressContainer, 'DetailsProgressContainer') }, h(doc, 'div', { class: cx(pb.DetailsProgressBar, 'DetailsProgressBar'), style: `width: ${pct}%;` })),
		),
	);
	return h(doc, 'div', { class: cx(pb.GameStat, 'GameStat', pb.MiniAchievements, 'MiniAchievements') }, icon, right);
}

export function ensureStyles(doc: Document) {
	applyStyle(doc);
	if (doc.getElementById('shortcut-studio-css')) return;
	doc.head.append(h(doc, 'style', { id: 'shortcut-studio-css' }, CSS));
}

const trophySvg = (back: string, front: string) =>
	'data:image/svg+xml;utf8,' +
	encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" fill="${back}"/><path fill="${front}" d="M22 16h20v4h7v6c0 5-3.6 9-8.4 9.8A12 12 0 0 1 35 41.6V46h6v4H23v-4h6v-4.4A12 12 0 0 1 23.4 35.8C18.6 35 15 31 15 26v-6h7zm-3 8v2c0 2.6 1.6 4.8 3.8 5.6V24zm26 0v7.6c2.2-.8 3.8-3 3.8-5.6v-2z"/></svg>`);
// The placeholder for achievements without art, in each palette (an image, so CSS variables can't reach it).
const TROPHIES = { stock: trophySvg('#2a3646', '#8f98a0'), 'simply-dark': trophySvg('#2a2a2b', '#8f8f8f') };
const trophy = () => TROPHIES[lastPalette()];

export const iconFor = (a: Achievement) => (a.unlockedAt != null ? a.icon : a.iconGray || a.icon) || trophy();
const bg = (url: string) => `background-image: url("${url}")`;

function progress(data: GameData) {
	const total = data.achievements.length;
	const done = data.achievements.filter((a) => a.unlockedAt != null).length;
	return { total, done, pct: total ? Math.floor((100 * done) / total) : 0 };
}

const tooltip = (a: Achievement) => (a.hidden && a.unlockedAt == null ? 'Hidden achievement' : `${a.name}\n${a.description}`);

/**
 * Right-column "Achievements" section, built like Steam's own (sp.js): an AppDetailsSection with its header, then a
 * panel holding the highlight (unlocked count and bar), and a body with the set summary, the newest unlock (two when
 * exactly two are unlocked), the other unlocked icons, the locked icons under a divider, and View My Achievements.
 */
export function achievementsSection(doc: Document, data: GameData, openList: () => void): HTMLElement {
	const unlocked = data.achievements.filter((a) => a.unlockedAt != null).sort((a, b) => b.unlockedAt! - a.unlockedAt!);
	const locked = data.achievements.filter((a) => a.unlockedAt == null);
	const total = data.achievements.length;
	const done = unlocked.length;
	const pct = total ? Math.round((100 * done) / total) : 0;

	// Highlight: "You've unlocked 7/44 (16%)" over a bar split into unlocked / remaining.
	const highlight = h(
		doc,
		'div',
		{ class: cx(section.Highlight, ach.HighlightDiv) },
		h(doc, 'div', { class: ach.UnlockedLabel }, h(doc, 'span', {}, done < total ? `You've unlocked ${done}/${total}` : `You've unlocked all achievements! ${done}/${total}`), h(doc, 'span', { class: ach.UnlockedLabelPercent }, ` (${pct}%)`)),
		h(
			doc,
			'div',
			{ class: ach.ProgressRow },
			h(doc, 'div', { class: ach.ProgressBar }, h(doc, 'div', { class: 'ss-pbar', style: `grid-template-columns: ${done}fr ${total - done}fr` }, h(doc, 'div', { class: 'ss-done' }), h(doc, 'div', {}))),
		),
	);

	const body = h(doc, 'div', { class: section.Body });

	// Steam's GroupsSection: "1 base game set" / "2 DLC sets", only when there's more than one set.
	const sets = achievementSets(data);
	if (sets.length > 1) {
		const base = sets.filter((s) => !s.dlc).length;
		const dlc = sets.length - base;
		body.append(
			h(
				doc,
				'div',
				{ class: ach.GroupsSection },
				base ? h(doc, 'div', {}, base === 1 ? '1 base game set' : `${base} base game sets`) : null,
				dlc ? h(doc, 'div', { class: ach.DLC }, dlc === 1 ? '1 DLC set' : `${dlc} DLC sets`) : null,
			),
		);
	}

	const icon = (a: Achievement, isLocked: boolean) => {
		const hiddenLocked = isLocked && a.hidden;
		const el = h(doc, 'div', { class: cx('ss-icon', hiddenLocked ? 'ss-hidden-lock' : isLocked && !a.iconGray && 'ss-locked'), title: tooltip(a), style: hiddenLocked ? undefined : bg(iconFor(a)) }, hiddenLocked ? '?' : null);
		el.addEventListener('click', openList);
		return el;
	};
	/** One row of 48px icons, as many as fit; when some don't, the last cell becomes "+N" (Steam's IconRow). */
	const iconRow = (list: Achievement[], isLocked: boolean) => {
		const row = h(doc, 'div', { class: cx(ach.IconRow, 'ss-icons') });
		let cols = 0;
		const fill = () => {
			const width = row.clientWidth;
			const fit = width > 0 ? Math.max(1, Math.floor((width + 4) / 52)) : 9;
			if (fit === cols) return;
			cols = fit;
			const shown = list.length > fit ? fit - 1 : list.length;
			const cells = list.slice(0, shown).map((a) => icon(a, isLocked));
			if (list.length > shown) {
				const more = h(doc, 'div', { class: 'ss-icon ss-more', title: 'View all' }, `+${list.length - shown}`);
				more.addEventListener('click', openList);
				cells.push(more);
			}
			row.replaceChildren(...cells);
		};
		fill();
		const RO = (doc.defaultView as any)?.ResizeObserver as typeof ResizeObserver | undefined;
		if (RO) new RO(fill).observe(row);
		return row;
	};

	// The newest unlock with its name and description (both, when exactly two are unlocked), then the rest as icons.
	const featured = unlocked.slice(0, unlocked.length === 2 ? 2 : 1);
	for (const a of featured) {
		const el = h(
			doc,
			'div',
			{ class: cx(ach.Featured, ach.RightAchievementSection, ach.Clickable), role: 'link', title: tooltip(a) },
			h(doc, 'div', { class: cx(achIcon.AchievementIconWrapper, ach.Icon, 'ss-feat-icon'), style: bg(iconFor(a)) }),
			h(doc, 'div', { class: ach.TextSection }, h(doc, 'div', { class: ach.Name }, a.name), h(doc, 'div', { class: ach.Desc }, a.description)),
		);
		el.addEventListener('click', openList);
		body.append(el);
	}
	const rest = unlocked.slice(featured.length);
	if (rest.length) body.append(iconRow(rest, false));
	if (locked.length) body.append(h(doc, 'div', { class: cx(ach.UnachievedSection, featured.length > 0 && ach.ShowDivider) }, h(doc, 'div', { class: ach.Label }, 'Locked Achievements'), iconRow(locked, true)));

	const btn = h(doc, 'button', { type: 'button', class: cx(appButton.AppDetailsButton, appButton.BottomRight, 'DialogButton', '_DialogLayout', 'Secondary', 'Focusable', 'ss-view') }, 'View My Achievements');
	btn.addEventListener('click', openList);
	body.append(btn);

	return h(
		doc,
		'div',
		{ class: cx(section.AppDetailsSection, 'Panel', 'ss-section'), role: 'region', 'data-ss': 'achievements' },
		h(doc, 'div', { class: cx(header.SectionHeader, header.PadLeft) }, h(doc, 'div', { class: header.Label }, h(doc, 'div', { class: header.LabelText }, 'Achievements'))),
		h(doc, 'div', { class: cx(section.AppDetailsSectionContainer, section.AppDetailsSectionHasLabel, section.RightColumnSection, 'Panel') }, highlight, body),
	);
}

/** Strips anything active from store HTML before it goes on the page. */
function safeHtml(doc: Document, html: string): DocumentFragment {
	const tpl = doc.createElement('template');
	tpl.innerHTML = /<[a-z][\s\S]*>/i.test(html) ? html : html.replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c]!).replace(/\n/g, '<br>');
	tpl.content.querySelectorAll('script,iframe,object,embed,style,link,form,input,img,video').forEach((n) => n.remove());
	tpl.content.querySelectorAll('*').forEach((el) => {
		for (const attr of Array.from(el.attributes)) {
			if (/^on/i.test(attr.name) || ((attr.name === 'href' || attr.name === 'src') && /^\s*javascript:/i.test(attr.value))) el.removeAttribute(attr.name);
		}
		if (el.tagName === 'A') el.setAttribute('target', '_blank');
	});
	return tpl.content;
}

/** "About this game" section for the left column: description plus Developer / Publisher / Release date. */
export function gameInfoPanel(doc: Document, data: GameData): HTMLElement {
	const main = h(doc, 'div', { class: cx('ss-info-main', desc.Description) });
	if (data.shortDescription) main.append(h(doc, 'div', { class: 'ss-short' }, data.shortDescription));
	if (data.description) {
		const about = h(doc, 'div', { class: 'ss-about' });
		about.append(safeHtml(doc, data.description));
		main.append(about);
	}

	const side = h(doc, 'div', { class: 'ss-info-side' });
	const field = (k: string, v: string) => v && side.append(h(doc, 'div', { class: 'ss-info-row' }, h(doc, 'div', { class: cx('ss-k', desc.Label) }, k), h(doc, 'div', { class: 'ss-v' }, v)));
	field('Developer', data.developer);
	field('Publisher', data.publisher);
	field('Release date', data.releaseDate);
	field('Genre', data.genres.join(', '));

	return h(
		doc,
		'div',
		{ class: cx('ss-section', section.AppDetailsSectionContainer), 'data-ss': 'info' },
		h(doc, 'div', { class: cx(header.SectionHeader, header.PadLeft) }, h(doc, 'div', { class: header.Label }, h(doc, 'span', { class: header.LabelText }, 'About this game'))),
		h(doc, 'div', { class: cx(section.AppDetailsSection, section.AppDetailsSectionHasLabel) }, h(doc, 'div', { class: 'ss-info' }, main, side)),
	);
}

export function hasInfo(data: GameData) {
	return Boolean(data.shortDescription || data.description || data.developer || data.publisher || data.releaseDate || data.genres.length);
}

function formatDateTime(ms: number): string {
	const d = new Date(ms);
	const date = d.toLocaleDateString(navigator.languages as string[], { year: 'numeric', month: 'short', day: 'numeric' });
	const time = d.toLocaleTimeString(navigator.languages as string[], { hour: 'numeric', minute: '2-digit' });
	return `${date}, ${time}`;
}

/** The game's hero art and Steam's playtime numbers (the plugin copies shortcut playtime onto the overview). */
function gameArt(appid: number): { hero?: string; minutes: number; minutes2wk: number } {
	const store = (globalThis as any).appStore;
	try {
		const overview = store?.GetAppOverviewByAppID?.(appid);
		if (!overview) return { minutes: 0, minutes2wk: 0 };
		const heroes = store.GetCustomHeroImageURLs?.(overview) ?? store.GetHeroImageURLs?.(overview);
		return {
			hero: Array.isArray(heroes) ? heroes[0] : typeof heroes === 'string' ? heroes : undefined,
			minutes: Number(overview.minutes_playtime_forever) || 0,
			minutes2wk: Number(overview.minutes_playtime_last_two_weeks) || 0,
		};
	} catch {
		return { minutes: 0, minutes2wk: 0 };
	}
}

/** Steam's wording: "51.9 hours" over two hours, otherwise minutes. */
function formatPlaytime(minutes: number): string {
	if (minutes >= 120) return `${(minutes / 60).toFixed(1)} hours`;
	return `${minutes} minute${minutes === 1 ? '' : 's'}`;
}

type Filter = 'all' | 'unlocked' | 'locked';
const FILTER_LABEL: Record<Filter, string> = { all: 'All Achievements', unlocked: 'Unlocked Achievements', locked: 'Locked Achievements' };
// Per game, for this Steam session: whether global stats are showing.
const globalStats = new Map<number, boolean>();

/**
 * Full achievement list, laid out like Steam's own achievements page: portrait capsule, title, "Achievements: X / Y |
 * Playtime" line and progress bar; a Show Global Stats switch, an All / Unlocked / Locked filter and search; then
 * UNLOCKED ACHIEVEMENTS (newest first) and LOCKED ACHIEVEMENTS, with hidden locked ones folded into one row at the
 * end. Hand-made games can be locked/unlocked with a link on each row.
 */
/** Closes the open achievement list properly (its controller focus included) before another one opens. */
let closeOpenList: (() => void) | null = null;

export function openAchievementList(doc: Document, appid: number, gameName: string, data: GameData, toggle: (a: Achievement) => Promise<void>, capsule = '') {
	closeOpenList?.();
	doc.querySelector('.ss-modal')?.remove();
	const win = doc.defaultView;
	const bp = isBigPicture(win);
	let nav: FocusTree | null = null;
	let watcher: MutationObserver | null = null;
	const close = () => {
		if (closeOpenList === close) closeOpenList = null;
		watcher?.disconnect();
		nav?.close();
		nav = null;
		modal.remove();
		if (bp) refocus(doc.querySelector<HTMLElement>('[data-ss="achievements"] .ss-view'));
		doc.removeEventListener('keydown', onKey, true);
		doc.removeEventListener('click', closeMenu, true);
		win?.removeEventListener('vgp_onbuttondown', onPad, true);
	};
	const onKey = (e: KeyboardEvent) => {
		if (e.key !== 'Escape') return;
		e.preventDefault();
		e.stopPropagation();
		close();
	};
	// Big Picture: B closes. The D-pad moves between the list's stops (its own focus tree, set up below).
	const onPad = (e: Event) => {
		if ((e as CustomEvent).detail?.button !== 2) return;
		e.preventDefault();
		e.stopPropagation();
		close();
	};

	const synced = data.trophySource;
	// Hidden ones revealed with "Click to reveal" stay revealed only while this list is open, like Steam's. Keeping
	// them for the whole Steam session made newly hidden achievements look like they never got hidden.
	const revealed = new Set<string>(); // sets ("" = base game, else the DLC name) whose hidden ones were revealed
	const peeked = new Set<string>(); // revealed rows clicked to stay unblurred
	let filter: Filter = 'all';
	let query = '';
	let showGlobal = globalStats.get(appid) ?? false;
	const hasGlobal = data.achievements.some((a) => a.percent != null);

	const art = gameArt(appid);
	const capsuleEl = h(doc, 'div', { class: cx('ss-capsule', !capsule && 'ss-none'), style: capsule ? bg(capsule) : undefined }, capsule ? null : gameName);
	const subline = h(doc, 'div', { class: 'ss-subline' });
	const earnedHost = h(doc, 'div', { class: 'ss-earned' });

	const top = h(
		doc,
		'div',
		{ class: 'ss-top' },
		h(doc, 'div', { class: 'ss-top-bg', style: art.hero ? bg(art.hero) : undefined }),
		h(doc, 'div', { class: 'ss-head' }, capsuleEl, h(doc, 'div', { class: 'ss-head-main' }, h(doc, 'div', { class: 'ss-title' }, gameName), subline, earnedHost)),
	);

	// ---- Controls: Show Global Stats, filter dropdown, search ----
	const globalSwitch = h(doc, 'div', { class: cx('ss-switch', showGlobal && 'ss-on'), title: hasGlobal ? '' : 'No global unlock percentages for this game' }, 'Show Global Stats', h(doc, 'div', { class: 'ss-switch-track' }));
	globalSwitch.addEventListener('click', () => {
		showGlobal = !showGlobal;
		globalStats.set(appid, showGlobal);
		globalSwitch.classList.toggle('ss-on', showGlobal);
		renderList();
	});
	let menu: HTMLElement | null = null;
	const closeMenu = (e?: Event) => {
		if (menu && (!e || !menu.contains(e.target as Node))) {
			menu.remove();
			menu = null;
		}
	};
	/** A Steam-style dropdown. items() is read each time it opens; label(key) draws the closed state. */
	const dropdown = <K,>(label: (key: K) => (Node | string)[], items: () => K[], onPick: (key: K) => void, initial: K) => {
		const el = h(doc, 'div', { class: 'ss-filter' });
		let current = initial;
		const draw = (k: K) => el.replaceChildren(...label(k));
		draw(initial);
		el.addEventListener('click', (e) => {
			e.stopPropagation();
			if (bp) {
				// With a controller, A steps to the next choice (no menu to navigate).
				const keys = items();
				current = keys[(keys.indexOf(current) + 1) % keys.length];
				draw(current);
				onPick(current);
				return;
			}
			if (menu) return closeMenu();
			menu = h(doc, 'div', { class: 'ss-menu' });
			for (const k of items()) {
				const item = h(doc, 'div', {}, ...label(k));
				item.addEventListener('click', (ev) => {
					ev.stopPropagation();
					closeMenu();
					current = k;
					draw(k);
					onPick(k);
				});
				menu.append(item);
			}
			el.append(menu);
		});
		return el;
	};
	const filterEl = dropdown<Filter>((f) => [FILTER_LABEL[f]], () => ['all', 'unlocked', 'locked'], (f) => ((filter = f), renderList()), filter);

	// Steam's set filter: "All Sets (3)", "Base Game", "DLC  Name". Only shown when the game has more than one set.
	let setFilter: string | null = null; // null = all sets, "" = base game, else a DLC name
	const setLabel = (k: string | null): (Node | string)[] =>
		k == null ? [`All Sets (${achievementSets(data).length})`] : k === '' ? ['Base Game'] : [h(doc, 'span', { class: 'ss-dlc-tag' }, 'DLC'), k];
	const setFilterEl = dropdown<string | null>(setLabel, () => [null, ...achievementSets(data).map((s) => s.dlc)], (k) => ((setFilter = k), renderList()), null);
	doc.addEventListener('click', closeMenu, true);
	const search = h(doc, 'input', { class: 'ss-search', type: 'text', placeholder: 'Search...' }) as HTMLInputElement;
	search.addEventListener('input', () => {
		query = search.value.trim().toLowerCase();
		renderList();
	});
	const multiSet = () => achievementSets(data).length > 1;
	if (!multiSet()) setFilterEl.style.display = 'none';
	const controls = h(doc, 'div', { class: 'ss-controls' }, globalSwitch, h(doc, 'div', { class: 'ss-spacer' }), setFilterEl, filterEl, h(doc, 'div', { class: 'ss-searchbox' }, search));

	const list = h(doc, 'div', { class: 'ss-list' });
	const closeBtn = h(doc, 'div', { class: 'ss-x', title: 'Close' });
	closeBtn.addEventListener('click', close);
	const dialog = h(doc, 'div', { class: 'ss-dialog' }, closeBtn, h(doc, 'div', { class: 'ss-scroll' }, top, controls, list));
	const modal = h(doc, 'div', { class: 'ss-modal' }, dialog);
	modal.addEventListener('click', (e) => e.target === modal && close());
	doc.addEventListener('keydown', onKey, true);
	win?.addEventListener('vgp_onbuttondown', onPad, true);

	const renderTop = () => {
		const { total, done, pct } = progress(data);
		const sets = achievementSets(data);
		subline.replaceChildren(`Achievements: ${done} / ${total}`);
		if (sets.length > 1) subline.append(h(doc, 'span', { class: 'ss-sep' }), `Sets: ${sets.filter((s) => s.done === s.total).length} / ${sets.length}`);
		if (art.minutes > 0) subline.append(h(doc, 'span', { class: 'ss-sep' }), `Playtime: ${formatPlaytime(art.minutes)} - Last 2 weeks: ${formatPlaytime(art.minutes2wk)}`);
		const row = h(doc, 'div', { class: 'ss-earned-row' }, `${done}/${total} achievements unlocked `, h(doc, 'span', { class: 'ss-pct' }, `(${pct}%)`), sets.length > 1 ? ` across ${sets.length} sets` : null);
		earnedHost.replaceChildren(row, h(doc, 'div', { class: 'ss-earned-bar' }, h(doc, 'div', { style: `width:${pct}%` })));
		if (synced) earnedHost.append(h(doc, 'div', { class: 'ss-sync-note' }, `Synced live from ${emulatorName(synced.emulator)}: earn them in the game to unlock them here.`));
	};

	const globalLine = (a: Achievement) =>
		showGlobal && a.percent != null
			? h(doc, 'div', { class: 'ss-ach-global' }, `${a.percent.toFixed(1)}%`, h(doc, 'div', { class: 'ss-ach-bar' }, h(doc, 'div', { style: `width:${Math.min(100, a.percent)}%` })))
			: null;

	const row = (a: Achievement) => {
		const isLocked = a.unlockedAt == null;
		const name = h(doc, 'div', { class: 'ss-ach-name' }, a.name);
		if (gradeName(a.grade)) name.append(h(doc, 'span', { class: `ss-grade ss-grade-${a.grade}` }, gradeName(a.grade)));
		if (a.points != null) name.append(h(doc, 'span', { class: 'ss-grade ss-grade-pts' }, `${a.points} pts`));
		const text = h(doc, 'div', { class: 'ss-ach-text' }, name, h(doc, 'div', { class: 'ss-ach-desc' }, a.description));

		const side = h(doc, 'div', { class: 'ss-ach-side' });
		if (!isLocked) side.append(h(doc, 'div', {}, 'Unlocked ' + formatDateTime(a.unlockedAt!)));
		else if (a.progress) {
			// Steam's progress for locked achievements: "3 / 4" right-aligned above a blue bar.
			const pct = Math.min(100, (100 * a.progress.current) / a.progress.max);
			side.append(
				h(doc, 'div', { class: 'ss-prog' }, h(doc, 'div', { class: 'ss-prog-text' }, `${a.progress.current} / ${a.progress.max}`), h(doc, 'div', { class: 'ss-prog-bar' }, h(doc, 'div', { style: `width:${pct}%` }))),
			);
		}
		const g = globalLine(a);
		if (g) side.append(g);
		if (!synced) {
			// Hand-made achievements: lock/unlock from here (synced ones are earned in the game).
			const link = h(doc, 'div', { class: 'ss-ach-toggle' }, isLocked ? 'Unlock' : 'Lock');
			link.addEventListener('click', async (e) => {
				e.stopPropagation();
				await toggle(a);
				render();
			});
			side.append(link);
		}
		return h(doc, 'div', { class: cx('ss-ach', isLocked && !a.iconGray && 'ss-locked') }, h(doc, 'div', { class: 'ss-ach-icon', style: bg(iconFor(a)) }), text, side);
	};

	const match = (a: Achievement) => !query || a.name.toLowerCase().includes(query) || a.description.toLowerCase().includes(query);

	/** One set's header, like Steam's: name (with a DLC tag), "Unlocked 3 / 10 (30%)" and a bar. */
	const setHeader = (s: AchievementSet) => {
		const pct = s.total ? Math.floor((100 * s.done) / s.total) : 0;
		const name = h(doc, 'div', { class: 'ss-set-name' }, ...(s.dlc ? [h(doc, 'span', { class: 'ss-dlc-tag' }, 'DLC'), s.dlc] : ['Base Game']));
		return h(
			doc,
			'div',
			{ class: cx('ss-set', s.done === s.total && 'ss-complete') },
			h(doc, 'div', { class: 'ss-set-head' }, name, h(doc, 'div', { class: 'ss-set-count' }, `Unlocked ${s.done} / ${s.total} (${pct}%)`)),
			h(doc, 'div', { class: 'ss-set-bar' }, h(doc, 'div', { style: `width:${pct}%` })),
		);
	};

	const renderList = () => {
		const sets = achievementSets(data);
		setFilterEl.style.display = sets.length > 1 ? '' : 'none';
		if (setFilter != null && !sets.some((s) => s.dlc === setFilter)) setFilter = null;
		const out: HTMLElement[] = [];
		for (const s of sets) {
			if (setFilter != null && s.dlc !== setFilter) continue;
			const rows = renderSet(s.achievements, s.dlc);
			if (sets.length > 1 && rows.length) out.push(setHeader(s));
			out.push(...rows);
		}
		list.replaceChildren(...out);
		if (!out.length) list.append(h(doc, 'div', { class: 'ss-empty' }, data.achievements.length ? 'No achievements match.' : 'This game has no achievements.'));
	};

	/** Unlocked (newest first), then locked, then the folded hidden row, for one set's achievements. */
	const renderSet = (achievements: Achievement[], set: string): HTMLElement[] => {
		const unlocked = achievements.filter((a) => a.unlockedAt != null).sort((a, b) => b.unlockedAt! - a.unlockedAt!);
		const lockedAll = achievements.filter((a) => a.unlockedAt == null);
		const secret = lockedAll.filter((a) => a.hidden);
		const locked = lockedAll.filter((a) => !a.hidden);
		const out: HTMLElement[] = [];

		if (filter !== 'locked') {
			const u = unlocked.filter(match);
			if (u.length) out.push(h(doc, 'div', { class: 'ss-group' }, `Unlocked Achievements (${unlocked.length})`), ...u.map(row));
		}
		if (filter !== 'unlocked') {
			const l = locked.filter(match);
			// Revealed: Steam lists them after the other locked ones (not back in their place), each blurred until
			// hovered, or clicked to keep it showing. Searching only matches them once revealed.
			const s = revealed.has(set) ? secret.filter(match) : [];
			const showSecret = secret.length > 0 && !query && !revealed.has(set);
			if (l.length || s.length || showSecret) out.push(h(doc, 'div', { class: 'ss-group' }, `Locked Achievements (${lockedAll.length})`), ...l.map(row));
			for (const a of s) {
				const el = row(a);
				el.classList.add('ss-secret');
				el.classList.toggle('ss-peek', peeked.has(a.id));
				el.addEventListener('click', () => {
					if (peeked.has(a.id)) peeked.delete(a.id);
					else peeked.add(a.id);
					el.classList.toggle('ss-peek', peeked.has(a.id));
				});
				out.push(el);
			}
			if (showSecret) {
				// Like Steam: hidden ones stay folded into one row until revealed.
				const el = h(
					doc,
					'div',
					{ class: 'ss-ach ss-locked ss-hiddenrow', title: 'Click to reveal' },
					h(doc, 'div', { class: 'ss-ach-icon' }, '?'),
					h(doc, 'div', { class: 'ss-ach-text' }, h(doc, 'div', { class: 'ss-ach-name' }, `${secret.length} hidden achievement${secret.length === 1 ? '' : 's'} remaining`), h(doc, 'div', { class: 'ss-ach-desc' }, 'Details for each achievement will be revealed once unlocked. Click to reveal them now.')),
				);
				el.addEventListener('click', () => {
					revealed.add(set);
					renderList();
				});
				out.push(el);
			}
		}
		return out;
	};

	const render = () => {
		renderTop();
		renderList();
	};

	render();
	doc.body.append(modal);
	closeOpenList = close;
	if (bp && win) {
		try {
			nav = new FocusTree(win, dialog, 'ShortcutStudio_AchievementList');
			const t = nav;
			t.add(closeBtn);
			t.add(controls, { row: true });
			for (const c of [globalSwitch, setFilterEl, filterEl]) if (c.style.display !== 'none') t.add(c);
			t.add(search, { onOk: () => search.focus() });
			const rows = () => Array.from(list.querySelectorAll<HTMLElement>('.ss-ach'));
			const addRows = () => {
				for (const r of rows()) t.add(r, { onOk: () => (r.querySelector<HTMLElement>('.ss-ach-toggle') ?? r).click() });
			};
			addRows();
			t.focus(rows()[0] ?? filterEl);
			// The list is redrawn on every filter, search, reveal and lock change: swap its stops, and keep the
			// controller on the same row number when the focused one was replaced.
			watcher = new (win as any).MutationObserver((records: MutationRecord[]) => {
				const removed: HTMLElement[] = [];
				for (const rec of records)
					for (const n of Array.from(rec.removedNodes))
						if (n instanceof (win as any).HTMLElement) removed.push(...((n as HTMLElement).matches('.ss-ach') ? [n as HTMLElement] : Array.from((n as HTMLElement).querySelectorAll<HTMLElement>('.ss-ach'))));
				const was = removed.findIndex((el) => el.classList.contains('gpfocus'));
				t.prune();
				addRows();
				if (was >= 0 && !t.focused()) {
					const now = rows();
					t.focus(now[Math.min(was, now.length - 1)] ?? filterEl);
				}
			});
			watcher!.observe(list, { childList: true, subtree: true });
		} catch (e) {
			console.warn('[Shortcut Studio] controller navigation for the achievement list failed', e);
		}
	}
	return render;
}

export const gradeName = (g: string) => ({ P: 'Platinum', G: 'Gold', S: 'Silver', B: 'Bronze' })[g] ?? '';

/** "Achievement Unlocked" popup in the bottom-right corner. */
export function showUnlockToast(doc: Document, a: Achievement, gameName = '') {
	// Several unlocks at once stack upwards, like Steam's.
	const stacked = doc.querySelectorAll('.ss-toast').length;
	const toast = h(
		doc,
		'div',
		{ class: 'ss-toast', style: stacked ? `bottom: ${18 + stacked * 98}px` : undefined },
		h(doc, 'div', { class: 'ss-row-icon', style: bg(a.icon || trophy()) }),
		h(
			doc,
			'div',
			{},
			h(doc, 'div', { class: 'ss-t1' }, 'Achievement Unlocked!'),
			h(doc, 'div', { class: 'ss-t2' }, a.name + (gradeName(a.grade) ? ` · ${gradeName(a.grade)}` : a.points != null ? ` · ${a.points} points` : '')),
			gameName ? h(doc, 'div', { class: 'ss-t2', style: 'font-size:11px' }, gameName) : null,
		),
	);
	doc.body.append(toast);
	requestAnimationFrame(() => requestAnimationFrame(() => toast.classList.add('ss-in')));
	setTimeout(() => {
		toast.classList.remove('ss-in');
		setTimeout(() => toast.remove(), 400);
	}, 4500);
}
