import { describe, it, expect, vi, beforeEach } from 'vitest';

const mockRequest = vi.fn().mockResolvedValue({ data: { items: [], totalCount: 0 }, headers: {} });

vi.mock('axios', () => {
    const CancelToken = { source: () => ({ token: {}, cancel: vi.fn() }) };
    return {
        default: {
            create: () => ({
                request: mockRequest,
                interceptors: {
                    request: { use: vi.fn() },
                    response: { use: vi.fn() },
                },
            }),
            CancelToken: Object.assign(function (executor: (cancel: Function) => void) {
                executor(vi.fn());
            }, { source: CancelToken.source }),
        },
        AxiosError: class AxiosError extends Error {},
    };
});

vi.mock('vue', () => ({
    ref: (val: any) => ({ value: val }),
}));

vi.mock('@/composables/useAlert', () => ({
    useAlert: () => ({ showHttpError: vi.fn() }),
}));

vi.mock('@/stores/authStore', () => ({
    useAuthStore: () => ({ handle401Error: vi.fn() }),
}));
vi.mock('@/stores/alertStore', () => ({
    useAlertStore: () => ({ showHttpError: vi.fn() }),
}));
vi.mock('@/stores/functionNames', () => ({
    useFunctionNameStore: () => ({ getNamespaceOrNull: () => null }),
}));
vi.mock('@/stores/timeZoneStore', () => ({
    useTimeZoneStore: () => ({ effectiveTimeZone: 'UTC' }),
}));

vi.mock('@/utilities/pathResolver', () => ({
    getApiBaseUrl: () => '/api',
    getAuthMode: () => 'none',
}));

vi.mock('@/http/services/types/base/baseHttpResponse.types', () => ({
    getStatusValueSafe: (v: any) => v,
    Status: {},
}));

describe('timeTickerService.getTimeTickersPaginated', () => {
    beforeEach(() => {
        mockRequest.mockClear();
        mockRequest.mockResolvedValue({ data: { items: [], totalCount: 0 }, headers: {} });
    });

    it('omits status and search when not provided', async () => {
        const { timeTickerService } = await import('@/http/services/timeTickerService');
        const svc = timeTickerService.getTimeTickersPaginated();

        await svc.requestAsync(1, 20);

        expect(mockRequest).toHaveBeenCalledTimes(1);
        expect(mockRequest.mock.calls[0][0].params).toEqual({ pageNumber: 1, pageSize: 20 });
    });

    it('forwards status and search when provided', async () => {
        const { timeTickerService } = await import('@/http/services/timeTickerService');
        const svc = timeTickerService.getTimeTickersPaginated();

        await svc.requestAsync(2, 10, 'Failed', 'timeout');

        expect(mockRequest.mock.calls[0][0].params).toEqual({
            pageNumber: 2,
            pageSize: 10,
            status: 'Failed',
            search: 'timeout',
        });
    });

    it('omits empty-string search even if explicitly passed', async () => {
        const { timeTickerService } = await import('@/http/services/timeTickerService');
        const svc = timeTickerService.getTimeTickersPaginated();

        await svc.requestAsync(1, 20, undefined, '');

        expect(mockRequest.mock.calls[0][0].params).toEqual({ pageNumber: 1, pageSize: 20 });
    });
});
