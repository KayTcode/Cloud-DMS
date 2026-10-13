if not redis.call('SET', KEYS[2], ARGV[1], 'NX', 'PX', 60000) then return 'busy' end
redis.call('HSET', KEYS[1], 'state', 'rebuilding')
return 'started'
