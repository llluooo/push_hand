"""为新增物理原型源文件分配稳定 GUID；只创建缺失的 .meta，不改已有资源或 GUID。"""
from pathlib import Path
import re
import uuid

root = Path(__file__).resolve().parents[1]
templates = {
    ".cs": root / "Assets/_Project/Scripts/Runtime/FighterView.cs.meta",
    ".asmdef": root / "Assets/_Project/Scripts/Runtime/PushHands.Runtime.asmdef.meta",
    ".ttf": root / "Assets/_Project/Art/Fonts/PushHandsUI.ttf.meta",
    ".txt": root / "Assets/_Project/Art/Fonts/characters.txt.meta",
    ".md": root / "Assets/_Project/Art/Fonts/characters.txt.meta",
    "folder": root / "Assets/_Project/Art/Fonts.meta",
}
folders = [root / "Assets/_Project/Scripts/Physics", root / "Assets/_Project/Art/PhysicalFonts"]
paths = list(folders)
for folder in folders:
    paths.extend(path for path in folder.iterdir() if path.suffix != ".meta")
paths.extend(root / path for path in (
    "Assets/_Project/Editor/PhysicalBuilder.cs",
    "Assets/_Project/Tests/EditMode/PhysicalRulesTests.cs",
    "Assets/_Project/Tests/PlayMode/PhysicalTests.cs",
))
created = 0
for path in paths:
    metadata = Path(str(path) + ".meta")
    if metadata.exists():
        continue
    key = "folder" if path.is_dir() else path.suffix
    if key not in templates:
        raise ValueError(f"未声明的导入类型：{path}")
    source = templates[key].read_text(encoding="utf-8")
    metadata.write_text(re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, source), encoding="utf-8")
    created += 1
print(f"新增 {created} 个 .meta；已有 GUID 未修改。")
