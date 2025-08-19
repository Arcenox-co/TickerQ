
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import { GetCronTickerOccurrenceGraphDataRequest, GetCronTickerOccurrenceGraphDataResponse, GetCronTickerOccurrenceRequest, GetCronTickerOccurrenceResponse } from './types/cronTickerOccurrenceService.types';
import { format} from 'timeago.js';
import { nameof } from '@/utilities/nameof';

const getByCronTickerId = () => {
    const baseHttp = useBaseHttpService<GetCronTickerOccurrenceRequest, GetCronTickerOccurrenceResponse>('array')
        .FixToResponseModel(GetCronTickerOccurrenceResponse, response => {
            // Add null check to prevent "Cannot set properties of undefined" error
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


    const requestAsync = async (id: string | undefined) => (await baseHttp.sendAsync("GET", "cron-ticker-occurrences/:cronTickerId", { paramData: { cronTickerId: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronTickerOccurrence = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-ticker-occurrence/:delete", { paramData: { id: id } }));

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

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("GET", "cron-ticker-occurrences/:cronTickerId/:graph-data", { paramData: { cronTickerId: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronTickerOccurrenceService = {
    getByCronTickerId,
    deleteCronTickerOccurrence,
    getCronTickerOccurrenceGraphData
};
