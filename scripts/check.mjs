#!/usr/bin/env node
// Docs consistency check. Run after `npm run build` (link checks read dist/).
//
//   node scripts/check.mjs            errors fail the run, warnings are printed
//   node scripts/check.mjs --status   also print per-section page status (todo/draft/done)
//
// Checks:
//   - every EN page has a RU twin at the same path, and vice versa
//   - the twins agree on `updated:` (one language was updated without the other)
//   - every page listed in coverage.yml exists
//   - every <Shot id="…"> is listed in shots/manifest.yml
//   - every `sources:` path exists in the app repo (../nota, or $NOTA_REPO)
//   - internal links in dist/ resolve to a built page or asset
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import YAML from 'yaml';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const docs = path.join(root, 'src/content/docs');
const appRepo = path.resolve(process.env.NOTA_REPO ?? path.join(root, '../nota'));
const errors = [];
const warnings = [];

const walk = (dir) =>
  fs.existsSync(dir)
    ? fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
        e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)],
      )
    : [];

const frontmatter = (file) => {
  const m = fs.readFileSync(file, 'utf8').match(/^---\n([\s\S]*?)\n---/);
  return m ? (YAML.parse(m[1]) ?? {}) : {};
};

// ── pages ───────────────────────────────────────────────────────────────────────────
const pages = walk(docs).filter((f) => /\.mdx?$/.test(f));
const slugOf = (f) => path.relative(docs, f).replace(/\.mdx?$/, '');
const en = new Map();
const ru = new Map();
for (const f of pages) {
  const slug = slugOf(f);
  if (slug.startsWith('ru/')) ru.set(slug.slice(3), f);
  else en.set(slug, f);
}

for (const [slug, f] of en) {
  const twin = ru.get(slug);
  if (!twin) {
    errors.push(`missing RU twin: ru/${slug} (for ${slug})`);
    continue;
  }
  const a = frontmatter(f);
  const b = frontmatter(twin);
  if ((a.updated ?? '') !== (b.updated ?? ''))
    errors.push(`stale twin: ${slug} updated=${a.updated ?? '—'} but ru/${slug} updated=${b.updated ?? '—'}`);
  for (const src of a.sources ?? []) {
    if (!fs.existsSync(path.join(appRepo, src))) warnings.push(`${slug}: source not found in app repo: ${src}`);
  }
}
for (const slug of ru.keys()) if (!en.has(slug)) errors.push(`RU page without EN original: ru/${slug}`);

// ── coverage.yml ────────────────────────────────────────────────────────────────────
const coverage = YAML.parse(fs.readFileSync(path.join(root, 'coverage.yml'), 'utf8'));
const covered = [];
const collect = (node) => {
  if (Array.isArray(node)) node.forEach(collect);
  else if (node && typeof node === 'object') {
    if (typeof node.page === 'string') covered.push(node.page);
    Object.values(node).forEach(collect);
  }
};
collect(coverage.sections);
for (const slug of covered) if (!en.has(slug)) errors.push(`coverage.yml lists ${slug} but the page does not exist`);
if (coverage.pending?.length) warnings.push(`coverage.yml has ${coverage.pending.length} pending change(s) waiting for their pages`);

// ── screenshots ─────────────────────────────────────────────────────────────────────
const manifest = YAML.parse(fs.readFileSync(path.join(root, 'shots/manifest.yml'), 'utf8'));
const shotIds = new Set((manifest.shots ?? []).map((s) => s.id));
for (const f of pages) {
  for (const m of fs.readFileSync(f, 'utf8').matchAll(/<Shot\s[^>]*id="([^"]+)"/g)) {
    if (!shotIds.has(m[1])) errors.push(`${slugOf(f)}: <Shot id="${m[1]}"> is not in shots/manifest.yml`);
  }
}

// ── links in the built site ─────────────────────────────────────────────────────────
const dist = path.join(root, 'dist');
if (fs.existsSync(dist)) {
  const base = (process.env.BASE_PATH ?? '/').replace(/\/?$/, '/');
  const exists = (urlPath) => {
    const rel = decodeURIComponent(urlPath.slice(base.length));
    const p = path.join(dist, rel);
    return fs.existsSync(p) && (fs.statSync(p).isFile() || fs.existsSync(path.join(p, 'index.html')))
      || fs.existsSync(p + '.html');
  };
  for (const f of walk(dist).filter((f) => f.endsWith('.html'))) {
    const html = fs.readFileSync(f, 'utf8');
    for (const m of html.matchAll(/href="([^"#?]+)[^"]*"/g)) {
      let href = m[1];
      if (/^(https?:|mailto:|data:|\/\/)/.test(href)) continue;
      if (!href.startsWith('/')) href = path.posix.join('/' + path.relative(dist, path.dirname(f)).split(path.sep).join('/'), href);
      if (!href.startsWith(base)) continue;
      if (!exists(href)) errors.push(`broken link in ${path.relative(dist, f)}: ${m[1]}`);
    }
  }
} else {
  warnings.push('dist/ not found — run `npm run build` first to check links');
}

// ── report ──────────────────────────────────────────────────────────────────────────
if (process.argv.includes('--status')) {
  const bySection = {};
  for (const [slug, f] of en) {
    const s = frontmatter(f).status ?? 'done';
    const sec = slug.includes('/') ? slug.split('/')[0] : slug;
    (bySection[sec] ??= { todo: 0, draft: 0, done: 0 })[s]++;
  }
  console.log('section'.padEnd(16), 'todo draft done');
  for (const [sec, c] of Object.entries(bySection))
    console.log(sec.padEnd(16), String(c.todo).padStart(4), String(c.draft).padStart(5), String(c.done).padStart(4));
}
const uniq = (a) => [...new Set(a)];
for (const w of uniq(warnings)) console.log(`warn   ${w}`);
for (const e of uniq(errors)) console.log(`error  ${e}`);
console.log(`${en.size} pages · ${uniq(errors).length} error(s) · ${uniq(warnings).length} warning(s)`);
process.exit(errors.length ? 1 : 0);
