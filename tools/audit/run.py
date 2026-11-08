"""Reproducible local-only package/content/offline-consumer audit. No publish command."""
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
OUT = ROOT / 'artifacts' / 'audit'
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
    run('dotnet', 'run', '--project', 'tools/MetadataSharp.Audit', '-c', 'Release', '--', str(OUT))
    reports = []
    for name in ('MetadataSharp.Core', 'MetadataSharp.Formats'):
        run('dotnet', 'pack', f'src/{name}/{name}.csproj', '-c', 'Release', '-p:MetadataSharpAuditPackage=true', '-o', str(FEED))
        package = FEED / f'{name}.0.0.0-audit.nupkg'
        with zipfile.ZipFile(package) as z:
            names = sorted(z.namelist())
            required = {'LICENSE', 'README.md', f'lib/net8.0/{name}.dll', f'lib/net8.0/{name}.xml'}
            assert required <= set(names), names
            assert z.read('LICENSE') == (ROOT / 'LICENSE').read_bytes()
            assert z.read('README.md') == (ROOT / 'README.md').read_bytes()
            dlls = [n for n in names if n.endswith('.dll')]
            assert dlls == [f'lib/net8.0/{name}.dll'], dlls
            assert not any(n.endswith(('.exe', '.cs', '.pdf', '.tif', '.jpg', '.png', '.webp')) for n in names), names
            ns = ET.fromstring(z.read(f'{name}.nuspec'))
            local = lambda tag: [e for e in ns.iter() if e.tag.split('}')[-1] == tag]
            license_node = local('license')[0]
            assert license_node.text == 'MIT' and license_node.get('type') == 'expression'
            dependencies = [(e.get('id'), e.get('version')) for e in local('dependency')]
            assert dependencies == ([] if name.endswith('Core') else [('MetadataSharp.Core', '0.0.0-audit')]), dependencies
            xml = ET.fromstring(z.read(f'lib/net8.0/{name}.xml'))
            reports.append(dict(package=package.name, sha256=hashlib.sha256(package.read_bytes()).hexdigest(), entries=names,
                                dependencies=dependencies, documentedMembers=len(xml.findall('./members/member'))))
    consumer = OUT / 'consumer'; consumer.mkdir(exist_ok=True)
    (consumer / 'NuGet.Config').write_text(f'<configuration><packageSources><clear/><add key="audit" value="{FEED.as_posix()}"/></packageSources></configuration>')
    # A unique cache and cleared sources ensure the smoke cannot use project references or an internet feed.
    (consumer / 'consumer.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="MetadataSharp.Formats" Version="[0.0.0-audit]"/></ItemGroup></Project>''')
    (consumer / 'Program.cs').write_text('''using MetadataSharp.Formats.Tiff; using MetadataSharp.Core;
using var doc = TiffMetadataDocument.Open(args[0]);
var edit = new TiffMetadataEdit(); edit.Set(new(doc.Layout.FirstIfdOffset, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "audit\\0"u8));
using var output = new MemoryStream(); doc.Save(output, edit); output.Position = 0;
using var reopened = TiffMetadataDocument.Open(output);
if (reopened.ReadValue(new(reopened.Layout.FirstIfdOffset, 305))?.Count != 6) throw new Exception("Package consumer mismatch.");
Console.WriteLine("Offline package consumer passed.");''')
    run('dotnet', 'restore', str(consumer / 'consumer.csproj'), '--configfile', str(consumer / 'NuGet.Config'), '--packages', str(OUT / 'isolated-cache'))
    run('dotnet', 'run', '--project', str(consumer / 'consumer.csproj'), '-c', 'Release', '--no-restore', '--', str(OUT / 'consumer.tif'))
    report = dict(schema=1, sourceCommit=os.getenv('AUDIT_SOURCE_COMMIT'), sdkInfo=subprocess.check_output(['dotnet', '--info'], text=True), packages=reports, offlineConsumer='passed',
                  publication='not attempted', docCaveat='XML member counts are not full API documentation coverage; CS1591 suppressed only for audit candidates')
    (OUT / 'packages.json').write_text(json.dumps(report, indent=2))
    # Persist small machine-readable evidence in Actions logs as well as artifacts.
    for file in ('baseline.json', 'packages.json'):
        value = json.loads((OUT / file).read_text())
        print('::notice title=M18 ' + file + '::' + json.dumps(value, separators=(',', ':')).replace('%', '%25'), flush=True)

if __name__ == '__main__':
    try:
        main()
    except Exception:
        message = traceback.format_exc()[-12000:].replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print('::error title=M18 audit failure::' + message, flush=True)
        raise
