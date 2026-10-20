"""从官方 OFL 源字体生成灰盒界面所需的常规字重子集。原始字体放在 Artifacts/Sources。"""
from pathlib import Path
import hashlib
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset
import argparse
import shutil

root = Path(__file__).resolve().parent.parent
source = root / 'Artifacts/Sources/NotoSansSC.ttf'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--physical', action='store_true', help='生成物理原型独立字体，保留灰盒字体')
args = parser.parse_args()
target = root / ('Assets/_Project/Art/PhysicalFonts' if args.physical else 'Assets/_Project/Art/Fonts')
font = TTFont(source)
font = instantiateVariableFont(font, {'wght': 400}, inplace=True)
text = ''.join(chr(i) for i in range(32, 127))
text += '\n'.join(p.read_text(encoding='utf-8') for p in (root / 'Assets/_Project/Scripts').rglob('*.cs'))
text += '推手灰盒等待加入玩家一二获胜平局迈步负暂停继续重置测试靶未就绪'
characters = ''.join(sorted(set(text)))
options = subset.Options()
options.name_IDs = ['*']
subsetter = subset.Subsetter(options=options)
subsetter.populate(text=characters)
subsetter.subset(font)
for name in font['name'].names:
    if name.nameID in (1, 3, 4, 6, 16):
        name.string = ('PushHandsUI-Regular' if name.nameID in (3, 6) else 'PushHands UI').encode(name.getEncoding())
target.mkdir(parents=True, exist_ok=True)
font.save(target / 'PushHandsUI.ttf')
if args.physical:
    shutil.copyfile(root / 'Assets/_Project/Art/Fonts/OFL.txt', target / 'OFL.txt')
(target / 'characters.txt').write_text(characters, encoding='utf-8')
(target / '来源.md').write_text(
    '# 灰盒界面字体\n\n'
    '来源：[Google Fonts Noto Sans SC](https://github.com/google/fonts/tree/main/ofl/notosanssc)。\n'
    '许可证见 OFL.txt。使用 fontTools 固定为 400 字重并按界面字符裁剪，修改后的字体名为 PushHands UI。\n'
    f'原始文件 SHA-256：`{hashlib.sha256(source.read_bytes()).hexdigest()}`。\n'
    '重新生成：将原始 NotoSansSC[wght].ttf 保存到 Artifacts/Sources/NotoSansSC.ttf 后运行 '
    f'`python Tools/PrepareUIFont.py{" --physical" if args.physical else ""}`；随后通过编辑器生成或更新 TMP 字体资源。\n', encoding='utf-8')
print(f'字体生成完成：{len(characters)} 个候选字符，{(target / "PushHandsUI.ttf").stat().st_size} 字节')
