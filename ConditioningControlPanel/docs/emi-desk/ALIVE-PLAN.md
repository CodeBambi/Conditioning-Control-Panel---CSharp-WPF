# EMI Desk, second pass: making her alive (2026-08-29)

The play-test verdict was that desktop EMI feels dead next to campus EMI, and the verdict is right.
The campus widget spends most of its code on things that are not features: it blinks on a clock, it
leans a few pixels toward your cursor, it perks up when you rush at it and looks away when you stare
too long. None of that ever shows up in a feature list, but it is the whole difference between a
sticker and a pet. This plan closes that gap first, then goes past it, because the desktop version
has something the campus one never had: she is a physical object you can grab, drop, squash and
carry around a real screen, and almost none of that is answered yet.

Wave 2 is already building the click squash, the head-click pet, the drag wobble, the ring open
animation, the card borders and the arcademy farewell, so none of those are re-planned here. Where
an item below touches the same code (the drag path, ClampIntoWorkArea), it is marked to build after
wave 2 lands rather than beside it. Two laws carry over from campus unchanged: a line is never cut
mid-sentence no matter what the body is doing, and everything here is transforms and timers, never
bitmap repaints, with every timer stopped while she is hidden (the StopIdleBeats seam already does
this for the beats we have).

## 1. One body, one spring

Right now each reaction animates its own transform from scratch, so a click, a drag and a drop all
feel like different objects. The fix is a single tiny physics helper in EmiDeskWindow.xaml.cs: three
damped springs (scale, rotation, vertical offset) ticked by one CompositionTarget.Rendering hook
that attaches only while any spring is live and detaches the moment they settle. Every touch becomes
an impulse into that one model instead of its own storyboard: a click compresses scale, drag
velocity leans rotation, the leftover velocity at drop becomes a vertical bounce, and a landing
above a speed threshold asks EmiDeskWindow.Fx.cs for a small dust puff from the existing Burst
particles. Suggested constants to start from the campus feel: spring k 4, damping 0.75, rotation
capped at 6 degrees, scale impulses under 10 percent. Once this exists, everything in section 3
is a few lines each, and she reads as one object with real mass. Effort M, EmiDeskWindow.xaml.cs
plus one puff call in EmiDeskWindow.Fx.cs.

## 2. Idle life

**Blink.** Campus blinks every 5200ms with a 110ms lid hold, deterministically, so she always reads
as breathing. Desktop rolls a 50 percent chance on a 4200ms tick, which means she regularly goes
twelve seconds stone still and then blinks twice in a row; that irregularity is a big part of the
dead feeling. Fix: blink on a 5200ms clock with 600ms of jitter, hold the lid 110ms, and once in
about seven blinks do a quick double. Effort S, the idle beats in EmiDeskWindow.xaml.cs.

**Sway** is already ported (same 200ms step cycle) and is fine; just make sure the centre pause
varies 600 to 900ms like the campus DIALS instead of being fixed, so the loop never becomes visible.

**Micro-fidgets.** New; campus gets away without them because the room around her moves, the desktop
does not. Every 25 to 50 seconds, while no bubble, chain or drag is live, play one of three
half-second wordless beats: an antenna twitch (2px translate on the top of the sprite), a weight
shift (1 degree rotation held two seconds and released through the spring), or a glance chain to a
random side. Never two of the same kind in a row. Effort S, driven from the same idle beat timer,
fx through OnChainFxCore where a chain is involved.

**Stretch.** Rare and precious: once per 20 to 40 minutes of being out, scale up 4 percent over
400ms with >_< and settle back to ^_^. No new art, transforms only. Effort S.

**Sleep ramp.** Campus has no sleep at all, so this is the first place desktop goes past parity, and
it is the single biggest alive win for someone who leaves her out while working. After 6 minutes
with no cursor inside 300 DIPs of her and no host moment fired, she dozes: face =_=, sway stops,
blink slows to every 8 seconds. After 12 minutes she sleeps: face ZzZ, the glass dims about 10
percent, and the only motion is a breathing scale of 1.5 percent on a 3 second cycle through the
spring. She wakes when the cursor comes within 200 DIPs or any priority 2 or 3 moment fires: wake
chain, a quick stretch, then normal idle, and she will not re-doze for 3 minutes. The wake is
also the natural place the existing backSoon and morningFirst moments finally land, since the
service already knows how long the user was away. Effort M, EmiDeskWindow.xaml.cs for the ramp,
one Fire from EmiDeskService when a wake follows a long gap.

## 3. Reactivity

**Cursor gaze.** The campus face leans up to 3px toward your cursor (distance divided by 60, eased
at 0.15 per frame, as a CSS transform on the face canvas). Desktop has nothing; she stares through
you, and this is the second biggest dead tell after the blink. Fix: while she is visible, poll
GetCursorPos at 10Hz (there is no document mousemove to borrow on the desktop) and ease a
TranslateTransform on the face element toward the cursor, capped at 3 DIPs. Ten position reads a
second is free. Effort S, EmiDeskWindow.xaml.cs.

**Approach perk.** Campus: when the cursor is closing on her faster than 1.2 px/ms and gets within
120px of her edge, she perks o_o; slower arrivals get a glance chain; 30 second cooldown. The same
numbers work on the desktop straight off the gaze poll. Effort S.

**Hover linger.** Cursor resting on her for 2 seconds without a click earns an expectant ^_^, and if
it is still there at 4 seconds she looks away ¬_¬, pretending she was not waiting. This sits
naturally in front of the head-hover pet gesture wave 2 keeps, and makes the pet feel invited.
Effort S.

