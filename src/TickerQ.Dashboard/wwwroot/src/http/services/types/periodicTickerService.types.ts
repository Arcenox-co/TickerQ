export class GetPeriodicTickerRequest {
}

export class GetPeriodicTickerResponse {
    id!: string;
    function!: string;
    interval!: string;
    intervalFormatted?: string;
    initIdentifier!: string;
    retryIntervals!: string[];
    description!: string;
    requestType!: string;
    createdAt!: string;
    updatedAt!: string;
    startTime?: string;
    endTime?: string;
    lastExecutedAt?: string;
    lastStartedAt?: string;
    executionCount!: number;
    chainOverlapBehavior!: number | string;
    retries!: number;
    isActive!: boolean;
    actions: string | undefined = undefined;
}

export class UpdatePeriodicTickerRequest {
    function!: string;
    interval!: string;
    request?: string;
    retries?: number;
    description?: string;
    intervals?: number[];
    startTime?: string | null;
    endTime?: string | null;
    chainOverlapBehavior?: number;
    isActive?: boolean;
}

export class AddPeriodicTickerRequest {
    function!: string;
    interval!: string;
    request?: string;
    retries?: number;
    description?: string;
    intervals?: number[];
    startTime?: string | null;
    endTime?: string | null;
    chainOverlapBehavior?: number;
}

export class GetPeriodicTickerGraphDataRangeResponse {
    date!: string;
    results!: { item1: number, item2: number }[];
}

export class GetPeriodicTickerGraphDataResponse {
    item1!: number;
    item2!: number;
}
