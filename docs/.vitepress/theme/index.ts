// Custom VitePress theme: the default theme re-skinned in the Abiotic Factor
// "facility" palette (see style.css). Both light and dark map to the game's
// Cascade Light / Cascade Dark palettes lifted from the app's ThemeService.
import DefaultTheme from 'vitepress/theme'
import { useRoute } from 'vitepress'
import { onMounted, watch, nextTick } from 'vue'
import mediumZoom from 'medium-zoom'
import './style.css'

export default {
  extends: DefaultTheme,
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
