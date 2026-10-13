if redis.call('EXISTS', 'security:bootstrap-lock') == 1 then return 'rebuilding' end
local raw = redis.call('GET', KEYS[1])
redis.call('SET', 'auth:blocked:' .. string.sub(KEYS[1], 14), '1')
local snapshot = raw and cjson.decode(raw) or {complete=false, version=0}
snapshot.blocked = true
snapshot.isActive = false
snapshot.version = tonumber(snapshot.version) + 1
redis.call('SET', KEYS[1], cjson.encode(snapshot))
return 'blocked'
