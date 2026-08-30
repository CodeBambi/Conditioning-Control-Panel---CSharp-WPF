#!/usr/bin/env python3
"""
codex-extract.py - pull the website's field manual down to plain text, one
block per section, so the codex writers have a source to cut from.

THIS IS A WRITER'S TOOL, NOT A BUILD STEP. Nothing in the app runs it and no
release depends on it. The chapters that ship in Resources/web/codex/chapters/
are hand-cut JSON written against docs/emi-desk/WAVE2-CONTRACT.md; this only
produces the raw material somebody reads while cutting them.

WHY IT EXISTS. The site's manual is roughly 49,000 words across two dozen
guide pages. A codex chapter is 450 to 650 words and there are fourteen of
them. That is not an edit, it is a rewrite, and the useful first step is
seeing each section's prose stripped of its screenshots, navigation and
chrome, with a word count attached so the size of the cut is visible.

WHAT IT KEEPS. Headings, paragraphs, list items, info-box copy (labelled by
kind, because the danger boxes are usually where the honest limitation is
hiding) and settings-table rows, which are where the actual knob names live.

WHAT IT DROPS. Every screenshot and figure, the contents box, the site header
and footer, scripts and styles. None of it survives the trip into a book with
no image assets.

USAGE
    python codex-extract.py --site C:\\Projects\\cclabs-site
    python codex-extract.py --site ... --page flash-images --page sessions
    python codex-extract.py --site ... --out docs/codex/source
    python codex-extract.py --site ... --list

Standard library only, on purpose: a writer should be able to run it on a
fresh machine without installing anything.
"""

import argparse
import os
import re
import sys
from html.parser import HTMLParser

# The site wraps its manual body in <article class="doc wrap" id="doc"> and
# breaks it into <section id="..."><h2>. Those three are the whole contract
# this script has with the site; if the site restyles around them, nothing
# here needs to change.
ARTICLE_CLASS = "doc"

# Subtrees that never contain prose worth cutting from.
SKIP_TAGS = {"script", "style", "nav", "figure", "svg", "noscript"}
SKIP_CLASSES = {"toc", "toc-body", "crumbs", "guide-screenshot", "lights"}

# Blocks whose text is flushed as one line.
BLOCK_TAGS = {"p", "li", "h2", "h3", "h4", "h5", "h6", "td", "th", "dt", "dd"}

# HTML void elements. These NEVER produce an end tag, so counting them as a
# level of nesting makes the depth counter drift upward forever, and a skipped
# subtree containing one (every <figure> on the site holds an <img>) then never
# closes again - which silently swallowed whole pages.
VOID_TAGS = {"area", "base", "br", "col", "embed", "hr", "img", "input",
             "link", "meta", "param", "source", "track", "wbr"}

WS = re.compile(r"\s+")


class Section(object):
    def __init__(self, sid, heading):
        self.id = sid
        self.heading = heading
        self.lines = []          # (prefix, text)

    def words(self):
        return sum(len(t.split()) for _, t in self.lines)


class GuideParser(HTMLParser):
    """
    Walks one guide page and collects its sections.

    Deliberately forgiving. These pages are hand-maintained and a writer
    running this should never see a traceback because one <div> went unclosed
    on a page they were not even asking for.
    """

    def __init__(self):
        HTMLParser.__init__(self, convert_charrefs=True)
        self.sections = []
        self.title = ""
        self.lede = ""

        self._in_article = False
        self._article_depth = 0
        self._depth = 0

        self._skip_until = None      # depth at which the skipped subtree began
        self._buf = []
        self._block = None
        self._box = None             # info-box kind, when we are inside one
        self._box_depth = None
        self._pending = None         # 'title' or 'lede' while outside the article

    # -- helpers ----------------------------------------------------------
    @staticmethod
    def _cls(attrs):
        for k, v in attrs:
            if k == "class":
                return (v or "").split()
        return []

    @staticmethod
    def _attr(attrs, name):
        for k, v in attrs:
            if k == name:
                return v or ""
        return ""

    def _flush(self):
        if self._block is None:
            return
        text = WS.sub(" ", "".join(self._buf)).strip()
        self._buf = []
        block, self._block = self._block, None
        if not text:
            return

        if self._pending == "title":
            self.title = text
            self._pending = None
            return
        if self._pending == "lede":
            self.lede = text
            self._pending = None
            return

        if not self._in_article or not self.sections:
            return

        if block in ("h3", "h4", "h5", "h6"):
            prefix = "##"
        elif block == "li":
            prefix = "-"
        elif block in ("td", "th"):
            prefix = "|"
        elif self._box:
            prefix = "[%s]" % self._box
        else:
            prefix = ""
        self.sections[-1].lines.append((prefix, text))

    # -- parser callbacks -------------------------------------------------
    def handle_starttag(self, tag, attrs):
        if tag in VOID_TAGS:
            return                       # no end tag will ever arrive: no depth
        self._depth += 1
        classes = self._cls(attrs)

        if self._skip_until is not None:
            return

        if tag in SKIP_TAGS or (set(classes) & SKIP_CLASSES):
            self._skip_until = self._depth
            return

        if tag == "article" and ARTICLE_CLASS in classes:
            self._in_article = True
            self._article_depth = self._depth
            return

        # the page's own h1 and lede live above the article and are worth
        # carrying: they are the only place the page says what it is about
        if not self._in_article:
            if tag == "h1":
                self._flush()
                self._block, self._buf, self._pending = "h1", [], "title"
            elif tag == "p" and "lede" in classes:
                self._flush()
                self._block, self._buf, self._pending = "p", [], "lede"
            return

        if tag == "section":
            sid = self._attr(attrs, "id")
            if sid:
                self._flush()
                self.sections.append(Section(sid, ""))
            return

        if tag == "div" and "info-box" in classes:
            kind = "note"
            for c in classes:
                if c in ("warning", "danger", "tip", "success"):
                    kind = c
            self._box, self._box_depth = kind, self._depth
            return

        if tag in BLOCK_TAGS:
            self._flush()
            self._block, self._buf = tag, []

    def handle_endtag(self, tag):
        if tag in VOID_TAGS:
            return                       # matches the start tag we did not count
        if self._skip_until is not None:
            if self._depth <= self._skip_until:
                self._skip_until = None
            self._depth = max(0, self._depth - 1)
            return

        if self._block == tag or (self._block and tag in BLOCK_TAGS):
            # an h2 names the section it opened
            was = self._block
            text = WS.sub(" ", "".join(self._buf)).strip()
            if was == "h2" and self._in_article and self.sections and text:
                self.sections[-1].heading = text
                self._buf, self._block = [], None
            else:
                self._flush()

        if self._box is not None and self._box_depth is not None and self._depth <= self._box_depth:
            self._box, self._box_depth = None, None

        if self._in_article and self._depth <= self._article_depth and tag == "article":
            self._in_article = False

        self._depth = max(0, self._depth - 1)

    def handle_startendtag(self, tag, attrs):
        """<br/> and friends: never a level of nesting either."""
        if tag in VOID_TAGS:
            return
        self.handle_starttag(tag, attrs)
        self.handle_endtag(tag)

    def handle_data(self, data):
        if self._skip_until is None and self._block is not None:
            self._buf.append(data)


