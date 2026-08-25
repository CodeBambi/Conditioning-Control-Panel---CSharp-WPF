#!/usr/bin/env python3
# CCP greenfield verification harness — tier 2 Linux/X11 ELEMENT ROUTE (AT-SPI over D-Bus).
#
# WHY THIS EXISTS. capture.ps1 finds every surface it photographs through UIA: an AutomationId
# lookup, a screen BoundingRectangle, and a pattern read that confirms the state before a pixel is
# captured. Linux has no UIA, so until now the Linux leg could reach exactly three of the 45 named
# checks — the two rail-door borders and the whole-window dashboard, all three of which get their
# rectangle from the app's own layout probe rather than from any element route. The other 42 all
# begin "find the element", and there was nothing to find it with.
#
# THE ROUTE IS AT-SPI, AND IT NEEDED NO NEW PACKAGE — the same finding XTEST produced.
# Avalonia 12.1.1 ships a complete AT-SPI server (Avalonia.FreeDesktop.AtSpi.dll, a separate
# NuGet package, resolved into the app's own output directory), and Avalonia.X11 wires it up in
# X11AtSpiAccessibility. at-spi2-core 2.60.0 is already installed in this WSL image and
# org.a11y.Bus is D-Bus ACTIVATED, so the bus stands itself up on first contact. python3-dbus is
# already installed. Nothing was apt-installed to make this work.
#
# THERE IS NO OPT-IN, AND THAT WAS MEASURED RATHER THAN ASSUMED. The obvious expectation is that
# Avalonia waits for the desktop's own accessibility switch, because Avalonia.X11's
# X11AtSpiAccessibility subscribes to org.a11y.Status and has an OnAccessibilityEnabledChanged.
# Measured on this image 2026-08-25, both directions: with the desktop switch
# (org.gnome.desktop.interface toolkit-accessibility, which is what org.a11y.Status.IsEnabled
# writes) forced FALSE and the harness setting nothing, the tree is published and every query here
# answers; with no session bus at all, nothing is published. So the ONE precondition is a session
# bus on which org.a11y.Bus is reachable — and at-spi2-core registers that service as D-Bus
# ACTIVATED, so asking it for its address is enough to start it.
#
# WHAT AT-SPI DOES NOT CARRY, and this is the one place the Linux route is not a translation of
# the Windows one: THE AutomationId IS NOT ON THE WIRE. Measured on the running shell — every
# Avalonia element publishes attributes {toolkit: Avalonia, explicit-name: true} and nothing else,
# with no 'id' among them, while capture.ps1's whole element route is
# AutomationIdProperty lookup. What AT-SPI does carry is the accessible NAME, which Avalonia fills
# from AutomationProperties.Name and falls back to the control's text. So this tool matches on
# NAME, and a surface whose control carries an AutomationId but no Name is not addressable from
# here. That is a per-surface gate, not a general one, and it is stated per surface in
# capture-wslg.sh rather than hidden here.
#
# UNIQUENESS IS ENFORCED, exactly as Get-Element enforces it. A selector that matches two elements
# is refused rather than resolved to the first one: "the first match" is how a harness silently
# photographs the wrong row after a layout change.
#
# COORDINATES. Everything printed is WINDOW-RELATIVE device pixels (AT-SPI coordType 1), because
# both consumers want that space: xgetimage.py --crop takes it directly and xinput.py adds the X
# window's own root origin itself. Screen coordinates are ALSO printed (coordType 0) but only as
# diagnostics — on X11 the meaning of a screen coordinate moves while the window manager places
# the window, which is the same trap capture-wslg.sh records for the layout probe's `@ screen`.
#
# Usage:
#   atspi.py "<window title>" tree                     dump the accessible tree of that window
#   atspi.py "<window title>" window                   the window's own rect and states
#   atspi.py "<window title>" rect   "<name>"          the uniquely-named element's rect + states
#   atspi.py "<window title>" rect   "^<prefix>"       ... by name prefix; "~<part>" by substring
#   atspi.py "<window title>" rect   "<name>" @slider  ... narrowed to one role when a name collides
#   atspi.py "<window title>" in     "<name>" "<sel>"  the unique DESCENDANT matching (@role or name)
#   atspi.py "<window title>" scroll "<name>"          that element's nearest scroll-pane ancestor
#   atspi.py "<window title>" text   "<name>"          print that element's accessible name only
#
# `rect`, `in`, `scroll` and `window` print shell-assignable KEY=value lines: X, Y, W, H are
# window-relative device pixels, SX/SY are screen, ROLE and NAME are shell-quoted, and
# SELECTED / CHECKED / SHOWING / VISIBLE / SENSITIVE / ENABLED are 0|1. SENSITIVE and ENABLED are
# what UIA's IsEnabled becomes here — AT-SPI splits the one Windows property in two, and the
# session feature lock is read off them.
# Exit 0 = found and unique. Exit 2 = not found, not unique, or the a11y route is not up.
import shlex
import sys

