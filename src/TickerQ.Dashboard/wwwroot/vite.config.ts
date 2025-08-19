import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  
  return {
    plugins: [
      vue(),
      vueDevTools({
        launchEditor: undefined
      }),
    ],
    build: {
      outDir: 'dist', // Ensure assets are placed in 'dist'
      assetsDir: 'tickerqassets', // Rename 'assets' to 'tickerqassets'
    },
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url))
      },
    },
    define: {
      __TICKERQ_CONFIG__: JSON.stringify({
        basePath: env.VITE_TICKERQ_BASE_PATH || '/tickerq-dashboard',
        backendDomain: env.VITE_TICKERQ_BACKEND_DOMAIN
      })
    }
  }
})
