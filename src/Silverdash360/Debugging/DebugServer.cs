using System;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// A TCP port that takes commands while the dashboard runs, for testing
    /// and debugging on a console without a development kit. Each line sent
    /// is a command (see <see cref="DebugRequest"/>); each reply is one line
    /// of JSON: {"ok":true,"command":...,"ms":...,"result":...} or
    /// {"ok":false,...,"error":...}. "help" lists the commands.
    ///
    /// It's built in only with the DEBUG_SERVER symbol (the DebugServer
    /// build property) and takes no password: anyone on the network can
    /// read what the dashboard knows and drive it.
    /// </summary>
    public sealed class DebugServer
    {
        public const int DefaultPort = 4601;
        /// <summary>Ports tried from DefaultPort on, for when one is taken.</summary>
        public const int PortsTried = 10;
        public const int ProtocolVersion = 1;

        private readonly object sync = new object();
        private NativeSocket listener;
        private int clients;

        public DebugServer(DashboardContext context)
        {
            Context = context;
            Commands = new DebugCommandSet();
            Started = DateTime.UtcNow;
            Port = DefaultPort;
        }

        /// <summary>The one the app started, or null.</summary>
        public static DebugServer Current { get; private set; }

        public DashboardContext Context { get; private set; }
        public DebugCommandSet Commands { get; private set; }
        public DateTime Started { get; private set; }
        public int Port { get; set; }
        public int ListeningPort { get; private set; }
        public bool IsRunning { get { return listener != null; } }
        public int ClientCount { get { return clients; } }

        /// <summary>Starts the server with the standard commands; a failure is logged, not thrown.</summary>
        public static void StartDefault(DashboardContext context)
        {
            DebugServer server = new DebugServer(context);
            foreach (IDebugCommands group in new IDebugCommands[]
            {
                new SystemCommands(server), new LibraryCommands(context), new ApiCommands(context),
                new UiCommands(context), new BenchCommands(context), new AppCommands(context), new MediaCommands(context),
                new ThemeCommands(context), new SysCacheCommands(context),
            })
            {
                group.Register(server.Commands);
            }
            Current = server;
            try
            {
                server.Start();
            }
            catch (Exception e)
            {
                Log.Write("Debug server didn't start: {0}", e.Message);
            }
        }

        public void Start()
        {
            lock (sync)
            {
                if (listener != null)
                {
                    return;
                }
                // The next ports when one is taken, as another program on a PC
                // running xenia can have it; settings shows the one in use.
                for (int attempt = 0; listener == null; attempt++)
                {
                    if (Port + attempt == Services.Media.LocalMediaServer.Port)
                    {
                        continue;
                    }
                    try
                    {
                        listener = NativeSocket.Listen(Port + attempt, 2);
                    }
                    catch (System.IO.IOException)
                    {
                        if (attempt >= PortsTried - 1)
                        {
                            throw;
                        }
                    }
                }
                uint ignored;
                int port;
                listener.LocalEndPoint(out ignored, out port);
                ListeningPort = port != 0 ? port : Port;
            }
            new Thread(AcceptLoop) { IsBackground = true, Name = "Debug server" }.Start(listener);
            Log.Write("Debug server listening on port {0}", ListeningPort);
        }

        private void AcceptLoop(object state)
        {
            NativeSocket socket = (NativeSocket)state;
            while (true)
            {
                NativeSocket client;
                try
                {
                    client = socket.Accept();
                }
                catch (Exception e)
                {
                    Log.Write("Debug server stopped accepting: {0}", e.Message);
                    return;
                }
                new Thread(Serve) { IsBackground = true, Name = "Debug client" }.Start(client);
            }
        }

        private void Serve(object state)
        {
            NativeSocket client = (NativeSocket)state;
            Interlocked.Increment(ref clients);
            Log.Write("Debug client connected");
            using (NativeBuffer input = new NativeBuffer(4096))
            {
                try
                {
                    client.Send(Json.Write(new JsonObject
                    {
                        { "ok", true },
                        { "command", "hello" },
                        { "result", new JsonObject
                            {
                                { "server", "Silverdash360 debug server" },
                                { "protocol", ProtocolVersion },
                                { "commands", Commands.All.Count },
                            }
                        },
                    }) + "\n");
                    StringBuilder pending = new StringBuilder();
                    string line;
                    while ((line = ReadLine(client, input, pending)) != null)
                    {
                        if (line.Trim().Length == 0)
                        {
                            continue;
                        }
                        DebugRequest request = new DebugRequest(line);
                        if (request.Command == "quit" || request.Command == "exit")
                        {
                            client.Send(Json.Write(new JsonObject { { "ok", true }, { "command", request.Command } }) + "\n");
                            break;
                        }
                        Activity.Debug = request.Command + " (handling)";
                        string reply = Handle(request);
                        Activity.Debug = request.Command + " (sending " + reply.Length + " chars)";
                        client.Send(reply + "\n");
                        Activity.Debug = request.Command + " (done)";
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Debug client dropped: {0}", e.Message);
                }
                finally
                {
                    client.Dispose();
                    Interlocked.Decrement(ref clients);
                    Log.Write("Debug client disconnected");
                }
            }
        }

        /// <summary>Runs a command and makes its reply.</summary>
        public string Handle(DebugRequest request)
        {
            DebugCommand command = Commands.Find(request.Command);
            int start = Environment.TickCount;
            JsonObject reply = new JsonObject();
            try
            {
                if (command == null)
                {
                    throw new DebugException("Unknown command " + request.Command + ". \"help\" lists them.");
                }
                object result = command.Run(request);
                reply.Add("ok", true);
                reply.Add("command", request.Command);
                reply.Add("ms", Environment.TickCount - start);
                reply.Add("result", result);
            }
            catch (Exception e)
            {
                reply = new JsonObject
                {
                    { "ok", false },
                    { "command", request.Command },
                    { "ms", Environment.TickCount - start },
                    { "error", e is DebugException ? e.Message : e.GetType().Name + ": " + e.Message },
                };
                if (!(e is DebugException))
                {
                    Log.Write("Debug command {0} failed: {1}", request.Line, e);
                }
            }
            return Json.Write(reply);
        }

        private static string ReadLine(NativeSocket client, NativeBuffer input, StringBuilder pending)
        {
            while (true)
            {
                string buffered = pending.ToString();
                int end = buffered.IndexOf('\n');
                if (end >= 0)
                {
                    pending.Remove(0, end + 1);
                    return buffered.Substring(0, end).TrimEnd('\r');
                }
                if (pending.Length > 16384)
                {
                    return null;
                }
                int received = client.Receive(input, input.Length);
                if (received == 0)
                {
                    return null;
                }
                pending.Append(Encoding.UTF8.GetString(input.Bytes, 0, received));
            }
        }
    }
}
