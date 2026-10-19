interface GroupConnection {
  invoke(method: string, group: string): Promise<unknown>;
}

/** Keeps mounted cron subscriptions across connection startup and reconnects. */
export class CronGroups {
  private subscribers = new Map<string, number>();
  private session: {
    connection: GroupConnection;
    joined: Set<string>;
    pending: Promise<void>;
  } | null = null;

  connect(connection: GroupConnection): Promise<void> {
    this.session = { connection, joined: new Set(), pending: Promise.resolve() };
    return this.sync();
  }

  disconnect(): void {
    this.session = null;
  }

  join(id: string): Promise<void> {
    this.subscribers.set(id, (this.subscribers.get(id) ?? 0) + 1);
    return this.sync();
  }

  leave(id: string): Promise<void> {
    const remaining = (this.subscribers.get(id) ?? 0) - 1;
    if (remaining > 0) this.subscribers.set(id, remaining);
    else this.subscribers.delete(id);
    return this.sync();
  }

  private sync(): Promise<void> {
    const session = this.session;
    if (!session) return Promise.resolve();

    // Serialize joins/leaves so a slow join cannot undo a later unsubscribe.
    const pending = session.pending.then(async () => {
      for (const id of this.subscribers.keys()) {
        if (this.session !== session) return;
        if (session.joined.has(id)) continue;
        await session.connection.invoke("JoinGroup", id);
        session.joined.add(id);
      }
      for (const id of session.joined) {
        if (this.session !== session) return;
        if (this.subscribers.has(id)) continue;
        await session.connection.invoke("LeaveGroup", id);
        session.joined.delete(id);
      }
    });
    // A failed invocation must not prevent a later connection/subscription retry.
    session.pending = pending.catch(() => {});
    return pending;
  }
}
