# UnoCSS: the shared vocabulary

The UI's look comes from one place: `:root` tokens (`css/ui.css`) and the
component vocabulary in `uno.config.js`. Views compose them — they do not
invent classes.

## Build model

UnoCSS runs at **dev time** and the app serves the generated sheet statically.

- `npm install`, then `npm run unocss:build` → `wwwroot/css/unocss.css`,
  containing only the utilities and shortcuts the markup actually uses.
- `npm run watch` during a styling session.
- `unocss-check` (CI) regenerates and fails on drift, so a missed local build
  cannot leave the committed CSS behind the markup.
- No runtime, no first-paint flash, no Node step in the .NET pipeline.

`wwwroot/css/ui.css` keeps what utilities cannot express:

- the token layer (`:root`, `html[data-theme="dark"]`),
- element base rules (`*`, `body`, `header`, `main`, `button` incl. `:disabled`,
  `input/textarea/select`, `label`, `ul`, `li`, `code`, `details summary`) —
  an element rule cannot be forgotten by a template,
- the app chrome (`.header-actions` and its narrow-viewport rule),
- `#index-health`, whose content is injected HTML sitting on the always-dark
  header and needs brighter variants than the ink tokens.

Preset preflight is off (the app styles its own elements), so **every rule that
paints a border also names its style** (`border-solid`, `border-dashed`).
`*` must not set `border-style`: with `border-width` at its initial `medium`
and `border-color` at `currentColor`, a global style paints a border on every
single element.

## Choosing a form

| situation | use |
|---|---|
| layout of one element | utilities in the markup (`flex items-start gap-4`) |
| a named component, or a shape reused 3+ times | shortcut (`row`, `pager`, `qr-frame`) |
| a look that no template should have to remember | element rule in `ui.css` |

Shortcuts resolve to `var(--token)` colours, so the dark theme keeps working.
A literal colour in a shortcut silently breaks it.

## Verification

A browser engine cannot judge this CSS: Lightpanda resolves no colours, borders
or shadows, and its `getComputedStyle` returns empty strings, so a before/after
screenshot diff proves nothing. `ci/ui-rig` still matters, but for structure —
views, routes, requests, panels surviving the class-name churn.

What a stylesheet move is verified with instead:

1. **`ci/unocss-parity.mjs`** — declaration-level parity between the `ui.css` a
   class left and the utilities that replaced it, plus an **orphan check**
   (`--all`): every old class the markup still carries must still paint
   something, and an **audit** (`--audit`): every token the markup carries must
   have a rule in the new CSS. The first three found real bugs during the
   conversion (`btn-link` padding, `chip-user` padding, `share-bar` margin,
   `app-card` never mapped, `.workspace-toolbar h2` and `table td .hint`
   losing their element-targeted rules); the audit found one after merge
   review: `backdrop:bg-*` is not a `::backdrop` selector, UnoCSS drops it
   without a trace, and the dialog's dim overlay went with it. (A `//` comment
   in the same file was swallowing the `*` rule the same way — the parser reads
   it as a selector — which is why every comment there is `/* */`.)

   ```
   node ci/unocss-parity.mjs --old before.css --new css/ui.css --new css/unocss.css \
     --markup ui.html,js/ui.js --all --audit \
     --classes 'meta-pre=pre-block,app-pager=pager'
   ```

   Read the report, do not just count it: spellings (`48px` vs `3rem`, `flex: 1`
   vs `flex: 1 1 0%`, UnoCSS opacity and transform variables) are noise, value
   changes are not.
2. **`ci/ui-rig`** — 21 structural checks after every converted view.
3. **`unocss-check`** — generated CSS matches the committed one.
4. **A maintainer pass in a real browser**, screen by screen, light and dark:
   login, app list (filter / sort / paging), app detail with each panel,
   statistics, API keys, F-Droid client key, the index-error dialog.

## Deviations the conversion accepted on purpose

- Transitions come from the preset's `transition` utility (colour, border,
  shadow, opacity, transform, filter) instead of each rule's hand-picked list.
- `row` unifies three near-identical rows; their gap went 0.6rem → 0.75rem.
- `h2 button.ghost` and the pager's `.ghost` buttons are one thing now
  (`btn-quiet`). The pager's were white-on-white in the light theme; the fix is
  a side effect of the vocabulary, not a coincidence.
- `.api-key-meta code { margin-right }` is gone: those rows are flex with a gap.
