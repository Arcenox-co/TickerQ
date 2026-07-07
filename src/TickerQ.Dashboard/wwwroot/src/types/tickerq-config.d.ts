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
      headerButtons?: Array<{
        label: string;
        icon?: string;
        href: string;
        openInNewTab?: boolean;
        tooltip?: string;
      }>;
    };
  }
}

export {};