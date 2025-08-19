declare global {
  interface Window {
    TickerQConfig?: {
      basePath: string;
      backendDomain?: string;
      useHostAuthentication?: boolean;
      enableBuiltInAuth?: boolean;
      enableBasicAuth?: boolean;
    };
  }
}

export {};
