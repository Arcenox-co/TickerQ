
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import { GetCronTickerOccurrenceGraphDataRequest, GetCronTickerOccurrenceGraphDataResponse, GetCronTickerOccurrenceRequest, GetCronTickerOccurrenceResponse } from './types/cronTickerOccurrenceService.types';
import { format} from 'timeago.js';
import { nameof } from '@/utilities/nameof';

interface PaginatedCronTickerOccurrenceResponse {
    items: GetCronTickerOccurrenceResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getByCronTickerId = () => {
    const baseHttp = useBaseHttpService<GetCronTickerOccurrenceRequest, GetCronTickerOccurrenceResponse>('array')
        .FixToResponseModel(GetCronTickerOccurrenceResponse, response => {
            if (!response) {
                return response;
            }
            
            // Safely set status with null check
            if (response.status !== undefined && response.status !== null) {
                response.status = Status[response.status as any];
            }

            if (response.executedAt != null || response.executedAt != undefined) {
                // Ensure the datetime is treated as UTC by adding 'Z' if missing
                const utcExecutedAt = response.executedAt.endsWith('Z') ? response.executedAt : response.executedAt + 'Z';
                response.executedAt = `${format(utcExecutedAt)} (took ${formatTime(response.elapsedTime as number, true)})`;
            }

            const utcExecutionTime = response.executionTime.endsWith('Z') ? response.executionTime : response.executionTime + 'Z';
            response.executionTimeFormatted = formatDate(utcExecutionTime);
            response.lockedAt = formatDate(response.lockedAt)
            return response;
        })
        .FixToHeaders((header) => {
            if (header.key == nameof<GetCronTickerOccurrenceResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetCronTickerOccurrenceResponse>(x => x.id, x => x.elapsedTime, x => x.executionTime, x => x.retryCount, x => x.exception).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetCronTickerOccurrenceResponse>(x => x.executedAt) == header.key) {
                header.title = "Executed At (Elapsed Time)"
            }
            if (nameof<GetCronTickerOccurrenceResponse>(x => x.executionTimeFormatted) == header.key) {
                header.title = "Execution Time"
            }
            return header;
        })
        .ReOrganizeResponse((res) => res.sort((a, b) => new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()));


    const requestAsync = async (id: string | undefined) => (await baseHttp.sendAsync("GET", `cron-ticker-occurrences/${id}`));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getByCronTickerIdPaginated = () => {
    const baseHttp = useBaseHttpService<object, PaginatedCronTickerOccurrenceResponse>('single');
    
    const processResponse = (response: PaginatedCronTickerOccurrenceResponse): PaginatedCronTickerOccurrenceResponse => {
            // Process items in the paginated response
            if (response && response.items && Array.isArray(response.items)) {
                response.items = response.items.map((item: GetCronTickerOccurrenceResponse) => {
                    if (!item) return item;
                    
                    // Safely set status with null check
                    if (item.status !== undefined && item.status !== null) {
                        item.status = Status[item.status as any];
                    }
                    
                    if (item.executedAt != null || item.executedAt != undefined) {
                        // Ensure the datetime is treated as UTC by adding 'Z' if missing
                        const utcExecutedAt = item.executedAt.endsWith('Z') ? item.executedAt : item.executedAt + 'Z';
                        item.executedAt = `${format(utcExecutedAt)} (took ${formatTime(item.elapsedTime as number, true)})`;
                    }
                    
                    const utcExecutionTime = item.executionTime.endsWith('Z') ? item.executionTime : item.executionTime + 'Z';
                    item.executionTimeFormatted = formatDate(utcExecutionTime);
                    item.lockedAt = formatDate(item.lockedAt);
                    
                    return item;
                });
                
                // Sort items
                response.items.sort((a: GetCronTickerOccurrenceResponse, b: GetCronTickerOccurrenceResponse) => 
                    new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()
                );
            }
            
            return response;
    };
    
    const requestAsync = async (id: string | undefined, pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", `cron-ticker-occurrences/${id}/paginated`, { 
            paramData: { pageNumber, pageSize } 
        });
        return processResponse(response);
    };
    
    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronTickerOccurrence = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-ticker-occurrence/delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getCronTickerOccurrenceGraphData = () => {
    const baseHttp = useBaseHttpService<GetCronTickerOccurrenceGraphDataRequest, GetCronTickerOccurrenceGraphDataResponse>('array')
        .FixToResponseModel(GetCronTickerOccurrenceGraphDataResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date),
                type: "line",
                statuses: item.results.map(x => Status[x.item1])
            }
        });

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("GET", `cron-ticker-occurrences/${id}/graph-data`));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronTickerOccurrenceService = {
    getByCronTickerId,
    getByCronTickerIdPaginated,
    deleteCronTickerOccurrence,
    getCronTickerOccurrenceGraphData
};
