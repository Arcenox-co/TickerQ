
import { useBaseHttpService } from '../base/baseHttpService';
import { CancelTickerRequest, CancelTickerResponse, GetFunctionDataRequest, GetJobStatusesOverall, GetFunctionDataResponse, GetJobStatusesPastWeek, GetMachineJobs, GetNextPlannedTickerResponse, GetOptions, GetTickerDataRequest, GetTickerDataResponse, GetTickerHostStatusResponse } from './types/tickerService.types';

const requestCancel = () => {
    const baseHttp = useBaseHttpService<CancelTickerRequest, CancelTickerResponse>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("POST", "ticker/cancel", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getRequestData = () => {
    const baseHttp = useBaseHttpService<GetTickerDataRequest, GetTickerDataResponse>('single');

    const requestAsync = async (id: string, type: number) => (await baseHttp.sendAsync("GET", "ticker-request/id", { paramData: { tickerId: id, tickerType: type } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getFunctionData = () => {
    const baseHttp = useBaseHttpService<GetFunctionDataRequest, GetFunctionDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "ticker-functions"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getNextPlannedTicker = () => {
    const baseHttp = useBaseHttpService<object, GetNextPlannedTickerResponse>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "ticker-host/next-ticker"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const stopTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "ticker-host/stop"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const startTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "ticker-host/start"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const restartTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "ticker-host/restart"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTickerHostStatus = () => {
    const baseHttp = useBaseHttpService<object, GetTickerHostStatusResponse>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "ticker-host/status"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getOptions = () => {
    const baseHttp = useBaseHttpService<object, GetOptions>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "options"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getMachineJobs = () => {
    const baseHttp = useBaseHttpService<object, GetMachineJobs>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "ticker/machine/jobs"));

    return {
        ...baseHttp,
        requestAsync
    };
}


const getJobStatusesPastWeek = () => {
    const baseHttp = useBaseHttpService<object, GetJobStatusesPastWeek>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "/ticker/statuses/get-last-week"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getJobStatusesOverall = () => {
    const baseHttp = useBaseHttpService<object, GetJobStatusesOverall>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "/ticker/statuses/get"));

    return {
        ...baseHttp,
        requestAsync
    };
}


export const tickerService = {
    requestCancel,
    getRequestData,
    getFunctionData,
    getNextPlannedTicker,
    stopTicker,
    startTicker,
    restartTicker,
    getTickerHostStatus,
    getOptions,
    getMachineJobs,
    getJobStatusesPastWeek,
    getJobStatusesOverall
};
