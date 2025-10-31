export class GetCronTickerRequest {
}

export class GetCronTickerResponse {
    id!:string;
    function!:string;
    expression!:string;
    initIdentifier!:string;
    retryIntervals!:string[];
    description!:string;
    requestType!:string;
    createdAt!:string;
    updatedAt!:string;
    retries!:number;
    actions:string|undefined = undefined;
}

export class UpdateCronTickerRequest {
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}

export class GetCronTickerGraphDataRangeResponse{
    date!:string;
    results!:{item1:number, item2:number }[];
}

export class GetCronTickerGraphDataResponse{
    item1!:number;
    item2!:number;
}

export class AddCronTickerRequest {
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}
