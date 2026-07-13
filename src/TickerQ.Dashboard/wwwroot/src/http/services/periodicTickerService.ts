
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { AddPeriodicTickerRequest, GetPeriodicTickerGraphDataRangeResponse, GetPeriodicTickerGraphDataResponse, GetPeriodicTickerRequest, GetPeriodicTickerResponse, UpdatePeriodicTickerRequest } from './types/periodicTickerService.types';
import { nameof } from '@/utilities/nameof';
import { useFunctionNameStore } from '@/stores/functionNames';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedPeriodicTickerResponse {
    items: GetPeriodicTickerResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

// Formats a .NET TimeSpan string (e.g. "00:05:00", "1.00:00:00", "00:00:00.2000000") into a compact
// human-friendly interval like "5m", "1d", "200ms".
const formatInterval = (interval: string | null | undefined): string => {
    if (!interval) return 'N/A';
    // Parse [d.]hh:mm:ss[.fffffff]
    const daySplit = interval.split('.');
    let days = 0;
    let rest = interval;
    if (daySplit.length > 1 && daySplit[0].indexOf(':') === -1) {
        days = parseInt(daySplit[0], 10) || 0;
        rest = daySplit.slice(1).join('.');
    }
    const [hh = '0', mm = '0', ssPart = '0'] = rest.split(':');
    const secSplit = ssPart.split('.');
    const seconds = parseInt(secSplit[0], 10) || 0;
    const fraction = secSplit.length > 1 ? parseFloat('0.' + secSplit[1]) : 0;
    const totalMs = ((((days * 24 + parseInt(hh, 10)) * 60) + parseInt(mm, 10)) * 60 + seconds) * 1000 + Math.round(fraction * 1000);

    if (totalMs < 1000) return `${totalMs}ms`;
    const parts: string[] = [];
    let s = Math.floor(totalMs / 1000);
    const d = Math.floor(s / 86400); s -= d * 86400;
    const h = Math.floor(s / 3600); s -= h * 3600;
    const m = Math.floor(s / 60); s -= m * 60;
    if (d) parts.push(`${d}d`);
    if (h) parts.push(`${h}h`);
    if (m) parts.push(`${m}m`);
    if (s) parts.push(`${s}s`);
    return parts.length ? parts.join(' ') : '0s';
};

const applyCommon = (item: GetPeriodicTickerResponse, functionNamesStore: ReturnType<typeof useFunctionNameStore>, timeZoneStore: ReturnType<typeof useTimeZoneStore>): GetPeriodicTickerResponse => {
    item.requestType = functionNamesStore.getNamespaceOrNull(item.function) ?? 'N/A';
    item.intervalFormatted = formatInterval(item.interval);
    item.createdAt = formatDate(item.createdAt, true, timeZoneStore.effectiveTimeZone);
    item.updatedAt = formatDate(item.updatedAt, true, timeZoneStore.effectiveTimeZone);
    if (item.startTime) item.startTime = formatDate(item.startTime, true, timeZoneStore.effectiveTimeZone);
    if (item.endTime) item.endTime = formatDate(item.endTime, true, timeZoneStore.effectiveTimeZone);
    if (item.lastExecutedAt) item.lastExecutedAt = formatDate(item.lastExecutedAt, true, timeZoneStore.effectiveTimeZone);
    if (item.lastStartedAt) item.lastStartedAt = formatDate(item.lastStartedAt, true, timeZoneStore.effectiveTimeZone);
    item.initIdentifier = item.initIdentifier?.split("_").slice(0, 2).join("_");
    if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries == null || (item.retries as number) == 0))
        item.retryIntervals = [];
    else if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries != null && (item.retries as number) > 0))
        item.retryIntervals = Array(1).fill(`${30}s`);
    else
        item.retryIntervals = (item.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
    return item;
};

const getPeriodicTickers = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();

    const baseHttp = useBaseHttpService<GetPeriodicTickerRequest, GetPeriodicTickerResponse>('array')
        .FixToResponseModel(GetPeriodicTickerResponse, response => applyCommon(response, functionNamesStore, timeZoneStore))
        .FixToHeaders((header) => {
            if (header.key == nameof<GetPeriodicTickerResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetPeriodicTickerResponse>(x => x.id, x => x.retries, x => x.interval).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetPeriodicTickerResponse>(x => x.intervalFormatted) == header.key) {
                header.title = "Interval";
            }
            return header;
        });

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "periodic-tickers"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getPeriodicTickersPaginated = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();

    const baseHttp = useBaseHttpService<object, PaginatedPeriodicTickerResponse>('single');

    const processResponse = (response: PaginatedPeriodicTickerResponse): PaginatedPeriodicTickerResponse => {
        if (response && response.items && Array.isArray(response.items)) {
            response.items = response.items.map((item: GetPeriodicTickerResponse) => applyCommon(item, functionNamesStore, timeZoneStore));
        }
        return response;
    };

    const requestAsync = async (pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", "periodic-tickers/paginated", {
            paramData: { pageNumber, pageSize }
        });
        return processResponse(response);
    };

    return {
        ...baseHttp,
        requestAsync
    };
}

const updatePeriodicTicker = () => {
    const baseHttp = useBaseHttpService<UpdatePeriodicTickerRequest, object>('single')
    const requestAsync = async (id: string, request: UpdatePeriodicTickerRequest) => (await baseHttp.sendAsync("PUT", "periodic-ticker/update", { bodyData: request, paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addPeriodicTicker = () => {
    const baseHttp = useBaseHttpService<AddPeriodicTickerRequest, object>('single')
    const requestAsync = async (request: AddPeriodicTickerRequest) => (await baseHttp.sendAsync("POST", "periodic-ticker/add", { bodyData: request }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deletePeriodicTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "periodic-ticker/delete", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const togglePeriodicTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string, isActive: boolean) => (await baseHttp.sendAsync("PUT", "periodic-ticker/toggle", { paramData: { id, isActive } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const pausePeriodicTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("PUT", "periodic-ticker/pause", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const resumePeriodicTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("PUT", "periodic-ticker/resume", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getPeriodicTickersGraphDataRange = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<object, GetPeriodicTickerGraphDataRangeResponse>('array')
        .FixToResponseModel(GetPeriodicTickerGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false, timeZoneStore.effectiveTimeZone),
            }
        });

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "periodic-tickers/graph-data-range", { paramData: { pastDays: startDate, futureDays: endDate } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getPeriodicTickersGraphDataRangeById = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<object, GetPeriodicTickerGraphDataRangeResponse>('array')
        .FixToResponseModel(GetPeriodicTickerGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false, timeZoneStore.effectiveTimeZone),
            }
        });

    const requestAsync = async (id: string, startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "periodic-tickers/graph-data-range-id", { paramData: { id: id, pastDays: startDate, futureDays: endDate } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getPeriodicTickersGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetPeriodicTickerGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "periodic-tickers/graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const periodicTickerService = {
    getPeriodicTickers,
    getPeriodicTickersPaginated,
    updatePeriodicTicker,
    addPeriodicTicker,
    deletePeriodicTicker,
    togglePeriodicTicker,
    pausePeriodicTicker,
    resumePeriodicTicker,
    getPeriodicTickersGraphDataRange,
    getPeriodicTickersGraphDataRangeById,
    getPeriodicTickersGraphData
};
