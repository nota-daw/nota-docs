#!/usr/bin/env node
// Take the screenshots listed in shots/manifest.yml.
//
//   npm run shots                        everything
//   npm run shots -- devices/volt        ids equal to or under a prefix (several allowed)
//   npm run shots -- --source app        only app (or design) shots
//   npm run shots -- --theme light       only one theme
//
// design shots: ../nota-design mockups, served locally and shot with Playwright at 2×
//               ([data-screen-label] or a CSS selector). Mockups are dark-only.
// app shots:    the real app, rendered headlessly by shots/app (C#, Avalonia.Headless) in
//               both themes, with the machine's user/device names scrubbed.
// Output: src/assets/shots/<theme>/<id>.webp
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import YAML from 'yaml';
import sharp from 'sharp';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const appRepo = path.resolve(process.env.NOTA_REPO ?? path.join(root, '../nota'));
const designRepo = path.resolve(process.env.NOTA_DESIGN ?? path.join(root, '../nota-design'));
const cache = path.join(root, 'shots/.cache');
const assets = path.join(root, 'src/assets/shots');

// ── args ────────────────────────────────────────────────────────────────────────────
const argv = process.argv.slice(2);
const opt = (name) => {
  const i = argv.indexOf(name);
  return i >= 0 ? argv.splice(i, 2)[1] : undefined;
};
const onlySource = opt('--source');
const onlyTheme = opt('--theme');
const prefixes = argv.filter((a) => !a.startsWith('--'));

const manifest = YAML.parse(fs.readFileSync(path.join(root, 'shots/manifest.yml'), 'utf8'));
const all = manifest.shots ?? [];
const ids = new Set();
for (const s of all) {
  if (ids.has(s.id)) throw new Error(`duplicate shot id ${s.id}`);
  ids.add(s.id);
}
const wanted = all.filter(
  (s) =>
    (!onlySource || s.source === onlySource) &&
    (prefixes.length === 0 || prefixes.some((p) => s.id === p || s.id.startsWith(p.replace(/\/?$/, '/')))),
);
const themesOf = (s) => (s.themes ?? (s.source === 'design' ? ['dark'] : ['dark', 'light'])).filter((t) => !onlyTheme || t === onlyTheme);
if (wanted.length === 0) {
  console.log('no shots match');
  process.exit(0);
}

const failures = [];
const toWebp = async (png, theme, id) => {
  const out = path.join(assets, theme, id + '.webp');
  fs.mkdirSync(path.dirname(out), { recursive: true });
  await sharp(png).webp({ quality: 90, effort: 5 }).toFile(out);
};

