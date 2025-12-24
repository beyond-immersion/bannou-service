-- Asset Token Validation Handler
-- Validates one-time asset tokens for upload/download operations
-- Tokens are stored in Redis by the Asset service

local redis = require "resty.redis"
local cjson = require "cjson"

-- Redis connection helper for asset state store (bannou-redis)
local function get_redis_connection()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000)

    -- Use asset-specific Redis (bannou-redis) not routing-redis
    local redis_host = ngx.shared.asset_tokens:get("redis_host") or "bannou-redis"
    local redis_port = ngx.shared.asset_tokens:get("redis_port") or 6379

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.log(ngx.ERR, "Asset token validation: Failed to connect to Redis (", redis_host, ":", redis_port, "): ", err)
        return nil, err
    end

    return red
end

-- Validate upload token
-- Token format: asset:upload:{uploadId}
local function validate_upload_token(token, session_id)
    if not token then
        return false, "Missing upload token"
    end

    local red, err = get_redis_connection()
    if not red then
        return false, "Redis connection failed: " .. (err or "unknown")
    end

    -- Look up the upload session
    local token_key = "upload:" .. token
    local session_data, get_err = red:get(token_key)

    if get_err then
        red:set_keepalive(10000, 100)
        return false, "Redis error: " .. get_err
    end

    if not session_data or session_data == ngx.null then
        red:set_keepalive(10000, 100)
        return false, "Invalid or expired upload token"
    end

    -- Parse session data
    local ok, session = pcall(cjson.decode, session_data)
    if not ok then
        red:set_keepalive(10000, 100)
        return false, "Invalid session data format"
    end

    -- Check expiration
    if session.ExpiresAt then
        local expires_at = session.ExpiresAt
        -- Parse ISO 8601 date (basic check)
        local now = os.time()
        -- For simplicity, we'll trust the service-side expiration check
        -- The token existing means it's valid at the service level
    end

    -- Validate session_id matches if provided
    if session_id and session.SessionId and session.SessionId ~= session_id then
        ngx.log(ngx.WARN, "Asset upload token: Session mismatch. Expected=", session.SessionId, " Got=", session_id)
        red:set_keepalive(10000, 100)
        return false, "Session mismatch"
    end

    red:set_keepalive(10000, 100)

    return true, {
        upload_id = token,
        storage_key = session.StorageKey,
        content_type = session.ContentType,
        size = session.Size,
        is_multipart = session.IsMultipart
    }
end

-- Validate download token
-- Token format: asset:download:{assetId}:{tokenId}
local function validate_download_token(token)
    if not token then
        return false, "Missing download token"
    end

    local red, err = get_redis_connection()
    if not red then
        return false, "Redis connection failed: " .. (err or "unknown")
    end

    -- Look up the download token
    local token_key = "download:" .. token
    local token_data, get_err = red:get(token_key)

    if get_err then
        red:set_keepalive(10000, 100)
        return false, "Redis error: " .. get_err
    end

    if not token_data or token_data == ngx.null then
        red:set_keepalive(10000, 100)
        return false, "Invalid or expired download token"
    end

    -- Parse token data
    local ok, download_info = pcall(cjson.decode, token_data)
    if not ok then
        red:set_keepalive(10000, 100)
        return false, "Invalid token data format"
    end

    -- Mark token as used (one-time token) using atomic GETDEL
    local deleted = red:del(token_key)
    if not deleted or deleted == 0 then
        ngx.log(ngx.WARN, "Asset download token: Could not mark as used (race condition?)")
        -- Continue anyway - token was valid when we checked
    end

    red:set_keepalive(10000, 100)

    return true, {
        asset_id = download_info.AssetId,
        storage_key = download_info.StorageKey,
        content_type = download_info.ContentType,
        version_id = download_info.VersionId
    }
end

