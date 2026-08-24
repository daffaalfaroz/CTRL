namespace CTRL.Desktop.Session;

/// <summary>
/// Stores per-device tokens for §12 reconnect authentication. Implementations
/// MUST NOT persist raw tokens (protocol.md: "Server menyimpan token ter-hash,
/// bukan plaintext"). Values handed to and returned from this store are
/// SHA-256 hashes of the actual token bytes.
/// </summary>
public interface ITokenStore
{
    bool TryGetHash(string deviceId, out byte[] tokenHash);
    void StoreHash(string deviceId, byte[] tokenHash);
    void Remove(string deviceId);
}

/// <summary>In-memory token hash store (hash-at-rest). Production deployments
/// back this with durable storage; the hash-only rule still applies.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly Dictionary<string, byte[]> _hashes = new();

    public bool TryGetHash(string deviceId, out byte[] tokenHash)
    {
        lock (_hashes)
        {
            if (_hashes.TryGetValue(deviceId, out var stored))
            {
                tokenHash = stored;
                return true;
            }
        }
        tokenHash = Array.Empty<byte>();
        return false;
    }

    public void StoreHash(string deviceId, byte[] tokenHash)
    {
        lock (_hashes)
        {
            _hashes[deviceId] = tokenHash;
        }
    }

    public void Remove(string deviceId)
    {
        lock (_hashes)
        {
            _hashes.Remove(deviceId);
        }
    }
}
