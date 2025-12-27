# Remaining Asset Plugin Work: Edge Tests and Performance Validation

**Status**: ~5% remaining from original Asset Management Plugin plan
**Prerequisite**: Asset service is fully implemented and HTTP tests pass
**Last Updated**: 2025-12-27

---

## Summary

The Asset Management Plugin is **~95% complete**. All core functionality is implemented:
- Pre-signed URL upload/download flows
- MinIO storage with IAssetStorageProvider abstraction (lib-state pattern)
- Multipart uploads for large files
- Asset versioning, search, and metadata management
- .bannou bundle format with LZ4 compression
- Processing pipeline (texture, model, audio processors)
- Client events via IClientEventPublisher (lib-messaging pattern)
- Prometheus-compatible metrics and health checks
- NGINX/Lua token validation for direct storage access
- HTTP integration tests (11 test methods)
- Developer documentation (docs/guides/ASSET_SERVICE.md)

**Remaining work**: Edge tests for WebSocket event delivery and optional performance validation.

---

## 1. Edge Tests for Asset Events

### Task 1.1: Create AssetEventTests.cs

**What**: WebSocket tests verifying asset events are delivered to clients via the Connect service.

**Why**: The AssetEventEmitter publishes events via IClientEventPublisher to RabbitMQ pub/sub. Connect service should forward these to the client's WebSocket. This flow is untested.

**Files to create**:
- `edge-tester/Tests/AssetEventTests.cs`

**Implementation approach**:

```csharp
public class AssetEventTests : BaseEdgeTestHandler
{
    /// <summary>
    /// Test that upload completion event is received via WebSocket.
    /// </summary>
    public async Task TestUploadCompleteEvent()
    {
        // 1. Connect to WebSocket and authenticate
        // 2. Request upload URL via WebSocket API
        // 3. Upload file to MinIO pre-signed URL
        // 4. Call CompleteUpload via WebSocket
        // 5. Wait for AssetUploadCompleteEvent on WebSocket
        // 6. Verify event contains correct asset_id, content_hash, size
    }

    /// <summary>
    /// Test that asset ready event is received after processing.
    /// </summary>
    public async Task TestAssetReadyEvent()
    {
        // Similar flow, but wait for AssetReadyEvent
        // Note: Non-processable content types (application/json) skip processing
        // Use image/png with small file to test full flow
    }

    /// <summary>
    /// Test bundle creation complete event.
    /// </summary>
    public async Task TestBundleCreationEvent()
    {
        // 1. Upload multiple test assets
        // 2. Create bundle via WebSocket
        // 3. Wait for BundleCreationCompleteEvent
        // 4. Verify download_url, asset_count
    }
}
```

**Test Matrix**:
| Event | Trigger | Expected Fields |
|-------|---------|-----------------|
| `asset.upload.complete` | CompleteUpload API | upload_id, success, asset_id, content_hash |
| `asset.processing.complete` | Processing pipeline | asset_id, processing_type, outputs |
| `asset.ready` | After processing | asset_id, version_id, content_hash, size |
| `asset.bundle.creation.complete` | CreateBundle API | bundle_id, download_url, asset_count |

**Acceptance criteria**:
- [ ] `make test-edge` includes asset event tests
- [ ] All 4 event types tested for successful receipt
- [ ] Error events tested (upload failure, processing failure)

**Estimated effort**: 2-4 hours

---

## 2. Performance Testing (Optional)

### Task 2.1: Performance Baseline Validation

**What**: Validate asset service meets performance targets under load.

**Targets** (from original plan):
- 100 concurrent uploads
- 1000 concurrent downloads
- 50MB average file size
- < 200ms latency for URL generation

**Approach options**:

1. **k6 Load Testing** (Recommended)
   - Create k6 script for upload/download flows
   - Run against local Docker Compose environment
   - Measure p50, p95, p99 latencies

2. **Simple Benchmark Script**
   - C# console app using `HttpClient`
   - Parallel.ForEachAsync for concurrent operations
   - Measure and report timing statistics

**Files to create** (if implemented):
- `benchmarks/asset-load-test.js` (k6 script)
- OR `benchmarks/AssetBenchmark/Program.cs` (.NET benchmark)

**Acceptance criteria** (if implemented):
- [ ] URL generation p95 < 200ms at 100 concurrent requests
- [ ] No errors under sustained load
- [ ] MinIO handles 1000 concurrent download URLs

**Estimated effort**: 4-8 hours (optional)

---

## Definition of Done

This planning document is complete when:
- [ ] `edge-tester/Tests/AssetEventTests.cs` created and passing
- [ ] All 4 event types tested (upload, processing, ready, bundle)
- [ ] `make test-edge` passes with new asset tests
- [ ] (Optional) Performance baselines established

---

## Notes

### Already Complete (Do Not Duplicate)

The following are already implemented - do not recreate:
- HTTP integration tests in `http-tester/Tests/AssetTestHandler.cs`
- Unit tests in `lib-asset.tests/`
- All AssetService business logic
- IAssetEventEmitter and AssetEventEmitter
- AssetMetrics and AssetHealthChecks
- NGINX Lua scripts for token validation
- Developer documentation

### Infrastructure Context

The asset service uses the standard Bannou infrastructure patterns:
- **lib-state**: For asset metadata and upload session storage
- **lib-messaging**: For event publishing (IClientEventPublisher)
- **lib-mesh**: For service-to-service calls (not directly used by asset service)
