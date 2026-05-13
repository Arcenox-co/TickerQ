import { TickerSdkOptions, TICKERQ_SDK_CONSTANTS } from '../TickerSdkOptions';
import { TickerFunctionProvider } from './TickerFunctionProvider';
import type { SyncNodesAndFunctionsResult } from '../models/SyncNodesAndFunctionsResult';
import { createCredentials, createMetadata, hubProto, normalizeGrpcTarget } from '../grpc/GrpcContracts';

/**
 * Synchronizes registered functions with the TickerQ Hub.
 *
 * On startup, sends all registered functions to Hub over gRPC and receives:
 * - ApplicationUrl (Scheduler endpoint for persistence calls)
 * - WebhookSignature (HMAC key for signing/validating requests)
 */
export class TickerQFunctionSyncService {
    private readonly options: TickerSdkOptions;
    private readonly client: any;

    constructor(options: TickerSdkOptions) {
        this.options = options;
        const HubService = hubProto.tickerq.v1.HubService;
        this.client = new HubService(
            normalizeGrpcTarget(this.options.hubControlUri),
            createCredentials(this.options.hubControlUri, this.options.allowSelfSignedCerts),
            {
                'grpc.max_receive_message_length': 16 * 1024 * 1024,
                'grpc.max_send_message_length': 16 * 1024 * 1024,
            },
        );
    }

    /**
     * Sync all registered functions with the Hub.
     */
    async syncAsync(signal?: AbortSignal): Promise<SyncNodesAndFunctionsResult | null> {
        const functions = TickerFunctionProvider.tickerFunctions;
        const requestInfos = TickerFunctionProvider.tickerFunctionRequestInfos;

        const descriptors: Array<Record<string, unknown>> = [];

        for (const [name, reg] of functions) {
            const requestInfo = requestInfos.get(name);

            descriptors.push({
                functionName: name,
                expression: reg.cronExpression ?? '',
                taskPriority: reg.priority,
                requestType: requestInfo?.requestType ?? '',
                requestExampleJson: requestInfo?.requestExampleJson ?? '',
            });
        }

        const request = {
            nodeName: this.options.nodeName!,
            callbackUrl: '',
            functions: descriptors,
            sdkType: TICKERQ_SDK_CONSTANTS.SdkType,
        };

        const result = await this.callSync(request, signal);

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

    private callSync(request: Record<string, unknown>, signal?: AbortSignal): Promise<SyncNodesAndFunctionsResult> {
        return new Promise((resolve, reject) => {
            const deadline = new Date(Date.now() + this.options.timeoutMs);
            const call = this.client.syncNodesFunctions(
                request,
                createMetadata(this.options.apiKey ?? ''),
                { deadline },
                (error: Error | null, response: SyncNodesAndFunctionsResult) => {
                    if (error) {
                        reject(error);
                        return;
                    }
                    resolve(response);
                },
            );

            if (signal) {
                if (signal.aborted) {
                    call.cancel();
                    reject(new Error('TickerQ SDK: Hub sync aborted.'));
                    return;
                }

                signal.addEventListener('abort', () => call.cancel(), { once: true });
            }
        });
    }
}
