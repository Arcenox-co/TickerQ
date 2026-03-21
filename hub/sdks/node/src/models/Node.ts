import { NodeFunction } from './NodeFunction';

export interface Node {
    nodeName: string;
    callbackUrl: string;
    isProduction: boolean;
    functions: NodeFunction[];
}
