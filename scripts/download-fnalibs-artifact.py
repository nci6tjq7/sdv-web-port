#!/usr/bin/env python3
"""Download the fnalibs-single artifact from the latest successful build-fnalibs run.

Usage: download-fnalibs-artifact.py <output_dir> <github_token>

Falls back to r58Playz pre-built fnalibs if no successful build-fnalibs run exists.
"""
import json
import os
import subprocess
import sys
import urllib.request
import urllib.error
import zipfile


def api_get(url, token):
    req = urllib.request.Request(url, headers={
        'Authorization': f'token {token}',
        'Accept': 'application/vnd.github+json',
    })
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read())


def api_get_redirect(url, token):
    """Get the redirect URL without following it (artifact downloads need this)."""
    req = urllib.request.Request(url, headers={
        'Authorization': f'token {token}',
        'Accept': 'application/vnd.github+json',
    })
    # Don't follow redirects
    opener = urllib.request.build_opener(urllib.request.HTTPRedirectHandler())
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *a, **k):
            return None
    opener = urllib.request.build_opener(NoRedirect)
    try:
        opener.open(req)
    except urllib.error.HTTPError as e:
        return e.headers.get('Location')
    return None


def main():
    if len(sys.argv) < 3:
        print('Usage: download-fnalibs-artifact.py <output_dir> <github_token>')
        sys.exit(1)

    output_dir = sys.argv[1]
    token = sys.argv[2]
    repo = 'nci6tjq7/sdv-web-port'

    os.makedirs(output_dir, exist_ok=True)

    # Find the latest successful build-fnalibs run
    try:
        url = f'https://api.github.com/repos/{repo}/actions/workflows/build-fnalibs.yml/runs?status=completed&conclusion=success&per_page=1'
        data = api_get(url, token)
        runs = data.get('workflow_runs', [])
        if not runs:
            raise RuntimeError('No successful build-fnalibs runs found')
        run_id = runs[0]['id']
        print(f'Found build-fnalibs run ID: {run_id}')
    except Exception as e:
        print(f'Error finding build-fnalibs run: {e}')
        print('Falling back to r58Playz pre-built fnalibs...')
        release = '37a2ca3d-6f0d-4d76-91f7-d23f0ca7121b'
        base = f'https://github.com/r58Playz/FNA-WASM-Build/releases/download/{release}'
        for lib in ['SDL3.a', 'FNA3D.a', 'FAudio.a', 'libmojoshader.a']:
            subprocess.run(['curl', '-sL', f'{base}/{lib}', '-o', os.path.join(output_dir, lib)], check=True)
        return

    # Find the fnalibs-single artifact in that run
    try:
        url = f'https://api.github.com/repos/{repo}/actions/runs/{run_id}/artifacts'
        data = api_get(url, token)
        artifact_id = None
        for a in data.get('artifacts', []):
            if a['name'] == 'fnalibs-single':
                artifact_id = a['id']
                break
        if not artifact_id:
            raise RuntimeError('fnalibs-single artifact not found in run')
        print(f'Artifact ID: {artifact_id}')
    except Exception as e:
        print(f'Error finding artifact: {e}')
        sys.exit(1)

    # Download the artifact (follows redirect to blob storage)
    redirect_url = api_get_redirect(
        f'https://api.github.com/repos/{repo}/actions/artifacts/{artifact_id}/zip',
        token,
    )
    if not redirect_url:
        print('ERROR: No redirect URL for artifact download')
        sys.exit(1)

    zip_path = '/tmp/fnalibs-single.zip'
    subprocess.run(['curl', '-sL', redirect_url, '-o', zip_path], check=True)

    # Extract to output_dir
    with zipfile.ZipFile(zip_path, 'r') as z:
        z.extractall(output_dir)

    # libmojoshader.a is NOT created — it's merged into FNA3D.a by build-fnalibs.
    # The csproj does NOT reference libmojoshader.a (only SDL3.a, FNA3D.a, FAudio.a).
    # Including an empty .a file causes 'wasm-ld: unknown file type' error.

    print(f'fnalibs downloaded to {output_dir}')


if __name__ == '__main__':
    main()
