"""Takes the screenshots the docs and the website show, into docs/images.

    python tools/docs_screenshots.py 127.0.0.1:4601
    python tools/docs_screenshots.py 192.168.1.20 --out shots --only home search

The dashboard (a build with the debug server) goes to each screen and draws
it (ui.screenshot); once they're all drawn, they're copied off with the
dashboard's FTP server, so its messages aren't in them, and saved as
960x540 PNGs. The shelf view and theme are put back as they were.

What's on screen is the console's: its titles, the profile signed in and
its theme. Take them in xenia with test content (tools/Run.ps1) for
pictures to publish, or pick a theme with --theme.
"""

import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import screenshot  # noqa: E402
from screenshot import DebugClient, parse_address  # noqa: E402

DOCS_IMAGES = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'docs', 'images')
WIDTH, HEIGHT = 960, 540


class Screens:
    def __init__(self, client, args):
        self.c = client
        self.args = args

    def settle(self, seconds=1.5):
        """Pages slide in, and covers and achievements load after the page
        is up: the time given, then until every image asked for is shown
        (twice in a row, as a page asks for more as it lays out)."""
        time.sleep(seconds)
        quiet = 0
        end = time.time() + 30
        while quiet < 2 and time.time() < end:
            quiet = quiet + 1 if self.c.call('ui.state')['imagesPending'] == 0 else 0
            time.sleep(0.3)
        if quiet < 2:
            print('  images still loading after 30 s', flush=True)
        # Every so often the hub's logo turns into Digiex's for a while;
        # the pictures show Silverdash360's.
        end = time.time() + 15
        while self.c.call('ui.state').get('logoShown', 1) < 1 and time.time() < end:
            time.sleep(0.3)

    def hub(self, header):
        if self.c.call('ui.state')['page'] != 'HubPage':
            self.c.call('ui.navigate', 'hub')
        state = self.c.call('ui.section', header)
        # A section's grid is built once the user stops on it.
        for _ in range(50):
            if state.get('grid') or header not in ('home', 'games', 'apps'):
                break
            time.sleep(0.2)
            state = self.c.call('ui.state')
        return state

    def shelf(self, header, list_view):
        """A shelf as covers or as a list, as Y switches it."""
        state = self.hub(header)
        if state.get('listView') is not None and state['listView'] != list_view:
            self.c.call('ui.press', 'y')
        return state

    def title(self):
        # The title played last: its art, achievements and content.
        if self.args.title:
            return self.args.title
        item = (self.hub('home').get('grid') or {}).get('selectedItem')
        if not isinstance(item, dict):
            sys.exit('Nothing has been played to show the details of; give --title.')
        return item['key']

    def query(self):
        # The start of the first game's name, or app's.
        if self.args.query:
            return self.args.query
        for which in ('games', 'apps'):
            titles = [t for t in self.c.call('library.titles', which, 0, 1)['titles'] if t['name']]
            if titles:
                return titles[0]['name'][:3].upper()
        return 'A'

    def page(self, *args):
        self.c.call('ui.navigate', *args)

    def title_tab(self, name):
        self.page('title', self.title())
        if name == 'content':
            # Achievements come first for a played title.
            self.settle()
            self.c.call('ui.tab', 'next')

    def theme_page(self):
        # From the top: editing and the built-in themes, not the one in use.
        self.page('theme')
        time.sleep(1)
        self.c.call('ui.press', 'up', '40')

    # Each screen, in the order the docs show them: (name, how to get there,
    # how long it takes to settle).
    def steps(self):
        return [
            ('home', lambda: self.hub('home'), 2.5),
            ('games-covers', lambda: self.shelf('games', False), 2.5),
            ('games-list', lambda: self.shelf('games', True), 1.5),
            ('apps-covers', lambda: self.shelf('apps', False), 2.5),
            ('apps-list', lambda: self.shelf('apps', True), 1.5),
            ('title-details', lambda: self.title_tab('achievements'), 3),
            ('title-content', lambda: self.title_tab('content'), 2),
            ('search', lambda: self.page('search', self.query()), 1.5),
            # Media servers answer within a few seconds.
            ('media', lambda: self.hub('media'), 8),
            ('profile', lambda: self.hub('me'), 2.5),
            ('settings', lambda: self.hub('settings'), 2.5),
            ('theme', self.theme_page, 2),
            ('theme-editor', lambda: self.page('theme-editor'), 2),
            ('storage', lambda: self.page('storage'), 2),
            ('welcome', lambda: self.page('welcome'), 1.5),
        ]


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('address', help='The console or xenia, host or host:port of the debug server.')
    parser.add_argument('--out', default=DOCS_IMAGES, help='Where the PNGs go (docs/images by default).')
    parser.add_argument('--only', nargs='+', metavar='NAME', help='Just these screenshots.')
    parser.add_argument('--title', help='The title whose details are shown (a key from library.titles); the one played last by default.')
    parser.add_argument('--query', help="The search typed; the start of the games shelf's first title by default.")
    parser.add_argument('--theme', help='A theme to take them with, as theme.set takes it (silverdash, builtin:1, scheme:carbon...).')
    parser.add_argument('--full-size', action='store_true', help='Keep them 1280x720.')
    args = parser.parse_args()

    host, port = parse_address(args.address)
    c = DebugClient(host, port, timeout=120)
    print('Waiting for the library', flush=True)
    c.call('library.wait', '300000')
    if c.call('ui.state')['page'] == 'WelcomePage':
        c.call('ui.back')
    screens = Screens(c, args)
    steps = screens.steps()
    names = [name for name, _, _ in steps]
    for name in args.only or []:
        if name not in names:
            parser.error('No screenshot %s; there are %s.' % (name, ', '.join(names)))
    steps = [s for s in steps if not args.only or s[0] in args.only]

    theme = c.call('theme.state')['setting'] if args.theme else None
    views = {h: screens.hub(h).get('listView') for h in ('games', 'apps')}
    drawn = []
    try:
        if args.theme:
            c.call('theme.set', args.theme, '30000')
        for name, go, wait in steps:
            go()
            screens.settle(wait)
            drawn.append((name, screenshot.take(c, name)))
            print('  drew %s' % name, flush=True)
            # Back to the hub; the theme editor puts the theme back as it goes.
            for _ in range(3):
                if c.call('ui.state')['page'] == 'HubPage':
                    break
                c.call('ui.back')
                time.sleep(0.5)
    finally:
        for header, list_view in views.items():
            if list_view is not None:
                screens.shelf(header, list_view)
        screens.hub('home')
        if theme:
            c.call('theme.set', theme, '30000')

    os.makedirs(args.out, exist_ok=True)
    ftp = screenshot.Ftp(c, host)
    try:
        for name, reply in drawn:
            shot = screenshot.fetch(ftp, reply)
            if not args.full_size:
                shot = shot.scaled(WIDTH, HEIGHT)
            path = os.path.join(args.out, name + '.png')
            with open(path, 'wb') as f:
                f.write(shot.png())
            print('%s: %dx%d' % (os.path.normpath(path), shot.width, shot.height), flush=True)
    finally:
        ftp.close()


if __name__ == '__main__':
    main()
