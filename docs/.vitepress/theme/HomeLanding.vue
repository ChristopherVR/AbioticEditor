<script setup lang="ts">
import { withBase } from 'vitepress'
const routes = [
  { number: '01', title: 'Edit a save', text: 'Open a folder, make your changes, and save when you are ready.', link: '/guide/getting-started', label: 'Start here' },
  { number: '02', title: 'Connect to your game', text: 'Use the desktop editor with the live agent. Changes affect the running session.', link: '/guide/live-editing', label: 'Live editing guide' },
  { number: '03', title: 'Find an answer', text: 'Game Pass, Steam Deck, item transfers, plugins, and the save format.', link: '/guide/', label: 'Browse all documentation' },
]
</script>

<template>
  <div class="field-manual">
    <section class="manual-hero" aria-labelledby="manual-title">
      <div class="manual-intro">
        <p class="manual-kicker">Abiotic Factor / Field manual</p>
        <h1 id="manual-title">Your world.<br>Your next experiment.</h1>
        <p class="manual-deck">Repair a save. Rebuild your inventory. Pick up where you want to play. Abiotic Editor puts your character and world within reach.</p>
        <div class="manual-actions">
          <a class="manual-primary" :href="withBase('/app/')" target="_self">Open browser editor <span aria-hidden="true">↗</span></a>
          <a href="https://github.com/ChristopherVR/AbioticEditor/releases/latest">Download desktop app <span aria-hidden="true">↓</span></a>
        </div>
        <p class="manual-note">Free, fan-made, and open source. Save files stay on your computer.</p>
      </div>
      <figure class="manual-preview">
        <div class="manual-preview-label"><span>Inside the editor</span><span>Player / Inventory</span></div>
        <img :src="withBase('/screenshots/11-player-inventory.png')" alt="Player inventory in Abiotic Editor, showing equipment slots and the item catalog">
        <figcaption>Work with the game's items, skills, recipes, and world state.</figcaption>
      </figure>
    </section>
    <nav class="manual-routes" aria-label="Choose a documentation path">
      <a v-for="route in routes" :key="route.number" :href="withBase(route.link)">
        <span class="manual-number">{{ route.number }} /</span>
        <h2>{{ route.title }}</h2>
        <p>{{ route.text }}</p>
        <span class="manual-route-link">{{ route.label }} <span aria-hidden="true">→</span></span>
      </a>
    </nav>
  </div>
</template>
