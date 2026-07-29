import { TickerFunctionBuilder, type TickerFunctionContext } from '../src';

const contract = { schema: { type: 'object' } };

new TickerFunctionBuilder('TypedResult')
    .withResult({ ok: false }, contract)
    .handle(async context => {
        context.setResult({ ok: true });
        // @ts-expect-error declared result requires an object with an ok boolean
        context.setResult({ nope: true });
    });

new TickerFunctionBuilder('RequestAndResult')
    .withRequest({ id: '' }, contract)
    .withResult({ count: 0 }, contract)
    .handle(async context => {
        const id: string = context.request.id;
        context.setResult<{ count: number }>({ count: id.length });
        const parent = context.getParentResult<{ count: number }>();
        const maybeCount: number | undefined = parent?.count;
        void maybeCount;
    });

// Existing source remains valid: no request and unconstrained additive result.
const legacy: TickerFunctionContext = {} as TickerFunctionContext;
legacy.setResult('compatible');
