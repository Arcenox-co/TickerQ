import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClientProvider } from "@tanstack/react-query";
import { TooltipProvider } from "@/components/ui/tooltip";
import { Toaster } from "@/components/ui/sonner";
import { queryClient } from "@/lib/query-client";
import { getRuntimeConfig, normalizeBasePath } from "@/lib/runtime-config";
import { TimezoneProvider } from "@/lib/timezone";
import { AuthProvider } from "@/lib/auth/auth-context";
import App from "./App";
import "./index.css";

const basename = normalizeBasePath(getRuntimeConfig().basePath);

// Customer-branded title (SetTitle on the backend) — applied before first paint.
document.title = getRuntimeConfig().title;

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter basename={basename}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <TimezoneProvider>
            <TooltipProvider>
              <App />
              <Toaster />
            </TooltipProvider>
          </TimezoneProvider>
        </AuthProvider>
      </QueryClientProvider>
    </BrowserRouter>
  </StrictMode>
);
