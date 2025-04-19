import { useAuthStore } from '@/stores/authStore';
import axios, {AxiosError, type AxiosInstance, type AxiosResponse } from 'axios';

const baseTag = document.querySelector<HTMLBaseElement>('base');
const authStore = useAuthStore();

const axiosInstance: AxiosInstance = axios.create({
  baseURL: import.meta.env.PROD
    ? `${baseTag?.href}/api`
    : 'http://localhost:5079/tickerq-dashboard/api',
});

// ✅ Request Interceptor: Set Authorization header from localStorage
axiosInstance.interceptors.request.use(
  (config: any) => {
    const auth = authStore.auth;

    if (auth) {
      if (!config.headers) {
        config.headers = {};
      }

      // Handle modern AxiosHeaders or plain object
      if (typeof config.headers.set === 'function') {
        (config.headers as any).set('Authorization', `Basic ${auth}`);
      } else {
        config.headers['Authorization'] = `Basic ${auth}`;
      }
    }

    return config;
  },
  (error: AxiosError) => {
    return Promise.reject(error);
  }
);

// ✅ Response Interceptor: Remove auth on 401
axiosInstance.interceptors.response.use(
  (response: AxiosResponse) => response,
  (error: AxiosError) => {
    if (error.response?.status === 401) {
      authStore.auth = '';
      authStore.errorMessage = true;
    }

    return Promise.reject(error);
  }
);

export default axiosInstance;