#!/usr/bin/env bash
# Updates FaustBot in this directory to the latest GitHub release and restarts the service.
# config.json and state.json are never touched. If the new version doesn't stay running, the previous
# one is restored automatically.
#
# Usage:
#   ./update.sh                    Install the latest release
#   ./update.sh --version v3.0.1   Install a specific release (also works for pre-releases)
#   ./update.sh --force            Reinstall even if already up to date
#   ./update.sh --rollback         Swap back to the previously installed version
#
# Run as the bot's user, not root; sudo is only used to restart the service.
set -euo pipefail

REPO="${FAUSTBOT_REPO:-Llamaware/FaustBot}"
SERVICE_NAME="${SERVICE_NAME:-faustbot}"
ASSET="faustbot-linux-x64.tar.gz"
HEALTH_CHECK_SECONDS=15
INSTALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Files shipped in a release. config.json and state.json are deliberately not in this list.
RELEASE_FILES=(FaustBot VERSION install.sh update.sh faustbot.service.template config.example.json)

die() { echo "Error: $*" >&2; exit 1; }

# Releases ship a VERSION file; only the legacy (pre-3.0) bot is missing one.
installed_version() { cat "${INSTALL_DIR}/VERSION" 2>/dev/null || echo "pre-3.0"; }

# Restarts the service and checks it is still running after a short wait, which catches a rejected
# token or a config error that only shows up once the bot connects.
restart_and_check() {
    echo "Restarting ${SERVICE_NAME}..."
    sudo systemctl restart "${SERVICE_NAME}" || return 1
    echo "Waiting ${HEALTH_CHECK_SECONDS}s to make sure it stays up..."
    sleep "${HEALTH_CHECK_SECONDS}"
    systemctl is-active --quiet "${SERVICE_NAME}"
}

show_logs() {
    echo "Recent logs:" >&2
    sudo journalctl -u "${SERVICE_NAME}" -n 30 --no-pager >&2 || true
}

# Swaps FaustBot/VERSION with FaustBot.old/VERSION.old, so running it twice switches back again.
swap_with_previous() {
    [[ -f "${INSTALL_DIR}/FaustBot.old" ]] || die "no previous version to roll back to"
    cd "${INSTALL_DIR}"
    mv -f FaustBot FaustBot.swap
    mv -f FaustBot.old FaustBot
    mv -f FaustBot.swap FaustBot.old

    # A legacy install has no VERSION file, so either side of the swap may be missing.
    if [[ -f VERSION ]]; then
        mv -f VERSION VERSION.swap
    fi
    if [[ -f VERSION.old ]]; then
        mv -f VERSION.old VERSION
    fi
    if [[ -f VERSION.swap ]]; then
        mv -f VERSION.swap VERSION.old
    fi
}

rollback() {
    swap_with_previous
    echo "Rolled back to version $(installed_version)."
    restart_and_check || { show_logs; die "the previous version didn't stay running either"; }
    echo "FaustBot is running."
}

[[ $EUID -ne 0 ]] || die "run as the bot's user, not root (sudo is only used to restart the service)"

target_tag=""
force=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --rollback) rollback; exit 0 ;;
        --version) [[ $# -ge 2 ]] || die "--version needs a tag, e.g. --version v3.0.1"; target_tag="$2"; shift 2 ;;
        --force) force=true; shift ;;
        -h|--help) sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) die "unknown option: $1 (see --help)" ;;
    esac
done

if [[ -z "${target_tag}" ]]; then
    release_json="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest")" \
        || die "couldn't fetch the latest release from GitHub"
    target_tag="$(sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' <<<"${release_json}" | head -n 1)"
    [[ -n "${target_tag}" ]] || die "couldn't find the latest release tag"
fi

current="$(installed_version)"
if [[ "${current}" == "${target_tag#v}" && "${force}" == false ]]; then
    echo "Already up to date (${current})."
    exit 0
fi
echo "Updating FaustBot: ${current} -> ${target_tag#v}"

work_dir="$(mktemp -d)"
trap 'rm -rf "${work_dir}"' EXIT
base_url="https://github.com/${REPO}/releases/download/${target_tag}"

curl -fL --retry 3 --progress-bar -o "${work_dir}/${ASSET}" "${base_url}/${ASSET}" \
    || die "couldn't download ${ASSET} for ${target_tag}"
curl -fsSL --retry 3 -o "${work_dir}/${ASSET}.sha256" "${base_url}/${ASSET}.sha256" \
    || die "couldn't download the checksum for ${target_tag}"
(cd "${work_dir}" && sha256sum --check --quiet "${ASSET}.sha256") || die "checksum mismatch, not installing"

mkdir "${work_dir}/release"
tar -xzf "${work_dir}/${ASSET}" -C "${work_dir}/release"
[[ -f "${work_dir}/release/FaustBot" ]] || die "the release archive doesn't contain FaustBot"

if [[ -f "${INSTALL_DIR}/faustbot.service.template" ]] \
    && ! cmp -s "${INSTALL_DIR}/faustbot.service.template" "${work_dir}/release/faustbot.service.template"; then
    unit_changed=true
else
    unit_changed=false
fi

# Set the running version aside. It only becomes the --rollback target once the new version proves
# healthy, so a failed update never leaves the broken release as "previous".
# mv replaces files without disturbing the running process.
cd "${INSTALL_DIR}"
rm -f FaustBot.pending-old VERSION.pending-old
if [[ -f FaustBot ]]; then
    cp -p FaustBot FaustBot.pending-old
fi
if [[ -f VERSION ]]; then
    cp -p VERSION VERSION.pending-old
fi
for file in "${RELEASE_FILES[@]}"; do
    if [[ -f "${work_dir}/release/${file}" ]]; then
        mv -f "${work_dir}/release/${file}" "${INSTALL_DIR}/${file}"
    fi
done
chmod +x FaustBot install.sh update.sh

if restart_and_check; then
    if [[ -f FaustBot.pending-old ]]; then
        mv -f FaustBot.pending-old FaustBot.old
        if [[ -f VERSION.pending-old ]]; then
            mv -f VERSION.pending-old VERSION.old
        else
            rm -f VERSION.old
        fi
    fi
    echo "FaustBot ${target_tag#v} is running. Undo with: ./update.sh --rollback"
    if [[ "${unit_changed}" == true ]]; then
        echo "Note: the service definition changed in this release. Apply it with: sudo ./install.sh"
    fi
else
    echo "The new version didn't stay running." >&2
    show_logs
    [[ -f FaustBot.pending-old ]] || die "there was no previous version to restore"
    echo "Restoring version ${current}..." >&2
    mv -f FaustBot.pending-old FaustBot
    if [[ -f VERSION.pending-old ]]; then
        mv -f VERSION.pending-old VERSION
    else
        rm -f VERSION
    fi
    restart_and_check || { show_logs; die "version ${current} didn't stay running either"; }
    echo "Version ${current} is running again." >&2
    exit 1
fi
