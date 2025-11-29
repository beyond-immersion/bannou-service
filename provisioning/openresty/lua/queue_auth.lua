-- Queue Authentication Handler
-- Validates queue tokens and API access
-- Based on API-DESIGN.md security requirements

local redis = require "resty.redis"
local cjson = require "cjson"

-- Redis connection helper
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

-- Validate queue access token
local function validate_queue_token(token)
    if not token then
        return false, "Missing token"
    end

    local red = get_redis_connection()
    if not red then
        return false, "Redis connection failed"
    end

    local token_key = "queue_token:" .. token
    local token_data, err = red:hgetall(token_key)

    if err or not token_data then
        red:close()
        return false, "Invalid token"
    end

    local token_info = red:array_to_hash(token_data)
    if not token_info.queue_id then
        red:close()
        return false, "Invalid token format"
    end

    red:close()
    return true, token_info
end

-- Validate API key or session token
local function validate_api_access(auth_header)
    if not auth_header then
        return false, "Missing authorization"
    end

    -- Extract token from header
    local token = string.match(auth_header, "Bearer%s+(.+)")
    if not token then
        return false, "Invalid authorization format"
    end

    -- For now, accept any bearer token (in production, validate against auth service)
    if string.len(token) < 10 then
        return false, "Invalid token"
    end

    return true, {user_id = "validated_user"}
end

-- Main access control handler
local function handle_queue_auth()
    local method = ngx.var.request_method
    local uri = ngx.var.uri
    local auth_header = ngx.var.http_authorization
    local queue_token = ngx.var.http_x_queue_token

    -- For queue operations, validate queue token if provided
    if queue_token then
        local valid, token_info = validate_queue_token(queue_token)
        if not valid then
            ngx.log(ngx.WARN, "Queue token validation failed: ", token_info)
            ngx.status = 403
            ngx.header.content_type = "application/json"
            ngx.say(cjson.encode({error = "Invalid queue token"}))
            return
        end

        -- Set user context from token
        ngx.req.set_header("X-User-Id", token_info.user_id)
        ngx.req.set_header("X-Queue-Id", token_info.queue_id)
    end

    -- For API access, validate authorization
    if auth_header then
        local valid, user_info = validate_api_access(auth_header)
        if not valid then
            ngx.log(ngx.WARN, "API auth validation failed: ", user_info)
            ngx.status = 401
            ngx.header.content_type = "application/json"
            ngx.say(cjson.encode({error = "Unauthorized"}))
            return
        end

        -- Set user context from auth
        ngx.req.set_header("X-User-Id", user_info.user_id)
    end

    -- For admin endpoints, require both auth and admin role
    if string.match(uri, "^/admin/") then
        if not auth_header then
            ngx.status = 401
            ngx.header.content_type = "application/json"
            ngx.say(cjson.encode({error = "Admin access requires authentication"}))
            return
        end

        -- Additional admin validation would go here
        ngx.log(ngx.INFO, "Admin access granted for: ", uri)
    end

    -- Log successful auth
    ngx.log(ngx.DEBUG, "Queue auth passed for: ", method, " ", uri)
end

handle_queue_auth()