<script setup lang="ts">
import { withBase } from 'vitepress'
const routes = [
  { number: '01', title: 'My first save edit', text: 'Find your world, change one thing, and get back into the Facility.', link: '/guide/getting-started', label: 'Follow the walkthrough' },
  { number: '02', title: 'Move my supplies', text: 'Bring items from one save to another with the desktop app.', link: '/guide/transfer-items', label: 'Pack for the next world' },
  { number: '03', title: 'I need a hand', text: 'Game Pass, Steam Deck, missing items, and answers in plain language.', link: '/guide/', label: 'Find your player guide' },
]
</script>

<template>
  <div class="field-manual">
    <div class="manual-status"><span>Facility survival desk</span><span>Unofficial player handbook / AE-01</span></div>
    <section class="manual-hero" aria-labelledby="manual-title">
      <div class="manual-intro">
        <p class="manual-kicker">Abiotic Factor / Save editor</p>
        <h1 id="manual-title">Science went wrong.<br><em>Take your save back.</em></h1>
        <p class="manual-deck">Restock your backpack. Adjust your skills. Give your world a second chance. Abiotic Editor helps you change your saved game, with step-by-step help and no coding needed.</p>
        <div class="manual-actions">
          <a class="manual-primary" :href="withBase('/app/')" target="_self">Open browser editor <span aria-hidden="true">↗</span></a>
          <a href="https://github.com/ChristopherVR/AbioticEditor/releases/latest">Download desktop app <span aria-hidden="true">↓</span></a>
        </div>
        <p class="manual-note">Free and fan-made. Your save files stay on your computer.</p>
      </div>
      <figure class="manual-preview">
        <div class="manual-preview-label"><span>Equipment check</span><span>01 / Your backpack</span></div>
        <img :src="withBase('/screenshots/11-player-inventory.png')" alt="Player inventory in Abiotic Editor, showing equipment slots and the item catalog">
        <figcaption>Your gear, at a glance. Pick an item, make a change, then press <strong>SAVE</strong>.</figcaption>
      </figure>
    </section>
    <aside class="manual-checkpoint" aria-label="Before editing"><strong>Before your next experiment</strong><span>Close the game. Keep a copy of your world. Save your changes before heading back in.</span><a :href="withBase('/guide/getting-started')">Show me how <span aria-hidden="true">→</span></a></aside>
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
