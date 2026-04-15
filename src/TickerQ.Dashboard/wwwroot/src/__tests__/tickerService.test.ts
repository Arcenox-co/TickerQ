import { describe, it, expect, vi, beforeEach } from 'vitest';

/**
 * We test the URL and param construction of tickerService.getRequestData()
 * by mocking the axios instance that baseHttpService delegates to.
 *
 * The bug in issue #817 was that getRequestData sent:
 *   GET ticker-request/id?tickerId=<guid>&tickerType=<int>
 * instead of:
 *   GET ticker-request/<guid>?tickerType=<int>
 */

// Mock axios before any imports that use it
const mockRequest = vi.fn().mockResolvedValue({ data: {}, headers: {} });
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

// Mock vue reactivity
vi.mock('vue', () => ({
    ref: (val: any) => ({ value: val }),
}));

// Mock composables used by baseHttpService
vi.mock('@/composables/useAlert', () => ({
    useAlert: () => ({ showHttpError: vi.fn() }),
}));

// Mock stores used by axiosConfig
vi.mock('@/stores/authStore', () => ({
    useAuthStore: () => ({ handle401Error: vi.fn() }),
}));
vi.mock('@/stores/alertStore', () => ({
    useAlertStore: () => ({ showHttpError: vi.fn() }),
}));

// Mock pathResolver used by axiosConfig
vi.mock('@/utilities/pathResolver', () => ({
    getApiBaseUrl: () => '/api',
    getAuthMode: () => 'none',
}));

// Mock the base response helper
vi.mock('@/http/services/types/base/baseHttpResponse.types', () => ({
    getStatusValueSafe: (v: any) => v,
}));

describe('tickerService.getRequestData', () => {
    beforeEach(() => {
        mockRequest.mockClear();
        mockRequest.mockResolvedValue({ data: { id: 'test' }, headers: {} });
    });

    it('places the ticker GUID in the URL path, not as a query param', async () => {
        // Dynamic import so mocks are in place
        const { tickerService } = await import('@/http/services/tickerService');
        const svc = tickerService.getRequestData();

        const guid = 'eee8fa9a-4a62-452b-8eb0-1331e58d76d9';
        const tickerType = 0;

        await svc.requestAsync(guid, tickerType);

        expect(mockRequest).toHaveBeenCalledTimes(1);
        const callArgs = mockRequest.mock.calls[0][0];

        // URL must contain the actual GUID, not the literal string "id"
        expect(callArgs.url).toBe(`ticker-request/${guid}`);
        // tickerType must be a query param
        expect(callArgs.params).toEqual({ tickerType: 0 });
        // tickerId must NOT be in the query params (it's in the path now)
        expect(callArgs.params).not.toHaveProperty('tickerId');
    });

    it('uses GET method', async () => {
        const { tickerService } = await import('@/http/services/tickerService');
        const svc = tickerService.getRequestData();

        await svc.requestAsync('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', 1);

        const callArgs = mockRequest.mock.calls[0][0];
        expect(callArgs.method).toBe('GET');
    });

    it('works with tickerType=1 for CronTicker', async () => {
        const { tickerService } = await import('@/http/services/tickerService');
        const svc = tickerService.getRequestData();

        const guid = '11111111-2222-3333-4444-555555555555';
        await svc.requestAsync(guid, 1);

        const callArgs = mockRequest.mock.calls[0][0];
        expect(callArgs.url).toBe(`ticker-request/${guid}`);
        expect(callArgs.params).toEqual({ tickerType: 1 });
    });
});
