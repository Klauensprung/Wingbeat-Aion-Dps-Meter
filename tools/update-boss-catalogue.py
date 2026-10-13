#!/usr/bin/env python3
"""Import factual boss/NPC IDs and instance associations; no site prose or artwork.
Requires Python 3.11+ and beautifulsoup4 (python -m pip install beautifulsoup4).
Run explicitly; Wingbeat never downloads data at runtime. Cached HTML is private build input.
"""
import argparse, concurrent.futures, datetime, hashlib, json, pathlib, re, time, urllib.request
from bs4 import BeautifulSoup
BASE = 'https://aion2data.com'
p = argparse.ArgumentParser()
p.add_argument('--cache', type=pathlib.Path, default=pathlib.Path('.cache/boss-catalogue'))
p.add_argument('--output', type=pathlib.Path, default=pathlib.Path('AionDpsMeter/Assets/bosses.json'))
p.add_argument('--refresh', action='store_true')
a = p.parse_args(); a.cache.mkdir(parents=True, exist_ok=True)
def page(path):
    if not re.fullmatch(r'/[a-z0-9/-]+', path): raise ValueError('Unexpected source path')
    dest = a.cache / (path.strip('/').replace('/', '_') + '.html')
    if not dest.exists() or a.refresh:
        for attempt in range(3):
            try:
                req = urllib.request.Request(BASE + path, headers={'User-Agent': 'Wingbeat-boss-catalogue/1.0'})
                with urllib.request.urlopen(req, timeout=45) as response: data = response.read(8_000_001)
                if len(data) > 8_000_000: raise ValueError('Oversized source page')
                dest.write_bytes(data); break
            except Exception:
                if attempt == 2: raise
                time.sleep(attempt + 1)
    data = dest.read_bytes()
    soup = BeautifulSoup(data, 'html.parser', from_encoding='utf-8')
    main = soup.find('main')
    if main is None or main.find('h1') is None: raise ValueError('Missing catalogue content: ' + path)
    return soup, main, hashlib.sha256(data).hexdigest()
def links(node, prefix):
    return sorted({x['href'] for x in node.select('a[href]') if x['href'].startswith(prefix)})
def parallel(paths, fn):
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        jobs = {pool.submit(fn, path): path for path in paths}
        for i, job in enumerate(concurrent.futures.as_completed(jobs), 1):
            results.append(job.result())
            if i % 25 == 0 or i == len(paths): print(f'Imported {i}/{len(paths)} pages', flush=True)
    return results
soup, main, _ = page('/dungeons')
version = re.search(r'client (\d+\.\d+\.\d+\.\d+ hotfix \d+)', soup.get_text(' ',strip=True))
if not version: raise ValueError('Missing client version')
client = version.group(1)
instances = {}
for section in main.select('section'):
    heading = section.find('h2')
    if not heading or heading.get_text(strip=True) == 'More': continue
    for path in links(section, '/dungeons/'):
        instances[path] = heading.get_text(' ', strip=True)
for index, kind in [('/dungeons/nightmare','Nightmare'),('/dungeons/sealed','Sealed Dungeon')]:
    _, body, _ = page(index)
    for path in links(body, '/dungeons/'):
        if path not in ['/dungeons/nightmare','/dungeons/sealed']: instances[path] = kind
instances.pop('/dungeons/nightmare', None); instances.pop('/dungeons/sealed', None)
def dungeon(path):
    soup, body, digest = page(path)
    if client not in soup.get_text(' ',strip=True): raise ValueError('Mixed source versions: '+path)
    bosses = []
    for section in body.select('section'):
        h = section.find('h2')
        if h and h.get_text(strip=True) == 'Bosses': bosses.extend(links(section, '/monsters/'))
    if instances[path] == 'Nightmare': bosses = links(body, '/monsters/')
    return {'Key':path.removeprefix('/dungeons/'), 'Name':body.h1.get_text(' ',strip=True), 'Kind':instances[path],
            'BossKeys':sorted({int(re.search(r'/monsters/(\d+)',b).group(1)) for b in bosses}),
            'Source':BASE+path, 'SourceSha256':digest, '_bossPaths':bosses}
dungeons = parallel(sorted(instances), dungeon)
_, main, _ = page('/bosses')
monster_paths = set(links(main, '/monsters/')); field = set()
for section in main.select('section'):
    h=section.find('h2')
    if h and not h.get_text().startswith('Hero and legend'): field.update(links(section,'/monsters/'))
for d in dungeons: monster_paths.update(d.pop('_bossPaths'))
def monster(path):
    soup, body, digest = page(path)
    if client not in soup.get_text(' ',strip=True): raise ValueError('Mixed source versions: '+path)
    key=int(re.search(r'/monsters/(\d+)',path).group(1)); variants=[]
    table=body.select_one('#variants table')
    if table:
        for row in table.select('tbody tr'):
            cells=row.find_all('td',recursive=False)
            code=cells[0].find('code') if cells else None
            if not code or len(cells)<4: raise ValueError('Unexpected variant row: '+path)
            npc=int(re.search(r'NPC\s*(\d+)',code.get_text(' ',strip=True)).group(1))
            label=cells[0].find('span')
            variants.append({'NpcId':npc,'Context':label.get_text(' ',strip=True) if label else '',
                'Level':cells[1].get_text(' ',strip=True),'Type':cells[2].get_text(' ',strip=True),'Location':cells[3].get_text(' ',strip=True)})
    if not variants: variants=[{'NpcId':key,'Context':'','Level':'','Type':'','Location':''}]
    if key not in [v['NpcId'] for v in variants]: raise ValueError('Canonical ID missing from variants: '+path)
    return {'Key':key,'Name':body.h1.get_text(' ',strip=True),'FieldBoss':path in field,
            'InstanceKeys':sorted(d['Key'] for d in dungeons if key in d['BossKeys']), 'Variants':variants,
            'Source':BASE+path,'SourceSha256':digest}
bosses = parallel(sorted(monster_paths), monster)
if len(dungeons)<200 or len(bosses)<521: raise ValueError('Catalogue unexpectedly incomplete')
# Conflicting aliases stay explicit: consumers must not silently select one boss.
result={'Version':1,'ClientVersion':client,'Region':'Global','RetrievedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'Source':BASE,'Verification':'Source catalogue; NPC IDs not all verified against captures. Instance links are family-level candidates, not proof of the active instance. Boss ordering and difficulty/phase mappings are not inferred.',
    'Dungeons':sorted(dungeons,key=lambda d:d['Key']),'Bosses':sorted(bosses,key=lambda b:b['Key'])}
a.output.parent.mkdir(parents=True,exist_ok=True)
tmp=a.output.with_suffix('.json.tmp'); tmp.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');tmp.replace(a.output)
print(json.dumps({'Dungeons':len(dungeons),'BossFamilies':len(bosses),'UniqueNpcIds':len({v['NpcId'] for b in bosses for v in b['Variants']})}),flush=True)
