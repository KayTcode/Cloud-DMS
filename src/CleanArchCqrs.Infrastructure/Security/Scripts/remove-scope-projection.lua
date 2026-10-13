if redis.call('EXISTS', 'security:bootstrap-lock') == 1 then return 'rebuilding' end
local markerRaw = redis.call('GET', KEYS[1])
if not markerRaw then
    return 'not-owner'
end

local ok, marker = pcall(cjson.decode, markerRaw)
if not ok
    or type(marker) ~= 'table'
    or marker.updating ~= true
    or marker.operationId ~= ARGV[1]
    or tonumber(marker.version) ~= tonumber(ARGV[2]) then
    return 'not-owner'
end

redis.call('DEL', KEYS[2])
redis.call('SREM', KEYS[3], KEYS[2])
return 'removed'
