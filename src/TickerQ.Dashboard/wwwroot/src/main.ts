import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import '@mdi/font/css/materialdesignicons.css'
import 'vuetify/styles'
import App from './App.vue'
import router from './router'
import { VDateInput } from 'vuetify/labs/VDateInput'
import { VPullToRefresh } from 'vuetify/labs/VPullToRefresh'
import VueTheMask from 'vue-the-mask';

if (localStorage.getItem('auth') == null) {
  localStorage.setItem('auth', 'ZHVtbXk6ZHVtbXk=');
}
const app = createApp(App)
const vuetify = createVuetify({
  components: {
    ...components,
    VDateInput,
    VPullToRefresh
  },
  directives,
  theme: {
    defaultTheme: 'dark'
  }
})

app.use(VueTheMask as any);
app.use(createPinia())
app.use(router)
app.use(vuetify)

app.mount('#app')
