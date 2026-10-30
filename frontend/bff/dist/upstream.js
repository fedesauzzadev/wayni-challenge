/** Proxy fino a los microservicios: pasa status + body tal cual (ProblemDetails incluidos). */
export function createUpstreamClient(config) {
    async function call(url, init = {}, headers = {}) {
        const response = await fetch(url, {
            ...init,
            headers: { "content-type": "application/json", ...headers },
            signal: AbortSignal.timeout(60_000),
        });
        const text = await response.text();
        let body = text;
        try {
            body = text === "" ? null : JSON.parse(text);
        }
        catch {
            // body no-JSON: se devuelve como texto
        }
        return { status: response.status, body };
    }
    return {
        latestImport: () => call(`${config.ingestionApiUrl}/imports/latest`),
        importStatus: (id) => call(`${config.ingestionApiUrl}/imports/${id}`),
        requestImport: (payload) => call(`${config.ingestionApiUrl}/imports`, {
            method: "POST",
            body: JSON.stringify(payload),
        }, { "X-Api-Key": config.uploadApiKey }),
        deudores: (query) => call(`${config.queryApiUrl}/deudores?${query}`),
        entidades: (query) => call(`${config.queryApiUrl}/entidades?${query}`),
        totales: () => call(`${config.queryApiUrl}/totales`),
    };
}
