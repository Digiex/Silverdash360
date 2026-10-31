using System;
using System.Collections.Generic;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services;

namespace Silverdash360.Debugging
{
    /// <summary>The server itself, the console, memory and the log.</summary>
    public sealed class SystemCommands : IDebugCommands
    {
        private const int PageSize = 4096;

        private readonly DebugServer server;

        public SystemCommands(DebugServer server)
        {
            this.server = server;
        }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("help", "", "Lists the commands.", Help);
            commands.Add("ping", "", "Answers pong.", r => "pong");
            commands.Add("echo", "<text>", "Sends the text back.", r => r.Text);
            commands.Add("info", "", "The dashboard, the console and the server.", Info);
            commands.Add("memory", "[collect]", "Managed heap and the kernel's memory counts; collect runs the GC first.", Memory);
            commands.Add("log", "[count]", "The latest lines the dashboard logged (at most " + Log.Kept + ").", r => Log.Recent(r.Int(0, 50)));
            commands.Add("status", "<text>", "Shows a message in the hub's footer.", r =>
            {
                UiThread.Invoke(() => server.Context.ShowStatus(r.Text));
                return null;
            });
        }

        private object Help(DebugRequest request)
        {
            List<JsonObject> list = new List<JsonObject>();
            foreach (DebugCommand command in server.Commands.All)
            {
                list.Add(new JsonObject { { "name", command.Name }, { "usage", command.Usage }, { "help", command.Help } });
            }
            return list;
        }

        private object Info(DebugRequest request)
        {
            SystemService system = server.Context.System;
            return new JsonObject
            {
                { "app", typeof(App).Assembly.FullName },
                { "protocol", DebugServer.ProtocolVersion },
                { "systemVersion", system.SystemVersion },
                { "titleId", system.CurrentTitleId },
                { "language", system.Language },
                { "consoleTime", system.ConsoleTime },
                { "uptimeMs", (long)(DateTime.UtcNow - server.Started).TotalMilliseconds },
                { "address", server.Context.Ftp.Address },
                { "debugPort", server.ListeningPort },
                { "debugClients", server.ClientCount },
                { "ftpRunning", server.Context.Ftp.IsRunning },
                { "processors", Environment.ProcessorCount },
            };
        }

        private static object Memory(DebugRequest request)
        {
            bool collect = request.Arg(0, "") == "collect";
            long before = GC.GetTotalMemory(false);
            JsonObject result = new JsonObject { { "managedBytes", before } };
            if (collect)
            {
                result.Add("managedBytesAfterCollect", GC.GetTotalMemory(true));
            }
            using (NativeBuffer stats = new NativeBuffer(104))
            {
                stats.SetU32(0, 104);
                int status = Kernel.MmQueryStatistics(stats.Address);
                result.Add("kernelStatus", status);
                if (status == 0)
                {
                    result.Add("totalPhysicalBytes", (long)stats.U32(4) * PageSize);
                    result.Add("kernelBytes", (long)stats.U32(8) * PageSize);
                    result.Add("title", Section(stats, 12));
                    result.Add("system", Section(stats, 56));
                }
            }
            return result;
        }

        private static JsonObject Section(NativeBuffer stats, int o)
        {
            return new JsonObject
            {
                { "availableBytes", (long)stats.U32(o) * PageSize },
                { "virtualBytes", (long)stats.U32(o + 4) },
                { "reservedVirtualBytes", (long)stats.U32(o + 8) },
                { "physicalBytes", (long)stats.U32(o + 12) * PageSize },
                { "poolBytes", (long)stats.U32(o + 16) * PageSize },
                { "stackBytes", (long)stats.U32(o + 20) * PageSize },
                { "imageBytes", (long)stats.U32(o + 24) * PageSize },
                { "heapBytes", (long)stats.U32(o + 28) * PageSize },
                { "virtualPageBytes", (long)stats.U32(o + 32) * PageSize },
                { "pageTableBytes", (long)stats.U32(o + 36) * PageSize },
                { "cacheBytes", (long)stats.U32(o + 40) * PageSize },
            };
        }
    }
}
