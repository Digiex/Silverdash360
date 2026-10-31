"""The debug protocol and the console's functions as the dashboard calls them."""

import re

from harness import test

HEX8 = re.compile(r'^[0-9A-F]{8}$')
HEX16 = re.compile(r'^[0-9A-F]{16}$')
PACKAGE_MAGICS = ('CON ', 'LIVE', 'PIRS')
SAVED_GAME = '00000001'


@test('api')
def protocol_hello(t):
    hello = t.client.hello['result']
    t.equal(hello['protocol'], 1, 'protocol version')
    t.check(hello['commands'] > 20, 'only %d commands' % hello['commands'])


@test('api')
def protocol_unknown_command(t):
    reply = t.raw('no.such.command')
    t.equal(reply['ok'], False, 'ok')
    t.check('Unknown command' in reply['error'], reply['error'])


@test('api')
def protocol_quoted_arguments(t):
    t.equal(t.call('echo', 'two words'), '"two words"', 'echo')
    reply = t.raw('library.search "no such title anywhere 1234"')
    t.equal(reply['result']['query'], 'no such title anywhere 1234', 'query without its quotes')


@test('api')
def protocol_bad_argument(t):
    reply = t.raw('log many')
    t.equal(reply['ok'], False, 'ok')


@test('api')
def help_lists_commands(t):
    names = {c['name'] for c in t.call('help')}
    for name in ('info', 'library', 'xam.content', 'fs.list', 'ui.state', 'bench'):
        t.check(name in names, 'help has no %s' % name)


@test('api')
def system_info(t):
    info = t.call('info')
    t.check(re.match(r'^\d+\.\d+\.\d+\.\d+$', info['systemVersion']), 'system version %r' % info['systemVersion'])
    t.check(HEX8.match(info['titleId']), 'title ID %r' % info['titleId'])
    t.check(info['debugClients'] >= 1, 'no clients counted')
    t.note('system %s, title %s, %s' % (info['systemVersion'], info['titleId'], info['language']))


@test('api')
def xam_system(t):
    raw = t.call('xam.system')
    info = t.call('info')
    t.check(raw['systemVersion'] != '00000000', 'system version 0')
    t.equal(raw['titleId'], info['titleId'], 'title ID')


@test('api')
def memory_statistics(t):
    memory = t.call('memory')
    t.check(memory['managedBytes'] > 0, 'no managed memory')
    t.equal(memory['kernelStatus'], 0, 'MmQueryStatistics status')
    total = memory['totalPhysicalBytes']
    t.check(256 << 20 <= total <= 1 << 30, 'total physical memory %d' % total)
    available = memory['title']['availableBytes']
    t.check(0 < available < total, 'title memory available %d of %d' % (available, total))
    t.note('%d MB managed, %d MB of %d MB available' % (memory['managedBytes'] >> 20, available >> 20, total >> 20))


@test('api')
def signin(t):
    signin = t.call('xam.signin')
    t.equal(len(signin['users']), 4, 'users')
    for gamer in signin['signedIn']:
        t.check(HEX16.match(gamer['xuid']), 'XUID %r' % gamer['xuid'])
        t.check(gamer['gamertag'], 'empty gamertag')
        t.check(signin['users'][gamer['index']]['state'] != 0, 'user %d signed in but state 0' % gamer['index'])


@test('api')
def content_enumeration(t):
    library = t.library()
    content = t.call('xam.content')
    t.note('%d packages of %d titles in %d ms' % (content['count'], content['titles'], content['enumerateMs']))
    if library['content'] == 0:
        t.skip('the library has no content')
    t.check(content['count'] > 0, 'XamContentAggregateCreateEnumerator found nothing')
    # The library adds the profile's own content and drops duplicates.
    t.check(content['titles'] <= library['titles'] + 1, '%d titles enumerated, %d in the library' % (
        content['titles'], library['titles']))


