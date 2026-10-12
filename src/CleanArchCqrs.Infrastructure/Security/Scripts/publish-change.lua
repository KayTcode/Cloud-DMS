local currentRaw = redis.call('GET', KEYS[1])
if not currentRaw then
    return 'not-owner'
end

local ok, current = pcall(cjson.decode, currentRaw)
if not ok or type(current) ~= 'table' then
    return 'not-owner'
end

local okSnapshot, snapshot = pcall(cjson.decode, ARGV[3])
if not okSnapshot
    or type(snapshot) ~= 'table'
    or snapshot.complete ~= true
    or tonumber(snapshot.version) ~= tonumber(ARGV[2]) then
    return 'invalid-snapshot'
end

if current.updating ~= true then
    if current.complete == true
        and tonumber(current.version) == tonumber(ARGV[2])
        and current.operationId == nil then
        redis.call('DEL', KEYS[2])
        return 'already-published'
    end
    return 'not-owner'
end
if current.operationId ~= ARGV[1] or tonumber(current.version) ~= tonumber(ARGV[2]) then
    return 'not-owner'
end

snapshot.updating = false
snapshot.operationId = nil
redis.call('SET', KEYS[1], cjson.encode(snapshot))
redis.call('DEL', KEYS[2])
return 'published'
