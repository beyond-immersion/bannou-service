# Edge Test Results - 2025-12-02

## Test Summary
- **Total Tests**: 17
- **Passed**: 13
- **Failed**: 4

## Failed Tests
1. Orchestrator - Infrastructure Health (WebSocket)
2. Orchestrator - Services Health (WebSocket)
3. Orchestrator - Get Backends (WebSocket)
4. Orchestrator - Get Status (WebSocket)

## Key Diagnostic Findings

### Admin JWT Token Analysis
```json
{
  "session_key": "09dce803da7a44ed838865001692d562",
  "nameid": "199ba1e1-f715-42ea-9073-d2fa42c090dd",
  "sub": "199ba1e1-f715-42ea-9073-d2fa42c090dd",
  "jti": "451788a1-6b2f-4f6a-966d-67c297a55188",
  "iat": 1764656851,
  "iss": "bannou-auth-ci",
  "aud": "bannou-api-ci"
}
```
- Admin token IS valid and contains correct account ID
- Session key maps to admin account

### Capability Manifest Analysis
- **Available APIs**: 9 (all from accounts service)
- **Missing**: ALL orchestrator endpoints

**Available endpoints in manifest**:
- GET:/accounts (accounts)
- POST:/accounts (accounts)
- GET:/accounts/{accountId} (accounts)
- PUT:/accounts/{accountId} (accounts)
- DELETE:/accounts/{accountId} (accounts)
- GET:/accounts/{accountId}/auth-methods (accounts)
- POST:/accounts/{accountId}/auth-methods (accounts)
- DELETE:/accounts/{accountId}/auth-methods/{methodId} (accounts)
- PUT:/accounts/{accountId}/profile (accounts)

### Root Cause Analysis
The orchestrator service endpoints are NOT appearing in the capability manifest.

**What IS working**:
- Admin account created with admin role (verified by accounts endpoints appearing)
- Admin JWT generated with valid session key
- Accounts service permissions registered correctly
- Permission compilation working for accounts service

**What is NOT working**:
- Orchestrator service permissions NOT registered
- OR orchestrator endpoints not being compiled into admin capabilities

### Investigation Points
1. Is orchestrator service being loaded as a plugin?
2. Is orchestrator RegisterServicePermissionsAsync being called?
3. Is the permission registration event being published?
4. Is the event being received and processed by PermissionsEventsController?
5. Is the orchestrator added to REGISTERED_SERVICES_KEY?
6. Is permissions:orchestrator:authenticated:admin being stored?

## Configuration
```
ORCHESTRATOR_SERVICE_ENABLED=true
BANNOU_ADMINEMAILDOMAIN=@admin.test.local
ADMIN_USERNAME=admin@admin.test.local
```
