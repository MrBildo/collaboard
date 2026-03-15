#!/usr/bin/env bash
set -euo pipefail

REPO="MrBildo/collaboard"
INSTALL_DIR="${HOME}/.collaboard"

# Detect platform and architecture
detect_platform() {
    local os arch
    os="$(uname -s)"
    arch="$(uname -m)"

    case "$os" in
        Linux)  os="linux" ;;
        Darwin) os="osx" ;;
        *)      echo "Unsupported OS: $os" >&2; exit 1 ;;
    esac

    case "$arch" in
        x86_64|amd64)  arch="x64" ;;
        arm64|aarch64) arch="arm64" ;;
        *)             echo "Unsupported architecture: $arch" >&2; exit 1 ;;
    esac

    echo "${os}-${arch}"
}

PLATFORM="$(detect_platform)"
ARTIFACT_NAME="collaboard-${PLATFORM}"

echo "Detected platform: ${PLATFORM}"
echo "Install directory: ${INSTALL_DIR}"
echo

# Get latest release tag from GitHub API
echo "Fetching latest release..."
RELEASE_TAG=$(curl -sSf "https://api.github.com/repos/${REPO}/releases/latest" | grep '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$RELEASE_TAG" ]; then
    echo "Failed to fetch latest release." >&2
    exit 1
fi

echo "Latest release: ${RELEASE_TAG}"

# Download artifact
DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${RELEASE_TAG}/${ARTIFACT_NAME}.tar.gz"
TEMP_DIR="$(mktemp -d)"

echo "Downloading ${ARTIFACT_NAME}.tar.gz..."
curl -sSfL "$DOWNLOAD_URL" -o "${TEMP_DIR}/${ARTIFACT_NAME}.tar.gz"

# Extract
echo "Extracting to ${INSTALL_DIR}..."
mkdir -p "$INSTALL_DIR"
tar xzf "${TEMP_DIR}/${ARTIFACT_NAME}.tar.gz" -C "$INSTALL_DIR" --strip-components=1

# Clean up
rm -rf "$TEMP_DIR"

# Make executable
chmod +x "${INSTALL_DIR}/Collaboard.Api"

echo
echo "Collaboard installed to ${INSTALL_DIR}"
echo

# Suggest adding to PATH
SHELL_NAME="$(basename "$SHELL")"
case "$SHELL_NAME" in
    zsh)  RC_FILE="$HOME/.zshrc" ;;
    bash) RC_FILE="$HOME/.bashrc" ;;
    *)    RC_FILE="$HOME/.profile" ;;
esac

if [[ ":$PATH:" != *":${INSTALL_DIR}:"* ]]; then
    echo "To add Collaboard to your PATH, run:"
    echo "  echo 'export PATH=\"${INSTALL_DIR}:\$PATH\"' >> ${RC_FILE}"
    echo "  source ${RC_FILE}"
    echo
fi

echo "To start Collaboard:"
echo "  ${INSTALL_DIR}/Collaboard.Api"
echo
echo "Then open http://localhost:8080 in your browser."
