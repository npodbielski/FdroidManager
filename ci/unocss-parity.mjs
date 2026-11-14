// Declaration-level parity between hand-written CSS and generated utilities.
//
// A browser-engine check cannot judge CSS here (Lightpanda resolves no colours,
// borders or shadows), so a class moved out of ui.css is verified by comparing
// the declarations it used to carry with the ones the generated sheet gives the
// classes that replaced it. One line per differing declaration; intentional
// differences are expected and read by a human, unexpected ones are bugs — this
// is how `border-style` (invisible borders with preflight off), `leading-1.2`
// and `btn-link`'s padding were caught.
//
//   node ci/unocss-parity.mjs --old before.css --new ui.css --new unocss.css \
//     --classes 'card=card,ghost(header)=btn-ghost,caret=text-[0.65rem] opacity-85'
//
// `--classes` maps each old class to the class tokens that now carry it; an
// optional (label) marks which use of a reused old class is meant.
//
// `--all` (with --markup) adds the orphan check: an old class the markup still
// carries but nothing styles any more. `--audit` (with --markup) is its
// inverse: a token the markup carries that the new CSS has no rule for — which
// is how the `backdrop:bg-*` token was found: it is not a `::backdrop`
// selector, UnoCSS drops it without a trace, and no old class name pointed at
// it.
import { readFileSync } from "node:fs";

const argv = process.argv.slice(2);
const opts = { old: "", new: [], classes: [] };
const VALUELESS = new Set(["all", "audit"]);
for (let i = 0; i < argv.length; i++) {
  const arg = argv[i];
  if (!arg.startsWith("--")) continue;
  const key = arg.slice(2);
  if (VALUELESS.has(key)) { opts[key] = true; continue; }
  if (key === "new") opts.new.push(argv[++i]);
  else opts[key] = argv[++i];
}
const oldCss = readFileSync(opts.old, "utf8");
const mappings = (typeof opts.classes === "string" ? opts.classes : "")
  .split(",").filter((spec) => spec.trim() !== "").map((spec) => {
  const [left, right = ""] = spec.split("=");
  const [name] = left.split("(");
  return { old: name, label: left.match(/\((.*)\)/)?.[1] ?? "", replacements: right.trim() };
});

const SIDES = ["top", "right", "bottom", "left"];
const COLOURISH = /color|shadow|^background$|^outline$/;
// (text-decoration, transition and font shorthands are expanded above.)
const UNITS = /^\d|(px|rem|em|%)$/;

// Split a declaration value on whitespace, keeping functions intact.
function parts(value) {
  const out = [];
  let depth = 0, cur = "";
  for (const c of value) {
    if (c === "(") depth++;
    if (c === ")") depth--;
    if (c === " " && depth === 0) { if (cur) out.push(cur), (cur = ""); continue; }
    cur += c;
  }
  if (cur) out.push(cur);
  return out;
}

function colour(value) {
  const hex = value.match(/^#([0-9a-f]{6}|[0-9a-f]{3})$/i);
  if (hex) {
    const h = hex[1];
    const n = (h.length === 3 ? [...h].map((c) => c + c) : h.match(/../g)).map((c) => parseInt(c, 16));
    return `rgb(${n[0]} ${n[1]} ${n[2]})`;
  }
  const m = value.match(/^rgba?\(([^)]+)\)$/);
  if (!m) return value.trim();
  const n = m[1].split(/[,\s/]+/).filter(Boolean);
  return n.length > 3 ? `rgb(${n[0]} ${n[1]} ${n[2]} / ${n[3]})` : `rgb(${n[0]} ${n[1]} ${n[2]})`;
}

