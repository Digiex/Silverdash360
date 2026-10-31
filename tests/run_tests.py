"""Runs the API, functional and performance tests against a running dashboard.

    python tests/run_tests.py 192.168.1.20
    python tests/run_tests.py 127.0.0.1 --suite api,functional --quick
    python tests/run_tests.py 192.168.1.20 --suite performance --save-baseline perf-console.json
    python tests/run_tests.py 192.168.1.20 --baseline perf-console.json --json results.json

The dashboard has to be built with the debug server (the default for now) and
running. Tests live in tests/suites/: each module's functions marked with
@test are run in the order they're written. See tests/README.md.
"""

import argparse
import importlib
import json
import os
import sys
import time
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from debugclient import DEFAULT_PORT, CommandError, DebugClient, parse_address  # noqa: E402
from harness import REGISTRY, Context, Skip, TestFailure  # noqa: E402

SUITES = ('api', 'functional', 'media', 'performance')


def load_suites(names):
    for name in names:
        importlib.import_module('suites.' + name)
    return [t for t in REGISTRY if t.suite in names]


def compare(metrics, baseline, tolerance):
    """Metrics that got worse than the baseline by more than the tolerance."""
    regressions = []
    for key, metric in metrics.items():
        base = baseline.get(key)
        if base is None or not metric.get('compare', True):
            continue
        old, new = base['value'], metric['value']
        if old is None or new is None:
            continue
        lower_better = metric.get('lower_is_better', True)
        # Small times are mostly the clock's resolution.
        floor = metric.get('noise', 0)
        if lower_better and new > old * (1 + tolerance) and new - old > floor:
            regressions.append((key, old, new))
        if not lower_better and new < old * (1 - tolerance) and old - new > floor:
            regressions.append((key, old, new))
    return regressions


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('address', help='The console or xenia: host or host:port (port %d by default).' % DEFAULT_PORT)
    parser.add_argument('--suite', default=','.join(SUITES), help='Suites to run, comma-separated: %s.' % ', '.join(SUITES))
    parser.add_argument('-k', '--filter', default='', help='Run only tests whose name contains this.')
    parser.add_argument('-x', '--exclude', default='', help='Leave out tests whose name contains this.')
    parser.add_argument('--quick', action='store_true', help='Fewer iterations, for xenia runs and smoke tests.')
    parser.add_argument('--json', help='Write the results and metrics to this file.')
    parser.add_argument('--baseline', help='Compare performance metrics with an earlier --json or --save-baseline file.')
    parser.add_argument('--tolerance', type=float, default=0.25, help='How much worse than the baseline fails (0.25 = 25%%).')
    parser.add_argument('--save-baseline', help='Write the performance metrics to this file as a baseline.')
    parser.add_argument('--timeout', type=float, default=180, help='Seconds to wait for a reply.')
    args = parser.parse_args()

    names = [s.strip() for s in args.suite.split(',') if s.strip()]
    unknown = [n for n in names if n not in SUITES]
    if unknown:
        parser.error('No suite %s.' % ', '.join(unknown))
    tests = [t for t in load_suites(names)
             if args.filter in t.name and not (args.exclude and args.exclude in t.name)]

    host, port = parse_address(args.address)
    client = DebugClient(host, port, timeout=args.timeout)
    info = client.call('info')
    print('Silverdash360 at %s:%d, system %s, running as %s, up %d s' % (
        host, port, info['systemVersion'], info['titleId'], info['uptimeMs'] // 1000))

    context = Context(client, host, port, quick=args.quick)
    results = []
    started = time.time()
    for t in tests:
        t0 = time.time()
        status, message = 'pass', ''
        try:
            t.run(context)
        except Skip as e:
            status, message = 'skip', str(e)
        except (TestFailure, AssertionError) as e:
            status, message = 'fail', str(e) or 'assertion failed'
        except CommandError as e:
            status, message = 'fail', 'command failed: %s' % e
        except (ConnectionError, OSError) as e:
            status, message = 'error', 'connection: %s' % e
        except Exception as e:  # A broken test shouldn't stop the others.
            status, message = 'error', '%s\n%s' % (e, traceback.format_exc())
        seconds = time.time() - t0
        results.append({'suite': t.suite, 'name': t.name, 'status': status, 'message': message,
                        'seconds': round(seconds, 2), 'notes': context.take_notes()})
        print('%-5s %-11s %-42s %6.1fs  %s' % (status.upper(), t.suite, t.name, seconds, message.splitlines()[0] if message else ''))
        if status == 'error' and message.startswith('connection'):
            # The dashboard went away; reconnecting tells whether it's still there.
            try:
                context.reconnect()
            except OSError:
                print('The dashboard is gone; stopping.')
                break

    metrics = context.metrics
    regressions = []
    if args.baseline:
        with open(args.baseline, encoding='utf-8') as f:
            baseline = json.load(f).get('metrics', {})
        regressions = compare(metrics, baseline, args.tolerance)
    if metrics:
        print('\nMetrics:')
        for key in sorted(metrics):
            m = metrics[key]
            line = '  %-48s %12s %s' % (key, format_value(m['value']), m.get('unit', ''))
            if args.baseline and key in baseline:
                line += '   (baseline %s)' % format_value(baseline[key]['value'])
            print(line)
    if regressions:
        print('\nSlower than the baseline by more than %d%%:' % (args.tolerance * 100))
        for key, old, new in regressions:
            print('  %s: %s -> %s' % (key, format_value(old), format_value(new)))

    counts = {s: sum(1 for r in results if r['status'] == s) for s in ('pass', 'fail', 'error', 'skip')}
    print('\n%(pass)d passed, %(fail)d failed, %(error)d errors, %(skip)d skipped' % counts +
          ' in %.0f s' % (time.time() - started) + (', %d regressions' % len(regressions) if args.baseline else ''))

    report = {'target': '%s:%d' % (host, port), 'info': info, 'quick': args.quick, 'results': results,
              'metrics': metrics, 'regressions': [{'metric': k, 'baseline': o, 'value': n} for k, o, n in regressions]}
    if args.json:
        with open(args.json, 'w', encoding='utf-8') as f:
            json.dump(report, f, indent=2)
    if args.save_baseline:
        with open(args.save_baseline, 'w', encoding='utf-8') as f:
            json.dump({'target': report['target'], 'info': info, 'quick': args.quick, 'metrics': metrics}, f, indent=2)
    try:
        context.client.close()
    except OSError:
        pass
    return 1 if counts['fail'] or counts['error'] or regressions else 0


def format_value(value):
    if isinstance(value, float):
        return '%.3f' % value if value < 100 else '%.0f' % value
    return str(value)


if __name__ == '__main__':
    sys.exit(main())