**Repeated pokes.** Three body clicks inside 4 seconds that are not the head and not the ring: first
gets the squash only, second gets squash plus -_-, third gets the rage chain and >:( held 1.5
seconds, wordless, then a 60 second truce. No new pool needed; annoyance is funnier silent.
Effort S, OnBodyClickedCore.

**Being squished.** While the resize grip has her under 170 DIPs she wears >_<, and on release she
pops back through the spring with a small puff. The resized moment and its lines already exist and
keep firing exactly as now. Effort S.

**Fast shakes.** During a drag, three direction reversals above 1.5 px/ms inside 700ms flags dizzy:
on drop she plays the dizzy chain, @_@, and the settle bounce runs at double amplitude. 60 second
cooldown so deliberate rattling stays funny instead of constant. Effort S once the spring exists.

**Screen edges.** She should treat the monitor like furniture. Dragged hard against a side edge she
leans away from it with >.< while pressed. Dropped so her lower third would hang past the work area
bottom, let her actually hang for a beat: 6 degree tilt, x_x after 5 seconds, then she hauls herself
up with a spring impulse and dusts off with one puff. Dropped onto the taskbar line she just sits,
no correction. This means softening ClampIntoWorkArea from an instant snap into a short grace, so
build it after wave 2 finishes in that method. Effort M, EmiDeskWindow.xaml.cs.

**Ignored mid-sentence.** If a priority 2 or 3 bubble is up and the foreground window changes before
the bubble is half done, she still finishes the line (a say is never cut), and when the bubble ends
she holds ._. for 2 seconds. At most once per 10 minutes. Effort S, the bubble-end path in
EmiDeskWindow.Bubble.cs plus a GetForegroundWindow check on the same 10Hz poll.

## 4. Moments of delight

These are the reasons to leave her out. All are rare on purpose; a surprise on a cooldown of hours
stays a surprise. Four are planned, cheapest first.

**The glass wipe.** Once a day she notices her screen is dusty and wipes it from the inside: the
existing SweepFx streak, =_= concentration, ending on the smug chain. Effort S, EmiDeskWindow.Fx.cs.

**The morning ceremony.** The first summon or sleep-wake between 5 and 11 local gets a slower CRT
warm-up (about 400ms extra), a stretch, and a line from the morningFirst pool that already exists.
Effort S.

**The field trip.** Once per 4 to 8 hours of being out, campus style: CRT off, reappear in another
corner of the same monitor (never under the cursor, never over the centre of the foreground window,
rect resolved at fire time), a glance around, three seconds of sway, then home the same way or the
moment she is clicked, because touch always wins. Effort M, the CRT off and on already exist in
EmiDeskWindow.Fx.cs, the timer lives in EmiDeskService.

**The moth.** At most once a day a six pixel moth flutters into her glass, orbits, and she tracks it
with the gaze lean, tries one bonk (a quick squash), misses, and wears x_x then ._. while it leaves.
One sprite, one timer, the gaze plumbing from section 3 does the acting. Effort M,
EmiDeskWindow.Glass.cs.

## 5. Touch sound

Campus gives every touch a tiny sound through the sfx bus: a pad on lift at 0.08, bump and thud on
drop at 0.12 and 0.16, a pop on pet, a chime on the third pet. The desktop should mirror those five
one-shots at the same gains through AudioService, and every one of them respects the mute arbiter
and stays silent while the avatar tube is speaking. Effort S to M depending on asset sourcing; the
campus assets can be reused as-is.

## 6. Build order

| wave | item | effort | file | status |
|---|---|---|---|---|
| A | blink parity 5200/110 + jitter | S | EmiDeskWindow.xaml.cs (OnIdleTick) + EmiDeskWindow.Alive.cs | DONE |
| A | cursor gaze lean, 10Hz poll | S | EmiAlive.cs + EmiDeskWindow.Alive.cs | DONE |
| A | approach perk + glance | S | EmiDeskWindow.Alive.cs | DONE |
| A | hover linger ^_^ then ¬_¬ | S | EmiDeskWindow.Alive.cs | DONE |
| A | micro-fidgets + rare stretch | S | EmiDeskWindow.Alive.cs | DONE |
| A | poke ladder + squish face | S | EmiDeskWindow.Alive.cs + React.cs | DONE |
| B | the shared spring (foundation) | M | EmiDeskWindow.xaml.cs + Fx.cs | |
| B | sleep ramp doze/ZzZ/wake | M | EmiDeskWindow.xaml.cs + EmiDeskService.cs | |
| B | shake dizzy + double-amp settle | S | EmiDeskWindow.xaml.cs | |
| B | edge lean + bottom dangle (after wave 2) | M | EmiDeskWindow.xaml.cs | |
| B | touch sfx, five one-shots | S | EmiDeskWindow.xaml.cs + AudioService | |
| B | ignored mid-sentence ._. | S | EmiDeskWindow.Bubble.cs | |
| C | glass wipe + morning ceremony | S | EmiDeskWindow.Fx.cs | |
| C | field trip | M | EmiDeskService.cs + Fx.cs | |
| C | the moth | M | EmiDeskWindow.Glass.cs | |

Wave A is deliberately all watching and no physics: it needs one poll timer and a handful of face
swaps, lands in a day or two, and on its own turns the stare into eye contact. Wave B gives her the
body. Wave C gives people stories to tell each other about what she did.

**Wave A landed 2026-08-29** on `feat/emi-desk`. It came in on the shape above with two notes for
whoever picks up wave B. First, the numbers and the decisions live in a new pure file,
`Services/EmiDesk/EmiAlive.cs`, so the whole wave is unit-testable without a window: the window half
in `Windows/EmiDesk/EmiDeskWindow.Alive.cs` only reads the cursor, converts it, and asks. Second,
there is exactly ONE 100ms poll for all six items, started and stopped by her visibility, so wave B's
spring should ride that same tick rather than add a second clock. See EMI_DESK_PRIMER section 14.
