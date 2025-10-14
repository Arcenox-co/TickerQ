import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { getBasePath, requiresAuthentication } from '@/utilities/pathResolver'

const router = createRouter({
  history: createWebHistory(getBasePath()),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/Login.vue'),
      meta: { requiresAuth: false, hideForAuthenticated: true }
    },
    {
      path: '/',
      name: 'Dashboard',
      component: () => import("../views/Dashboard.vue"),
      meta: { requiresAuth: true }
    },
    {
      path: '/cron-tickers',
      name: 'CronTicker',
      component: () => import('../views/CronTicker.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/time-tickers',
      name: 'TimeTicker',
      component: () => import('../views/TimeTicker.vue'),
      meta: { requiresAuth: true }
    },
  ],
})

// Navigation guard for authentication
router.beforeEach(async (to, from, next) => {
  // Always allow access to login page
  if (to.name === 'Login') {
    next();
    return;
  }
  
  // Check if authentication is required globally
  const authRequired = requiresAuthentication();
  
  // If no authentication is required globally, allow all routes
  if (!authRequired) {
    next();
    return;
  }
  
  // Authentication is required - get auth store
  const authStore = useAuthStore();
  
  // Initialize auth if not already done
  if (!authStore.isInitialized) {
    try {
      await authStore.initializeAuth();
    } catch (error) {
      console.error('Auth initialization failed:', error);
      next({ name: 'Login' });
      return;
    }
  }
  
  // Check authentication status
  const isAuthenticated = authStore.isLoggedIn;
  
  if (isAuthenticated) {
    next();
  } else {
    next({ name: 'Login', query: { redirect: to.fullPath } });
  }
})

export default router
