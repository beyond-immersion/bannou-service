-- Redis Health Check
-- Simple health monitoring for Redis connectivity

local redis = require "resty.redis"
local cjson = require "cjson"

local function check_redis_health()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000)

    local redis_host = os.getenv("REDIS_HOST") or "routing-redis"
    local redis_port = tonumber(os.getenv("REDIS_PORT") or "6379")

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.status = 503
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({
            status = "error",
            message = "Redis connection failed: " .. (err or "unknown error"),
            debug = {
                redis_host = redis_host,
                redis_port = redis_port
            }
        }))
        return
    end

    -- Test Redis with a simple ping
    local pong, err = red:ping()
    if not pong or err then
        red:close()
        ngx.status = 503
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({
            status = "error",
            message = "Redis ping failed: " .. (err or "no response")
        }))
        return
    end

    -- Test basic operations
    local test_key = "health_check_" .. ngx.time()
    red:set(test_key, "ok")
    local test_result, err = red:get(test_key)
    red:del(test_key)

    if err or test_result ~= "ok" then
        red:close()
        ngx.status = 503
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({
            status = "error",
            message = "Redis operations failed"
        }))
        return
    end

    red:close()

    ngx.status = 200
    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode({
        status = "healthy",
        redis_host = redis_host,
        redis_port = redis_port,
        response_time = "< 1s"
    }))
end

check_redis_health()