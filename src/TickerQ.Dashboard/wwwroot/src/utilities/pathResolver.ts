/**
 * Utility functions for resolving paths using the TickerQ configuration
 */


/**
 * Resolve a path relative to the TickerQ base path
 * @param path - The path to resolve (can be relative or absolute)
 * @returns The resolved path
 */
export function resolvePath(path: string): string {
  const config = window.TickerQConfig;
  
  if (!config) {
    console.warn('TickerQ configuration not found, using path as-is');
    return path;
  }

  // If path is already absolute (starts with http/https), return as-is
  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }

  // If path starts with /, it's an absolute path from the base
  if (path.startsWith('/')) {
    return `${config.basePath}${path}`;
  }

  // If path starts with ./ or ../, it's a relative path
  if (path.startsWith('./') || path.startsWith('../')) {
    return `${config.basePath}/${path}`;
  }

  // Otherwise, treat as relative to base path
  return `${config.basePath}/${path}`;
}

/**
 * Resolve an API URL using the backend domain or base path + /api
 * @param endpoint - The API endpoint (e.g., '/auth/test')
 * @returns The full API URL
 */
export function resolveApiUrl(endpoint: string): string {
  const config = window.TickerQConfig;
  
  if (!config) {
    console.warn('TickerQ configuration not found, using endpoint as-is');
    return endpoint;
  }

  // Remove leading slash if present
  const cleanEndpoint = endpoint.startsWith('/') ? endpoint.slice(1) : endpoint;
  
  // If backend domain is configured, use it for API calls
  if (config.backendDomain) {
    const protocol = getProtocolFromDomain(config.backendDomain);
    const cleanDomain = getCleanDomain(config.backendDomain);
    return `${protocol}://${cleanDomain}/api/${cleanEndpoint}`;
  }
  
  // Otherwise, use the base path (relative to current domain)
  return `${config.basePath}/api/${cleanEndpoint}`;
}

/**
 * Get the base path from configuration
 * @returns The base path or '/' as fallback
 */
export function getBasePath(): string {
  const config = window.TickerQConfig;
  return config?.basePath || '/';
}

/**
 * Get the full backend URL (without /api)
 * @returns The full backend URL or null if not configured
 */
export function getBackendUrl(): string | null {
  const config = window.TickerQConfig;
  
  if (!config?.backendDomain) {
    return null;
  }
  
  const protocol = getProtocolFromDomain(config.backendDomain);
  const cleanDomain = getCleanDomain(config.backendDomain);
  return `${protocol}://${cleanDomain}`;
}

/**
 * Get the API base URL using backend domain or base path + /api
 * @returns The API base URL or '/api' as fallback
 */
export function getApiBaseUrl(): string {
  const config = window.TickerQConfig;
  
  if (!config) {
    return '/api';
  }
  
  // If backend domain is configured, use it for API calls
  if (config.backendDomain) {
    const protocol = getProtocolFromDomain(config.backendDomain);
    const cleanDomain = getCleanDomain(config.backendDomain);
    return `${protocol}://${cleanDomain}/api`;
  }
  
  // Otherwise, use the base path (relative to current domain)
  return `${config.basePath}/api`;
}

export function isBasicAuthEnabled(): boolean {
  const config = window.TickerQConfig;
  return config?.auth?.mode === 'basic' || false;
}

export function isApiKeyAuthEnabled(): boolean {
  const config = window.TickerQConfig;
  return config?.auth?.mode === 'apikey' || false;
}

export function isHostAuthEnabled(): boolean {
  const config = window.TickerQConfig;
  return config?.auth?.mode === 'host' || false;
}

export function requiresAuthentication(): boolean {
  const config = window.TickerQConfig;
  return config?.auth?.enabled || false;
}

export function getAuthMode(): 'basic' | 'apikey' | 'host' | 'none' {
  const config = window.TickerQConfig;
  if (!config?.auth) return 'none';
  
  return config.auth.mode as 'basic' | 'apikey' | 'host' | 'none';
}

/**
 * Get protocol from domain based on prefix
 * @param domain - The domain with optional protocol prefix
 * @returns The protocol (https:// or http://)
 */
function getProtocolFromDomain(domain: string): string {
  if (domain.startsWith('ssl:')) {
    return 'https';
  }
  return 'http';
}

/**
 * Get clean domain without protocol prefix
 * @param domain - The domain with optional protocol prefix
 * @returns The clean domain without prefix
 */
function getCleanDomain(domain: string): string {
  if (domain.startsWith('ssl:')) {
    return domain.substring(4); // Remove 'ssl:' prefix
  }
  return domain;
}

/**
 * Check if TickerQ configuration is available
 * @returns True if configuration is available
 */
export function hasConfig(): boolean {
  return !!window.TickerQConfig;
}
