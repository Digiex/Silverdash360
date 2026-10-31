"""What the test suites are written with.

A test is a function taking a Context, marked with @test('suite'):

    @test('functional')
    def search_finds_a_title(t):
        result = t.call('library.search', 'halo')
        t.check(result['total'] > 0, 'nothing found')

t.call runs a dashboard command, t.check fails the test, t.skip skips it,
t.metric records a performance number and t.note adds a line to the report.
"""

from debugclient import DebugClient

REGISTRY = []


class TestFailure(Exception):
    pass


class Skip(Exception):
    pass


class Test:
    def __init__(self, suite, name, function):
        self.suite = suite
        self.name = name
        self.function = function

    def run(self, context):
        self.function(context)


def test(suite, name=None):
    """Marks a function as a test of a suite; its name is the function's unless given."""
    def mark(function):
        REGISTRY.append(Test(suite, name or function.__name__, function))
        return function
    return mark


class Context:
    def __init__(self, client, host, port, quick=False):
        self.client = client
        self.host = host
        self.port = port
        self.quick = quick
        self.metrics = {}
        self.notes = []

    def call(self, command, *args):
        return self.client.call(command, *args)

    def raw(self, line):
        return self.client.raw(line)

    def connect(self):
        """Another connection to the dashboard, for tests of several clients."""
        return DebugClient(self.host, self.port, timeout=self.client.sock.gettimeout())

    def reconnect(self):
        try:
            self.client.sock.close()
        except OSError:
            pass
        self.client = self.connect()

    def check(self, condition, message):
        if not condition:
            raise TestFailure(message)

    def equal(self, actual, expected, what):
        if actual != expected:
            raise TestFailure('%s: expected %r, got %r' % (what, expected, actual))

    def skip(self, reason):
        raise Skip(reason)

    def note(self, text):
        self.notes.append(text)

    def take_notes(self):
        notes, self.notes = self.notes, []
        return notes

    def metric(self, name, value, unit='ms', lower_is_better=True, noise=0, compare=True):
        """
        A performance number. noise is a difference too small to call a
        regression, such as the console clock's millisecond; compare=False
        records it without comparing it with a baseline.
        """
        self.metrics[name] = {'value': value, 'unit': unit, 'lower_is_better': lower_is_better,
                              'noise': noise, 'compare': compare}

    def library(self, timeout=300000):
        """The library's status once it has loaded, waiting for a refresh to end."""
        status = self.call('library.wait', timeout)
        self.check(status['loaded'], 'the library never loaded')
        return status

    def iterations(self, default):
        """Fewer iterations in quick runs."""
        return max(1, default // 5) if self.quick else default
