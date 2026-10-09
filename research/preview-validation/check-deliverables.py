"""Validate design-document anchors and candidate SQLite schema without mutating the app."""
from pathlib import Path
from html.parser import HTMLParser
import sqlite3
import json

root = Path(__file__).resolve().parents[2]
plan = root / 'docs/Windows壁纸软件-详细开发方案.html'
class Inspect(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.ids=[]; self.anchors=[]; self.local=[]; self.sections=0
    def handle_starttag(self, tag, attrs):
        data=dict(attrs)
        if 'id' in data: self.ids.append(data['id'])
        if tag=='h2': self.sections+=1
        if tag=='a' and 'href' in data:
            href=data['href']
            if href.startswith('#'): self.anchors.append(href[1:])
            elif not href.startswith(('https://','http://')): self.local.append(href)
parser=Inspect(); parser.feed(plan.read_text(encoding='utf-8'))
assert len(parser.ids)==len(set(parser.ids)), 'Duplicate IDs'
assert set(parser.anchors)<=set(parser.ids), 'Broken section links'
assert all((plan.parent/path).exists() for path in parser.local), 'Missing local references'
assert parser.sections==16

db=sqlite3.connect(':memory:')
db.executescript((root/'docs/database-schema.sql').read_text(encoding='utf-8'))
assert db.execute('PRAGMA user_version').fetchone()[0]==1
assert db.execute('PRAGMA foreign_key_check').fetchall()==[]
tables=db.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
assert len(tables)==13
now='2026-10-08T00:00:00Z'
db.execute('INSERT INTO sources(id,name,provider_kind,created_at) VALUES(?,?,?,?)',('s','Local','local',now))
db.execute('INSERT INTO wallpapers(id,source_id,remote_id,title,fetched_at) VALUES(?,?,?,?,?)',('w','s','1','Test',now))
db.execute('INSERT INTO variants(id,wallpaper_id,kind) VALUES(?,?,?)',('v','w','original'))
def rejected(sql,args):
    try: db.execute(sql,args)
    except sqlite3.IntegrityError: return
    raise AssertionError('Constraint did not reject invalid row')
rejected('INSERT INTO sources(id,name,provider_kind,created_at) VALUES(?,?,?,?)',('s2','local','local',now))
rejected('INSERT INTO wallpapers(id,source_id,remote_id,title,fetched_at) VALUES(?,?,?,?,?)',('w2','s','1','Duplicate',now))
rejected('INSERT INTO wallpapers(id,source_id,remote_id,title,fetched_at) VALUES(?,?,?,?,?)',('w3','missing','3','Orphan',now))
daily='INSERT INTO daily_selections(profile_id,local_date,wallpaper_id,reason,time_zone_id,selected_at) VALUES(?,?,?,?,?,?)'
db.execute(daily,('p','2026-10-08','w','daily','Asia/Hong_Kong',now))
rejected(daily,('p','2026-10-08','w','daily','UTC',now))
job='INSERT INTO download_jobs(id,wallpaper_id,variant_id,state,created_at,updated_at) VALUES(?,?,?,?,?,?)'
db.execute(job,('j','w','v','queued',now,now))
rejected(job,('j2','w','v','transferring',now,now))
db.execute("UPDATE download_jobs SET state='cancelled' WHERE id='j'")
db.execute(job,('j2','w','v','queued',now,now))
rejected(job,('j3','w','v','bogus',now,now))
assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
print(json.dumps({'plan_sections':parser.sections,'valid_navigation_links':len(parser.anchors),'sqlite_tables':len(tables),'schema_integrity':'ok','constraints_checked':['source name','source+remote id','foreign keys','daily uniqueness','active download uniqueness','job state']},ensure_ascii=False))
