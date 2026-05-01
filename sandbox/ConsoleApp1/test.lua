-- string.dump round-trip
local function add(a, b) return a + b end
local bytes = string.dump(add)
print("dump bytes:", #bytes)
local f = load(bytes)
print("loaded:", type(f))
print("call result:", f(3, 4))

-- os.execute
print("shell available:", os.execute())

local ok, kind, code = os.execute("true")
print("true:", ok, kind, code)

local ok2, kind2, code2 = os.execute("exit 7")
print("exit 7:", ok2, kind2, code2)
