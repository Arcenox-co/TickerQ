import { TickerStatus, RunCondition } from '../enums';

export interface TimeTickerEntity {
    id: string;
    function: string;
    description: string | null;
    initIdentifier: string | null;
    createdAt: string;
    updatedAt: string;
    status: TickerStatus;
    lockHolder: string | null;
    request: string | null;
    executionTime: string | null;
    lockedAt: string | null;
    executedAt: string | null;
    exceptionMessage: string | null;
    skippedReason: string | null;
    elapsedTime: number;
    retries: number;
    retryCount: number;
    retryIntervals: number[] | null;
    parentId: string | null;
    children: TimeTickerEntity[];
    runCondition: RunCondition | null;
}
