import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const router = createRouter({
  history: createWebHistory(import.meta.env.PROD ? "" : "/tickerq-dashboard"),
  routes: [
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
      // route level code-splitting
      // this generates a separate chunk (About.[hash].js) for this route
      // which is lazy-loaded when the route is visited.
      component: () => import('../views/TimeTicker.vue'),
      meta: { requiresAuth: true }
    },
  ],
})

// Navigation guard to check authentication
router.beforeEach((to, from, next) => {
  const authStore = useAuthStore()
  
  // If route requires auth and user is not logged in, redirect to root
  if (to.meta.requiresAuth && !authStore.isLoggedIn) {
    // Only redirect if auth has been initialized (prevents flash)
    if (authStore.isInitialized) {
      next('/')
    } else {
      // Wait for auth to initialize
      next()
    }
  } else {
    next()
  }
})

export default router
