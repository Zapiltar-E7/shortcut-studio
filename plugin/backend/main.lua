-- Shortcut Studio companion plugin backend.
-- Reads what the Shortcut Studio app writes to %APPDATA%\ShortcutStudio\extras and stores achievement unlocks.
-- Every call takes one JSON string and returns one JSON string (the same bridge the frontend's `call` helper uses).

local logger     = require("logger")
local millennium = require("millennium")
local fs         = require("fs")
local json       = require("json")
local utils      = require("utils")

local function data_dir()
    local appdata = utils.getenv("APPDATA") or ""
    return fs.join(appdata, "ShortcutStudio", "extras")
end

local function read_json(path)
    if not fs.exists(path) then return nil end
    local raw = utils.read_file(path)
    if raw == nil or raw == "" then return nil end
    local ok, data = pcall(json.decode, raw)
    if ok then return data end
    logger:error("Couldn't parse " .. path)
    return nil
end

local function write_json(path, value)
    fs.create_directories(data_dir())
    local tmp = path .. ".tmp"
    local ok, err = utils.write_file(tmp, json.encode(value))
    if not ok then
        logger:error("Couldn't write " .. tmp .. ": " .. tostring(err))
        return false
    end
    return fs.rename(tmp, path) and true or false
end

local function games_file() return fs.join(data_dir(), "games.json") end
local function unlocks_file() return fs.join(data_dir(), "unlocks.json") end

local function args(payload)
    local ok, data = pcall(json.decode, payload or "{}")
    if ok and type(data) == "table" then return data end
    return {}
end

local MIME = { png = "image/png", jpg = "image/jpeg", jpeg = "image/jpeg", webp = "image/webp", gif = "image/gif" }

-- Turns an icon file name into a data: URI the Steam UI can show (it can't load local files directly).
local function icon_uri(appid, file)
    if type(file) ~= "string" or file == "" or file:find("[/\\]") or file:find("%.%.") then return "" end
    local path = fs.join(data_dir(), tostring(appid), file)
    if not fs.exists(path) then return "" end
    local bytes = utils.read_file(path)
    if bytes == nil then return "" end
    local ext = (file:match("%.([%w]+)$") or "png"):lower()
    return "data:" .. (MIME[ext] or "image/png") .. ";base64," .. utils.base64_encode(bytes)
end

-- ---------------------------------------------------------------------------------------------
-- Emulator trophies: read live unlocks straight from RPCS3 / shadPS4 files.
-- ---------------------------------------------------------------------------------------------

local function be_u32(s, o) -- o is 0-based
    local a, b, c, d = s:byte(o + 1, o + 4)
    if not d then return nil end
    return ((a * 256 + b) * 256 + c) * 256 + d
end

-- RPCS3 TROPUSR.DAT: big-endian; table type 6 = per-trophy {id, state, ..., CellRtc tick in microseconds since year 1}.
local function rpcs3_unlocks(path)
    local out = {}
    local s = utils.read_file(path)
    if not s or #s < 48 or be_u32(s, 0) ~= 0x818F54AD then return out end
    local tables = be_u32(s, 8) or 0
    for i = 0, tables - 1 do
        local h = 48 + i * 32
        local ttype, size, count = be_u32(s, h), be_u32(s, h + 4), be_u32(s, h + 12)
        local off = (be_u32(s, h + 16) or 0) * 4294967296 + (be_u32(s, h + 20) or 0)
        if ttype == 6 and size and count then
            for e = 0, count - 1 do
                local p = off + e * (16 + size)
                local id, state = be_u32(s, p + 16), be_u32(s, p + 20)
                if id == nil then break end
                if state and state ~= 0 then
                    local hi, lo = be_u32(s, p + 32) or 0, be_u32(s, p + 36) or 0
                    local micros = hi * 4294967296 + lo
                    local ms = math.floor((micros - 62135596800000000) / 1000)
                    if ms < 0 then ms = utils.time_ms() end
                    out[id] = ms
                end
            end
        end
    end
    return out
end

-- shadPS4 user\home\<id>\trophy\<NPWR>.xml: <trophy id="1" ... unlockstate="true" timestamp="unix seconds">
local function shadps4_unlocks(path)
    local out = {}
    local s = utils.read_file(path)
    if not s then return out end
    for tag in s:gmatch("<trophy%s[^>]*>") do
        if tag:find('unlockstate%s*=%s*"true"') then
            local id = tonumber(tag:match('%sid%s*=%s*"(%d+)"'))
            local ts = tonumber(tag:match('timestamp%s*=%s*"(%d+)"')) or 0
            if id then out[id] = (ts > 100000000000) and ts or ts * 1000 end
        end
    end
    return out
end

-- ---------------------------------------------------------------------------------------------
-- RetroAchievements: unlocks come from the web API and are cached per game in extras\ra\<gameId>.json
-- ---------------------------------------------------------------------------------------------

local http = require("http")
local ra_last_refresh = {} -- gameId -> unix ms of the last API call (per Steam session)

local function ra_credentials()
    local path = fs.join(utils.getenv("APPDATA") or "", "ShortcutStudio", "settings.json")
    local s = read_json(path)
    if type(s) ~= "table" then return nil end
    local user, key = s.RaUser, s.RaKey
    if type(user) ~= "string" or user == "" or type(key) ~= "string" or key == "" then return nil end
    return user, key
end

-- "YYYY-MM-DD HH:MM:SS" (UTC) -> unix ms, without relying on os.time's local timezone.
local function utc_ms(s)
    if type(s) ~= "string" then return nil end
    local y, mo, d, h, mi, se = s:match("(%d+)-(%d+)-(%d+)[ T](%d+):(%d+):(%d+)")
    if not y then return nil end
    y, mo, d = tonumber(y), tonumber(mo), tonumber(d)
    if mo <= 2 then y = y - 1 end
    local era = math.floor(y / 400)
    local yoe = y - era * 400
    local mp = (mo + 9) % 12
    local doy = math.floor((153 * mp + 2) / 5) + d - 1
    local doe = yoe * 365 + math.floor(yoe / 4) - math.floor(yoe / 100) + doy
    local days = era * 146097 + doe - 719468
    return ((days * 24 + tonumber(h)) * 60 + tonumber(mi)) * 60000 + tonumber(se) * 1000
end

local function url_encode(s)
    return (tostring(s):gsub("[^%w%-_%.~]", function(c) return string.format("%%%02X", string.byte(c)) end))
end

--- Fetches the user's progress for one RA game and rewrites its unlock cache. Returns true on success.
local function ra_refresh(game_id, cache_file)
    local user, key = ra_credentials()
    if not user then return false end
    ra_last_refresh[game_id] = utils.time_ms()
    local url = "https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?y=" .. url_encode(key) ..
        "&u=" .. url_encode(user) .. "&g=" .. url_encode(game_id)
    local resp, err = http.get(url, { timeout = 15, user_agent = "ShortcutStudio/1.0" })
    if not resp or resp.status ~= 200 then
        logger:error("RetroAchievements request failed: " .. tostring(err or (resp and resp.status)))
        return false
    end
    local ok, data = pcall(json.decode, resp.body)
    if not ok or type(data) ~= "table" or type(data.Achievements) ~= "table" then return false end
    local out = {}
    for _, a in pairs(data.Achievements) do
        if type(a) == "table" and a.ID then
            local t = utc_ms(a.DateEarnedHardcore) or utc_ms(a.DateEarned)
            if t then out[tostring(math.floor(a.ID))] = t end
        end
    end
    fs.create_directories(fs.parent_path(cache_file))
    local tmp = cache_file .. ".tmp"
    if utils.write_file(tmp, json.encode(out)) then fs.rename(tmp, cache_file) end
    return true
end

local function ra_unlocks(path)
    local out = {}
    local m = read_json(path)
    if type(m) == "table" then
        for id, t in pairs(m) do
            local n = tonumber(id)
            if n and type(t) == "number" then out[n] = t end
        end
    end
    return out
end

--- Refresh an RA-synced game's cache if it's older than max_age_ms.
local function ra_maybe_refresh(src, max_age_ms)
    if type(src) ~= "table" or src.emulator ~= "retroachievements" then return end
    local id = tostring(src.npCommId or "")
    if id == "" then return end
    local last = ra_last_refresh[id] or 0
    if utils.time_ms() - last >= max_age_ms then pcall(ra_refresh, id, src.unlockFile) end
end

-- ps3recomp PC ports (e.g. Soulbrandt): gamedata\trophies\<comm id>.json = { trophies = { { id, timestamp (unix s) } } }
local function ps3recomp_unlocks(path)
    local out = {}
    local data = read_json(path)
    if type(data) ~= "table" or type(data.trophies) ~= "table" then return out end
    for _, t in ipairs(data.trophies) do
        local id, ts = tonumber(t.id), tonumber(t.timestamp) or 0
        if id then out[id] = (ts > 100000000000) and ts or (ts > 0 and ts * 1000 or utils.time_ms()) end
    end
    return out
end

-- ---------------------------------------------------------------------------------------------
-- Save-file achievements: games with no trophy system of their own (e.g. Breath of the Wild on Cemu).
-- extras\saverules\<npCommId>.json holds rules that are checked against every save slot under unlockFile
-- (the game's "user" save folder). Flag files are a 12-byte header, then 8-byte records of
-- { big-endian u32 CRC32(flag name), u32 value }; arrays and strings repeat one hash over several records.
-- Unlocks are permanent once seen and kept in extras\saverules\<npCommId>.unlocks.json.
-- ---------------------------------------------------------------------------------------------

local save_rules_cache = {}  -- rules path -> { stamp, rules }
local save_slot_cache = {}   -- slot folder -> { stamp, mode, met = { [trophyId] = true } }

local function stamp_of(path)
    if not fs.exists(path) then return "" end
    return tostring(fs.last_write_time(path) or "")
end

--- Reads a flag file into { s = bytes, first = { [hash] = offset }, n = { [hash] = records } }.
local function parse_flag_file(path)
    local s = utils.read_file(path)
    if not s or #s < 20 then return nil end
    local first, n = {}, {}
    local len = #s
    local o = 12
    while o + 8 <= len do
        local a, b, c, d = s:byte(o + 1, o + 4)
        local h = ((a * 256 + b) * 256 + c) * 256 + d
        local k = n[h]
        if k == nil then first[h] = o; n[h] = 1 else n[h] = k + 1 end
        o = o + 8
    end
    return { s = s, first = first, n = n }
end

local function flag_value(f, h)
    if not f then return nil end
    local o = f.first[h]
    if not o then return nil end
    return be_u32(f.s, o + 4)
end

--- A string or string array stored as consecutive 4-byte chunks under one hash.
local function flag_text(f, h)
    if not f then return nil end
    local o, count = f.first[h], f.n[h]
    if not o then return nil end
    local parts = {}
    for i = 0, count - 1 do parts[#parts + 1] = f.s:sub(o + i * 8 + 5, o + i * 8 + 8) end
    return table.concat(parts)
end

local function rule_met(c, game, caption)
    if type(c) ~= "table" then return false end
    if type(c.any) == "table" then
        for _, sub in ipairs(c.any) do if rule_met(sub, game, caption) then return true end end
        return false
    end
    if type(c.count) == "table" then
        local k = 0
        for _, h in ipairs(c.count) do
            local v = flag_value(game, h)
            if v and v ~= 0 then k = k + 1 end
        end
        return k >= (tonumber(c.min) or #c.count)
    end
    if c.text then
        local t = flag_text(game, c.text)
        return t ~= nil and type(c.contains) == "string" and t:find(c.contains, 1, true) ~= nil
    end
    if c.flag then
        local f = (c.file == "caption") and caption or game
        local v = flag_value(f, c.flag)
        return v ~= nil and v >= (tonumber(c.min) or 1)
    end
    return false
end

--- Steam-style progress for a rule ("12 / 900"): each "count" condition adds how many of its flags are set and each
--- "flag" condition adds its value, both capped at what the rule needs. Returns current, maximum.
local function cond_progress(c, game, caption)
    if type(c) ~= "table" or c.noProgress == true then return 0, 0 end
    if type(c.any) == "table" then
        -- Either-or: show the alternative with the most steps; if any alternative is already met, it's full.
        local best_cur, best_max, done = 0, 0, false
        for _, sub in ipairs(c.any) do
            local sc, sm = cond_progress(sub, game, caption)
            if sm > best_max then best_cur, best_max = sc, sm end
            if game and rule_met(sub, game, caption) then done = true end
        end
        if done then best_cur = best_max end
        return best_cur, best_max
    end
    if type(c.count) == "table" then
        local k = 0
        for _, h in ipairs(c.count) do
            local v = flag_value(game, h)
            if v and v ~= 0 then k = k + 1 end
        end
        local m = tonumber(c.min) or #c.count
        return math.min(k, m), m
    end
    if c.flag then
        local f = (c.file == "caption") and caption or game
        local m = tonumber(c.min) or 1
        -- A threshold this large is a raw encoding (e.g. float bits), not a count: one yes/no step.
        if m > 100000 then return (game and rule_met(c, game, caption)) and 1 or 0, 1 end
        return math.min(flag_value(f, c.flag) or 0, m), m
    end
    return 0, 0
end

local function rule_progress(r, game, caption)
    local cur, max = 0, 0
    for _, c in ipairs(r.all or {}) do
        local cc, cm = cond_progress(c, game, caption)
        cur, max = cur + cc, max + cm
    end
    return cur, max
end

local function load_save_rules(id)
    local path = fs.join(data_dir(), "saverules", id .. ".json")
    local st = stamp_of(path)
    local cached = save_rules_cache[path]
    if cached and cached.stamp == st then return cached.rules end
    local rules = read_json(path)
    save_rules_cache[path] = { stamp = st, rules = rules }
    return rules
end

--- Evaluates every rule for one save slot, re-reading it only when its files changed.
local function slot_result(dir, rules)
    local game_path, caption_path = fs.join(dir, "game_data.sav"), fs.join(dir, "caption.sav")
    local st = stamp_of(game_path) .. "|" .. stamp_of(caption_path)
    local cached = save_slot_cache[dir]
    if cached and cached.stamp == st and cached.rules == rules then return cached end
    local game = parse_flag_file(game_path)
    local caption = parse_flag_file(caption_path)
    local mode = "normal"
    if rules.modeFlag then
        local v = flag_value(caption, rules.modeFlag)
        if v and v ~= 0 then mode = "master" end
    end
    local met, progress = {}, {}
    if game then
        for id, r in pairs(rules.achievements or {}) do
            if type(r) == "table" and (r.mode or "normal") == mode then
                local ok = true
                for _, c in ipairs(r.all or {}) do
                    if not rule_met(c, game, caption) then ok = false; break end
                end
                if ok then met[tonumber(id)] = true end
                if r.progress == true then progress[tonumber(id)] = { rule_progress(r, game, caption) } end
            end
        end
    end
    local res = { stamp = st, rules = rules, mode = mode, met = met, progress = progress }
    save_slot_cache[dir] = res
    return res
end

--- Every save slot folder under a save root: <root>\<user id>\<slot>\game_data.sav (or slots directly in root).
local function save_slots(root)
    local slots = {}
    local function scan(dir, depth)
        for _, e in ipairs(fs.list(dir) or {}) do
            if e.is_directory then
                if fs.exists(fs.join(e.path, "game_data.sav")) then slots[#slots + 1] = e.path
                elseif depth < 2 then scan(e.path, depth + 1) end
            end
        end
    end
    scan(root, 0)
    return slots
end

local function savefile_unlocks(src)
    local id = tostring(src.npCommId or "")
    if id == "" or id:find("[/\\]") or id:find("%.%.") then return {} end
    local rules = load_save_rules(id)
    if type(rules) ~= "table" then return {} end
    local store_path = fs.join(data_dir(), "saverules", id .. ".unlocks.json")
    local stored = read_json(store_path)
    if stored == nil and fs.exists(store_path) then stored = nil else stored = stored or {} end
    local changed = false
    if stored then
        -- Capstones ("requiresAll") also need every other achievement in the set unlocked first.
        local capstones = {}
        for id, r in pairs(rules.achievements or {}) do
            if type(r) == "table" and r.requiresAll == true then capstones[tostring(id)] = true end
        end
        local capstone_met = {}
        for _, dir in ipairs(save_slots(src.unlockFile)) do
            local r = slot_result(dir, rules)
            for tid in pairs(r.met) do
                local key = tostring(tid)
                if capstones[key] then capstone_met[key] = true
                elseif stored[key] == nil then stored[key] = utils.time_ms(); changed = true end
            end
        end
        for key in pairs(capstone_met) do
            if stored[key] == nil then
                local all_done = true
                for id in pairs(rules.achievements or {}) do
                    local k = tostring(id)
                    if k ~= key and not capstones[k] and stored[k] == nil then all_done = false; break end
                end
                if all_done then stored[key] = utils.time_ms(); changed = true end
            end
        end
        if changed then write_json(store_path, stored) end
    end
    local out = {}
    for key, t in pairs(stored or read_json(store_path) or {}) do
        local n = tonumber(key)
        if n and type(t) == "number" then out[n] = t end
    end
    return out
end

--- Progress toward save-file achievements: { [trophyId] = { current, max } }, the furthest slot of the rule's mode.
local function savefile_progress(src)
    local out = {}
    if type(src) ~= "table" or src.emulator ~= "savefile" then return out end
    local id = tostring(src.npCommId or "")
    if id == "" or id:find("[/\\]") or id:find("%.%.") or not fs.exists(src.unlockFile or "") then return out end
    local rules = load_save_rules(id)
    if type(rules) ~= "table" then return out end
    for _, dir in ipairs(save_slots(src.unlockFile)) do
        local r = slot_result(dir, rules)
        for tid, p in pairs(r.progress or {}) do
            local best = out[tid]
            if best == nil or p[1] > best[1] then out[tid] = { p[1], p[2] } end
        end
    end
    -- Rules with progress but no save yet still show 0 / max.
    for tid, r in pairs(rules.achievements or {}) do
        local n = tonumber(tid)
        if type(r) == "table" and r.progress == true and out[n] == nil then
            local _, max = rule_progress(r, nil, nil)
            out[n] = { 0, max }
        end
    end
    return out
end

local function emulator_unlocks(src)
    if type(src) ~= "table" or type(src.unlockFile) ~= "string" or src.unlockFile == "" then return nil end
    if not fs.exists(src.unlockFile) then return {} end
    if src.emulator == "savefile" then
        local ok, res = pcall(savefile_unlocks, src)
        if ok then return res end
        logger:error("Couldn't read save-file achievements from " .. src.unlockFile .. ": " .. tostring(res))
        return {}
    end
    local reader = shadps4_unlocks
    if src.emulator == "rpcs3" then reader = rpcs3_unlocks
    elseif src.emulator == "retroachievements" then reader = ra_unlocks
    elseif src.emulator == "ps3recomp" then reader = ps3recomp_unlocks end
    local ok, res = pcall(reader, src.unlockFile)
    if ok then return res end
    logger:error("Couldn't read trophies from " .. src.unlockFile .. ": " .. tostring(res))
    return {}
end

--- Merged unlock times (ms) for one game: the emulator's record first, then hand-made unlocks.
local function merged_unlocks(appid, game, manual)
    local out = {}
    local emu = emulator_unlocks(game.trophySource)
    for _, a in ipairs(game.achievements or {}) do
        local t = nil
        if emu and type(a.trophyId) == "number" then t = emu[a.trophyId] end
        if t == nil and type(manual) == "table" then t = manual[a.id] end
        if t ~= nil then out[a.id] = t end
    end
    return out
end

--- Unlocks for every game that syncs from an emulator: { "<appid>": { "<achievement id>": ms } }.
--- The frontend polls this to pop "Achievement Unlocked" while you play.
function PollUnlocks(payload)
    local games = read_json(games_file()) or {}
    local manual = read_json(unlocks_file()) or {}
    -- RetroAchievements is a web API: only ask it about games that are running right now, at most once a minute.
    local running = {}
    for _, id in ipairs(args(payload).running or {}) do running[tostring(id)] = true end
    local out = {}
    for appid, game in pairs(games) do
        if type(game) == "table" and type(game.trophySource) == "table" then
            if running[appid] then ra_maybe_refresh(game.trophySource, 60000) end
            out[appid] = merged_unlocks(appid, game, manual[appid])
        end
    end
    return json.encode(out)
end

-- ---------------------------------------------------------------------------------------------
-- Live saving: the app drops requests in extras\live\request-<id>.json; the frontend applies them through
-- Steam's own API (no restart) and answers in response-<id>.json. The heartbeat tells the app we're alive.
-- ---------------------------------------------------------------------------------------------

local function live_dir() return fs.join(data_dir(), "live") end

--- Called every ~1.5 s by the frontend. Returns the oldest waiting request, or null.
function LiveTick(payload)
    local dir = live_dir()
    fs.create_directories(dir)
    utils.write_file(fs.join(dir, "heartbeat"), tostring(utils.time_ms()))
    local entries = fs.list(dir) or {}
    local oldest = nil
    for _, e in ipairs(entries) do
        local name = e.name or (e.path and e.path:match("[^/\\]+$")) or ""
        if name:match("^request%-.+%.json$") and (oldest == nil or name < oldest) then oldest = name end
    end
    if oldest == nil then return "null" end
    local raw = utils.read_file(fs.join(dir, oldest))
    if raw == nil or raw == "" then return "null" end
    return raw
end

--- The frontend finished a request. payload: { id, result }
function LiveDone(payload)
    local a = args(payload)
    local id = tostring(a.id or "")
    if id == "" or id:find("[/\\]") then return "false" end
    local dir = live_dir()
    local tmp = fs.join(dir, "response-" .. id .. ".tmp")
    utils.write_file(tmp, json.encode(a.result or {}))
    fs.rename(tmp, fs.join(dir, "response-" .. id .. ".json"))
    fs.remove(fs.join(dir, "request-" .. id .. ".json"))
    return "true"
end

--- An image file as base64, for SetCustomArtworkForApp. Only files inside Steam's grid folders are allowed.
function ReadImage(payload)
    local path = tostring(args(payload).path or "")
    local lower = path:lower():gsub("\\", "/")
    if not lower:find("/userdata/%d+/config/grid/") or lower:find("%.%.") then return "" end
    local bytes = utils.read_file(path)
    if bytes == nil then return "" end
    return utils.base64_encode(bytes)
end

-- ---------------------------------------------------------------------------------------------
-- Steam's own playtime for shortcuts. Steam records it in userdata\<id>\config\localconfig.vdf
-- (apps\<signed appid>\Playtime, Playtime2wks, in minutes) but only shows it on the game page; the
-- library (Recent Games hover card, sorting) reads the app overview, which stays at 0. The frontend copies
-- these numbers onto the overviews.
-- ---------------------------------------------------------------------------------------------

local function localconfig_path()
    local userdata = fs.join(millennium.steam_path(), "userdata")
    local best, best_time = nil, -1
    for _, e in ipairs(fs.list(userdata) or {}) do
        if e.is_directory and (e.name or ""):match("^%d+$") and e.name ~= "0" then
            local p = fs.join(e.path, "config", "localconfig.vdf")
            if fs.exists(p) then
                local t = tonumber(fs.last_write_time(p)) or 0
                if t > best_time then best, best_time = p, t end
            end
        end
    end
    return best
end

--- payload: { appids = { "123", ... } } -> { "<appid>": { minutesForever, minutesLastTwoWeeks, lastPlayed } }
function GetSteamPlaytimes(payload)
    local path = localconfig_path()
    local text = path and utils.read_file(path)
    local out = {}
    if not text then return json.encode(out) end
    for _, appid in ipairs(args(payload).appids or {}) do
        local n = tonumber(appid)
        if n then
            -- Shortcuts are stored under their appid as a signed 32-bit number.
            local key = n >= 2147483648 and tostring(math.floor(n - 4294967296)) or tostring(math.floor(n))
            local s = text:find('"' .. key .. '"%s*{')
            if s then
                -- Walk to this block's closing brace (it can contain nested blocks such as "cloud").
                local open = text:find("{", s, true)
                local depth, i = 0, open
                repeat
                    local c = text:sub(i, i)
                    if c == "{" then depth = depth + 1 elseif c == "}" then depth = depth - 1 end
                    i = i + 1
                until depth == 0 or i > #text
                local block = text:sub(open, i)
                local forever = tonumber(block:match('"Playtime"%s*"(%d+)"'))
                if forever then
                    out[tostring(appid)] = {
                        minutesForever = forever,
                        minutesLastTwoWeeks = tonumber(block:match('"Playtime2wks"%s*"(%d+)"')) or 0,
                        lastPlayed = tonumber(block:match('"LastPlayed"%s*"(%d+)"')) or 0,
                    }
                end
            end
        end
    end
    return json.encode(out)
end

--- The shortcut's portrait library capsule (grid\<appid>p.*) as a data: URI, or "" if it has none.
--- payload: { appid }
function GetCapsule(payload)
    local appid = tostring(args(payload).appid or "")
    if not appid:match("^%d+$") then return "" end
    local cfg = localconfig_path()
    if not cfg then return "" end
    local grid = fs.join(fs.parent_path(cfg), "grid")
    for _, ext in ipairs({ "png", "jpg", "jpeg", "webp" }) do
        local p = fs.join(grid, appid .. "p." .. ext)
        if fs.exists(p) then
            local bytes = utils.read_file(p)
            if bytes then return "data:" .. (MIME[ext] or "image/png") .. ";base64," .. utils.base64_encode(bytes) end
        end
    end
    return ""
end

-- ---------------------------------------------------------------------------------------------
-- Launch menu: several ways to start one shortcut, picked in a Steam-style dialog when Play is pressed.
-- ---------------------------------------------------------------------------------------------

local function launch_choice_file() return fs.join(data_dir(), "launch_choice.json") end

--- payload: { appid } -> { options = [...], base = {...}, choice = index|null } or null when there's no menu.
function GetLaunch(payload)
    local appid = tostring(args(payload).appid or "")
    local game = (read_json(games_file()) or {})[appid]
    if type(game) ~= "table" or type(game.launchOptions) ~= "table" or #game.launchOptions < 2 then return "null" end
    local choices = read_json(launch_choice_file()) or {}
    return json.encode({ options = game.launchOptions, base = game.launchBase, choice = choices[appid] })
end

--- Remember (or forget, with index = null) the option to start without asking. payload: { appid, index }
function SetLaunchChoice(payload)
    local a = args(payload)
    local appid = tostring(a.appid or "")
    if appid == "" then return "false" end
    local all = read_json(launch_choice_file())
    if all == nil and fs.exists(launch_choice_file()) then return "false" end -- unreadable: don't overwrite
    all = all or {}
    if type(a.index) == "number" then all[appid] = math.floor(a.index) else all[appid] = nil end
    return write_json(launch_choice_file(), all) and "true" or "false"
end

--- A cheap fingerprint of everything one game's page shows: games.json, unlocks.json and the game's unlock
--- source (emulator file, RetroAchievements cache, or the save rules, their unlocks and every save slot).
--- The page polls this and reloads only when it changes. payload: { "appid": "123" }
local stamp_games = { stamp = nil, games = nil }
function GetStamp(payload)
    local appid = tostring(args(payload).appid or "")
    local gs = stamp_of(games_file())
    if stamp_games.stamp ~= gs then
        local games = read_json(games_file())
        -- Unreadable mid-write: don't cache, so the next poll tries again.
        stamp_games = { stamp = games and gs or nil, games = games }
    end
    local parts = { gs, stamp_of(unlocks_file()) }
    local game = stamp_games.games and stamp_games.games[appid]
    local src = type(game) == "table" and game.trophySource or nil
    if type(src) == "table" then
        local id = tostring(src.npCommId or "")
        if src.emulator == "savefile" then
            local rules_dir = fs.join(data_dir(), "saverules")
            parts[#parts + 1] = stamp_of(fs.join(rules_dir, id .. ".json"))
            parts[#parts + 1] = stamp_of(fs.join(rules_dir, id .. ".unlocks.json"))
            if type(src.unlockFile) == "string" and src.unlockFile ~= "" then
                for _, dir in ipairs(save_slots(src.unlockFile)) do
                    parts[#parts + 1] = stamp_of(fs.join(dir, "game_data.sav")) .. "/" .. stamp_of(fs.join(dir, "caption.sav"))
                end
            end
        elseif type(src.unlockFile) == "string" and src.unlockFile ~= "" then
            parts[#parts + 1] = stamp_of(src.unlockFile)
        end
    end
    return table.concat(parts, "|")
end

--- Everything the game page needs for one shortcut, or null.
--- payload: { "appid": "123" }
function GetGame(payload)
    local appid = tostring(args(payload).appid or "")
    local games = read_json(games_file()) or {}
    local game = games[appid]
    if type(game) ~= "table" then return "null" end

    -- Opening an RA game's page refreshes its unlocks if they're more than 5 minutes old.
    ra_maybe_refresh(game.trophySource, 300000)
    local unlocks = merged_unlocks(appid, game, (read_json(unlocks_file()) or {})[appid])
    local ok_p, progress = pcall(savefile_progress, game.trophySource)
    if not ok_p then logger:error("Couldn't read achievement progress: " .. tostring(progress)); progress = {} end
    local list = {}
    for _, a in ipairs(game.achievements or {}) do
        local p = type(a.trophyId) == "number" and progress[a.trophyId] or nil
        list[#list + 1] = {
            progressCurrent = p and p[1] or nil,
            progressMax = p and p[2] or nil,
            id = a.id,
            name = a.name or "",
            description = a.description or "",
            hidden = a.hidden == true,
            percent = a.globalPercent,
            grade = a.grade or "",
            points = a.points,
            dlc = type(a.dlc) == "string" and a.dlc or "",
            icon = icon_uri(appid, a.icon),
            iconGray = icon_uri(appid, a.iconGray),
            unlockedAt = unlocks[a.id],
        }
    end
    local src = game.trophySource
    local source = nil
    if type(src) == "table" then source = { emulator = src.emulator, title = src.title, npCommId = src.npCommId } end

    return json.encode({
        shortDescription = game.shortDescription or "",
        description = game.description or "",
        developer = game.developer or "",
        publisher = game.publisher or "",
        releaseDate = game.releaseDate or "",
        genres = game.genres or {},
        sourceAppId = game.sourceAppId or 0,
        trophySource = source,
        achievements = list,
    })
end

--- Locks or unlocks one achievement. payload: { "appid": "123", "id": "ACH_1", "unlocked": true }
function SetUnlocked(payload)
    local a = args(payload)
    local appid, id = tostring(a.appid or ""), tostring(a.id or "")
    if appid == "" or id == "" then return "false" end

    -- If the file exists but can't be read (e.g. the app is replacing it this instant), retry, and never
    -- overwrite it with an empty table: that would wipe every other game's unlocks.
    local all = read_json(unlocks_file())
    for _ = 1, 10 do
        if all ~= nil or not fs.exists(unlocks_file()) then break end
        utils.sleep(50)
        all = read_json(unlocks_file())
    end
    if all == nil then
        if fs.exists(unlocks_file()) then
            logger:error("unlocks.json couldn't be read; not saving to avoid losing data")
            return "false"
        end
        all = {}
    end
    local mine = all[appid]
    if type(mine) ~= "table" then mine = {} end

    local stamp = nil
    if a.unlocked then
        stamp = mine[id] or utils.time_ms()
        mine[id] = stamp
    else
        mine[id] = nil
    end
    if next(mine) == nil then all[appid] = nil else all[appid] = mine end

    if not write_json(unlocks_file(), all) then return "false" end
    return json.encode({ unlockedAt = stamp })
end

local function on_load()
    logger:info("Shortcut Studio plugin loaded; data folder: " .. data_dir())
    millennium.ready()
end

local function on_unload() end
local function on_frontend_loaded() end

return {
    on_load = on_load,
    on_unload = on_unload,
    on_frontend_loaded = on_frontend_loaded,
}
