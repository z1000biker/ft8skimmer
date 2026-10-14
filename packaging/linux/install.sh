#!/bin/sh
# Installs RtlFt8Skimmer to /opt/rtlft8skimmer, the udev rule for the dongle,
# and (optionally) a systemd service that starts it at boot for the given user.
#   sudo ./install.sh            install files and udev rule
#   sudo ./install.sh --service  also enable the service for the user who ran sudo
set -e
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo."; exit 1; }
HERE="$(cd "$(dirname "$0")" && pwd)"

if ! command -v jt9 >/dev/null 2>&1; then
  echo "Note: jt9 not found. Install WSJT-X first, e.g.: sudo apt install wsjtx"
fi

rm -rf /opt/rtlft8skimmer
mkdir -p /opt/rtlft8skimmer
cp -a "$HERE"/. /opt/rtlft8skimmer/
chmod +x /opt/rtlft8skimmer/rtlft8skimmer
ln -sf /opt/rtlft8skimmer/rtlft8skimmer /usr/local/bin/rtlft8skimmer

cp "$HERE/99-rtlsdr-v4.rules" /etc/udev/rules.d/
udevadm control --reload-rules && udevadm trigger || true
echo "Installed. Unplug and replug the dongle once."

if [ "$1" = "--service" ]; then
  U="${SUDO_USER:-$(logname)}"
  cp "$HERE/rtlft8skimmer@.service" /etc/systemd/system/
  systemctl daemon-reload
  systemctl enable --now "rtlft8skimmer@$U"
  echo "Service enabled for user $U: systemctl status rtlft8skimmer@$U"
fi
echo "Run: rtlft8skimmer   then open http://localhost:8073/"
