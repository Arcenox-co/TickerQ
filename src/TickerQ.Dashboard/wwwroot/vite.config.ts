import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import { dynamicBase } from 'vite-plugin-dynamic-base'

export default defineConfig(({ mode }) => {
  return {
    plugins: [
      vue(),
      vueDevTools({ launchEditor: undefined }),
      dynamicBase({
        // keep a single runtime variable; we'll set it in index.html
        publicPath: 'window.__dynamic_base__',
        transformIndexHtml: true,
      }),
    ],

    // Use dynamic base only in production, normal base in development
    base: mode === 'production' ? '/__dynamic_base__/' : '/',

    build: {
      outDir: 'dist',
      assetsDir: 'assets',
    },

    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    }
  }
})