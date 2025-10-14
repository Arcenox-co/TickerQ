declare global {
  interface Window {
    TickerQConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom';
        enabled: boolean;
        sessionTimeout: number;
      };
    };
  }
}

export {};