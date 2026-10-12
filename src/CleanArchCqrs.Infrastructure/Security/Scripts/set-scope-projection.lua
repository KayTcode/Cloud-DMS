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

local okSnapshot, snapshot = pcall(cjson.decode, ARGV[4])
if not okSnapshot or type(snapshot) ~= 'table' or snapshot.complete ~= true then
    return 'invalid-projection'
end

redis.call('SET', KEYS[2], ARGV[4])
redis.call('SADD', KEYS[3], KEYS[2])
return 'stored'
