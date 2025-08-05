<script setup lang="ts">
import { useAuthStore } from './stores/authStore'
import { useRouter } from 'vue-router'

const navigationLinks = [
  { icon: 'mdi-view-dashboard', text: 'Dashboard', path: '/' },
  { icon: 'mdi-alarm', text: 'Time Tickers', path: '/time-tickers' },
  { icon: 'mdi-calendar-sync', text: 'Cron Tickers', path: '/cron-tickers' },
]

const authStore = useAuthStore()
const router = useRouter()

const navigateToDashboard = () => {
  router.push('/')
}

</script>

<template>
  <span v-if="authStore.isLoggedIn">
    <v-app id="inspire">
      <v-app-bar>
          <template v-slot:prepend>
            <div class="d-flex align-center" style="cursor: pointer" @click="navigateToDashboard">
              <v-icon class="ml-5 mr-4">
                <img
                  src="https://arcenox.com/assets/imgs/main/arcenox-logo.svg"
                  alt="Arcenox"
                  style="height: 40px"
                  class="ml-4"
                />
              </v-icon>
              <v-app-bar-title class="ml-4"><strong>TickerQ</strong></v-app-bar-title>
            </div>
          </template>

        <template v-slot:append>
          <v-btn
            v-for="link in navigationLinks"
            :key="link.path"
            :text="link.text"
            :to="link.path"
            variant="text"
            class="mx-2"
            :prepend-icon="link.icon"
          ></v-btn>
        </template>
      </v-app-bar>
      <v-main>
        <RouterView :key="String(authStore.isLoggedIn)" />
      </v-main>
    </v-app>
    <v-footer class="text-center d-flex flex-column ga-2">
    <v-divider class="my-2" thickness="2" width="50"></v-divider>
    <div>
      2025 — <strong>Arcenox</strong>
    </div>
  </v-footer>
  </span>
  <span v-else>
    <v-sheet class="mx-auto" width="300">
      <v-form fast-fail @submit.prevent="authStore.setToLocalStorage">
        <v-text-field v-model="authStore.credentials.username" label="Username"></v-text-field>

        <v-text-field v-model="authStore.credentials.password" label="Password"></v-text-field>

        <v-btn class="mt-2" type="submit" block>Submit</v-btn>
      </v-form>
      <div v-if="authStore.errorMessage" class="mt-5 text-center" style="color: red">
        Invalid Credentials
      </div>
    </v-sheet>
  </span>
</template>
