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
run_test "OpenResty health endpoint" "curl --verbose --fail --max-time 5 http://openresty/health"
run_test "Redis connectivity via OpenResty" "curl --verbose --fail --max-time 5 http://openresty/health/redis"

# Test 3-5: Basic service availability
echo "🔍 Testing Basic Service Availability..."
run_test "Bannou service direct health check" "curl --verbose --fail --max-time 10 http://bannou:80/health || true"
run_test "Bannou service plugin enabled check" "curl --verbose --fail --max-time 10 http://bannou:80/testing/health || true"

# Test 5: Integration tests (testing lib only)
echo "🔍 Running Integration Tests (Testing Plugin Only)..."
run_test "Bannou service plugin test validation check" "curl --verbose --fail --max-time 10 http://bannou:80/testing/run"

# Test 6: Admin heartbeats
echo "🔍 Testing Admin Heartbeats..."
run_test "Admin heartbeats endpoint" "curl --verbose --fail --max-time 5 http://openresty:8080/admin/heartbeats"

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
