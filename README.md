# Nota Docs

The user manual for [Nota](https://github.com/nota-daw/nota), in English and Russian.
Built with [Astro Starlight](https://starlight.astro.build) and published to GitHub Pages.

```bash
npm install
npm run dev        # http://localhost:4321
npm run build      # → dist/
npm run check      # twins, coverage, screenshot ids, sources, links (after build)
npm run status     # how many pages are todo / draft / done per section
```

## Layout

| Path | What |
|---|---|
| `src/content/docs/<slug>.mdx` | English page |
| `src/content/docs/ru/<slug>.mdx` | Russian twin — same path, same structure |
| `src/components/` | `Shot` (themed screenshot), `Kbd` (key caps) — import via `@components/…` |
| `src/assets/shots/{dark,light}/` | Screenshots, listed in `shots/manifest.yml` |
| `templates/` | `device.mdx`, `page.mdx` — copy for a new page |
| `coverage.yml` | Every app feature → its page, with the app source paths it's written from |
| `PLAN.md` | Structure, stages, writing rules |

Page frontmatter adds `since`, `updated` (the Nota version the page was last checked
against), `sources` (paths in the app repo) and `status` (`todo` · `draft` · `done`).

`check.mjs` looks for the app repo at `../nota` (override with `NOTA_REPO`).

Docs are kept in sync with the app by the `nota-docs` skill in the app repo
(`.claude/skills/nota-docs`).

Geist fonts: SIL OFL 1.1, see `public/fonts/OFL-1.1-Geist.txt`.
