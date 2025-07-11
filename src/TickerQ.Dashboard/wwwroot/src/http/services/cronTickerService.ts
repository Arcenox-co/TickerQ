
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { AddCronTickerRequest, GetCronTickerGraphDataRangeResponse, GetCronTickerGraphDataResponse, GetCronTickerRequest, GetCronTickerResponse, UpdateCronTickerRequest } from './types/cronTickerService.types';
import { nameof } from '@/utilities/nameof';
import { useFunctionNameStore } from '@/stores/functionNames';

const getCronTickers = () => {
    const functionNamesStore = useFunctionNameStore();

    const baseHttp = useBaseHttpService<GetCronTickerRequest, GetCronTickerResponse>('array')
        .FixToResponseModel(GetCronTickerResponse, response => {
            response.requestType = functionNamesStore.getNamespaceOrNull(response.function) ?? 'N/A';
            response.createdAt = formatDate(response.createdAt);
            response.updatedAt = formatDate(response.updatedAt);
            response.initIdentifier = response.initIdentifier?.split("_").slice(0, 2).join("_");
            if ((response.retryIntervals == null || response.retryIntervals.length == 0) && (response.retries == null || (response.retries as number) == 0))
                response.retryIntervals = [];
            else if ((response.retryIntervals == null || response.retryIntervals.length == 0) && (response.retries != null && (response.retries as number) > 0))
                response.retryIntervals = Array(1).fill(`${30}s`);
            else 
                response.retryIntervals = (response.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
            
            return response;
        })
        .FixToHeaders((header) => {
            if (header.key == nameof<GetCronTickerResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetCronTickerResponse>(x => x.id, x => x.retries).includes(header.key)) {
                header.visibility = false;
            }
            return header;
        });

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "cron-tickers"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const updateCronTicker = () => {
    const baseHttp = useBaseHttpService<UpdateCronTickerRequest, object>('single')
    const requestAsync = async (id: string, request: UpdateCronTickerRequest) => (await baseHttp.sendAsync("PUT", "cron-ticker/:update", { bodyData: request, paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addCronTicker = () => {
    const baseHttp = useBaseHttpService<AddCronTickerRequest, object>('single')
    const requestAsync = async (request: AddCronTickerRequest) => (await baseHttp.sendAsync("POST", "cron-ticker/:add", { bodyData: request }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-ticker/:delete", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const runCronTickerOnDemand = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("POST", "cron-ticker/:run", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphDataRange = () => {
    const baseHttp = useBaseHttpService<object, GetCronTickerGraphDataRangeResponse>('array')
        .FixToResponseModel(GetCronTickerGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-tickers/:graph-data-range", {paramData: {pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphDataRangeById = () => {
    const baseHttp = useBaseHttpService<object, GetCronTickerGraphDataRangeResponse>('array')
        .FixToResponseModel(GetCronTickerGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (id:string ,startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-tickers/:graph-data-range-id", {paramData: {id: id, pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetCronTickerGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "cron-tickers/:graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronTickerService = {
    getCronTickers,
    updateCronTicker,
    addCronTicker,
    deleteCronTicker,
    runCronTickerOnDemand,
    getTimeTickersGraphDataRange,
    getTimeTickersGraphDataRangeById,
    getTimeTickersGraphData
};

