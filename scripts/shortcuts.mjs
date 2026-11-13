#!/usr/bin/env node
// Generates reference/shortcuts (EN + RU, and FR when nota-translations is checked out) from the
// app's own Settings ▸ Shortcuts page: the key-action catalog and its layout (specs/096), dumped
// by the app's smoke test (`--shortcuts-dump`). The keys are the shipped defaults; everyone can
// change them. Run `npm run shortcuts` after the nota-shortcuts skill has brought the app up to
// date. Never edit the generated pages by hand.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const appRepo = path.resolve(process.env.NOTA_REPO ?? path.join(root, '../nota'));
const srcPath = 'src/managed/Nota.Application/Input/ShortcutLayout.cs';
const version = fs.readFileSync(path.join(appRepo, 'VERSION'), 'utf8').trim();

// The smoke test writes the page as JSON; built once (Release), then run per language.
const dll = path.join(appRepo, 'tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll');
execFileSync('dotnet', ['build', path.join(appRepo, 'tests/Nota.SmokeTest'), '-c', 'Release', '--nologo', '-v', 'q'], { stdio: 'inherit' });
const dump = (languageFile) => {
  const out = path.join(os.tmpdir(), `nota-shortcuts-${process.pid}-${languageFile ? path.basename(languageFile) : 'en'}`);
  execFileSync('dotnet', [dll, '--shortcuts-dump', out, ...(languageFile ? [languageFile] : [])], { stdio: 'ignore' });
  const groups = JSON.parse(fs.readFileSync(out, 'utf8')).groups;
  fs.rmSync(out, { force: true });
  // Commands with no key by default are left out: the page lists keys, and any command can get
  // one in Settings ▸ Shortcuts.
  return groups
    .map((g) => ({ title: g.title, rows: g.rows.filter((r) => r.keys).map((r) => [r.keys, r.text]) }))
    .filter((g) => g.rows.length);
};
const groups = dump(null);
if (!groups.length) throw new Error('no shortcut groups in the dump');

// French comes from nota-translations (a sibling checkout, or $NOTA_TRANSLATIONS = a languages/fr.json).
const frPath = process.env.NOTA_TRANSLATIONS ?? path.join(root, '../nota-translations/languages/fr.json');
const groupsFr = fs.existsSync(frPath) ? dump(frPath) : null;
if (!groupsFr) console.warn(`no ${frPath}: reference/shortcuts is generated in English and Russian only`);

// A key has no lower-case letters, or is a named key; anything else ("Drag note edge") is a gesture.
const named = new Set(['Space', 'Return', 'Delete', 'Tab', 'Esc', 'PgUp', 'PgDn', 'Home', 'End']);
const seps = new Set(['/', '·', '+']);
const isKey = (t) => named.has(t.replace(/^[⌘⇧⌥⌃]+/, '')) || seps.has(t) || !/[a-z]/.test(t);
const splitMods = (t) => {
  const out = [];
  let rest = t;
  while (rest.length > 1 && /^[⌘⇧⌥⌃]/.test(rest)) { out.push(rest[0]); rest = rest.slice(1); }
  out.push(rest);
  return out;
};
const esc = (s) => s.replace(/[{}<>]/g, (c) => ({ '{': '&#123;', '}': '&#125;', '<': '&lt;', '>': '&gt;' })[c]).replace(/\|/g, '\\|');
const renderKey = (key) =>
  key
    .split(/\s{2,}/)
    .map((alt) => {
      const tokens = alt.trim().split(/\s+/);
      if (!tokens.every(isKey)) return esc(alt.trim());
      return `<Kbd keys="${tokens.flatMap((t) => (seps.has(t) || named.has(t) ? [t] : splitMods(t))).join(' ')}" />`;
    })
    .join(' · ');

const sentence = (t) => t.charAt(0) + t.slice(1).toLowerCase().replace(/\bmidi\b/g, 'MIDI').replace(/\bmacos\b/g, 'macOS');
const ru = {
  FILE: 'Файл', EDIT: 'Правка', VIEW: 'Вид', TRANSPORT: 'Транспорт', TRACK: 'Трек', ARRANGEMENT: 'Arrangement',
  GRID: 'Сетка', SESSION: 'Session', 'PIANO ROLL': 'Piano roll', DEVICES: 'Устройства', MODULAR: 'Modular',
  'BROWSER FILES': 'Браузер · файлы', 'PLAY NOTES (COMPUTER KEYBOARD)': 'Игра нотами (клавиатура компьютера)',
  SETTINGS: 'Настройки', HELP: 'Справка', 'COMMAND PALETTE': 'Палитра команд', RACKS: 'Рэки',
  'AUDIO CLIP EDITOR': 'Редактор аудиоклипа', 'BROWSER · PREVIEW': 'Браузер · предпрослушивание',
  'GAMEPAD (MACOS)': 'Геймпад (macOS)', MOUSE: 'Мышь',
};

