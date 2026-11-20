#!/usr/bin/env python3
"""HaruKit 套件規範檢查：CONVENTIONS.md 中可機器檢查的規則。"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
CATEGORIES = ["UX", "Framework", "DependencyCore", "Tools"]
errors = []


def rel(path):
    return path.relative_to(ROOT).as_posix()


packages = []
for category in CATEGORIES:
    for manifest in sorted((ROOT / category).glob("*/package.json")):
        packages.append(manifest.parent)

# 1. .cs / .asmdef 必須有 .meta（§2.4）；~ 結尾的資料夾 Unity 不匯入，略過
for package in packages:
    for path in package.rglob("*"):
        if any(part.endswith("~") for part in path.relative_to(package).parts):
            continue
        if path.suffix in (".cs", ".asmdef") and not path.with_name(path.name + ".meta").exists():
            errors.append(f"缺少 .meta：{rel(path)}")

# 2. 版本一致：package.json、repo README 套件表、套件 README 開頭
readme = (ROOT / "README.md").read_text(encoding="utf-8")
for package in packages:
    data = json.loads((package / "package.json").read_text(encoding="utf-8"))
    version, tool = data["version"], package.name
    row = re.search(rf"^\|[^|\n]*\|\s*(?:\[{tool}\]\([^)]*\)|{tool})\s*\|\s*([^|\s]+)\s*\|", readme, re.M)
    if not row:
        errors.append(f"README.md 套件表找不到 {tool}")
    elif row.group(1) != version:
        errors.append(f"README.md 套件表 {tool} 是 {row.group(1)}，package.json 是 {version}")
    tool_readme = package / "README.md"
    if tool_readme.exists():
        line = re.search(r"目前套件版本：`([^`]+)`", tool_readme.read_text(encoding="utf-8"))
        if line and line.group(1) != version:
            errors.append(f"{rel(tool_readme)} 寫 {line.group(1)}，package.json 是 {version}")

# 3. 主選單路徑（§2.6）：HaruFamily/<Tool>/... 或 Assets/HaruFamily/...
menu = re.compile(r'\[MenuItem\("([^"]+)"')
for package in packages:
    for path in package.rglob("*.cs"):
        for match in menu.finditer(path.read_text(encoding="utf-8", errors="replace")):
            item = match.group(1)
            if item.startswith("Assets/HaruFamily/"):
                continue
            if not item.startswith(f"HaruFamily/{package.name}/"):
                errors.append(f"選單路徑不符 §2.6：{rel(path)} → {item}")

if errors:
    print("\n".join(errors))
    sys.exit(1)
print(f"通過：檢查了 {len(packages)} 個套件")
