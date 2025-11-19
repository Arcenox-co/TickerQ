export class CancelTickerRequest {
    
}

export class CancelTickerResponse {

}


export class GetTickerDataRequest{

}

export class GetTickerDataResponse{
    result?:string;
    matchType!:number;
}

export class GetFunctionDataRequest{

}

export class GetFunctionDataResponse{
    functionName!:string;
    functionRequestNamespace!:string;
    functionRequestType!:string;
    priority!:number
}

export class GetNextPlannedTickerResponse{
    nextOccurrence?:string;
}

export class GetTickerHostStatusResponse{
    isRunning!:boolean;
}


export class GetOptions{
    maxConcurrency!:number;
    currentMachine!:string;
    lastHostExceptionMessage!:string;
    schedulerTimeZone?:string;
}

export class GetMachineJobs{
    item1!:string;
    item2!:number;
}


export class GetJobStatusesPastWeek{
    item1!:string;
    item2!:number;
}

export class GetJobStatusesOverall{
    item1!:string;
    item2!:number;
}
