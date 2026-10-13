if redis.call('GET', KEYS[2]) ~= ARGV[1] then return 'not-owner' end
if redis.call('EXISTS', 'security:bootstrap-invalidated:' .. ARGV[1]) == 1 then
    redis.call('DEL', KEYS[2], 'security:bootstrap-invalidated:' .. ARGV[1])
    return 'snapshot-changed'
end
local projections = cjson.decode(ARGV[5])
local indexes = cjson.decode(ARGV[6])
local desiredScopes = {}
for _, p in ipairs(projections) do
    if string.sub(p.key, 1, 10) == 'dms:scope:' then desiredScopes[p.key] = true end
end
for _, key in ipairs(redis.call('KEYS', 'dms:scope:*')) do
    if not desiredScopes[key] then
        local ok, current = pcall(cjson.decode, redis.call('GET', key))
        if not ok or type(current) ~= 'table' or current.updating == true then
            redis.call('DEL', KEYS[2])
            return 'pending-conflict'
        end
    end
end
-- A mutation begun before the rebuild may still be committing to MySQL.
-- Never replace its Redis barrier with an older database snapshot.
for _, p in ipairs(projections) do
    if string.sub(p.key, 1, 10) == 'dms:scope:' then
        local raw = redis.call('GET', p.key)
        if raw then
            local ok, current = pcall(cjson.decode, raw)
            local desired = cjson.decode(p.json)
            if not ok or type(current) ~= 'table' or tonumber(current.version) > tonumber(desired.version)
                or (current.updating == true and (desired.updating ~= true or current.operationId ~= desired.operationId)) then
                redis.call('DEL', KEYS[2])
                return 'pending-conflict'
            end
        end
    end
end
for _, pattern in ipairs({'dms:user:*', 'dms:file:*', 'dms:grant:*', 'auth:session:*', 'dms:scope:*', 'security:scope-index:*'}) do
    for _, key in ipairs(redis.call('KEYS', pattern)) do redis.call('DEL', key) end
end
for _, p in ipairs(projections) do
    if string.sub(p.key, 1, 13) == 'auth:session:' and redis.call('EXISTS', 'auth:blocked:' .. string.sub(p.key, 14)) == 1 then
        local snapshot = cjson.decode(p.json)
        snapshot.blocked = true
        snapshot.isActive = false
        snapshot.version = 2
        p.json = cjson.encode(snapshot)
    end
    redis.call('SET', p.key, p.json)
end
for _, index in ipairs(indexes) do
    for _, key in ipairs(index.keys) do redis.call('SADD', index.key, key) end
end
redis.call('HSET', KEYS[1], 'generation', ARGV[2], 'state', 'ready', 'serverRunId', ARGV[3], 'schemaVersion', ARGV[4])
redis.call('DEL', KEYS[2])
return 'ready'
