import { useAuthStore } from '@/stores/authStore';
import { useAlertStore } from '@/stores/alertStore';
import axios, { AxiosError, type AxiosInstance, type AxiosResponse } from 'axios';
import { getApiBaseUrl, getAuthMode } from '@/utilities/pathResolver';

const axiosInstance: AxiosInstance = axios.create({
  baseURL: getApiBaseUrl(),
});

// Request Interceptor: Set Authorization header
axiosInstance.interceptors.request.use(
  (config: any) => {
    // Get auth headers from localStorage (using correct keys)
    const apiKey = localStorage.getItem('tickerq_api_key');
    const basicAuth = localStorage.getItem('tickerq_basic_auth');
    const hostAccessKey = localStorage.getItem('tickerq_host_access_key');
    
    if (apiKey) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', `Bearer ${apiKey}`);
      } else {
        config.headers['Authorization'] = `Bearer ${apiKey}`;
      }
    } else if (basicAuth) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', `Basic ${basicAuth}`);
      } else {
        config.headers['Authorization'] = `Basic ${basicAuth}`;
      }
    } else if (hostAccessKey) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', hostAccessKey);
      } else {
        config.headers['Authorization'] = hostAccessKey;
      }
    }

    return config;
  },
  (error: AxiosError) => {
    return Promise.reject(error);
  }
);

// Response Interceptor: Handle errors and auth
axiosInstance.interceptors.response.use(
  (response: AxiosResponse) => response,
  async (error: AxiosError) => {
    try {
      // Handle 401 authentication errors
      if (error.response?.status === 401) {
        console.log('401 Unauthorized - handling authentication failure');
        
        // Get auth store and handle 401
        const authStore = useAuthStore();
        await authStore.handle401Error();
        
        // Don't show alert for 401 errors - let the auth system handle it
        return Promise.reject(error);
      }

      // Show error alert for other HTTP errors
      if (!error.message?.includes('canceled')) {
        const alertStore = useAlertStore();
        alertStore.showHttpError(error);
      }
    } catch (alertError) {
      // Prevent infinite loops if alert system has issues
      console.error('Error in response interceptor:', alertError);
    }

    return Promise.reject(error);
  }
);

export default axiosInstance;