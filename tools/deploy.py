"""Updates Silverdash360 on a console over FTP and starts the new build.

    python tools/deploy.py 192.168.1.20
    python tools/deploy.py 192.168.1.20 -- --suite functional --quick

With the dashboard running and its debug server on, it asks the
dashboard where it runs from, starts the dashboard's FTP server, uploads the
files that changed since the last deploy, and starts the new build; then it
waits for the new build's debug server and checks it's the one uploaded.
Anything after "--" goes to tests/run_tests.py, run once the new build is up.

A running dashboard's files aren't replaced: when the target is the folder it
runs from, the build goes to <target>/_update and starts from there, copies
the changed files into the target and starts that (see Debugging/SelfInstall.cs).
The first time, that folder gets the whole package; after that only changes.

Without the dashboard running, give another FTP server with --ftp (a homebrew
dashboard's, say): the files go straight into the target and you start it yourself.
"""

import argparse
import ftplib
import hashlib
import io
import json
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'tests'))

from debugclient import DEFAULT_PORT, CommandError, DebugClient, parse_address  # noqa: E402

# The Debug build, whose debug server (which deploying needs) starts on.
PACKAGE = os.path.join(ROOT, 'src', 'Silverdash360', 'bin', 'Debug', 'xbox')
FALLBACK_TARGET = '/Hdd1/Apps/Silverdash360'
STAGING = '_update'
MANIFEST = '.deploy-manifest.json'
DEPLOY_ID = '.deploy-id'
MARKER = 'deploy-install.txt'


def log(text):
    print(text, flush=True)


def local_files(package):
    """{relative path with slashes: sha1} of the package's files."""
    files = {}
    for folder, _, names in os.walk(package):
        for name in names:
            path = os.path.join(folder, name)
            relative = os.path.relpath(path, package).replace(os.sep, '/')
            with open(path, 'rb') as f:
                files[relative] = hashlib.sha1(f.read()).hexdigest()
    return files


class Ftp:
    def __init__(self, host, port, user, password):
        self.ftp = ftplib.FTP()
        self.ftp.connect(host, port, timeout=60)
        self.ftp.login(user, password)
        self.ftp.set_pasv(True)
        self.made = set()

    def read(self, path):
        data = io.BytesIO()
        try:
            self.ftp.retrbinary('RETR ' + path, data.write)
        except ftplib.error_perm:
            return None
        return data.getvalue()

    def make_dirs(self, folder):
        parts = folder.strip('/').split('/')
        for i in range(1, len(parts) + 1):
            path = '/' + '/'.join(parts[:i])
            if i == 1 or path in self.made:
                continue  # The drive is there.
            try:
                self.ftp.mkd(path)
            except ftplib.error_perm:
                pass  # There already.
            self.made.add(path)

    def write(self, path, data):
        self.make_dirs(path.rsplit('/', 1)[0])
        self.ftp.storbinary('STOR ' + path, io.BytesIO(data), blocksize=0x10000)

    def close(self):
        try:
            self.ftp.quit()
        except (OSError, ftplib.Error):
            self.ftp.close()


def remote_manifest(ftp, folder):
    data = ftp.read(folder + '/' + MANIFEST)
    if data is None:
        return {}
    try:
        return json.loads(data.decode('utf-8')).get('files', {})
    except ValueError:
        return {}


def changed(files, manifest):
    return sorted(name for name, sha in files.items() if manifest.get(name) != sha)


