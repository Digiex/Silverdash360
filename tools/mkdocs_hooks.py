"""MkDocs hook for the documentation site (mkdocs.yml).

The docs link to the tests, tools and other files outside docs/ with relative
paths, which work when reading them on GitHub. The site only contains docs/,
so this rewrites those links to the files on GitHub.
"""
import os
import posixpath
import re

REPO = "https://github.com/Digiex/Silverdash360"
BRANCH = "master"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# [text](target) and [text](target#anchor), but not images or absolute URLs.
LINK = re.compile(r"(?<!!)\[([^\]]*)\]\(([^)\s#]+)(#[^)\s]*)?\)")


def on_page_markdown(markdown, page, config, files):
    page_dir = posixpath.dirname("docs/" + page.file.src_uri)

    def rewrite(match):
        text, target, anchor = match.group(1), match.group(2), match.group(3) or ""
        if re.match(r"^[a-z]+:", target) or target.startswith("/"):
            return match.group(0)
        path = posixpath.normpath(posixpath.join(page_dir, target))
        if path.startswith("docs/") or path.startswith(".."):
            return match.group(0)
        kind = "tree" if os.path.isdir(os.path.join(ROOT, path)) else "blob"
        return "[%s](%s/%s/%s/%s%s)" % (text, REPO, kind, BRANCH, path, anchor)

    return LINK.sub(rewrite, markdown)
