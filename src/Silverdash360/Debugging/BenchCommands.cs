using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverlight360;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// Timed runs of the dashboard's own work, on the library it has, and of
    /// interpreted code against the framework's. Each runs once untimed,
    /// then the number of times asked; times are in milliseconds.
    /// </summary>
    public sealed class BenchCommands : IDebugCommands
    {
        private sealed class Benchmark
        {
            public string Name;
            public string Help;
            public int Iterations;
            /// <summary>Makes what the run needs and returns one iteration.</summary>
            public Func<Action> Prepare;
        }

        private readonly DashboardContext context;
        private readonly List<Benchmark> benchmarks = new List<Benchmark>();

        public BenchCommands(DashboardContext context)
        {
            this.context = context;
            Add("interp.loop", "A loop over 64 KB of bytes in app code, which is interpreted.", 3, () =>
            {
                byte[] bytes = new byte[0x10000];
                return () =>
                {
                    int sum = 0;
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        sum += bytes[i];
                    }
                };
            });
            Add("framework.indexof", "Array.IndexOf over 1 MB, in the framework's precompiled code.", 50, () =>
            {
                byte[] bytes = new byte[0x100000];
                return () => Array.IndexOf(bytes, (byte)1);
            });
            Add("utf16.decode", "Decoding a package's display name.", 2000, () =>
            {
                byte[] record = SampleRecord();
                return () => BigEndian.Utf16(record, 0x08, 0x80);
            });
            Add("record.parse", "A package record into a ContentItem.", 1000, () =>
            {
                byte[] record = SampleRecord();
                return () => new ContentItem(record);
            });
            Add("record.write", "A package record into the cache's form.", 1000, () =>
            {
                ContentItem item = new ContentItem(SampleRecord());
                MemoryStream stream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(stream);
                return () =>
                {
                    stream.Position = 0;
                    item.WriteRecord(writer);
                };
            });
            Add("record.read", "A package record back from the cache's form.", 1000, () =>
            {
                MemoryStream stream = new MemoryStream();
                new ContentItem(SampleRecord()).WriteRecord(new BinaryWriter(stream));
                BinaryReader reader = new BinaryReader(stream);
                return () =>
                {
                    stream.Position = 0;
                    ContentItem.ReadRecord(reader);
                };
            });
            Add("metadata.parse", "A package header's metadata into a ContentMetadata.", 500, () =>
            {
                byte[] metadata = SampleMetadata();
                return () => new ContentMetadata(metadata, 0, false);
            });
            Add("cache.serialize", "The whole library into the cache file's bytes.", 3, () =>
            {
                IList<ContentItem> content = context.Library.AllContent;
                List<ExecutableItem> executables = Executables();
                IList<IList<ContentItem>> sections = new List<IList<ContentItem>> { content };
                return () => LibraryCache.Serialize(sections, executables);
            });
            Add("cache.deserialize", "The cache file's bytes back into the library.", 3, () =>
            {
                byte[] bytes = LibraryCache.Serialize(new List<IList<ContentItem>> { context.Library.AllContent }, Executables());
                return () => LibraryCache.Deserialize(new MemoryStream(bytes), null);
            });
            Add("library.search", "Searching the library for \"the\".", 20, () => () => context.Library.Search("the"));
            Add("sort.orderby", "Sorting the library's names with OrderBy and a key.", 10, () =>
            {
                List<string> names = Names();
                return () => names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            });
            Add("sort.comparison", "Sorting the library's names with a comparison lambda.", 3, () =>
            {
                List<string> names = Names();
                return () => new List<string>(names).Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            });
            Add("json.write", "Writing 100 titles as JSON, as the debug server does.", 10, () =>
            {
                List<JsonObject> titles = context.Library.Titles.Take(100).Select(t => LibraryCommands.TitleSummary(t)).ToList();
                return () => Json.Write(titles);
            });
            Add("alloc.small", "Allocating 10,000 small objects.", 10, () => () =>
            {
                object[] objects = new object[10000];
                for (int i = 0; i < objects.Length; i++)
                {
                    objects[i] = new object();
                }
            });
            Add("gc.collect", "A full garbage collection.", 5, () => () => GC.Collect());
        }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("bench.list", "", "The benchmarks and their default iterations.", r => benchmarks.Select(b => new JsonObject
            {
                { "name", b.Name }, { "iterations", b.Iterations }, { "help", b.Help },
            }).ToList());
            commands.Add("bench", "<name> [iterations]", "Runs a benchmark.", Run);
        }

        private void Add(string name, string help, int iterations, Func<Action> prepare)
        {
            benchmarks.Add(new Benchmark { Name = name, Help = help, Iterations = iterations, Prepare = prepare });
        }

        private object Run(DebugRequest request)
        {
            string name = request.Arg(0, "benchmark", true);
            Benchmark benchmark = benchmarks.FirstOrDefault(b => b.Name == name);
            if (benchmark == null)
            {
                throw new DebugException("No benchmark " + name + ". \"bench.list\" lists them.");
            }
            int iterations = Math.Max(1, request.Int(1, benchmark.Iterations));
            Action run = benchmark.Prepare();
            run();
            long memory = GC.GetTotalMemory(false);
            int start = Environment.TickCount;
            for (int i = 0; i < iterations; i++)
            {
                run();
            }
            int total = Environment.TickCount - start;
            return new JsonObject
            {
                { "name", name },
                { "iterations", iterations },
                { "totalMs", total },
                { "opMs", (double)total / iterations },
                { "managedBytesDelta", GC.GetTotalMemory(false) - memory },
                { "libraryContent", context.Library.AllContent.Count },
            };
        }

        // The first package's record, or a typical one when there's none.
        private byte[] SampleRecord()
        {
            IList<ContentItem> content = context.Library.AllContent;
            if (content.Count > 0)
            {
                return (byte[])content[0].Record.Clone();
            }
            byte[] record = new byte[ContentItem.RecordSize];
            BigEndian.SetU32(record, 0x140, 0x584111F7);
            Put(record, 0x08, "Some Arcade Game: Full Version");
            Put(record, 0x158, "Some Arcade Game");
            Encoding.UTF8.GetBytes("6F1132CE697FD7B5FB8052FEAA182741944E0EC458", 0, 42, record, 0x108);
            return record;
        }

        private static byte[] SampleMetadata()
        {
            byte[] metadata = new byte[ContentMetadata.Size];
            Put(metadata, 0x411 - 0x344, "Some Arcade Game");
            Put(metadata, 0xD11 - 0x344, "A description of the game that goes on for a while, as they do, in two sentences.");
            Put(metadata, 0x1611 - 0x344, "Some Publisher");
            Put(metadata, 0x1691 - 0x344, "Some Arcade Game");
            return metadata;
        }

        private static void Put(byte[] b, int offset, string text)
        {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text);
            Buffer.BlockCopy(bytes, 0, b, offset, bytes.Length);
        }

        private List<ExecutableItem> Executables()
        {
            return context.Library.Titles.SelectMany(t => t.Executables).ToList();
        }

        private List<string> Names()
        {
            return context.Library.Titles.Select(t => t.Name ?? "").ToList();
        }
    }
}
