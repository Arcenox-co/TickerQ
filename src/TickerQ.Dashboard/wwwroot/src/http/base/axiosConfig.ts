import { useAuthStore } from '@/stores/authStore';
import { useAlertStore } from '@/stores/alertStore';
import axios, {AxiosError, type AxiosInstance, type AxiosResponse } from 'axios';
import { getApiBaseUrl, getBackendUrl } from '@/utilities/pathResolver';

const authStore = useAuthStore();
const alertStore = useAlertStore();

const axiosInstance: AxiosInstance = axios.create({
  //baseURL: getApiBaseUrl(),
   baseURL: 'https://localhost:7231/tickerq/dashboard/api',
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

// ✅ Response Interceptor: Handle errors and auth
axiosInstance.interceptors.response.use(
  (response: AxiosResponse) => response,
  (error: AxiosError) => {
    try {
      // Handle 401 authentication errors
      if (error.response?.status === 401) {
        authStore.auth = '';
        authStore.errorMessage = 'Authentication failed. Please log in again.';
      }

      // Show error alert for HTTP errors (except 401 which is handled by auth flow)
      // Also avoid showing alerts for cancelled requests
      if (error.response?.status !== 401 && !error.message?.includes('canceled')) {
        alertStore.showHttpError(error);
      }
    } catch (alertError) {
      // Prevent infinite loops if alert system has issues
      console.error('Error showing alert:', alertError);
    }

    return Promise.reject(error);
  }
);

export default axiosInstance;