import { useAuthStore } from '@/stores/authStore';
import { resolveApiUrl, resolvePath } from '@/utilities/pathResolver';

export interface AuthConfig {
  enableBasicAuth: boolean;
  useHostAuthentication: boolean;
  enableBuiltInAuth: boolean;
}

export class AuthService {
  /**
   * Get the auth store lazily to avoid Pinia initialization issues
   */
  private get authStore() {
    return useAuthStore();
  }

  /**
   * Check if basic auth is enabled and redirect to login if needed
   */
  public checkAuthAndRedirect(): void {
    const config = window.TickerQConfig;
    
    if (!config) {
      console.warn('TickerQ configuration not found');
      return;
    }

    // If basic auth is enabled and user is not authenticated, redirect to login
    if (config.enableBasicAuth && !this.authStore.auth) {
      this.redirectToLogin();
    }
  }

  /**
   * Redirect to the login page
   */
  private redirectToLogin(): void {
    // For basic auth, we'll redirect to a login page or show a login modal
    // The actual credentials will be handled by the browser's basic auth prompt
    // or by a custom login form that doesn't expose the credentials
    
    // Check if we're already on a login page to avoid infinite redirects
    if (window.location.pathname.includes('/login')) {
      return;
    }

    // Redirect to login page using path resolver
    const loginPath = resolvePath('/login');
    window.location.href = loginPath;
  }

  /**
   * Handle basic auth login
   */
  public async handleBasicAuthLogin(username: string, password: string): Promise<boolean> {
    try {
      // Create basic auth token
      const token = btoa(`${username}:${password}`);
      
      // Test the credentials by making a request to the API
      const response = await fetch(resolveApiUrl('/auth/test'), {
        method: 'GET',
        headers: {
          'Authorization': `Basic ${token}`,
          'Content-Type': 'application/json'
        }
      });

      if (response.ok) {
        // Store the auth token
        this.authStore.auth = token;
        localStorage.setItem('auth', token);
        return true;
      } else {
        // Clear any existing auth
        this.authStore.auth = '';
        localStorage.removeItem('auth');
        return false;
      }
    } catch (error) {
      console.error('Authentication failed:', error);
      this.authStore.auth = '';
      localStorage.removeItem('auth');
      return false;
    }
  }

  /**
   * Logout user
   */
  public logout(): void {
    this.authStore.auth = '';
    localStorage.removeItem('auth');
    
    // Redirect to login if basic auth is enabled
    if (window.TickerQConfig?.enableBasicAuth) {
      this.redirectToLogin();
    }
  }

  /**
   * Check if user is authenticated
   */
  public isAuthenticated(): boolean {
    return !!this.authStore.auth;
  }

  /**
   * Get current auth configuration
   */
  public getAuthConfig(): AuthConfig | null {
    return window.TickerQConfig ? {
      enableBasicAuth: window.TickerQConfig.enableBasicAuth || false,
      useHostAuthentication: window.TickerQConfig.useHostAuthentication || false,
      enableBuiltInAuth: window.TickerQConfig.enableBuiltInAuth || false
    } : null;
  }
}

// Create singleton instance
export const authService = new AuthService(); 