const isColour = (v) => /^(rgba?\(|#|[a-z]+$)/.test(v) && !/^(none|auto|inherit|cover|solid|dashed|dotted)$/.test(v);

// Shorthands and spellings differ between a hand-written rule and a generated
// one; compare what they resolve to, and record border-style explicitly because
// preset preflight is off (a border with no style is invisible).
function expand(prop, value) {
  const out = new Map();
  const edge = (base) => {
    const edges = new Map();
    for (const p of parts(value)) {
      if (UNITS.test(p) && !isColour(p)) edges.set(`${base}-width`, p);
      else if (/^(solid|dashed|dotted|double|none|hidden|groove|ridge|inset|outset)$/.test(p)) edges.set(`${base}-style`, p);
      else edges.set(`${base}-color`, colour(p));
    }
    return edges;
  };
  if (/^border(-(top|bottom|left|right))?$/.test(prop) && parts(value).length > 1) return edge(prop);
  if (prop === "outline" && parts(value).length > 1) return edge("outline");
  if (prop === "padding" || prop === "margin") {
    const p = parts(value).map((v) => (v === "0" ? "0" : v));
    const [a, b = a, c = a, d = b] = p;
    out.set(`${prop}-${SIDES[0]}`, a); out.set(`${prop}-${SIDES[1]}`, b);
    out.set(`${prop}-${SIDES[2]}`, c); out.set(`${prop}-${SIDES[3]}`, d);
    return out;
  }
  if (prop === "font") {
    const size = value.match(/(\d*\.?\d+(?:rem|px|em))(?=\s|\/|$)/);
    if (size) out.set("font-size", size[1]);
    const weight = value.match(/\b(\d{3})\b/);
    if (weight) out.set("font-weight", weight[1]);
    return out;
  }
  if (prop === "background") {
    if (value === "none") return new Map([["background-image", "none"]]);
    if (value === "transparent") return new Map([["background-color", "transparent"]]);
    if (value.includes("gradient")) return new Map([["background-image", value]]);
    return new Map([["background-color", colour(value)]]);
  }
  if (prop === "text-decoration") return new Map([["text-decoration-line", parts(value)[0]]]);
  if (prop === "border-radius") {
    return new Map([[prop, /^(999px|9999px|50%)$/.test(value) ? "circle" : value]]);
  }
  if (COLOURISH.test(prop) && prop !== "background-image") {
    if (prop === "box-shadow") return new Map([[prop, value.replace(/rgba?\([^)]+\)/g, colour)]]);
    return new Map([[prop, colour(value)]]);
  }
  if (value === "none" && /^(border|outline)/.test(prop)) return new Map([[prop.replace(/-color$/, "-style").replace(/^(border|outline)$/, "$1-style"), "none"]]);
  return new Map([[prop, value]]);
}

function resolve(decls) {
  const shadow = decls.get("--un-shadow");
  if (shadow && decls.get("box-shadow")?.includes("--un-shadow")) decls.set("box-shadow", shadow);
  for (const [k, v] of decls) {
    if (k.startsWith("--un-")) decls.delete(k);
    else decls.set(k, v.replace(/ \/ var\(--un-[a-z-]+\)/, "").replace(/var\(--un-[a-z-]+-color, ([^)]+)\)/, "$1"));
  }
  return decls;
}

function rules(css) {
  const list = [];
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, "");
  for (const m of stripped.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const decls = new Map();
    for (const decl of m[2].split(";")) {
      const i = decl.indexOf(":");
      if (i < 0) continue;
      const prop = decl.slice(0, i).trim();
      const value = decl.slice(i + 1).trim();
      if (!prop || !value) continue;
      for (const [k, v] of expand(prop, value)) decls.set(k, v);
    }
    resolve(decls);
    for (const selector of m[1].split(",").map((s) => s.trim())) {
      if (selector) list.push({ selector, decls });
    }
  }
  return list;
}

// What a class contributes, keyed by the rest of the selector so that `.x` and
// `.x:hover` (or a descendant rule) stay apart.
function contribution(ruleList, cls) {
  const self = new Map();
  const descendants = [];
  const escaped = cls.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const compound = new RegExp(`[.#]${escaped}(?![\w-])`);
  const pseudoTail = /(:[\w-]+(\([^)]*\))?)+$/g;
  for (const { selector, decls } of ruleList) {
    // Generated selectors escape utility syntax (`.[&_th]\:text-left`); unescape
    // before matching so arbitrary-variant utilities are found by their name.
    const plain = selector.replace(/\\(.)/g, "$1");
    if (!compound.test(plain)) continue;
    const at = plain.search(new RegExp(`[.#]${escaped}(?![\w-])`));
    const after = plain.slice(at).replace(new RegExp(`^[.#]${escaped}(?![\w-])`), "");
    const pseudo = after.match(pseudoTail)?.[0] ?? "(self)";
    if (after.replace(pseudoTail, "").trim()) {
      descendants.push({ selector: plain, decls });
      continue;
    }
    if (!self.has(pseudo)) self.set(pseudo, new Map());
    for (const [k, v] of decls) self.get(pseudo).set(k, v);
  }
  return { self, descendants };
}

const oldRules = rules(oldCss);
const newRules = opts.new.flatMap((f) => rules(readFileSync(f, "utf8")));
let total = 0;

