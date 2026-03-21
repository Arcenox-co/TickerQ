export enum RunCondition {
    OnSuccess = 0,
    OnFailure = 1,
    OnCancelled = 2,
    OnFailureOrCancelled = 3,
    OnAnyCompletedStatus = 4,
    InProgress = 5,
}
