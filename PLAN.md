# Nota Docs — план

Полная пользовательская документация Nota: **Astro Starlight**, **EN (основной) + RU**,
картинки — макеты `nota-design` + headless-рендер настоящего приложения, светлая и тёмная
тема. Хостинг — GitHub Pages (`nota-daw/nota-docs`).

Источники правды (в порядке доверия): код `../nota/src` → `../nota/CHANGELOG.md` →
`../nota/FEATURES.md` (отстаёт: описывает 0.42.1) → макеты `../nota-design`.
Документация описывает **то, что есть в коде**, а не то, что нарисовано в макете.

---

## Устройство репозитория (целевое)

```
nota-docs/
  astro.config.mjs            Starlight: locales { root: en, ru }, sidebar, тема Ember
  src/content/docs/           EN-страницы (.mdx)
  src/content/docs/ru/        RU-двойники с теми же путями
  src/components/Shot.astro   <Shot id="devices/volt/signal" /> — dark/light по теме сайта
  src/components/Kbd.astro    клавиши ⌘ ⇧ ⌥ в стиле приложения (импорт: @components/…)
  src/styles/ember.css        токены из ../nota DESIGN.md / NotaPalette
  src/styles/fonts.css        Geist / Geist Mono из ../nota/assets/fonts (с кириллицей)
  templates/                  device.mdx, page.mdx — заготовки новых страниц
  src/assets/shots/{dark,light}/<id>.webp
  shots/manifest.yml          каждый скриншот: id, source (design|app), откуда снимать
  shots/design.mjs            Playwright: nota-design → [data-screen-label] → webp (dpr 2)
  shots/app/                  C#-харнесс (Avalonia.Headless) → сцены приложения
  coverage.yml                фича/область кода → страницы (по нему работает скилл)
  scripts/check.mjs           битые ссылки, нет RU-двойника, нет картинки из manifest,
                              устаревшие страницы (since/updated vs VERSION)
  scripts/shortcuts.mjs       генерирует reference/shortcuts из PreferencesWindow.ShortcutGroups
  .github/workflows/deploy.yml
```

Фронтматтер страницы: `title`, `description`, `since: 0.50` (версия, где появилось),
`updated: 0.50.1` (по какой версии сверено с кодом), `sources:` (пути в `../nota/src`,
по которым страница написана — по ним скилл находит, что надо обновить).

---

## Структура документации (сайдбар)

1. **Start here** — Установка (macOS / Windows / Linux), Первый запуск и Start window,
   Обзор главного окна, Урок «первый трек за 10 минут».
2. **Basics** — Проект и сохранение, Version history, Транспорт (темп, размер, тональность,
   луп, метроном, count-in, follow, quantize), Треки и их типы, Undo, Цвета и имена.
3. **Views** — Arrangement, Session (клипы, сцены, launch modes, follow actions, запись),
   Modular (CV-маршрутизация), Mixer (sends, группы, мастер).
4. **Editing** — Piano roll / Clip editor, Аудио-клипы и warp, Запись аудио и MIDI,
   Автоматизация, Диапазоны (⌘⇧D, ⌘⇧I), Arrangement ⇄ Session.
5. **Devices** — Как устроена цепочка, карточки, пресеты, макросы, окно устройства;
   - Instruments: Synth, Sampler, Grain, Volt, Aurora, Operator, Pendulum, Bass, Physical,
     Flux, Rhythm, Pentad, Consort (+ Monolith, Strata — сверить по коду)
   - Audio effects: EQ-8, EQ-3, Dynamic EQ, Compressor, Reverb, Delay, Utility, Valve,
     Auto Filter, Auto Shift, Vintage, Beat Repeat, Orbit, Crush, Ceiling, Forge, Level,
     Shutter, Chamber, Prism, Lens, Flanger, Phaser, Chorus
   - MIDI effects: Arp, Scale, Length, Velocity, Random, Chord
   - Racks: Instrument Rack, Drum Rack (+ 25 kits), Audio Effect Rack
6. **Plug-ins** — VST3/AU, сканирование, Get Plug-ins, карточка плагина.
7. **Browser & library** — вкладки браузера, Preview Player, smart samples, Sample Packs.
8. **Controllers & Remote** — MIDI-контроллеры, MIDI Learn, геймпады, Nota Remote (QR / USB,
   pads, keys, XY, mixer, macros, session).
