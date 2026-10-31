using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// A command line sent to the debug server: the command's name, then
    /// arguments separated by spaces. An argument with spaces is put in
    /// double quotes.
    /// </summary>
    public sealed class DebugRequest
    {
        public DebugRequest(string line)
        {
            Line = line.Trim();
            int space = Line.IndexOf(' ');
            Command = (space < 0 ? Line : Line.Substring(0, space)).ToLowerInvariant();
            Text = space < 0 ? "" : Line.Substring(space + 1).Trim();
            Args = Split(Text);
        }

        public string Line { get; private set; }
        public string Command { get; private set; }

        /// <summary>Everything after the command, as it was sent.</summary>
        public string Text { get; private set; }

        public List<string> Args { get; private set; }

        public string Arg(int index, string fallback)
        {
            return index < Args.Count ? Args[index] : fallback;
        }

        /// <summary>A required argument.</summary>
        public string Arg(int index, string name, bool required)
        {
            if (index >= Args.Count)
            {
                throw new DebugException("Missing " + name + ".");
            }
            return Args[index];
        }

        public int Int(int index, int fallback)
        {
            if (index >= Args.Count)
            {
                return fallback;
            }
            int value;
            if (!int.TryParse(Args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new DebugException("Not a number: " + Args[index]);
            }
            return value;
        }

        /// <summary>A number in hex (0x optional), such as a title ID.</summary>
        public uint Hex(int index, string name)
        {
            string text = Arg(index, name, true);
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            uint value;
            if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                throw new DebugException("Not a hex number: " + text);
            }
            return value;
        }

        private static List<string> Split(string text)
        {
            List<string> args = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            bool any = false;
            foreach (char c in text)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                }
                else if (c == ' ' && !quoted)
                {
                    if (any)
                    {
                        args.Add(current.ToString());
                        current.Length = 0;
                        any = false;
                    }
                }
                else
                {
                    current.Append(c);
                    any = true;
                }
            }
            if (any)
            {
                args.Add(current.ToString());
            }
            return args;
        }
    }

    /// <summary>A command's failure, sent back as the reply's error.</summary>
    public class DebugException : Exception
    {
        public DebugException(string message) : base(message)
        {
        }
    }

    /// <summary>What a command returns becomes the reply's result.</summary>
    public delegate object DebugHandler(DebugRequest request);

    public sealed class DebugCommand
    {
        public string Name;
        public string Usage;
        public string Help;
        public DebugHandler Run;
    }

    /// <summary>A group of commands, registered with the server at its start.</summary>
    public interface IDebugCommands
    {
        void Register(DebugCommandSet commands);
    }

    /// <summary>The commands the server knows, by name.</summary>
    public sealed class DebugCommandSet
    {
        private readonly Dictionary<string, DebugCommand> commands = new Dictionary<string, DebugCommand>();
        private readonly List<DebugCommand> ordered = new List<DebugCommand>();

        /// <summary>Adds a command. usage is how its arguments are written, such as "&lt;titleId&gt; [count]".</summary>
        public void Add(string name, string usage, string help, DebugHandler run)
        {
            DebugCommand command = new DebugCommand { Name = name, Usage = usage, Help = help, Run = run };
            commands[name] = command;
            ordered.Add(command);
        }

        public DebugCommand Find(string name)
        {
            DebugCommand command;
            return commands.TryGetValue(name, out command) ? command : null;
        }

        public IList<DebugCommand> All { get { return ordered; } }
    }
}
