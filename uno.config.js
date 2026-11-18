// UnoCSS runs at dev time (npm run unocss:build); the app serves the generated
// wwwroot/css/unocss.css statically. Every colour, border and shadow resolves to
// the CSS variables defined in css/ui.css, so the palette and its light/dark
// token layer keep a single source of truth.
import presetWind3 from "@unocss/preset-wind3";

const v = (name) => `var(--${name})`;

export default {
  // The app styles its own elements; preset-wind's preflight would reset them.
  presets: [presetWind3({ preflight: false })],
  content: {
    pipeline: {
      include: ["src/FdroidMgr.Api/wwwroot/**/*.{html,js}"],
    },
  },
  theme: {
    colors: {
      bg: v("bg"),
      card: v("card"),
      ink: v("ink"),
      muted: v("muted"),
      accent: v("accent"),
      "accent-strong": v("accent-strong"),
      ok: v("ok"),
      "ok-soft": v("ok-soft"),
      err: v("err"),
      "err-soft": v("err-soft"),
      line: v("border"),
      field: v("input-bg"),
      chip: v("chip"),
      soft: v("soft"),
      "pre-bg": v("pre-bg"),
      "pre-ink": v("pre-ink"),
    },
    borderRadius: {
      card: "12px",
      field: "6px",
      tile: "8px",
      icon: "10px",
    },
    boxShadow: {
      card: v("shadow"),
      "card-hover": v("shadow-hover"),
      ring: "0 0 0 3px var(--ring)",
      menu: "0 12px 32px rgba(4, 8, 16, 0.32)",
      dialog: "0 24px 60px rgba(4, 8, 16, 0.45)",
    },
    fontFamily: {
      ui: 'system-ui, -apple-system, "Segoe UI", sans-serif',
      mono: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
    },
  },
  // The shared vocabulary: views compose these instead of inventing a class of
  // their own. Layout (flex/grid/gap/sizes) stays as utilities in the markup.
  shortcuts: [
    ["card", "bg-card border border-solid border-line rounded-card px-5 py-4 mb-4 shadow-card transition hover:shadow-card-hover"],
    ["panel", "border-dashed mt-[0.6rem] bg-soft"],
    ["cards", "grid gap-3"],
    ["form-grid", "grid gap-3 grid-cols-[repeat(auto-fit,minmax(220px,1fr))]"],
    ["col-full", "col-span-full"],

    ["btn", "inline-block text-center bg-card text-ink border border-solid border-line rounded-field px-[0.9rem] py-[0.45rem] cursor-pointer transition hover:border-accent focus-visible:outline-2 focus-visible:outline-solid focus-visible:outline-accent focus-visible:outline-offset-2 disabled:opacity-40 disabled:cursor-default"],
    ["btn-primary", "bg-accent text-white border-accent hover:bg-accent-strong hover:border-accent-strong"],
    ["btn-danger", "text-err border-err bg-transparent hover:bg-err-soft"],
    ["btn-link", "border-none bg-none text-accent px-1 py-0 hover:text-accent-strong hover:underline"],
    ["btn-ghost", "bg-white/12 border border-solid border-white/28 text-white text-[0.9rem] leading-[1.2] no-underline px-[0.65rem] py-[0.3rem] rounded-field transition hover:bg-white/24 hover:border-white/45 hover:text-white focus-visible:outline-2 focus-visible:outline-solid focus-visible:outline-white focus-visible:outline-offset-2"],
    ["btn-quiet", "bg-chip border border-solid border-line text-ink hover:bg-soft hover:border-accent"],

    ["hint", "text-[0.8rem] text-muted mt-2"],
    ["msg", "text-[0.85rem] mt-2"],
    ["ok", "text-ok"],
    ["err", "text-err"],
    ["muted", "text-muted"],
    ["pre-block", "bg-pre-bg text-pre-ink rounded-field px-4 py-3 overflow-auto font-mono text-[0.8rem] whitespace-pre-wrap mt-2"],
    ["secret-block", "bg-pre-bg text-pre-ink px-[0.7rem] py-[0.5rem] rounded-field break-all grow min-w-200px select-all"],

    ["row", "flex items-center gap-3 flex-wrap"],
    ["col", "flex flex-col"],
    ["page-title", "flex items-center gap-2 m-0"],

    ["table-releases", "w-full border-collapse mt-3 text-[0.9rem] [&_th]:text-left [&_th]:border-b [&_th]:border-b-solid [&_th]:border-b-line [&_th]:px-2.5 [&_th]:py-2 [&_th]:text-[0.75rem] [&_th]:uppercase [&_th]:tracking-0.04em [&_th]:text-muted [&_td]:text-left [&_td]:border-b [&_td]:border-b-solid [&_td]:border-b-line [&_td]:px-2.5 [&_td]:py-2 [&_tbody_tr:hover_td]:bg-soft [&_td_.hint]:m-0"],

    ["stat-value", "text-[2.2rem] font-700 leading-[1.1]"],
    ["stat-num", "font-700 text-accent whitespace-nowrap"],
    ["share-bar", "h-6px rounded-3px bg-chip overflow-hidden mt-[0.4rem]"],
    ["share-fill", "h-full bg-accent rounded-3px transition-width duration-200"],
    ["chart-bars", "flex items-end gap-3px h-160px pt-2"],
    ["chart-col", "flex-1 h-full flex flex-col justify-end items-stretch min-w-0 [&_.chart-count]:hidden [&:hover_.chart-count]:block"],
    ["chart-bar", "bg-accent rounded-t-3px min-h-2px relative transition hover:bg-accent-strong"],
    ["chart-count", "absolute -top-[1.15em] left-1/2 -translate-x-1/2 text-[0.65rem] text-muted whitespace-nowrap"],
    ["chart-day", "text-center text-[0.6rem] text-muted mt-3px overflow-hidden text-ellipsis whitespace-nowrap"],

    ["toolbar", "flex items-center justify-between gap-4 flex-wrap mb-4"],
    ["toolbar-search", "flex gap-2 my-4 [&_input]:grow [&_select]:w-auto"],
    ["pager", "flex items-center justify-center gap-4 mt-4"],

    ["chip-user", "inline-flex items-center gap-2 bg-transparent border-none text-white text-[0.9rem] font-550 px-[0.5rem] py-[0.15rem] rounded-full cursor-pointer whitespace-nowrap hover:bg-white/14 focus-visible:outline-2 focus-visible:outline-solid focus-visible:outline-white focus-visible:outline-offset-2"],
    ["avatar-fallback", "inline-flex items-center justify-center bg-accent text-white font-700 text-[0.95rem]"],
    ["menu", "absolute right-0 top-[calc(100%+0.45rem)] min-w-190px bg-card text-ink border border-solid border-line rounded-icon shadow-menu p-[0.4rem] z-100 [&_button]:block [&_button]:w-full [&_button]:text-left [&_button]:bg-none [&_button]:border-none [&_button]:text-ink [&_button]:px-3 [&_button]:py-2 [&_button]:rounded-field [&_button:hover]:bg-chip"],
    ["avatar", "w-30px h-30px rounded-full border-2 border-solid border-white/65 shrink-0"],

    ["dialog-shell", "border border-solid border-line rounded-card bg-card text-ink w-[min(760px,calc(100vw-2rem))] max-w-full p-0 overflow-hidden shadow-dialog"],
    ["dialog-head", "flex items-center justify-between gap-4 px-[1.1rem] py-[0.8rem] border-b border-b-solid border-b-line [&_h3]:m-0 [&_h3]:text-16px [&_button]:border-none [&_button]:bg-none [&_button]:text-muted [&_button]:text-[1rem] [&_button]:px-[0.4rem] [&_button]:py-[0.2rem] [&_button]:cursor-pointer [&_button:hover]:text-ink"],
    ["dialog-body", "px-[1.1rem] py-[0.9rem] [&_.pre-block]:m-0 [&_.pre-block]:max-h-55vh [&_.pre-block]:overflow-auto"],

    // Collapsed section affordance: same control in the release form and the
    // create-app panel, read as a link until opened.
    ["summary-toggle", "[&_summary]:inline-flex [&_summary]:items-center [&_summary]:gap-[0.4rem] [&_summary]:py-[0.35rem] [&_summary]:px-[0.6rem] [&_summary]:rounded-field [&_summary]:text-accent [&_summary]:font-600 [&_summary:hover]:text-accent-strong [&_summary:hover]:underline [&_summary:hover]:bg-soft [&[open]_summary]:mb-3 [&[open]_summary]:p-0 [&[open]_summary]:text-ink [&[open]_summary:hover]:bg-none [&[open]_summary:hover]:no-underline"],

    ["app-icon", "w-12 h-12 rounded-icon object-contain shrink-0 bg-chip p-1 border border-solid border-line cursor-pointer hover:border-accent"],
    ["screenshot", "h-176px w-auto max-w-full border border-solid border-line rounded-tile bg-chip"],
    ["url-line", "block bg-pre-bg text-pre-ink rounded-field px-[0.7rem] py-[0.5rem] text-[0.78rem] break-all my-[0.35rem]"],
    ["qr-frame", "shrink-0 w-160px h-160px p-[0.5rem] border border-solid border-line rounded-tile bg-white [&_svg]:w-full [&_svg]:h-full [&_svg]:block"],
    ["scope-fieldset", "border border-solid border-line rounded-tile px-[0.8rem] py-[0.6rem] m-0 [&_legend]:text-[0.85rem] [&_legend]:text-muted [&_legend]:px-[0.3rem]"],
    ["scope-label", "text-ink text-[0.85rem] mt-[0.35rem]"],
    ["stat-total", "flex flex-col gap-[0.15rem]"],
    ["stat-label", "text-[0.8rem] text-muted uppercase tracking-0.04em"],
    ["stat-row", "border border-solid border-line rounded-tile px-[0.65rem] py-[0.5rem] bg-soft"],
    ["stat-row-head", "flex items-baseline justify-between gap-3 flex-wrap"],
    ["stat-row-sub", "text-[0.8rem] text-muted mt-[0.15rem]"],

    ["dropzone", "flex flex-col items-center gap-1 px-4 py-9 border-2 border-dashed border-line rounded-icon bg-soft cursor-pointer text-muted text-center transition hover:border-accent hover:bg-chip hover:shadow-ring [&.dragover]:border-accent [&.dragover]:bg-chip [&.dragover]:shadow-ring [&_input[type=file]]:hidden [&_.dz-icon]:text-[1.6rem] [&_.dz-title]:text-[1.05rem] [&_.dz-title]:font-600 [&_.dz-title]:text-ink [&_.dz-file]:text-[0.85rem]"],
    ["dropzone-file", "border-ok bg-ok-soft [&_.dz-title]:text-ok [&_.dz-file]:text-ok"],
  ],
};