9. **AI** — AI Models (разделение на стемы, audio→MIDI), MCP-управление.
10. **Export** — рендер, стемы, форматы.
11. **Preferences** — по странице на каждый раздел настроек.
12. **Reference** — Горячие клавиши (генерируется), Глоссарий, Troubleshooting / FAQ,
    Changelog (ссылка на GitHub Releases).

Шаблон страницы устройства (`templates/device.mdx`): одна фраза «что это и зачем» → главный
скриншот → сигнальный путь (схема/табы карточки) → таблица параметров по панелям
(параметр · диапазон · что делает) → модуляция/автоматизация/CV → пресеты → 2–3 рецепта.

---

## Этапы

Каждый этап заканчивается: `npm run build` + `scripts/check.mjs` зелёные, EN и RU готовы
**вместе** (перевод не откладываем — иначе RU навсегда отстанет), коммит в `nota-docs`.

| # | Этап | Результат |
|---|---|---|
| 0 ✅ | **Инвентаризация** | `coverage.yml`: все фичи из кода/CHANGELOG/FEATURES → страница; список расхождений макетов с кодом; список устройств с путями к DSP-классам и карточкам. |
| 1 ✅ | **Каркас** | Starlight + i18n + Ember-тема (dark/light), компоненты `Shot`/`Kbd`, шаблоны страниц, деплой на Pages, пустой сайдбар со всеми разделами. |
| 2 | **Скриншоты** | `shots/design.mjs` (макеты, обе темы — проверить, есть ли light-вариант у каждого макета), C#-харнесс сцен приложения (главное окно, views, mixer, preferences, браузер), `manifest.yml`, `npm run shots [id…]`. |
| 3 | **Start here + Basics** | Установка, тур по окну, урок, проект/транспорт/треки/history. |
| 4 | **Views + Editing** | Arrangement, Session, Modular, Mixer, клип-редакторы, запись, автоматизация. |
| 5a | **Instruments** | 13–15 страниц по шаблону. Параметры — из DSP-класса и карточки, не из макета. |
| 5b | **Audio effects** | ~24 страницы. |
| 5c | **MIDI effects + Racks** | 9 страниц + обзор цепочки устройств. |
| 6 | **Остальное** | Plug-ins, Browser & library, Remote, AI, Export, Preferences. |
| 7 | **Reference** | Генератор shortcuts, глоссарий, troubleshooting, поиск (Pagefind) проверен на EN/RU. |
| 8 | **Интеграция** | Help ▸ Documentation в приложении, ссылка «?» на карточке устройства → страница устройства, ссылка с nota-site, README. |
| 9 | **Поддержка** | Скилл `nota-docs` в `../nota/.claude/skills` (уже добавлен), шаг в `/release`, CI-проверка в nota-docs. |

Порядок важен: 0→1→2 — фундамент; 3–7 можно перемежать, но каждый этап самодостаточен
и публикуется сразу.

---

## Правила текста

- Голос — как в CHANGELOG: коротко, по-человечески, второе лицо, без маркетинга.
- Названия элементов UI — ровно как на экране (**Launch quantize**, **Copy to Session**),
  жирным; клавиши — через `<Kbd>`; ⌘ на macOS = Ctrl на Windows/Linux (оговорено один раз
  на странице shortcuts и в «Обзоре окна»).
- RU: UI-названия не переводим (в приложении они английские), объясняющий текст — на русском.
- Никаких обещаний будущих функций.

---

## Журнал

- **2026-10-07 · этапы 0–1.** `coverage.yml` — 99 страниц (50 устройств) с путями к коду.
  Каркас: Starlight 0.42 / Astro 7, EN + RU, тема Ember (Graphite/Paper), шрифты Geist,
  `Shot`/`Kbd`, страницы-заглушки (`status: todo`) для всего сайдбара, `scripts/check.mjs`,
  деплой на Pages. Node: `/opt/homebrew/Cellar/node/26.10.0_1/bin` (brew-node не слинкован).
  Репозиторий `nota-daw/nota-docs` приватный — для Pages его нужно сделать публичным.
