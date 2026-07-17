#!/bin/bash
# Upload SDV Content/ files to GitHub release as content.zip
#
# Usage:
#   1. Place 星露谷物语.zip (or the game's Content/ directory) in /home/z/my-project/upload/
#   2. Run: bash scripts/upload-content-release.sh
#
# This script:
#   - Finds the zip file or Content/ directory
#   - Repackages Content/ as content.zip
#   - Uploads to GitHub release "sdv-content-v1"
#   - The CI workflow will download and deploy to wwwroot/deps/content/

set -e

REPO="nci6tjq7/sdv-web-port"
UPLOAD_DIR="/home/z/my-project/upload"
RELEASE_TAG="sdv-content-v1"
CONTENT_ZIP="/tmp/content.zip"

# Get GitHub token
GH_TOKEN=$(cd /home/z/my-project && git config --get remote.origin.url | sed 's|https://x-access-token:||;s|@.*||')
if [ -z "$GH_TOKEN" ]; then
    echo "ERROR: Could not get GitHub token from git remote"
    exit 1
fi

echo "=== Looking for game files in $UPLOAD_DIR ==="

# Look for 星露谷物语.zip or similar
GAME_ZIP=""
for f in "$UPLOAD_DIR"/*.zip "$UPLOAD_DIR"/星露谷物语*.zip "$UPLOAD_DIR"/Stardew*.zip; do
    if [ -f "$f" ]; then
        GAME_ZIP="$f"
        break
    fi
done

if [ -n "$GAME_ZIP" ]; then
    echo "Found game zip: $GAME_ZIP"
    echo "=== Extracting Content/ from game zip ==="
    rm -rf /tmp/sdv-content-extract
    mkdir -p /tmp/sdv-content-extract
    unzip -q "$GAME_ZIP" -d /tmp/sdv-content-extract

    # Find Content/ directory
    CONTENT_DIR=$(find /tmp/sdv-content-extract -type d -name "Content" | head -1)
    if [ -z "$CONTENT_DIR" ]; then
        echo "ERROR: No Content/ directory found in $GAME_ZIP"
        echo "Contents of zip:"
        find /tmp/sdv-content-extract -maxdepth 2 -type d | head -20
        exit 1
    fi
    echo "Found Content/ at: $CONTENT_DIR"
else
    # Check if Content/ directory exists directly
    CONTENT_DIR="$UPLOAD_DIR/Content"
    if [ ! -d "$CONTENT_DIR" ]; then
        echo "ERROR: No game zip found in $UPLOAD_DIR"
        echo "Expected: 星露谷物语.zip or Content/ directory"
        echo ""
        echo "Files in $UPLOAD_DIR:"
        ls -la "$UPLOAD_DIR"
        exit 1
    fi
    echo "Found Content/ directory: $CONTENT_DIR"
fi

echo "=== Content/ size ==="
du -sh "$CONTENT_DIR"
echo "=== File count ==="
find "$CONTENT_DIR" -type f | wc -l
echo "=== Sample files ==="
find "$CONTENT_DIR" -type f | head -10

echo ""
echo "=== Creating content.zip ( preserving Content/ prefix) ==="
cd "$(dirname "$CONTENT_DIR")"
rm -f "$CONTENT_ZIP"
# Create zip with Content/ as the top-level directory
zip -r -q "$CONTENT_ZIP" "$(basename "$CONTENT_DIR")"
ls -lh "$CONTENT_ZIP"

echo ""
echo "=== Uploading to GitHub release $RELEASE_TAG ==="

# Delete existing release if it exists
curl -s -X DELETE -H "Authorization: token $GH_TOKEN" \
    "https://api.github.com/repos/$REPO/releases/tags/$RELEASE_TAG" 2>/dev/null || true
curl -s -X DELETE -H "Authorization: token $GH_TOKEN" \
    "https://api.github.com/repos/$REPO/git/refs/tags/$RELEASE_TAG" 2>/dev/null || true

# Create new release
RELEASE_DATA=$(cat <<EOF
{
    "tag_name": "$RELEASE_TAG",
    "name": "SDV Content Files",
    "body": "Stardew Valley Content/ directory for WASM port",
    "draft": false,
    "prerelease": false
}
EOF
)
RELEASE_RESPONSE=$(curl -s -X POST -H "Authorization: token $GH_TOKEN" \
    -H "Accept: application/vnd.github+json" \
    "https://api.github.com/repos/$REPO/releases" \
    -d "$RELEASE_DATA")

RELEASE_ID=$(echo "$RELEASE_RESPONSE" | python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))")
if [ -z "$RELEASE_ID" ]; then
    echo "ERROR: Failed to create release"
    echo "$RELEASE_RESPONSE"
    exit 1
fi
echo "Created release ID: $RELEASE_ID"

# Upload content.zip as asset
UPLOAD_URL=$(echo "$RELEASE_RESPONSE" | python3 -c "import json,sys; print(json.load(sys.stdin)['upload_url'].replace('{?name,label}',''))")
echo "Uploading content.zip..."
curl -s -X POST -H "Authorization: token $GH_TOKEN" \
    -H "Content-Type: application/zip" \
    --data-binary @"$CONTENT_ZIP" \
    "${UPLOAD_URL}?name=content.zip" | python3 -c "import json,sys; r=json.load(sys.stdin); print(f'Uploaded: {r.get(\"name\",\"?\")} ({r.get(\"size\",0)/1024/1024:.1f} MB)')"

echo ""
echo "=== Done! ==="
echo "Content files uploaded to release: $RELEASE_TAG"
echo "The CI workflow will download content.zip and deploy to wwwroot/deps/content/"
