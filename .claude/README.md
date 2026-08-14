# .claude

Project-scoped Claude Code configuration. Drop files in the two folders below and Claude Code picks them up automatically for anyone working in this repo.

```
.claude/
  skills/<skill-name>/SKILL.md   invoked as /<skill-name>, or loaded automatically
  agents/<anything>.md           subagent definitions, identity comes from the `name` field
```

This is Claude Code's own format. It is separate from the Pi (`.pi/skills`, `.pi/agents`) and kimi (`.kimi-code/skills`, `.kimi-code/agents`) sets already in this repo, which use different frontmatter and tool names. A skill or agent has to be duplicated per ecosystem; they do not share files.

Restart note: the file watcher only covers directories that existed when the session started. The first time you add a file to a folder that was empty at session start, restart Claude Code to load it. After that, edits are picked up within a few seconds with no restart.

## Skills

Path: `.claude/skills/<skill-name>/SKILL.md`. The directory name becomes the slash command. Supporting files (scripts, reference docs, templates) can sit next to `SKILL.md` in the same directory, and only load when the skill runs.

```markdown
---
description: What it does and when to use it. Claude reads this to decide when to load the skill.
disable-model-invocation: true
allowed-tools: Read, Grep, Bash
---

Instructions Claude follows when the skill runs.
```

All fields are optional; `description` is the one that matters, because it is the only part kept in context until the skill is invoked. `name` defaults to the directory name.

Fields worth knowing:

| Field | Effect |
|-------|--------|
| `description` | When to use it. Truncated at 1,536 chars in the listing, so put the key trigger first |
| `when_to_use` | Extra trigger phrases, appended to `description` in the listing |
| `disable-model-invocation` | `true` means only you can run it with `/name`. Use for anything slow or destructive |
| `user-invocable` | `false` hides it from the `/` menu. Use for background knowledge |
| `allowed-tools` | Pre-approved for the invoking turn only, so the skill does not stop for permission prompts |
| `argument-hint` / `arguments` | Autocomplete hint and named `$arg` substitution |
| `paths` | Glob patterns. Auto-loads only when working on matching files |
| `model` / `effort` | Override model or effort while the skill is active |
| `context: fork` | Run the skill in a forked subagent instead of the main conversation |
| `shell` | `bash` (default) or `powershell` for inline command blocks |

Body content can inline live command output before Claude reads it:

```
!`git diff --name-only HEAD`
```

Precedence: personal (`~/.claude/skills/`) overrides project. A project skill named `code-review` replaces the bundled `/code-review`.

## Agents

Path: `.claude/agents/<anything>.md`, scanned recursively, so `agents/review/security.md` is fine. The `name` field is the identity, not the filename. Keep names unique across the tree.

```markdown
---
name: my-agent
description: When Claude should delegate to this agent
tools: Read, Grep, Glob, Bash
model: inherit
---

System prompt. This is all the agent gets, plus the working directory. It does not
inherit the main conversation's system prompt or context.
```

`name` and `description` are required, everything else is optional.

- `tools`: comma-separated, capitalized exactly as Claude Code names them (`Read`, `Write`, `Edit`, `Grep`, `Glob`, `Bash`, `PowerShell`, `WebFetch`, `WebSearch`, `Task`). Omit to inherit every tool. An unrecognized-only list makes the agent fail to launch.
- `disallowedTools`: subtracted from the inherited or listed set.
- `model`: `sonnet`, `opus`, `haiku`, `fable`, a full id such as `claude-opus-5`, or `inherit` (the default).
- Also available: `permissionMode`, `maxTurns`, `skills` (preload skill content), `memory`, `effort`, `isolation: worktree`, `color`, `hooks`, `mcpServers`.

Invoke explicitly by name, or let Claude delegate based on `description`. Write the description in terms of when to delegate, not what the agent is.

Precedence: project (`.claude/agents/`) wins over personal (`~/.claude/agents/`), the opposite of skills.

## What is here now

- `skills/gates/` - runs the correct verification gate for whichever code tree changed.
- `agents/wpf-archaeologist.md` - read-only behavioral extraction from the legacy WPF tree without loading the 100KB+ files into the main conversation.

Both are examples as much as tools. Delete or rewrite them freely.