const seenDescendants = new Set();
for (const { old: cls, label, replacements } of mappings) {
  const { self: before, descendants } = contribution(oldRules, cls);
  for (const d of descendants) {
    const key = `${cls} ${d.selector}`;
    if (seenDescendants.has(key)) continue;
    seenDescendants.add(key);
    console.log(`# descendant rule kept for manual mapping: ${d.selector} { ${[...d.decls].map(([k, v]) => `${k}:${v}`).join("; ")} }`);
  }
  const after = new Map();
  for (const token of replacements ? replacements.split(/\s+/) : [cls]) {
    for (const [where, decls] of contribution(newRules, token).self) {
      if (!after.has(where)) after.set(where, new Map());
      for (const [k, v] of decls) after.get(where).set(k, v);
    }
  }
  const lines = [];
  for (const [where, decls] of before) {
    const now = after.get(where);
    for (const [prop, value] of decls) {
      const got = now?.get(prop);
      if (got === undefined) lines.push(`  ${where} ${prop}: ${value} — dropped`);
      else if (got !== value) lines.push(`  ${where} ${prop}: ${value} → ${got}`);
    }
  }
  for (const [where, decls] of after) {
    const was = before.get(where);
    for (const [prop, value] of decls) if (!was?.has(prop)) lines.push(`  ${where} ${prop}: ${value} — added`);
  }
  const name = label ? `${cls} (${label})` : cls;
  if (lines.length) { total += lines.length; console.log(`${name} → ${replacements}\n${lines.join("\n")}`); }
  else console.log(`${name} → ${replacements}: identical`);
}
console.log(`\n${total} declaration difference(s) across ${mappings.length} mapping(s)`);

// Completeness, as important as the diffs above: a renamed class that a template
// still carries styles nothing, and nothing else here would notice.
if (opts.all !== undefined) {
  // A class is "orphaned" when the markup still carries it but nothing paints it
  // any more: renamed in the stylesheet, missed in a template.
  // Only class attributes and classList calls count; ids share the namespace of
  // class names and would report every `id="user-avatar"` as an orphan.
  const markup = opts.markup.split(",").map((f) => readFileSync(f, "utf8")).join("\n");
  const used = [...markup.matchAll(/class="([^"]*)"|classList\.(?:add|remove|toggle)\("([^"]*)"\)/g)]
    .map((m) => m[1] ?? m[2]).join(" ");
  const names = new Set();
  for (const { selector } of oldRules) for (const c of selector.matchAll(/\.([a-zA-Z][\w-]*)/g)) names.add(c[1]);
  const orphans = [...names].filter((n) => contribution(oldRules, n).self.size > 0
    && contribution(newRules, n).self.size === 0
    && new RegExp(`(^|\\s)${n}(\\s|$)`).test(used));
  console.log(`\norphaned (markup uses it, nothing styles it): ${orphans.join(" ") || "none"}`);
}

// The inverse of the orphan check: every token the markup carries must have a
// rule in the new CSS. Backslashes in selectors are CSS escaping
// (`.text-\[0.65rem\]`), so they are stripped before matching.
if (opts.audit !== undefined) {
  const markup = opts.markup.split(",").map((f) => readFileSync(f, "utf8")).join("\n");
  const tokens = new Set();
  for (const m of markup.matchAll(/class="([^"]*)"/g)) tokens.add(...m[1].split(/\s+/));
  for (const m of markup.matchAll(/classList\.(?:add|remove|toggle)\((['"])((?:[^)'\\]|\\.)*)\1/g)) tokens.add(...m[2].split(/\s+/));
  const norm = (s) => s.replace(/\\(.)/gs, "$1");
  const escapeRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const styled = (sheet, t) => new RegExp(`\\.${escapeRe(t)}(?=[\\s:,{.&>\\[]|$)`).test(sheet);
  const now = norm(opts.new.map((f) => readFileSync(f, "utf8")).join("\n"));
  const before = norm(oldCss);
  const plausible = [...tokens].filter((t) => /^[a-zA-Z][\w/:[\]-]*$/.test(t));
  const missing = plausible.filter((t) => !styled(now, t) && styled(before, t));
  const never = plausible.filter((t) => !styled(now, t) && !styled(before, t));
  console.log(`\naudit (markup token with no rule in the new CSS):`);
  console.log(`  styled before, gone now: ${missing.join(" ") || "none"}`);
  console.log(`  never styled (JS hooks etc.): ${never.join(" ") || "none"}`);
}
