# Why Don't Assets Route Through the WebSocket Gateway Like Everything Else?

> **Short Answer**: Because routing a 500MB texture file through a 31-byte binary header protocol designed for JSON messages would be architecturally insane. Assets use pre-signed URLs so clients upload and download directly to object storage. The WebSocket gateway never touches the bytes.

---

## How Everything Else Works

Bannou's standard communication path is:

```
Client ──WebSocket──> Connect Service ──mesh──> Backend Service
              31-byte binary header
              JSON payload
```

This works beautifully for API calls. The binary header provides zero-copy routing (the Connect service reads 16 bytes for the service GUID and forwards without deserializing the JSON), and the persistent WebSocket connection eliminates per-request overhead. A `character/get` call is a few hundred bytes of JSON wrapped in a 31-byte header. Fast, efficient, elegant.

Now imagine routing a 500MB terrain texture through this path.

---

## Why It Would Be Terrible

### 1. The WebSocket Connection Becomes a Bottleneck

The Connect service maintains one WebSocket connection per client. Every API call, every server push event, every capability manifest update flows through this single connection. If you route a 500MB file through it, that connection is saturated for the duration of the transfer. During that time:
- API calls queue behind the file transfer
- Server push events (capability updates, client events) are delayed
- The game feels unresponsive because the control channel is clogged with bulk data

Separating control plane (WebSocket) from data plane (direct HTTP to storage) means a 500MB download does not block a 200-byte API call.

### 2. The Connect Service Becomes a Proxy

In the standard path, Connect reads 31 bytes, looks up a GUID, and forwards the message. It never examines or copies the JSON payload. Zero-copy routing is what makes Connect efficient enough to handle thousands of concurrent connections.

If Connect has to proxy file transfers, it becomes a multi-gigabyte data relay. Every byte of every asset passes through Connect's memory, consuming bandwidth, CPU, and RAM that should be reserved for real-time message routing. The service designed to be the thinnest possible routing layer becomes the fattest bottleneck in the system.

### 3. The Binary Protocol Is Not Designed for It

The 31-byte binary header includes a 4-byte sequence number and an 8-byte message ID. These are designed for request/response correlation of JSON messages, not for streaming large binary transfers. You would need chunking, reassembly, progress tracking, resume-on-disconnect, and integrity verification -- all bolted onto a protocol designed for small, atomic messages.

Every streaming protocol that handles large file transfers (HTTP range requests, S3 multipart upload, WebRTC data channels) exists because the problem is hard enough to warrant a dedicated solution. Cramming it into a JSON message protocol is solving a hard problem with the wrong tool.

---

## How Asset Storage Actually Works

The Asset service uses **pre-signed URLs** -- short-lived, authenticated URLs that grant direct access to the object storage backend (MinIO/S3) without going through the Bannou service layer.

### Upload Flow

```
Client ──WebSocket──> Asset Service: "I want to upload a texture"
                          │
                          v
                    Generate pre-signed PUT URL
                    (expires in N minutes)
                          │
Client <──WebSocket── Asset Service: "Upload directly to this URL"
                          │
Client ──HTTP PUT──────> MinIO/S3 (direct, no proxy)
                          │
MinIO ──webhook──────> Asset Service: "Upload completed"
                          │
Asset Service ──event──> "asset.upload.completed"
```

The WebSocket carries the small coordination messages (request upload, receive URL, confirm completion). The actual binary data goes directly from the client to the storage backend over HTTP. The Asset service never touches the bytes.

### Download Flow

```
Client ──WebSocket──> Asset Service: "I need texture abc123"
                          │
                          v
                    Generate pre-signed GET URL
                    (expires in N minutes)
                          │
Client <──WebSocket── Asset Service: "Download from this URL"
                          │
Client ──HTTP GET──────> MinIO/S3 (direct, no proxy)
```

Same pattern. The client gets a URL and fetches directly. The WebSocket gateway is never involved in the transfer.

### Why Pre-Signed URLs Are the Right Abstraction

Pre-signed URLs solve every problem simultaneously:
- **Authentication**: The URL itself contains a cryptographic signature. No cookies, no tokens, no headers required. The storage backend validates the signature independently.
- **Expiry**: URLs expire after a configurable duration. A leaked URL is useless after it expires.
- **Direct transfer**: Client talks directly to the storage backend. No proxy, no relay, no middleman adding latency and consuming bandwidth.
- **Resumability**: HTTP supports range requests natively. A failed download can resume where it left off. This is a solved problem in HTTP -- no need to reinvent it over WebSocket.
- **CDN compatibility**: Pre-signed URLs work with CDN edge caches. The storage backend can sit behind CloudFront or another CDN for geographic distribution. You cannot put a WebSocket connection behind a CDN.

---

## The Dual-Endpoint Pattern

The Asset service uses the same MinIO/S3 backend for both internal and external URLs, but with potentially different endpoints:

- **`StorageEndpoint`** (`minio:9000`): The internal endpoint used by the Asset service to manage buckets, list objects, and handle webhooks. This is a Docker-internal hostname.
- **`StoragePublicEndpoint`** (configurable): The public endpoint embedded in pre-signed URLs. In Docker development, this might be `localhost:9000`. In production, it might be a CDN domain or a public-facing S3 endpoint.

This split exists because the client cannot resolve Docker-internal hostnames. The pre-signed URL must point to an endpoint the client can actually reach, which may be different from the endpoint the Asset service uses internally.

---

## What About Bundles?

The Asset service also manages **bundles** -- grouped assets in a custom `.bannou` format with LZ4 compression. Bundles are assembled and stored server-side, but delivered to clients the same way: pre-signed URLs for direct download from object storage.

The bundle creation process happens entirely within the service layer:
1. Client requests bundle creation via WebSocket
2. Asset service fetches individual assets from MinIO, assembles and compresses them
3. Asset service stores the bundle back to MinIO
4. Client receives a pre-signed download URL for the completed bundle

The heavy lifting (fetching, compressing, storing) happens server-to-server between the Asset service and MinIO. The client only deals with the final download -- one HTTP GET for the complete bundle.

---

## The Principle

The WebSocket gateway is a **control plane** -- it routes small, structured messages between clients and services. It is optimized for low latency and high concurrency on small payloads.

Asset storage is a **data plane** -- it moves large binary blobs between clients and persistent storage. It is optimized for throughput, resumability, and CDN distribution.

These are fundamentally different workloads with fundamentally different optimization targets. Running them through the same channel would compromise both. The pre-signed URL pattern keeps them separated with minimal coordination overhead: a few hundred bytes of WebSocket messages to set up, then a direct HTTP transfer for the actual data.
