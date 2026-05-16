# Local AI with Ollama: How the Companion's Brain Works

> A complete tour of the local-AI integration in the Conditioning Control Panel:
> what it is, why it exists, how the setup wizard works, how a chat request
> travels from your keyboard to the model and back, how the persona is built,
> how effect commands are emitted and gated, and how to troubleshoot when
> something goes wrong.

This guide is for users who want to know exactly what the app is doing on their
machine, for tinkerers who want to swap models or hosts, and for anyone curious
about how the Companion gets its personality.

---

## Table of Contents

1. [What is "Local AI"?](#1-what-is-local-ai)
2. [Why local instead of cloud?](#2-why-local-instead-of-cloud)
3. [Architecture at a glance](#3-architecture-at-a-glance)
4. [The setup wizard, step by step](#4-the-setup-wizard-step-by-step)
5. [The request lifecycle](#5-the-request-lifecycle)
6. [How the persona is built](#6-how-the-persona-is-built)
7. [The enrichment block and structured output](#7-the-enrichment-block-and-structured-output)
8. [Effect commands: letting the AI control the app](#8-effect-commands-letting-the-ai-control-the-app)
9. [Safety: permissions, caps, and the master toggle](#9-safety-permissions-caps-and-the-master-toggle)
10. [Persistent chat memory](#10-persistent-chat-memory)
11. [Warm-up, lifecycle, and shutdown](#11-warm-up-lifecycle-and-shutdown)
12. [Error handling and fallbacks](#12-error-handling-and-fallbacks)
13. [Picking a model](#13-picking-a-model)
14. [Privacy](#14-privacy)
15. [Troubleshooting](#15-troubleshooting)
16. [Advanced: pointing at a remote Ollama](#16-advanced-pointing-at-a-remote-ollama)
17. [Glossary](#17-glossary)

---

## 1. What is "Local AI"?

The Companion (the floating avatar that talks to you, reacts to your screen,
and can trigger effects) has two possible brains:

- **Cloud AI** - the default. Your chat lines go to a small proxy server we
  run, which forwards them to a hosted language model. Comes with daily limits
  (100/day for free, 1000/day for supporters), no install required.
- **Local AI** - you run a language model on your own computer using
  [Ollama](https://ollama.com). Nothing leaves your machine. No request limits.
  No subscription required. You pay in disk space, RAM, and (optionally) GPU
  cycles instead.

Both brains implement the same internal interface (`IAiService`) so every
feature that talks to the Companion - chat, screen awareness, video reactions,
lock screen, keyword catches - works identically with either backend. You can
switch between them in **Companion → AI** at any time without restarting the
app.

## 2. Why local instead of cloud?

You'd choose local AI if any of these matter to you:

- **Privacy.** Chat lines, your screen context, your kink list - none of it
  travels over the internet. The model lives in `%LOCALAPPDATA%\Programs\Ollama`
  and runs on `http://localhost:11434`. There is no network egress from the
  app once it's set up (apart from update checks and any unrelated features).
- **No daily limits.** The cloud proxy caps requests to keep costs sane.
  Local AI is bounded only by how fast your hardware can run the model.
- **No login.** Cloud AI needs an account; local AI does not.
- **Customization.** Want to run a 70B model? Swap to a model fine-tuned for
  roleplay? Run a totally different persona? You can do all of that - just
  `ollama pull` a different tag and point the app at it.
- **Offline use.** Once installed, local AI works with the network unplugged.

The tradeoffs are real:

- **Disk.** The default `qwen3.5:latest` model is about **6.6 GB**. Bigger
  models can be 20–40 GB.
- **First response is slow.** Cold-start (model loading from disk into RAM
  or VRAM) is ~30–60 seconds for an 8B-class model on CPU. The app warms the
  model on startup to hide this, but the very first request after a fresh
  install still takes a moment.
- **Response time depends on your hardware.** On a GPU, replies feel snappy
  (1–3 seconds). On a CPU-only laptop, a chat reply can take 10–20 seconds.
  Reasoning models (qwen3, deepseek-r1) are slower still, which is why the
  app sends `think:false` to disable their internal reasoning phase by default.

## 3. Architecture at a glance

Everything in the Companion's brain lives under
`Services/AIService/`. The shape:

```
IAiService              ← interface every provider implements
├── AiService           ← cloud-proxy provider (default)
└── LocalAiService      ← local-Ollama provider

AiServiceStrategy       ← routes calls to whichever provider the user picked
OllamaSetupService      ← detect / download / install / pull / smoke-test
LocalAiSetupWizard      ← the onboarding window the user sees
AiResponseParser        ← extracts text + effect commands from model output
Enrichment/
├── PromptService       ← builds the JSON-output instructions
└── KnowledgeService    ← loads assets/knowledge.json into the enrichment block
AiCommandService        ← gates and dispatches effect commands the AI emits
```

`AiServiceStrategy` is what the rest of the app talks to. It checks
`CompanionPrompt.UseLocalAi` on every call and lazily constructs whichever
provider is active. Switching providers at runtime is free - no restart, no
re-init.

The cloud provider (`AiService`) is stateless: every request includes the full
system prompt and the user line; the proxy holds no state. The local provider
(`LocalAiService`) holds a persistent chat history in memory and on disk so
the Companion can remember you across sessions.

## 4. The setup wizard, step by step

Opening **Companion → Use Local AI** for the first time launches
`LocalAiSetupWizard`. It's a single window that walks through every step
needed to go from zero to a working local model. The whole flow is in
`OllamaSetupService` - the wizard is just a UI on top.

### Step 1 - Detect

Before showing you anything, the wizard probes the machine:

- Looks for `ollama.exe` and `ollama app.exe` under
  `%LOCALAPPDATA%\Programs\Ollama\`. Either being present means Ollama is
  installed.
- Calls `GET http://localhost:11434/api/tags` with a 4-second timeout to see
  whether the Ollama HTTP server is running and which models are pulled.

It then picks one of four states:

| State | What it means | What happens next |
|---|---|---|
| **Ready** | Ollama is running, target model is pulled | Skip to smoke test |
| **RunningNoModel** | Ollama is running but the target model isn't there | Skip to pull |
| **InstalledNotRunning** | Ollama is installed but the HTTP server isn't up | Start `ollama serve` headlessly, then continue |
| **NotInstalled** | No Ollama at all | Show the consent screen |

This means a user who already has Ollama set up for some other reason gets a
near-instant "ready" instead of being walked through an unnecessary install.

### Step 2 - Consent

If Ollama isn't installed, the wizard asks for explicit consent before doing
anything that touches disk. It tells you:

- Ollama is about to be downloaded from the official installer URL.
- The default model (`qwen3.5:latest`) is about 6.6 GB.
- You can change the model under "Advanced" - for an unknown custom model
  the wizard says "size varies" because it can't know in advance.

There's also a "manual install" link that opens https://ollama.com/download
in your browser, for people who'd rather install it themselves and skip the
silent install path.

### Step 3 - Download installer

The wizard streams `OllamaSetup.exe` from
`https://ollama.com/download/OllamaSetup.exe` to your `%TEMP%` folder.
Progress is reported every 200 ms with a live rate (e.g.
`240.5 MB / 700.0 MB (34%) · 18.3 MB/s`). If you cancel mid-download, the
partial file is cleaned up.

### Step 4 - Silent install

The downloaded installer is launched with the NSIS silent flag (`/S`).
No installer UI, no progress bar from Ollama itself - just a hidden process
that puts Ollama under `%LOCALAPPDATA%\Programs\Ollama\`. After the process
exits with code 0, the wizard polls `/api/tags` for up to 60 seconds waiting
for the service to come up.

Two safety choices here:

- The **cancel button is disabled** during install. Ollama's NSIS installer
  doesn't roll back cleanly if interrupted, and a half-installed Ollama is
  worse than a finished one you can uninstall.
- If the post-install auto-start doesn't bind port 11434 within 60 seconds,
  the wizard spawns `ollama.exe serve` itself, with a hidden window. It
  deliberately avoids `ollama app.exe` because that's the GUI chat client in
  newer Ollama versions and would pop a window.

The wizard tracks any `ollama serve` process it spawns and terminates it on
app exit (more on this in [Section 11](#11-warm-up-lifecycle-and-shutdown)).
Servers started by the Ollama tray app or the installer's own auto-start are
left alone - only the process we spawned is killed.

The downloaded `OllamaSetup.exe` is deleted from `%TEMP%` on success. On
failure it's intentionally left behind so you (or a retry) can inspect it
without re-downloading 700 MB.

### Step 5 - Pull the model

The wizard streams `POST /api/pull` with `stream:true`. Ollama sends back
NDJSON: one JSON object per line, one per layer of the model file (think of
them as tar layers - the model is built from several large binary chunks).
Each line includes a `status`, a `digest`, and `completed`/`total` byte
counts so the wizard can show real progress per layer.

Notable details:

- The HTTP client uses `Timeout.InfiniteTimeSpan` here. A 6.6 GB pull over
  a residential connection can easily exceed any reasonable per-request
  timeout, and Ollama's layer-by-layer NDJSON output is the heartbeat.
- Ollama caches partial layers, so if you cancel and re-run, the pull picks
  up where it left off. No wasted bandwidth.
- Errors come back as `{"error":"..."}` in the stream - usually for unknown
  model names ("model 'lol' not found") - and the wizard surfaces them
  verbatim.

### Step 6 - Smoke test

Once the model is on disk, the wizard sends one tiny request:

```json
POST /api/chat
{
  "model": "qwen3.5:latest",
  "messages": [{"role": "user", "content": "Say hi in one word."}],
  "stream": false,
  "think": false
}
```

If a `message.content` comes back, the wizard records the elapsed time and
declares success. This serves two purposes: it warms the model into RAM (so
your first real chat is fast), and it proves end-to-end that everything is
wired up.

### Step 7 - Done

On success, the wizard writes two settings:

```
CompanionPrompt.UseLocalAi  = true
CompanionPrompt.AiModel     = <whatever you picked>
```

…and saves. From this point on, every Companion call routes through
`LocalAiService` instead of the cloud provider.

The error screen (Step.Error) has a **Retry** button that re-runs detection.
This is intentional - the right "next step" after a failure depends entirely
on what state Ollama is now in, and re-detecting is the cleanest way to
figure that out.

## 5. The request lifecycle

Here's what happens when you type a message into the Companion chat box and
hit enter, assuming local AI is selected. (Awareness reactions, video-done
hints, lock-screen comments, and keyword catches all follow the same path
with different system prompts and inputs.)

```
user input
   ↓
AiServiceStrategy.GetBambiReplyAsync(text, isUser:true)
   ↓
LocalAiService.GetAiResponseAsync
   ↓
build/refresh system prompt    ← BambiSprite.GetSystemPrompt()
inject enrichment block        ← PromptService.BuildEnrichmentMessage()
append user message
   ↓
POST http://localhost:11434/api/chat
{
  "model": "qwen3.5:latest",
  "messages": [system, enrichment?, …history…, user],
  "stream": false,
  "think": false
}
   ↓
Ollama loads the model (cached after first call), generates tokens
   ↓
response body → ExtractContent()
   ↓
AiResponseParser.Parse(content)
   ├── extract "response" text  → goes to the speech bubble
   └── extract "effects" array  → AiCommandService dispatches each
   ↓
append assistant turn to history, persist to disk async
   ↓
return clean text → avatar speech bubble
```

A few things worth highlighting:

**Concurrency control.** A `SemaphoreSlim(1, 1)` guarantees one in-flight
request at a time. Behavior depends on who's asking:

- **User-triggered request while busy** - drops the new call but returns a
  random "still thinking" phrase (`"Bambi's thinking real hard right now..."`)
  so you don't get silence. Mods can supply their own thinking phrases via
  `App.Mods?.GetPhrases("Thinking")`.
- **Automated request while busy** (awareness, video-done, etc.) - drops
  silently. Better to skip a passive reaction than queue them up and have
  the Companion fire stale comments seconds later.

**System prompt refresh.** The system message at index 0 is rebuilt on every
call. This means changes to your persona, knowledge base, mods, or content
mode take effect immediately - no need to restart or clear history.

**History rollback on failure.** If the request errors or returns empty
content, the just-appended user turn is popped off the history so it doesn't
poison future requests with an unanswered turn.

**`think:false`.** Reasoning models (qwen3, deepseek-r1, and their relatives)
have an internal "thinking" phase where they output long chains of reasoning
before the actual answer. For roleplay chat this adds 30–50 seconds of latency
for no benefit. Setting `think:false` cuts that out. Non-reasoning models
ignore the flag.

## 6. How the persona is built

The system prompt sent to the model is assembled by `BambiSprite` from
several layers. From outer to inner:

1. **The persona block.** The "Bad Influence Bestie" character description:
   tone (casual texting), topics (makeup, pink things, empty heads), role
   (tempt the user into being blank). If "Slut Mode" is on and the current
   personality preset defines a Slut Mode variant, that variant replaces the
   default personality text - same character, spicier vibe.
2. **Explicit reaction rules.** How the Companion reacts when the user
   mentions explicit topics: flustered redirect rather than full roleplay.
   This can be overridden per-personality.
3. **Knowledge base.** Lists of audio playlists and videos the Companion is
   allowed to recommend, with strict instructions to use exact titles
   (otherwise the app can't auto-link them). For BambiCloud playlists, the
   AI is told to wrap titles in markdown link syntax with the exact URL.
4. **Global knowledge base links.** Anything the user has added to the
   "Knowledge Base Links" list in settings - extra videos, custom content
   packs, the user's own files.
5. **HypnoTube link pool.** If the user configured their own pool (Bambi
   mode or Sissy Hypno mode), those video names are appended. Names are
   resolved via `AvatarTubeWindow.KnownVideoLinks` so the auto-linker can
   wrap them as clickable URLs.
6. **Screen awareness rules.** How to react to different categories of
   activity (work, social, shopping, streaming, hypno content, idle). The
   Companion is told the user's current context in the format
   `[Category: X | App: Y | Title: Z | Duration: N]` and expected to react
   appropriately.
7. **Output rules.** Length cap (typically ~15 words), emoji cap (1 per
   message), no bracket tags in the visible reply.
8. **Quiz context** (if you've taken the in-app quiz). The Companion sees
   your archetype and a short profile snippet, with instructions to
   reference it naturally ~20% of the time.
9. **Mod-aware substitutions.** If you're using a mod that renames the user
   ("Bambi" → "Unit" for the Drone mod, or your chosen term for Sissy Hypno
   mode), the entire prompt is run through a substitution pass.

The point of building it this way: every layer can be customized
independently. You can write a totally different persona while keeping the
knowledge base intact, or vice versa.

## 7. The enrichment block and structured output

When **"Allow AI to control effects"** is on, the local provider inserts an
extra context message right after the system prompt. This is the
"enrichment block" produced by `PromptService.BuildEnrichmentMessage`. It's
sent as a `user`-role message but clearly marked `[CONTEXT BLOCK - NOT
DIALOGUE]` so the model treats it as operating instructions rather than
something to reply to.

The enrichment block does four things:

### 7.1 Forces structured JSON output

```json
{
  "response": "<your in-character text reply>",
  "effects": [ <zero or more effect commands> ]
}
```

The block explicitly tells the model that **any** earlier persona instruction
saying "no brackets" or "respond only with text" is **overridden by this
format**. This matters: many community personality presets include strict
"no JSON, no tags, just text" rules, which would otherwise conflict with
the effect-emission format. The override resolves the conflict in favor of
the structured output, and the `response` field carries the plain-text reply
the user actually sees.

### 7.2 Tells the model when to fire effects

The block lists the supported commands and gives the model concrete examples
of phrases that should trigger them:

| User says | Effect to emit |
|---|---|
| "flash me" / "make me see flashes" | `flash_image` |
| "spawn bubbles" / "start bubbles" | `bubbles` (on) |
| "stop bubbles" | `bubbles` (off) |
| "subliminal X" / "flash the word X" | `subliminal` |
| "spiral" / "show me a spiral" | `spiral` |
| "pink filter" / "make my screen pink" | `pink` |
| "lock card" / "lock me with the mantra X" | `mantra_lockscreen` |
| "vibrate" / "buzz me" / "haptic" | `haptic` |
| "play X" | `audio` or `video` |

Crucially, the block also says: when the user is just chatting, leave
`effects` empty. **Don't fire unprovoked.** Combined with the per-effect
permission gates ([Section 9](#9-safety-permissions-caps-and-the-master-toggle)),
this is what keeps the AI from spam-firing flashes at you while you're
trying to have a normal conversation.

### 7.3 Provides live context

Two final blocks appear in the enrichment:

```
<time>2026-05-15 Thursday 2:47:32 PM</time>
<data>[ … knowledge.json contents as JSON … ]</data>
```

The time stamp gives the model a sense of when "now" is. The data block is
`assets/knowledge.json` - a flat list of static facts the Companion is
allowed to know (terminology, names, lore). `KnowledgeService` loads this
file at construction; if it's missing, the data block is just `[]`.

### 7.4 Sets reply etiquette

- Keep `response` short (the persona's word limit still applies).
- Don't echo the user's request word-for-word.
- When you DO trigger an effect, briefly acknowledge it ("Flashing for you,
  hot stuff~").
- Don't trigger video unprompted - videos are disruptive.

When the master "Allow AI to control effects" toggle is **off**, the entire
enrichment block is removed from the conversation. The model goes back to
producing plain-text replies, no JSON wrapping. The parser falls back to
treating any incidental JSON as garbage and stripping it out.

## 8. Effect commands: letting the AI control the app

The Companion can trigger 11 distinct effect types (`AICommandType`):

| Command | What it does | Data fields |
|---|---|---|
| `flash_image` | Flash random images on-screen | `Amount`, `Duration`, `Size`, `Opacity` |
| `bubbles` | Start/stop the bubble-popping minigame | `On`, `Frequency` |
| `subliminal` | Show subliminal text | `Text`, `Opacity` |
| `mantra_lockscreen` | Make the user chant a mantra | `Mantra`, `Amount` |
| `spiral` | Spinning spiral overlay | `On`, `Intensity` |
| `pink` | Pink color overlay | `On`, `Intensity` |
| `bounce` | Bouncing text overlay | `On` |
| `haptic` | Vibrate a connected toy/buttplug | `Intensity` (0–1), `Duration` |
| `audio` | Play an audio file | `Title`, `Path`, `Random` |
| `video` | Play a video file | `Title`, `Path`, `Random` |
| `getbacktome` | Schedule a follow-up after a delay | `Delay` (seconds) |
| `none` | No-op (model output we ignore) | - |

The parsing pipeline is in `AiResponseParser`. It's intentionally tolerant -
local models love to wrap JSON in markdown fences, mix prose and JSON, leave
trailing commas, or close braces incorrectly:

- If the response is wrapped in a ` ```json … ``` ` fence, the fence is
  stripped.
- If the response is pure JSON with a `response` field, it's parsed directly.
- Otherwise the parser scans the text for `{…}` blocks, tries to parse each
  one, replaces any with a `response` field by their content (so JSON becomes
  prose in the final output), and collects any `effects` arrays.
- `RepairJson` handles trailing commas, mismatched braces, and unquoted
  keys before parsing.
- `SanitizeResponse` strips any leftover `[Category: …]` or `[Mode/Tag]`
  tags that the model copied from the input.

The result is a `ParsedAiResponse` with clean text for the speech bubble
and a list of `AiCommandData` to dispatch.

Once parsed, each command goes through `AiCommandService.ExecuteCommand`:

1. Validate against `CompanionPrompt` settings (master toggle + per-effect).
2. Enforce a per-response cap (max 3 commands per AI reply).
3. Append a human-readable line to the **AI Brain → Live actions** feed on
   the Companion tab, so you can see what fired and when:
   `[14:47:32] 🫧 Bubbles started (5/min)`
4. Create a `CancellationTokenSource` for the command if it has a token
   (for effects that need to be cancellable, like long-running spirals).
5. Build the concrete command via `CommandFactory.CreateCommand` and run it
   asynchronously.

The 3-commands-per-reply cap is a hard cap. If the model emits five `flash`
effects in one response (which happens occasionally with some models), only
the first three execute.

## 9. Safety: permissions, caps, and the master toggle

The defaults are **conservative**. Even after you turn on local AI, the
Companion can't fire effects until you explicitly enable them. The relevant
settings live in `CompanionPromptSettings`:

```
AllowAiToControlEffects = false   ← MASTER TOGGLE
AllowAiBubbles          = true    ← visual, passive, on by default
AllowAiSubliminal       = true    ← visual, passive, on by default
AllowAiBounce           = true    ← visual, passive, on by default
AllowAiFlash            = false   ← intrusive, off by default
AllowAiVideo            = false   ← disruptive, off by default
AllowAiAudio            = false   ← disruptive, off by default
AllowAiOverlay          = false   ← intrusive (spiral + pink), off by default
AllowAiLockCard         = false   ← intrusive, off by default
AllowAiHaptic           = false   ← hardware, off by default
AllowAiGetBackToMe      = false   ← recursive, off by default

MaxAiHapticIntensity    = 0.6     ← upper bound regardless of AI-emitted value
```

The dispatcher (`AiCommandService.ExecuteCommand`) checks:

1. **`AllowAiToControlEffects`** - the master toggle. Off → drop every
   command silently. This is also reflected in the enrichment block: if the
   master toggle is off, the enrichment block isn't even sent, so the model
   reverts to plain-text replies.
2. **Per-effect toggle** - drop if disabled.
3. **Batch cap** - at most 3 commands per AI response.
4. **Per-command caps** - applied at execution time. Haptic intensity is
   clamped to `MaxAiHapticIntensity`; flash counts, durations, frequencies,
   and intensities are all clamped to sane ranges.

This three-layer defense means even a misbehaving or jailbroken model can't
do something destructive - at worst it spams logs with rejected commands.

## 10. Persistent chat memory

The local provider remembers your conversation across app launches.
This is one of the key differences from the cloud provider, which is
stateless by design.

How it works:

- After every successful exchange, the user/assistant pair is appended to
  the in-memory history (`_messages`).
- An async write fires (`Task.Run(PersistHistory)`) to flush the dialogue
  to `%APPDATA%\ConditioningControlPanel\local_chat_history.json`. Disk I/O
  is off the response path so chat latency is unaffected.
- On next app launch, `LoadPersistedHistory` reads that file back and
  seeds `_messages`. The system prompt and enrichment block are NOT
  persisted - they're rebuilt fresh on every call so prompt edits take
  effect immediately.

The persisted file is capped at **50 pairs** (100 messages). When the cap is
exceeded, the oldest pairs are dropped first. This keeps the file under
~200 KB in practice and bounds the context the model has to chew through on
every request.

You can turn this off in **Companion → AI** by unchecking "Remember
chat between sessions" - that flips `ChatMemoryEnabled` to false and the
provider stops both reading and writing the file. Clearing memory is also
available; it deletes both the in-memory history and the on-disk file. The
"Clear chat" actions on the avatar and tray also clear local history via
`AiServiceStrategy.ClearLocalHistory()`.

## 11. Warm-up, lifecycle, and shutdown

Two life-cycle hooks help the local provider feel snappy and behave well.

### Warm-up on startup

In `App.OnStartup`, right after the strategy is constructed:

```csharp
Ai = new AiServiceStrategy();
Commands = new AiCommandService();

if (Ai is AiServiceStrategy aiStrategy)
{
    _ = Task.Run(async () => { try { await aiStrategy.WarmUpLocalAsync(); } catch { } });
}
```

`WarmUpLocalAsync` is a no-op if cloud AI is selected. For local users it
sends `POST /api/generate` with `model=<configured>` and `keep_alive=30m`
and an empty body. Ollama interprets this as "load this model into memory
but don't generate anything." The `keep_alive` value asks the model to stay
resident longer than the default 5 minutes - without this, the model would
unload after 5 minutes of inactivity and the next chat would pay the
cold-start cost again.

Warm-up is **fire-and-forget** and silent on failure. If Ollama isn't
running yet, the warm-up just logs and moves on - the next real chat will
surface a clear error.

### Shutdown

If the wizard spawned `ollama serve` itself (because the post-install
auto-start didn't fire), that process is tracked. On `App.OnExit`:

```csharp
try { Services.AIService.OllamaSetupService.StopSpawnedServer(); }
catch (Exception ex) { Logger?.Warning(ex, "Failed to stop spawned Ollama server"); }
```

Only the process we spawned is killed. Servers started by the Ollama tray
app or the installer's own auto-start are left running - they belong to
the user, not to us.

### Host changes

If you change `AiOllamaHost` while the app is running (say, to point at a
remote machine - see [Section 16](#16-advanced-pointing-at-a-remote-ollama))
the `EnsureHost` check on every request notices the change, disposes the
old `HttpClient`, and rebuilds one against the new base address. No
restart needed.

## 12. Error handling and fallbacks

Local model failures look very different from cloud failures, so the local
provider has dedicated error-to-text mapping. The error string the user
sees is computed by `DescribeChatException` (for transport failures) and
`DescribeOllamaError` (for HTTP errors with a body):

| Symptom | What you see | What it means |
|---|---|---|
| Connection refused | `(Can't reach Ollama at … - looks like it isn't running. Start Ollama, or install it from https://ollama.com)` | The HTTP server isn't bound - Ollama crashed or never started |
| DNS failure | `(Can't reach Ollama host … - check the host setting in Companion → AI)` | Wrong host name, almost always a typo in a remote-host config |
| Timeout | `(Ollama took too long to respond. The first request after launch can take ~30-60s as the model loads - try once more.)` | Model was cold and didn't finish loading inside the 5-minute client timeout, or you picked a huge model |
| 404 with "model not found" | `(Ollama: model 'X' not found - check 'ollama list' or pull it)` | Settings point at a tag you don't have pulled |
| Generic HTTP error | `(Ollama HTTP NNN: …)` | Surfaces the structured `error` field from Ollama if present |

If a request returns 200 but with empty content (rare, but seen with some
models on heavy load), the user gets the mode-appropriate fallback line
("Bambi's head is so empty right now~ *giggles*") and the user turn is
rolled back from history.

Any exception thrown by the chat call is caught and logged, and the user
gets a descriptive line rather than a blank speech bubble. The semaphore
is always released in `finally`, even if it's been disposed (which only
happens when the app is mid-shutdown).

## 13. Picking a model

The default is `qwen3.5:latest`. It's a good fit because:

- ~6.6 GB (fits in most consumer setups).
- Reasoning model - so it can follow the structured-output instructions
  reliably - but we send `think:false` to skip the slow reasoning phase
  during chat.
- Strong on instruction-following and JSON output, which matters for the
  effect-command flow.

That said, the provider is model-agnostic. Anything you can pull through
Ollama and chat with via `/api/chat` should work. To switch models:

1. Pull the new tag manually: `ollama pull mistral-nemo:latest`.
2. Open **Companion → AI** and either re-run the setup wizard with an
   advanced model name, or edit the value in settings directly.
3. The strategy notices the change on the next chat - no restart needed.

Rough guidance:

- **3B–8B params** (qwen3.5, llama3.1:8b, mistral-nemo, gemma2:9b) - best
  match for chat latency on consumer hardware. ~5–8 GB on disk. Most users
  should start here.
- **13B–22B params** (mistral-small, llama3.1:13b) - noticeably better
  prose quality, much slower without a GPU. ~10–14 GB.
- **30B+** - if you have a real GPU with 24+ GB VRAM, the prose lift is
  real but the warm-up is brutal. Not recommended unless you know what
  you're doing.
- **Reasoning models** (qwen3, deepseek-r1) - work fine; remember the
  provider always sends `think:false` to keep latency reasonable.
- **Uncensored fine-tunes** (dolphin, hermes, etc.) - these can be useful
  if you find the default model is too prudish about explicit roleplay.
  Look for `:uncensored` or `:abliterated` tags on Ollama's model registry.

You can list what's installed with `ollama list` from a terminal, or look at
`http://localhost:11434/api/tags` in a browser.

## 14. Privacy

Once local AI is set up, the only network traffic from the AI feature is:

- Ollama's own model downloads (only when you run `ollama pull` or use the
  wizard's pull step) - go directly to Ollama's CDN.
- The Ollama installer download (once, from `ollama.com`) - only during the
  wizard's install step.

After that, every chat request goes to `http://localhost:11434`. The model
itself runs entirely on your machine. The Companion's chat history is stored
in `%APPDATA%\ConditioningControlPanel\local_chat_history.json` in plain
JSON - readable by anything that can open a text file. If that matters to
you, turn off "Remember chat between sessions" or use full-disk encryption.

The cloud provider, by contrast, sends each chat line + your system prompt +
your screen-awareness context to the proxy server, which forwards to a
hosted model. We log request counts and basic auth state but do not log
chat content.

## 15. Troubleshooting

### "Can't reach Ollama at http://localhost:11434/"

Ollama isn't running. Try:

1. Start the Ollama tray app (Start menu → Ollama).
2. Or, from a terminal: `ollama serve`. If you see "address already in use,"
   the server is running but the app can't reach it - check Windows firewall.
3. Open `http://localhost:11434` in a browser. You should see `Ollama is
   running`. If you don't, Ollama isn't actually up.

### "Ollama took too long to respond"

The model loaded for the first time and exceeded the 5-minute timeout.
Either:

- Wait, then try again. Subsequent calls reuse the loaded model.
- Pick a smaller model. `qwen3.5:latest` is 8B; try `gemma2:2b` or
  `phi3.5:3.8b` if your hardware is struggling.
- Run `ollama ps` from a terminal to see what's loaded. If the model isn't
  listed, it didn't finish loading.

### "model 'X' not found"

The tag in your settings isn't pulled. `ollama pull X` from a terminal,
then try again. Or re-run the setup wizard, which handles the pull for you.

### The Companion replies with JSON or curly braces

You're seeing raw model output that the parser couldn't clean. This usually
means:

- The model isn't producing the expected `{response: …, effects: […]}`
  shape. Try a different model - small models (~1B) sometimes can't
  follow structured-output instructions reliably.
- You have a custom personality preset that aggressively forbids structured
  output. The enrichment block is supposed to override this, but some
  models miss the override. Either edit the preset or turn off "Allow AI to
  control effects" (which removes the enrichment block entirely).

### Effects fire even though I told it not to

Check three places:

- `Companion → AI → Allow AI to control effects` - the master toggle.
- The per-effect toggles right below it.
- The "Live actions" feed on the Companion tab - it shows what actually
  fired in the last 30 actions, so you can confirm the gate is working.

If you see commands in the feed for effects you've disabled, that's a bug
- file an issue with a copy of the relevant `logs/crash.log` lines.

### The AI is repetitive / boring

Chat history is the usual culprit. Try "Clear chat memory" from the
Companion tab - that wipes both the in-memory history and the on-disk file.
If a long conversation has driven the model into a rut, a fresh start
often helps.

### Effects feel laggy

Local model latency is real. A chat reply that triggers a bubble effect on
a CPU-only laptop takes the chat latency (5–15s) plus the effect dispatch
time (~50 ms). On a GPU, the chat call drops to 1–3 seconds and the lag
becomes imperceptible. If you have a CUDA-capable GPU and Ollama isn't
using it, check `ollama ps` - if the model is "100% CPU," Ollama hasn't
detected the GPU. Reinstall Ollama with NVIDIA drivers up to date.

## 16. Advanced: pointing at a remote Ollama

The `AiOllamaHost` setting accepts any URL. If you have a beefier machine
on your LAN (or a remote server you trust), point the app at its Ollama
instead:

1. On the server: start Ollama with `OLLAMA_HOST=0.0.0.0` so it binds to
   all interfaces. By default Ollama only listens on localhost.
2. Make sure the model you want is pulled on that machine.
3. In the app: edit **Companion → AI → Ollama Host** to
   `http://your-server:11434/`.
4. The strategy notices the host change on the next request, rebuilds its
   HTTP client, and you're done.

Caveats:

- Ollama has no authentication. Don't expose it to the public internet
  without a reverse proxy and auth in front of it.
- Network latency is added to every chat call. On a LAN this is negligible;
  over WAN it can be the dominant factor.
- The default 5-minute client timeout still applies. Very slow remotes may
  need a smaller model or a closer server.

## 17. Glossary

- **Ollama** - A local-model runner from https://ollama.com. Installs as a
  background HTTP server (port 11434), pulls models from a registry, and
  serves them via a simple chat-completion API.
- **Cloud AI / proxy** - Our hosted service at
  `https://codebambi-proxy.vercel.app`, which forwards requests to a hosted
  model. The default option; needs a free account.
- **Local AI** - Ollama running on your machine, used by the app as a
  drop-in replacement for the cloud proxy.
- **Model / tag** - A specific weight file Ollama can serve, identified
  by a name like `qwen3.5:latest` or `mistral-nemo:12b-instruct`.
- **System prompt** - The character and rule description sent to the model
  at the start of every conversation. Built by `BambiSprite`.
- **Enrichment block** - An extra context message inserted between the
  system prompt and the dialogue, telling the model to output structured
  JSON and giving it access to live time + knowledge data. Only present
  when "Allow AI to control effects" is on.
- **Effect command** - A JSON object the AI can emit in the `effects` array
  to trigger app features (flash, bubbles, haptic, etc.).
- **Master toggle** - `AllowAiToControlEffects`. The single switch that
  controls whether the AI can trigger any effect at all.
- **Warm-up** - Loading the model into RAM/VRAM ahead of time so the first
  chat doesn't pay the cold-start cost. Done with an empty `/api/generate`
  call at app startup.
- **Persistent chat history** - The `local_chat_history.json` file in
  `%APPDATA%\ConditioningControlPanel\`. Caps at 50 user/assistant pairs.
  Only used by the local provider.

---

*Questions, suggestions, or "this section is wrong" reports - open an issue
at [CC-Labs-llc/ccp-bugs](https://github.com/CC-Labs-llc/ccp-bugs) or ping
in the Discord. The integration is still young (shipped in v5.8.4) and the
docs will evolve with it.*
