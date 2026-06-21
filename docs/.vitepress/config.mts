import { defineConfig } from 'vitepress'

// VitePress config for the Abiotic Editor docs site.
// Deployed to GitHub Pages at https://christophervr.github.io/AbioticEditor/ by
// .github/workflows/docs.yml, so `base` must be the repository name.
//
// The docs are organized into two first-class tracks:
//   - /guide/      "Guide": how to USE the app and the CLI (player-facing).
//   - /reference/  "Reference": the technical track (how saves work,
//                  localization, plugin development, maintainer tasks).
// Each track has its own sidebar, keyed by path in `sidebar` below.
export default defineConfig({
  title: 'Abiotic Editor',
  description:
    'A save-game editor for Abiotic Factor: desktop app, scriptable CLI, and a plugin SDK over one shared engine.',
  base: '/AbioticEditor/',
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: true,

  // PROGRESS.md is the internal session log (large, not user-facing). The
  // research notes under reference/research/ are kept (they're technical
  // reference) but every folder's README.md stays out of the published site.
  srcExclude: ['PROGRESS.md', '**/README.md'],

  // Reference docs cross-link each other and the repo with relative paths;
  // don't fail the deploy if a link target ends up relocated or excluded.
  ignoreDeadLinks: true,

  // These docs were authored for GitHub's renderer, where literal angle brackets
  // in prose (e.g. <WorldName>, <steamid>) and type names are plain text. With
  // raw-HTML passthrough off, markdown-it escapes them instead of handing the Vue
  // compiler malformed tags, so the source notes build unchanged.
  markdown: {
    html: false,
  },

  head: [
    ['link', { rel: 'icon', type: 'image/png', href: '/AbioticEditor/logo.png' }],
    ['meta', { name: 'theme-color', content: '#0c1a24' }],
    ['meta', { name: 'og:title', content: 'Abiotic Editor' }],
    [
      'meta',
      {
        name: 'og:description',
        content: 'A save-game editor for Abiotic Factor.',
      },
    ],
  ],

  themeConfig: {
    logo: '/logo.png',

    // Two top-level entries, one per track, plus the download.
    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '/guide/' },
      { text: 'Reference', link: '/reference/save-format', activeMatch: '/reference/' },
      {
        text: 'Download',
        link: 'https://github.com/ChristopherVR/AbioticEditor/releases/latest',
      },
    ],

    sidebar: {
      // Track 1 - Using the editor: task-oriented, player-facing.
      '/guide/': [
        {
          text: 'Use the editor',
          items: [
            { text: 'Getting started', link: '/guide/getting-started' },
            { text: 'Desktop app', link: '/guide/desktop-app' },
            { text: 'Command-line tool', link: '/guide/cli' },
            { text: 'Game Pass saves', link: '/guide/game-pass' },
            { text: 'Plugins & language packs', link: '/guide/plugins' },
            { text: 'Keeping game data current', link: '/guide/game-data' },
          ],
        },
        {
          text: 'Going deeper',
          items: [
            { text: 'Technical reference', link: '/reference/save-format' },
          ],
        },
      ],

      // Track 2 - Under the hood: developer / contributor / maintainer.
      '/reference/': [
        {
          text: 'How saves work',
          items: [
            { text: 'Overview', link: '/reference/save-format' },
            { text: 'Player save schema', link: '/reference/player-save-schema' },
            { text: 'World save schema', link: '/reference/world-save-schema' },
            { text: 'Game Pass format', link: '/reference/game-pass-format' },
          ],
        },
        {
          text: 'Localization',
          items: [
            { text: 'Translating the editor', link: '/reference/localization' },
          ],
        },
        {
          text: 'Plugin development',
          items: [
            { text: 'Plugin system', link: '/reference/plugin-system' },
            { text: 'Authoring guide', link: '/reference/plugin-authoring' },
            { text: 'Building & installing', link: '/reference/plugin-building' },
            { text: 'Sample catalog', link: '/reference/plugin-samples' },
            { text: 'Fix-up cookbook', link: '/reference/plugin-fixups' },
          ],
        },
        {
          text: 'Maintaining the editor',
          items: [
            { text: 'Maintainer commands', link: '/reference/maintainer-commands' },
          ],
        },
        {
          text: 'Research notes',
          collapsed: true,
          items: [
            { text: 'Backpack & traits', link: '/reference/research/research-backpack-traits' },
            { text: 'Customization', link: '/reference/research/research-customization' },
            { text: 'GatePal & quests', link: '/reference/research/research-gatepal-quests' },
            { text: 'Narrative NPCs', link: '/reference/research/research-narrative-npcs' },
            { text: 'New-save gaps', link: '/reference/research/research-new-save-gaps' },
            { text: 'Performance review', link: '/reference/research/research-perf-review' },
            { text: 'Respawn terminals', link: '/reference/research/research-respawn-terminals' },
            { text: 'Server saves', link: '/reference/research/research-server-saves' },
            { text: 'Slot types', link: '/reference/research/research-slot-types' },
            { text: 'Transmog & appearance', link: '/reference/research/research-transmog-appearance' },
            { text: 'Wiki round 10', link: '/reference/research/research-wiki-round10' },
          ],
        },
      ],
    },

    search: { provider: 'local' },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/ChristopherVR/AbioticEditor' },
    ],

    editLink: {
      pattern:
        'https://github.com/ChristopherVR/AbioticEditor/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },

    footer: {
      message:
        'A fan-made tool. Not affiliated with or endorsed by the developers of Abiotic Factor.',
      copyright: 'Abiotic Editor',
    },
  },
})
