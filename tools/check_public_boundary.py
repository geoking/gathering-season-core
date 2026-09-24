#!/usr/bin/env python3
"""Keep the Git index within the Core/CLI repository scope."""

from pathlib import PurePosixPath
import subprocess
import sys


ALLOWED_ROOT_FILES = {
    ".gitattributes", ".gitignore", "AGENTS.md", "README.md", "LICENSE",
    "GatheringSeason.sln",
}
ALLOWED_GUIDES = {
    "docs/README.md", "docs/cli/README.md", "docs/rules/README.md",
    "docs/rules/board-and-shop.md", "docs/rules/encounters.md",
    "docs/rules/world-events.md", "docs/architecture/compatibility.md",
}
ALLOWED_ROOT_DIRS = {"src", "tests", "tools", ".github"}
EXCLUDED_DIRS = {"docs", "plans", "evidence", "screenshots", "recordings", "assets"}
EXCLUDED_EXTENSIONS = {
    ".md", ".mdx", ".rst", ".pdf", ".docx", ".pptx", ".xlsx",
    ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".psd",
    ".mp4", ".mov", ".webm", ".mp3", ".wav", ".ogg", ".ttf", ".otf",
    ".unity", ".prefab", ".asset", ".meta", ".dll", ".pdb", ".zip",
}


def is_out_of_scope(name):
    path = PurePosixPath(name)
    if name in ALLOWED_ROOT_FILES or name in ALLOWED_GUIDES:
        return False
    return (
        path.parts[0] not in ALLOWED_ROOT_DIRS
        or any(part.lower() in EXCLUDED_DIRS for part in path.parts[:-1])
        or path.suffix.lower() in EXCLUDED_EXTENSIONS
    )


def main():
    result = subprocess.run(
        ["git", "ls-files", "--cached", "-z"],
        check=True, stdout=subprocess.PIPE,
    )
    paths = result.stdout.decode("utf-8").split("\0")
    rejected = [name for name in paths if name and is_out_of_scope(name)]
    if rejected:
        print("Paths outside the Core/CLI scope must be removed from the Git index:")
        print("\n".join(rejected))
        return 1
    print("Core/CLI repository scope check passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
