using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Immutable, transport-neutral envelope for a ticker function's published result.
    /// <para>
    /// Carries the serialized <see cref="Payload"/> bytes plus explicit self-describing
    /// metadata: a positive <see cref="Version"/>, a non-blank <see cref="MediaType"/>, and
    /// optional contract identity (<see cref="ContractId"/>) and type name
    /// (<see cref="ContractType"/>) recorded as strings. It deliberately contains NO CLR
    /// <see cref="Type"/>, delegates, or service providers so it stays safe to persist and
    /// ship across any transport. The stored bytes are copied on the way in and out, so a
    /// caller can never mutate what has been published, and unknown envelope versions
    /// <see cref="EnsureSupportedVersion">fail closed</see> rather than being silently trusted.
    /// </para>
    /// </summary>
    public sealed class TickerResultEnvelope
    {
        /// <summary>The envelope schema version this runtime writes and knows how to read.</summary>
        public const int CurrentVersion = 1;

        private readonly byte[] _payload;

        public TickerResultEnvelope(
            byte[] payload,
            int version,
            string mediaType,
            string contractId = null,
            string contractType = null)
        {
            if (payload is null)
                throw new ArgumentNullException(nameof(payload));
            if (version <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(version), version, "Result envelope version must be positive.");
            if (string.IsNullOrWhiteSpace(mediaType))
                throw new ArgumentException("Result envelope media type must be non-blank.", nameof(mediaType));
            if (contractId != null && string.IsNullOrWhiteSpace(contractId))
                throw new ArgumentException("Result envelope contract id must be non-blank when provided.", nameof(contractId));
            if (contractType != null && string.IsNullOrWhiteSpace(contractType))
                throw new ArgumentException("Result envelope contract type must be non-blank when provided.", nameof(contractType));

            // Defensive copy: the published bytes are owned by the envelope and cannot be
            // mutated through the caller's original array reference after construction.
            _payload = (byte[])payload.Clone();
            Version = version;
            MediaType = mediaType;
            ContractId = contractId;
            ContractType = contractType;
        }

        /// <summary>Positive schema version of this envelope.</summary>
        public int Version { get; }

        /// <summary>Non-blank media type describing the payload encoding (e.g. <c>application/json</c>).</summary>
        public string MediaType { get; }

        /// <summary>Optional contract identity (e.g. a schema fingerprint). Null when not supplied.</summary>
        public string ContractId { get; }

        /// <summary>Optional payload type name recorded as a string. Never a CLR <see cref="Type"/>.</summary>
        public string ContractType { get; }

        /// <summary>Length of the stored payload in bytes.</summary>
        public int PayloadLength => _payload.Length;

        /// <summary>Read-only view over the stored payload; cannot be used to mutate it.</summary>
        public ReadOnlyMemory<byte> Payload => _payload;

        /// <summary>Returns an independent copy of the stored payload each call.</summary>
        public byte[] ToPayloadArray() => (byte[])_payload.Clone();

        /// <summary>Whether this envelope's <see cref="Version"/> is understood by this runtime.</summary>
        public bool IsSupportedVersion => Version == CurrentVersion;

        /// <summary>
        /// Throws <see cref="NotSupportedException"/> when the envelope version is not understood
        /// by this runtime. Callers must invoke this before trusting the payload so that a
        /// forward-versioned result fails closed instead of being misread.
        /// </summary>
        public void EnsureSupportedVersion()
        {
            if (Version != CurrentVersion)
                throw new NotSupportedException(
                    $"Unsupported ticker result envelope version {Version}; " +
                    $"this runtime supports version {CurrentVersion}.");
        }
    }
}
