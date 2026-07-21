import zipfile, glob, re
for f in sorted(glob.glob('/tmp/nuget/*.nupkg')):
    z = zipfile.ZipFile(f)
    names = z.namelist()
    natives = [n for n in names if 'runtimes/' in n]
    libs = [n for n in names if n.startswith('lib/')]
    print(f"=== {f.split('/')[-1]}: {len(names)} files")
    print("  lib TFMs:", sorted(set(n.split('/')[1] for n in libs)) if libs else 'none')
    print("  natives:", natives[:25])
    print("  license files:", [n for n in names if 'licen' in n.lower()])
    nuspec = [n for n in names if n.endswith('.nuspec')][0]
    txt = z.read(nuspec).decode('utf8', errors='replace')
    for m in re.finditer(r'<(license[^>]*|licenseUrl|projectUrl|repository[^>]*)>([^<]*)<', txt):
        print('  meta:', (m.group(0)[:200]).strip())
