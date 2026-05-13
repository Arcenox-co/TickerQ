import * as path from 'node:path';
import * as grpc from '@grpc/grpc-js';
import * as protoLoader from '@grpc/proto-loader';

const protoRoot = path.resolve(__dirname, '..', 'protos');

const loaderOptions: protoLoader.Options = {
    keepCase: false,
    longs: String,
    enums: Number,
    defaults: true,
    oneofs: true,
    bytes: Buffer,
};

function loadProto(fileName: string): grpc.GrpcObject {
    const definition = protoLoader.loadSync(path.join(protoRoot, fileName), loaderOptions);
    return grpc.loadPackageDefinition(definition);
}

export const hubProto = loadProto('hub_sync_service.proto') as any;
export const sdkControlProto = loadProto('sdk_control_service.proto') as any;
export const workerProto = loadProto('worker_service.proto') as any;

export function normalizeGrpcTarget(targetUrl: string): string {
    if (!targetUrl.includes('://')) {
        return targetUrl.replace(/\/+$/, '');
    }

    const parsed = new URL(targetUrl);
    const defaultPort = parsed.protocol === 'http:' ? '80' : '443';
    return `${parsed.hostname}:${parsed.port || defaultPort}`;
}

export function createCredentials(targetUrl?: string, allowSelfSignedCerts = false): grpc.ChannelCredentials {
    if (targetUrl?.toLowerCase().startsWith('http://')) {
        return grpc.credentials.createInsecure();
    }

    if (allowSelfSignedCerts) {
        throw new Error(
            'TickerQ SDK: allowSelfSignedCerts no longer disables TLS verification process-wide. ' +
            'Use an http:// scheduler URL for local h2c development or configure the scheduler certificate as trusted.',
        );
    }

    return grpc.credentials.createSsl();
}

export function createMetadata(apiKey: string): grpc.Metadata {
    const metadata = new grpc.Metadata();
    metadata.set('x-api-key', apiKey);
    return metadata;
}
