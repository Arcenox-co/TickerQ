
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
    exception?:string;
    batchParent?:string;
    batchRunCondition?:number;
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

export class SetBatchParentRequest {
  parentId!:string;
  batchRunCondition?:number;
  targetId!:string;
}

export class UnbatchTickerRequest {
  tickerId!:string;
}