try:
    import dbus
except ImportError:  # pragma: no cover - the package is present on this image
    sys.exit("FAIL: python3-dbus is not installed; the AT-SPI element route needs it")

ACCESSIBLE = "org.a11y.atspi.Accessible"
COMPONENT = "org.a11y.atspi.Component"
COORD_SCREEN = 0
COORD_WINDOW = 1

# AtspiStateType ordinals used here (at-spi2-core atspi-constants.h). The state set arrives as
# two uint32 words, low word first, so state n is bit (n % 32) of word (n // 32).
STATE_CHECKED = 4
STATE_ENABLED = 8
STATE_SELECTED = 23
STATE_SENSITIVE = 24
STATE_SHOWING = 25
STATE_VISIBLE = 30


def a11y_bus():
    """The accessibility bus, whose address the session bus publishes at org.a11y.Bus."""
    try:
        session = dbus.SessionBus()
        obj = session.get_object("org.a11y.Bus", "/org/a11y/bus")
        address = str(obj.GetAddress(dbus_interface="org.a11y.Bus"))
    except dbus.DBusException as ex:
        sys.exit("FAIL: org.a11y.Bus is not reachable on this session bus (%s), so there is no "
                 "accessibility bus to read. The harness must run under a session bus that has "
                 "at-spi2-core's org.a11y.Bus on it." % ex)
    return dbus.bus.BusConnection(address)


class Tree:
    def __init__(self, bus):
        self.bus = bus

    def iface(self, ref, name):
        return dbus.Interface(self.bus.get_object(ref[0], ref[1]), name)

    def name_of(self, ref):
        try:
            props = self.iface(ref, "org.freedesktop.DBus.Properties")
            return str(props.Get(ACCESSIBLE, "Name"))
        except dbus.DBusException:
            return ""

    def role_of(self, ref):
        try:
            return str(self.iface(ref, ACCESSIBLE).GetRoleName())
        except dbus.DBusException:
            return "?"

    @staticmethod
    def same_role(actual, wanted):
        """AT-SPI role names carry spaces ('push button', 'radio button', 'check box'), and a role
        with a space inside a shell argument is a quoting trap that costs an hour the first time it
        silently splits into two arguments. Hyphens are accepted for the same role."""
        return actual.replace(" ", "-") == wanted.replace(" ", "-")

    def children(self, ref):
        try:
            return [(str(k[0]), str(k[1])) for k in self.iface(ref, ACCESSIBLE).GetChildren()]
        except dbus.DBusException:
            return []

    def states(self, ref):
        try:
            words = [int(w) for w in self.iface(ref, ACCESSIBLE).GetState()]
        except dbus.DBusException:
            return set()
        out = set()
        for index, word in enumerate(words):
            for bit in range(32):
                if word & (1 << bit):
                    out.add(index * 32 + bit)
        return out

    def extents(self, ref, coord):
        try:
            e = self.iface(ref, COMPONENT).GetExtents(dbus.UInt32(coord))
            return tuple(int(v) for v in e)
        except dbus.DBusException:
            return None

    def frames(self):
        root = ("org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root")
        found = []
        for app in self.children(root):
            for frame in self.children(app):
                found.append(frame)
        return found

    def walk(self, ref, depth=0, parent=None):
        yield ref, depth, parent
        for child in self.children(ref):
            for item in self.walk(child, depth + 1, ref):
                yield item


