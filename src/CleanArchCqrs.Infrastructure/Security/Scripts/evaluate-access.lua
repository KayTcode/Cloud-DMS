local function decode(raw)
    if not raw then
        return nil
    end

    local ok, value = pcall(cjson.decode, raw)
    if not ok or type(value) ~= 'table' then
        return nil
    end

    return value
end

local function normalizeId(value)
    if type(value) ~= 'string' then
        return ''
    end

    return string.lower(string.gsub(value, '-', ''))
end

local function hasId(value)
    return type(value) == 'string' and normalizeId(value) ~= ''
end

local meta = redis.call(
    'HMGET',
    KEYS[1],
    'generation',
    'state',
    'serverRunId',
    'schemaVersion'
)
local securityReady = meta[1] == ARGV[1]
    and meta[2] == 'ready'
    and meta[3] == ARGV[2]
    and meta[4] == '1'

local userRaw = redis.call('GET', KEYS[2])
local sessionRaw = redis.call('GET', KEYS[3])
local resourceRaw = ARGV[4] ~= '' and redis.call('GET', KEYS[4]) or false
local grantRaw = ARGV[3] == 'file' and ARGV[4] ~= '' and redis.call('GET', KEYS[5]) or false
local user = decode(userRaw)
local session = decode(sessionRaw)
local resource = decode(resourceRaw)
local grant = decode(grantRaw)

local scopeKeys = {}
local seenScopeKeys = {}
local function addScopeKey(scopeKey)
    if not seenScopeKeys[scopeKey] then
        seenScopeKeys[scopeKey] = true
        table.insert(scopeKeys, scopeKey)
    end
end

addScopeKey('dms:scope:global')
if user and hasId(user.tenantId) then
    addScopeKey('dms:scope:tenant:' .. normalizeId(user.tenantId))
end
if user and hasId(user.departmentId) then
    addScopeKey('dms:scope:department:' .. normalizeId(user.departmentId))
end
if resource and hasId(resource.tenantId) then
    addScopeKey('dms:scope:tenant:' .. normalizeId(resource.tenantId))
end
if resource and hasId(resource.departmentId) then
    addScopeKey('dms:scope:department:' .. normalizeId(resource.departmentId))
end

local scopeSnapshots = {}
local allScopesComplete = true
local anyScopeUpdating = false
for _, scopeKey in ipairs(scopeKeys) do
    local scope = decode(redis.call('GET', scopeKey))
    local scopeComplete = scope ~= nil
        and scope.complete == true
        and type(scope.updating) == 'boolean'
        and tonumber(scope.version) ~= nil
    local scopeUpdating = scope ~= nil and scope.updating == true
    if not scopeComplete then
        allScopesComplete = false
    end
    if scopeUpdating then
        anyScopeUpdating = true
    end
    table.insert(scopeSnapshots, {
        key = scopeKey,
        version = scopeComplete and tonumber(scope.version) or 0,
        isUpdating = scopeUpdating,
        complete = scopeComplete
    })
end

local snapshotsComplete = securityReady
    and user ~= nil
    and normalizeId(user.userId) == ARGV[5]
    and user.complete == true
    and type(user.isActive) == 'boolean'
    and type(user.roles) == 'table'
    and type(user.permissions) == 'table'
    and tonumber(user.version) ~= nil
    and session ~= nil
    and normalizeId(session.sessionId) == ARGV[6]
    and normalizeId(session.userId) == ARGV[5]
    and session.complete == true
    and type(session.isActive) == 'boolean'
    and type(session.blocked) == 'boolean'
    and tonumber(session.expiresAtUnixMilliseconds) ~= nil
    and tonumber(session.version) ~= nil
    and allScopesComplete

if ARGV[4] ~= '' then
    snapshotsComplete = snapshotsComplete
        and resource ~= nil
        and normalizeId(resource.id) == ARGV[4]
        and resource.complete == true
        and tonumber(resource.version) ~= nil
end

if ARGV[3] == 'file' and ARGV[4] ~= '' then
    snapshotsComplete = snapshotsComplete
        and normalizeId(resource.tenantId) ~= ''
        and (resource.departmentId == nil
            or resource.departmentId == cjson.null
            or normalizeId(resource.departmentId) ~= '')
        and normalizeId(resource.ownerId) ~= ''
        and type(resource.lifecycle) == 'string'
end

if grantRaw and grant then
    snapshotsComplete = snapshotsComplete
        and grant.complete == true
        and type(grant.canRead) == 'boolean'
        and type(grant.canWrite) == 'boolean'
        and type(grant.canDelete) == 'boolean'
        and resource ~= nil
        and tonumber(grant.resourceVersion) == tonumber(resource.version)
        and normalizeId(grant.userId) == ARGV[5]
        and (grant.expiresAtUnixMilliseconds == nil
            or grant.expiresAtUnixMilliseconds == cjson.null
            or tonumber(grant.expiresAtUnixMilliseconds) ~= nil)
end

if grantRaw and not grant then
    snapshotsComplete = false
end

local now = redis.call('TIME')
local serverTimeUnixMilliseconds = tonumber(now[1]) * 1000 + math.floor(tonumber(now[2]) / 1000)

return cjson.encode({
    securityReady = securityReady,
    snapshotsComplete = snapshotsComplete,
    isScopeUpdating = anyScopeUpdating,
    serverTimeUnixMilliseconds = serverTimeUnixMilliseconds,
    generation = meta[1] or '',
    userJson = userRaw or cjson.null,
    sessionJson = sessionRaw or cjson.null,
    resourceJson = resourceRaw or cjson.null,
    grantJson = grantRaw or cjson.null,
    scopesJson = cjson.encode(scopeSnapshots)
})
