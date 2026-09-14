return function(H)
    local open, remove, rename = io.open, os.remove, os.rename
    local files, callbacks = {}, {}
    local root = ".\\AbioticEditorLiveAgent\\ipc\\"
    io.open = function(path, mode)
        if mode == "rb" then
            if not files[path] then return nil end
            return { read = function() return files[path] end, close = function() end }
        end
        return { write = function(_, text) files[path] = text end, close = function() end }
    end
    os.remove = function(path) files[path] = nil end
    os.rename = function(source, target) files[target] = files[source]; files[source] = nil end
    H.mod.handlers["test.delayed"] = function(payload, respond)
        callbacks[#callbacks + 1] = function() respond(payload.value) end
    end
    local ok, err = pcall(function()
        for _, id in ipairs({ "first", "second" }) do
            files[root .. "request.json"] = H.json.encode({ requestId = id, cmd = "test.delayed", payload = { value = id } })
            H.poll()
        end
        H.eq(#callbacks, 2, "both asynchronous requests scheduled")
        callbacks[2]()
        local second = H.json.decode(files[root .. "response.json"])
        H.eq(second.requestId, "second", "second callback keeps its own id")
        callbacks[1]()
        local first = H.json.decode(files[root .. "response.json"])
        H.eq(first.requestId, "first", "late callback keeps original id")
        H.eq(first.result, "first", "late result paired with original id")
        files[root .. "request.json"] = H.json.encode({ requestId = "unknown", cmd = "test.unknown" })
        H.poll()
        H.eq(H.json.decode(files[root .. "response.json"]).requestId, "unknown", "error responses keep request id")
    end)
    io.open, os.remove, os.rename = open, remove, rename
    H.mod.handlers["test.delayed"] = nil
    if not ok then error(err) end
end