def frame_named(tree, needle):
    frames = tree.frames()
    if not frames:
        sys.exit("FAIL: the accessibility registry lists no application windows at all, so this "
                 "route can measure nothing. Either the app is not running, or it started before "
                 "org.a11y.Bus was reachable on its session bus and so never registered.")
    matches = [f for f in frames if needle in tree.name_of(f)]
    if not matches:
        names = ", ".join(repr(tree.name_of(f)) for f in frames)
        sys.exit("FAIL: no accessible window whose title contains %r; the registry offers %s"
                 % (needle, names))
    if len(matches) > 1:
        sys.exit("FAIL: %d accessible windows contain %r; a capture must name exactly one"
                 % (len(matches), needle))
    return matches[0]


def matcher(selector):
    """'^p' prefix, '~p' substring, anything else exact. Substring exists for ONE shape: a control
    whose text names a runtime value ('Morning Drift is running this. ...'), where the invariant
    half of the sentence is the part a harness can assert."""
    if selector.startswith("^"):
        return lambda name: name.startswith(selector[1:])
    if selector.startswith("~"):
        return lambda name: selector[1:] in name
    return lambda name: name == selector


def select(tree, frame, selector):
    """Every element under `frame` whose name matches.

    Returns (ref, name, parents) where parents is the chain from the element up to the frame,
    nearest first — the ancestor walk `scroll` needs and nothing else here uses.
    """
    matches = matcher(selector)
    parent_of = {}
    hits = []
    for ref, _, parent in tree.walk(frame):
        parent_of[ref] = parent
        name = tree.name_of(ref)
        if matches(name):
            hits.append((ref, name))
    out = []
    for ref, name in hits:
        chain, cursor = [], parent_of.get(ref)
        while cursor is not None:
            chain.append(cursor)
            cursor = parent_of.get(cursor)
        out.append((ref, name, chain))
    return out


def one(tree, frame, selector, role=None, what="element"):
    hits = select(tree, frame, selector)
    if role is not None:
        # A ROLE IS A DISAMBIGUATOR, NEVER A TIE-BREAK. It narrows by a property the control really
        # publishes, which is a different thing from taking the first of several matches. The
        # collision this exists for is real and is a shape rather than an accident: a dial's
        # caption TextBlock carries the same words as the dial's own AutomationProperties.Name, so
        # 'Master volume' names both a label and a slider (StudioPage.axaml:1849-1855).
        hits = [h for h in hits if Tree.same_role(tree.role_of(h[0]), role)]
    if not hits:
        sys.exit("FAIL: no %s named %r in this window. AT-SPI carries the accessible NAME, never "
                 "the AutomationId, so a control with only an AutomationId is not addressable "
                 "here." % (what, selector))
    if len(hits) > 1:
        sys.exit("FAIL: %d elements match %r (%s). A capture must name exactly one, or it "
                 "photographs whichever the tree walk reached first."
                 % (len(hits), selector, ", ".join(repr(n) for _, n, _ in hits)))
    return hits[0]


def emit(tree, ref, name):
    win = tree.extents(ref, COORD_WINDOW)
    scr = tree.extents(ref, COORD_SCREEN)
    if win is None:
        sys.exit("FAIL: %r implements no Component interface, so it has no rectangle. Avalonia "
                 "gives layout-only elements no bounds." % name)
    states = tree.states(ref)
    # Shell-assignable, and QUOTED: every name here is prose with spaces in it ('Flash Images rack
    # row'), and an unquoted assignment would eval as a command.
    print("X=%d" % win[0])
    print("Y=%d" % win[1])
    print("W=%d" % win[2])
    print("H=%d" % win[3])
    print("SX=%d" % (scr[0] if scr else -1))
    print("SY=%d" % (scr[1] if scr else -1))
    print("ROLE=%s" % shlex.quote(tree.role_of(ref)))
    print("SELECTED=%d" % (1 if STATE_SELECTED in states else 0))
    print("CHECKED=%d" % (1 if STATE_CHECKED in states else 0))
    print("SHOWING=%d" % (1 if STATE_SHOWING in states else 0))
    print("VISIBLE=%d" % (1 if STATE_VISIBLE in states else 0))
    print("SENSITIVE=%d" % (1 if STATE_SENSITIVE in states else 0))
    print("ENABLED=%d" % (1 if STATE_ENABLED in states else 0))
    print("NAME=%s" % shlex.quote(name.replace("\n", "\\n")))


