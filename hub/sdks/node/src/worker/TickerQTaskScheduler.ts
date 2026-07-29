import { TickerTaskPriority } from '../enums';

interface QueuedTask {
    work: (signal: AbortSignal) => Promise<void>;
    priority: TickerTaskPriority;
    controller: AbortController;
    resolve: () => void;
    reject: (err: unknown) => void;
    detachAbort?: () => void;
}

function abortError(message = 'TickerQ scheduled task was aborted.'): Error {
    return Object.assign(new Error(message), { name: 'AbortError' });
}

/** Priority scheduler with per-item cancellation and settlement-safe shutdown. */
export class TickerQTaskScheduler {
    private readonly maxWorkers: number;
    private activeWorkers = 0;
    private _isFrozen = false;
    private _isDisposed = false;
    private readonly queues = new Map<TickerTaskPriority, QueuedTask[]>([
        [TickerTaskPriority.High, []], [TickerTaskPriority.Normal, []], [TickerTaskPriority.Low, []],
    ]);
    private longRunningCount = 0;
    private readonly runningTasks = new Set<Promise<void>>();
    private readonly runningControllers = new Set<AbortController>();
    private stateVersion = 0;
    private stateWaiters = new Set<() => void>();

    constructor(maxWorkers?: number) {
        const cpus = typeof require !== 'undefined' ? require('os').cpus()?.length ?? 4 : 4;
        this.maxWorkers = maxWorkers ?? cpus;
    }
    get isFrozen(): boolean { return this._isFrozen; }
    get isDisposed(): boolean { return this._isDisposed; }
    get totalActiveWorkers(): number { return this.activeWorkers + this.longRunningCount; }
    get totalQueuedTasks(): number { let n = 0; for (const q of this.queues.values()) n += q.length; return n; }

    async queueAsync(work: (signal: AbortSignal) => Promise<void>, priority: TickerTaskPriority, signal?: AbortSignal): Promise<void> {
        if (this._isDisposed) throw new Error('TickerQTaskScheduler is disposed.');
        if (this._isFrozen) throw new Error('TickerQTaskScheduler is frozen. Call resume() first.');
        if (signal?.aborted) throw abortError();
        if (priority === TickerTaskPriority.LongRunning) return this.executeLongRunning(work, signal);
        return new Promise<void>((resolve, reject) => {
            const controller = new AbortController();
            const task: QueuedTask = { work, priority, controller, resolve, reject };
            if (signal) {
                const onAbort = () => {
                    controller.abort();
                    const queue = this.queues.get(priority)!;
                    const index = queue.indexOf(task);
                    if (index >= 0) {
                        queue.splice(index, 1);
                        task.detachAbort?.();
                        reject(abortError());
                        this.signalState();
                    }
                };
                signal.addEventListener('abort', onAbort, { once: true });
                task.detachAbort = () => signal.removeEventListener('abort', onAbort);
            }
            this.queues.get(priority)!.push(task);
            this.signalState();
            this.processNext();
        });
    }

    freeze(): void { this._isFrozen = true; this.signalState(); }
    resume(): void {
        if (this._isDisposed) throw new Error('TickerQTaskScheduler is disposed.');
        this._isFrozen = false; this.processNext(); this.signalState();
    }

    abortAll(): void {
        this.freeze();
        for (const queue of this.queues.values()) {
            for (const task of queue.splice(0)) {
                task.controller.abort(); task.detachAbort?.(); task.reject(abortError('TickerQTaskScheduler is stopping.'));
            }
        }
        for (const controller of this.runningControllers) controller.abort();
        this.signalState();
    }

    async waitForRunningTasks(timeoutMs?: number): Promise<boolean> {
        const deadline = timeoutMs == null ? Number.POSITIVE_INFINITY : Date.now() + Math.max(0, timeoutMs);
        while (this.runningTasks.size !== 0 || this.totalQueuedTasks !== 0) {
            const remaining = deadline - Date.now();
            if (remaining <= 0) return false;
            const version = this.stateVersion;
            const changed = new Promise<void>(resolve => {
                const waiter = () => { this.stateWaiters.delete(waiter); resolve(); };
                this.stateWaiters.add(waiter);
                if (this.stateVersion !== version) waiter();
                else if (Number.isFinite(remaining)) setTimeout(waiter, remaining).unref?.();
            });
            await changed;
        }
        return true;
    }

    dispose(): void {
        if (this.runningTasks.size !== 0) throw new Error('Cannot dispose TickerQTaskScheduler while work is still running.');
        this._isDisposed = true; this.abortAll();
    }

    getDiagnostics(): string {
        return `Workers: ${this.activeWorkers}/${this.maxWorkers} (LongRunning: ${this.longRunningCount})\n` +
            `Queued: High=${this.queues.get(TickerTaskPriority.High)!.length} Normal=${this.queues.get(TickerTaskPriority.Normal)!.length} Low=${this.queues.get(TickerTaskPriority.Low)!.length}\n` +
            `Frozen: ${this._isFrozen} Disposed: ${this._isDisposed}`;
    }

    private signalState(): void { this.stateVersion++; for (const waiter of [...this.stateWaiters]) waiter(); }
    private processNext(): void {
        if (this._isFrozen || this._isDisposed || this.activeWorkers >= this.maxWorkers) return;
        const task = this.dequeueHighestPriority();
        if (!task) return;
        task.detachAbort?.();
        if (task.controller.signal.aborted) { task.reject(abortError()); this.signalState(); this.processNext(); return; }
        this.activeWorkers++; this.runningControllers.add(task.controller); this.signalState();
        let taskPromise!: Promise<void>;
        taskPromise = Promise.resolve().then(() => task.work(task.controller.signal))
            .then(task.resolve, task.reject)
            .finally(() => {
                this.activeWorkers--; this.runningControllers.delete(task.controller);
                this.runningTasks.delete(taskPromise); this.signalState(); this.processNext();
            });
        this.runningTasks.add(taskPromise);
        this.processNext();
    }
    private dequeueHighestPriority(): QueuedTask | null {
        for (const p of [TickerTaskPriority.High, TickerTaskPriority.Normal, TickerTaskPriority.Low]) {
            const q = this.queues.get(p)!; if (q.length) return q.shift()!;
        }
        return null;
    }
    private async executeLongRunning(work: (signal: AbortSignal) => Promise<void>, signal?: AbortSignal): Promise<void> {
        const controller = new AbortController();
        const onAbort = () => controller.abort();
        signal?.addEventListener('abort', onAbort, { once: true });
        if (signal?.aborted) controller.abort();
        this.longRunningCount++; this.runningControllers.add(controller); this.signalState();
        let taskPromise!: Promise<void>;
        taskPromise = Promise.resolve().then(() => {
            if (controller.signal.aborted) throw abortError();
            return work(controller.signal);
        }).finally(() => {
            signal?.removeEventListener('abort', onAbort); this.longRunningCount--;
            this.runningControllers.delete(controller); this.runningTasks.delete(taskPromise); this.signalState();
        });
        this.runningTasks.add(taskPromise);
        return taskPromise;
    }
}
