redis.call(
    'HSET',
    KEYS[1],
    'generation', ARGV[1],
    'state', 'ready',
    'serverRunId', ARGV[2],
    'schemaVersion', ARGV[3]
)
return 'OK'
