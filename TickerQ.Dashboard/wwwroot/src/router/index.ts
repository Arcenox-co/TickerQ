import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.PROD ? "" : "/tickerq-dashboard"),
  routes: [
    {
      path: '/',
      name: 'Dashboard',
      component: () => import("../views/Dashboard.vue")
    },
    {
      path: '/cron-tickers',
      name: 'CronTicker',
      component: () => import('../views/CronTicker.vue'),
    },
    {
      path: '/time-tickers',
      name: 'TimeTicker',
      // route level code-splitting
      // this generates a separate chunk (About.[hash].js) for this route
      // which is lazy-loaded when the route is visited.
      component: () => import('../views/TimeTicker.vue'),
    },
  ],
})

export default router
