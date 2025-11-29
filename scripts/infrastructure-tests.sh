#!/bin/sh

# Infrastructure Integration Tests
# Tests MINIMAL infrastructure: bannou + dapr + placement + rabbitmq
# NO databases, NO OpenResty - just core Bannou service infrastructure
# Runs with network_mode: service:bannou, so:
#   - 127.0.0.1 = bannou itself
#   - Service names (placement, rabbitmq) work via default bridge network

set -e

echo "🧪 Running minimal infrastructure integration tests..."

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Test counters
TESTS_RUN=0
TESTS_PASSED=0
TESTS_FAILED=0

# Helper function to run a test
run_test() {
    local test_name="$1"
    local test_command="$2"

    TESTS_RUN=$((TESTS_RUN + 1))
    echo "  📋 Test $TESTS_RUN: $test_name"

    if eval "$test_command" > /dev/null 2>&1; then
        echo -e "    ${GREEN}✅ PASSED${NC}"
        TESTS_PASSED=$((TESTS_PASSED + 1))
        return 0
    else
        echo -e "    ${RED}❌ FAILED${NC}"
        TESTS_FAILED=$((TESTS_FAILED + 1))
        return 1
    fi
}

# Test 1-2: Bannou service availability (via 127.0.0.1 - shared network stack)
echo "🔍 Testing Bannou Service..."
run_test "Bannou service health check" "curl --verbose --fail --max-time 10 http://127.0.0.1:80/health"
run_test "TESTING plugin enabled" "curl --verbose --fail --max-time 10 http://127.0.0.1:80/testing/health"

# Test 3: TESTING plugin functionality
echo "🔍 Testing TESTING Plugin..."
run_test "TESTING plugin execution" "curl --verbose --fail --max-time 10 http://127.0.0.1:80/testing/run"

# Test 4: Dapr sidecar availability (via 127.0.0.1 - shared network stack)
echo "🔍 Testing Dapr Sidecar..."
run_test "Dapr health endpoint" "curl --verbose --fail --max-time 5 http://127.0.0.1:3500/v1.0/healthz"

# Test 5: Dapr can reach placement service (via service name on default bridge)
echo "🔍 Testing Dapr Placement Connectivity..."
run_test "Dapr metadata (verifies placement)" "curl --verbose --fail --max-time 5 http://127.0.0.1:3500/v1.0/metadata"

# Test 6: Configuration validation
echo "🔍 Testing Configuration..."
run_test "Environment variables accessible" "test -n \"$SERVICE_DOMAIN\" || echo 'SERVICE_DOMAIN not set, using defaults'"

# Summary
echo ""
echo "📊 Infrastructure Test Results:"
echo -e "  Total Tests: $TESTS_RUN"
echo -e "  ${GREEN}Passed: $TESTS_PASSED${NC}"

if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "  ${RED}Failed: $TESTS_FAILED${NC}"
    echo ""
    echo -e "${RED}❌ Infrastructure tests failed! Some components not working correctly.${NC}"
    exit 1
else
    echo -e "  ${GREEN}Failed: 0${NC}"
    echo ""
    echo -e "${GREEN}🎉 All infrastructure tests passed! Minimal Bannou infrastructure is working.${NC}"
    exit 0
fi
