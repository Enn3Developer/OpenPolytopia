# Dedicated server

Run locally with `dotnet run --project OpenPolytopia.Server -- 6969 127.0.0.1`.
The server saves accounts, sessions, lobbies and games to `openpolytopia.db` in the working directory.
Set `OPENPOLYTOPIA_DATABASE` to an absolute path on a persistent volume for deployment.
Run one server process per database. Preserve the database when updating or restarting the server.

Remote connections require TLS. Set `OPENPOLYTOPIA_TLS_CERTIFICATE` to a PFX certificate with its private key
and `OPENPOLYTOPIA_TLS_PASSWORD` to its password, then bind to the desired interface through
`OPENPOLYTOPIA_BIND_ADDRESS` (or the second command-line argument). The certificate must be trusted by
clients and match the hostname they use. Only loopback bindings permit plaintext development connections;
clients enable TLS automatically for other hosts and never fall back to plaintext after TLS failure.

The SQLite store uses WAL and transactional writes. Back up using SQLite's backup API, or stop the server
before copying the database; copying only the main file during live writes can miss the WAL.
Schema and game snapshot versions reject unsupported formats. Gameplay content must remain compatible
with existing snapshots; changes to packed tiles, troops or technology definitions need a migration.
Completed games remain available to their members.

## Accounts and connections

After the protocol handshake, send `RegisterAccountPacket`, `LoginPacket` or `ResumeSessionPacket`.
Usernames are unique without regard to case and accept 3–32 ASCII letters, digits or underscores.
Passwords accept 12–128 characters. Passwords are salted and derived with PBKDF2-HMAC-SHA256;
only hashes of opaque session tokens are stored. Sessions expire after 30 days.
The server bounds authentication attempts across connections to limit expensive password work.

`AuthenticationPacket` supplies the stable account id and a session token. The handshake id is not an
account identity. The client stores the token per server address; it never stores the password.
`LogoutPacket` revokes that session. Logging in from another connection detaches the old connection.
`SetNamePacket` changes the authenticated account's display name; it cannot create or authenticate accounts.
The protocol version is changed so older clients fail the handshake cleanly.

An account can belong to multiple lobbies and games. Lobby operations identify the target lobby explicitly.
Disconnecting preserves lobby membership and readiness. Fully ready lobbies may start while members are offline.

`GetMyGamesPacket` lists retained game ids. `JoinGamePacket` opens an existing seat, subscribes to its updates
and returns a full `GameStatePacket`. It never creates a seat in a game the account does not own.
`LeaveGamePacket` closes that view without resigning; disconnecting closes all views with the same effect.
Gameplay requests require an open view and remain subject to the engine's ownership and turn checks.
`ResignGamePacket` is the explicit permanent resignation operation and does not require an open view.

Successful state-changing responses are queued only after SQLite commits. Lobby ids, membership, full game
state and results survive a restart. Transports are deliberately absent from snapshots: reconnecting clients
must authenticate and open their games again.
