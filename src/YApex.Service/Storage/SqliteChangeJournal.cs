// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using YApex.Core.Abstractions;
using YApex.Core.Domain;

namespace YApex.Service.Storage;

public sealed class SqliteChangeJournal : IChangeJournal
{
    private const int MaximumEntryBytes = 4 * 1024 * 1024;
    private const int MaximumUnfinishedEntries = 64;
    private const int MaximumRecoveryBytes = 16 * 1024 * 1024;
    private const int MaximumRecentEntries = 32;
    private const int MaximumRetainedEntries = 10_000;
    private const int MaximumRecentPayloadBytes = 16 * 1024 * 1024;
    private static readonly byte[] ProtectedPayloadMarker = [0x59, 0x41, 0x50, 0x01];
    private readonly string _connectionString;
    private readonly bool _protectPayloads;

    public SqliteChangeJournal()
        : this(protectPayloads: true)
    {
    }

    public SqliteChangeJournal(bool protectPayloads)
    {
        ServiceDataPaths.EnsurePrivateDirectory();
        _protectPayloads = protectPayloads;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ServiceDataPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    public async Task AppendAsync(ChangeJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.OperationId == Guid.Empty || !Enum.IsDefined(entry.State) ||
            entry.Before is null || entry.After is null || string.IsNullOrWhiteSpace(entry.Reason) || entry.Reason.Length > 256 ||
            entry.ProgramPath is { Length: > 32_767 } || entry.Error is { Length: > 2048 })
        {
            throw new ArgumentException("The change journal entry contains invalid or excessive fields.", nameof(entry));
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(entry);
        if (json.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException("The journal entry exceeds the maximum supported size.");
        }

        var payload = Protect(json);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ChangeJournal (OperationId, TimestampUtc, State, Payload)
            VALUES ($operationId, $timestampUtc, $state, $payload);
            """;
        command.Parameters.AddWithValue("$operationId", entry.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$timestampUtc", entry.Timestamp.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$state", entry.State.ToString());
        command.Parameters.AddWithValue("$payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var trimCommand = connection.CreateCommand();
        trimCommand.CommandText = """
            DELETE FROM ChangeJournal
            WHERE Sequence < COALESCE((SELECT MAX(Sequence) FROM ChangeJournal), 0) - $retainedEntries
                AND OperationId NOT IN (
                    SELECT current.OperationId
                    FROM ChangeJournal AS current
                    INNER JOIN (
                        SELECT OperationId, MAX(Sequence) AS LatestSequence
                        FROM ChangeJournal
                        GROUP BY OperationId
                    ) AS latest ON current.Sequence = latest.LatestSequence
                    WHERE current.State IN ('Planned', 'Applied', 'RollingBack', 'RecoveryRequired')
                        AND current.Sequence > COALESCE(
                            (SELECT MAX(Sequence) FROM ChangeJournal WHERE State = 'EmergencyRemoval'),
                            0)
                );
            """;
        trimCommand.Parameters.AddWithValue("$retainedEntries", MaximumRetainedEntries);
        await trimCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChangeJournalEntry>> GetUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT latest_entry.OperationId, latest_entry.State, latest_entry.Payload
            FROM ChangeJournal AS latest_entry
            INNER JOIN (
                SELECT OperationId, MAX(Sequence) AS LatestSequence
                FROM ChangeJournal
                GROUP BY OperationId
            ) AS latest ON latest_entry.Sequence = latest.LatestSequence
            WHERE latest_entry.Sequence > COALESCE(
                    (SELECT MAX(Sequence) FROM ChangeJournal WHERE State = 'EmergencyRemoval'),
                    0)
                AND latest_entry.State IN ('Planned', 'Applied', 'RollingBack', 'RecoveryRequired')
            ORDER BY latest_entry.Sequence DESC
            LIMIT $maximumEntries;
            """;
        command.Parameters.AddWithValue("$maximumEntries", MaximumUnfinishedEntries + 1);

        var entries = new List<ChangeJournalEntry>();
        var totalPayloadBytes = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storedOperationId = reader.GetString(0);
            var storedStateText = reader.GetString(1);
            var payload = reader.GetFieldValue<byte[]>(2);
            totalPayloadBytes = checked(totalPayloadBytes + payload.Length);
            if (payload.Length > MaximumEntryBytes + 4096 ||
                totalPayloadBytes > MaximumRecoveryBytes ||
                entries.Count >= MaximumUnfinishedEntries)
            {
                throw new InvalidDataException("The unfinished journal exceeds the safe recovery limits.");
            }

            var entry = DeserializeEntry(payload, storedOperationId, storedStateText);
            if (entry.State is not (JournalState.Planned or JournalState.Applied or JournalState.RollingBack or JournalState.RecoveryRequired))
            {
                throw new InvalidDataException("The change journal contains an invalid unfinished entry.");
            }

            entries.Add(entry);
        }

        return entries;
    }

