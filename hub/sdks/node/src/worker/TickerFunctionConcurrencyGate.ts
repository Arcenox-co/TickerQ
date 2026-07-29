/**
 * Per-function concurrency limiter.
 *
 * Uses a simple semaphore pattern: acquire() returns a release function.
 * If maxConcurrency is 0, no limit is applied.
 */
export class TickerFunctionConcurrencyGate {
    private readonly semaphores: Map<string, Semaphore> = new Map();

    /** Get or create a semaphore for the given function. */
    getSemaphore(functionName: string, maxConcurrency: number): Semaphore | null {
        if (maxConcurrency <= 0) return null;
        let sem = this.semaphores.get(functionName);
        if (!sem) {
            sem = new Semaphore(maxConcurrency);
            this.semaphores.set(functionName, sem);
        }
        return sem;
    }
}

interface Waiter {
    grant: () => void;
    reject: (reason?: unknown) => void;
    signal?: AbortSignal;
    abort?: () => void;
}

/** Async counting semaphore with cancellation-safe waiter removal. */
export class Semaphore {
    private currentCount: number;
    private readonly waiters: Waiter[] = [];

    constructor(private readonly maxCount: number) {
        this.currentCount = maxCount;
    }

    async acquire(signal?: AbortSignal): Promise<() => void> {
        if (signal?.aborted) throw abortError();
        if (this.currentCount > 0) {
            this.currentCount--;
            return () => this.release();
        }

        return new Promise<() => void>((resolve, reject) => {
            const waiter: Waiter = {
                reject,
                signal,
                grant: () => {
                    if (waiter.abort) signal?.removeEventListener('abort', waiter.abort);
                    this.currentCount--;
                    resolve(() => this.release());
                },
            };
            waiter.abort = () => {
                const index = this.waiters.indexOf(waiter);
                if (index >= 0) this.waiters.splice(index, 1);
                reject(abortError());
            };
            signal?.addEventListener('abort', waiter.abort, { once: true });
            this.waiters.push(waiter);
        });
    }

    private release(): void {
        this.currentCount++;
        if (this.waiters.length > 0 && this.currentCount > 0) {
            this.waiters.shift()!.grant();
        }
    }

    get availableCount(): number { return this.currentCount; }
    get waitingCount(): number { return this.waiters.length; }
}

function abortError(): Error {
    return Object.assign(new Error('Semaphore wait was cancelled.'), { name: 'AbortError' });
}
