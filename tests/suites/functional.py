"""The dashboard's features: the library, search, the cache, the hub and pages."""

import time
from urllib.parse import quote

from harness import test


def go_to_section(t, header):
    state = t.call('ui.state')
    if state['page'] != 'HubPage':
        t.call('ui.navigate', 'hub')
    return t.call('ui.section', header)


def first_title_section(t):
    """A section whose focused grid shows titles, or skips."""
    for header in ('games', 'apps', 'home'):
        state = go_to_section(t, header)
        grid = state.get('grid')
        if grid and grid['items'] > 1 and isinstance(grid['selectedItem'], dict):
            return state
    t.skip('no section with two or more titles')


@test('functional')
def library_loads(t):
    status = t.library()
    t.note('%d titles (%d games, %d apps), %d packages, signed in: %s' % (
        status['titles'], status['games'], status['apps'], status['content'], status['gamer']))


@test('functional')
def library_lists_agree(t):
    status = t.library()
    t.check(status['games'] + status['apps'] <= status['titles'], 'more games and apps than titles')
    t.check(status['recent'] <= status['titles'], 'more recent titles than titles')
    titles = t.call('library.titles', 'all', 0, 100000)
    t.equal(titles['total'], status['titles'], 'titles listed')
    keys = [x['key'] for x in titles['titles']]
    t.equal(len(set(keys)), len(keys), 'distinct keys')
    for which, kind in (('games', 'Game'), ('apps', 'App')):
        listed = t.call('library.titles', which, 0, 100000)
        t.equal(listed['total'], status[which], '%s listed' % which)
        t.check(all(x['kind'] == kind for x in listed['titles']), '%s holds another kind' % which)
        installed = [x['installed'] for x in listed['titles']]
        # Installed titles come first in a section.
        t.check(installed == sorted(installed, reverse=True), '%s: installed titles not first' % which)


@test('functional')
def title_lookup(t):
    t.library()
    titles = t.call('library.titles', 'all', 0, 10)['titles']
    if not titles:
        t.skip('no titles')
    for summary in titles:
        title = t.call('library.title', summary['key'])
        t.equal(title['name'], summary['name'], 'name of %s' % summary['key'])
        t.equal(len(title['content']), summary['content'], 'content of %s' % summary['key'])
    by_id = [x for x in titles if x['key'] == x['titleId']]
    if by_id:
        t.equal(t.call('library.title', '0x' + by_id[0]['titleId'])['key'], by_id[0]['key'], 'lookup by title ID')
    t.equal(t.raw('library.title NoSuchKey')['ok'], False, 'lookup of a missing title')


@test('functional')
def search_finds_titles(t):
    t.library()
    titles = [x for x in t.call('library.titles', 'all', 0, 200)['titles'] if x['kind'] != 'Other' and x['name']]
    if not titles:
        t.skip('no titles to search for')
    for title in titles[:5]:
        word = max(title['name'].split(), key=len)
        result = t.call('library.search', word)
        keys = [x['key'] for x in result['titles']]
        t.check(title['key'] in keys or result['total'] > len(keys),
                'searching %r did not find %s' % (word, title['name']))
        for found in result['titles']:
            text = ' '.join([found['name'] or '', found['titleId']]).lower()
            if word.lower() not in text:
                # The publisher matched; it isn't in the summary.
                detail = t.call('library.title', found['key'])
                t.check(word.lower() in (detail['publisher'] or '').lower(),
                        '%r found %s, which does not contain it' % (word, found['name']))
    t.equal(t.call('library.search', 'zzqqxxnotatitle')['total'], 0, 'results for nonsense')
    t.equal(t.call('library.search')['total'], 0, 'results for nothing')


def reference_search(titles, query):
    """What search should find: as Library.Search is documented, in Python."""
    words = query.lower().split()
    if not words:
        return []
    found = [x for x in titles if x['kind'] != 'Other']
    found.sort(key=lambda x: (x['name'] or '').upper())
    texts = {x['key']: ('%s %s %s' % (x['name'] or '', x['publisher'] or '', x['titleId'])).lower() for x in found}
    found = [x for x in found if all(w in texts[x['key']] for w in words)]
    return ([x for x in found if texts[x['key']].startswith(words[0])] +
            [x for x in found if not texts[x['key']].startswith(words[0])])


