#!/usr/bin/env python3
"""Build the reviewed MVC preview and publish a copied, package-only Stockroom."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

PORT = Path(__file__).resolve().parent
OUT = PORT / 'artifacts' / 'demo'


def main():
    sdk = os.environ.get('DOTNET10_SDK')
    runtime = os.environ.get('DOTNET10_RUNTIME')
    if not sdk or not runtime:
        raise RuntimeError('Set DOTNET10_SDK and DOTNET10_RUNTIME to installed dotnet hosts; see port/README.md.')
    sdk, runtime = str(Path(sdk).expanduser().resolve()), str(Path(runtime).expanduser().resolve())
    if not Path(sdk).is_file() or not Path(runtime).is_file():
        raise RuntimeError('The selected SDK/runtime host does not exist.')
    if OUT.exists():
        raise RuntimeError('port/artifacts/demo already exists. Move it aside before a fresh build; this helper never deletes it.')
    OUT.mkdir(parents=True)
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_CLI_HOME=str(OUT / 'sdk-home'), NUGET_PACKAGES=str(OUT / 'packages'),
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')

    def run(args, cwd=PORT, capture=False):
        return subprocess.run(args, cwd=cwd, env=env, check=True, text=True,
                              stdout=subprocess.PIPE if capture else None).stdout

    version = run([sdk, '--version'], capture=True).strip()
    if version != '10.0.101':
        raise RuntimeError('SDK10.0.101 is required by global.json; resolved ' + version)
    runtimes = run([runtime, '--list-runtimes'], capture=True)
    for name in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'):
        versions = re.findall(r'^' + re.escape(name) + r' (10\.0\.[0-9]+) ', runtimes, re.MULTILINE)
        if not any(int(v.split('.')[2]) >= 2 for v in versions):
            raise RuntimeError(name + '10.0.2+ is required on DOTNET10_RUNTIME.')

    feed = OUT / 'feed'
    feed.mkdir()
    configuration = ET.Element('configuration')
    sources = ET.SubElement(configuration, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local-preview', value=str(feed))
    # An existing offline feed can replace NuGet.org; SDK/runtime installation remains external.
    dependency_source = env.get('MVC_PREVIEW_NUGET_SOURCE', 'https://api.nuget.org/v3/index.json')
    ET.SubElement(sources, 'add', key='dependencies', value=dependency_source)
    config = OUT / 'NuGet.Config'
    ET.ElementTree(configuration).write(config, encoding='unicode')
    common = ['--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
              '-p:NativeRazorDotNetHost=' + runtime]
    for project in ('Packaging/Runtime/Mvc5.Native.Experimental.csproj',
                    'Packaging/Razor.Build/Mvc5.Native.Experimental.Razor.Build.csproj'):
        run([sdk, 'restore', project, '--configfile', str(config), '--disable-parallel', '-p:NuGetAudit=false'])
        run([sdk, 'pack', project, '-c', 'Release', '-o', str(feed), *common])

    source = OUT / 'source'
    shutil.copytree(PORT / 'Stockroom', source, ignore=shutil.ignore_patterns('bin', 'obj'))
    shutil.copyfile(config, source / 'NuGet.Config')
    run([sdk, 'restore', 'Stockroom.csproj', '--configfile', 'NuGet.Config', '--disable-parallel', '-p:NuGetAudit=false'], source)
    run([sdk, 'build', 'Stockroom.csproj', '-c', 'Debug', *common], source)
    run([sdk, 'publish', 'Stockroom.csproj', '-c', 'Release', '--no-self-contained', '-o', str(OUT / 'published'), *common], source)
    if list((OUT / 'published').glob('*RazorCompiler*')):
        raise RuntimeError('The private Razor compiler must not be published.')
    result = {'status': 'built', 'sdk': version, 'package_version': '0.1.0-u60',
              'consumer': 'Copied source using package references only',
              'published_directory': 'port/artifacts/demo/published',
              'runtime_selection': 'Core and ASP.NET Core10.0.2+; LatestPatch',
              'next': 'Supply a disposable account and run Stockroom.dll as described in port/Stockroom/README.md.'}
    (OUT / 'build-result.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, subprocess.CalledProcessError) as error:
        print('Preview build failed: ' + str(error), file=sys.stderr)
        sys.exit(1)
