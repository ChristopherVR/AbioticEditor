import { defineConfig } from 'vitepress'

// VitePress config for the Abiotic Editor docs site.
// Deployed to GitHub Pages at https://christophervr.github.io/AbioticEditor/ by
// .github/workflows/docs.yml, so `base` must be the repository name.
export default defineConfig({
  title: 'Abiotic Editor',
  description:
    'A save-game editor for Abiotic Factor: desktop app, scriptable CLI, and a plugin SDK over one shared engine.',
  base: '/AbioticEditor/',
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: true,

  // PROGRESS.md is the internal session log (large, not user-facing).
  // The research notes under reference/research/ are developer research dumps,
  // not documentation intended for end users.
  srcExclude: ['PROGRESS.md', '**/README.md', 'reference/research/**'],

  // Reference docs cross-link each other with relative paths; don't fail the
  // deploy if a link target ends up excluded or relocated.
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

    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Plugins', link: '/reference/plugin-system' },
      { text: 'Localization', link: '/reference/localization' },
      { text: 'Save format', link: '/reference/player-save-schema' },
      {
        text: 'Download',
        link: 'https://github.com/ChristopherVR/AbioticEditor/releases/latest',
      },
    ],

    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Getting started', link: '/guide/getting-started' },
          { text: 'Desktop app', link: '/guide/desktop-app' },
          { text: 'Command-line tool', link: '/guide/cli' },
          { text: 'Game Pass saves', link: '/guide/game-pass' },
          { text: 'Keeping game data current', link: '/guide/game-data' },
          { text: 'Plugins & language packs', link: '/guide/plugins' },
        ],
      },
      {
        text: 'Plugins',
        items: [
          { text: 'Plugin system', link: '/reference/plugin-system' },
          { text: 'Authoring guide', link: '/reference/plugin-authoring' },
          { text: 'Building & installing', link: '/reference/plugin-building' },
          { text: 'Sample catalog', link: '/reference/plugin-samples' },
          { text: 'Plugin fix-ups', link: '/reference/plugin-fixups' },
        ],
      },
      {
        text: 'Localization',
        items: [
          { text: 'Translating the editor', link: '/reference/localization' },
        ],
      },
      {
        text: 'Save format',
        items: [
          { text: 'Player save schema', link: '/reference/player-save-schema' },
          { text: 'World save schema', link: '/reference/world-save-schema' },
        ],
      },
      {
        text: 'Technical reference',
        collapsed: true,
        items: [
          { text: 'Game Pass format', link: '/reference/game-pass-format' },
          { text: 'Maintainer commands', link: '/reference/maintainer-commands' },
        ],
      },
    ],

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
