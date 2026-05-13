export function toBareFunctionName(functionName: string): string {
    const atIndex = functionName.indexOf('@');
    return atIndex > 0 ? functionName.slice(0, atIndex) : functionName;
}

export function qualifyFunctionName(functionName: string, nodeName: string): string {
    if (!functionName || functionName.includes('@')) {
        return functionName;
    }

    return `${functionName}@${nodeName}`;
}
