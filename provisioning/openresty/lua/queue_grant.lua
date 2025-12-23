-- Queue Grant Handler
-- Implements queue grant with token generation and service routing
-- Based on docs/BANNOU_DESIGN.md queue system architecture

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

-- Generate queue access token
local function generate_queue_token(queue_id, user_id)
    local timestamp = ngx.time()
    local nonce = ngx.var.request_id or "default"

    -- Simple token format: queue_id:user_id:timestamp:nonce (base64 encoded)
    local token_data = queue_id .. ":" .. user_id .. ":" .. timestamp .. ":" .. nonce
    local encoded_token = ngx.encode_base64(token_data)

    return encoded_token
end

-- Process queue grant (remove from queue, generate access token)
local function process_queue_grant(queue_id, user_id)
    local red = get_redis_connection()
    if not red then
        return {error = "Redis connection failed"}
    end

    local queue_key = "queue:" .. queue_id
    local meta_key = "queue_meta:" .. queue_id

    -- Use Redis transaction for atomic operations
    red:multi()

    -- Check if user is at front of queue
    red:lindex(queue_key, 0)

    -- Get current queue size
    red:hget(meta_key, "current_size")

    local results, err = red:exec()
    if err then
        red:close()
        return {error = "Transaction failed"}
    end

    local front_user = results[1]
    local current_size = tonumber(results[2]) or 0

    -- Check if this user is at the front
    if front_user ~= user_id then
        red:close()
        return {
            success = false,
            error = "Not at front of queue",
            front_user = front_user
        }
    end

    -- Remove user from front of queue
    red:lpop(queue_key)

    -- Update queue metadata
    red:hset(meta_key, "current_size", math.max(0, current_size - 1))
    red:hset(meta_key, "last_updated", ngx.time())

    -- Generate access token
    local access_token = generate_queue_token(queue_id, user_id)

    -- Store token with expiration (5 minutes)
    local token_key = "queue_token:" .. access_token
    red:hset(token_key, "queue_id", queue_id)
    red:hset(token_key, "user_id", user_id)
    red:hset(token_key, "granted_at", ngx.time())
    red:expire(token_key, 300)  -- 5 minutes

    red:close()

    return {
        success = true,
        access_token = access_token,
        expires_at = ngx.time() + 300,
        queue_remaining = math.max(0, current_size - 1)
    }
end

-- Validate admin permissions (placeholder)
local function validate_admin_access(auth_token)
    -- In production, this would validate against auth service
    -- For now, accept any token for testing
    return auth_token and string.len(auth_token) > 0
end

-- Main handler
local function handle_queue_grant()
    ngx.req.read_body()
    local body = ngx.req.get_body_data()

    if not body then
        ngx.status = 400
        ngx.say(cjson.encode({error = "Missing request body"}))
        return
    end

    local ok, request = pcall(cjson.decode, body)
    if not ok or not request then
        ngx.status = 400
        ngx.say(cjson.encode({error = "Invalid JSON"}))
        return
    end

    local queue_id = request.queue_id
    local user_id = request.user_id
    local auth_token = request.auth_token

    if not queue_id or not user_id then
        ngx.status = 400
        ngx.say(cjson.encode({error = "Missing queue_id or user_id"}))
        return
    end

    -- Validate admin access for manual grants
    if not validate_admin_access(auth_token) then
        ngx.status = 403
        ngx.say(cjson.encode({error = "Admin access required for queue grants"}))
        return
    end

    -- Process the grant
    local result = process_queue_grant(queue_id, user_id)

    if result.error then
        ngx.status = 400
    else
        ngx.status = 200
    end

    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode(result))
end

handle_queue_grant()