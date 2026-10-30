import { createReadStream } from "node:fs";
import { createHash } from "node:crypto";
import { PutObjectCommand, S3Client } from "@aws-sdk/client-s3";
export async function sha256File(filePath) {
    const hash = createHash("sha256");
    for await (const chunk of createReadStream(filePath)) {
        hash.update(chunk);
    }
    return hash.digest("hex");
}
/** Sube a S3 (LocalStack en dev) con path-style: el bucket es allow-listed en ingestion. */
export function createS3Uploader(config) {
    const client = new S3Client({
        endpoint: config.s3.endpoint,
        region: config.s3.region,
        forcePathStyle: true,
        credentials: {
            accessKeyId: config.s3.accessKeyId,
            secretAccessKey: config.s3.secretAccessKey,
        },
    });
    return async (filePath, size, key) => {
        await client.send(new PutObjectCommand({
            Bucket: config.s3.bucket,
            Key: key,
            Body: createReadStream(filePath),
            ContentLength: size,
        }));
        return { bucket: config.s3.bucket, key, source: `s3://${config.s3.bucket}/${key}` };
    };
}
/**
 * Key del archivo de deudores: por periodo + hash del contenido. Mismo archivo + mismo
 * periodo → mismo source + mismo etag → el ingestion deduplica por (source, etag, periodo).
 */
export function buildUploadKey(periodo, etag) {
    return `uploads/${periodo}/deudores-${etag.slice(0, 16)}.txt`;
}
/**
 * Key canónico de los archivos de referencia: el ImportJobProcessor del ingestion resuelve
 * Maeent.txt/Nomdeu.txt en el mismo "directorio" del source del job (uploads/<periodo>/).
 */
export function buildReferenceKey(periodo, fileName) {
    if (fileName !== "Maeent.txt" && fileName !== "Nomdeu.txt") {
        throw new Error(`Archivo de referencia no soportado: ${fileName}`);
    }
    return `uploads/${periodo}/${fileName}`;
}
