"""Reproducible local-only package/content/offline-consumer audit. No publish command."""
import base64
import zlib
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import traceback
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ('net8.0', 'net10.0')
TARGET = os.getenv('AUDIT_TFM', 'net8.0')
if TARGET not in TARGETS:
    raise ValueError('Unsupported audit target framework')
OUT = ROOT / 'artifacts' / 'audit' / TARGET
FEED = OUT / 'packages'

def run(*args):
    result = subprocess.run(args, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout, flush=True)
    if result.returncode:
        message = result.stdout[-12000:].replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print('::error title=M18 command failure::' + message, flush=True)
        raise subprocess.CalledProcessError(result.returncode, args)

def main():
    FEED.mkdir(parents=True, exist_ok=True)
    run('dotnet', 'run', '--project', 'tools/MetadataSharp.Audit', '-c', 'Release', '--framework', TARGET, '--', str(OUT))
    baseline = json.loads((OUT / 'baseline.json').read_text())
    assert baseline['targetFramework'] == f'.NETCoreApp,Version=v{TARGET[3:]}', baseline['targetFramework']
    assert baseline['runtime'].startswith(f'.NET {TARGET[3:]}.'), baseline['runtime']
    surfaces = {}
    for tfm in TARGETS:
        path = OUT / f'api-{tfm}.json'
        run('dotnet', 'run', '--project', 'tools/MetadataSharp.Audit', '-c', 'Release', '--framework', tfm, '--', '--api', str(path))
        surfaces[tfm] = json.loads(path.read_text())
        assert surfaces[tfm]['runtimeMajor'] == int(tfm[3:].split('.')[0])
    assert surfaces['net8.0']['signatures'] == surfaces['net10.0']['signatures'], 'Cross-target public signatures differ'
    signature_hash = hashlib.sha256(json.dumps(surfaces['net8.0']['signatures'], separators=(',', ':')).encode()).hexdigest()
    reports = []
    for name in ('MetadataSharp.Core', 'MetadataSharp.Formats'):
        run('dotnet', 'pack', f'src/{name}/{name}.csproj', '-c', 'Release', '-p:MetadataSharpAuditPackage=true', '-o', str(FEED))
        package = FEED / f'{name}.0.0.0-audit.nupkg'
        with zipfile.ZipFile(package) as z:
            names = sorted(z.namelist())
            required = {'LICENSE', 'README.md'} | {f'lib/{tfm}/{name}.{ext}' for tfm in TARGETS for ext in ('dll', 'xml')}
            assert required <= set(names), names
            assert z.read('LICENSE') == (ROOT / 'LICENSE').read_bytes()
            assert z.read('README.md') == (ROOT / 'README.md').read_bytes()
            dlls = [n for n in names if n.endswith('.dll')]
            assert dlls == sorted(f'lib/{tfm}/{name}.dll' for tfm in TARGETS), dlls
            assert not any(n.endswith(('.exe', '.cs', '.pdf', '.tif', '.jpg', '.png', '.webp')) for n in names), names
            ns = ET.fromstring(z.read(f'{name}.nuspec'))
            local = lambda tag: [e for e in ns.iter() if e.tag.split('}')[-1] == tag]
            license_node = local('license')[0]
            assert license_node.text == 'MIT' and license_node.get('type') == 'expression'
            groups = {}
            for group in local('group'):
                declared = group.get('targetFramework')
                tfm = next((t for t in TARGETS if declared in (t, '.NETCoreApp' + t[3:], '.NETCoreApp,Version=v' + t[3:])), None)
                assert tfm is not None and tfm not in groups, declared
                dependencies = [(e.get('id'), e.get('version')) for e in group if e.tag.split('}')[-1] == 'dependency']
                assert dependencies == ([] if name.endswith('Core') else [('MetadataSharp.Core', '0.0.0-audit')]), dependencies
                groups[tfm] = dependencies
            assert set(groups) == set(TARGETS), groups
            docs = {tfm: len(ET.fromstring(z.read(f'lib/{tfm}/{name}.xml')).findall('./members/member')) for tfm in TARGETS}
            reports.append(dict(package=package.name, sha256=hashlib.sha256(package.read_bytes()).hexdigest(), entries=names,
                                dependencyGroups=groups, documentedMembers=docs))
    consumer = OUT / 'consumer'; consumer.mkdir(exist_ok=True)
    (consumer / 'NuGet.Config').write_text(f'<configuration><packageSources><clear/><add key="audit" value="{FEED.as_posix()}"/></packageSources></configuration>')
    # A unique cache and cleared sources ensure the smoke cannot use project references or an internet feed.
    (consumer / 'consumer.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>AUDIT_TARGET</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="MetadataSharp.Formats" Version="[0.0.0-audit]"/></ItemGroup></Project>'''.replace('AUDIT_TARGET', TARGET))
    (consumer / 'Program.cs').write_text('''using MetadataSharp.Formats.Tiff; using MetadataSharp.Core;
using var doc = TiffMetadataDocument.Open(args[0]);
var edit = new TiffMetadataEdit(); edit.Set(new(doc.Layout.FirstIfdOffset, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "audit\\0"u8));
using var output = new MemoryStream(); doc.Save(output, edit); output.Position = 0;
using var reopened = TiffMetadataDocument.Open(output);
if (reopened.ReadValue(new(reopened.Layout.FirstIfdOffset, 305))?.Count != 6) throw new Exception("Package consumer mismatch.");
if (System.Environment.Version.Major != EXPECTED_MAJOR) throw new Exception("Consumer ran on wrong runtime.");
Console.WriteLine("Offline package consumer passed.");'''.replace('EXPECTED_MAJOR', TARGET[3:].split('.')[0]))
    run('dotnet', 'restore', str(consumer / 'consumer.csproj'), '--configfile', str(consumer / 'NuGet.Config'), '--packages', str(OUT / 'isolated-cache'))
    run('dotnet', 'run', '--project', str(consumer / 'consumer.csproj'), '-c', 'Release', '--no-restore', '--', str(OUT / 'consumer.tif'))
    report = dict(schema=2, targetFramework=TARGET, publicSignatureSha256=signature_hash, publicSignatureCount=len(surfaces['net8.0']['signatures']), sourceCommit=os.getenv('AUDIT_SOURCE_COMMIT'), sdkInfo=subprocess.check_output(['dotnet', '--info'], text=True), packages=reports, offlineConsumer='passed',
                  publication='not attempted', docCaveat='XML member counts are not full API documentation coverage; CS1591 suppressed only for audit candidates')
    (OUT / 'packages.json').write_text(json.dumps(report, indent=2))
    # Persist small machine-readable evidence in Actions logs as well as artifacts.
    for file in ('baseline.json', 'packages.json'):
        value = json.loads((OUT / file).read_text())
        encoded = base64.b64encode(zlib.compress(json.dumps(value, separators=(',', ':')).encode())).decode()
        chunks = [encoded[i:i+2500] for i in range(0, len(encoded), 2500)]
        for i, chunk in enumerate(chunks):
            print(f'::notice title=M18Z {file} {i}/{len(chunks)}::' + chunk, flush=True)

if __name__ == '__main__':
    try:
        main()
    except Exception:
        message = traceback.format_exc()[-12000:].replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print('::error title=M18 audit failure::' + message, flush=True)
        raise
