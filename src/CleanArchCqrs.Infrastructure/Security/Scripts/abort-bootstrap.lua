if redis.call('GET', KEYS[1]) == ARGV[1] then redis.call('DEL', KEYS[1]) end
redis.call('DEL', 'security:bootstrap-invalidated:' .. ARGV[1])
return 'aborted'
