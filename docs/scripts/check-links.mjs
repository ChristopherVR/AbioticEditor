// Check the rendered site, including sidebar links, fragments, and image paths.
// The WebAssembly app is assembled later by the Pages workflow.
import { readdir, readFile, stat } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../.vitepress/dist/', import.meta.url))
const base = '/AbioticEditor/'
const origin = 'https://docs.invalid'
async function walk(dir) {
  const entries = await readdir(dir, { withFileTypes: true })
  return (await Promise.all(entries.map(entry => entry.isDirectory()
    ? walk(join(dir, entry.name)) : join(dir, entry.name)))).flat()
}
const files = (await walk(root)).filter(file => file.endsWith('.html'))
const pages = new Map(await Promise.all(files.map(async file => [resolve(file), await readFile(file, 'utf8')])))
const failures = new Set()
let checked = 0
for (const [file, html] of pages) {
  const route = base + file.slice(resolve(root).length + 1).replaceAll('\\', '/').replace(/index\.html$/, '').replace(/\.html$/, '')
  for (const match of html.matchAll(/<(?:a|img)\b[^>]*?\b(?:href|src)="([^"]+)"/g)) {
    const href = match[1].replaceAll('&amp;', '&')
    const url = new URL(href, origin + route)
    if (url.origin !== origin || url.pathname === base + 'app/') continue
    if (!url.pathname.startsWith(base)) {
      failures.add(`${route}: path escapes Pages base: ${href}`)
      continue
    }
    const path = join(root, decodeURIComponent(url.pathname.slice(base.length)))
    const candidates = [path, path + '.html', join(path, 'index.html')]
    let target
    for (const candidate of candidates) {
      if (await stat(candidate).then(s => s.isFile()).catch(() => false)) { target = resolve(candidate); break }
    }
    checked++
    if (!target) { failures.add(`${route}: missing file: ${href}`); continue }
    if (url.hash && pages.has(target)) {
      const id = decodeURIComponent(url.hash.slice(1))
      if (!pages.get(target).includes(`id="${id}"`)) failures.add(`${route}: missing anchor: ${href}`)
    }
  }
}
if (failures.size) {
  console.error([...failures].join('\n'))
  process.exitCode = 1
} else {
  console.log(`Checked ${checked} local links and images across ${pages.size} rendered pages.`)
}