// ── design ──────────────────────────────────────────────────────────────────────────
const design = wanted.filter((s) => s.source === 'design' && themesOf(s).length);
if (design.length) {
  if (!fs.existsSync(designRepo)) throw new Error(`nota-design not found at ${designRepo} (set NOTA_DESIGN)`);
  const server = http.createServer((req, res) => {
    const file = path.join(designRepo, decodeURIComponent(new URL(req.url, 'http://x').pathname));
    if (!file.startsWith(designRepo) || !fs.existsSync(file)) return res.writeHead(404).end();
    res.writeHead(200, { 'content-type': file.endsWith('.html') ? 'text/html' : 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${server.address().port}/`;
  const { chromium } = await import('playwright');
  const browser = await chromium.launch();
  // Mockups use the designer's own name in places ("<Name>'s iPhone"); show a neutral one.
  const fullName = spawnSync('id', ['-F'], { encoding: 'utf8' }).stdout?.trim() ?? '';
  const names = [...new Set([...fullName.split(/\s+/), os.userInfo().username])].filter((n) => n.length > 2);
  const byFile = Map.groupBy(design, (s) => s.file);
  for (const [file, shots] of byFile) {
    const page = await browser.newPage({ deviceScaleFactor: 2, viewport: { width: 1600, height: 1000 } });
    await page.goto(base + encodeURIComponent(file));
    await page.waitForTimeout(2500);
    await page.evaluate((names) => {
      const re = new RegExp(`\\b(${names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})\\b`, 'g');
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      for (let n = walker.nextNode(); n; n = walker.nextNode()) n.nodeValue = n.nodeValue.replace(re, "Alex");
    }, names);
    for (const s of shots) {
      try {
        const loc = s.label ? page.locator(`[data-screen-label="${s.label}"]`) : page.locator(s.selector);
        const png = path.join(cache, 'design', s.id + '.png');
        fs.mkdirSync(path.dirname(png), { recursive: true });
        if (s.inner) {
          // Board cells are [label, content…, caption]: keep only the content (drop short strips at the ends).
          const box = await loc.first().evaluate((el) => {
            let kids = [...el.children].map((c) => c.getBoundingClientRect());
            while (kids.length > 1 && kids[0].height < 60) kids.shift();
            while (kids.length > 1 && kids.at(-1).height < 60) kids.pop();
            const x = Math.min(...kids.map((r) => r.left)), y = Math.min(...kids.map((r) => r.top));
            return { x: x + scrollX, y: y + scrollY, width: Math.max(...kids.map((r) => r.right)) - x, height: Math.max(...kids.map((r) => r.bottom)) - y };
          });
          await page.screenshot({ path: png, clip: box, fullPage: true });
        } else {
          await loc.first().screenshot({ path: png });
        }
        for (const t of themesOf(s)) await toWebp(png, t, s.id);
        console.log(`ok     ${s.id}  (design)`);
      } catch (e) {
        failures.push(s.id);
        console.log(`FAILED ${s.id}: ${e.message.split('\n')[0]}`);
      }
    }
    await page.close();
  }
  await browser.close();
  server.close();
}

// ── app ─────────────────────────────────────────────────────────────────────────────
const app = wanted.filter((s) => s.source === 'app');
if (app.length) {
  const project = path.join(root, 'shots/app/NotaDocsShots.csproj');
  const build = spawnSync('dotnet', ['build', project, '-v', 'q', '--nologo', `-p:NotaRepo=${appRepo}`], { stdio: 'inherit' });
  if (build.status !== 0) throw new Error('harness build failed');
  for (const theme of ['dark', 'light']) {
    const shots = app.filter((s) => themesOf(s).includes(theme));
    if (!shots.length) continue;
    const out = path.join(cache, theme);
    const data = path.join(cache, `data-${theme}`);
    fs.mkdirSync(out, { recursive: true });
    fs.mkdirSync(data, { recursive: true });
    const job = path.join(cache, `job-${theme}.json`);
    fs.writeFileSync(job, JSON.stringify({ out, theme, shots: shots.map(({ id, scene, args }) => ({ id, scene, args: args ?? {} })) }));
    console.log(`── app · ${theme} · ${shots.length} shot(s)`);
    const run = spawnSync('dotnet', ['run', '--no-build', '--project', project, '--', job], {
      encoding: 'utf8',
      env: { ...process.env, NOTA_DATA_DIR: data, NOTA_REPO: appRepo },
    });
    const okIds = new Set();
    for (const line of (run.stdout ?? '').split('\n')) {
      if (line.startsWith('ok ')) okIds.add(line.slice(3).trim());
      else if (line.startsWith('FAILED')) {
        failures.push(`${line.split(' ')[1].replace(/:$/, '')} (${theme})`);
        console.log(line);
      }
    }
    if (run.status !== 0 && okIds.size === 0) console.log(run.stderr || run.stdout);
    for (const s of shots) {
      if (!okIds.has(s.id)) continue;
      await toWebp(path.join(out, s.id + '.png'), theme, s.id);
      console.log(`ok     ${s.id}  (app · ${theme})`);
    }
  }
}

console.log(`${wanted.length} shot(s) · ${failures.length} failed`);
process.exit(failures.length ? 1 : 0);
