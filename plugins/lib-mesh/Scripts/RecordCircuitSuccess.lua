-- RecordCircuitSuccess.lua
-- Atomically records a circuit breaker success, resetting to Closed state.
--
-- KEYS[1] = circuit state key (e.g., "mesh:cb:{appId}")
--
-- Returns JSON: {"state": "Closed", "stateChanged": true|false, "previousState": "Closed|Open|HalfOpen"}
--
-- State is stored as a Redis hash with fields:
--   - failures: consecutive failure count (integer)
--   - state: "Closed", "Open", or "HalfOpen" (string)
--   - openedAt: timestamp when circuit opened (milliseconds, or empty if not Open)

local key = KEYS[1]

-- Get current state
local currentState = redis.call('HGET', key, 'state') or 'Closed'
local stateChanged = false

-- If not already Closed, transition to Closed
if currentState ~= 'Closed' then
    stateChanged = true
    -- Clear all circuit state: reset failures, state to Closed, remove openedAt
    redis.call('HMSET', key, 'state', 'Closed', 'failures', '0', 'openedAt', '')
else
    -- Even if already Closed, reset failures on success
    redis.call('HSET', key, 'failures', '0')
end

return string.format(
    '{"state":"Closed","stateChanged":%s,"previousState":"%s"}',
    stateChanged and 'true' or 'false',
    currentState
)
