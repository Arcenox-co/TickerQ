
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import {
  AddTimeTickerRequest,
  AddChainJobsRequest,
  GetTimeTickerGraphDataRangeResponse,
  GetTimeTickerGraphDataResponse,
  GetTimeTickerResponse,
  UpdateTimeTickerRequest
} from './types/timeTickerService.types'
import { nameof } from '@/utilities/nameof';
import { format} from 'timeago.js';
import { useFunctionNameStore } from '@/stores/functionNames';

const getTimeTickers = () => {
    const functionNamesStore = useFunctionNameStore();

    const baseHttp = useBaseHttpService<object, GetTimeTickerResponse>('array')
        .FixToResponseModel(GetTimeTickerResponse, response => {
            // Add null check to prevent "Cannot set properties of undefined" error
            if (!response) {
                return response;
            }

            // Recursive function to process item and its children
            const processItem = (item: GetTimeTickerResponse): GetTimeTickerResponse => {
                // Safely set status with null check
                if (item.status !== undefined && item.status !== null) {
                    item.status = Status[item.status as any];
                }

                if (item.executedAt != null || item.executedAt != undefined)
                    item.executedAt = `${format(item.executedAt)} (took ${formatTime(item.elapsedTime as number, true)})`;

                item.executionTimeFormatted = formatDate(item.executionTime);
                item.requestType = functionNamesStore.getNamespaceOrNull(item.function) ?? '';

                if (item.retryIntervals == null || item.retryIntervals.length == 0 && item.retries != null && (item.retries as number) > 0)
                    item.retryIntervals = Array(1).fill(`${30}s`);
                else
                    item.retryIntervals = (item.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));

                item.lockHolder = item.lockHolder ?? '-';

                // Process children recursively
                if (item.children && Array.isArray(item.children)) {
                    item.children = item.children.map(child => processItem(child));
                }

                return item;
            };

            return processItem(response);
        })
        .FixToHeaders((header) => {
            if (nameof<GetTimeTickerResponse>(x => x.actions) == header.key) {
                header.sortable = false;
            }
            if (nameof<GetTimeTickerResponse>(x => x.id, x => x.exceptionMessage,x => x.skippedReason, x => x.retries, x => x.lockedAt, x => x.createdAt, x => x.updatedAt, x => x.retryCount, x => x.elapsedTime, x => x.executionTime, x => x.children).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetTimeTickerResponse>(x => x.executedAt) == header.key) {
                header.title = "Executed At (Elapsed Time)"
            }
            if (nameof<GetTimeTickerResponse>(x => x.executionTimeFormatted) == header.key) {
                header.title = "Execution Time"
            }

            if (nameof<GetTimeTickerResponse>((x) => x.batchParent) == header.key) {
              header.visibility = false
            }

            if (nameof<GetTimeTickerResponse>((x) => x.batchRunCondition) == header.key) {
              header.visibility = false
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

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "time-ticker/delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addTimeTicker = () => {
    const baseHttp = useBaseHttpService<AddTimeTickerRequest, object>('single');

    const requestAsync = async (data: AddTimeTickerRequest) => (await baseHttp.sendAsync("POST", "time-ticker/add", { bodyData: data }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const updateTimeTicker = () => {
    const baseHttp = useBaseHttpService<UpdateTimeTickerRequest, object>('single');

    const requestAsync = async (id: string, data: UpdateTimeTickerRequest) => (await baseHttp.sendAsync("PUT", "time-ticker/update", { bodyData: data, paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addChainJobs = () => {
  const baseHttp = useBaseHttpService<AddChainJobsRequest, object>('single');

  const requestAsync = async (data: AddChainJobsRequest) => (await baseHttp.sendAsync("POST", "time-ticker/:add", { bodyData: data }));

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
    addChainJobs
};
