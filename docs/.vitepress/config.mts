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

  // These research notes and the internal session log live in docs/ for the
  // repo, but are not navigation-worthy pages. The session log in particular is
  // huge and internal; keep it out of the published site entirely.
  srcExclude: ['PROGRESS.md', '**/README.md'],

  // The imported research notes cross-link each other and the repo with paths
  // VitePress can't always resolve at build time; don't fail the deploy over them.
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
      { text: 'Plugins', link: '/plugins' },
      { text: 'Save format', link: '/player-save-schema' },
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
        ],
      },
      {
        text: 'Plugins',
        items: [
          { text: 'Plugin system', link: '/plugins' },
          { text: 'Authoring guide', link: '/plugin-authoring' },
          { text: 'Building & installing', link: '/plugin-building' },
          { text: 'Sample catalog', link: '/plugin-samples' },
          { text: 'Plugin fix-ups', link: '/plugin-fixups' },
        ],
      },
      {
        text: 'Save format',
        items: [
          { text: 'Player save schema', link: '/player-save-schema' },
          { text: 'World save schema', link: '/world-save-schema' },
        ],
      },
      {
        text: 'Research notes',
        collapsed: true,
        items: [
          { text: 'Backpack & traits', link: '/research-backpack-traits' },
          { text: 'Customization', link: '/research-customization' },
          { text: 'GatePal & quests', link: '/research-gatepal-quests' },
          { text: 'Narrative NPCs', link: '/research-narrative-npcs' },
          { text: 'New-save gaps', link: '/research-new-save-gaps' },
          { text: 'Performance review', link: '/research-perf-review' },
          { text: 'Respawn terminals', link: '/research-respawn-terminals' },
          { text: 'Server saves', link: '/research-server-saves' },
          { text: 'Slot types', link: '/research-slot-types' },
          { text: 'Transmog & appearance', link: '/research-transmog-appearance' },
          { text: 'Wiki round 10', link: '/research-wiki-round10' },
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
