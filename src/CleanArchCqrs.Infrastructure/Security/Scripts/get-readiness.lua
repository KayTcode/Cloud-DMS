local values = redis.call(
    'HMGET',
    KEYS[1],
    'generation',
    'state',
    'serverRunId',
    'schemaVersion'
)
return cjson.encode(values)
