-- Queue Status Handler
-- Implements queue position and wait time estimation
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

-- Get queue position and wait time
local function get_queue_status(queue_id, user_id)
    local red = get_redis_connection()
    if not red then
        return {error = "Redis connection failed"}
    end

    -- Get user's position in queue
    local queue_key = "queue:" .. queue_id
    local position, err = red:lpos(queue_key, user_id)

    if err then
        red:close()
        return {error = "Failed to get queue position"}
    end

    if not position then
        red:close()
        return {
            in_queue = false,
            position = nil,
            estimated_wait = nil
        }
    end

    -- Get queue metadata
    local meta_key = "queue_meta:" .. queue_id
    local queue_meta, err = red:hgetall(meta_key)

    if err or not queue_meta then
        red:close()
        return {error = "Failed to get queue metadata"}
    end

    local meta = red:array_to_hash(queue_meta)
    local capacity = tonumber(meta.capacity) or 100
    local processing_rate = tonumber(meta.processing_rate) or 10  -- per minute
    local current_size = tonumber(meta.current_size) or 0

    -- Calculate estimated wait time
    local estimated_wait = 0
    if position > 0 and processing_rate > 0 then
        estimated_wait = math.ceil(position / processing_rate * 60)  -- seconds
    end

    red:close()

    return {
        in_queue = true,
        position = position + 1,  -- Convert to 1-based indexing
        estimated_wait = estimated_wait,
        queue_capacity = capacity,
        current_size = current_size
    }
end

-- Main handler
local function handle_queue_status()
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

    if not queue_id or not user_id then
        ngx.status = 400
        ngx.say(cjson.encode({error = "Missing queue_id or user_id"}))
        return
    end

    local status = get_queue_status(queue_id, user_id)

    ngx.status = 200
    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode(status))
end

handle_queue_status()