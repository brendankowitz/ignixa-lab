import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Served from https://brendankowitz.github.io/ignixa-lab/ (a GitHub Pages
  // project page, not a custom domain), so every built asset URL needs this
  // prefix. Harmless for local dev, which always runs from `/`.
  base: '/ignixa-lab/',
  server: {
    // Proxy API calls to the local Azure Functions host during development so
    // the SPA can use same-origin relative `/api/*` paths without CORS setup.
    proxy: {
      '/api': {
        target: 'http://localhost:7071',
        changeOrigin: true,
      },
    },
  },
  build: {
    rollupOptions: {
      // Multi-page app: a static landing page (index.html, the site root) plus
      // two separate React roots — Conformance Testing (conformance.html) and
      // Ignixa Lab / Expression Benches (lab.html) — sharing the same theme
      // tokens, not one router-based SPA. See
      // docs/superpowers/specs/2026-07-02-expression-benches-design.md.
      input: {
        landing: resolve(__dirname, 'index.html'),
        conformance: resolve(__dirname, 'conformance.html'),
        benches: resolve(__dirname, 'lab.html'),
      },
    },
  },
})
