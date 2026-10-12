local values = redis.call('SMEMBERS', KEYS[1])
return table.concat(values, '\n')
