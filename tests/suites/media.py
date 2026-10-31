"""The media section: its sources, browsing drives and media servers, showing and playing.

A media server is looked for on the network; to test one that the search
doesn't reach (multicast blocked, or tests/dlna_server.py on a PC whose
firewall drops the answers), set SILVERDASH_DLNA to its description URL,
such as http://192.168.1.10:8200/description.xml.
"""

import os
import time

from harness import Skip, test

# Formats the console plays without the Optional Media Update first.
PREFERRED = ('.wmv', '.wma', '.mp3', '.asf', '.mp4', '.m4v', '.mov', '.m4a')


def find_media(t, kind, max_lists=40):
    """(source key, folder ID, index) of a media entry of a kind, looking through drives and servers."""
    t.library()
    sources = t.call('media.sources')
    keys = [d['key'] for d in sources['drives']] + [s['key'] for s in sources['servers']]
    found = []
    lists = 0
    for key in keys:
        queue = [None]
        while queue and lists < max_lists:
            folder = queue.pop(0)
            lists += 1
            try:
                entries = t.call('media.list', key, *([folder] if folder else []))['entries']
            except Exception:
                continue
            for index, entry in enumerate(entries):
                if entry['kind'] == kind:
                    found.append((key, folder or '-', index, entry))
                elif entry['kind'] == 'Folder' and len(queue) < 20:
                    queue.append(entry['id'])
            if found:
                break
        if found:
            break
    if not found:
        t.skip('no %s found on the drives or media servers' % kind.lower())

    def rank(item):
        name = item[3]['name'].lower()
        return next((i for i, ext in enumerate(PREFERRED) if name.endswith(ext)), len(PREFERRED))
    return sorted(found, key=rank)[0]


@test('media')
def sources_listed(t):
    sources = t.call('media.sources')
    t.check(sources['drives'], 'no drives with media listed')
    for drive in sources['drives']:
        t.check(drive['key'].startswith('drive:'), 'drive key %r' % drive['key'])
    t.note('drives: %s; disc: %s' % (', '.join(d['name'] for d in sources['drives']), sources['disc']))


@test('media')
def media_servers(t):
    found = t.call('media.search', 15000)
    url = os.environ.get('SILVERDASH_DLNA')
    if url:
        t.call('media.add', url)
        found = t.call('media.sources')
    if not found['servers']:
        t.skip('no media server found; set SILVERDASH_DLNA to add one')
    server = found['servers'][0]
    root = t.call('media.list', server['key'])
    t.check(root['count'] > 0, '%s lists nothing' % server['name'])
    for entry in root['entries']:
        if entry['kind'] != 'Folder':
            t.check(entry['url'], '%s has no URL' % entry['name'])
    t.note('%s: %d entries at the top, listed in %d ms' % (server['name'], root['count'], root['listMs']))


@test('media')
def picture_shown(t):
    key, folder, index, entry = find_media(t, 'Picture')
    t.call('ui.navigate', 'hub')
    t.call('media.play', key, folder, index)
    for _ in range(20):
        time.sleep(0.5)
        state = t.call('media.state')
        picture = state.get('picture') or {}
        if picture.get('width'):
            break
    t.equal(state['page'], 'PicturePage', 'page')
    t.check(picture.get('width', 0) > 0, '%s was not shown: %s' % (entry['name'], picture.get('status')))
    t.note('%s: %dx%d' % (entry['name'], picture['width'], picture['height']))
    t.call('ui.navigate', 'hub')


@test('media')
def music_or_video_plays(t):
    key, folder, index, entry = None, None, None, None
    for kind in ('Video', 'Music'):
        try:
            key, folder, index, entry = find_media(t, kind)
            break
        except Skip:
            pass
    if entry is None:
        t.skip('no music or video found')
    t.call('ui.navigate', 'hub')
    t.call('media.play', key, folder, index)
    state = None
    for _ in range(30):
        time.sleep(0.5)
        state = t.call('media.state')
        player = state.get('player') or {}
        if player.get('error') or player.get('state') in ('Playing', 'Paused') and player.get('positionMs', 0) > 0:
            break
    player = state.get('player') or {}
    t.check(not player.get('error'), '%s: %s' % (entry['name'], player.get('error')))
    t.check(player.get('state') in ('Playing', 'Paused', 'Buffering'), '%s is %s' % (entry['name'], player.get('state')))
    t.note('%s: %s at %d ms of %s' % (entry['name'], player['state'], player['positionMs'], player.get('durationMs')))
    t.call('ui.navigate', 'hub')
