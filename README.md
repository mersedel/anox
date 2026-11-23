# ANOX

**English** · [Українська](README.uk.md)

A console client–server chat in C# (.NET 10). Users register with a name, send each other chat requests and talk in private one-to-one sessions.

## Projects

| Project | Purpose |
|---|---|
| `ANOX.Server` | TCP server with TLS: registration, sessions, chat requests, server commands |
| `ANOX.Client` | Console client |
| `ANOX.Core` | Shared protocol: message types, serialization, validation |
| `ANOX.Logger` | Console and file logging |

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build anox.slnx
```

### Server

```bash
dotnet run --project ANOX.Server
```

On first start, a `server.json` with default settings is created next to the executable. Use `--config <path>` to load a different file.

The server only accepts encrypted (TLS) connections and doesn't start without a certificate. On first start it creates `server.json` and stops with a message asking for `TlsCertificatePath` and `TlsKeyPath`. For local development, see [Local TLS certificate](#local-tls-certificate).

### Client

```bash
dotnet run --project ANOX.Client
```

By default the client connects to `anox.ddns.net:5656`. To use another server, for example a local one for testing, pass its address as an argument: `AnoxClient <host>[:port]` or `AnoxClient <host> <port>`.

```bash
dotnet run --project ANOX.Client -- localhost
```

The client checks the server's certificate the standard way: it must chain up to a trusted authority and be issued for the name you connected to. If the check fails, or the server doesn't answer the TLS handshake within 10 seconds, the client says which of the two happened and doesn't connect.

### Local TLS certificate

For a local server, use the .NET development certificate. It is issued for `localhost`, `127.0.0.1` and `::1`. First, trust it (once per machine; Windows asks you to confirm):

```bash
dotnet dev-certs https --trust
```

Then export it as PEM, outside the repository:

```powershell
dotnet dev-certs https --export-path "$env:USERPROFILE\.anox-dev\localhost.pem" --format PEM --no-password
```

This writes `localhost.pem` and `localhost.key`. The key must never be committed; `.gitignore` also ignores `*.pem` and `*.key` as a safety net. Add both paths to the local `server.json` (with `dotnet run` it is `ANOX.Server/bin/Debug/net10.0/server.json`). JSON needs forward slashes or doubled backslashes:

```json
"TlsCertificatePath": "C:/Users/<you>/.anox-dev/localhost.pem",
"TlsKeyPath": "C:/Users/<you>/.anox-dev/localhost.key"
```

Connect the client to `localhost`. Any other name, such as the computer's name, fails the certificate check.

## Server configuration (`server.json`)

| Setting | Default | Description |
|---|---|---|
| `Host` | `0.0.0.0` | IP address to listen on (`0.0.0.0` means all interfaces) |
| `Port` | `5656` | Port |
| `TlsCertificatePath` | *(empty)* | **Required.** PEM certificate chain: the server's certificate first, then the intermediates (e.g. `fullchain.pem`) |
| `TlsKeyPath` | *(empty)* | **Required.** PEM private key of that certificate (e.g. `privkey.pem`) |
| `AdminPasswordHash` | *(empty)* | Password hash for `/login`; empty disables admin access |
| `MaxLoginAttempts` | `5` | Wrong passwords allowed before a lockout |
| `LoginLockoutSeconds` | `60` | Lockout duration (counted per IP address) |
| `InviteTimeoutSeconds` | `60` | How long a chat request stays valid |
| `InviteSameUserCooldownSeconds` | `15` | Pause before asking the same user again |
| `InviteRateWindowSeconds` | `60` | Time window for the request limit |
| `MaxInvitesPerWindow` | `5` | Maximum chat requests per window |
| `MaxMessageLength` | `2000` | Maximum message length in characters |
| `RegisterTimeoutSeconds` | `10` | A new connection that doesn't finish the TLS handshake and register in time is closed |
| `MaxConnections` | `500` | Open connections allowed in total |
| `MaxConnectionsPerIp` | `5` | Open connections allowed from one IP (IPv6: per /64) |
| `SendTimeoutSeconds` | `10` | A client that stops reading for this long is disconnected |
| `MaxMessagesPerSecond` | `5` | Average messages per second per connection; extra ones are dropped |
| `MessageBurst` | `20` | Messages allowed in a short burst above that rate |
| `MaxProtocolViolations` | `10` | Malformed messages tolerated before the connection is closed |
| `LogFilePath` | `logs/server.log` | Log file; empty means console only |

Relative paths (TLS files, log file) are resolved from the executable's folder. The certificate is read only at startup, so restart the server after renewing it.

### Admin password

```bash
dotnet run --project ANOX.Server -- --hash-password
```

You'll be asked for the password twice (input is hidden), and the command prints its hash (PBKDF2-SHA256). Paste the hash into `AdminPasswordHash` and restart the server. The password itself is never stored.

## Deploy

The public server runs on an AWS instance (Ubuntu, ARM64) available as `anox.ddns.net`. It is deployed from a developer machine with `deploy.ps1`.

### How the server is set up

| What | Where |
|---|---|
| Program | `/opt/anox/`: only the build output; replaced on every deploy |
| Config | `/etc/anox/server.json`: kept apart from the program, so a deploy never overwrites it |
| TLS certificate | `/etc/anox/tls/fullchain.pem` and `privkey.pem`: Let's Encrypt certificate for `anox.ddns.net` |
| Service | `/etc/systemd/system/anox.service` |
| Logs | systemd journal (`LogFilePath` is empty, so the server writes to the console and journald collects it) |

- The server runs as a separate system user `anox`. It can't log in (`nologin`), and everything in `/opt/anox` and `/etc/anox` belongs to `root`: the service can read its program and config but can't change them. The one exception is `/etc/anox/tls`: the certificate files belong to `anox` with mode `600`, so only the service (and `root`) can read the private key.
- certbot renews the certificate. Its hook puts the new files into `/etc/anox/tls` and restarts the service, because the server reads the certificate only at startup.
- The unit adds sandboxing on top: `ProtectSystem=strict` (the whole file system is read-only for the service), `ProtectHome`, `PrivateTmp`, `NoNewPrivileges`. Because of `ProtectSystem=strict` there is nowhere to write a log file, which is why file logging is turned off.
- systemd starts the server at boot and restarts it 5 seconds after a crash (`Restart=on-failure`).
- The build is framework-dependent (`--self-contained false`), so the .NET 10 runtime is installed on the server itself. When the project moves to a new .NET version, update the runtime there first.

### SSH access

`deploy.ps1` and the commands below use the SSH host alias `anox`. Add it to `~/.ssh/config`:

```
Host anox
    HostName anox.ddns.net
    User ubuntu
    IdentityFile ~/.ssh/<key>.pem
