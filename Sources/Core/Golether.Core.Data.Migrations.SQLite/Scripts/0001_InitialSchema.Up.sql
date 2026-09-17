-- Golether initial schema.
-- Timestamps are Unix milliseconds (UTC). Secret columns hold data protected by ISecretProtector.

-- Known devices of other participants.
CREATE TABLE Contacts (
    PeerId        TEXT    NOT NULL PRIMARY KEY CHECK (length(PeerId) = 64),
    DisplayName   TEXT    NOT NULL,
    IsTrusted     INTEGER NOT NULL DEFAULT 0 CHECK (IsTrusted IN (0, 1)),
    LastEndpoint  TEXT    NULL,
    Notes         TEXT    NULL,
    FirstSeenAt   INTEGER NOT NULL,
    LastSeenAt    INTEGER NOT NULL
);

CREATE INDEX IX_Contacts_LastSeenAt ON Contacts (LastSeenAt DESC);

-- The AmneziaWG interface of this device when it hosts tunnels (keys and obfuscation, protected).
CREATE TABLE HostTunnelInterfaces (
    InterfaceName TEXT    NOT NULL PRIMARY KEY,
    SecretData    BLOB    NOT NULL,
    CreatedAt     INTEGER NOT NULL
);

-- Tunnel offers and established tunnels.
-- Role: 0 = this device hosts the tunnel, 1 = this device joined the tunnel of another host.
-- Status: 0 = offer sent, waiting for the answer; 1 = ready; 2 = revoked.
CREATE TABLE Tunnels (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OfferId        TEXT    NOT NULL,
    Role           INTEGER NOT NULL CHECK (Role IN (0, 1)),
    Status         INTEGER NOT NULL CHECK (Status IN (0, 1, 2)),
    InterfaceName  TEXT    NOT NULL,
    TunnelAddress  TEXT    NOT NULL,
    PeerId         TEXT    NULL CHECK (PeerId IS NULL OR length(PeerId) = 64),
    PeerName       TEXT    NULL,
    SecretData     BLOB    NULL,
    CreatedAt      INTEGER NOT NULL,
    UpdatedAt      INTEGER NOT NULL,
    ExpiresAt      INTEGER NULL
);

CREATE UNIQUE INDEX UX_Tunnels_Role_OfferId ON Tunnels (Role, OfferId);
CREATE INDEX IX_Tunnels_PeerId ON Tunnels (PeerId);

-- Application settings.
CREATE TABLE Settings (
    Key    TEXT NOT NULL PRIMARY KEY,
    Value  TEXT NOT NULL
);
