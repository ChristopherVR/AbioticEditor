// Field-manual theme with the standard VitePress navigation and search.
import DefaultTheme from 'vitepress/theme'
import { useRoute } from 'vitepress'
import { h, onMounted, watch, nextTick } from 'vue'
import mediumZoom from 'medium-zoom'
import './style.css'
import HomeLanding from './HomeLanding.vue'

export default {
  extends: DefaultTheme,
  Layout: () => h(DefaultTheme.Layout, null, { 'home-hero-before': () => h(HomeLanding) }),
  setup() {
    const route = useRoute()

    // Click any content image to open it enlarged in a lightbox overlay.
    // medium-zoom is re-applied after each client-side navigation because the
    // SPA swaps page content without a full reload. Scoping the selector to
    // `.vp-doc img` keeps the logo, nav icons, and other chrome out of it.
    const applyZoom = () =>
      mediumZoom('.vp-doc img', {
        background: 'var(--vp-c-bg)',
        margin: 24,
      })

    onMounted(() => applyZoom())
    watch(
      () => route.path,
      () => nextTick(() => applyZoom()),
    )
  },
}
