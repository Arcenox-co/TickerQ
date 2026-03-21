import { TickerTaskPriority } from '../enums';

interface QueuedTask {
    work: (signal: AbortSignal) => Promise<void>;
    priority: TickerTaskPriority;
    resolve: () => void;
    reject: (err: unknown) => void;
}

/**
 * Priority-based async task scheduler for TickerQ function execution.
 *
 * Node.js is single-threaded, but we still benefit from:
 * - Priority-ordered execution (High > Normal > Low)
 * - Controlled concurrency (prevents unbounded parallel I/O)
 * - LongRunning tasks run in a separate "lane" (no max concurrency)
 * - Per-function concurrency gates via TickerFunctionConcurrencyGate
 *
 * Default worker concurrency = number of CPUs.
 */
export class TickerQTaskScheduler {
    private readonly maxWorkers: number;
    private activeWorkers = 0;
    private _isFrozen = false;
    private _isDisposed = false;

    /** Separate queues per priority level. */
    private readonly queues: Map<TickerTaskPriority, QueuedTask[]> = new Map([
        [TickerTaskPriority.High, []],
        [TickerTaskPriority.Normal, []],
        [TickerTaskPriority.Low, []],
    ]);

    /** LongRunning tasks bypass the concurrency limit. */
    private longRunningCount = 0;

    /** Track running task promises for graceful shutdown. */
    private readonly runningTasks: Set<Promise<void>> = new Set();

    constructor(maxWorkers?: number) {
        const cpus = typeof require !== 'undefined'
            ? require('os').cpus()?.length ?? 4
            : 4;
        this.maxWorkers = maxWorkers ?? cpus;
    }

    get isFrozen(): boolean {
        return this._isFrozen;
    }

    get isDisposed(): boolean {
        return this._isDisposed;
    }

    get totalActiveWorkers(): number {
        return this.activeWorkers + this.longRunningCount;
    }

    get totalQueuedTasks(): number {
        let total = 0;
        for (const queue of this.queues.values()) {
            total += queue.length;
        }
        return total;
    }

    /**
     * Queue an async task with priority.
     */
    async queueAsync(
        work: (signal: AbortSignal) => Promise<void>,
        priority: TickerTaskPriority,
    ): Promise<void> {
        if (this._isDisposed) {
            throw new Error('TickerQTaskScheduler is disposed.');
        }

        if (this._isFrozen) {
            throw new Error('TickerQTaskScheduler is frozen. Call resume() first.');
        }

        // LongRunning tasks execute immediately without queuing.
        if (priority === TickerTaskPriority.LongRunning) {
            return this.executeLongRunning(work);
        }

        return new Promise<void>((resolve, reject) => {
            const queue = this.queues.get(priority)!;
            queue.push({ work, priority, resolve, reject });
            this.processNext();
        });
    }

    freeze(): void {
        this._isFrozen = true;
    }

    resume(): void {
        this._isFrozen = false;
        this.processNext();
    }

    /**
     * Wait for all running tasks to complete.
     */
    async waitForRunningTasks(timeoutMs?: number): Promise<boolean> {
        if (this.runningTasks.size === 0 && this.totalQueuedTasks === 0) {
            return true;
        }

        const allDone = Promise.all(this.runningTasks).then(() => true);

        if (timeoutMs == null) {
            await allDone;
            return true;
        }

        const timeout = new Promise<boolean>((resolve) =>
            setTimeout(() => resolve(false), timeoutMs),
        );

        return Promise.race([allDone, timeout]);
    }

    dispose(): void {
        this._isDisposed = true;
        this._isFrozen = true;
        // Clear queues and reject pending tasks.
        for (const queue of this.queues.values()) {
            for (const task of queue) {
                task.reject(new Error('TickerQTaskScheduler disposed.'));
            }
            queue.length = 0;
        }
    }

    getDiagnostics(): string {
        const lines: string[] = [
            `Workers: ${this.activeWorkers}/${this.maxWorkers} (LongRunning: ${this.longRunningCount})`,
            `Queued: High=${this.queues.get(TickerTaskPriority.High)!.length} Normal=${this.queues.get(TickerTaskPriority.Normal)!.length} Low=${this.queues.get(TickerTaskPriority.Low)!.length}`,
            `Frozen: ${this._isFrozen} Disposed: ${this._isDisposed}`,
        ];
        return lines.join('\n');
    }

    private processNext(): void {
        if (this._isFrozen || this._isDisposed) return;
        if (this.activeWorkers >= this.maxWorkers) return;

        const task = this.dequeueHighestPriority();
        if (!task) return;

        this.activeWorkers++;
        const ac = new AbortController();

        const taskPromise = task.work(ac.signal)
            .then(() => task.resolve())
            .catch((err) => task.reject(err))
            .finally(() => {
                this.activeWorkers--;
                this.runningTasks.delete(taskPromise);
                this.processNext();
            });

        this.runningTasks.add(taskPromise);

        // Check if we can start more tasks in parallel.
        this.processNext();
    }

    private dequeueHighestPriority(): QueuedTask | null {
        // Priority order: High > Normal > Low
        for (const priority of [TickerTaskPriority.High, TickerTaskPriority.Normal, TickerTaskPriority.Low]) {
            const queue = this.queues.get(priority)!;
            if (queue.length > 0) {
                return queue.shift()!;
            }
        }
        return null;
    }

    private async executeLongRunning(work: (signal: AbortSignal) => Promise<void>): Promise<void> {
        this.longRunningCount++;
        const ac = new AbortController();

        const taskPromise = work(ac.signal).finally(() => {
            this.longRunningCount--;
            this.runningTasks.delete(taskPromise);
        });

        this.runningTasks.add(taskPromise);
        return taskPromise;
    }
}
