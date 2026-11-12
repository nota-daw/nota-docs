// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// GitHub Pages serves the site from /<repo>/; the deploy workflow passes the base path.
const base = process.env.BASE_PATH ?? '/';

/** French sidebar labels, by the English label. The names follow the French app (Réglages, Appareils…). */
const fr = {
  'Start here': 'Pour commencer',
  Basics: 'Bases',
  Views: 'Vues',
  Editing: 'Édition',
  Devices: 'Appareils',
  Instruments: 'Instruments',
  'Audio effects': 'Effets audio',
  'MIDI effects': 'Effets MIDI',
  Racks: 'Racks',
  'Plug-ins': 'Plug-ins',
  'Browser & library': 'Navigateur et bibliothèque',
  'Controllers & Remote': 'Contrôleurs et Remote',
  AI: 'IA',
  Export: 'Export',
  Settings: 'Réglages',
  Reference: 'Référence',
};

/** A sidebar group whose pages come from one directory, labelled in every language. */
const group = (label, ru, directory, extra = {}) => ({
  label,
  translations: { ru, fr: fr[label] },
  items: [{ autogenerate: { directory } }],
  ...extra,
});

export default defineConfig({
  site: 'https://nota-daw.github.io',
  base,
  trailingSlash: 'ignore',
  integrations: [
    starlight({
      title: 'Nota Docs',
      description: 'The Nota user manual — a free, open-source DAW for macOS, Windows and Linux.',
      logo: {
        dark: './src/assets/logo-dark.svg',
        light: './src/assets/logo-light.svg',
        alt: 'Nota',
      },
      favicon: '/favicon.svg',
      defaultLocale: 'root',
      locales: {
        root: { label: 'English', lang: 'en' },
        ru: { label: 'Русский', lang: 'ru' },
        fr: { label: 'Français', lang: 'fr' },
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/nota-daw/nota' },
        { icon: 'discord', label: 'Discord', href: 'https://discord.gg/apf4Q2JKWk' },
        { icon: 'telegram', label: 'Telegram', href: 'https://t.me/notadaw' },
      ],
      editLink: { baseUrl: 'https://github.com/nota-daw/nota-docs/edit/main/' },
      lastUpdated: true,
      customCss: ['./src/styles/fonts.css', './src/styles/studio.css'],
      components: { ThemeSelect: './src/components/ThemeToggle.astro' },
      // Browser chrome in the header's tone; ThemeToggle switches it with the site theme.
      head: [{ tag: 'meta', attrs: { name: 'theme-color', content: '#2b2d31' } }],
      sidebar: [
        group('Start here', 'Начало работы', 'start-here'),
        group('Basics', 'Основы', 'basics'),
        group('Views', 'Виды', 'views'),
        group('Editing', 'Редактирование', 'editing'),
        {
          label: 'Devices',
          translations: { ru: 'Устройства', fr: fr.Devices },
          items: [
            { slug: 'devices' },
            group('Instruments', 'Инструменты', 'devices/instruments', { collapsed: true }),
            group('Audio effects', 'Аудиоэффекты', 'devices/audio-effects', { collapsed: true }),
            group('MIDI effects', 'MIDI-эффекты', 'devices/midi-effects', { collapsed: true }),
            group('Racks', 'Рэки', 'devices/racks', { collapsed: true }),
          ],
        },
        group('Plug-ins', 'Плагины', 'plugins'),
        group('Browser & library', 'Браузер и библиотека', 'library'),
        group('Controllers & Remote', 'Контроллеры и Remote', 'control'),
        group('AI', 'ИИ', 'ai'),
        group('Export', 'Экспорт', 'export'),
        group('Settings', 'Настройки', 'preferences', { collapsed: true }),
        group('Reference', 'Справочник', 'reference'),
      ],
    }),
  ],
});
