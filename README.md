# ANOX

**English** · [Українська](README.uk.md)

A console client–server chat in C# (.NET 10). Users register with a name, send each other chat requests and talk in private one-to-one sessions.

## Projects

| Project | Purpose |
|---|---|
| `ANOX.Server` | TCP server: registration, sessions, chat requests, server commands |
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

### Client

```bash
dotnet run --project ANOX.Client -- 192.168.0.10:5656
```

The client takes the server address from the first source available:
1. Arguments: `AnoxClient <host>[:port]` or `AnoxClient <host> <port>`
2. `client.json` next to the executable
3. A prompt at startup. The address you enter is saved to `client.json`.

## Server configuration (`server.json`)

| Setting | Default | Description |
|---|---|---|
| `Host` | `0.0.0.0` | IP address to listen on (`0.0.0.0` means all interfaces) |
| `Port` | `5656` | Port |
| `AdminPasswordHash` | *(empty)* | Password hash for `/login`; empty disables admin access |
| `MaxLoginAttempts` | `5` | Wrong passwords allowed before a lockout |
| `LoginLockoutSeconds` | `60` | Lockout duration (counted per IP address) |
| `InviteTimeoutSeconds` | `60` | How long a chat request stays valid |
| `InviteSameUserCooldownSeconds` | `15` | Pause before asking the same user again |
| `InviteRateWindowSeconds` | `60` | Time window for the request limit |
| `MaxInvitesPerWindow` | `5` | Maximum chat requests per window |
| `MaxMessageLength` | `2000` | Maximum message length in characters |
| `RegisterTimeoutSeconds` | `10` | A new connection that doesn't register in time is closed |
| `MaxConnections` | `500` | Open connections allowed in total |
| `MaxConnectionsPerIp` | `5` | Open connections allowed from one IP (IPv6: per /64) |
| `SendTimeoutSeconds` | `10` | A client that stops reading for this long is disconnected |
| `MaxMessagesPerSecond` | `5` | Average messages per second per connection; extra ones are dropped |
| `MessageBurst` | `20` | Messages allowed in a short burst above that rate |
| `MaxProtocolViolations` | `10` | Malformed messages tolerated before the connection is closed |
| `LogFilePath` | `logs/server.log` | Log file; empty means console only |

### Admin password

```bash
dotnet run --project ANOX.Server -- --hash-password
```

You'll be asked for the password twice (input is hidden), and the command prints its hash (PBKDF2-SHA256). Paste the hash into `AdminPasswordHash` and restart the server. The password itself is never stored.

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

Each message is a single line ending with `\n`:

```
<MessageType>|<sender>|<receiver>|<base64(UTF-8 text)>
```

- Usernames: 1–20 characters; only letters, digits, `_`, `-` and `.`. `SERVER` and `ALL` are reserved.
- `Register`: the username goes in the sender field and the client's protocol version in the text. If the versions don't match, the server replies with `ProtocolMismatch` and closes the connection.
- `ServerError`: the text is `<MessageErrorType code>|<description>`.
- The current protocol version is `ProtocolInfo.Version` in `ANOX.Core`. Bump it on every incompatible change. The application version is set in `Directory.Build.props`.

> ⚠️ Traffic is not encrypted. Running over the internet needs TLS (planned).
