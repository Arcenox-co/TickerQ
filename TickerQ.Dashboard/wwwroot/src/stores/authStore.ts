// stores/authStore.ts
import { defineStore } from 'pinia';
import { ref, computed, reactive, watch, type Ref } from 'vue';

export const useAuthStore = defineStore('auth', () => {
  // 1. Reactive base64 token
  const auth = ref(localStorage.getItem('auth') || '');

  const errorMessage = ref(false);
  // 2. Sync to localStorage
  watch(auth, (newVal) => {
    if (newVal) {
      localStorage.setItem('auth', newVal);
    } else {
      localStorage.removeItem('auth');
    }
  });

  // 3. Reactive credentials for the login form
  const credentials = reactive({
    username: '',
    password: ''
  });

  // 4. Computed login state
  const isLoggedIn = computed(() => !!auth.value);

  // 5. Set to localStorage (login)
  const setToLocalStorage = () => {
    const authHeader = btoa(`${credentials.username}:${credentials.password}`);
    auth.value = authHeader;
  };

  // 6. Logout
  const logout = () => {
    auth.value = '';
    credentials.username = '';
    credentials.password = '';
  };

  return {
    auth,
    credentials,
    isLoggedIn,
    setToLocalStorage,
    logout,
    errorMessage
  };
});