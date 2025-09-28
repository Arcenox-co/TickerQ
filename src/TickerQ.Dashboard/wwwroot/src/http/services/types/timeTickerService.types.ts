
export class GetTimeTickerResponse {
    id!:string;
    function!:string;
    status!:string|number;
    retries!:string|number;
    retryCount!:number;
    retryIntervals!:string[]|string|null;
    description!:string;
    requestType!:string;
    lockHolder!:string;
    lockedAt!:string;
    executionTime!:string;
    executionTimeFormatted!:string;
    createdAt!:string;
    updatedAt!:string;
    executedAt!:string;
    elapsedTime!:string|number;
    actions:string|undefined = undefined;
    exceptionMessage?:string;
    skippedReason?:string;
    batchParent?:string;
    batchRunCondition?:string|number;
    children?:GetTimeTickerResponse[];
}

export class GetTimeTickerGraphDataRangeResponse{
    date!:string;
    results!:{item1:number, item2:number }[];
}

export class GetTimeTickerGraphDataResponse{
    item1!:number;
    item2!:number;
}

export class AddTimeTickerRequest {
    function!:string;
    request!:string;
    retries!:number;
    description!:string;
    executionTime?:string;
    intervals?:number[];
}

export class UpdateTimeTickerRequest {
    function!:string;
    request!:string;
    retries!:number;
    description!:string;
    executionTime?:string;
    intervals?:number[];
}

export class AddChainJobsRequest {
  function!: string;
  description!: string;
  executionTime?: string | null;
  retries!: number;
  request!: string | null; // string that gets converted to bytes by custom converter, or null if not set
  intervals?: number[];
  runCondition?: number;
  children?: AddChainJobsRequest[];
}
