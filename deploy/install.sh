#!/usr/bin/env bash
# Installs FaustBot as a systemd service that runs from this directory.
#
# Usage:  cd /path/to/faustbot && sudo ./install.sh
#
# The service runs as the user who invoked sudo. Safe to run again: the unit file is rewritten and the
# previous one is kept as <unit>.bak. Set SERVICE_NAME to use a name other than "faustbot".
set -euo pipefail

SERVICE_NAME="${SERVICE_NAME:-faustbot}"
INSTALL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
TEMPLATE="${INSTALL_DIR}/faustbot.service.template"

die() { echo "Error: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run with sudo: sudo ./install.sh"
[[ -n "${SUDO_USER:-}" && "${SUDO_USER}" != "root" ]] \
    || die "run with sudo from the account the bot should run as, not as root directly"
[[ -f "${INSTALL_DIR}/FaustBot" ]] || die "FaustBot binary not found in ${INSTALL_DIR}"
[[ -f "${TEMPLATE}" ]] || die "faustbot.service.template not found in ${INSTALL_DIR}"

RUN_AS="${SUDO_USER}"
chmod +x "${INSTALL_DIR}/FaustBot" "${INSTALL_DIR}/update.sh" 2>/dev/null || true

if [[ ! -f "${INSTALL_DIR}/config.json" ]]; then
    echo "Note: ${INSTALL_DIR}/config.json doesn't exist yet."
    echo "      Copy config.example.json to config.json and fill it in, then run: sudo systemctl restart ${SERVICE_NAME}"
fi

new_unit="$(mktemp)"
trap 'rm -f "${new_unit}"' EXIT
sed -e "s|@USER@|${RUN_AS}|g" -e "s|@DIR@|${INSTALL_DIR}|g" "${TEMPLATE}" > "${new_unit}"

if [[ -f "${UNIT_PATH}" ]]; then
    if cmp -s "${new_unit}" "${UNIT_PATH}"; then
        echo "${UNIT_PATH} is already up to date."
    else
        cp "${UNIT_PATH}" "${UNIT_PATH}.bak"
        echo "Updating ${UNIT_PATH} (previous version saved as ${UNIT_PATH}.bak):"
        diff -u "${UNIT_PATH}.bak" "${new_unit}" || true
    fi
else
    echo "Creating ${UNIT_PATH}."
fi

install -m 644 "${new_unit}" "${UNIT_PATH}"
systemctl daemon-reload
systemctl enable "${SERVICE_NAME}" >/dev/null

if [[ -f "${INSTALL_DIR}/config.json" ]]; then
    echo "Restarting ${SERVICE_NAME}..."
    if systemctl restart "${SERVICE_NAME}"; then
        echo "FaustBot is running. Logs: journalctl -u ${SERVICE_NAME} -f"
    else
        echo "FaustBot failed to start. Recent logs:" >&2
        journalctl -u "${SERVICE_NAME}" -n 30 --no-pager >&2
        exit 1
    fi
fi
