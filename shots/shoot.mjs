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
// remote shots: the Nota Remote phone page on the app repo's stand-in Nota, at phone size, in
//               Studio Dark (dark) and Studio Light (light).
// app shots:    the real app, rendered headlessly by shots/app (C#, Avalonia.Headless) in
//               both themes, with the machine's user/device names scrubbed.
// Output: src/assets/shots/<theme>/<id>.webp
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import os from 'node:os';
import { spawn, spawnSync } from 'node:child_process';
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

// ── remote ──────────────────────────────────────────────────────────────────────────
// The phone page of Nota Remote, served by the app repo's stand-in Nota
// (src/web/remote/mock/nota-mock.mjs) and shot at phone size. Each site theme wears the
// matching Nota theme (Studio Dark / Studio Light) unless args.theme names one per theme.
const remote = wanted.filter((s) => s.source === 'remote' && themesOf(s).length);
if (remote.length) {
  const mockScript = path.join(appRepo, 'src/web/remote/mock/nota-mock.mjs');
  let mock, base = process.env.NOTA_REMOTE_MOCK;
  if (!base) {
    if (!fs.existsSync(mockScript)) throw new Error(`stand-in Nota not found at ${mockScript} (set NOTA_REPO)`);
    const port = 7799;
    mock = spawn(process.execPath, [mockScript], { env: { ...process.env, PORT: String(port) }, stdio: ['ignore', 'pipe', 'inherit'] });
    process.on('exit', () => mock.kill());
    await new Promise((resolve, reject) => {
      mock.stdout.on('data', (d) => String(d).includes('Stand-in Nota') && resolve());
      mock.on('exit', (code) => reject(new Error(`stand-in Nota exited (${code})`)));
    });
    base = `http://127.0.0.1:${port}`;
  }
  const { chromium } = await import('playwright');
  const browser = await chromium.launch();
  for (const s of remote) {
    const a = s.args ?? {};
    for (const theme of themesOf(s)) {
      const ctx = await browser.newContext({
        viewport: { width: a.width ?? 390, height: a.height ?? 844 },
        deviceScaleFactor: 2,
        isMobile: true,
        hasTouch: true,
        colorScheme: a.phone ?? theme,
      });
      const page = await ctx.newPage();
      try {
        const worn = a.theme?.[theme] ?? `studio-${theme}`;
        const q = new URLSearchParams({ theme: worn, ...(a.pick ? { pick: a.pick } : {}) });
        await page.goto(`${base}/?${q}#p=1234`);
        await page.getByRole('navigation', { name: 'Screens' }).waitFor();
        if (a.track) {
          await page.getByLabel('Choose the track to play').click();
          await page.getByRole('dialog', { name: 'Play on track' }).getByRole('button', { name: new RegExp(`^${a.track}\\b`) }).click();
        }
        if (a.screen) await page.getByRole('navigation', { name: 'Screens' }).getByRole('button', { name: a.screen, exact: true }).click();
        if (a.menu) await page.getByRole('button', { name: 'Menu', exact: true }).click();
        if (a.transport) await page.getByRole('button', { name: 'Big transport' }).click();
        await page.waitForTimeout(800);
        const png = path.join(cache, 'remote', theme, s.id + '.png');
        fs.mkdirSync(path.dirname(png), { recursive: true });
        await page.screenshot({ path: png });
        await toWebp(png, theme, s.id);
        console.log(`ok     ${s.id}  (remote · ${theme})`);
      } catch (e) {
        failures.push(`${s.id} (${theme})`);
        console.log(`FAILED ${s.id} (${theme}): ${e.message.split('\n')[0]}`);
      }
      await ctx.close();
    }
  }
  await browser.close();
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
