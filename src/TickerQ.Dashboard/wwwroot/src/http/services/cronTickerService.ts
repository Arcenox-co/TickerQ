
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { AddCronTickerRequest, GetCronTickerGraphDataRangeResponse, GetCronTickerGraphDataResponse, GetCronTickerRequest, GetCronTickerResponse, UpdateCronTickerRequest } from './types/cronTickerService.types';
import { nameof } from '@/utilities/nameof';
import { useFunctionNameStore } from '@/stores/functionNames';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedCronTickerResponse {
    items: GetCronTickerResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getCronTickers = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();

    const baseHttp = useBaseHttpService<GetCronTickerRequest, GetCronTickerResponse>('array')
        .FixToResponseModel(GetCronTickerResponse, response => {
            response.requestType = functionNamesStore.getNamespaceOrNull(response.function) ?? 'N/A';
            response.createdAt = formatDate(response.createdAt, true, timeZoneStore.effectiveTimeZone);
            response.updatedAt = formatDate(response.updatedAt, true, timeZoneStore.effectiveTimeZone);
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

const getCronTickersPaginated = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();
    
    const baseHttp = useBaseHttpService<object, PaginatedCronTickerResponse>('single');
    
    const processResponse = (response: PaginatedCronTickerResponse): PaginatedCronTickerResponse => {
            // Process items in the paginated response
            if (response && response.items && Array.isArray(response.items)) {
                response.items = response.items.map((item: GetCronTickerResponse) => {
                    item.requestType = functionNamesStore.getNamespaceOrNull(item.function) ?? 'N/A';
                    item.createdAt = formatDate(item.createdAt, true, timeZoneStore.effectiveTimeZone);
                    item.updatedAt = formatDate(item.updatedAt, true, timeZoneStore.effectiveTimeZone);
                    item.initIdentifier = item.initIdentifier?.split("_").slice(0, 2).join("_");
                    if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries == null || (item.retries as number) == 0))
                        item.retryIntervals = [];
                    else if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries != null && (item.retries as number) > 0))
                        item.retryIntervals = Array(1).fill(`${30}s`);
                    else 
                        item.retryIntervals = (item.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
                    
                    return item;
                });
            }
            
            return response;
    };
    
    const requestAsync = async (pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", "cron-tickers/paginated", { 
            paramData: { pageNumber, pageSize } 
        });
        return processResponse(response);
    };
    
    return {
        ...baseHttp,
        requestAsync
    };
}

const updateCronTicker = () => {
    const baseHttp = useBaseHttpService<UpdateCronTickerRequest, object>('single')
    const requestAsync = async (id: string, request: UpdateCronTickerRequest) => (await baseHttp.sendAsync("PUT", "cron-ticker/update", { bodyData: request, paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addCronTicker = () => {
    const baseHttp = useBaseHttpService<AddCronTickerRequest, object>('single')
    const requestAsync = async (request: AddCronTickerRequest) => (await baseHttp.sendAsync("POST", "cron-ticker/add", { bodyData: request }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-ticker/delete", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const runCronTickerOnDemand = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("POST", "cron-ticker/run", { paramData: { id } }));

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

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-tickers/graph-data-range", {paramData: {pastDays: startDate, futureDays: endDate}}));

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

    const requestAsync = async (id:string ,startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-tickers/graph-data-range-id", {paramData: {id: id, pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetCronTickerGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "cron-tickers/graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronTickerService = {
    getCronTickers,
    getCronTickersPaginated,
    updateCronTicker,
    addCronTicker,
    deleteCronTicker,
    runCronTickerOnDemand,
    getTimeTickersGraphDataRange,
    getTimeTickersGraphDataRangeById,
    getTimeTickersGraphData
};
