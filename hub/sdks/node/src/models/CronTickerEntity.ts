export interface CronTickerEntity {
    id: string;
    function: string;
    description: string | null;
    initIdentifier: string | null;
    createdAt: string;
    updatedAt: string;
    expression: string;
    request: string | null;
    retries: number;
    retryIntervals: number[] | null;
    isEnabled: boolean;
}
