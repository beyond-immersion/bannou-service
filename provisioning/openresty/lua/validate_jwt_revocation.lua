-- JWT Revocation Validation Handler
-- Checks if a JWT token has been revoked at the edge layer before forwarding to upstream
-- Revocation entries are stored in Redis by the Auth service's EdgeRevocationService
--
-- Redis Keys (prefix: auth:edge):
--   auth:edge:token:{jti}     - Token-level revocation (specific JWT revoked)
--   auth:edge:account:{uuid}  - Account-level revocation (all tokens issued before timestamp)
--
-- This is a defense-in-depth layer - the upstream service also validates tokens

local redis = require "resty.redis"
local cjson = require "cjson"

-- Redis connection helper for auth state store (bannou-redis)
local function get_redis_connection()
    local red = redis:new()
    red:set_timeouts(1000, 1000, 1000)

    -- Use auth Redis (bannou-redis) - same as asset tokens
    local redis_host = ngx.shared.auth_revocation:get("redis_host") or "bannou-redis"
    local redis_port = ngx.shared.auth_revocation:get("redis_port") or 6379

    local ok, err = red:connect(redis_host, redis_port)
    if not ok then
        ngx.log(ngx.ERR, "JWT revocation check: Failed to connect to Redis (", redis_host, ":", redis_port, "): ", err)
        return nil, err
    end

    return red
end

-- Base64URL decode (JWT uses URL-safe base64)
local function base64url_decode(input)
    if not input then
        return nil
    end

    -- Replace URL-safe chars with standard base64 chars
    local b64 = input:gsub("-", "+"):gsub("_", "/")

    -- Add padding if needed
    local padding = 4 - (#b64 % 4)
    if padding < 4 then
        b64 = b64 .. string.rep("=", padding)
    end

    local ok, decoded = pcall(ngx.decode_base64, b64)
    if not ok or not decoded then
        return nil
    end

    return decoded
end

-- Extract claims from JWT without signature verification
-- (Signature is verified by upstream - we just need jti, sub, iat for revocation check)
local function extract_jwt_claims(token)
    if not token then
        return nil, "No token provided"
    end

    -- Split JWT into header.payload.signature
    local parts = {}
    for part in string.gmatch(token, "[^.]+") do
        table.insert(parts, part)
    end

    if #parts ~= 3 then
        return nil, "Invalid JWT format"
    end

    -- Decode payload (second part)
    local payload_json = base64url_decode(parts[2])
    if not payload_json then
        return nil, "Failed to decode JWT payload"
    end

    local ok, payload = pcall(cjson.decode, payload_json)
    if not ok or not payload then
        return nil, "Failed to parse JWT payload"
    end

    return payload
end

-- Check if token is revoked (by JTI)
local function check_token_revocation(red, jti)
    if not jti then
        return false, nil  -- No JTI means we can't check token revocation
    end

    local key = "auth:edge:token:" .. jti
    local data, err = red:get(key)

    if err then
        ngx.log(ngx.ERR, "JWT revocation check: Redis error checking token ", jti, ": ", err)
        return false, err
    end

    if data and data ~= ngx.null then
        -- Token is revoked - parse entry for logging
        local ok, entry = pcall(cjson.decode, data)
        if ok and entry then
            ngx.log(ngx.WARN, "JWT revocation check: Token revoked. JTI=", jti,
                " AccountId=", entry.AccountId or "?",
                " Reason=", entry.Reason or "?")
        else
            ngx.log(ngx.WARN, "JWT revocation check: Token revoked. JTI=", jti)
        end
        return true, nil
    end

    return false, nil
end

-- Check if account tokens are revoked (by account ID and issued-at time)
local function check_account_revocation(red, account_id, iat)
    if not account_id then
        return false, nil  -- No account ID means we can't check
    end

    local key = "auth:edge:account:" .. account_id
    local data, err = red:get(key)

    if err then
        ngx.log(ngx.ERR, "JWT revocation check: Redis error checking account ", account_id, ": ", err)
        return false, err
    end

    if data and data ~= ngx.null then
        -- Account has revocation entry - check if token was issued before cutoff
        local ok, entry = pcall(cjson.decode, data)
        if ok and entry and entry.IssuedBeforeUnix then
            -- Token iat is Unix timestamp - compare with revocation cutoff
            if iat and iat < entry.IssuedBeforeUnix then
                ngx.log(ngx.WARN, "JWT revocation check: Account tokens revoked. AccountId=", account_id,
                    " TokenIAT=", iat,
                    " IssuedBefore=", entry.IssuedBeforeUnix,
                    " Reason=", entry.Reason or "?")
                return true, nil
            end
            -- Token was issued after the revocation cutoff - still valid
            ngx.log(ngx.DEBUG, "JWT revocation check: Token issued after revocation cutoff. AccountId=", account_id,
                " TokenIAT=", iat,
                " IssuedBefore=", entry.IssuedBeforeUnix)
        end
    end

    return false, nil
end

-- Main revocation check function
-- Returns: is_revoked (bool), error_message (string or nil)
local function check_revocation(token)
    -- Extract claims from JWT
    local claims, err = extract_jwt_claims(token)
    if not claims then
        ngx.log(ngx.DEBUG, "JWT revocation check: Could not extract claims: ", err or "unknown")
        -- Can't check revocation without claims - let upstream handle validation
        return false, nil
    end

    -- Get JTI (JWT ID) and account info
    local jti = claims.jti
    local account_id = claims.sub  -- Subject claim is typically account ID
    local iat = claims.iat  -- Issued-at timestamp

    -- If no JTI or account ID, we can't check revocation
    if not jti and not account_id then
        ngx.log(ngx.DEBUG, "JWT revocation check: No jti or sub claim - skipping revocation check")
        return false, nil
    end

    -- Connect to Redis
    local red, connect_err = get_redis_connection()
    if not red then
        -- Redis connection failed - fail open (let upstream handle it)
        ngx.log(ngx.WARN, "JWT revocation check: Redis unavailable - allowing request through")
        return false, nil
    end

    local is_revoked = false
    local check_err = nil

    -- Check token-level revocation first (most specific)
    if jti then
        is_revoked, check_err = check_token_revocation(red, jti)
    end

    -- Check account-level revocation if token not specifically revoked
    if not is_revoked and account_id then
        is_revoked, check_err = check_account_revocation(red, account_id, iat)
    end

    -- Return connection to pool
    red:set_keepalive(10000, 100)

    return is_revoked, check_err
end

-- Extract Bearer token from Authorization header
local function get_bearer_token()
    local auth_header = ngx.var.http_authorization
    if not auth_header then
        return nil
    end

    local token = string.match(auth_header, "Bearer%s+(.+)")
    return token
end

-- Access phase handler - called before proxying to upstream
local function handle_access()
    local token = get_bearer_token()

    if not token then
        -- No token - let upstream handle authentication
        return
    end

    local is_revoked, err = check_revocation(token)

    if is_revoked then
        ngx.status = 401
        ngx.header.content_type = "application/json"
        ngx.header["WWW-Authenticate"] = 'Bearer error="invalid_token", error_description="Token has been revoked"'
        ngx.say(cjson.encode({
            error = "token_revoked",
            message = "This token has been revoked"
        }))
        return ngx.exit(401)
    end

    -- Token not revoked (or revocation check failed) - continue to upstream
    if err then
        -- Log error but allow request through (fail open for defense-in-depth)
        ngx.log(ngx.WARN, "JWT revocation check: Error during check - ", err, " - allowing request through")
    end
end

-- Export for use as access_by_lua_file
handle_access()
