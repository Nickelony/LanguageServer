# LuaLS integration fixture

The live integration tests are opt-in and require a LuaLS archive. Configure the archive with
the `NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE` environment variable. When the variable is not
set, the tests also look for the neutral repository asset `Tests/TestAssets/LuaLS.zip`.

The archive must contain `bin/lua-language-server.exe` on Windows or
`bin/lua-language-server` on Unix-like systems. If no fixture is present, the tests skip
explicitly with a prerequisite message; they are not deleted or silently treated as passing.
