# z.ai Tool Mapping

Skills use Claude Code tool names. When you encounter these in a skill, use your platform equivalent:

| Skill references | z.ai equivalent | Notes |
|-----------------|----------------|-------|
| `Read` (file reading) | `Read` | Same name, same behavior |
| `Write` (file creation) | `Write` | Same name, same behavior |
| `Edit` (file editing) | `Edit` | Same name, same behavior |
| `MultiEdit` (batch edits) | `MultiEdit` | Same name, same behavior |
| `Bash` (run commands) | `Bash` | Same name, same behavior |
| `Grep` (search file content) | `Grep` | Same name, same behavior |
| `Glob` (search files by name) | `Glob` | Same name, same behavior |
| `LS` (list directory) | `LS` | Same name, same behavior |
| `Skill` tool (invoke a skill) | `Skill` | Same name, same behavior |
| `Task` tool (dispatch subagent) | `Task` | Same name, same behavior |
| `TodoWrite` (task tracking) | `TodoWrite` | Same name, same behavior |
| `TodoRead` (read task list) | `TodoRead` | Same name, same behavior |
| `WebFetch` | Use `Skill` to invoke `web-reader` skill | z.ai uses skill-based web reading |
| `WebSearch` | Use `Skill` to invoke `web-search` skill | z.ai uses skill-based web search |
| `EnterPlanMode` / `ExitPlanMode` | N/A on z.ai | Claude Code concept. On z.ai, planning is handled by writing-plans skill. |
| `Cron` | `Cron` | Main agent only, NOT available to subagents |
| `Complete` | `Complete` | Main agent only, NOT available to subagents |
| `send_message` | `send_message` | Main agent only, NOT available to subagents |
| `sessions_spawn` | `Task` | Claude Code (OpenClaw) only. z.ai uses `Task` tool |
| `exec` | `Bash` | Claude Code (OpenClaw) only. z.ai uses `Bash` |

## z.ai Agent Types

z.ai's `Task` tool accepts a `subagent_type` parameter:

| Claude Code agent | z.ai equivalent | Notes |
|-------------------|----------------|-------|
| `general-purpose` | `"general-purpose"` | (Tools: * ) — all tools available |
| `Explore` | `"Explore"` | Fast codebase exploration |
| `Plan` | `"Plan"` | Software architect for implementation plans |
| `frontend-styling-expert` | `"frontend-styling-expert"` | CSS, UI/UX, responsive design |
| `full-stack-developer` | `"full-stack-developer"` | Next.js 16, React, Prisma |
| Named Claude Code agents (e.g. `superpowers:code-reviewer`, `superpowers:implementer`) | `"general-purpose"` with inlined prompt from template file | z.ai does not support named agent types. On z.ai, use `general-purpose` + copy the prompt from the matching template file (e.g. `code-reviewer.md`, `implementer-prompt.md`, `task-reviewer-prompt.md`). Note: upstream v6.0.3 merged spec-reviewer and code-quality-reviewer into `task-reviewer-prompt.md`. |

## Main Agent vs Subagent Tool Differences

| Tool | Main Agent | Subagent |
|------|-----------|----------|
| `Bash`, `Glob`, `Grep`, `LS` | Yes | Yes |
| `Read`, `Edit`, `MultiEdit`, `Write` | Yes | Yes |
| `TodoWrite`, `TodoRead` | Yes | Yes |
| `Skill` | Yes | Yes |
| `Task` | Yes | **No** (subagents cannot nest) |
| `Cron` | Yes | No |
| `Complete` | Yes | No |
| `send_message` | Yes | No |

## Key Behavioral Differences

1. **Subagents cannot nest**: Subagents do NOT have the `Task` tool. They cannot spawn further subagents. All subagent dispatch must be done by the main agent.

2. **Subagent prompt is everything**: Subagents see ONLY what's in the prompt. Include all necessary context, plan content, and file paths.

3. **Task ID injection**: Task IDs are not auto-assigned in z.ai. The main agent must manually include the Task ID in the prompt when dispatching.

4. **Work log**: Subagents should append work records to `/home/z/my-project/worklog.md` using the standard template.

5. **Project config**: Claude Code's `CLAUDE.md` / `GEMINI.md` maps to z.ai's `AGENTS.md`. Skills referencing "project config" should use `AGENTS.md` on z.ai.

6. **Parallel dispatch**: Multiple `Task` calls in a single message run concurrently — same as Claude Code.

7. **Model selection**: `Task` tool accepts `model` parameter: `"sonnet"` (default), `"haiku"` (fast/cheap), `"opus"` (most capable).
