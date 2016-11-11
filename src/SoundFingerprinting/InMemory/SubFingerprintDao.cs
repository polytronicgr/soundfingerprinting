﻿namespace SoundFingerprinting.InMemory
{
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    using SoundFingerprinting.DAO;
    using SoundFingerprinting.DAO.Data;
    using SoundFingerprinting.Data;
    using SoundFingerprinting.Infrastructure;
    using SoundFingerprinting.Math;

    internal class SubFingerprintDao : ISubFingerprintDao
    {
        private static long counter;

        private readonly IRAMStorage storage;
        private readonly IHashConverter hashConverter;

        public SubFingerprintDao() : this(DependencyResolver.Current.Get<IRAMStorage>(), DependencyResolver.Current.Get<IHashConverter>())
        {
        }

        public SubFingerprintDao(IRAMStorage storage, IHashConverter hashConverter)
        {
            this.storage = storage;
            this.hashConverter = hashConverter;
        }

        public SubFingerprintData ReadSubFingerprint(IModelReference subFingerprintReference)
        {
            if (storage.SubFingerprints.ContainsKey(subFingerprintReference))
            {
                return storage.SubFingerprints[subFingerprintReference];
            }

            return null;
        }

        public IModelReference InsertSubFingerprint(long[] hashes, int sequenceNumber, double sequenceAt, IModelReference trackReference)
        {
            var subFingerprintReference = new ModelReference<long>(Interlocked.Increment(ref counter));
            storage.SubFingerprints[subFingerprintReference] = new SubFingerprintData(hashes, sequenceNumber, sequenceAt, subFingerprintReference, trackReference);
            if (!storage.TracksHashes.ContainsKey(trackReference))
            {
                storage.TracksHashes[trackReference] = new ConcurrentDictionary<IModelReference, HashedFingerprint>();
            }

            byte[] bytes = hashConverter.ToBytes(hashes, hashes.Length * 4);
            storage.TracksHashes[trackReference][subFingerprintReference] = new HashedFingerprint(bytes, hashes, sequenceNumber, sequenceAt);
            this.InsertHashes(hashes, subFingerprintReference, trackReference);
            return subFingerprintReference;
        }

        public IList<HashedFingerprint> ReadHashedFingerprintsByTrackReference(IModelReference trackReference)
        {
            if (storage.TracksHashes.ContainsKey(trackReference))
            {
                return storage.TracksHashes[trackReference].Values.ToList();
            }

            return Enumerable.Empty<HashedFingerprint>().ToList();
        }

        public IEnumerable<SubFingerprintData> ReadSubFingerprints(long[] hashes, int thresholdVotes)
        {
            int table = 0;
            var hashTables = storage.HashTables;
            var subFingeprintCount = new Dictionary<IModelReference, int>();
            foreach (var hashBin in hashes)
            {
                if (hashTables[table].ContainsKey(hashBin))
                {
                    foreach (var subFingerprintId in hashTables[table][hashBin])
                    {
                        if (!subFingeprintCount.ContainsKey(subFingerprintId))
                        {
                            subFingeprintCount[subFingerprintId] = 0;
                        }

                        subFingeprintCount[subFingerprintId]++;
                    }
                }

                table++;
            }

            return subFingeprintCount.Where(pair => pair.Value >= thresholdVotes)
                                     .Select(pair => storage.SubFingerprints[pair.Key]);
        }

        public IEnumerable<SubFingerprintData> ReadSubFingerprints(long[] hashes, int thresholdVotes, string trackGroupId)
        {
            var trackReferences = storage.Tracks.Where(pair => pair.Value.GroupId == trackGroupId)
                                         .Select(pair => pair.Value.TrackReference).ToList();

            if (trackReferences.Any())
            {
                return ReadSubFingerprints(hashes, thresholdVotes)
                                .Where(subFingerprint => trackReferences.Contains(subFingerprint.TrackReference));
            }

            return Enumerable.Empty<SubFingerprintData>();
        }

        public ISet<SubFingerprintData> ReadSubFingerprints(IEnumerable<long[]> hashes, int threshold)
        {
            var allCandidates = new HashSet<SubFingerprintData>();
            foreach (var hashedFingerprint in hashes)
            {
                var subFingerprints = this.ReadSubFingerprints(hashedFingerprint, threshold);
                foreach (var subFingerprint in subFingerprints)
                {
                    allCandidates.Add(subFingerprint);
                }
            }

            return allCandidates;
        }

        private void InsertHashes(long[] hashBins, IModelReference subFingerprintReference, IModelReference trackReference)
        {
            int table = 0;
            lock (((ICollection)storage.HashTables).SyncRoot)
            {
                foreach (var hashTable in storage.HashTables)
                {
                    if (!hashTable.ContainsKey(hashBins[table]))
                    {
                        hashTable[hashBins[table]] = new List<IModelReference>();
                    }

                    hashTable[hashBins[table]].Add(subFingerprintReference);
                    table++;
                }

                storage.TracksHashes[trackReference][subFingerprintReference].HashBins = hashBins;
            }
        }
    }
}
