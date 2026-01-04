-- Service Routing with Dynamic Backend Selection
-- Reads service routing from Redis (written by orchestrator via lib-state)
-- Sets nginx variables for dynamic upstream selection
-- Redis key pattern: orchestrator-routings:{serviceName}

local redis = require "resty.redis"
local cjson = require "cjson"

local _M = {}

-- Redis key patterns - must match what orchestrator writes via lib-state
-- The orchestrator uses store name "orchestrator-routings" which becomes the key prefix
local ROUTING_KEY_PREFIX = "orchestrator-routings:"
local DEFAULT_APP_ID = "bannou"
local DEFAULT_HOST = "bannou"
local DEFAULT_PORT = 80

-- Cache TTL for routing data (seconds)
local CACHE_TTL = 10

-- Service name to routing key mapping for exposed endpoints
local SERVICE_MAPPING = {
    auth = "auth",
    connect = "connect",
    website = "website",
    accounts = "accounts"
}

-- Redis connection helper with connection pooling
local function get_redis_connection()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000) -- 1sec timeout

    local redis_host = ngx.shared.service_routes:get("redis_host") or "routing-redis"
    local redis_port = tonumber(ngx.shared.service_routes:get("redis_port")) or 6379

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.log(ngx.ERR, "Failed to connect to Redis: ", err)
        return nil
    end

    return red
end

-- Release Redis connection back to pool
local function release_redis_connection(red)
    if red then
        local ok, err = red:set_keepalive(10000, 100) -- 10s timeout, 100 connections
        if not ok then
            ngx.log(ngx.WARN, "Failed to set keepalive: ", err)
        end
    end
end

-- Get service routing from Redis
local function get_service_routing(service_name)
    -- Check shared dict cache first
    local cache_key = "route_cache:" .. service_name
    local cached = ngx.shared.service_routes:get(cache_key)
    if cached then
        local routing = cjson.decode(cached)
        ngx.log(ngx.DEBUG, "Using cached routing for ", service_name, ": ", routing.app_id)
        return routing
    end

    -- Query Redis
    local red = get_redis_connection()
    if not red then
        ngx.log(ngx.WARN, "Redis unavailable, using default routing for ", service_name)
        return nil
    end

    local key = ROUTING_KEY_PREFIX .. service_name
    local value, err = red:get(key)

    release_redis_connection(red)

    if not value or value == ngx.null then
        ngx.log(ngx.DEBUG, "No routing found for ", service_name, ", using default")
        return nil
    end

    -- Parse JSON routing data
    local ok, routing = pcall(cjson.decode, value)
    if not ok then
        ngx.log(ngx.ERR, "Failed to parse routing for ", service_name, ": ", routing)
        return nil
    end

    -- Cache the result
    local cache_value = cjson.encode(routing)
    ngx.shared.service_routes:set(cache_key, cache_value, CACHE_TTL)

    ngx.log(ngx.INFO, "Loaded routing for ", service_name, ": ", routing.AppId or routing.app_id, " @ ", routing.Host or routing.host)

    return routing
end

-- Determine service name from request URI
local function get_service_from_uri(uri)
    -- Check known service prefixes
    if string.match(uri, "^/auth/") then
        return "auth"
    elseif string.match(uri, "^/connect") then
        return "connect"
    elseif string.match(uri, "^/accounts/") then
        return "accounts"
    elseif string.match(uri, "^/website/") or string.match(uri, "^/$") then
        return "website"
    end

    return nil -- Unknown service
end

-- Main routing function - sets nginx variables for upstream selection
function _M.route_request()
    local uri = ngx.var.uri
    local service_name = get_service_from_uri(uri)

    if not service_name then
        -- Not a known service route, use default
        ngx.var.target_app_id = DEFAULT_APP_ID
        ngx.var.target_host = DEFAULT_HOST
        ngx.var.target_port = DEFAULT_PORT
        ngx.log(ngx.DEBUG, "Unknown service for URI: ", uri, ", using default routing")
        return
    end

    -- Get routing from Redis (or cache)
    local routing = get_service_routing(service_name)

    if routing then
        -- Use dynamic routing from orchestrator
        -- Handle both camelCase (C# JSON) and lowercase field names
        ngx.var.target_app_id = routing.AppId or routing.app_id or DEFAULT_APP_ID
        ngx.var.target_host = routing.Host or routing.host or DEFAULT_HOST
        ngx.var.target_port = routing.Port or routing.port or DEFAULT_PORT

        ngx.log(ngx.INFO, "Routing ", service_name, " to app_id=", ngx.var.target_app_id,
                " host=", ngx.var.target_host, ":", ngx.var.target_port)
    else
        -- No routing data, use defaults
        ngx.var.target_app_id = DEFAULT_APP_ID
        ngx.var.target_host = DEFAULT_HOST
        ngx.var.target_port = DEFAULT_PORT

        ngx.log(ngx.DEBUG, "Using default routing for ", service_name, ": ", DEFAULT_APP_ID)
    end
end

-- Health check function - verify routing data is available
function _M.check_health()
    local red = get_redis_connection()
    if not red then
        return false, "Cannot connect to Redis"
    end

    -- Check for any routing keys
    local keys, err = red:keys(ROUTING_KEY_PREFIX .. "*")
    release_redis_connection(red)

    if err then
        return false, "Redis error: " .. err
    end

    local count = 0
    if keys then
        count = #keys
    end

    return true, "Found " .. count .. " service routings"
end

-- Main access phase handler
_M.route_request()

return _M
