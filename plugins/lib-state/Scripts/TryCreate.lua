-- TryCreate.lua
-- Atomically creates a JSON document if it doesn't already exist.
--
-- KEYS[1] = fullKey (JSON document key)
-- KEYS[2] = metaKey (metadata hash key)
-- ARGV[1] = JSON value to store
-- ARGV[2] = current timestamp (milliseconds)
--
-- Returns:
--   1  = success (document created)
--   -1 = failure (document already exists)

if redis.call('EXISTS', KEYS[1]) == 1 then
    return -1
end

redis.call('JSON.SET', KEYS[1], '$', ARGV[1])
redis.call('HSET', KEYS[2], 'version', 1, 'created', ARGV[2], 'updated', ARGV[2])

return 1
