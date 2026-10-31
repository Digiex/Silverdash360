"""
How fast the dashboard is: its own work, timed on the console, and how
smoothly the UI draws. Every number is recorded as a metric, for comparing
runs with --baseline; the console's clock counts whole milliseconds.
"""

import time

from harness import test

# Benchmarks of interpreted code next to the framework's, and of the
# dashboard's own work on the library the console has.
BENCHMARKS = (
    'interp.loop', 'framework.indexof', 'utf16.decode', 'record.parse', 'record.write', 'record.read',
    'metadata.parse', 'cache.serialize', 'cache.deserialize', 'library.search', 'sort.orderby',
    'sort.comparison', 'json.write', 'alloc.small', 'gc.collect',
)


@test('performance')
def benchmarks(t):
    t.library()
    available = {b['name']: b for b in t.call('bench.list')}
    for name in BENCHMARKS:
        if name not in available:
            t.note('no benchmark %s on this build' % name)
            continue
        result = t.call('bench', name, t.iterations(available[name]['iterations']))
        t.metric('bench.%s.opMs' % name, result['opMs'], noise=2.0 / result['iterations'])
    ratio = t.metrics['bench.interp.loop.opMs']['value'] / 0.0625 / max(t.metrics['bench.framework.indexof.opMs']['value'], 0.001)
    t.note('interpreted code is about %.0f times slower than the framework per byte' % ratio)


@test('performance')
def library_operations(t):
    status = t.library()
    t.metric('library.titles', status['titles'], unit='titles', compare=False)
    t.metric('library.content', status['content'], unit='packages', compare=False)
    searches = [t.call('library.search', q)['searchMs'] for q in ('a', 'the', 'game', 'xyz')]
    t.metric('library.search.ms', sum(searches) / len(searches), noise=2)
    t.metric('cache.load.ms', t.call('cache.load')['loadMs'], noise=5)
    content = t.call('xam.content')
    t.metric('xam.content.enumerate.ms', content['enumerateMs'], noise=5)
    if status['content']:
        reads = [t.call('xam.metadata', i)['readMs'] for i in range(min(5, status['content']))]
        t.metric('xam.metadata.read.ms', sum(reads) / len(reads), noise=2)


@test('performance')
def library_refresh(t):
    if t.quick:
        t.skip('quick run')
    t.library(settled=True)
    t.call('library.refresh')
    t.metric('library.refresh.ms', t.library(settled=True)['waitedMs'], noise=50)


@test('performance')
def memory(t):
    memory = t.call('memory', 'collect')
    t.metric('memory.managed.bytes', memory['managedBytesAfterCollect'], unit='bytes', noise=256 << 10)
    if memory['kernelStatus'] == 0:
        t.metric('memory.title.available.bytes', memory['title']['availableBytes'], unit='bytes',
                 lower_is_better=False, noise=1 << 20)


@test('performance')
def idle_frame_rate(t):
    t.call('ui.navigate', 'hub')
    t.call('ui.section', 0)
    time.sleep(0.5)
    fps = t.call('ui.fps', 1000 if t.quick else 3000)
    t.metric('ui.idle.fps', fps['fps'], unit='fps', lower_is_better=False, noise=2)
    t.metric('ui.idle.frameMs95', fps['frameMs95'], noise=5)


@test('performance')
def scrolling(t):
    steps = 10 if t.quick else 40
    for header in ('games', 'apps'):
        state = t.call('ui.navigate', 'hub') if t.call('ui.state')['page'] != 'HubPage' else None
        state = t.call('ui.section', header)
        grid = state.get('grid')
        if not grid or grid['items'] < 4:
            t.note('%s: too few items to scroll' % header)
            continue
        out = t.call('ui.scroll', steps, 'right')
        back = t.call('ui.scroll', steps, 'left')
        t.metric('ui.scroll.%s.frameMsAverage' % header, (out['frameMsAverage'] + back['frameMsAverage']) / 2, noise=3)
        t.metric('ui.scroll.%s.frameMs95' % header, max(out['frameMs95'], back['frameMs95']), noise=5)
        t.metric('ui.scroll.%s.framesOver50ms' % header, out['framesOver50ms'] + back['framesOver50ms'],
                 unit='frames', noise=2)
        t.note('%s: %d steps each way, %.1f fps, worst frame %d ms' % (
            header, out['steps'], (out['fps'] + back['fps']) / 2, max(out['frameMsMax'], back['frameMsMax'])))


@test('performance')
def page_open(t):
    titles = t.call('library.titles', 'all', 0, 3)['titles']
    if not titles:
        t.skip('no titles')
    times = []
    for title in titles:
        start = time.time()
        t.call('ui.navigate', 'title', title['key'])
        times.append((time.time() - start) * 1000)
        t.call('ui.back')
        time.sleep(0.3)
    # Includes the network and the 400 ms the command waits for the page to settle.
    t.metric('ui.titlePage.open.ms', sum(times) / len(times), noise=50)


@test('performance')
def press_timing(t):
    """From a button press to the frames that follow: the press's own work, the first frame, a page's arrival."""
    t.library()
    t.call('ui.navigate', 'hub')
    state = t.call('ui.section', 'apps')
    if not state.get('grid') or state['grid']['items'] < 3:
        state = t.call('ui.section', 'games')
    if not state.get('grid') or state['grid']['items'] < 3:
        t.skip('no section with three or more titles')
    t.note('motion: %s' % t.call('ui.motion'))
    for name, button, window in (('move', 'right', 400), ('moveBack', 'left', 400), ('section', 'rb', 500),
                                 ('sectionBack', 'lb', 500), ('openTitle', 'a', 1000), ('back', 'b', 1000)):
        r = t.call('ui.timeline', button, window)
        t.check(r['handled'], '%s was not handled' % button)
        t.metric('press.%s.handlerMs' % name, r['handlerMs'], noise=5)
        t.metric('press.%s.firstFrameMs' % name, r['firstFrameMs'], noise=10)
        t.metric('press.%s.frameMsMax' % name, r['frameMsMax'], noise=10)
        if r['readyMs'] >= 0:
            t.metric('press.%s.readyMs' % name, r['readyMs'], noise=30)
        time.sleep(0.3)
