-- Admin Queue Viewer
-- Displays queue status and statistics for monitoring

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

local function get_all_queues()
    local red = get_redis_connection()
    if not red then
        return {error = "Redis connection failed"}
    end

    -- Get all queue metadata keys
    local meta_keys, err = red:keys("queue_meta:*")
    if err then
        red:close()
        return {error = "Failed to get queue keys"}
    end

    local queues = {}

    for _, meta_key in ipairs(meta_keys) do
        local queue_id = string.match(meta_key, "queue_meta:(.+)")
        if queue_id then
            local queue_key = "queue:" .. queue_id

            -- Get queue metadata
            local meta_data, err = red:hgetall(meta_key)
            if meta_data and not err then
                local meta = red:array_to_hash(meta_data)

                -- Get queue length
                local queue_length, err = red:llen(queue_key)
                if err then queue_length = 0 end

                -- Get first few queue members
                local queue_members, err = red:lrange(queue_key, 0, 4)  -- First 5
                if err then queue_members = {} end

                -- Compile queue info
                local queue_info = {
                    queue_id = queue_id,
                    capacity = tonumber(meta.capacity) or 0,
                    current_size = queue_length,
                    processing_rate = tonumber(meta.processing_rate) or 0,
                    created_at = tonumber(meta.created_at) or 0,
                    last_updated = tonumber(meta.last_updated) or 0,
                    next_members = queue_members,
                    utilization = queue_length > 0 and (queue_length / (tonumber(meta.capacity) or 1)) * 100 or 0
                }

                table.insert(queues, queue_info)
            end
        end
    end

    red:close()

    -- Sort by current size (busiest first)
    table.sort(queues, function(a, b)
        return a.current_size > b.current_size
    end)

    -- Calculate statistics
    local total_queued = 0
    local total_capacity = 0
    for _, queue in ipairs(queues) do
        total_queued = total_queued + queue.current_size
        total_capacity = total_capacity + queue.capacity
    end

    return {
        queues = queues,
        statistics = {
            total_queues = #queues,
            total_queued = total_queued,
            total_capacity = total_capacity,
            overall_utilization = total_capacity > 0 and (total_queued / total_capacity) * 100 or 0,
            last_checked = ngx.time()
        }
    }
end

local function handle_admin_queues()
    local result = get_all_queues()

    ngx.status = 200
    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode(result))
end

handle_admin_queues()