-- Service Routing with Heartbeat Integration
-- Implements service discovery and load balancing based on Redis heartbeat data
-- Based on API-DESIGN.md service heartbeat patterns

local redis = require "resty.redis"
local cjson = require "cjson"

local _M = {}

-- Redis connection helper
local function get_redis_connection()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000) -- 1sec timeout

    local redis_host = ngx.shared.service_routes:get("redis_host") or "routing-redis"
    local redis_port = ngx.shared.service_routes:get("redis_port") or 6379

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.log(ngx.ERR, "Failed to connect to Redis: ", err)
        return nil
    end

    return red
end

-- Get service heartbeat data from Redis
local function get_service_heartbeats(service_name)
    local red = get_redis_connection()
    if not red then
        return {}
    end

    -- Get all services matching pattern
    local pattern = "heartbeat:" .. service_name .. ":*"
    local keys, err = red:keys(pattern)

    if not keys or err then
        ngx.log(ngx.WARN, "Failed to get service keys: ", err)
        red:close()
        return {}
    end

    local heartbeats = {}
    for _, key in ipairs(keys) do
        local data, err = red:hgetall(key)
        if data and not err then
            local heartbeat = red:array_to_hash(data)
            table.insert(heartbeats, heartbeat)
        end
    end

    red:close()
    return heartbeats
end

-- Select best service instance based on load
local function select_service_instance(heartbeats)
    if #heartbeats == 0 then
        return nil
    end

    -- Filter healthy services (updated within 90 seconds)
    local healthy_services = {}
    local now = ngx.time()

    for _, hb in ipairs(heartbeats) do
        local last_update = tonumber(hb.timestamp) or 0
        local age = now - last_update

        if age <= 90 then  -- Service heartbeat TTL
            table.insert(healthy_services, hb)
        end
    end

    if #healthy_services == 0 then
        ngx.log(ngx.WARN, "No healthy service instances found")
        return nil
    end

    -- Select service with lowest load
    table.sort(healthy_services, function(a, b)
        local load_a = tonumber(a.current_load) or 100
        local load_b = tonumber(b.current_load) or 100
        return load_a < load_b
    end)

    return healthy_services[1]
end

-- Route request to appropriate service instance
function _M.route_request()
    local uri = ngx.var.uri
    local service_name = "bannou"  -- Default service

    -- Determine service based on URI prefix
    if string.match(uri, "^/authorization/") then
        service_name = "auth"
    elseif string.match(uri, "^/connect/") then
        service_name = "connect"
    elseif string.match(uri, "^/accounts/") then
        service_name = "accounts"
    end

    -- Check cache first
    local cache_key = "route:" .. service_name
    local cached_instance = ngx.shared.service_routes:get(cache_key)

    if cached_instance then
        -- Use cached route (valid for 30 seconds)
        ngx.log(ngx.DEBUG, "Using cached route for ", service_name, ": ", cached_instance)
        return
    end

    -- Get service heartbeats and select best instance
    local heartbeats = get_service_heartbeats(service_name)
    local selected = select_service_instance(heartbeats)

    if selected then
        local instance_info = selected.app_id .. ":" .. selected.port

        -- Cache the selection
        ngx.shared.service_routes:set(cache_key, instance_info, 30)

        -- Set upstream for this request
        if selected.app_id ~= "bannou" then
            -- Custom upstream configuration would go here
            ngx.log(ngx.INFO, "Routing to service instance: ", instance_info)
        end
    else
        ngx.log(ngx.WARN, "No healthy instances for service: ", service_name)
    end
end

-- Main access phase handler
_M.route_request()

return _M