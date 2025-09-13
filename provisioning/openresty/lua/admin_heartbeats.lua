-- Admin Heartbeats Viewer
-- Displays service heartbeat data for monitoring

local redis = require "resty.redis"
local cjson = require "cjson"

local function get_redis_connection()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000)

    local redis_host = ngx.shared.service_routes:get("redis_host") or "routing-redis"
    local redis_port = ngx.shared.service_routes:get("redis_port") or 6379

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.log(ngx.ERR, "Failed to connect to Redis: ", err)
        return nil
    end

    return red
end

local function get_all_heartbeats()
    local red = get_redis_connection()
    if not red then
        return {error = "Redis connection failed"}
    end

    -- Get all heartbeat keys
    local keys, err = red:keys("heartbeat:*")
    if err then
        red:close()
        return {error = "Failed to get heartbeat keys"}
    end

    local heartbeats = {}
    local now = ngx.time()

    for _, key in ipairs(keys) do
        local data, err = red:hgetall(key)
        if data and not err then
            local heartbeat = red:array_to_hash(data)

            -- Add derived fields
            heartbeat.key = key
            heartbeat.age = now - (tonumber(heartbeat.timestamp) or 0)
            heartbeat.healthy = heartbeat.age <= 90  -- 90 second TTL

            table.insert(heartbeats, heartbeat)
        end
    end

    red:close()

    -- Sort by timestamp (newest first)
    table.sort(heartbeats, function(a, b)
        return (tonumber(a.timestamp) or 0) > (tonumber(b.timestamp) or 0)
    end)

    return {
        heartbeats = heartbeats,
        total_count = #heartbeats,
        healthy_count = #(function()
            local healthy = {}
            for _, hb in ipairs(heartbeats) do
                if hb.healthy then table.insert(healthy, hb) end
            end
            return healthy
        end)(),
        last_updated = now
    }
end

local function handle_admin_heartbeats()
    local result = get_all_heartbeats()

    ngx.status = 200
    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode(result))
end

handle_admin_heartbeats()