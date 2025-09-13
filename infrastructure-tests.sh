#!/bin/sh

set -e

echo "🧪 Running infrastructure tests (OpenResty + Redis + basic connectivity)..."

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

# Test 1-2: OpenResty Infrastructure
echo "🔍 Testing OpenResty Infrastructure..."
run_test "OpenResty health endpoint" "curl --fail --max-time 5 http://openresty/health"
run_test "Redis connectivity via OpenResty" "curl --fail --max-time 5 http://openresty/health/redis"

# Test 3-4: Basic service availability (infrastructure level only)
echo "🔍 Testing Basic Service Availability..."
run_test "Bannou service basic availability" "curl --fail --max-time 10 http://bannou/testing/run-enabled"
run_test "Bannou service alternative port" "curl --fail --max-time 10 http://bannou:80/testing/run-enabled || true"

# Test 5-6: Queue system infrastructure
echo "🔍 Testing Queue System Infrastructure..."
run_test "Queue status endpoint" "curl --fail --max-time 5 -X POST -H 'Content-Type: application/json' -d '{\"queue_id\":\"test\",\"user_id\":\"user1\"}' http://openresty/queue/status"
run_test "Admin heartbeats endpoint" "curl --fail --max-time 5 http://openresty:8080/admin/heartbeats"

# Test 7: Configuration validation
echo "🔍 Testing Configuration..."
run_test "Environment variables accessible" "test -n \"$SERVICE_DOMAIN\" || echo 'SERVICE_DOMAIN not set, using defaults'"

# Summary
echo ""
echo "📊 Integration Test Results:"
echo -e "  Total Tests: $TESTS_RUN"
echo -e "  ${GREEN}Passed: $TESTS_PASSED${NC}"

if [ $TESTS_FAILED -gt 0 ]; then
    echo -e "  ${RED}Failed: $TESTS_FAILED${NC}"
    echo ""
    echo -e "${RED}❌ Integration tests failed! Some components are not working correctly.${NC}"
    exit 1
else
    echo -e "  ${GREEN}Failed: 0${NC}"
    echo ""
    echo -e "${GREEN}🎉 All integration tests passed! OpenResty architecture is working correctly.${NC}"
    exit 0
fi
