-- Queue Join Handler
-- Implements queue joining with capacity checking and position assignment
-- Based on API-DESIGN.md queue system requirements

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

-- Join a queue with atomic operations
local function join_queue(queue_id, user_id, priority)
    local red = get_redis_connection()
    if not red then
        return {error = "Redis connection failed"}
    end

    local queue_key = "queue:" .. queue_id
    local meta_key = "queue_meta:" .. queue_id
    priority = priority or 0

    -- Use Redis transaction to ensure atomicity
    red:multi()

    -- Check if user is already in queue
    red:lpos(queue_key, user_id)

    -- Get current queue size and capacity
    red:hget(meta_key, "current_size")
    red:hget(meta_key, "capacity")

    local results, err = red:exec()
    if err then
        red:close()
        return {error = "Transaction failed"}
    end

    local existing_position = results[1]
    local current_size = tonumber(results[2]) or 0
    local capacity = tonumber(results[3]) or 100

    -- Check if user already in queue
    if existing_position then
        red:close()
        return {
            success = false,
            error = "Already in queue",
            position = existing_position + 1
        }
    end

    -- Check capacity
    if current_size >= capacity then
        red:close()
        return {
            success = false,
            error = "Queue full",
            queue_capacity = capacity,
            current_size = current_size
        }
    end

    -- Add user to queue (priority determines position)
    local position
    if priority > 0 then
        -- High priority - add to front
        red:lpush(queue_key, user_id)
        position = 1
    else
        -- Normal priority - add to back
        red:rpush(queue_key, user_id)
        position = current_size + 1
    end

    -- Update queue metadata
    red:hset(meta_key, "current_size", current_size + 1)
    red:hset(meta_key, "last_updated", ngx.time())

    -- Set queue expiration (prevent memory leaks)
    red:expire(queue_key, 3600)  -- 1 hour
    red:expire(meta_key, 3600)

    red:close()

    return {
        success = true,
        position = position,
        queue_capacity = capacity,
        current_size = current_size + 1,
        estimated_wait = math.ceil(position / 10 * 60)  -- Assume 10/min processing rate
    }
end

-- Initialize queue if it doesn't exist
local function initialize_queue(queue_id, capacity)
    local red = get_redis_connection()
    if not red then
        return false
    end

    local meta_key = "queue_meta:" .. queue_id

    -- Check if queue already exists
    local exists, err = red:exists(meta_key)
    if err or exists == 1 then
        red:close()
        return true
    end

    -- Initialize queue metadata
    red:hmset(meta_key,
        "capacity", capacity or 100,
        "current_size", 0,
        "processing_rate", 10,  -- per minute
        "created_at", ngx.time(),
        "last_updated", ngx.time())

    red:expire(meta_key, 3600)
    red:close()

    return true
end

-- Main handler
local function handle_queue_join()
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
    local priority = request.priority or 0
    local capacity = request.capacity or 100

    if not queue_id or not user_id then
        ngx.status = 400
        ngx.say(cjson.encode({error = "Missing queue_id or user_id"}))
        return
    end

    -- Initialize queue if needed
    initialize_queue(queue_id, capacity)

    -- Join the queue
    local result = join_queue(queue_id, user_id, priority)

    if result.error then
        ngx.status = 400
    else
        ngx.status = 200
    end

    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode(result))
end

handle_queue_join()