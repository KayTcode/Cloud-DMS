local currentRaw = redis.call('GET', KEYS[1])
if not currentRaw then
    return 'not-owner'
end

local ok, current = pcall(cjson.decode, currentRaw)
if not ok
    or type(current) ~= 'table'
    or current.updating ~= true
    or current.operationId ~= ARGV[1]
    or tonumber(current.version) ~= tonumber(ARGV[2]) then
    return 'not-owner'
end

-- Rollback restores only this operation's owned barrier. A rebuild that began
-- before the cancellation must recapture its database snapshot before publish.
local bootstrapOwner = redis.call('GET', 'security:bootstrap-lock')
if bootstrapOwner then
    redis.call('SET', 'security:bootstrap-invalidated:' .. bootstrapOwner, '1', 'PX', 60000)
end

local previousRaw = redis.call('GET', KEYS[2])
if not previousRaw or previousRaw == '' then
    redis.call('DEL', KEYS[1])
else
    redis.call('SET', KEYS[1], previousRaw)
end

redis.call('DEL', KEYS[2])
return 'cancelled'
