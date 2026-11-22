/**
 * Controller focus for our injected buttons in Big Picture (View My Achievements, the Play button's launch arrow).
 *
 * Steam's gamepad navigation only moves between nodes in its focus tree, which its own React components build, so
 * plain injected elements are skipped. Each of ours gets a real node under the nearest Steam node above it. Steam
 * sorts siblings by page order, so the D-pad reaches it from every side and plays its usual sounds. Steam fires
 * vgp_onok on the focused element for A; we click. Steam's React components add "gpfocus" for their own nodes, so we
 * toggle it from the node's focus state.
 */

const g = globalThis as any;

interface Registered {
	node: any;
	unsubscribe: () => void;
	onOk: (e: Event) => void;
}
const registered = new Map<HTMLElement, Registered>();

/** Every node in the Big Picture window's focus trees. */
function allNodes(win: Window): { node: any; tree: any }[] {
	const contexts: any[] = g.FocusNavController?.m_rgAllContexts ?? [];
	const ctx = contexts.find((c) => c.m_rootWindow === win || c.m_activeWindow === win) ?? contexts.find((c) => c.m_rgGamepadNavigationTrees?.some((t: any) => t.m_ID === 'GamepadUI_Full_Root'));
	const out: { node: any; tree: any }[] = [];
	const walk = (node: any, tree: any) => {
		out.push({ node, tree });
		for (const c of node.m_rgChildren ?? []) walk(c, tree);
	};
	for (const t of ctx?.m_rgGamepadNavigationTrees ?? []) if (t.m_Root) walk(t.m_Root, t);
	return out;
}

/** Makes `el` a stop for the controller. Safe to call on every tick; does nothing once registered. */
export function makeFocusable(win: Window, el: HTMLElement | null) {
	if (!el || registered.has(el) || !el.isConnected) return;
	try {
		const nodes = allNodes(win);
		let parent: { node: any; tree: any } | undefined;
		for (let p = el.parentElement; p && !parent; p = p.parentElement) parent = nodes.find((x) => x.node.m_element === p);
		if (!parent) return; // Steam hasn't built that part of the page yet; try again next tick
		const ring = parent.node.m_rgChildren?.[0]?.m_FocusRing ?? parent.node.m_FocusRing;
		const node = parent.tree.CreateNode(parent.node, ring);
		// noFocusRing like Steam's own buttons: they show focus by their gpfocus style, not the ring's glow.
		node.SetProperties({ focusable: true, noFocusRing: true });
		if (el.tabIndex < 0) el.tabIndex = 0;
		node.OnMount(el);
		const unsubscribe = node.m_Focused?.Subscribe?.((on: boolean) => el.classList.toggle('gpfocus', !!on))?.Unsubscribe ?? (() => {});
		const onOk = (e: Event) => {
			e.preventDefault();
			e.stopPropagation();
			el.click();
		};
		el.addEventListener('vgp_onok', onOk);
		registered.set(el, { node, unsubscribe, onOk });
	} catch (e) {
		console.warn('[Shortcut Studio] could not make an element controller-focusable', e);
	}
}

function forget(el: HTMLElement) {
	const r = registered.get(el);
	if (!r) return;
	registered.delete(el);
	try {
		r.unsubscribe();
		el.removeEventListener('vgp_onok', r.onOk);
		el.classList.remove('gpfocus');
		if (r.node.m_bMounted) r.node.OnUnmount();
	} catch {
		/* Steam already tore the tree down */
	}
}

/** Gives Steam's focus back to one of our registered elements (e.g. View My Achievements after the list closes). */
export function refocus(el: HTMLElement | null) {
	const r = el && registered.get(el);
	try {
		r?.node.m_Tree.Activate?.();
		r?.node.BTakeFocus(1);
	} catch {
		/* the page moved on */
	}
}

const LAYOUT_ROW = 2;

/**
 * A modal focus tree of our own, for an overlay such as the achievement list: while it is open the controller
 * moves only between its stops, like in Steam's own dialogs. Steam scrolls nothing for us, so a newly focused
 * stop is scrolled into view here.
 */
export class FocusTree {
	private tree: any;
	private unregister: (() => void) | null = null;
	private nodes = new Map<HTMLElement, { node: any; off: () => void }>();