@test('functional')
def search_matches_reference(t):
    t.library()
    titles = t.call('library.titles', 'all', 0, 100000)['titles']
    named = [x for x in titles if x['kind'] != 'Other' and x['name']]
    if not named:
        t.skip('no titles to search for')
    queries = ['a', 'the', 'e s', 'zzqqxx', named[0]['name'][:3], named[-1]['name'].split()[0],
               named[len(named) // 2]['titleId'][:4], ' '.join(named[len(named) // 3]['name'].split()[:2])]
    if any(x['publisher'] for x in named):
        queries.append(next(x['publisher'] for x in named if x['publisher']).split()[0])
    for query in queries:
        expected = reference_search(titles, query)
        result = t.call('library.search', query)
        t.equal(result['total'], len(expected), 'results for %r' % query)
        t.equal([x['key'] for x in result['titles']], [x['key'] for x in expected[:50]], 'order for %r' % query)


@test('functional')
def refresh_keeps_the_library(t):
    before = t.library(settled=True)
    started = t.call('library.refresh')
    t.check(started['started'], 'a refresh was already running')
    after = t.library(settled=True)
    t.note('refresh took %d ms' % after['waitedMs'])
    for key in ('titles', 'games', 'apps', 'content'):
        t.equal(after[key], before[key], key + ' after a refresh')


@test('functional')
def cache_matches_library(t):
    status = t.library(settled=True)
    cache = t.call('cache.load')
    if cache['content'] == 0:
        t.skip('nothing cached yet')
    t.equal(cache['content'], status['content'], 'packages in the cache')
    t.equal(cache['withMetadata'], cache['content'] - len(
        [c for c in t.call('library.content', 0, 100000)['content'] if c['metadata'] is None]), 'packages with metadata')
    t.note('cache read in %d ms' % cache['loadMs'])


@test('functional')
def hub_sections(t):
    state = go_to_section(t, 0)
    sections = state['sections']
    t.check(len(sections) >= 4, 'sections: %r' % sections)
    for index, header in enumerate(sections):
        state = t.call('ui.section', index)
        t.equal(state['section'], index, 'section shown')
        t.equal(state['sectionHeader'], header, 'section header')
        t.check(state['focused'] is not None, 'nothing has focus in %s' % header)
    t.call('ui.section', 0)


@test('functional')
def bumpers_switch_sections(t):
    state = go_to_section(t, 0)
    count = len(state['sections'])
    t.equal(t.call('ui.press', 'rb')['section'], 1, 'section after RB')
    t.equal(t.call('ui.press', 'lb')['section'], 0, 'section after LB')
    t.equal(t.call('ui.press', 'lb')['section'], count - 1, 'LB from the first section wraps')
    t.equal(t.call('ui.press', 'rb')['section'], 0, 'RB from the last section wraps')


@test('functional')
def dpad_moves_selection(t):
    state = first_title_section(t)
    start = state['grid']['selected']
    moved = t.call('ui.press', 'right')
    t.check(moved['handled'] == 1, 'right was not handled')
    t.check(moved['grid']['selected'] != start, 'right did not move the selection')
    back = t.call('ui.press', 'left')
    t.equal(back['grid']['selected'], start, 'selection after right then left')


@test('functional')
def a_opens_title_and_b_returns(t):
    state = first_title_section(t)
    section = state['section']
    selected = state['grid']['selectedItem']
    opened = t.call('ui.press', 'a')
    time.sleep(0.5)
    opened = t.call('ui.state')
    t.equal(opened['page'], 'TitlePage', 'page after A')
    # The page's address carries the key escaped, as Uri.EscapeDataString does.
    t.check(quote(selected['key'], safe='') in (opened['uri'] or ''),
            'title page for %s, not %s' % (selected['key'], opened['uri']))
    t.check(opened['focused'] is not None, 'nothing has focus on the title page')
    t.call('ui.press', 'b')
    time.sleep(0.5)
    back = t.call('ui.state')
    t.equal(back['page'], 'HubPage', 'page after B')
    t.equal(back['section'], section, 'section after coming back')
    t.check(back['focused'] is not None, 'nothing has focus after coming back')


@test('functional')
def pages_open_and_close(t):
    titles = t.call('library.titles', 'all', 0, 1)['titles']
    pages = [('search', 'SearchPage'), ('storage', 'StoragePage')]
    for name, page in pages:
        state = t.call('ui.navigate', name)
        t.equal(state['page'], page, 'page for %s' % name)
        t.check(state['focused'] is not None, 'nothing has focus on %s' % page)
        t.call('ui.back')
        time.sleep(0.3)
        t.equal(t.call('ui.state')['page'], 'HubPage', 'page after leaving %s' % page)
    if titles:
        state = t.call('ui.navigate', 'title', titles[0]['key'])
        t.equal(state['page'], 'TitlePage', 'title page')
        t.call('ui.back')
        time.sleep(0.3)
    t.equal(t.raw('ui.navigate nowhere')['ok'], False, 'navigating nowhere')


@test('functional')
def several_clients(t):
    with t.connect() as other:
        t.equal(other.call('ping'), 'pong', 'second client')
        t.equal(t.call('ping'), 'pong', 'first client')
        t.check(other.call('info')['debugClients'] >= 2, 'clients not counted')


@test('functional')
def log_keeps_lines(t):
    t.call('status', 'Silverdash360 test run')
    lines = t.call('log', 200)
    t.check(lines, 'no log lines')
    t.check(any('Debug client connected' in line for line in lines), 'the log does not show this connection')
