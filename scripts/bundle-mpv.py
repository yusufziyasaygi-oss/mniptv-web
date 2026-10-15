#!/usr/bin/env python3
"""Copy the actual Mach-O dependency closure; fail on unresolved dependencies.
Runs on the build Mac, never changes the Homebrew installation's libraries.
"""
import hashlib, json, os, re, shutil, subprocess, sys
from pathlib import Path

def output(*args):
    return subprocess.check_output(args, text=True)

def dependencies(path):
    return re.findall(r'^\s+(.+?) \(compatibility version', output('otool', '-L', str(path)), re.M)

def rpaths(path):
    return re.findall(r'cmd LC_RPATH\n.*?\n\s*path (.*?) \(offset', output('otool', '-l', str(path)), re.S)

def system(name):
    return name.startswith(('/System/Library/', '/usr/lib/'))

def deployment(path):
    text = output('otool', '-l', str(path))
    versions = re.findall(r'\bminos (\d+(?:\.\d+)+)', text)
    versions += re.findall(r'cmd LC_VERSION_MIN_MACOSX\n.*?\n\s*version (\d+(?:\.\d+)+)', text, re.S)
    return max((tuple(map(int, v.split('.'))) for v in versions), default=(10, 15))

def bundle(app, prefix, arch):
    executable = app / 'Contents/MacOS/MaviNoktaIPTV'
    destination = app / 'Contents/Frameworks'; destination.mkdir(parents=True, exist_ok=True)
    notices = app / 'Contents/Resources/ThirdParty'; notices.mkdir(parents=True, exist_ok=True)
    seen, by_name, manifest = {}, {}, []
    minimum = (11, 0) if arch == 'arm64' else (10, 15)
    def expand(value, owner):
        return value.replace('@loader_path', str(owner.parent)).replace('@executable_path', str(executable.parent))
    def resolve(name, owner):
        if name.startswith('@rpath/'):
            tail = name[len('@rpath/'):]
            candidates = [Path(expand(r, owner))/tail for r in rpaths(owner)]
            candidates += [owner.parent/tail, prefix/'lib'/tail]
        else:
            candidates = [Path(expand(name, owner))]
        for path in candidates:
            if path.is_file(): return path.resolve()
        raise RuntimeError('Unresolved native dependency: ' + Path(name).name)
    def copy_library(source):
        nonlocal minimum
        source = source.resolve()
        if source in seen: return seen[source]
        name = source.name
        if name in by_name and by_name[name] != source:
            raise RuntimeError('Conflicting native library basename: ' + name)
        target = destination/name; seen[source] = target; by_name[name] = source
        subprocess.run(['lipo', str(source), '-verify_arch', arch], check=True)
        minimum = max(minimum, deployment(source))
        shutil.copy2(source, target); target.chmod(0o755)
        rewrite(source, target)
        subprocess.run(['install_name_tool', '-id', '@rpath/'+name, str(target)], check=True)
        for old in set(rpaths(target)):
            subprocess.run(['install_name_tool', '-delete_rpath', old, str(target)], check=True)
        formula = next((p for p in source.parents if (p/'INSTALL_RECEIPT.json').is_file()), None)
        license_files = []
        if formula:
            candidates = [p for p in formula.iterdir() if p.is_file() and p.name.upper().startswith(('LICENSE','COPYING','COPYRIGHT'))]
            license_dir = formula/'share/licenses'
            if license_dir.is_dir(): candidates += [p for p in license_dir.rglob('*') if p.is_file()]
            for i, item in enumerate(candidates):
                dest = notices/(name+'-'+str(i)+'-'+item.name)
                shutil.copy2(item, dest); license_files.append(dest.name)
        manifest.append({'library': name, 'sha256_original': hashlib.sha256(source.read_bytes()).hexdigest(),
                         'formula': formula.parent.name if formula else None,
                         'version': formula.name if formula else None, 'notices': license_files})
        return target
    def rewrite(source, target):
        for name in dependencies(source):
            if system(name): continue
            resolved = resolve(name, source)
            # A dylib's install name appears in otool -L; fix it with -id below.
            if source != executable and resolved == source.resolve(): continue
            embedded = copy_library(resolved)
            subprocess.run(['install_name_tool', '-change', name, '@rpath/'+embedded.name, str(target)], check=True)
    rewrite(executable, executable)
    # No external Homebrew dependency may remain in the finished binary closure.
    for binary in [executable, *destination.glob('*.dylib')]:
        for name in dependencies(binary):
            if system(name): continue
            if not name.startswith('@rpath/') or not (destination/name[len('@rpath/'):]).is_file():
                raise RuntimeError('Non-bundled dependency remains: ' + binary.name)
    (notices/'MANIFEST.json').write_text(json.dumps(manifest, indent=2)+'\n')
    (app/'Contents/Resources/MinimumMPVSystem.txt').write_text('.'.join(map(str, minimum)))
    print('Bundled', len(seen), 'libraries; minimum macOS', '.'.join(map(str, minimum)))

if __name__ == '__main__':
    if len(sys.argv) != 4: raise SystemExit('Usage: bundle-mpv.py APP MPV_PREFIX ARCH')
    bundle(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve(), sys.argv[3])
