/** @type {import('tailwindcss').Config} */
export default {
 // Use your actual paths to Vue components here
  // so Tailwind can purge unused styles in production
  prefix: "tw-",
  content: [
    "./index.html",
    "./src/**/*.{vue,js,ts,jsx,tsx}",
  ],

  // Turn off preflight to avoid messing with Vuetify's global resets.
  corePlugins: {
    preflight: false,
  },

  theme: {
    extend: {},
  },
  plugins: [],
}

