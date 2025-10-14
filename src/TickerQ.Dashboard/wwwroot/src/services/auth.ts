/**
 * Clean, simple authentication service for TickerQ Dashboard
 */

// Extend window interface for TypeScript
declare global {
  interface Window {
    TickerQConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: string;
        enabled: boolean;
        sessionTimeout: number;
      };
    };
  }
}

export interface AuthConfig {
  mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom';
  enabled: boolean;
  sessionTimeout: number;
}

export interface AuthStatus {
  authenticated: boolean;
  username?: string;
  message?: string;
}

export interface LoginCredentials {
  username?: string;
  password?: string;
  apiKey?: string;
  hostAccessKey?: string;
}

class AuthService {
  private config: AuthConfig | null = null;
  private status: AuthStatus = { authenticated: false };

  /**
   * Initialize authentication service
   */
  async initialize(): Promise<void> {
    try {
      // Get auth configuration from window (injected by backend)
      const windowConfig = window.TickerQConfig?.auth;
      
      if (!windowConfig) {
        throw new Error('No auth configuration found');
      }
      
      this.config = {
        mode: windowConfig.mode as 'none' | 'basic' | 'apikey' | 'host' | 'custom',
        enabled: windowConfig.enabled,
        sessionTimeout: windowConfig.sessionTimeout
      };
      
      // If no auth required, set as authenticated
      if (!this.config.enabled) {
        this.status = { authenticated: true, username: 'anonymous' };
        return;
      }

      // For now, just check if we have stored credentials without validating
      // This prevents hanging on API calls during initialization
      if (this.hasStoredCredentials()) {
        // Set as authenticated for now - validation will happen on first API call
        const username = this.getStoredUsername();
        this.status = { authenticated: true, username: username || 'user' };
      } else {
        // No stored credentials - user needs to log in
        this.status = { authenticated: false, message: 'Please log in' };
      }
    } catch (error) {
      console.error('Auth initialization failed:', error);
      this.status = { authenticated: false, message: 'Authentication service unavailable' };
    }
  }

  /**
   * Login with credentials
   */
  async login(credentials: LoginCredentials): Promise<boolean> {
    try {
      if (!this.config?.enabled) {
        return true;
      }

      // Store credentials based on auth mode
      this.storeCredentials(credentials);

      // Validate with backend
      const result = await this.validateCredentials();
      
      if (result.authenticated) {
        this.status = result;
        return true;
      }

      // Clear invalid credentials
      this.clearCredentials();
      this.status = result;
      return false;
    } catch (error) {
      console.error('Login failed:', error);
      this.status = { authenticated: false, message: 'Login failed' };
      return false;
    }
  }

  /**
   * Logout
   */
  logout(): void {
    this.clearCredentials();
    this.status = { authenticated: false };
  }

  /**
   * Get current authentication status
   */
  getStatus(): AuthStatus {
    return { ...this.status };
  }

  /**
   * Get authentication configuration
   */
  getConfig(): AuthConfig | null {
    return this.config ? { ...this.config } : null;
  }

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return this.status.authenticated;
  }

  /**
   * Get authorization header for API calls
   */
  getAuthHeader(): string | null {
    if (!this.config?.enabled) {
      return null;
    }

    switch (this.config.mode) {
      case 'basic':
        const basicAuth = localStorage.getItem('tickerq_basic_auth');
        return basicAuth ? `Basic ${basicAuth}` : null;
      
      case 'apikey':
        const apiKey = localStorage.getItem('tickerq_api_key');
        return apiKey ? `Bearer ${apiKey}` : null;
      
      case 'host':
        const hostAccessKey = localStorage.getItem('tickerq_host_access_key');
        return hostAccessKey || null;
      
      default:
        return null;
    }
  }

  /**
   * Get access token for SignalR (WebSocket limitation)
   */
  getAccessToken(): string | null {
    if (!this.config?.enabled) {
      return null;
    }

    switch (this.config.mode) {
      case 'basic':
        return localStorage.getItem('tickerq_basic_auth');
      
      case 'apikey':
        const token = localStorage.getItem('tickerq_api_key');
        return token ? `Bearer:${token}` : null;
      
      case 'host':
        const hostToken = localStorage.getItem('tickerq_host_access_key');
        return hostToken || null;
      
      default:
        return null;
    }
  }


  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = this.getAuthHeader();
      
      // Get the correct base URL with base path
      const config = window.TickerQConfig;
      const baseUrl = config?.backendDomain || config?.basePath || '/tickerq/dashboard';
      const url = `${baseUrl}/api/auth/validate`;
      
      // Add timeout to prevent hanging
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 5000); // 5 second timeout
      
      const response = await fetch(url, {
        method: 'POST',
        headers: authHeader ? { 'Authorization': authHeader } : {},
        signal: controller.signal
      });
      
      clearTimeout(timeoutId);

      if (response.ok) {
        const result = await response.json();
        return {
          authenticated: result.authenticated,
          username: result.username,
          message: result.message
        };
      }

      return { authenticated: false, message: `Server error: ${response.status}` };
    } catch (error) {
      console.error('Credential validation failed:', error);
      return { authenticated: false, message: 'Validation failed' };
    }
  }

  /**
   * Validate stored credentials (public method)
   */
  async validateStoredCredentials(): Promise<void> {
    const result = await this.validateCredentials();
    this.status = result;
    
    if (!result.authenticated) {
      this.clearCredentials();
    }
  }

  private hasStoredCredentials(): boolean {
    return !!(localStorage.getItem('tickerq_basic_auth') || 
              localStorage.getItem('tickerq_api_key') ||
              localStorage.getItem('tickerq_host_access_key'));
  }

  private getStoredUsername(): string | null {
    const basicAuth = localStorage.getItem('tickerq_basic_auth');
    if (basicAuth) {
      try {
        const decoded = atob(basicAuth);
        return decoded.split(':')[0];
      } catch {
        return null;
      }
    }
    return null;
  }

  private storeCredentials(credentials: LoginCredentials): void {
    this.clearCredentials();

    switch (this.config?.mode) {
      case 'basic':
        if (credentials.username && credentials.password) {
          const encoded = btoa(`${credentials.username}:${credentials.password}`);
          localStorage.setItem('tickerq_basic_auth', encoded);
        }
        break;
      
      case 'apikey':
        if (credentials.apiKey) {
          localStorage.setItem('tickerq_api_key', credentials.apiKey);
        }
        break;
      
      case 'host':
        if (credentials.hostAccessKey) {
          localStorage.setItem('tickerq_host_access_key', credentials.hostAccessKey);
        }
        break;
    }
  }

  private clearCredentials(): void {
    localStorage.removeItem('tickerq_basic_auth');
    localStorage.removeItem('tickerq_api_key');
    localStorage.removeItem('tickerq_host_access_key');
  }
}

// Export singleton instance
export const authService = new AuthService();
