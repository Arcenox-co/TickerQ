export class GetPeriodicTickerOccurrenceRequest {
    id!: string
}

export class GetPeriodicTickerOccurrenceResponse {
    id!: string;
    status!: number | string;
    exceptionMessage?: string;
    skippedReason?: string;
    retryIntervals!: string[] | string | null;
    lockHolder!: string;
    lockedAt!: string;
    executionTime!: string;
    executionTimeFormatted!: string;
    executedAt!: string;
    elapsedTime!: string | number;
    retryCount!: number;
    actions: string | undefined = undefined;
}

export class GetPeriodicTickerOccurrenceGraphDataRequest {
}

export class GetPeriodicTickerOccurrenceGraphDataResponse {
    date!: string;
    results!: { item1: number, item2: number }[];
    type!: string;
    statuses!: string[]
}
