## Release 2.5.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TQ001   | TickerQ.SourceGenerator | Error | Class should be public or internal to be used with [TickerFunction]
TQ002   | TickerQ.SourceGenerator | Error | Method should be public or internal to be used with [TickerFunction]
TQ003   | TickerQ.SourceGenerator | Error | Invalid cron expression
TQ004   | TickerQ.SourceGenerator | Error | Missing function name in [TickerFunction] attribute
TQ005   | TickerQ.SourceGenerator | Error | Duplicate function name across [TickerFunction] methods
TQ006   | TickerQ.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [TickerQConstructor] attribute is specified
TQ007   | TickerQ.SourceGenerator | Error | Abstract class contains [TickerFunction] methods
TQ008   | TickerQ.SourceGenerator | Error | Nested class contains [TickerFunction] methods - only allowed in top-level classes
TQ009   | TickerQ.SourceGenerator | Error | Invalid TickerFunction parameter - only TickerFunctionContext, TickerFunctionContext<T>, CancellationToken, or no parameters allowed
TQ010   | TickerQ.SourceGenerator | Error | Multiple constructors with [TickerQConstructor] attribute - only one constructor can be marked