def render(name, parser):
    """One guide page as plain text."""
    out = []
    bar = "=" * 74
    out.append(bar)
    out.append(name)
    if parser.title:
        out.append("title: " + parser.title)
    if parser.lede:
        out.append("lede:  " + parser.lede)
    total = sum(s.words() for s in parser.sections)
    out.append("sections: %d    words: %d" % (len(parser.sections), total))
    out.append(bar)
    out.append("")

    for s in parser.sections:
        if not s.lines:
            continue
        head = s.heading or s.id
        out.append("--- %s  [#%s]  (%d words) ---" % (head, s.id, s.words()))
        out.append("")
        for prefix, text in s.lines:
            out.append((prefix + " " + text) if prefix else text)
            out.append("")
        out.append("")
    return "\n".join(out)


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Strip the site's guide pages to plain text, per section, "
                    "as source material for the codex chapters.")
    ap.add_argument("--site", default=r"C:\Projects\cclabs-site",
                    help="path to the cclabs-site checkout (default: %(default)s)")
    ap.add_argument("--page", action="append", default=[], metavar="SUBSTR",
                    help="only pages whose filename contains this; repeatable")
    ap.add_argument("--out", default=None, metavar="DIR",
                    help="write one .txt per page here instead of to stdout")
    ap.add_argument("--list", action="store_true", dest="do_list",
                    help="list the guide pages and their sections, and stop")
    args = ap.parse_args(argv)

    if not os.path.isdir(args.site):
        sys.stderr.write("no such site folder: %s\n" % args.site)
        return 2

    names = sorted(n for n in os.listdir(args.site)
                   if n.startswith("guide-") and n.endswith(".html"))
    if args.page:
        names = [n for n in names if any(p.lower() in n.lower() for p in args.page)]

    if not names:
        sys.stderr.write("no guide-*.html matched under %s\n" % args.site)
        return 1

    if args.out:
        try:
            os.makedirs(args.out, exist_ok=True)
        except OSError as exc:
            sys.stderr.write("cannot write to %s: %s\n" % (args.out, exc))
            return 2

    grand = 0
    for name in names:
        parser = GuideParser()
        try:
            parser.feed(read(os.path.join(args.site, name)))
            parser.close()
        except Exception as exc:                      # a broken page is not fatal
            sys.stderr.write("skipped %s: %s\n" % (name, exc))
            continue

        words = sum(s.words() for s in parser.sections)
        grand += words

        if args.do_list:
            print("%-34s %5d words  %d sections" % (name, words, len(parser.sections)))
            for s in parser.sections:
                if s.lines:
                    print("    #%-26s %5d  %s" % (s.id, s.words(), s.heading or ""))
            continue

        text = render(name, parser)
        if args.out:
            dest = os.path.join(args.out, name.replace(".html", ".txt"))
            with open(dest, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
            print("%-34s %5d words -> %s" % (name, words, dest))
        else:
            sys.stdout.write(text + "\n")

    if args.do_list or args.out:
        print("")
        print("%d pages, %d words. A codex chapter is 450 to 650." % (len(names), grand))
    return 0


if __name__ == "__main__":
    sys.exit(main())
