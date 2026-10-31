"""A client for Silverdash360's debug server.

The dashboard listens on TCP port 4601 when it's built with the debug server
(the default for now). Each line sent is a command; each reply is one line of
JSON. Run this file for an interactive shell, or with a command to run one:

    python tests/debugclient.py 192.168.1.20
    python tests/debugclient.py 192.168.1.20 library
    python tests/debugclient.py 127.0.0.1:4601 "library.search halo"

"help" lists the commands the dashboard knows.
"""

import json
import socket
import sys

DEFAULT_PORT = 4601


class CommandError(Exception):
    """The dashboard ran the command and reported a failure."""

    def __init__(self, command, error, reply):
        super().__init__('%s: %s' % (command, error))
        self.command = command
        self.error = error
        self.reply = reply


def parse_address(text, port=DEFAULT_PORT):
    """host or host:port."""
    if ':' in text:
        host, port = text.rsplit(':', 1)
        return host, int(port)
    return text, port


def quote(argument):
    """An argument as the server reads it: in double quotes when it has spaces."""
    argument = str(argument)
    return '"%s"' % argument if ' ' in argument or argument == '' else argument


class DebugClient:
    def __init__(self, host, port=DEFAULT_PORT, timeout=120):
        self.host = host
        self.port = port
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.reader = self.sock.makefile('r', encoding='utf-8', newline='\n')
        self.hello = self._read()
        if not self.hello.get('ok'):
            raise ConnectionError('Unexpected greeting: %r' % self.hello)
        self.last_ms = None

    def _read(self):
        line = self.reader.readline()
        if not line:
            raise ConnectionError('The dashboard closed the connection.')
        return json.loads(line)

    def raw(self, line):
        """Sends a command line and returns the whole reply."""
        self.sock.sendall((line.strip() + '\n').encode('utf-8'))
        return self._read()

    def call(self, command, *args):
        """Runs a command and returns its result; raises CommandError on a failure."""
        line = ' '.join([command] + [quote(a) for a in args])
        reply = self.raw(line)
        self.last_ms = reply.get('ms')
        if not reply.get('ok'):
            raise CommandError(command, reply.get('error'), reply)
        return reply.get('result')

    def close(self):
        try:
            self.raw('quit')
        except (OSError, ConnectionError, ValueError):
            pass
        self.sock.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    host, port = parse_address(argv[1])
    with DebugClient(host, port) as client:
        if len(argv) > 2:
            print(json.dumps(client.raw(' '.join(argv[2:])), indent=2))
            return 0
        print('Connected to %s:%d: %s' % (host, port, client.hello['result']['server']))
        print('Type a command, "help" for the list, or "quit".')
        while True:
            try:
                line = input('> ').strip()
            except (EOFError, KeyboardInterrupt):
                print()
                return 0
            if not line:
                continue
            if line in ('quit', 'exit'):
                return 0
            print(json.dumps(client.raw(line), indent=2))


if __name__ == '__main__':
    sys.exit(main(sys.argv))