	constructor(win: Window, root: HTMLElement, id: string) {
		const F = g.FocusNavController;
		const contexts: any[] = F?.m_rgAllContexts ?? [];
		const ctx = contexts.find((c) => c.m_rootWindow === win) ?? contexts.find((c) => c.m_rgGamepadNavigationTrees?.some((t: any) => t.m_ID === 'GamepadUI_Full_Root'));
		const parent = ctx?.m_rgGamepadNavigationTrees?.find((t: any) => t.m_ID === 'GamepadUI_Full_Root') ?? null;
		if (!F || !ctx) throw new Error("Steam's focus controller wasn't found");
		this.tree = F.NewGamepadNavigationTree(ctx, id, parent, { virtualFocus: false, modal: true, bIsEmbeddedInLegacyTree: false });
		this.tree.m_Root.OnMount(root);
		this.tree.SetIsEnabled(true);
		const unregister = F.RegisterGamepadNavigationTree(this.tree, win);
		// Steam's tree component turns raw presses on its root into logical events (vgp_ondirection for the D-pad,
		// vgp_onok for A), which the nodes act on. Without this nothing moves.
		const onButton = (e: Event) => this.tree.OnRootButtonDown(e);
		root.addEventListener('vgp_onbuttondown', onButton);
		this.unregister = () => {
			root.removeEventListener('vgp_onbuttondown', onButton);
			unregister();
		};
		this.tree.Activate();
	}

	/** Adds a stop (A clicks it, or runs onOk), or with row: true a container whose stops sit side by side. */
	add(el: HTMLElement, opts: { row?: boolean; onOk?: () => void } = {}) {
		if (this.nodes.has(el) || !el.isConnected) return;
		let parentNode = this.tree.m_Root;
		for (let p = el.parentElement; p; p = p.parentElement) {
			const n = this.nodes.get(p);
			if (n) {
				parentNode = n.node;
				break;
			}
		}
		const node = this.tree.CreateNode(parentNode, null);
		if (opts.row) {
			node.SetProperties({ layout: LAYOUT_ROW });
			node.OnMount(el);
			this.nodes.set(el, { node, off: () => {} });
			return;
		}
		node.SetProperties({ focusable: true, noFocusRing: true });
		if (el.tabIndex < 0) el.tabIndex = 0;
		node.OnMount(el);
		const sub = node.m_Focused?.Subscribe?.((on: boolean) => {
			el.classList.toggle('gpfocus', !!on);
			if (on) el.scrollIntoView({ block: 'nearest' });
		});
		const onOk = (e: Event) => {
			e.stopPropagation();
			e.preventDefault();
			(opts.onOk ?? (() => el.click()))();
		};
		el.addEventListener('vgp_onok', onOk);
		this.nodes.set(el, {
			node,
			off: () => {
				sub?.Unsubscribe?.();
				el.removeEventListener('vgp_onok', onOk);
				el.classList.remove('gpfocus');
			},
		});
	}

	/** Drops stops whose elements were replaced. */
	prune() {
		for (const [el, n] of Array.from(this.nodes)) {
			if (el.isConnected) continue;
			this.drop(el, n);
		}
	}

	private drop(el: HTMLElement, n: { node: any; off: () => void }) {
		this.nodes.delete(el);
		try {
			n.off();
			if (n.node.m_bMounted) n.node.OnUnmount();
		} catch {
			/* already gone */
		}
	}

	/** The stop that has the controller's focus, if any. */
	focused(): HTMLElement | null {
		for (const [el, n] of this.nodes) if (n.node.BHasFocus?.()) return el;
		return null;
	}

	focus(el: HTMLElement | null | undefined) {
		const n = el && this.nodes.get(el);
		if (!n) return;
		try {
			n.node.BTakeFocus(1);
		} catch {
			/* not focusable right now */
		}
	}

	close() {
		for (const [el, n] of Array.from(this.nodes)) this.drop(el, n);
		try {
			this.unregister?.();
		} catch {
			/* Steam already dropped it */
		}
		this.unregister = null;
	}
}

/** Drops nodes whose elements left the page (rebuilt block, another game). Pass `all` to drop every one. */
export function pruneFocusable(all = false) {
	for (const el of Array.from(registered.keys())) if (all || !el.isConnected) forget(el);
}