def upload(ftp, package, folder, names, files, deploy_id):
    """The files, then the manifest and the deploy ID: a manifest names only what's there."""
    total = sum(os.path.getsize(os.path.join(package, n)) for n in names)
    sent = 0
    started = time.time()
    for i, name in enumerate(names, 1):
        with open(os.path.join(package, name), 'rb') as f:
            data = f.read()
        ftp.write(folder + '/' + name, data)
        sent += len(data)
        log('  %3d/%d %-48s %8d KB' % (i, len(names), name, len(data) // 1024))
    ftp.write(folder + '/' + MANIFEST, json.dumps({'id': deploy_id, 'files': files}, indent=1).encode('utf-8'))
    ftp.write(folder + '/' + DEPLOY_ID, deploy_id.encode('utf-8'))
    seconds = time.time() - started
    if names:
        log('  %d KB in %.1f s (%.0f KB/s)' % (sent // 1024, seconds, sent / 1024 / max(seconds, 0.01)))


def wait_for_build(host, port, deploy_id, launched_at, timeout, target):
    """The new build's location once its debug server answers from target (or
    its install failed), or raises TimeoutError."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with DebugClient(host, port, timeout=10) as client:
                location = client.call('app.location')
                # The run that started the new build can still be answering.
                fresh = location['uptimeMs'] / 1000 < time.time() - launched_at + 2
                # A staged build answers from the staging folder until it
                # has installed itself and started the target.
                there = (location['path'] or '').lower() == target.lower()
                if fresh and location['deployId'] == deploy_id and (there or location['installError']):
                    return location, client.call('log', 30)
        except (OSError, ConnectionError, CommandError, ValueError):
            pass
        time.sleep(1)
    raise TimeoutError('the new build did not answer within %d s' % timeout)


def main():
    argv = sys.argv[1:]
    test_args = None
    if '--' in argv:
        test_args = argv[argv.index('--') + 1:]
        argv = argv[:argv.index('--')]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('address', help='The console: host, or host:port of the debug server (%d).' % DEFAULT_PORT)
    parser.add_argument('--package', default=PACKAGE, help='The built package (default: the Debug build, .\\tools\\Build.ps1 -Debug).')
    parser.add_argument('--target', help='Where the dashboard goes, such as /Hdd1/Apps/Silverdash360. By default '
                        'where it runs from, else ' + FALLBACK_TARGET + '.')
    parser.add_argument('--mode', choices=('auto', 'direct', 'staging'), default='auto',
                        help='direct uploads into the target, staging through <target>/_update; auto uses staging '
                        'when the dashboard runs from the target.')
    parser.add_argument('--full', action='store_true', help='Upload every file, not only the changed ones.')
    parser.add_argument('--ftp', help='Another FTP server, host:port, for when the dashboard isn\'t running.')
    parser.add_argument('--user', default='xbox', help='With --ftp: its user.')
    parser.add_argument('--password', default='xbox', help='With --ftp: its password.')
    parser.add_argument('--ftp-port', type=int, help='The port to start the dashboard\'s FTP server on (21; xenia '
                        'moves ports under 1024, so use 2121 there).')
    parser.add_argument('--no-launch', action='store_true', help='Upload only.')
    parser.add_argument('--timeout', type=int, default=300, help='Seconds to wait for the new build.')
    args = parser.parse_args(argv)

    package = os.path.abspath(args.package)
    if not os.path.exists(os.path.join(package, 'default.xex')):
        parser.error('No package at %s: build it first.' % package)
    host, port = parse_address(args.address)
    started = time.time()

    files = local_files(package)
    deploy_id = hashlib.sha1(json.dumps(files, sort_keys=True).encode('utf-8')).hexdigest()[:16]
    log('Package: %d files, build %s' % (len(files), deploy_id))

    client = None
    location = None
    try:
        client = DebugClient(host, port, timeout=30)
        location = client.call('app.location')
        log('Dashboard running from %s (%s)' % (location['path'] or 'an unknown folder', location['devicePath']))
    except (OSError, ConnectionError) as e:
        if not args.ftp:
            parser.error('The dashboard\'s debug server at %s:%d did not answer (%s). Start it, or give --ftp.' %
                         (host, port, e))
        log('The dashboard is not running; uploading over %s.' % args.ftp)

    target = args.target or (location['path'] if location and location['writable'] else FALLBACK_TARGET)
    target = '/' + target.strip('/')
    # A build left running from a staging folder belongs in the folder above.
    while target.endswith('/' + STAGING):
        target = target[:-len('/' + STAGING)] or FALLBACK_TARGET
    running_here = bool(location and location['path'] and location['path'].lower() == target.lower())
    mode = args.mode if args.mode != 'auto' else ('staging' if running_here else 'direct')
    if mode == 'staging' and not client:
        parser.error('Staging needs the dashboard running, to start the staged build.')
    log('Target %s, %s' % (target, 'through ' + target + '/' + STAGING if mode == 'staging' else 'directly'))

    if args.ftp:
        ftp_host, ftp_port = parse_address(args.ftp, 21)
        ftp = Ftp(ftp_host, ftp_port, args.user, args.password)
    else:
        server = client.call('ftp.start', *([args.ftp_port] if args.ftp_port else []))
        ftp = Ftp(host, args.ftp_port or server['port'], server['user'], server['password'])

    try:
        target_manifest = remote_manifest(ftp, target)
        if mode == 'direct':
            names = sorted(files) if args.full else changed(files, target_manifest)
            log('Uploading %d of %d files to %s' % (len(names), len(files), target))
            upload(ftp, package, target, names, files, deploy_id)
            launch = target
        else:
            staging = target + '/' + STAGING
            names = sorted(files) if args.full else changed(files, remote_manifest(ftp, staging))
            log('Uploading %d of %d files to %s' % (len(names), len(files), staging))
            upload(ftp, package, staging, names, files, deploy_id)
            install = sorted(files) if args.full else changed(files, target_manifest)
            marker = ['silverdash360-install 1', 'target ' + target]
            marker += ['file ' + name.replace('/', '\\') for name in install + [MANIFEST, DEPLOY_ID]]
            ftp.write(staging + '/' + MARKER, ('\n'.join(marker) + '\n').encode('utf-8'))
            log('The staged build will install %d files into %s' % (len(install), target))
            launch = staging
    finally:
        ftp.close()

    if args.no_launch or not client:
        log('Uploaded in %.0f s. Start %s/default.xex to run it.' % (time.time() - started, target))
        return 0

    client.call('app.launch', launch)
    client.sock.close()
    launched_at = time.time()
    log('Starting %s/default.xex' % launch)
    try:
        location, lines = wait_for_build(host, port, deploy_id, launched_at, args.timeout, target)
    except TimeoutError as e:
        log('Failed: %s.' % e)
        return 1
    if location['installError']:
        log('The install failed: %s' % location['installError'])
        for line in lines:
            log('  ' + line)
        return 1
    log('Build %s running from %s, %.0f s after starting it; %.0f s in all' % (
        deploy_id, location['path'] or location['devicePath'], time.time() - launched_at, time.time() - started))

    if test_args is not None:
        log('Running the tests')
        return subprocess.call([sys.executable, os.path.join(ROOT, 'tests', 'run_tests.py'),
                                '%s:%d' % (host, port)] + test_args)
    return 0


if __name__ == '__main__':
    sys.exit(main())
