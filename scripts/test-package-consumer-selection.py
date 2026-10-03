#!/usr/bin/env python3
"""Verify actual candidate-package closure and full content protection.

Requires an already packed complete .tmp/local-feed. Run only when changing
consumer selection, not as a daily gate.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', type=Path, default=ROOT / '.tmp/local-feed')
    parser.add_argument('--output', type=Path, default=ROOT / '.tmp/consumer-selection-evidence')
    parser.add_argument('--benchmark', action='store_true', help='Three alternating full/scoped consumer pairs')
    args = parser.parse_args()
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=True)
    graph = {}; packages = {}
    for path in args.feed.glob('*.nupkg'):
        with zipfile.ZipFile(path) as archive:
            xml = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
        metadata = next(node for node in xml if node.tag.split('}')[-1] == 'metadata')
        name = next(node.text for node in metadata if node.tag.split('}')[-1] == 'id')
        graph[name] = {node.attrib['id'] for node in metadata.iter() if node.tag.split('}')[-1] == 'dependency' and node.attrib['id'].startswith('Leistd.')}
        packages[name] = path
    assert len(graph) > 1, 'complete candidate feed required'
    plan_file = out / 'plan.json'
    subprocess.run(['python3','scripts/plan-quality-checks.py','--tier','pr','--output',str(plan_file)],cwd=ROOT,check=True,stdout=subprocess.DEVNULL)
    baseline = json.loads(plan_file.read_text())
    records = []
    def run(label, plan, feed, success=True, diagnostic=None):
        plan_file.write_text(json.dumps(plan))
        command = ['pwsh','-NoProfile','-File','framework/build/test-package-consumption.ps1','-FeedPath',str(feed),'-ValidationPlanPath',str(plan_file)]
        started=time.monotonic(); result=subprocess.run(command,cwd=ROOT,text=True,capture_output=True)
        log=result.stdout+result.stderr; (out/(label+'.log')).write_text(log)
        records.append(dict(label=label,seconds=round(time.monotonic()-started,3),exit_code=result.returncode))
        (out/'results.json').write_text(json.dumps(records,indent=2))
        assert (result.returncode==0)==success,(label,log[-2500:])
        if diagnostic: assert diagnostic in log,(label,diagnostic,log[-2000:])
        print('PASS',label,flush=True)
        return log
    for seed in ['Leistd.Email.Smtp','Leistd.Email.Core']:
        expected={seed}
        while True:
            expanded=expected|{name for name,deps in graph.items() if deps&expected}
            if expanded==expected: break
            expected=expanded
        log=run(seed,dict(baseline,ConsumerProjects=[seed]),args.feed)
        assert f'selected {len(expected)} isolated consumers' in log
        assert f'Package consumption passed for {len(expected)} package(s)' in log
        (out/(seed+'-expected.json')).write_text(json.dumps(sorted(expected)))
    content_only=dict(baseline,Mode='frontend',FrameworkTests=False,ConsumerProjects=[])
    log=run('template-inputs-content-only',content_only,args.feed)
    assert 'Consumer build not applicable' in log and '> dotnet' not in log
    # Defects in an unselected package still block content-only and scoped modes.
    corrupt=out/'corrupt-feed'; corrupt.mkdir(exist_ok=True)
    for path in args.feed.glob('*.nupkg'): shutil.copy2(path,corrupt/path.name)
    victim=corrupt/packages['Leistd.Core'].name
    original=victim.read_bytes()
    with zipfile.ZipFile(victim) as archive:
        entries={name:archive.read(name) for name in archive.namelist()}
    def rewrite(data):
        with zipfile.ZipFile(victim,'w',zipfile.ZIP_DEFLATED) as archive:
            for name,content in data.items(): archive.writestr(name,content)
    rewrite({name:content for name,content in entries.items() if not (name.startswith('lib/') and name.endswith('.xml'))})
    run('unselected-xml-defect',content_only,corrupt,False,'missing XML documentation')
    victim.write_bytes(original)
    victim.unlink();run('missing-unselected-package',content_only,corrupt,False,'missing source packages');victim.write_bytes(original)
    data=dict(entries); nuspec=next(name for name in data if name.endswith('.nuspec'))
    text=data[nuspec].decode(); assert '<dependencies>' in text
    data[nuspec]=text.replace('<dependencies>','<dependencies><dependency id="Leistd.Missing.QualityFixture" version="[0.12.0]" />',1).encode()
    rewrite(data);run('missing-candidate-dependency',content_only,corrupt,False,'Candidate dependency missing');victim.write_bytes(original)
    run('unknown-consumer-seed',dict(baseline,ConsumerProjects=['Leistd.Unknown.QualityFixture']),args.feed,False,'Unknown consumer seed')
    run('full-cannot-narrow',dict(baseline,Tier='full',ConsumerProjects=['Leistd.Email.Smtp']),args.feed,False,'must retain all scenarios')
    run('restored-content-only',content_only,corrupt)
    if args.benchmark:
        for number in range(3):
            run(f'benchmark-full-{number+1}',baseline,args.feed)
            run(f'benchmark-scoped-{number+1}',dict(baseline,ConsumerProjects=['Leistd.Email.Core']),args.feed)


if __name__ == '__main__': main()
