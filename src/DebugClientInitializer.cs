using System;
using System.Linq;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Games;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Host;
using Cthangover.Core.Utils;
using Cthangover.Mods.DebugClient.Mcp;
using Godot;

namespace Cthangover.Mods.DebugClient
{
    using Cthangover.Core.Games.LaunchParameters;

    /// <summary>Owns the MCP listener for the lifetime of the explicitly selected mod.</summary>
    public sealed class DebugClientInitializer : IModInitializer, IModUnloadAware
    {
        private const string ModId = "debug_client";
        private McpPipeServer server;

        /// <summary>The bridge starts after the load batch, so individual assembly notifications do not affect it.</summary>
        public void OnModLoaded(string modId) { }

        /// <summary>Starts the listener once when the launch or active collection explicitly selects the mod.</summary>
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || ApplicationExit.IsQuitting || server != null)
                return;

            var launch = GameLaunchArgs.Current;
            if (launch.Contains<NoDevelopmentControlParameter>() || GameLaunchArgs.Startup.Contains<NoDevelopmentControlParameter>()
                || (!launch.Get<WithModsParameter>().Value.Contains(ModId, StringComparer.OrdinalIgnoreCase)
                    && !ActiveGame.ModIds.Contains(ModId, StringComparer.OrdinalIgnoreCase)))
                return;

            server = new McpPipeServer(DebugHost.ExecuteAsync, LogWarning);
            ApplicationExit.Preparing += StopAsync;
            GameLogger.Log("DEBUG_CLIENT", "MCP listening on " + server.PipeName);
        }

        /// <summary>Revokes the external endpoint before the loader releases the adapter's assembly context.</summary>
        public void OnModUnloading()
        {
            ApplicationExit.Preparing -= StopAsync;
            // StopAsync closes the handle before returning; the worker drains off the game thread.
            _ = StopAsync(null);
        }

        /// <summary>The bridge keeps no references to other mod assemblies between commands.</summary>
        public void OnModUnloaded(string modId) { }

        private Task StopAsync(SceneTree tree)
        {
            var running = server;
            server = null;
            return running?.StopAsync() ?? Task.CompletedTask;
        }

        private static void LogWarning(string message)
        {
            GameLogger.Log("DEBUG_CLIENT", message, LogLevel.Warning);
        }
    }
}