```

`ubuntu` is the instance's admin account (with `sudo`); it is the one you log in as. The `anox` user only runs the service.

### Deploying

```powershell
.\deploy.ps1
```

The script:

1. builds `ANOX.Server` for `linux-arm64` into a clean `publish\server`;
2. clears the upload folder `~/anox-upload` on the server;
3. uploads the build there with `scp`;
4. stops the service, replaces the contents of `/opt/anox` with the new build (files that are no longer in the build are removed) and starts it again;
5. waits 3 seconds and checks that the service is active; if it isn't, the script fails and prints the command to view the logs.

The config is not uploaded. To change settings, edit `/etc/anox/server.json` on the server and restart the service. While the service restarts (a few seconds) connected clients are dropped; they can come back with `/reconnect`.

### Useful commands

```bash
ssh anox systemctl status anox
ssh anox journalctl -u anox -n 50
ssh anox journalctl -u anox -f
ssh -t anox sudoedit /etc/anox/server.json
ssh anox sudo systemctl restart anox
```

Status, the last 50 log lines, live logs, editing the config, restart.

## Client commands

| Command | Description |
|---|---|
| `/help` | List commands |
| `/connect <username>` | Send a chat request |
| `/accept [username]` | Accept a request (the name is needed only when there are several) |
| `/decline [username]` | Decline a request |
| `/leave` | Leave the current chat |
| `/ping` | Latency to the server |
| `/version` | Client, server and protocol versions |
| `/clear` | Clear the console |
| `/reconnect` | Reconnect to the server |
| `/exit` | Quit |
| `//text` | Send a message that starts with `/` |

Server commands: `/getServerStatus`, `/getServerInfo`, `/getServerTime`. Admins (after `/login <password>`) also get `/getAllUsersNames` and `/getAllUsersAmount`. The client forwards any command it doesn't know to the server.

## Protocol

The connection is TCP wrapped in TLS. Inside it, each message is a single line ending with `\n`:

```
<MessageType>|<sender>|<receiver>|<base64(UTF-8 text)>
```

- Usernames: 1–20 characters; only letters, digits, `_`, `-` and `.`. `SERVER` and `ALL` are reserved.
- `Register`: the username goes in the sender field and the client's protocol version in the text. If the versions don't match, the server replies with `ProtocolMismatch` and closes the connection.
- `ServerError`: the text is `<MessageErrorType code>|<description>`.
- The current protocol version is `ProtocolInfo.Version` in `ANOX.Core`. Bump it on every incompatible change. The application version is set in `Directory.Build.props`.
- Connections over the limits (`MaxConnections`, `MaxConnectionsPerIp`) are closed right away, without a message: sending one over TLS would need a full handshake, which is exactly the cost to avoid during a flood of connections.
