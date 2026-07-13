
import { formatDate, formatTime, formatTimeAgo } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import { GetPeriodicTickerOccurrenceGraphDataRequest, GetPeriodicTickerOccurrenceGraphDataResponse, GetPeriodicTickerOccurrenceRequest, GetPeriodicTickerOccurrenceResponse } from './types/periodicTickerOccurrenceService.types';
import { nameof } from '@/utilities/nameof';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedPeriodicTickerOccurrenceResponse {
    items: GetPeriodicTickerOccurrenceResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getByPeriodicTickerId = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<GetPeriodicTickerOccurrenceRequest, GetPeriodicTickerOccurrenceResponse>('array')
        .FixToResponseModel(GetPeriodicTickerOccurrenceResponse, response => {
            if (!response) {
                return response;
            }

            if (response.status != null) {
                response.status = Status[response.status as any];
            }

            if (response.executedAt != null) {
                response.executedAt = `${formatTimeAgo(response.executedAt)} (took ${formatTime(response.elapsedTime as number, true)})`;
            }

            const utcExecutionTime = response.executionTime.endsWith('Z') ? response.executionTime : response.executionTime + 'Z';
            response.executionTimeFormatted = formatDate(utcExecutionTime, true, timeZoneStore.effectiveTimeZone);
            response.lockedAt = formatDate(response.lockedAt, true, timeZoneStore.effectiveTimeZone)
            return response;
        })
        .FixToHeaders((header) => {
            if (header.key == nameof<GetPeriodicTickerOccurrenceResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetPeriodicTickerOccurrenceResponse>(x => x.id, x => x.elapsedTime, x => x.executionTime, x => x.retryCount, x => x.exceptionMessage, x => x.skippedReason).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetPeriodicTickerOccurrenceResponse>(x => x.executedAt) == header.key) {
                header.title = "Executed At (Elapsed Time)"
            }
            if (nameof<GetPeriodicTickerOccurrenceResponse>(x => x.executionTimeFormatted) == header.key) {
                header.title = "Execution Time"
            }
            return header;
        })
        .ReOrganizeResponse((res) => res.sort((a, b) => new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()));


    const requestAsync = async (id: string | undefined) => (await baseHttp.sendAsync("GET", `periodic-ticker-occurrences/${id}`));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getByPeriodicTickerIdPaginated = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<object, PaginatedPeriodicTickerOccurrenceResponse>('single');

    const processResponse = (response: PaginatedPeriodicTickerOccurrenceResponse): PaginatedPeriodicTickerOccurrenceResponse => {
        if (response && response.items && Array.isArray(response.items)) {
            response.items = response.items.map((item: GetPeriodicTickerOccurrenceResponse) => {
                if (!item) return item;

                if (item.status != null) {
                    const statusValue = Status[item.status as any];
                    item.status = statusValue !== undefined ? statusValue : String(item.status);
                } else {
                    item.status = 'Unknown';
                }

                if (item.executedAt != null) {
                    item.executedAt = `${formatTimeAgo(item.executedAt)} (took ${formatTime(item.elapsedTime as number, true)})`;
                }

                const utcExecutionTime = item.executionTime.endsWith('Z') ? item.executionTime : item.executionTime + 'Z';
                item.executionTimeFormatted = formatDate(utcExecutionTime, true, timeZoneStore.effectiveTimeZone);
                item.lockedAt = formatDate(item.lockedAt, true, timeZoneStore.effectiveTimeZone);

                return item;
            });

            response.items.sort((a: GetPeriodicTickerOccurrenceResponse, b: GetPeriodicTickerOccurrenceResponse) =>
                new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()
            );
        }

        return response;
    };

    const requestAsync = async (id: string | undefined, pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", `periodic-ticker-occurrences/${id}/paginated`, {
            paramData: { pageNumber, pageSize }
        });
        return processResponse(response);
    };

    return {
        ...baseHttp,
        requestAsync
    };
}

const deletePeriodicTickerOccurrence = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "periodic-ticker-occurrence/delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getPeriodicTickerOccurrenceGraphData = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<GetPeriodicTickerOccurrenceGraphDataRequest, GetPeriodicTickerOccurrenceGraphDataResponse>('array')
        .FixToResponseModel(GetPeriodicTickerOccurrenceGraphDataResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false, timeZoneStore.effectiveTimeZone),
                type: "line",
                statuses: item.results.map(x => Status[x.item1])
            }
        });

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("GET", `periodic-ticker-occurrences/${id}/graph-data`));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const periodicTickerOccurrenceService = {
    getByPeriodicTickerId,
    getByPeriodicTickerIdPaginated,
    deletePeriodicTickerOccurrence,
    getPeriodicTickerOccurrenceGraphData
};
