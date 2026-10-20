/** Both abort and generation checks are needed: a completed request may ignore cancellation. */
export class LatestRequest {
  private controller: AbortController | null = null;
  private generation = 0;

  begin() {
    this.cancel();
    const generation = this.generation;
    const controller = new AbortController();
    this.controller = controller;
    return { signal: controller.signal, isCurrent: () => this.generation === generation && !controller.signal.aborted };
  }

  cancel() {
    this.generation++;
    this.controller?.abort();
    this.controller = null;
  }
}