    public async Task<IReadOnlyList<ChangeJournalSummary>> GetRecentAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OperationId, State, Payload
            FROM ChangeJournal
            ORDER BY Sequence DESC
            LIMIT $maximumEntries;
            """;
        command.Parameters.AddWithValue("$maximumEntries", MaximumRecentEntries);

        var summaries = new List<ChangeJournalSummary>(MaximumRecentEntries);
        var totalPayloadBytes = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = reader.GetFieldValue<byte[]>(2);
            totalPayloadBytes = checked(totalPayloadBytes + payload.Length);
            if (payload.Length > MaximumEntryBytes + 4096 || totalPayloadBytes > MaximumRecentPayloadBytes)
            {
                throw new InvalidDataException("The recent change journal exceeds the safe read limits.");
            }

            var entry = DeserializeEntry(payload, reader.GetString(0), reader.GetString(1));
            summaries.Add(new ChangeJournalSummary(
                entry.OperationId,
                entry.Timestamp,
                entry.State,
                Bound(entry.Reason, 64),
                entry.ProgramPath is null ? null : Bound(entry.ProgramPath, 512)));
        }

        return summaries;
    }

    private ChangeJournalEntry DeserializeEntry(byte[] payload, string storedOperationId, string storedStateText)
    {
        var json = Unprotect(payload);
        var entry = JsonSerializer.Deserialize<ChangeJournalEntry>(json)
            ?? throw new InvalidDataException("The change journal contains an empty entry.");
        if (!Guid.TryParseExact(storedOperationId, "D", out var columnOperationId) ||
            columnOperationId != entry.OperationId ||
            !Enum.TryParse<JournalState>(storedStateText, ignoreCase: false, out var columnState) ||
            !Enum.IsDefined(columnState) || columnState != entry.State ||
            entry.OperationId == Guid.Empty || !Enum.IsDefined(entry.State) ||
            entry.Before is null || entry.After is null ||
            string.IsNullOrWhiteSpace(entry.Reason) || entry.Reason.Length > 256 ||
            entry.ProgramPath is { Length: > 32_767 } || entry.Error is { Length: > 2048 })
        {
            throw new InvalidDataException("The change journal contains an invalid entry.");
        }

        return entry;
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA journal_size_limit=10485760;
            PRAGMA max_page_count=32768;
            CREATE TABLE IF NOT EXISTS ChangeJournal (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationId TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                State TEXT NOT NULL,
                Payload BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ChangeJournal_TimestampUtc ON ChangeJournal(TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_ChangeJournal_OperationId_Sequence ON ChangeJournal(OperationId, Sequence DESC);
            CREATE INDEX IF NOT EXISTS IX_ChangeJournal_State_Sequence ON ChangeJournal(State, Sequence DESC);
            """;
        command.ExecuteNonQuery();
    }

    private byte[] Protect(byte[] value)
    {
        if (!_protectPayloads)
        {
            return value;
        }

        var protectedValue = ProtectedData.Protect(value, optionalEntropy: null, DataProtectionScope.LocalMachine);
        var result = new byte[ProtectedPayloadMarker.Length + protectedValue.Length];
        ProtectedPayloadMarker.CopyTo(result, 0);
        protectedValue.CopyTo(result, ProtectedPayloadMarker.Length);
        return result;
    }

    private static byte[] Unprotect(byte[] value)
    {
        if (value.AsSpan().StartsWith(ProtectedPayloadMarker))
        {
            return ProtectedData.Unprotect(
                value.AsSpan(ProtectedPayloadMarker.Length).ToArray(),
                optionalEntropy: null,
                DataProtectionScope.LocalMachine);
        }

        if (value.Length > 0 && value[0] == (byte)'{')
        {
            return value;
        }

        throw new InvalidDataException("The change journal payload is not recognized.");
    }
}
