import type { TickerTaskPriority } from '../enums/TickerTaskPriority';
import type { TickerFunctionRequestContractInfo } from '../infrastructure/TickerFunctionProvider';

/** A registered function sent to the Hub during node synchronization. */
export interface NodeFunction {
    functionName: string;
    expression: string;
    taskPriority: TickerTaskPriority;
    /** Legacy compatibility mirror. */
    requestType: string;
    /** Legacy compatibility mirror. */
    requestExampleJson: string;
    contractVersion: number;
    /** Presence-aware canonical request contract; omitted for request-less functions. */
    requestContract?: TickerFunctionRequestContractInfo;
}
