// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// GitHub Pages serves the site from /<repo>/; the deploy workflow passes the base path.
const base = process.env.BASE_PATH ?? '/';

/** A sidebar group whose pages come from one directory, labelled in both languages. */
const group = (label, ru, directory, extra = {}) => ({
  label,
  translations: { ru },
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
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/nota-daw/nota' },
        { icon: 'discord', label: 'Discord', href: 'https://discord.gg/apf4Q2JKWk' },
        { icon: 'telegram', label: 'Telegram', href: 'https://t.me/notadaw' },
      ],
      editLink: { baseUrl: 'https://github.com/nota-daw/nota-docs/edit/main/' },
      lastUpdated: true,
      customCss: ['./src/styles/fonts.css', './src/styles/ember.css'],
      components: {},
      sidebar: [
        group('Start here', 'Начало работы', 'start-here'),
        group('Basics', 'Основы', 'basics'),
        group('Views', 'Виды', 'views'),
        group('Editing', 'Редактирование', 'editing'),
        {
          label: 'Devices',
          translations: { ru: 'Устройства' },
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
