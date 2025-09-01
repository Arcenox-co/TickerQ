# Troubleshooting

Common issues and fast resolutions.

## Dashboard Issues
| Problem | Cause | Fix |
|---------|-------|-----|
| 403 / unauthorized | Missing / misconfigured Basic Auth | Ensure `TickerQ:BasicAuth` section present |
| Blank job list | Build not run / source gen failed | Rebuild solution; verify attribute namespace |
| Stale metrics | Hub blocked | Check browser console / server logs for SignalR errors |

## Migrations / Persistence
| Problem | Cause | Fix |
|---------|-------|-----|
| Migration fails (referential action) | EF generated SetNull not supported | Edit migration to `ReferentialAction.NoAction` |
| Timer ticker not persisted | Current limitation / bug | Track issue; verify with latest version |
| Rows not affected error | Concurrency / mismatch | Retry; ensure single migrator instance |

## Scheduling
| Problem | Cause | Fix |
|---------|-------|-----|
| Cron not firing | Invalid cron | Use 5-field standard; test with online parser |
| Time job missed after restart | Missed scan delay too high | Lower via `UpdateMissedJobCheckDelay` |
| Job executes twice | Multiple nodes same instance id | Set unique `SetInstanceIdentifier` per node |

## Execution
| Problem | Cause | Fix |
|---------|-------|-----|
| High latency | Under-provisioned threads | Increase `SetMaxConcurrency` |
| Starvation of low tasks | Many High tasks | Rebalance priorities / adjust frequency |
| Unhandled exception kills job | No handler | Implement `ITickerExceptionHandler` |

## Payload / Context
| Problem | Cause | Fix |
|---------|-------|-----|
| Null request payload | Not supplied or wrong type | Ensure correct serialization type registered |
| Cannot cancel job | Wrong ID | Verify tickerId from dashboard / logs |

## Diagnostic Checklist
- Version alignment (all TickerQ packages same version)
- Database reachable & migrated
- Attribute namespace: `TickerQ.Utilities.Base` imported
- Logs: enable detailed logging in development
- Verify single migration runner in CI/CD

## Getting Help
- Search existing issues
- Open new issue with: version, repro steps, logs, environment
- Join community chat (see main README badge)

Improve this page via PRs.