const page = (lang) => {
  const en = lang === 'en';
  const fr = lang === 'fr';
  const list = fr ? groupsFr : groups;
  const fm = [
    '---',
    `title: "${en ? 'Keyboard shortcuts' : fr ? 'Raccourcis clavier' : 'Горячие клавиши'}"`,
    `description: "${en ? 'Every keyboard shortcut and mouse gesture in Nota, grouped as on the Settings ▸ Shortcuts page.' : fr ? 'Tous les raccourcis clavier et gestes de souris de Nota, par groupes, comme dans la page Réglages ▸ Raccourcis.' : 'Все сочетания клавиш и жесты мыши в Nota, по группам, как на странице Settings ▸ Shortcuts.'}"`,
    'sidebar:\n  order: 1',
    `updated: "${version}"`,
    'status: done',
    `sources:\n  - ${srcPath}`,
    '---',
  ].join('\n');
  const intro = fr
    ? `Cette page est générée à partir de **Réglages ▸ Raccourcis**, les deux concordent donc toujours. Elle donne\nles touches **par défaut** : vous pouvez changer chacune d'elles, et en donner une à toute commande qui n'en a pas,\ndans [Réglages ▸ Raccourcis](../../preferences/shortcuts/). Les raccourcis sont écrits à la manière de macOS :\nsous Windows et Linux, lisez <Kbd keys="⌘" /> comme **Ctrl** et <Kbd keys="⌥" /> comme **Alt**.`
    : en
    ? `This page is generated from **Settings ▸ Shortcuts**, so the two always match. It lists the\n**default** keys: you can change any of them, and give a key to any command that has none, in\n[Settings ▸ Shortcuts](../../preferences/shortcuts/). Shortcuts are written the macOS way: on\nWindows and Linux read <Kbd keys="⌘" /> as **Ctrl** and <Kbd keys="⌥" /> as **Alt**.`
    : `Страница собрана из **Settings ▸ Shortcuts**, поэтому они всегда совпадают. Здесь клавиши\n**по умолчанию**: любую можно сменить, а команде без клавиши — назначить её, в\n[Settings ▸ Shortcuts](../../preferences/shortcuts/). Описания действий — на английском, как в\nприложении. Сочетания записаны как в macOS: в Windows и Linux <Kbd keys="⌘" /> — это **Ctrl**,\nа <Kbd keys="⌥" /> — **Alt**.`;
  const body = list
    .map((g) => {
      const head = en || fr ? sentence(g.title) : ru[g.title.toUpperCase()] ?? sentence(g.title);
      const th = en ? '| Keys | Action |' : fr ? '| Touches | Action |' : '| Клавиши | Действие |';
      return `## ${head}\n\n${th}\n|---|---|\n` + g.rows.map(([k, a]) => `| ${renderKey(k)} | ${esc(a)} |`).join('\n');
    })
    .join('\n\n');
  return `${fm}\n\n{/* Generated by scripts/shortcuts.mjs from ${srcPath} — do not edit by hand. */}\n\nimport Kbd from '@components/Kbd.astro';\n\n${intro}\n\n${body}\n`;
};

// French typography: a narrow no-break space before : ; ? ! and inside « » (the app's own texts
// use a plain space; the manual follows the French convention).
const frenchTypography = (text) =>
  text
    .replace(/ ([:;?!])(?=\s|$|[)\]*_<])/g, '\u202f$1')
    .replace(/«\s/g, '«\u202f')
    .replace(/\s»/g, '\u202f»');

for (const [lang, dir] of [['en', ''], ['ru', 'ru/'], ...(groupsFr ? [['fr', 'fr/']] : [])]) {
  const file = path.join(root, 'src/content/docs', dir, 'reference/shortcuts.mdx');
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, lang === 'fr' ? frenchTypography(page(lang)) : page(lang));
}
console.log(`${groups.length} groups · ${groups.reduce((n, g) => n + g.rows.length, 0)} shortcuts → reference/shortcuts (${groupsFr ? 'en, ru, fr' : 'en, ru'})`);
