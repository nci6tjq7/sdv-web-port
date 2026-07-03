#!/usr/bin/env bash
#
# Superpowers z.ai — Installer
# Installs all superpowers skills + AGENTS.md into a z.ai project.
#
# Usage:
#   ./install.sh                      # Install to current project (auto-detect)
#   ./install.sh /path/to/project     # Install to specified project
#   ./install.sh --global             # Install to user-level (~/.agents/skills/)
#   ./install.sh --list                # List all installable skills and exit
#   ./install.sh --dry-run [target]    # Show what would be installed without copying
#   ./install.sh --uninstall [target]  # Remove all installed superpowers skills + AGENTS.md
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ── Configurable archive exclusion patterns ──
# Space-separated glob patterns. Directories matching any pattern are skipped
# during install/uninstall and skill counting.
ARCHIVE_PATTERNS="old-* archived-* deprecated-*"

# ── Parse flags ──
FLAG_LIST=0
FLAG_DRY_RUN=0
FLAG_UNINSTALL=0
FLAG_GLOBAL=0
POSITIONAL_ARGS=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --list)
            FLAG_LIST=1
            shift
            ;;
        --dry-run)
            FLAG_DRY_RUN=1
            shift
            ;;
        --uninstall)
            FLAG_UNINSTALL=1
            shift
            ;;
        --global)
            FLAG_GLOBAL=1
            shift
            ;;
        --help|-h)
            echo "Superpowers z.ai Installer"
            echo ""
            echo "Usage:"
            echo "  ./install.sh                      # Install to current project (auto-detect)"
            echo "  ./install.sh /path/to/project     # Install to specified project"
            echo "  ./install.sh --global             # Install to user-level (~/.agents/skills/)"
            echo "  ./install.sh --list               # List all installable skills and exit"
            echo "  ./install.sh --dry-run [target]   # Show what would be installed"
            echo "  ./install.sh --uninstall [target] # Remove all installed skills"
            echo ""
            echo "Flags:"
            echo "  --list       Print installable skills with rename mapping and exit"
            echo "  --dry-run    Print what would happen without making changes"
            echo "  --uninstall  Remove superpowers-* skill dirs and AGENTS.md"
            echo "  --global     Install/uninstall to user-level directory"
            exit 0
            ;;
        --*)
            echo "ERROR: Unknown flag '$1'" >&2
            echo "Run with --help for usage information." >&2
            exit 1
            ;;
        *)
            POSITIONAL_ARGS+=("$1")
            shift
            ;;
    esac
done

# ── Auto-detect runtime context ──
# The script can run in two contexts:
#   1. Package context: install.sh is next to skill dirs (after extracting a release)
#   2. Repo context:    install.sh is in scripts/ subdirectory (dev workflow)
RUNTIME_CONTEXT=""
if [[ -f "$SCRIPT_DIR/brainstorming/SKILL.md" ]]; then
    SKILL_SRC="$SCRIPT_DIR"
    AGENTS_SRC="$SCRIPT_DIR/AGENTS.md"
    VERSION_SRC="$SCRIPT_DIR/.version-bump.json"
    RUNTIME_CONTEXT="package"
elif [[ -f "$SCRIPT_DIR/../skills/brainstorming/SKILL.md" ]]; then
    SKILL_SRC="$SCRIPT_DIR/../skills"
    AGENTS_SRC="$SCRIPT_DIR/../AGENTS.md"
    VERSION_SRC="$SCRIPT_DIR/../.version-bump.json"
    RUNTIME_CONTEXT="repo"
else
    echo "ERROR: Cannot find superpowers skills." >&2
    exit 1
fi

# ── Helper: check if a directory name matches any archive pattern ──
matches_archive_pattern() {
    local name="$1"
    for pattern in $ARCHIVE_PATTERNS; do
        # Use bash glob matching
        # shellcheck disable=SC2254
        case "$name" in
            $pattern) return 0 ;;
        esac
    done
    return 1
}

# ── Helper: compute target (installed) name for a skill ──
compute_target_name() {
    local skill_name="$1"
    case "$skill_name" in
        superpowers)    echo "superpowers" ;;
        hooks-skill)    echo "superpowers-hooks" ;;
        *)              echo "superpowers-$skill_name" ;;
    esac
}

