# Dapr Sidecar Restart Analysis
**Date:** 2025-10-04
**Issue:** HTTP tests failing due to RabbitMQ connection refused during Dapr initialization
**Implemented Solution:** `restart: always` policy on Dapr sidecars
**Status:** ⚠️ PROBLEMATIC - Violates production resilience requirements

## Executive Summary

The implemented solution (using `restart: always` on Dapr sidecars) successfully allows tests to pass by restarting Dapr containers when RabbitMQ isn't immediately available. However, **this approach directly contradicts the stated requirement that "containers need to be resilient, not restart when connections fail."**

This analysis examines:
1. What actually happens when Dapr sidecars restart
2. The impact on services that depend on them
3. Dapr's actual component resilience capabilities
4. Alternative approaches and their viability
5. Recommended path forward

## Critical Findings

### 1. Dapr Component Initialization Behavior

**Current Reality:**
- Dapr **exits gracefully** when component initialization fails (default `initTimeout: 5s`)
- There is **NO built-in retry mechanism** for component initialization
- Resiliency policies **do NOT apply** to component initialization - only to runtime operations
- Setting `ignoreErrors: true` allows Dapr to start without the component, but the component is **completely unavailable** (not retried later)

**Source Evidence:**
```
Failed to init component bannou-pubsub: [INIT_COMPONENT_FAILURE]:
initialization error occurred for bannou-pubsub (pubsub.rabbitmq/v1):
dial tcp 172.23.0.5:5672: connect: connection refused

time="..." level=warning msg="Error processing component, daprd process will exit gracefully"
time="..." level=fatal msg="Fatal error from runtime: process component bannou-pubsub error..."
```

From GitHub Issue #8056: "when Dapr fails to initialize a component, it appears to exit gracefully...after the initialization timeout, Dapr shuts down the runtime and exits with a 'Fatal error from runtime' message."

### 2. Runtime Reconnection vs. Initialization

**Two Distinct Behaviors:**

| Scenario | Dapr Behavior | Impact |
|----------|---------------|---------|
| **Initial Connection Fails** | Exits with fatal error | Container restart required |
| **Runtime Connection Lost** | Attempts reconnection every 3s | Service continues (with caveats) |

**Critical Gap:** The runtime reconnection logic **only works AFTER successful initial connection**. If RabbitMQ/Redis aren't available at startup, Dapr cannot reach the runtime reconnection phase.

