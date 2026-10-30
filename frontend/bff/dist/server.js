import { createApp } from "./app.js";
import { loadConfig } from "./config.js";
import { createRecipientStore } from "./db.js";
import { createLocalUploader } from "./local.js";
import { createS3Uploader } from "./s3.js";
import { createUpstreamClient } from "./upstream.js";
const config = loadConfig();
const app = createApp({
    config,
    upstream: createUpstreamClient(config),
    db: createRecipientStore(config),
    s3Uploader: createS3Uploader(config),
    localUploader: createLocalUploader(config),
});
app.listen(config.port, () => {
    console.log(`[bff] BCRA admin BFF escuchando en http://localhost:${config.port}`);
});
