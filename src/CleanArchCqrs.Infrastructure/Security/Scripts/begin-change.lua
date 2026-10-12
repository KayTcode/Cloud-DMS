local meta = redis.call('HMGET', KEYS[1], 'generation', 'state', 'serverRunId', 'schemaVersion')
if meta[1] ~= ARGV[1]
    or meta[2] ~= 'ready'
    or meta[3] ~= ARGV[2]
    or meta[4] ~= '1' then
    return 'not-ready'
end

local currentRaw = redis.call('GET', KEYS[2])
local current = nil
if currentRaw then
    local ok, value = pcall(cjson.decode, currentRaw)
    if not ok or type(value) ~= 'table' then
        return 'invalid-scope'
    end
    current = value
end

if current and current.updating == true then
    if current.operationId == ARGV[3] and tonumber(current.version) == tonumber(ARGV[4]) then
        return 'started'
    end
    return 'busy'
end

local currentVersion = current and tonumber(current.version) or 0
if tonumber(ARGV[4]) <= currentVersion then
    return 'stale-version'
end

redis.call('SET', KEYS[3], currentRaw or '')
redis.call(
    'SET',
    KEYS[2],
    cjson.encode({
        version = tonumber(ARGV[4]),
        updating = true,
        complete = current ~= nil and current.complete == true,
        operationId = ARGV[3]
    })
)
return 'started'
