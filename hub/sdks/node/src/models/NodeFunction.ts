import { TickerTaskPriority } from '../enums';

export interface NodeFunction {
    functionName: string;
    requestType: string;
    requestExampleJson: string;
    taskPriority: TickerTaskPriority;
    expression: string;
}