def main():
    if len(sys.argv) < 3:
        sys.exit("usage: atspi.py \"<window title>\" tree|rect|label|text [selector]")
    title, verb = sys.argv[1], sys.argv[2]
    tree = Tree(a11y_bus())
    frame = frame_named(tree, title)

    if verb == "window":
        emit(tree, frame, tree.name_of(frame))
        return 0

    if verb == "tree":
        for ref, depth, _ in tree.walk(frame):
            win = tree.extents(ref, COORD_WINDOW)
            print("%s%s %r %s" % ("  " * depth, tree.role_of(ref), tree.name_of(ref),
                                  "" if win is None else "@ %d,%d %dx%d" % win))
        return 0

    if len(sys.argv) < 4:
        sys.exit("FAIL: %s needs a selector" % verb)
    selector = sys.argv[3]
    # An optional trailing '@role' narrows rect/text; see one()'s note on why that is not a
    # tie-break.
    role = sys.argv[4][1:] if len(sys.argv) > 4 and sys.argv[4].startswith("@") else None

    if verb == "rect":
        ref, name, _ = one(tree, frame, selector, role)
        emit(tree, ref, name)
        return 0

    if verb == "text":
        _, name, _ = one(tree, frame, selector, role)
        print(name.replace("\n", "\\n"))
        return 0

    if verb == "in":
        # capture.ps1's Get-RowLabelRect generalised: the ONE descendant of a named element that
        # matches. '@role' matches by role, anything else by name (with the same '^' prefix rule).
        # Exactly one must match, for Get-RowLabelRect's reason: a row that grew a second caption
        # has changed shape, and the dot-cell arithmetic derived from it no longer names the dot.
        if len(sys.argv) < 5:
            sys.exit("FAIL: `in` needs a parent selector and a child selector")
        child = sys.argv[4]
        ref, name, _ = one(tree, frame, selector)
        # `in` spends argv[4] on the CHILD selector, so a parent whose own name collides is named
        # with a role-qualified `rect` first and reached through its unique child from there.
        if child.startswith("@"):
            role = child[1:]
            hits = [(r, tree.name_of(r)) for r, _, _ in tree.walk(ref)
                    if Tree.same_role(tree.role_of(r), role)]
        else:
            matches = matcher(child)
            hits = [(r, tree.name_of(r)) for r, _, _ in tree.walk(ref)
                    if matches(tree.name_of(r))]
        if len(hits) != 1:
            sys.exit("FAIL: %r has %d descendants matching %r (%s); expected exactly 1"
                     % (name, len(hits), child, ", ".join(repr(n) for _, n in hits)))
        emit(tree, hits[0][0], hits[0][1])
        return 0

    if verb == "scroll":
        # capture.ps1's Assert-Inside needs the VIEWPORT, which it names by AutomationId
        # ('RackScroll', 'IntakeScroll'). Those ids are not on the AT-SPI wire, so the viewport is
        # found structurally instead: the nearest scroll-pane ANCESTOR of the element. That is a
        # stronger statement than naming it, not a weaker one — it cannot name the wrong
        # ScrollViewer, because it is the one this element actually scrolls inside.
        _, name, parents = one(tree, frame, selector, role)
        for ancestor in parents:
            if Tree.same_role(tree.role_of(ancestor), "scroll pane"):
                emit(tree, ancestor, tree.name_of(ancestor))
                return 0
        sys.exit("FAIL: %r has no scroll-pane ancestor, so it is not inside a viewport at all"
                 % name)

    sys.exit("FAIL: unknown verb %r" % verb)


if __name__ == "__main__":
    sys.exit(main())
