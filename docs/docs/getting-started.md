# Getting Started
## Server storage and upgrades

The server stores accounts and live matches in SQLite. Completed matches move to a
separate archive in the same transaction that saves the final live state. Members
can still list and open their completed matches after reconnecting or restarting
the server. Archived worlds are loaded on demand and are excluded from routine
live-state snapshots.

Schema version 1 databases upgrade automatically to version 2 without removing
accounts, sessions, or game results. A pre-timer server snapshot also upgrades on
startup. Those snapshots did not retain the chosen timer mode, so existing active
matches receive a daily timer with a full 24 hours from the upgrade. The server
saves that deadline immediately; subsequent restarts do not reset it. Games that
already have timers retain their saved mode and deadline.

Remote connections require TLS. Set `OPENPOLYTOPIA_TLS_CERTIFICATE` to a PKCS#12
certificate file and `OPENPOLYTOPIA_TLS_PASSWORD` when it needs a password. Only a
server bound to a loopback address can start without a certificate.
