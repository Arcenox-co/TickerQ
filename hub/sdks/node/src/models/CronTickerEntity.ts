import type { TickerRequestPayload } from './TimeTickerEntity';

export interface CronTickerEntity {
    id: string;
    function: string;
    description: string | null;
    initIdentifier: string | null;
    createdAt: string;
    updatedAt: string;
    expression: string;
    request: TickerRequestPayload;
    retries: number;
    retryIntervals: number[] | null;
    isEnabled: boolean;
}
