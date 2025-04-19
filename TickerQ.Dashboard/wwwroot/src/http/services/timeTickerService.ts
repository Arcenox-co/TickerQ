
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import {
    AddTimeTickerRequest,
    GetTimeTickerGraphDataRangeResponse,
    GetTimeTickerGraphDataResponse,
    GetTimeTickerResponse,
    UpdateTimeTickerRequest
} from './types/timeTickerService.types';
import { nameof } from '@/utilities/nameof';
import { format} from 'timeago.js';
import { useFunctionNameStore } from '@/stores/functionNames';

const getTimeTickers = () => {
    const functionNamesStore = useFunctionNameStore();

    const baseHttp = useBaseHttpService<object, GetTimeTickerResponse>('array')
        .FixToResponseModel(GetTimeTickerResponse, response => {
            response.status = Status[response.status as any];

            if (response.executedAt != null || response.executedAt != undefined)
                response.executedAt = `${format(response.executedAt)} (took ${formatTime(response.elapsedTime as number, true)})`;

            response.executionTimeFormatted = formatDate(response.executionTime);
            response.requestType = functionNamesStore.getNamespaceOrNull(response.function) ?? 'N/A';

            response.description = response.description == '' ? 'N/A' : response.description;

            if (response.retryIntervals == null || response.retryIntervals.length == 0 && response.retries != null && (response.retries as number) > 0) 
                response.retryIntervals = Array(1).fill(`${30}s`);
            else 
                response.retryIntervals = (response.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
            
            response.lockHolder = response.lockHolder ?? 'N/A';

            return response;
        })
        .FixToHeaders((header) => {
            if (nameof<GetTimeTickerResponse>(x => x.actions) == header.key) {
                header.sortable = false;
            }
            if (nameof<GetTimeTickerResponse>(x => x.id, x => x.exception, x => x.retries, x => x.lockedAt, x => x.createdAt, x => x.updatedAt, x => x.retryCount, x => x.elapsedTime, x => x.executionTime).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetTimeTickerResponse>(x => x.executedAt) == header.key) {
                header.title = "Executed At (Elapsed Time)"
            }
            if (nameof<GetTimeTickerResponse>(x => x.executionTimeFormatted) == header.key) {
                header.title = "Execution Time"
            }

            return header;
        })
        .ReOrganizeResponse((res) => res.sort((a, b) => new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()));

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "time-tickers"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphDataRange = () => {
    const baseHttp = useBaseHttpService<object, GetTimeTickerGraphDataRangeResponse>('array')
        .FixToResponseModel(GetTimeTickerGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "time-tickers/:graph-data-range", {paramData: {pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeTickersGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetTimeTickerGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "time-tickers/:graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}


const deleteTimeTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "time-ticker/:delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addTimeTicker = () => {
    const baseHttp = useBaseHttpService<AddTimeTickerRequest, object>('single');

    const requestAsync = async (data: AddTimeTickerRequest) => (await baseHttp.sendAsync("POST", "time-ticker/:add", { bodyData: data }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const updateTimeTicker = () => {
    const baseHttp = useBaseHttpService<UpdateTimeTickerRequest, object>('single');

    const requestAsync = async (id: string, data: UpdateTimeTickerRequest) => (await baseHttp.sendAsync("PUT", "time-ticker/:update", { bodyData: data, paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}


export const timeTickerService = {
    getTimeTickers,
    deleteTimeTicker,
    getTimeTickersGraphDataRange,
    getTimeTickersGraphData,
    addTimeTicker,
    updateTimeTicker,
};