# ── Read source version ──
SOURCE_VERSION=""
if [[ -f "$VERSION_SRC" ]]; then
    # .version-bump.json doesn't store version directly; read from package.json
    PKG_JSON=""
    if [[ -f "$SCRIPT_DIR/package.json" ]]; then
        PKG_JSON="$SCRIPT_DIR/package.json"
    elif [[ -f "$SCRIPT_DIR/../package.json" ]]; then
        PKG_JSON="$SCRIPT_DIR/../package.json"
    fi
    if [[ -n "$PKG_JSON" ]]; then
        SOURCE_VERSION="$(python3 -c "import json; print(json.load(open('$PKG_JSON')).get('version',''))" 2>/dev/null | tr -d '[:space:]')"
    fi
fi

# ── Determine target directory ──
if [[ $FLAG_GLOBAL -eq 1 ]]; then
    TARGET_DIR="$HOME/.agents/skills"
    AGENTS_TARGET="$HOME/.agents/AGENTS.md"
elif [[ ${#POSITIONAL_ARGS[@]} -gt 0 ]]; then
    TARGET_DIR="${POSITIONAL_ARGS[0]}/skills"
    AGENTS_TARGET="${POSITIONAL_ARGS[0]}/AGENTS.md"
else
    # Auto-detect: find nearest git root or use current dir
    GIT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || true)"
    if [[ -n "$GIT_ROOT" ]]; then
        TARGET_DIR="$GIT_ROOT/skills"
        AGENTS_TARGET="$GIT_ROOT/AGENTS.md"
    else
        TARGET_DIR="$(pwd)/skills"
        AGENTS_TARGET="$(pwd)/AGENTS.md"
    fi
fi

# ══════════════════════════════════════════════════════════
# ── --list mode ──
# ══════════════════════════════════════════════════════════
if [[ $FLAG_LIST -eq 1 ]]; then
    echo "Installable skills (source -> target name):"
    echo ""
    count=0
    for skill_dir in "$SKILL_SRC"/*/; do
        [[ ! -d "$skill_dir" ]] && continue
        skill_name="$(basename "$skill_dir")"
        [[ ! -f "$skill_dir/SKILL.md" ]] && continue
        matches_archive_pattern "$skill_name" && continue
        target_name="$(compute_target_name "$skill_name")"
        printf "  %-45s -> %s\n" "$skill_name" "$target_name"
        count=$((count + 1))
    done
    echo ""
    echo "Total: $count skills"
    echo "Archive exclusion patterns: $ARCHIVE_PATTERNS"
    exit 0
fi

# ══════════════════════════════════════════════════════════
# ── --uninstall mode ──
# ══════════════════════════════════════════════════════════
if [[ $FLAG_UNINSTALL -eq 1 ]]; then
    echo "=== Superpowers z.ai — Uninstall ==="
    echo "Target skills:  $TARGET_DIR"
    echo "Target AGENTS.md: $AGENTS_TARGET"
    echo ""

    REMOVED=0

    # Remove superpowers-* directories
    if [[ -d "$TARGET_DIR" ]]; then
        for dir in "$TARGET_DIR"/superpowers*/; do
            [[ ! -d "$dir" ]] && continue
            dir_name="$(basename "$dir")"
            if [[ $FLAG_DRY_RUN -eq 1 ]]; then
                echo "  WOULD REMOVE: $dir_name"
            else
                rm -rf "$dir"
                echo "  REMOVED: $dir_name"
            fi
            REMOVED=$((REMOVED + 1))
        done
    fi

    # Remove AGENTS.md if it was installed by this script (check for Superpowers marker or Contributor Guidelines header)
    if [[ -f "$AGENTS_TARGET" ]] && (grep -q "Superpowers z.ai" "$AGENTS_TARGET" || grep -q "Superpowers — Contributor Guidelines" "$AGENTS_TARGET"); then
        if [[ $FLAG_DRY_RUN -eq 1 ]]; then
            echo "  WOULD REMOVE: AGENTS.md (contains Superpowers z.ai marker)"
        else
            backup_file="$AGENTS_TARGET.uninstall-backup.$(date +%Y%m%d%H%M%S)"
            cp "$AGENTS_TARGET" "$backup_file"
            echo "  BACKUP: $backup_file"
            rm -f "$AGENTS_TARGET"
            echo "  REMOVED: AGENTS.md"
        fi
        REMOVED=$((REMOVED + 1))
    fi

    # Remove bootstrap hint file
    PROJECT_ROOT_UNINSTALL="$(dirname "$AGENTS_TARGET")"
    BOOTSTRAP_FILE_UNINSTALL="$PROJECT_ROOT_UNINSTALL/.superpowers-bootstrap"
    if [[ -f "$BOOTSTRAP_FILE_UNINSTALL" ]]; then
        if [[ $FLAG_DRY_RUN -eq 1 ]]; then
            echo "  WOULD REMOVE: .superpowers-bootstrap"
        else
            rm -f "$BOOTSTRAP_FILE_UNINSTALL"
            echo "  REMOVED: .superpowers-bootstrap"
        fi
        REMOVED=$((REMOVED + 1))
    fi

    echo ""
    echo "Removed $REMOVED items."
    exit 0
fi

# ══════════════════════════════════════════════════════════
# ── Install mode (default) ──
# ══════════════════════════════════════════════════════════

echo "=== Superpowers z.ai Installer ==="
echo "Context: $RUNTIME_CONTEXT"
echo "Source skills: $SKILL_SRC"
echo "Target skills: $TARGET_DIR"
echo "Target AGENTS.md: $AGENTS_TARGET"
if [[ -n "$SOURCE_VERSION" ]]; then
    echo "Source version: $SOURCE_VERSION"
fi
if [[ $FLAG_DRY_RUN -eq 1 ]]; then
    echo "Mode: DRY RUN (no changes will be made)"
fi
echo ""

# ── Count skills to install (unified with install loop pattern) ──
SKILL_COUNT=0
for skill_dir in "$SKILL_SRC"/*/; do
    [[ ! -d "$skill_dir" ]] && continue
    skill_name="$(basename "$skill_dir")"
    [[ ! -f "$skill_dir/SKILL.md" ]] && continue
    matches_archive_pattern "$skill_name" && continue
    SKILL_COUNT=$((SKILL_COUNT + 1))
done
echo "Found $SKILL_COUNT skills to install (archive patterns: $ARCHIVE_PATTERNS)"

# ── Permission pre-check: verify target directory is writable ──
check_writable() {
    local dir="$1"
    local testfile
    testfile="$(mktemp "${dir}/.sp-write-test.XXXXXX" 2>/dev/null)" && rm -f "$testfile"
}
AGENTS_DIR="$(dirname "$AGENTS_TARGET")"
if [[ $FLAG_DRY_RUN -eq 0 ]]; then
    # Check skills target directory
    if [[ ! -d "$TARGET_DIR" ]]; then
        # Parent must be writable to create it
        PARENT_DIR="$(dirname "$TARGET_DIR")"
        if ! check_writable "$PARENT_DIR" 2>/dev/null; then
            echo "ERROR: Cannot write to $PARENT_DIR (needed to create $TARGET_DIR)" >&2
            exit 1
        fi
    else
        if ! check_writable "$TARGET_DIR" 2>/dev/null; then
            echo "ERROR: Target directory $TARGET_DIR is not writable" >&2
            exit 1
        fi
    fi
    # Check AGENTS.md target directory
    if ! check_writable "$AGENTS_DIR" 2>/dev/null; then
        echo "ERROR: Cannot write to $AGENTS_DIR (needed for AGENTS.md)" >&2
        exit 1
    fi
fi

# ── Create target if needed ──
if [[ $FLAG_DRY_RUN -eq 0 ]]; then
    mkdir -p "$TARGET_DIR"
fi

# ── Install skills (copy, not symlink — for portability) ──
echo ""
echo "Installing skills..."
INSTALLED=0

for skill_dir in "$SKILL_SRC"/*/; do
    [[ ! -d "$skill_dir" ]] && continue
    skill_name="$(basename "$skill_dir")"
    [[ ! -f "$skill_dir/SKILL.md" ]] && continue
    matches_archive_pattern "$skill_name" && continue

    target_name="$(compute_target_name "$skill_name")"

    # Handle existing skill directory: backup instead of rm -rf
    if [[ -d "$TARGET_DIR/$target_name" ]]; then
        backup_dir="${TARGET_DIR}/${target_name}.backup.$(date +%Y%m%d%H%M%S)"
        if [[ $FLAG_DRY_RUN -eq 1 ]]; then
            echo "  WOULD BACKUP: $target_name -> ${target_name}.backup.<timestamp>"
        else
            mv "$TARGET_DIR/$target_name" "$backup_dir"
            echo "  BACKED UP: $target_name -> $(basename "$backup_dir")"
        fi
    fi

    if [[ $FLAG_DRY_RUN -eq 1 ]]; then
        echo "  WOULD INSTALL: $skill_name -> $target_name"
    else
        cp -r "$skill_dir" "$TARGET_DIR/$target_name"
        echo "  OK: $skill_name -> $target_name"
    fi
    INSTALLED=$((INSTALLED + 1))
done

echo ""
echo "Installed $INSTALLED skills."

# ── Install AGENTS.md ──
echo ""
if [[ ! -f "$AGENTS_SRC" ]]; then
    echo "WARNING: AGENTS.md not found at $AGENTS_SRC, skipping."
else
    # Capture timestamp once to avoid cross-second mismatch
    agents_backup_ts="$(date +%Y%m%d%H%M%S)"
    if [[ -f "$AGENTS_TARGET" ]]; then
        backup_file="${AGENTS_TARGET}.backup.${agents_backup_ts}"
        if [[ $FLAG_DRY_RUN -eq 1 ]]; then
            echo "  WOULD BACKUP: AGENTS.md -> $(basename "$backup_file")"
        else
            # Verify AGENTS.md target directory is writable (already checked above)
            cp "$AGENTS_TARGET" "$backup_file"
            echo "  BACKED UP: AGENTS.md -> $(basename "$backup_file")"
        fi
    fi
    if [[ $FLAG_DRY_RUN -eq 1 ]]; then
        echo "  WOULD INSTALL: AGENTS.md"
    else
        cp "$AGENTS_SRC" "$AGENTS_TARGET"
        echo "Installed AGENTS.md"
    fi
fi

# ── Version comparison ──
if [[ -n "$SOURCE_VERSION" && -f "$AGENTS_TARGET" ]]; then
    INSTALLED_MARKER=""
    if grep -q "Version: " "$AGENTS_TARGET" 2>/dev/null; then
        INSTALLED_MARKER="$(grep "Version: " "$AGENTS_TARGET" 2>/dev/null | head -1 | sed 's/.*Version: //' | tr -d '[:space:]')"
    fi
    if [[ -n "$INSTALLED_MARKER" && "$INSTALLED_MARKER" != "$SOURCE_VERSION" ]]; then
        echo ""
        echo "NOTE: Version changed: $INSTALLED_MARKER -> $SOURCE_VERSION"
    fi
fi

# ── Initialize worklog if needed ──
PROJECT_ROOT="$(dirname "$AGENTS_TARGET")"
WORKLOG="$PROJECT_ROOT/worklog.md"
if [[ $FLAG_DRY_RUN -eq 0 && ! -f "$WORKLOG" ]]; then
    echo "# Superpowers z.ai Worklog" > "$WORKLOG"
    echo "Initialized worklog at $WORKLOG"
fi

# ── Create bootstrap hint file ──
BOOTSTRAP_FILE="$PROJECT_ROOT/.superpowers-bootstrap"
if [[ $FLAG_DRY_RUN -eq 0 ]]; then
    cat > "$BOOTSTRAP_FILE" <<'BOOTSTRAP_EOF'
# Superpowers Bootstrap
# Created by install.sh. Presence indicates superpowers is installed.
# The using-superpowers skill references this file as a first action.
# If you are reading this, superpowers is installed and ready.
# Invoke the skill system: Skill tool -> command="using-superpowers"
BOOTSTRAP_EOF
    echo "Created bootstrap hint: $BOOTSTRAP_FILE"
else
    echo "Would create bootstrap hint: $BOOTSTRAP_FILE"
fi

# ── Summary ──
echo ""
echo "=== Installation Complete ==="
echo "Skills:    $INSTALLED installed in $TARGET_DIR"
echo "AGENTS.md: $AGENTS_TARGET"
if [[ $FLAG_DRY_RUN -eq 1 ]]; then
    echo "Worklog:   (skipped in dry-run mode)"
else
    echo "Worklog:   $WORKLOG"
fi
if [[ -n "$SOURCE_VERSION" ]]; then
    echo "Version:   $SOURCE_VERSION"
fi
echo ""
# ── Post-install bootstrap guidance ──
echo ""
echo '  +=============================================================+'
echo '  |  Superpowers installed successfully!                       |'
echo '  |                                                             |'
echo '  |  FIRST SESSION ONLY: paste this as your FIRST message:     |'
echo '  |                                                             |'
echo '  |    /using-superpowers                                       |'
echo '  |                                                             |'
echo '  |  This triggers the full skill system. After that, all        |'
echo '  |  skills auto-load on every task. You only need to do         |'
echo '  |  this ONCE.                                                  |'
echo '  |                                                             |'
echo '  |  (Or just say "help me build X" - the enhanced description  |'
echo '  |   should auto-detect and trigger the skill.)                 |'
echo '  +=============================================================+'
echo ""