@test('api')
def metadata_matches_library(t):
    t.library()
    content = t.call('library.content', 0, 50)['content']
    with_metadata = [(i, c) for i, c in enumerate(content) if c['metadata']][:3]
    if not with_metadata:
        t.skip('no package with metadata')
    for index, item in with_metadata:
        metadata = t.call('xam.metadata', index)
        t.equal(metadata['titleName'], item['metadata']['titleName'], 'title name of package %d' % index)
        t.equal(metadata['contentType'], item['type'], 'content type of package %d' % index)
        t.equal(metadata['titleId'], item['titleId'], 'title ID of package %d' % index)
        if metadata['thumbnail']['bytes']:
            t.check(metadata['thumbnail']['png'], 'package %d thumbnail is not a PNG' % index)


@test('api')
def package_file_reads(t):
    t.library()
    content = t.call('library.content', 0, 20)['content']
    drives = {d['name']: d for d in t.call('fs.drives') if d['present']}
    if 'Hdd1' not in drives:
        t.skip('no hard drive')
    for item in content:
        # Saves can be open, the dashboard's own among them.
        if item['xuid'] != '0' * 16 or item['type'] == SAVED_GAME:
            continue
        path = '/Hdd1/Content/%s/%s/%s/%s' % (item['xuid'], item['titleId'], item['type'], item['fileName'])
        try:
            stat = t.call('fs.stat', path)
        except Exception:
            continue
        t.check(not stat['directory'], '%s is a folder' % path)
        read = t.call('fs.read', path, 4)
        t.equal(read['read'], 4, 'bytes read')
        t.check(read['ascii'] in PACKAGE_MAGICS, '%s starts with %r' % (path, read['ascii']))
        return
    t.skip('no package of the first 20 is on Hdd1')


@test('api')
def file_system_listing(t):
    drives = [d for d in t.call('fs.drives') if d['present']]
    t.check(drives, 'no drive present')
    root = t.call('fs.list', '/')
    t.equal(sorted(e['name'] for e in root['entries']), sorted(d['name'] for d in drives), 'root folders')
    listing = t.call('fs.list', '/' + drives[0]['name'])
    t.note('/%s has %d entries, listed in %d ms' % (drives[0]['name'], listing['count'], listing['listMs']))
    reply = t.raw('fs.stat /NoSuchDrive/file')
    t.equal(reply['ok'], False, 'stat of a missing file')


@test('api')
def played_titles_and_achievements(t):
    if not t.call('xam.signin')['signedIn']:
        t.skip('nobody is signed in')
    played = t.call('xam.played')
    t.note('%d played titles read in %d ms' % (played['count'], played['readMs']))
    with_achievements = [p for p in played['titles'] if p['achievementsTotal'] > 0]
    if not with_achievements:
        t.skip('no played title with achievements')
    title = with_achievements[0]
    achievements = t.call('xam.achievements', title['titleId'])
    t.check(achievements['count'] > 0, 'no achievements for %s' % title['titleId'])
    t.check(achievements['unlocked'] >= title['achievements'] or achievements['count'] >= title['achievementsTotal'],
            'achievement counts disagree: %r against %r' % (achievements['unlocked'], title))


@test('api')
def tiles(t):
    if not t.call('xam.signin')['signedIn']:
        t.skip('nobody is signed in')
    played = t.call('xam.played')['titles'][:5]
    if not played:
        t.skip('no played titles')
    read = [t.call('xam.tile', p['titleId']) for p in played]
    found = [r for r in read if r['bytes']]
    for r in found:
        t.check(r['png'], 'a tile that is not a PNG')
    t.note('%d of %d played titles have a tile' % (len(found), len(read)))


@test('api')
def network_address(t):
    address = t.call('net.address')
    if address['address'] is None:
        t.skip('no address (status %s)' % address['status'])
    t.check(re.match(r'^\d+\.\d+\.\d+\.\d+$', address['address']), 'address %r' % address['address'])
