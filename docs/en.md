# Debug Client

Debug Client connects a local MCP client to the running Cthangover process. The game publishes the available commands after loading the active collection.

## Enable the connection

Add `debug_client` to the active collection and apply the mod set, or start a single game process with `--with-mods=debug_client` after Godot's `--` separator. Start the relay on the same operating system and under the same account as the game. On Linux, both processes need the same `TMPDIR` (usually `/tmp`).

Linux MCP configuration

```json
{
  "mcpServers": {
    "cthangover": {
      "command": "python3",
      "args": ["-B", "/absolute/path/to/DebugClient/mcp_stdio.py"]
    }
  }
}
```

On Windows set `command` to `py` and use `["-3", "-B", "C:/path/to/DebugClient/mcp_stdio.py"]` as the argument list. The relay normally discovers the endpoint. If several instances are present, append `"--pid", "12345"` with the intended game PID. `--connect-timeout` sets the startup wait in seconds (default 5).

## Read the running game

```text
diagnose({"mod":"proto_land_pack"})
inspect({"area":"node","path":"/root/SceneManager"})
describe({"query":"settings"})
run({"command":"settings.read"})
```

`diagnose` returns compiler and runtime diagnostics for a mod. `inspect` reads a node, cache, runtime, resource or mod. `describe` searches the live command catalogue. `run` invokes a registered command. Supply `provider` for a command supplied by a mod and use `arguments` for its JSON parameters.

For a command that changes state, supply your own UUID as `operation_id`. After a disconnect query `run({"command":"operation.status","operation_id":"<uuid>"})` before repeating the request. The game keeps a bounded recent operation history.

Each game process accepts one MCP session at a time. After turning the mod off and back on, start a fresh client session. The relay writes its own diagnostics to stderr.
