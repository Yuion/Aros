import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import { resolve } from 'path'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'Aros',
        short_name: 'Aros',
        theme_color: '#0066cc',
        background_color: '#f5f5f5',
        display: 'standalone',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
        ],
      },
      workbox: {
        // The app shell only. API responses are deliberately NOT cached: they were, for a day
        // at a time, back when an offline mode was planned. That plan went with the Pi, and
        // again when Aros became a single closed machine that is never offline — but the cache
        // stayed, and quietly served state up to 24 hours old. It cost an evening wondering why
        // a freshly set API key still read as missing.
        globPatterns: ['**/*.{js,css,html,ico,png,svg}'],
      },
    }),
  ],
  resolve: {
    alias: { '@': resolve(__dirname, 'src') },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  base: './',
})
