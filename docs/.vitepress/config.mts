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
    'Your Abiotic Factor player handbook. Edit your character, restock your backpack, and change your world with step-by-step help.',
  base: '/AbioticEditor/',
  lang: 'en-US',
  cleanUrls: true,
  appearance: 'dark',
  lastUpdated: true,

  // PROGRESS.md is the internal session log (large, not user-facing). The
  // research notes under reference/research/ are kept (they're technical
  // reference) but every folder's README.md stays out of the published site.
  srcExclude: ['PROGRESS.md', '**/README.md'],

  // The browser editor is assembled separately by the Pages workflow.
  ignoreDeadLinks: [/^\/app\/(?:index)?$/],

  // These docs were authored for GitHub's renderer, where literal angle brackets
  // in prose (e.g. <WorldName>, <steamid>) and type names are plain text. With
  // raw-HTML passthrough off, markdown-it escapes them instead of handing the Vue
  // compiler malformed tags, so the source notes build unchanged.
  markdown: {
    html: false,
    config(md) {
      const renderLink = md.renderer.rules.link_open
      md.renderer.rules.link_open = (tokens, index, options, env, self) => {
        const href = tokens[index].attrGet('href')
        if (href === '/app/' || href === '/AbioticEditor/app/') {
          tokens[index].attrSet('href', '/AbioticEditor/app/')
          tokens[index].attrSet('target', '_self')
        }
        return renderLink ? renderLink(tokens, index, options, env, self)
          : self.renderToken(tokens, index, options)
      }
    },
  },

  // The Markdown renderer above keeps /app/ links as full navigations in both
  // server-rendered HTML and client-side page transitions.

  head: [
    ['link', { rel: 'icon', type: 'image/png', href: '/AbioticEditor/logo.png' }],
    ['meta', { name: 'theme-color', content: '#0c2023' }],
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
      { text: 'Player handbook', link: '/guide/', activeMatch: '/guide/' },
      { text: 'For creators', link: '/reference/', activeMatch: '/reference/' },
      {
        text: 'Download',
        link: 'https://github.com/ChristopherVR/AbioticEditor/releases/latest',
      },
      // The full editor running client-side, deployed to /app/ by this same workflow.
      // Saves never leave the player's machine. What it cannot do (compare, new world,
      // settings files, achievements) is gated in the app itself and listed in the guide.
      { text: 'Open in browser', link: '/app/', target: '_self' },
    ],

    sidebar: {
      // Track 1 - Using the editor: task-oriented, player-facing.
      '/guide/': [
        {
          text: 'Your field manual',
          items: [
            { text: 'What it can do', link: '/guide/' },
            { text: 'Getting started', link: '/guide/getting-started' },
            { text: 'Desktop app tour', link: '/guide/desktop-app' },
            { text: 'Bases & the 3D view', link: '/guide/3d-view' },
            { text: 'More world tools', link: '/guide/review-features' },
            { text: 'Screenshot tour', link: '/guide/screenshots' },
            { text: 'Edit in your browser', link: '/guide/browser-editor' },
            { text: 'Linux & Steam Deck', link: '/guide/linux-local-host' },
            { text: 'Transfer items', link: '/guide/transfer-items' },
            { text: 'Steam & achievements', link: '/guide/steam-achievements' },
            { text: 'Game Pass saves', link: '/guide/game-pass' },
            { text: 'Plugins & language packs', link: '/guide/plugins' },
            { text: 'Game data & 3D models', link: '/guide/game-data' },
            { text: 'Live editing', link: '/guide/live-editing' },
          ],
        },
        {
          text: 'For experienced users',
          items: [
            { text: 'Command-line tool', link: '/guide/cli' },
            { text: 'Technical reference', link: '/reference/' },
          ],
        },
      ],

      // Track 2 - Under the hood: developer / contributor / maintainer.
      '/reference/': [
        {
          text: 'How saves work',
          items: [
            { text: 'Reference directory', link: '/reference/' },
            { text: 'Architecture & contributing', link: '/reference/architecture' },
            { text: 'Save format', link: '/reference/save-format' },
            { text: 'Player save schema', link: '/reference/player-save-schema' },
            { text: 'World save schema', link: '/reference/world-save-schema' },
            { text: 'Game Pass format', link: '/reference/game-pass-format' },
            { text: 'Game Pass extraction inventory', link: '/reference/game-pass-extraction-inventory' },
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
          text: 'Live editing',
          items: [
            { text: 'Wire protocol', link: '/reference/live-editing-protocol' },
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
            { text: 'Item visual variants', link: '/reference/research/research-item-visual-variants' },
            { text: 'Transmog & appearance', link: '/reference/research/research-transmog-appearance' },
            { text: 'Game Pass conversion', link: '/reference/research/research-gamepass-to-steam' },
            { text: 'Historical Razor parity audit', link: '/architecture/razor-parity-audit' },
            { text: 'Wiki round 10', link: '/reference/research/research-wiki-round10' },
            { text: 'Character slot flags & effects', link: '/reference/research/research-player-slot-flags-and-effects' },
            { text: 'Account saves', link: '/reference/research/research-account-saves' },
            { text: 'World & placed-object state', link: '/reference/research/world-and-placed-object-state' },
            { text: 'Garden planting & pet feeding', link: '/reference/research/research-garden-planting-and-pet-feeding' },
            { text: 'Base building: object census', link: '/reference/research/base-building-placed-object-census' },
            { text: 'Base building: coordinates', link: '/reference/research/base-building-coordinate-spaces' },
            { text: 'Base building: group operations', link: '/reference/research/base-building-group-operations' },
            { text: 'Power network links', link: '/reference/research/research-power-network-links' },
            { text: 'Floor-plan extraction plan', link: '/reference/research/floor-plan-extraction-plan' },
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
