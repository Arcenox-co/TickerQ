import BaseHub from "./base/baseHub";

export const methodName = {
  onReceiveAddCronTicker: "AddCronTickerNotification",
  onReceiveUpdateCronTicker: "UpdateCronTickerNotification",
  onReceiveDeleteCronTicker: "RemoveCronTickerNotification",
  onReceiveUpdateCronTickerOccurrence: "UpdateCronOccurrenceNotification",
  onReceiveAddCronTickerOccurrence: "AddCronOccurrenceNotification",
  onReceiveAddTimeTicker: "AddTimeTickerNotification",
  onReceiveAddTimeTickersBatch: "AddTimeTickersBatchNotification",
  onReceiveUpdateTimeTicker: "UpdateTimeTickerNotification",
  onReceiveCancelledTicker: "CanceledTickerNotification",
  onReceiveDeleteTimeTicker: "RemoveTimeTickerNotification",
  onReceiveThreadsActive: "GetActiveThreadsNotification",
  onReceiveNextOccurrence: "GetNextOccurrenceNotification",
  onReceiveHostStatus: "GetHostStatusNotification",
  onReceiveHostExceptionMessage: "UpdateHostExceptionNotification"
}
// Define a SignalR service class
class TickerNotificationHub extends BaseHub {  

  async startConnection(): Promise<void> {
    await this.startConnectionAsync();

    Object.values(methodName).forEach((name) => {
      this.connection.on(name, () => {});
    });
  }

  async stopConnection(): Promise<void> {
    await this.stopConnectionAsync();
  }

  onReceiveAddCronTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteCronTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteCronTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronTickerOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronTickerOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveAddCronTickerOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronTickerOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  // Batch add time tickers (used as a lightweight signal to refresh data)
  onReceiveAddTimeTickersBatch(callback: () => void): void {
    this.connection.on(methodName.onReceiveAddTimeTickersBatch, () => {
      callback();
    });
  }

  onReceiveAddTimeTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddTimeTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateTimeTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateTimeTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveCancelledTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveCancelledTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteTimeTicker<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteTimeTicker, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveThreadsActive<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveThreadsActive, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveNextOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveNextOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostStatus<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostStatus, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostExceptionMessage<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostExceptionMessage, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  stopReceiver(methodName: string): void {
    this.connection.off(methodName);
  }

}
export type TickerNotificationHubType = InstanceType<typeof TickerNotificationHub>;
// Export as a singleton instance
export default new TickerNotificationHub();
