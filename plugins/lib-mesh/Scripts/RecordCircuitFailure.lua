-- RecordCircuitFailure.lua
-- Atomically records a circuit breaker failure and transitions to Open state if threshold reached.
--
-- KEYS[1] = circuit state key (e.g., "mesh:cb:{appId}")
-- ARGV[1] = failure threshold (integer)
-- ARGV[2] = current timestamp in milliseconds (for OpenedAt)
-- ARGV[3] = reset timeout in milliseconds (for calculating HalfOpen transition)
--
-- Returns JSON: {"failures": N, "state": "Closed|Open|HalfOpen", "stateChanged": true|false, "openedAt": timestamp|null}
--
-- State is stored as a Redis hash with fields:
--   - failures: consecutive failure count (integer)
--   - state: "Closed", "Open", or "HalfOpen" (string)
--   - openedAt: timestamp when circuit opened (milliseconds, or empty if not Open)

local key = KEYS[1]
local threshold = tonumber(ARGV[1])
local nowMs = tonumber(ARGV[2])
local resetTimeoutMs = tonumber(ARGV[3])

-- Get current state
local currentState = redis.call('HGET', key, 'state') or 'Closed'
local currentFailures = tonumber(redis.call('HGET', key, 'failures') or '0')
local openedAt = redis.call('HGET', key, 'openedAt')

local stateChanged = false
local newState = currentState
local newFailures = currentFailures

-- If currently Open, check if we should transition to HalfOpen
if currentState == 'Open' and openedAt then
    local openedAtMs = tonumber(openedAt)
    if openedAtMs and (nowMs - openedAtMs >= resetTimeoutMs) then
        -- Time to allow a probe request - transition to HalfOpen
        newState = 'HalfOpen'
        stateChanged = true
        redis.call('HSET', key, 'state', newState)
    end
end

-- If HalfOpen, a failure reopens the circuit
if newState == 'HalfOpen' then
    newState = 'Open'
    newFailures = threshold -- Reset to threshold to keep circuit open
    stateChanged = true
    openedAt = tostring(nowMs)
    redis.call('HMSET', key, 'state', newState, 'failures', newFailures, 'openedAt', openedAt)
elseif newState == 'Closed' then
    -- Increment failures
    newFailures = currentFailures + 1
    redis.call('HSET', key, 'failures', newFailures)

    -- Check if threshold reached
    if newFailures >= threshold then
        newState = 'Open'
        stateChanged = true
        openedAt = tostring(nowMs)
        redis.call('HMSET', key, 'state', newState, 'openedAt', openedAt)
    end
end

-- Build JSON response (T26 compliant: openedAt is null when not Open, not empty string)
local openedAtJson = 'null'
if newState == 'Open' and openedAt and openedAt ~= '' then
    openedAtJson = openedAt
end

return string.format(
    '{"failures":%d,"state":"%s","stateChanged":%s,"openedAt":%s}',
    newFailures,
    newState,
    stateChanged and 'true' or 'false',
    openedAtJson
)
