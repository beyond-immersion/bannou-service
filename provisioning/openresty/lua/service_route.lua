-- Service Routing with Dynamic Backend Selection
-- Reads service routing from Redis (written by orchestrator via lib-state)
-- Sets nginx variables for dynamic upstream selection
-- Redis key pattern: orch:rt:{serviceName} (lib-state prefix for "orchestrator-routings" store)

local redis = require "resty.redis"
local cjson = require "cjson"

local _M = {}

-- Redis key patterns - must match what orchestrator writes via lib-state
-- The orchestrator uses store name "orchestrator-routings" which maps to prefix "orch:rt" in StateServicePlugin
local ROUTING_KEY_PREFIX = "orch:rt:"
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
        ngx.log(ngx.NOTICE, "[ROUTE-CACHE] Using cached routing for ", service_name,
            ": AppId=", tostring(routing.AppId or routing.appId or routing.app_id),
            " Host=", tostring(routing.Host or routing.host))
        return routing
    end

    -- Query Redis
    local redis_host = ngx.shared.service_routes:get("redis_host") or "routing-redis"
    local redis_port = tonumber(ngx.shared.service_routes:get("redis_port")) or 6379
    ngx.log(ngx.NOTICE, "[ROUTE-REDIS] Connecting to Redis at ", redis_host, ":", redis_port)

    local red = get_redis_connection()
    if not red then
        ngx.log(ngx.WARN, "[ROUTE-REDIS] Redis unavailable, using default routing for ", service_name)
        return nil
    end

    local key = ROUTING_KEY_PREFIX .. service_name
    ngx.log(ngx.NOTICE, "[ROUTE-REDIS] Querying key: ", key)

    local value, err = red:get(key)

    release_redis_connection(red)

    if not value or value == ngx.null then
        ngx.log(ngx.NOTICE, "[ROUTE-REDIS] No routing found for key '", key, "' - value=", tostring(value), ", using default")
        return nil
    end

    ngx.log(ngx.NOTICE, "[ROUTE-REDIS] Raw value from Redis: ", value)

    -- Parse JSON routing data
    local ok, routing = pcall(cjson.decode, value)
    if not ok then
        ngx.log(ngx.ERR, "[ROUTE-REDIS] Failed to parse routing JSON for ", service_name, ": ", routing)
        return nil
    end

    -- Log all fields we found
    ngx.log(ngx.NOTICE, "[ROUTE-PARSE] Parsed routing: ",
        " AppId=", tostring(routing.AppId),
        " appId=", tostring(routing.appId),
        " app_id=", tostring(routing.app_id),
        " Host=", tostring(routing.Host),
        " host=", tostring(routing.host),
        " Port=", tostring(routing.Port),
        " port=", tostring(routing.port))

    -- Cache the result
    local cache_value = cjson.encode(routing)
    ngx.shared.service_routes:set(cache_key, cache_value, CACHE_TTL)

    ngx.log(ngx.NOTICE, "[ROUTE-LOADED] Routing for ", service_name, ": ",
        routing.AppId or routing.appId or routing.app_id, " @ ",
        routing.Host or routing.host, ":", routing.Port or routing.port or 80)

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

    ngx.log(ngx.NOTICE, "[ROUTE-REQUEST] URI=", uri, " -> service=", tostring(service_name))

    if not service_name then
        -- Not a known service route, use default
        ngx.var.target_app_id = DEFAULT_APP_ID
        ngx.var.target_host = DEFAULT_HOST
        ngx.var.target_port = DEFAULT_PORT
        ngx.log(ngx.NOTICE, "[ROUTE-DECISION] Unknown service for URI: ", uri, ", using default -> ", DEFAULT_APP_ID)
        return
    end

    -- Get routing from Redis (or cache)
    local routing = get_service_routing(service_name)

    if routing then
        -- Use dynamic routing from orchestrator
        -- Handle camelCase (C# JSON default), PascalCase, and snake_case field names
        ngx.var.target_app_id = routing.appId or routing.AppId or routing.app_id or DEFAULT_APP_ID
        ngx.var.target_host = routing.host or routing.Host or DEFAULT_HOST
        ngx.var.target_port = routing.port or routing.Port or DEFAULT_PORT

        ngx.log(ngx.NOTICE, "[ROUTE-DECISION] ", service_name, " -> DYNAMIC routing: ",
                "app_id=", ngx.var.target_app_id,
                " host=", ngx.var.target_host,
                " port=", ngx.var.target_port)
    else
        -- No routing data, use defaults
        ngx.var.target_app_id = DEFAULT_APP_ID
        ngx.var.target_host = DEFAULT_HOST
        ngx.var.target_port = DEFAULT_PORT

        ngx.log(ngx.NOTICE, "[ROUTE-DECISION] ", service_name, " -> DEFAULT routing: ",
                "app_id=", DEFAULT_APP_ID,
                " host=", DEFAULT_HOST,
                " port=", DEFAULT_PORT)
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