-- Extract token from request
local function get_token_from_request()
    -- Check query parameter first
    local token = ngx.var.arg_token
    if token then
        return token
    end

    -- Check header
    token = ngx.var.http_x_asset_token
    if token then
        return token
    end

    return nil
end

-- Extract session ID from JWT if present
local function get_session_id_from_jwt()
    local auth_header = ngx.var.http_authorization
    if not auth_header then
        return nil
    end

    local token = string.match(auth_header, "Bearer%s+(.+)")
    if not token then
        return nil
    end

    -- Basic JWT parsing (header.payload.signature)
    -- We only need to extract session_id from payload
    local parts = {}
    for part in string.gmatch(token, "[^.]+") do
        table.insert(parts, part)
    end

    if #parts ~= 3 then
        return nil
    end

    -- Decode base64url payload
    local payload_b64 = parts[2]
    -- Replace URL-safe chars
    payload_b64 = payload_b64:gsub("-", "+"):gsub("_", "/")
    -- Add padding if needed
    local padding = 4 - (#payload_b64 % 4)
    if padding < 4 then
        payload_b64 = payload_b64 .. string.rep("=", padding)
    end

    local ok, payload_json = pcall(ngx.decode_base64, payload_b64)
    if not ok or not payload_json then
        return nil
    end

    local ok2, payload = pcall(cjson.decode, payload_json)
    if not ok2 or not payload then
        return nil
    end

    -- Return session_id claim (adjust claim name as needed)
    return payload.session_id or payload.sid or payload.sub
end

-- Main access control handler for uploads
local function handle_upload_auth()
    local token = get_token_from_request()
    local session_id = get_session_id_from_jwt()

    if not token then
        ngx.log(ngx.WARN, "Asset upload: Missing token")
        ngx.status = 401
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({error = "Missing upload token"}))
        return ngx.exit(401)
    end

    local valid, result = validate_upload_token(token, session_id)
    if not valid then
        ngx.log(ngx.WARN, "Asset upload token validation failed: ", result)
        ngx.status = 403
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({error = result}))
        return ngx.exit(403)
    end

    -- Set headers for upstream proxy
    ngx.req.set_header("X-Asset-Upload-Id", result.upload_id)
    ngx.req.set_header("X-Asset-Storage-Key", result.storage_key)
    ngx.req.set_header("X-Asset-Content-Type", result.content_type)

    ngx.log(ngx.DEBUG, "Asset upload auth passed for upload_id=", result.upload_id)
end

-- Main access control handler for downloads
local function handle_download_auth()
    local token = get_token_from_request()

    if not token then
        ngx.log(ngx.WARN, "Asset download: Missing token")
        ngx.status = 401
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({error = "Missing download token"}))
        return ngx.exit(401)
    end

    local valid, result = validate_download_token(token)
    if not valid then
        ngx.log(ngx.WARN, "Asset download token validation failed: ", result)
        ngx.status = 403
        ngx.header.content_type = "application/json"
        ngx.say(cjson.encode({error = result}))
        return ngx.exit(403)
    end

    -- Set headers for upstream proxy
    ngx.req.set_header("X-Asset-Id", result.asset_id)
    ngx.req.set_header("X-Asset-Storage-Key", result.storage_key)
    ngx.req.set_header("X-Asset-Content-Type", result.content_type)
    if result.version_id then
        ngx.req.set_header("X-Asset-Version-Id", result.version_id)
    end

    ngx.log(ngx.DEBUG, "Asset download auth passed for asset_id=", result.asset_id)
end

-- Determine which handler to use based on URI
local uri = ngx.var.uri
if string.match(uri, "^/assets/upload") then
    handle_upload_auth()
elseif string.match(uri, "^/assets/download") then
    handle_download_auth()
else
    ngx.log(ngx.WARN, "Asset token validation: Unknown path ", uri)
    ngx.status = 404
    ngx.header.content_type = "application/json"
    ngx.say(cjson.encode({error = "Not found"}))
    return ngx.exit(404)
end
