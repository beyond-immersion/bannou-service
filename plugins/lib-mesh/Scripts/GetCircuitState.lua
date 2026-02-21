-- GetCircuitState.lua
-- Gets the current circuit breaker state, auto-transitioning Open→HalfOpen if timeout elapsed.
--
-- KEYS[1] = circuit state key (e.g., "mesh:cb:{appId}")
-- ARGV[1] = current timestamp in milliseconds
-- ARGV[2] = reset timeout in milliseconds
--
-- Returns JSON: {"state": "Closed|Open|HalfOpen", "failures": N, "stateChanged": true|false, "openedAt": timestamp|null}
--
-- State is stored as a Redis hash with fields:
--   - failures: consecutive failure count (integer)
--   - state: "Closed", "Open", or "HalfOpen" (string)
--   - openedAt: timestamp when circuit opened (milliseconds, or empty if not Open)

local key = KEYS[1]
local nowMs = tonumber(ARGV[1])
local resetTimeoutMs = tonumber(ARGV[2])

-- Get current state
local currentState = redis.call('HGET', key, 'state') or 'Closed'
local failures = tonumber(redis.call('HGET', key, 'failures') or '0')
local openedAt = redis.call('HGET', key, 'openedAt')

local stateChanged = false
local newState = currentState

-- If currently Open, check if we should transition to HalfOpen
if currentState == 'Open' and openedAt and openedAt ~= '' then
    local openedAtMs = tonumber(openedAt)
    if openedAtMs and (nowMs - openedAtMs >= resetTimeoutMs) then
        -- Time to allow a probe request - transition to HalfOpen
        newState = 'HalfOpen'
        stateChanged = true
        redis.call('HSET', key, 'state', newState)
    end
end

-- Build JSON response (T26 compliant: openedAt is null when not applicable)
local openedAtJson = 'null'
if (newState == 'Open' or newState == 'HalfOpen') and openedAt and openedAt ~= '' then
    openedAtJson = openedAt
end

return string.format(
    '{"state":"%s","failures":%d,"stateChanged":%s,"openedAt":%s}',
    newState,
    failures,
    stateChanged and 'true' or 'false',
    openedAtJson
)
