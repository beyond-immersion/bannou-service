-- TryUpdate.lua
-- Atomically updates a JSON document with optimistic concurrency control.
-- Checks that the version matches before updating.
--
-- KEYS[1] = fullKey (JSON document key)
-- KEYS[2] = metaKey (metadata hash key)
-- ARGV[1] = expected version (etag)
-- ARGV[2] = JSON value to store
-- ARGV[3] = current timestamp (milliseconds)
--
-- Returns:
--   new version number = success (document updated)
--   -1 = failure (version mismatch / concurrent modification)

local currentVersion = redis.call('HGET', KEYS[2], 'version')

-- Treat nil/false as version "0" (new document)
if currentVersion == false then
    currentVersion = '0'
end

-- Check version matches expected
if currentVersion ~= ARGV[1] then
    return -1
end

-- Atomically: set JSON, increment version, update timestamp
local newVersion = tonumber(ARGV[1]) + 1
redis.call('JSON.SET', KEYS[1], '$', ARGV[2])
redis.call('HSET', KEYS[2], 'version', newVersion, 'updated', ARGV[3])

return newVersion
