"""Check only the context documents created in this task and direct local links.

No directory traversal, package inspection, network access, or product execution.
"""
from pathlib import Path
from urllib.parse import unquote, urlsplit
import json
import re

ROOT = Path(__file__).resolve().parents[2]
DOCS = [
    'AGENTS.md', 'PROJECT_INDEX.md', 'README.md',
    'docs/context/NOW.md', 'docs/context/MAP.md',
    'docs/context/RUNBOOK.md', 'docs/context/DECISIONS.md',
    'docs/context/RISKS.md', 'docs/context/history/README.md',
]
errors = []
contents = {}
for name in DOCS:
    path = ROOT / name
    if not path.is_file():
        errors.append(f'Missing document: {name}')
        continue
    text = path.read_text(encoding='utf-8')
    contents[name] = text
    if re.search(r'(?<![A-Za-z])[A-Za-z]:[\\/]|/(?:Users|home)/[^\s]+', text):
        errors.append(f'Private/absolute device path in {name}')
    if not text.startswith('# '):
        errors.append(f'Missing document title: {name}')

anchor_cache = {}
def anchors(path):
    if path in anchor_cache:
        return anchor_cache[path]
    found = set()
    # Read only directly referenced target headings/ID attributes.
    with path.open(encoding='utf-8') as stream:
        for line in stream:
            if path.suffix == '.md':
                match = re.match(r'^#{1,6}\s+(.+?)\s*$', line)
                if match:
                    value = match.group(1).lower()
                    value = re.sub(r'[^\w\s-]', '', value)
                    found.add(re.sub(r'\s', '-', value))
            elif path.suffix == '.html':
                found.update(re.findall(r'\bid=["\']([^"\']+)["\']', line))
    anchor_cache[path] = found
    return found

links = 0
for name, text in contents.items():
    owner = ROOT / name
    for raw in re.findall(r'(?<!!)\[[^\]\n]+\]\(([^)\n]+)\)', text):
        link = urlsplit(raw)
        if link.scheme or link.netloc:
            # Public download/source links are external references, not project routes.
            if link.scheme != 'https':
                errors.append(f'Unsupported external link in {name}: {raw}')
            continue
        target = (owner.parent / unquote(link.path)).resolve() if link.path else owner
        if not target.is_relative_to(ROOT):
            errors.append(f'Link escapes project in {name}: {raw}')
            continue
        if not target.is_file():
            errors.append(f'Missing link target in {name}: {raw}')
            continue
        if link.fragment and unquote(link.fragment) not in anchors(target):
            errors.append(f'Missing anchor in {name}: {raw}')
        links += 1

rules = contents.get('AGENTS.md', '')
required_read = [
    '按当前问题选择资料', '不默认读取所有项目文件', '优先使用已有有效信息',
    '只有证据不足', '全文读取仅用于', '索引是条件路由', '不为节省上下文跳过适用规则',
]
required_write = [
    '仅在任务允许且信息有实质变化时更新', '每类事实只设一个权威来源',
    '修改前读取目标部分', '当前状态直接修订', '区分事实、计划、建议、已确认决定与待核实事项',
    '命令已配置不等于执行成功', '核验日期注明对象与范围',
]
for requirement in required_read + required_write:
    if requirement not in rules:
        errors.append('Missing rule: ' + requirement)
if errors:
    print(json.dumps({'status': 'failed', 'errors': errors}, ensure_ascii=False, indent=2))
    raise SystemExit(1)
print(json.dumps({
    'status': 'passed', 'documents': len(DOCS), 'direct_links': links,
    'read_rules': len(required_read), 'write_rules': len(required_write),
    'scope': 'new context documents and directly linked files/anchors only',
}, ensure_ascii=False))
