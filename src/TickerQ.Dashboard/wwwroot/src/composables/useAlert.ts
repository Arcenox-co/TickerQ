import { useAlertStore, type AlertOptions } from '@/stores/alertStore'

/**
 * Composable for managing alerts throughout the application
 * Provides convenient methods to show different types of alerts
 */
export function useAlert() {
  const alertStore = useAlertStore()

  /**
   * Show a general alert with custom options
   */
  const showAlert = (options: AlertOptions) => {
    return alertStore.addAlert(options)
  }

  /**
   * Show an error alert
   */
  const showError = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showError(message, options)
  }

  /**
   * Show a warning alert
   */
  const showWarning = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showWarning(message, options)
  }

  /**
   * Show an info alert
   */
  const showInfo = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showInfo(message, options)
  }

  /**
   * Show a success alert
   */
  const showSuccess = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showSuccess(message, options)
  }

  /**
   * Show an HTTP error alert with intelligent error parsing
   */
  const showHttpError = (error: any, customMessage?: string) => {
    return alertStore.showHttpError(error, customMessage)
  }

  /**
   * Dismiss a specific alert by ID
   */
  const dismissAlert = (id: string) => {
    alertStore.dismissAlert(id)
  }

  /**
   * Clear all alerts
   */
  const clearAllAlerts = () => {
    alertStore.clearAllAlerts()
  }

  /**
   * Get all current alerts
   */
  const alerts = alertStore.alerts

  return {
    // Methods
    showAlert,
    showError,
    showWarning,
    showInfo,
    showSuccess,
    showHttpError,
    dismissAlert,
    clearAllAlerts,
    
    // State
    alerts
  }
}
