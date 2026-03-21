import { TickerQSdkHttpClient } from '../client/TickerQSdkHttpClient';
import { TickerSdkOptions } from '../TickerSdkOptions';
import { TickerFunctionProvider } from './TickerFunctionProvider';
import type { Node } from '../models/Node';
import type { NodeFunction } from '../models/NodeFunction';
import type { SyncNodesAndFunctionsResult } from '../models/SyncNodesAndFunctionsResult';

/**
 * Synchronizes registered functions with the TickerQ Hub.
 *
 * On startup, sends all registered functions to Hub and receives:
 * - ApplicationUrl (Scheduler endpoint for persistence calls)
 * - WebhookSignature (HMAC key for signing/validating requests)
 */
export class TickerQFunctionSyncService {
    private readonly client: TickerQSdkHttpClient;
    private readonly options: TickerSdkOptions;

    constructor(client: TickerQSdkHttpClient, options: TickerSdkOptions) {
        this.client = client;
        this.options = options;
    }

    /**
     * Sync all registered functions with the Hub.
     *
     * POST /api/apps/sync/nodes-functions/batch
     */
    async syncAsync(signal?: AbortSignal): Promise<SyncNodesAndFunctionsResult | null> {
        const functions = TickerFunctionProvider.tickerFunctions;
        const requestInfos = TickerFunctionProvider.tickerFunctionRequestInfos;

        const nodeFunctions: NodeFunction[] = [];

        for (const [name, reg] of functions) {
            const requestInfo = requestInfos.get(name);

            const nodeFunction: NodeFunction = {
                functionName: name,
                expression: reg.cronExpression ?? '',
                taskPriority: reg.priority,
                requestType: requestInfo?.requestType ?? '',
                requestExampleJson: requestInfo?.requestExampleJson ?? '',
            };

            nodeFunctions.push(nodeFunction);
        }

        const node: Node = {
            nodeName: this.options.nodeName!,
            callbackUrl: this.options.callbackUri!,
            isProduction: process.env.NODE_ENV === 'production',
            functions: nodeFunctions,
        };

        const result = await this.client.postAsync<Node, SyncNodesAndFunctionsResult>(
            '/api/apps/sync/nodes-functions/batch',
            node,
            signal,
        );

        if (result) {
            if (result.applicationUrl) {
                this.options.apiUri = result.applicationUrl;
            }
            if (result.webhookSignature) {
                this.options.webhookSignature = result.webhookSignature;
            }
        }

        return result;
    }
}