**Additional Runtime Issue (GitHub #4143):** Even runtime reconnection has problems:
> "dapr seems to hiccup when the server closes the channel, and is left in a state of not being able to reconnect."
> Error: "channel not initialized"

### 3. Impact of Dapr Sidecar Restarts

#### On Application Services (bannou, bannou-http-tester)

**Network Architecture:**
```yaml
# bannou-dapr uses network_mode: "service:bannou"
# This means:
# - bannou OWNS the network namespace
# - bannou-dapr SHARES bannou's network namespace
# - If bannou-dapr restarts, bannou is NOT directly affected
# - If bannou restarts, bannou-dapr LOSES network connectivity
```

**Restart Impact Analysis:**

1. **When Dapr sidecar restarts (current solution):**
   - ✅ Application container (bannou) continues running
   - ✅ Network namespace remains intact (bannou owns it)
   - ❌ All Dapr API calls from application **FAIL** during restart (connection refused)
   - ❌ Application must handle Dapr unavailability (retry logic required)
   - ⏱️ Restart takes ~2-5 seconds typically

2. **When main application restarts:**
   - ❌ Dapr sidecar **LOSES network connectivity** entirely (GitHub docker/compose#10263)
   - ❌ Only solution: **manually restart Dapr sidecar**
   - ❌ Creates cascading failure scenario

#### On Dependent Infrastructure

**Current Dependencies:**
- **RabbitMQ:** pub/sub component (bannou-pubsub)
- **Redis (bannou-redis):** state store component (statestore)
- **Redis (auth-redis):** permissions state store (permissions-store)
- **MySQL:** account database (via Entity Framework, not Dapr component)

**Question:** What happens if Redis connections fail during runtime?

**Answer:** Dapr has runtime reconnection for Redis state stores (similar to RabbitMQ), BUT:
- If Redis is unavailable at **startup**, Dapr exits (same initialization problem)
- If Redis fails during **runtime**, Dapr attempts reconnection
- State operations **FAIL** until reconnection succeeds
- No circuit breaker or fallback - operations simply error

### 4. Production Viability Assessment

#### Current Solution (restart: always)

**Pros:**
- ✅ Works for test environment
- ✅ Eventually achieves stable state
- ✅ Simple to implement

**Cons:**
- ❌ **Violates stated resilience requirement:** "containers need to be resilient, not restart"
- ❌ **Service disruption during restart:** All Dapr API calls fail for 2-5 seconds
- ❌ **Cascading failures:** If main service restarts, Dapr loses network (requires manual intervention)
- ❌ **Not production-grade:** Restarting sidecars on connection issues is an anti-pattern
- ❌ **Multiple dependencies at risk:** RabbitMQ + 2 Redis instances = 3 potential restart triggers
- ❌ **No graceful degradation:** All-or-nothing approach

#### Alternative: ignoreErrors: true

**Implementation:**
```yaml
spec:
  ignoreErrors: true  # Dapr starts without the component
  initTimeout: 60s
```

**Pros:**
- ✅ Dapr doesn't exit on initialization failure
- ✅ No sidecar restarts

**Cons:**
- ❌ Component is **completely unavailable** (not registered)
- ❌ **No retry mechanism** - component never initializes
- ❌ Application must handle missing component **permanently**
- ❌ Worse than restarting: at least restart eventually works

**Dapr Documentation (GitHub #3175):**
> "sidecar logged a connection refused error but did not exit...Kubernetes to not be aware of the error since all pods conditions were Ready, and the error was only discovered when the MQTT message pipeline did not work properly"

**Production Warning:** Silent failures are worse than visible failures.

#### Alternative: Increase initTimeout

**Implementation:**
```yaml
spec:
  initTimeout: 60s  # Wait 60 seconds instead of 5s
```

**Pros:**
- ✅ Gives infrastructure more time to start
- ✅ No code changes required

**Cons:**
- ❌ Just delays the failure (doesn't retry)
- ❌ Doesn't solve the fundamental problem
- ❌ Slower startup in genuinely broken scenarios
- ❌ Single connection attempt over 60s - no exponential backoff

#### Alternative: Application-Level Dapr Wait Logic

**Implementation:**
```csharp
// In application startup
while (!await IsDaprReady() && elapsed < timeout) {
    await Task.Delay(1000);
}
```

**Pros:**
- ✅ Application controls readiness
- ✅ No Dapr modifications needed

**Cons:**
- ❌ Dapr **still exits** - application wait is irrelevant
- ❌ Requires application restart anyway
- ❌ Doesn't prevent sidecar restarts

### 5. What Dapr Actually Provides for Resilience

**Official Resiliency Features:**
- ✅ Runtime operation retries (service invocation, component calls)
- ✅ Circuit breakers for runtime failures
- ✅ Timeouts for runtime operations
- ✅ Runtime component reconnection (after successful init)

**NOT Provided:**
- ❌ Component initialization retries
- ❌ Lazy component initialization
- ❌ Component availability polling
- ❌ Graceful degradation without components

**Dapr's Official Production Guidance:**
- Production guidelines do NOT address component initialization failures
- No documented pattern for handling unavailable dependencies at startup
- Community relies on Kubernetes restart policies (same as our docker restart)

### 6. Why This Is Different From Production Kubernetes

**Kubernetes Pattern:**
```yaml
# What production Kubernetes does:
restartPolicy: Always  # Kubernetes restarts pods
livenessProbe: ...     # Kubernetes detects failures
readinessProbe: ...    # Kubernetes waits for ready
```

**What Kubernetes Provides That We're Missing:**
1. **Health probes** - Kubernetes knows when pod is ready
2. **Service mesh** - Traffic doesn't route to non-ready pods
3. **Rolling updates** - Zero-downtime deployments
4. **Pod disruption budgets** - Controlled restart scheduling

**Our Docker Compose Reality:**
1. ❌ No health-aware routing
2. ❌ No traffic management during restarts
3. ❌ No controlled restart coordination
4. ❌ Direct service-to-service calls fail immediately

**The Gap:** Kubernetes restarts are **transparent** to clients. Docker restarts are **visible failures**.

## The Fundamental Problem

### Root Cause Analysis

**The Issue Isn't:** Dapr being fragile
**The Issue IS:** Dapr's architecture assumes infrastructure availability at startup

**Design Assumption:**
- Dapr was designed for Kubernetes environments
- Kubernetes ensures dependency ordering (init containers, readiness probes)
- Dapr expects infrastructure (Redis, RabbitMQ) to be available before pods start
- `depends_on` in Docker Compose was meant to provide this guarantee

**Why depends_on Is Inadequate:**
```yaml
depends_on:
  rabbitmq:
    condition: service_healthy  # RabbitMQ process is running
    # But this doesn't mean:
    # - RabbitMQ is accepting connections
    # - Network is stable
    # - RabbitMQ is fully initialized
```

**The User's Requirement:**
> "containers need to be way more resilient than that. depends_on isn't a pattern that can be used in production, so it's a useless pattern"

**Translation:** Services must handle infrastructure unavailability gracefully **without external orchestration**.

### Why Current Solution Fails This Requirement

1. **Restarts are visible disruptions:** Services experience 2-5 second outages
2. **Cascading failures:** Main service restart breaks Dapr sidecar network
3. **Multiple failure points:** RabbitMQ + 2 Redis + MySQL = 4 restart triggers
4. **No graceful degradation:** Either everything works or nothing works
5. **Anti-pattern for production:** Restart-dependent resilience is not resilience

## Recommended Solutions (In Priority Order)

### Option 1: Remove Dapr Dependency on RabbitMQ/Redis at Initialization ⭐ RECOMMENDED

**Approach:** Make RabbitMQ and Redis **runtime-only** dependencies, not initialization dependencies.

**Implementation:**
1. Remove RabbitMQ pub/sub component from Dapr initialization
2. Remove Redis state stores from Dapr initialization
3. Implement **lazy initialization** - initialize components on first use
4. Application handles "component not available" gracefully

**Pros:**
- ✅ Dapr starts regardless of infrastructure state
- ✅ No sidecar restarts required
- ✅ Graceful degradation possible
- ✅ Aligns with production resilience requirement

**Cons:**
- ❌ Requires application code changes
- ❌ More complex error handling in application
- ❌ Dapr doesn't natively support lazy component init

**Feasibility:** ⚠️ **MEDIUM** - Requires bypassing Dapr's component system

### Option 2: Accept Restart Pattern as Temporary Solution ⚠️

**Approach:** Keep `restart: always` but acknowledge this is NOT production-ready.

**Requirements:**
1. Document as **TECHNICAL DEBT**
2. Must be replaced before production deployment
3. Only viable for local development/testing
4. Add circuit breakers in application code
5. Implement retry logic for all Dapr calls

**Pros:**
- ✅ Tests work now
- ✅ Development can continue
- ✅ Buys time for proper solution

**Cons:**
- ❌ Not production-viable
- ❌ Creates false confidence
- ❌ Technical debt must be paid

**Feasibility:** ✅ **HIGH** - Already implemented

### Option 3: Move Away From Dapr Component System

**Approach:** Use Dapr for service invocation only, not for infrastructure abstraction.

**Implementation:**
1. Remove Dapr state stores - use direct Redis clients
2. Remove Dapr pub/sub - use direct RabbitMQ clients
3. Keep Dapr for service-to-service communication only
4. Implement proper resilience patterns in application code

**Pros:**
- ✅ Full control over connection handling
- ✅ Proper retry/circuit breaker implementation
- ✅ No initialization dependency
- ✅ Standard patterns (Polly, custom retry logic)

**Cons:**
- ❌ Loses Dapr's infrastructure abstraction benefits
- ❌ More code to maintain
- ❌ Defeats purpose of using Dapr

**Feasibility:** ⚠️ **MEDIUM** - Major architectural change

### Option 4: Implement Init Container Pattern in Docker Compose

**Approach:** Mimic Kubernetes init containers using service dependencies.

**Implementation:**
```yaml
rabbitmq-wait:
  image: busybox
  command: ["sh", "-c", "until nc -z rabbitmq 5672; do sleep 1; done"]
  depends_on:
    - rabbitmq

bannou-dapr:
  depends_on:
    rabbitmq-wait:
      condition: service_completed_successfully
```

**Pros:**
- ✅ Dapr starts only when infrastructure ready
- ✅ No application changes required
- ✅ No sidecar restarts

**Cons:**
- ❌ Still uses `depends_on` (user rejected this pattern)
- ❌ Doesn't handle runtime connection loss
- ❌ Not production-viable per user requirement

**Feasibility:** ❌ **LOW** - Violates stated requirements

## Impact Assessment: What We're Currently Risking

### Test Environment
- ✅ **Works:** Tests pass, RabbitMQ issue resolved
- ⚠️ **Hidden issues:** False confidence in stability
- ❌ **Reality check:** Production will fail differently

### Production Environment (if deployed as-is)

**Scenario 1: RabbitMQ temporary network blip**
1. Dapr loses RabbitMQ connection
2. Runtime reconnection attempts start
3. If reconnection fails → Dapr sidecar restarts
4. All service API calls fail for 2-5 seconds
5. **Impact:** Brief service disruption, requests timeout

**Scenario 2: Redis instance restart**
1. Redis goes down for maintenance
2. Dapr initialization fails (if Dapr restarting)
3. Sidecar restart loop begins
4. **Impact:** Extended outage until Redis available

**Scenario 3: Service deployment/update**
1. Main service container updates
2. Service restarts
3. Dapr sidecar loses network namespace
4. Dapr must be manually restarted
5. **Impact:** Deployment requires manual intervention

**Scenario 4: Multiple infrastructure issues**
1. RabbitMQ + Redis + MySQL all experience issues
2. Dapr restart triggered by first failure
3. Dapr fails to initialize due to second failure
4. Restart loop continues
5. **Impact:** Service completely unavailable

## Conclusion and Recommendations

### Current State: ❌ NOT PRODUCTION READY

The `restart: always` solution works for testing but **fundamentally fails** the requirement for production resilience:

1. **Violates stated requirement:** "containers need to be resilient, not restart"
2. **Creates service disruptions:** 2-5 second outages per restart
3. **Risk of cascading failures:** Multiple restart triggers
4. **No graceful degradation:** All-or-nothing approach

### Immediate Action Required

**For Testing (Short-term):**
- ✅ Keep current solution (`restart: always`)
- ✅ Document as **TECHNICAL DEBT** immediately
- ✅ Add prominent warning comments in docker-compose files
- ✅ Create ticket for production-ready solution

**For Production (Before Deployment):**
- 🎯 **MUST** implement Option 1 (Lazy initialization) OR Option 3 (Direct clients)
- 🎯 **MUST** add circuit breakers and retry logic in application
- 🎯 **MUST** implement proper health checks and observability
- 🎯 **MUST** test with chaos engineering (random infrastructure failures)

### The Hard Truth

**Dapr's Component System Is Not Designed For This Use Case**

Dapr assumes:
- Infrastructure is available before services start (Kubernetes guarantee)
- Restarts are orchestrated and transparent (Kubernetes feature)
- Health probes prevent traffic to non-ready pods (Kubernetes routing)

**We Don't Have These Guarantees In Docker Compose**

Our options:
1. Change how we use Dapr (remove component dependencies)
2. Accept restart pattern as non-production stopgap
3. Move to Kubernetes (gets us proper orchestration)
4. Move away from Dapr's component system entirely

**Recommendation:** Implement Option 2 (accept restart pattern) for immediate testing needs, then implement Option 1 or 3 for production.

The current solution is **functionally correct but architecturally wrong**. It solves the immediate problem while creating a larger future problem.

## References

- [Dapr Issue #4143: RabbitMQ Reconnection Problems](https://github.com/dapr/dapr/issues/4143)
- [Dapr Issue #8056: Redis Init Timeout](https://github.com/dapr/dapr/issues/8056)
- [Dapr Issue #3175: ignoreErrors Silent Failures](https://github.com/dapr/dapr/issues/3175)
- [Docker Compose Issue #10263: network_mode Restart Behavior](https://github.com/docker/compose/issues/10263)
- [Dapr Component Schema Documentation](https://docs.dapr.io/reference/resource-specs/component-schema/)
- [Dapr Resiliency Overview](https://docs.dapr.io/operations/resiliency/resiliency-overview/)
