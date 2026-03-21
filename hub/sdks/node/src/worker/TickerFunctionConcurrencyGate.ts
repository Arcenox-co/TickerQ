/**
 * Per-function concurrency limiter.
 *
 * Uses a simple semaphore pattern: acquire() returns a release function.
 * If maxConcurrency is 0, no limit is applied.
 */
export class TickerFunctionConcurrencyGate {
    private readonly semaphores: Map<string, Semaphore> = new Map();

    /**
     * Get or create a semaphore for the given function.
     * Returns null if maxConcurrency is 0 (no limit).
     */
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

/**
 * Async counting semaphore.
 */
export class Semaphore {
    private currentCount: number;
    private readonly maxCount: number;
    private readonly waiters: Array<() => void> = [];

    constructor(maxCount: number) {
        this.maxCount = maxCount;
        this.currentCount = maxCount;
    }

    /**
     * Acquire one slot. Resolves when a slot is available.
     * Returns a release function that must be called when done.
     */
    async acquire(): Promise<() => void> {
        if (this.currentCount > 0) {
            this.currentCount--;
            return () => this.release();
        }

        return new Promise<() => void>((resolve) => {
            this.waiters.push(() => {
                this.currentCount--;
                resolve(() => this.release());
            });
        });
    }

    private release(): void {
        this.currentCount++;
        if (this.waiters.length > 0 && this.currentCount > 0) {
            const next = this.waiters.shift()!;
            next();
        }
    }

    get availableCount(): number {
        return this.currentCount;
    }

    get waitingCount(): number {
        return this.waiters.length;
    }
}
