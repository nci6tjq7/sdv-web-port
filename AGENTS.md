Superpowers — Contributor Guidelines
========================================

## Platform: z.ai

This is the z.ai-adapted version of [obra/superpowers](https://github.com/obra/superpowers) v6.0.3.

### Skill Architecture

- 14 active skills in `skills/` directory
- Each skill has a `SKILL.md` with YAML frontmatter (name + description)
- Skills are loaded via `Skill(command="skill-name")` using bare names (no prefix)

### Tool Mapping (z.ai vs Claude Code)

| Claude Code | z.ai |
|-------------|------|
| `Skill("superpowers:skill-name")` | `Skill(command="skill-name")` |
| `Task(subagent_type="...")` | `Task(subagent_type="...")` (same API) |
| CLAUDE.md | AGENTS.md |
| DOT graphs | Mermaid diagrams |
| `${CLAUDE_PLUGIN_ROOT}` | `${PLUGIN_ROOT}` |

### Key Differences

1. **No named agent types**: z.ai uses `general-purpose` for all subagents
2. **Skill names**: Use bare names without `superpowers:` prefix
3. **Subagent tools**: Have Read, Write, Edit, MultiEdit, Bash, Glob, Grep, LS, TodoWrite, TodoRead, Skill (no Task/send_message/Complete)
4. **Worklog**: All agents share `/home/z/my-project/worklog.md`
5. **Output directory**: Deliverables go to `/home/z/my-project/download/`

### Hooks

Session-start hooks run automatically on z.ai via `hooks/session-start`. The hook uses `PLUGIN_ROOT` (falls back to `CLAUDE_PLUGIN_ROOT` on Claude Code) to locate the skills directory. On z.ai, hooks are informational only — they print a status message but do not auto-install skills.

### Upstream Sync

Branch `feat/v6.0.3-catchup` is based on upstream `v6.0.3` tag with z.ai adaptations applied as an overlay.