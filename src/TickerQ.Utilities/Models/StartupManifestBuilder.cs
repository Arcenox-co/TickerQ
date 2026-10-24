using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Builds the startup manifest fingerprint: a hash of everything startup reconciliation applies or
    /// that decides whether two hosts may process the same jobs. Two hosts produce the same fingerprint
    /// exactly when they register the same functions with the same request and result contracts, declare
    /// the same seeds, and run the same TickerQ version. Every value is length-framed, and every
    /// collection is written in a fixed order, so the result does not depend on declaration order,
    /// culture, or machine.
    /// </summary>
    internal sealed class StartupManifestBuilder
    {
        internal const string Prefix = "m1:";
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal StartupManifestBuilder Section(string name) => Text("section").Text(name);

        internal StartupManifestBuilder Text(string value)
        {
            if (value == null) return Tag(0);
            Tag(1);
            var bytes = Encoding.UTF8.GetBytes(value);
            Length(bytes.Length);
            _hash.AppendData(bytes);
            return this;
        }

        internal StartupManifestBuilder Number(long? value)
        {
            if (value == null) return Tag(0);
            Tag(2);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value.Value);
            _hash.AppendData(buffer);
            return this;
        }

        internal StartupManifestBuilder Flag(bool value) => Tag(value ? (byte)3 : (byte)4);

        internal StartupManifestBuilder Bytes(byte[] value)
        {
            if (value == null) return Tag(0);
            Tag(5);
            Length(value.Length);
            _hash.AppendData(value);
            return this;
        }

        internal StartupManifestBuilder Numbers(IReadOnlyList<int> values)
        {
            if (values == null) return Tag(0);
            Tag(6);
            Length(values.Count);
            foreach (var value in values) Number(value);
            return this;
        }

        /// <summary>The fingerprint, for example <c>m1:3f2a…</c> (35 characters).</summary>
        internal string Build()
            => Prefix + Convert.ToHexString(_hash.GetHashAndReset().AsSpan(0, 16)).ToLowerInvariant();

        /// <summary>The TickerQ package version without build metadata, so local rebuilds of one version agree.</summary>
        internal static string TickerQVersion()
        {
            var assembly = typeof(StartupManifestBuilder).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString() ?? string.Empty;
            var metadata = version.IndexOf('+');
            return metadata < 0 ? version : version[..metadata];
        }

        /// <summary>
        /// Every locally registered function with its request and result contracts. Remote SDK functions
        /// (<c>name@node</c>) come and go at runtime and are excluded.
        /// </summary>
        internal StartupManifestBuilder Functions()
        {
            Section("functions");
            var descriptors = TickerFunctionProvider.Snapshot.Descriptors;
            foreach (var name in TickerFunctionProvider.TickerFunctions.Keys
                         .Where(name => !name.Contains('@'))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                Text(name);
                if (descriptors.TryGetValue(name, out var descriptor))
                {
                    Number(descriptor.ContractVersion);
                    Text(descriptor.Request?.Fingerprint).Text(descriptor.Request?.MediaType)
                        .Flag(descriptor.Request?.Required ?? false);
                    Text(descriptor.Result?.Fingerprint).Text(descriptor.Result?.MediaType);
                }
                else
                {
                    Tag(0);
                }
            }
            return this;
        }

        internal StartupManifestBuilder Cron(string key, CronTickerEntity cron)
        {
            Text(key).Text(cron.Function).Text(cron.Expression).Text(cron.Description).Flag(cron.IsEnabled)
                .Text(cron.TimeZoneId).Text(SchedulePolicyNames.Normalize(cron.SchedulePolicyName))
                .Number((long?)cron.BlockedOccurrenceBehavior).Number((long)cron.MissedExecutionAction)
                .Number(cron.MaximumLateness?.Ticks).Number((long)cron.CatchUpMode).Flag(cron.SkipIfAlreadyRunning)
                .Number((long)cron.OnStale).Bytes(cron.Request).Number(cron.Retries).Numbers(cron.RetryIntervals)
                .Number(cron.TimeoutSeconds).Number(cron.RequestContractVersion).Text(cron.RequestContractFingerprint)
                .Text(CronChainMaterializer.Serialize(cron.Chain));
            return this;
        }

        internal StartupManifestBuilder Policy(SchedulePolicy policy)
        {
            // Identity and bookkeeping fields are assigned by the store, not declared by code.
            var canonical = policy.Copy();
            canonical.Id = Guid.Empty;
            canonical.ApplicationNamespaceKey = string.Empty;
            canonical.NameKey = string.Empty;
            canonical.CreatedAtUtc = default;
            canonical.UpdatedAtUtc = default;
            canonical.Version = 0;
            return Text(SchedulePolicySerializer.Serialize(canonical));
        }

        private StartupManifestBuilder Tag(byte tag)
        {
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = tag;
            _hash.AppendData(buffer);
            return this;
        }

        private void Length(int length)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, length);
            _hash.AppendData(buffer);
        }
    }
}
