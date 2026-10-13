if redis.call('EXISTS', 'security:bootstrap-lock') == 1 then return 'rebuilding' end
local currentRaw = redis.call('GET', KEYS[1])
if not currentRaw then
    return 'not-owner'
end

local ok, current = pcall(cjson.decode, currentRaw)
if not ok
    or type(current) ~= 'table'
    or current.operationId ~= ARGV[1]
    or tonumber(current.version) ~= tonumber(ARGV[2]) then
    return 'not-owner'
end

local previousRaw = redis.call('GET', KEYS[2])
if not previousRaw or previousRaw == '' then
    redis.call('DEL', KEYS[1])
else
    redis.call('SET', KEYS[1], previousRaw)
end

redis.call('DEL', KEYS[2])
return 'cancelled'
